#ifndef GRAPHIX_TOON_INCLUDED
#define GRAPHIX_TOON_INCLUDED

struct Lighting
{
    half NdotL;
    half specular;
};

half3 CalculateLighting(Lighting lighting, half3 color, half3 V, half3 N)
{
    // https://roystan.net/articles/toon-shader/

    half diffuse = step(0.01, lighting.NdotL); 
    half specular = step(0.01, lighting.specular);

    // Calculate rim lighting.
    // We only want rim to appear on the lit side of the surface,
    // so multiply it by NdotL, raised to a power to smoothly blend it.
    // half rim = (1 - dot(viewDir, normal)) * pow(NdotL, 0.1);
    half rim = (1 - dot(V, N)) * step(0.001, lighting.NdotL);
    rim = step(0.716, rim);

    return (max(diffuse, 0.35) + specular * 0.5 /* specular color */) * color + rim;
}

#endif