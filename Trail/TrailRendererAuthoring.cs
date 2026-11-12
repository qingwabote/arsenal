using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Arsenal
{
    /// <summary>
    /// Named TrailParams (not Trail) because a type name equal to its assembly name trips the
    /// Entities source generator's query-name resolution (SGQC001: the generator resolves the
    /// bare name to the assembly symbol, not this struct).
    /// </summary>
    public struct TrailRenderer : IComponentData
    {
        public float Lifetime;
        public float MinVertexDistance;
        public float WidthOverTrail;
        public UnityObjectRef<Mesh> Mesh;
        public UnityObjectRef<Material> Material;
    }

    /// <summary>
    /// Builder-private history anchor: where this trail's segment sits in the current stream
    /// side and how many texels it spans (the baked tip included — it is an ordinary segment
    /// point the expiry walk consumes like any other).
    /// </summary>
    public struct TrailState : IComponentData
    {
        public float3 LastPosition;   // 里程计锚点;MaxValue.x = 尚未被 builder 目击(出生帧)
        public int PrevBase;          // 上一帧流内段首 texel(读地址)
        public int PrevCount;         // 上一帧段长(含切点)
    }

    /// <summary>
    /// bag collects this by raw blit into one Vector4 slot (size-registered, no field
    /// coupling), and C# value types are Sequential by default — declaration order IS the
    /// xyzw channel order. Base/Count deliberately mirror PrevBase/PrevCount: one writer
    /// (the builder) stamps both each frame, the payload just republishes them to the shader.
    /// </summary>
    [MaterialProperty("_TrailData")]
    public struct TrailData : IComponentData
    {
        public float Base;        // x = 本帧流内段首 texel
        public float Count;       // y = 段长(含切点)
        public float WidthScale;  // z = ribbon full width in metres
        public float Spare;       // w 预留
    }

    public class TrailRendererAuthoring : MonoBehaviour
    {
        [Tooltip("Seconds a recorded trail point stays alive (TrailRenderer.time semantics)")]
        public float Lifetime = 1f;

        [Tooltip("Minimum distance between recorded trail points (TrailRenderer.minVertexDistance)")]
        public float MinVertexDistance = 0.2f;

        [Tooltip("Ribbon width as a fraction of the particle size (TrailModule.widthOverTrail)")]
        public float WidthOverTrail = 0.45f;

        [Tooltip("True: the ribbon dies with its particle. False: the trail detaches at death and unwinds on its own (dieWithParticles)")]
        public bool DieWithParticles = true;

        [Tooltip("Static ribbon topology (editor-created via Partity/Create Trail Ribbon Assets)")]
        public Mesh Mesh;

        [Tooltip("Ribbon material (editor-created; the stream texture binds via MPB at submit time)")]
        public Material Material;

        class Baker : Baker<TrailRendererAuthoring>
        {
            public override void Bake(TrailRendererAuthoring authoring)
            {
                DependsOn(authoring.Mesh);
                DependsOn(authoring.Material);

                var entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new TrailRenderer
                {
                    Lifetime = authoring.Lifetime,
                    MinVertexDistance = authoring.MinVertexDistance,
                    WidthOverTrail = authoring.WidthOverTrail,
                    Mesh = authoring.Mesh,
                    Material = authoring.Material,
                });

                // dormant zeros: the builder stamps real values on first sight, so spawning
                // costs no structural change
                AddComponent(entity, new TrailState
                {
                    // sentinel: emitters spawn mid-Simulation after TransformSystemGroup, so
                    // the first sighting's LTW is still the prefab bake — the builder arms the
                    // anchor without recording, the first point lands next frame
                    LastPosition = new float3(float.MaxValue),
                });
                AddComponent(entity, new TrailData());
            }
        }
    }
}
