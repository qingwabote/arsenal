using Bastard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Arsenal
{
    /// <summary>
    /// Managed on purpose (no [BurstCompile]): the stream sides are managed TextureViews —
    /// reintroducing any "snapshot into a blittable singleton" hop re-creates the
    /// double-truth-source desync. The loop is per-trail microsecond scale.
    ///
    /// Every frame each trail's live run is rebuilt into one compact stream segment
    /// [tip?, oldest … newest]. A baked tip carries birth = bake-time − TTL, so on the next
    /// pass it is the just-expired side of the TTL boundary — the expiry walk consumes it and
    /// the rebake interpolates against it, which keeps the tip creeping at the exact TTL
    /// moment without ever needing the discarded older points.
    ///
    /// The dirty flag is global: one trail's append shifts every later segment's address, and
    /// a live tip creeps every frame (its walk-past counts as a drop), so either forces the
    /// full rewrite and upload. Frames whose stream would come out bit-identical skip both.
    ///
    /// Color is deliberately not recorded: the ribbon reads the particle's live BaseColor as
    /// an instanced property. Time is deliberately unscaled (TrailRenderer parity).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct TrailUpdateSystem : ISystem
    {
        private static readonly Profile.Handle s_Profile = Profile.DefineEntry("Trail");

        /// <summary>
        /// Per-trail texel budget (rendered points, tip included) so one long trail cannot
        /// eat the stream. Deliberately neither the mesh segment count (the only fidelity
        /// knob) nor the buffer size (TrailStream.TexelCapacity) — K is free to differ from
        /// both without touching shader or material.
        /// </summary>
        public const int Kmax = 128;

        public void OnUpdate(ref SystemState state)
        {
            using var scope = s_Profile.Auto();

            var stream = state.World.GetExistingSystemManaged<TrailRenderSystem>().Stream;
            var oldTexels = stream.Read.Source.Value;
            var newTexels = stream.Write.Source.Value;
            double now = SystemAPI.Time.ElapsedTime;
            int cursor = 0;
            bool dirty = false;

            foreach (var (parametersRef, stateRef, renderRef, transform) in
                SystemAPI.Query<RefRO<TrailRenderer>, RefRW<TrailState>, RefRW<TrailData>, RefRO<LocalToWorld>>())
            {
                var p = parametersRef.ValueRO;
                TrailState s = stateRef.ValueRW;
                float3 pos = transform.ValueRO.Position;

                bool fresh = s.LastPosition.x == float.MaxValue;
                // an empty chain records its first point unconditionally (minVertexDistance
                // spaces the points along the path — the odometer only governs successors)
                bool append = !fresh && (s.PrevCount == 0 ||
                    math.distancesq(pos, s.LastPosition) >= p.MinVertexDistance * p.MinVertexDistance);

                // births ascend toward the newest, so expiry is a FIFO prefix walk (the baked
                // tip sits just past TTL and walks off here every frame it exists)
                int tail = 0;
                while (tail < s.PrevCount && now - Birth(oldTexels, s.PrevBase + tail) > p.Lifetime)
                    tail++;
                int survivors = s.PrevCount - tail;

                // tip needs the TTL boundary intact and a live point on its young side: a
                // truncation below turns the boundary into a hard cut instead
                bool tip = tail > 0 && tail < s.PrevCount;

                // one policy for both caps: truncate oldest-first, kill the tip, keep the
                // append while any room exists (the stream budget leg is the global
                // overflow valve — see TrailStream.TexelCapacity)
                int budget = math.min(Kmax, TrailStream.TexelCapacity - cursor);
                int liveCount = survivors + (append ? 1 : 0);
                if (liveCount + (tip ? 1 : 0) > budget)
                {
                    tip = false;
                    survivors = math.min(survivors, math.max(budget - (append ? 1 : 0), 0));
                    if (survivors + (append ? 1 : 0) > budget)
                        append = false; // budget exhausted: even the new point defers a frame
                    liveCount = survivors + (append ? 1 : 0);
                }

                int segBase = cursor;
                if (tip)
                {
                    float ageLive = (float)now - Birth(oldTexels, s.PrevBase + tail);
                    float ageDead = (float)now - Birth(oldTexels, s.PrevBase + tail - 1);
                    float3 oldestLive = Position(oldTexels, s.PrevBase + tail);
                    float3 newestDead = Position(oldTexels, s.PrevBase + tail - 1);
                    float cut = ageDead > ageLive
                        ? math.saturate((p.Lifetime - ageLive) / (ageDead - ageLive)) : 0f;
                    Write(newTexels, cursor, math.lerp(oldestLive, newestDead, cut),
                        (float)(now - p.Lifetime));
                    cursor++;
                }
                if (survivors > 0)
                {
                    NativeArray<float>.Copy(oldTexels, (s.PrevBase + tail) * 4,
                        newTexels, cursor * 4, survivors * 4);
                    cursor += survivors;
                }
                if (append)
                {
                    Write(newTexels, cursor, pos, (float)now);
                    cursor++;
                }

                s.PrevBase = segBase;
                s.PrevCount = liveCount + (tip ? 1 : 0);
                if (append || fresh)
                    s.LastPosition = pos; // point recorded, or spawn frame arming the odometer
                stateRef.ValueRW = s;
                renderRef.ValueRW = new TrailData
                {
                    Base = segBase,
                    Count = s.PrevCount,
                    WidthScale = p.WidthOverTrail * math.length(transform.ValueRO.Value.c0.xyz),
                    Spare = 0f,
                };

                dirty |= append || tail > 0;
            }

            stream.Dirty |= dirty;
        }

        static float Birth(NativeArray<float> t, int texel) => t[texel * 4 + 3];

        static float3 Position(NativeArray<float> t, int texel) =>
            new(t[texel * 4], t[texel * 4 + 1], t[texel * 4 + 2]);

        static void Write(NativeArray<float> t, int texel, float3 pos, float birth)
        {
            int b = texel * 4;
            t[b + 0] = pos.x;
            t[b + 1] = pos.y;
            t[b + 2] = pos.z;
            t[b + 3] = birth;
        }
    }
}
