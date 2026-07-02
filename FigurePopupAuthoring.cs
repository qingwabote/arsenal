using Unity.Entities;
using Unity.Mathematics;
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

#if UNITY_EDITOR
    [RequireComponent(typeof(FigureAuthoring))]
    public class FigurePopupAuthoring : MonoBehaviour
    {
        [Tooltip("In camera space")]
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
#endif

    [UpdateInGroup(typeof(TransformSystemGroup), OrderFirst = true)]
    [RequireMatchingQueriesForUpdate]
    public partial struct FigurePopupSystem : ISystem
    {
        private const float k_Duration = 0.75f;

        public void OnUpdate(ref SystemState state)
        {
            var cameraTransform = Camera.main.transform;

            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach (var (popup, colors, transform, entity) in SystemAPI.Query<RefRW<FigurePopupState>, DynamicBuffer<DigitColor>, RefRW<LocalTransform>>().WithEntityAccess())
            {
                var time = popup.ValueRO.Time;
                if (time == 0) // initialize
                {
                    popup.ValueRW.Color = colors[0].Value;
                    popup.ValueRW.Position = transform.ValueRO.Position;
                    popup.ValueRW.Time = SystemAPI.Time.DeltaTime;
                    transform.ValueRW.Rotation = cameraTransform.rotation;
                    continue;
                }

                if (time >= k_Duration)
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                var t = time / k_Duration;

                var movement = (float3)cameraTransform.TransformDirection(popup.ValueRO.Movement);
                transform.ValueRW.Position = popup.ValueRO.Position + movement * t;
                transform.ValueRW.Rotation = cameraTransform.rotation;

                var color = math.lerp(popup.ValueRO.Color, float4.zero, t * t);
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
