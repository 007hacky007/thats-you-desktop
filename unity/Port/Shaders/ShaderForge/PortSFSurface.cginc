// From-scratch reimplementation for the port.
// Shared helpers for the "Shader Forge/*" opaque PBR surface variants (built-in render pipeline).
// Every variant is Unity Standard lighting fed from extra inputs; this file holds the common
// pieces: raw UV passing, a normal decode that tolerates every normal map import layout,
// metallic/smoothness packing, an occlusion mix and the edge scattering lighting model.

#ifndef PORT_SF_SURFACE_INCLUDED
#define PORT_SF_SURFACE_INCLUDED

#include "UnityCG.cginc"
#include "UnityPBSLighting.cginc"

// First and second mesh UV sets packed in one interpolator (xy = uv0, zw = uv1). Each texture
// then applies its own _ST tiling/offset, so every map keeps its material tiling.
inline float4 PortSF_RawUV(appdata_full v)
{
    return float4(v.texcoord.xy, v.texcoord1.xy);
}

// Tangent space normal decode. The packed texel can come in three layouts depending on how
// the texture got imported: RG (BC5, A = 1), AG (DXT5nm, R = 1) or a plain RGBA export where
// R and A both carry X. min(R, A) picks X correctly in all three.
inline half3 PortSF_UnpackNormal(half4 texel)
{
    half3 n;
    n.x = min(texel.r, texel.a) * 2.0h - 1.0h;
    n.y = texel.g * 2.0h - 1.0h;
    n.z = sqrt(1.0h - saturate(dot(n.xy, n.xy)));
    return n;
}

// Metallic map convention used by these materials: R = metallic, A = smoothness.
inline void PortSF_MetallicSmoothness(half4 m, out half metallic, out half smoothness)
{
    metallic = m.r;
    smoothness = m.a;
}

// Ambient occlusion mixed in by an amount slider (0 = none, 1 = full map).
inline half PortSF_Occlusion(half aoSample, half amount)
{
    return lerp(1.0h, aoSample, saturate(amount));
}

// ---------------------------------------------------------------------------------------------
// Edge scattering model (DiffuseScattering, DiffuseScatteringAlpha).
// Standard PBR plus a soft, velvet-like rim: a fresnel style edge mask (exponent = edge
// threshold, scaled by the tint boost) adds tinted albedo light at grazing angles. The light
// direction contribution sharpens that rim towards the lit side of the object.
// ---------------------------------------------------------------------------------------------

struct SurfaceOutputPortScatter
{
    fixed3 Albedo;
    float3 Normal;
    half3 Emission;
    half Metallic;
    half Smoothness;
    half Occlusion;
    fixed Alpha;
    half3 EdgeTint;
    half EdgeExponent;
    half EdgeBoost;
    half LightPower;
};

inline SurfaceOutputStandard PortSF_ToStandard(SurfaceOutputPortScatter s)
{
    SurfaceOutputStandard o;
    o.Albedo = s.Albedo;
    o.Normal = s.Normal;
    o.Emission = s.Emission;
    o.Metallic = s.Metallic;
    o.Smoothness = s.Smoothness;
    o.Occlusion = s.Occlusion;
    o.Alpha = s.Alpha;
    return o;
}

inline half4 LightingPortSFScatter(SurfaceOutputPortScatter s, float3 viewDir, UnityGI gi)
{
    SurfaceOutputStandard so = PortSF_ToStandard(s);
    half4 c = LightingStandard(so, viewDir, gi);

    half nv = saturate(dot(s.Normal, viewDir));
    half edge = saturate(pow(1.0h - nv, max(s.EdgeExponent, 0.001h)) * s.EdgeBoost);

    half wrapNL = saturate(dot(s.Normal, gi.light.dir) * 0.5h + 0.5h);
    half lightFactor = pow(wrapNL, max(s.LightPower, 0.001h));

    half3 edgeColor = s.Albedo * s.EdgeTint * edge;
    c.rgb += edgeColor * (gi.light.color * lightFactor + gi.indirect.diffuse);
    return c;
}

inline void LightingPortSFScatter_GI(SurfaceOutputPortScatter s, UnityGIInput data, inout UnityGI gi)
{
    SurfaceOutputStandard so = PortSF_ToStandard(s);
    LightingStandard_GI(so, data, gi);
}

#endif // PORT_SF_SURFACE_INCLUDED
