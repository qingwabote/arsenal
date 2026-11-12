using Bag;
using Bastard;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arsenal
{
    /// <summary>
    /// The double-buffered compact stream. Ping-pong sides keep this frame's read (the last
    /// uploaded side) and the in-progress write from aliasing, so the rebuild never fights an
    /// in-place move. Sides are pre-allocated at full budget: TextureView growth goes through
    /// Texture.Reinitialize (fresh GPU storage, hitches a frame), so the stream must never
    /// resize in play.
    /// </summary>
    public sealed class TrailStream
    {
        // design budget: 1024 trails x 64 points. Deliberately not wired to
        // TrailStreamBuilder.Kmax — the two knobs are tuned independently, and overflow
        // degrades by the documented truncation policy instead of crashing.
        public const int TexelCapacity = 1024 * 64;

        public readonly TextureView[] Sides = { new(TexelCapacity * 4), new(TexelCapacity * 4) };
        public int ReadIndex = 1;
        public int WriteIndex = 0;
        public bool Dirty;

        public TextureView Read => Sides[ReadIndex];
        public TextureView Write => Sides[WriteIndex];

        /// <summary>Upload the written side and flip; the committed side becomes the read.</summary>
        public void Commit()
        {
            Write.Texture.Apply(false);
            ReadIndex = WriteIndex;
            WriteIndex ^= 1;
            Dirty = false;
        }

        /// <summary>
        /// Without this, every editor play session leaks both Persistent references plus both
        /// textures (NativeLeakDetection reports them at each domain reload).
        /// </summary>
        public void Dispose()
        {
            foreach (var side in Sides)
            {
                side.Dispose();
                Object.Destroy(side.Texture);
            }
        }
    }

    /// <summary>
    /// No proxy: the trail rides the particle entity; bag collects LocalToWorld, _TrailData
    /// and _BaseColor off the particle's own archetype, so the ribbon inherits the live
    /// particle color with zero trail-side plumbing.
    ///
    /// _TrailTex is bound on the block directly on purpose: batch is ref readonly, so calling
    /// bag's PropertyTextureBind would write a defensive copy and the texture would never
    /// reach the GPU (the instanced-invisibility root cause). The engine fills
    /// _TrailTex_TexelSize from the texture binding itself.
    ///
    /// A SystemBase because it owns the managed <see cref="TrailStream"/> as an instance
    /// field; the stream is created eagerly so the builder never sees null.
    /// </summary>
    [UpdateInGroup(typeof(BatchGroup))]
    [CreateAfter(typeof(BatchGroup))]
    public partial class TrailRenderSystem : SystemBase
    {
        static readonly MaterialPropertyBlock s_Block = new();
        static readonly int s_TrailTex = Shader.PropertyToID("_TrailTex");

        BatchQueue m_Queue;
        TrailStream m_Stream;

        /// <summary>This world's trail stream; never null after OnCreate.</summary>
        public TrailStream Stream => m_Stream;

        protected override void OnCreate()
        {
            m_Stream = new TrailStream();

            // CreateAfter: the group's OnCreate (context allocation) is guaranteed to have run,
            // so the queue cannot capture a null context
            m_Queue = World.GetExistingSystemManaged<BatchGroup>().CreateQueue();
        }

        protected override void OnDestroy()
        {
            m_Queue.Dispose();
            m_Stream.Dispose();
        }

        protected override void OnUpdate()
        {
            // commit before any early-out: a rewrite with no renderable chunk must still
            // upload and flip, or the next builder pass would overwrite unapplied data
            if (m_Stream.Dirty)
                m_Stream.Commit();

            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();
            EntityManager.CompleteDependencyBeforeRO<TrailData>();

            var chunks = SystemAPI.QueryBuilder()
                .WithAll<TrailRenderer, TrailData, LocalToWorld>()
                .Build()
                .ToArchetypeChunkArray(Allocator.Temp);
            if (chunks.Length == 0)
            {
                chunks.Dispose();
                return;
            }

            var TrailRenderer = SystemAPI.GetComponentTypeHandle<TrailRenderer>(true);
            var TrailData = SystemAPI.GetComponentTypeHandle<TrailData>(true);
            foreach (var chunk in chunks)
            {
                using var batcher = m_Queue.Auto(chunk);

                var renderer = chunk.GetNativeArray(ref TrailRenderer);
                var data = chunk.GetNativeArray(ref TrailData);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (data[i].Count < 2f)
                        continue; // fewer than 2 points: no polyline to unfold
                    batcher.Add(renderer[i].Material, renderer[i].Mesh, i);
                }
            }
            chunks.Dispose();

            var tex = m_Stream.Read.Texture;
            var batches = m_Queue.Dump();
            for (int index = 0; index < batches.Length; index++)
            {
                ref readonly var batch = ref batches.ElementAt(index);

                var material = (Material)batch.Material;
                var mesh = (Mesh)batch.Mesh;
                if (material == null || mesh == null)
                    continue;

                s_Block.Clear();
                s_Block.SetTexture(s_TrailTex, tex);
                batch.PropertyToBlock(s_Block);

                var parameters = new RenderParams(material)
                {
                    matProps = s_Block,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false
                };
                Graphics.RenderMeshInstanced(parameters, mesh, 0,
                    batch.LocalToWorlds.AsArray().Reinterpret<Matrix4x4>(), batch.Count);
            }
            batches.Dispose();
        }
    }
}
