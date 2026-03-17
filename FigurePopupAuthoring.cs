using Graphix;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Arsenal
{
    public struct FigurePopupState : IComponentData
    {
        public float4 Color;
        public float3 Position;
        public float Time;

        public float3 Movement;
    }

    [RequireComponent(typeof(FigureAuthoring))]
    public class FigurePopupAuthoring : MonoBehaviour
    {
        public float3 Movement;

        class FigurePopupBaker : Baker<FigurePopupAuthoring>
        {
            public override void Bake(FigurePopupAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new FigurePopupState()
                {
                    Movement = authoring.Movement
                });
            }
        }
    }

    [UpdateInGroup(typeof(TransformSystemGroup), OrderFirst = true)]
    public partial struct FigurePopup : ISystem
    {
        private const float k_Duration = 0.75f;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach (var (popup, colors, transform, entity) in SystemAPI.Query<RefRW<FigurePopupState>, DynamicBuffer<DigitColor>, RefRW<LocalTransform>>().WithEntityAccess())
            {
                var time = popup.ValueRO.Time;
                if (time == 0) // initialize
                {
                    popup.ValueRW.Color = colors[0].Value;
                    popup.ValueRW.Position = transform.ValueRO.Position;
                    popup.ValueRW.Time = SystemAPI.Time.DeltaTime;
                    continue;
                }

                if (time >= k_Duration)
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                var t = time / k_Duration;

                transform.ValueRW.Position = popup.ValueRO.Position + popup.ValueRO.Movement * t;
                var color = math.lerp(popup.ValueRO.Color, float4.zero, t);
                for (int i = 0; i < colors.Length; i++)
                {
                    colors.ElementAt(i).Value = color;
                }

                popup.ValueRW.Time = time + SystemAPI.Time.DeltaTime;
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
