// From-scratch reimplementation for the port.
// Shared helpers for the small stand-alone shaders in Shaders/Misc:
//   - the depth+normals replacement pass (Hidden/Camera-DepthNormalTexture),
//   - the cinematic ambient occlusion image effect (Hidden/Image Effects/Cinematic/AmbientOcclusion).

#ifndef PORT_MISC_INCLUDED
#define PORT_MISC_INCLUDED

#include "UnityCG.cginc"

// ---------------------------------------------------------------------------
// Depth + normals replacement pass
// Output contract (same as Unity's _CameraDepthNormalsTexture):
// EncodeDepthNormal(linear 0..1 depth, view space normal with z towards the camera).
// ---------------------------------------------------------------------------

struct PortDNAppData
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct PortDNV2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 nz : TEXCOORD1; // xyz view normal, w linear 01 depth
    UNITY_VERTEX_OUTPUT_STEREO
};

PortDNV2f PortDNVert(PortDNAppData v, float4 st)
{
    PortDNV2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord.xy * st.xy + st.zw;
    float3 viewPos = UnityObjectToViewPos(v.vertex.xyz);
    o.nz.xyz = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);
    o.nz.w = -viewPos.z * _ProjectionParams.w;
    return o;
}

float4 PortDNEncode(PortDNV2f i)
{
    return EncodeDepthNormal(i.nz.w, normalize(i.nz.xyz));
}

// ---------------------------------------------------------------------------
// Fullscreen helpers for the ambient occlusion effect
// ---------------------------------------------------------------------------

// Pseudo random value in [0,1) from a 2D seed.
float PortHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

// Camera space position (z forward, positive eye depth) for a screen uv and eye depth.
float3 PortViewPosFromDepth(float2 uv, float eyeDepth)
{
    float2 ndc = uv * 2.0 - 1.0;
    float2 scale = float2(unity_CameraProjection._11, unity_CameraProjection._22);
    float2 offs = float2(unity_CameraProjection._13, unity_CameraProjection._23);
    return float3((ndc + offs) * eyeDepth / scale, eyeDepth);
}

// Screen uv of a camera space point (inverse of PortViewPosFromDepth).
float2 PortUVFromViewPos(float3 p)
{
    float2 scale = float2(unity_CameraProjection._11, unity_CameraProjection._22);
    float2 offs = float2(unity_CameraProjection._13, unity_CameraProjection._23);
    float2 ndc = p.xy * scale / max(p.z, 1e-4) - offs;
    return ndc * 0.5 + 0.5;
}

#endif
