using Graphix;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Arsenal
{
    [MaterialProperty("_Index")]
    public struct DigitIndex : IBufferElementData
    {
        public float Value;
    }

    [MaterialProperty("_Offset")]
    public struct DigitOffset : IBufferElementData
    {
        public float4 Value;
    }

    [MaterialProperty("_BaseColor")]
    public struct DigitColor : IBufferElementData
    {
        public float4 Value;
    }

    [WriteGroup(typeof(MaterialMeshBaking))]
    public struct Figure : IComponentData, IEnableableComponent
    {
        public int Value;
    }

    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class FigureAuthoring : MonoBehaviour
    {
        public Color Color;

        class Baker : Baker<FigureAuthoring>
        {
            public override void Bake(FigureAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                var meshFilter = authoring.GetComponent<MeshFilter>();
                var meshRenderer = authoring.GetComponent<MeshRenderer>();
                AddComponentObject(entity, new MaterialMeshBufferedBaking
                {
                    Meshes = new[] { meshFilter.sharedMesh },
                    Materials = new[] { meshRenderer.sharedMaterial }
                });
                var indexes = AddBuffer<DigitIndex>(entity);
                indexes.Add(new());
                var offsets = AddBuffer<DigitOffset>(entity);
                offsets.Add(new());
                var colors = AddBuffer<DigitColor>(entity);
                colors.Add(new() { Value = UnsafeUtility.As<Color, float4>(ref authoring.Color) });
                AddComponent<Figure>(entity);
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup)), UpdateBefore(typeof(BatchGroup))]
    public partial struct FigureRenderer : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (dirty, figure, indexes, offsets, colors, mms) in SystemAPI.Query<EnabledRefRW<Figure>, RefRW<Figure>, DynamicBuffer<DigitIndex>, DynamicBuffer<DigitOffset>, DynamicBuffer<DigitColor>, DynamicBuffer<MaterialMeshInfoBuffered>>())
            {
                indexes.Clear();
                offsets.Clear();
                var number = figure.ValueRO.Value;
                do
                {
                    indexes.Add(new() { Value = number % 10 });
                    number /= 10;
                } while (number != 0);

                var w = 0.5f;
                for (int i = 0; i < indexes.Length; i++)
                {
                    var offset = -(w * i) - w / 2 + w * indexes.Length / 2;
                    offsets.Add(new() { Value = new float4(offset, 0, 0, 0) });
                }

                for (int i = colors.Length; i < indexes.Length; i++)
                {
                    colors.Add(colors[i - 1]);
                }
                colors.ResizeUninitialized(indexes.Length);

                for (int i = mms.Length; i < indexes.Length; i++)
                {
                    mms.Add(mms[i - 1]);
                }
                mms.ResizeUninitialized(indexes.Length);

                dirty.ValueRW = false;
            }
        }
    }
}