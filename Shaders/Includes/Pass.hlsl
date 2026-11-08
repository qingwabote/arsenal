#if defined(_SKINNING_ON)
#include "Skinning.hlsl"
#endif

Varyings vert (Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionOS = input.positionOS.xyz;
    #if defined(_SKINNING_ON)
    SkinningDeform(positionOS, input.normalOS, input.indices, input.weights, _JointMap, _JointMap_TexelSize.z, UNITY_ACCESS_INSTANCED_PROP(PerInstance, _JointOffset));
    #endif
    float3 positionWS = TransformObjectToWorld(positionOS);

    output.uv = input.texcoord;
    output.positionWS = positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.positionHCS = TransformWorldToHClip(positionWS);
    return output;
}

float4 frag (Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    float3 normalWS = normalize(input.normalWS);

    half4 color = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _BaseColor);
    color *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

    half4 albedo = CalculateSurface(color, input.uv);

    Lighting lighting;
    Light light = GetMainLight();
    lighting.NdotL = saturate(dot(normalWS, light.direction));
    half3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float3 H = SafeNormalize(float3(light.direction) + float3(V));
    half NdotH = saturate(dot(normalWS, H));
    lighting.specular = pow(float(NdotH), float(exp2(10 * _Smoothness + 1))); // Half produces banding, need full precision

    return albedo * half4(CalculateLighting(lighting, light.color, V, normalWS), 1.0);
}