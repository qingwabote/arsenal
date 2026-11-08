#ifndef GRAPHIX_SKINNING_INCLUDED
#define GRAPHIX_SKINNING_INCLUDED

void SkinningDeform(inout float3 pos, inout float3 normal, float4 indices, float4 weights, Texture2D map, float width, float offset)
{
    // exclude from editor
    if (width < 1) { return; }

    float width_inv = 1.0 / width;
    float offset_texel = offset / 4.0;

    float4x4 mat = (float4x4)0.0;
    for (int n = 0; n < 4; n++)
    {
        float index = indices[n] * 3.0 + offset_texel;

        float4 c[3];
        for(int j = 0; j < 3; j++) 
        {
            float i = index + float(j);
            float y = floor(i * width_inv);
            float x = i - width * y;
            c[j] = map.Load(int3(x, y, 0));
        }
        mat += mul(float4x4(
            c[0].x, c[1].x, c[2].x, c[0].w,
            c[0].y, c[1].y, c[2].y, c[1].w,
            c[0].z, c[1].z, c[2].z, c[2].w,
            0.0,   0.0,    0.0,    1.0), 
            weights[n]);
    }
    pos = mul(mat, float4(pos, 1.0)).xyz;
    normal = mul((float3x3)mat, normal); // https://webglfundamentals.org/webgl/lessons/webgl-skinning.html
}

void SkinningDeform_float(float3 pos, float3 normal, float4 indices, float4 weights, Texture2D map, float width, float offset, out float3 outPos, out float3 outNormal)
{
    
    SkinningDeform(pos, normal, indices, weights, map, width, offset);
    outPos = pos;
    outNormal = normal;
}

#endif 