Shader "Graphix/Phong"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        _BaseMap("Base Map", 2D) = "white" {}

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5

        [Toggle] _SKINNING ("Enable Vertex Skinning", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _SKINNING_ON

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma instancing_options nolightprobe

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;

                #if defined(_SKINNING_ON)
                uint4 indices : BLENDINDICES;
                float4 weights : BLENDWEIGHTS;
                #endif

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float4 positionHCS : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            CBUFFER_START(UnityPerMaterial)
                half _Smoothness;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(half4, _BaseColor)
            #if defined(_SKINNING_ON)
                UNITY_DEFINE_INSTANCED_PROP(float, _JointOffset)
            #endif
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            #if defined(_SKINNING_ON)
            TEXTURE2D(_JointMap);
            float4 _JointMap_TexelSize;
            #endif

            half4 CalculateSurface(half4 color, float2 uv)
            {
                return color;
            }

            struct Lighting
            {
                half NdotL;
                half specular;
            };

            half3 CalculateLighting(Lighting lighting, half3 color, half3 V, half3 N)
            {
                return (lighting.NdotL + lighting.specular * 0.5 /* specular color */) * color + half3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
            }

            #include "Includes/Pass.hlsl"
            ENDHLSL
        }
    }
}
