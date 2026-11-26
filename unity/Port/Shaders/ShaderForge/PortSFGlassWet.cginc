// From-scratch reimplementation for the port.
// Shared helpers for the Shader Forge glass, water and wet-surface family (U8).
// Built-in render pipeline, CG/HLSL.
#ifndef PORT_SF_GLASS_WET_INCLUDED
#define PORT_SF_GLASS_WET_INCLUDED

#include "UnityCG.cginc"
#include "UnityLightingCommon.cginc"

// ---------------------------------------------------------------------------------------------
// Normals
// ---------------------------------------------------------------------------------------------

// Tangent-space normal from a normal map sample, with the xy tilt scaled by strength
// (0 = flat, 1 = as authored).
half3 PortSF_UnpackNormalStrength(half4 packedNormal, half strength)
{
    half3 n = UnpackNormal(packedNormal);
    n.xy *= strength;
    return normalize(n);
}

// Combines two tangent-space normals (whiteout style: tilts add, z multiplies).
half3 PortSF_BlendNormals(half3 a, half3 b)
{
    return normalize(half3(a.xy + b.xy, a.z * b.z));
}

// Tangent-space normal to world space with the per-vertex basis (t = tangent, b = bitangent,
// n = geometric normal, all world space).
half3 PortSF_TangentToWorld(half3 tn, float3 t, float3 b, float3 n)
{
    return normalize(t * tn.x + b * tn.y + n * tn.z);
}

// World-space tangent basis for a vertex.
void PortSF_WorldBasis(float3 objNormal, float4 objTangent, out float3 wT, out float3 wB, out float3 wN)
{
    wN = UnityObjectToWorldNormal(objNormal);
    wT = UnityObjectToWorldDir(objTangent.xyz);
    wB = cross(wN, wT) * (objTangent.w * unity_WorldTransformParams.w);
}

// ---------------------------------------------------------------------------------------------
// Fresnel, specular and reflections
// ---------------------------------------------------------------------------------------------

// 0 when facing the viewer, rising to 1 at grazing angles; exponent shapes the falloff.
half PortSF_Fresnel(half3 n, half3 v, half exponent)
{
    return pow(1.0h - saturate(dot(n, v)), max(exponent, 0.001h));
}

// Blinn-Phong highlight. gloss 0..1 maps to an exponent of 2..2048.
half PortSF_BlinnSpec(half3 n, half3 l, half3 v, half gloss)
{
    half3 h = normalize(l + v);
    half power = exp2(saturate(gloss) * 10.0h + 1.0h);
    return pow(saturate(dot(n, h)), power) * saturate(dot(n, l) * 8.0h);
}

// Reflection probe lookup (probe 0), blurrier for lower smoothness.
half3 PortSF_EnvReflection(half3 n, half3 v, half smoothness)
{
    float3 r = reflect(-v, n);
    half mip = (1.0h - saturate(smoothness)) * 6.0h;
    half4 encoded = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, r, mip);
    return DecodeHDR(encoded, unity_SpecCube0_HDR);
}

// Main directional light (ForwardBase) or the current light (ForwardAdd) direction.
half3 PortSF_LightDir(float3 worldPos)
{
    return normalize(_WorldSpaceLightPos0.xyz - worldPos * _WorldSpaceLightPos0.w);
}

// ---------------------------------------------------------------------------------------------
// Screen grab used by the glass shaders in this family
// ---------------------------------------------------------------------------------------------
// Each glass shader declares an unnamed GrabPass { }, so Unity copies the screen into
// _GrabTexture right before every glass object draws. That copy already contains any glass
// drawn earlier in the transparent queue, so glass seen through glass keeps both layers
// (a shared named grab would be taken once and later glass would paint over earlier glass).

sampler2D _GrabTexture;

float2 PortSF_GrabUV(float4 grabPos)
{
    return grabPos.xy / max(grabPos.w, 1e-5);
}

half3 PortSF_SampleGrab(float2 uv)
{
    return tex2D(_GrabTexture, uv).rgb;
}

// Small disc blur (13 taps: centre, inner ring of 6 at half radius, outer ring of 6 at full
// radius). radius is in screen uv units of the screen height (BLUR AMOUNT values are 0..0.005).
half3 PortSF_SampleGrabBlurred(float2 uv, float radius)
{
    float2 r = float2(radius * _ScreenParams.y / _ScreenParams.x, radius);
    float2 ri = r * 0.5;
    half3 c = tex2D(_GrabTexture, uv).rgb * 0.16h;

    half3 inner = 0;
    inner += tex2D(_GrabTexture, uv + ri * float2( 1.0,    0.0  )).rgb;
    inner += tex2D(_GrabTexture, uv + ri * float2( 0.5,    0.866)).rgb;
    inner += tex2D(_GrabTexture, uv + ri * float2(-0.5,    0.866)).rgb;
    inner += tex2D(_GrabTexture, uv + ri * float2(-1.0,    0.0  )).rgb;
    inner += tex2D(_GrabTexture, uv + ri * float2(-0.5,   -0.866)).rgb;
    inner += tex2D(_GrabTexture, uv + ri * float2( 0.5,   -0.866)).rgb;

    half3 outer = 0;
    outer += tex2D(_GrabTexture, uv + r * float2( 0.866,  0.5)).rgb;
    outer += tex2D(_GrabTexture, uv + r * float2( 0.0,    1.0)).rgb;
    outer += tex2D(_GrabTexture, uv + r * float2(-0.866,  0.5)).rgb;
    outer += tex2D(_GrabTexture, uv + r * float2(-0.866, -0.5)).rgb;
    outer += tex2D(_GrabTexture, uv + r * float2( 0.0,   -1.0)).rgb;
    outer += tex2D(_GrabTexture, uv + r * float2( 0.866, -0.5)).rgb;

    return c + inner * 0.09h + outer * 0.05h;
}

// Screen-space refraction offset. tangentTilt is the xy of the (blended) tangent-space normal;
// the geometric term bends the background a little near silhouettes of curved glass even
// without a normal map. strength is the DISTORTION STRENGTH slider (0..10).
float2 PortSF_RefractionOffset(half2 tangentTilt, half3 worldNormal, half3 viewDir, half strength)
{
    float3 vn = mul((float3x3)UNITY_MATRIX_V, (float3)worldNormal);
    #if UNITY_UV_STARTS_AT_TOP
    vn.y = -vn.y;
    #endif
    half rim = 1.0h - saturate(dot(worldNormal, viewDir));
    float2 tilt = (float2)tangentTilt + vn.xy * (0.25 * rim);
    return tilt * (strength * 0.01);
}

// ---------------------------------------------------------------------------------------------
// Shared vertex stage for the hand-written glass passes
// ---------------------------------------------------------------------------------------------
// ForwardAdd passes include AutoLight.cginc and wrap this in their own struct with
// UNITY_LIGHTING_COORDS (see PORT_SF_GLASS_ADD_V2F below).

struct PortSFGlassAppdata
{
    float4 vertex   : POSITION;
    float3 normal   : NORMAL;
    float4 tangent  : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 texcoord1 : TEXCOORD1;
};

struct PortSFGlassV2F
{
    float4 pos      : SV_POSITION;
    float2 uv       : TEXCOORD0;
    float4 grabPos  : TEXCOORD1;
    float3 worldPos : TEXCOORD2;
    float3 wT       : TEXCOORD3;
    float3 wB       : TEXCOORD4;
    float3 wN       : TEXCOORD5;
    UNITY_FOG_COORDS(6)
};

PortSFGlassV2F PortSF_GlassVert(PortSFGlassAppdata v)
{
    PortSFGlassV2F o;
    UNITY_INITIALIZE_OUTPUT(PortSFGlassV2F, o);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    o.grabPos = ComputeGrabScreenPos(o.pos);
    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    PortSF_WorldBasis(v.normal, v.tangent, o.wT, o.wB, o.wN);
    UNITY_TRANSFER_FOG(o, o.pos);
    return o;
}

// ForwardAdd variant: same data plus light attenuation coordinates. Use after
// #include "AutoLight.cginc" in a pass compiled with #pragma multi_compile_fwdadd.
#define PORT_SF_GLASS_ADD_V2F                                                          \
    struct PortSFGlassAddV2F                                                           \
    {                                                                                  \
        float4 pos      : SV_POSITION;                                                 \
        float2 uv       : TEXCOORD0;                                                   \
        float3 worldPos : TEXCOORD1;                                                   \
        float3 wT       : TEXCOORD2;                                                   \
        float3 wB       : TEXCOORD3;                                                   \
        float3 wN       : TEXCOORD4;                                                   \
        UNITY_FOG_COORDS(5)                                                            \
        UNITY_LIGHTING_COORDS(6, 7)                                                    \
    };                                                                                 \
    PortSFGlassAddV2F PortSF_GlassAddVert(PortSFGlassAppdata v)                        \
    {                                                                                  \
        PortSFGlassAddV2F o;                                                           \
        UNITY_INITIALIZE_OUTPUT(PortSFGlassAddV2F, o);                                 \
        o.pos = UnityObjectToClipPos(v.vertex);                                        \
        o.uv = v.texcoord;                                                             \
        o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;                           \
        PortSF_WorldBasis(v.normal, v.tangent, o.wT, o.wB, o.wN);                      \
        UNITY_TRANSFER_FOG(o, o.pos);                                                  \
        UNITY_TRANSFER_LIGHTING(o, v.texcoord1);                                       \
        return o;                                                                      \
    }

// ---------------------------------------------------------------------------------------------
// Wet surfaces
// ---------------------------------------------------------------------------------------------

// Height-aware mask blend: mask 0 gives 0, mask 1 gives 1, in between the low parts of
// height (0..1) fill first. hardness sharpens the transition edge.
half PortSF_HeightBlend(half mask, half height, half hardness)
{
    half t = mask * 1.5h - height * 0.5h;
    return saturate((t - 0.5h) * hardness + 0.5h);
}

// UV of one cell of a cols x rows flipbook, cells read left to right, top row first.
float2 PortSF_FlipbookUV(float2 uv, float frame, float cols, float rows)
{
    cols = max(cols, 1.0);
    rows = max(rows, 1.0);
    float count = cols * rows;
    float f = floor(fmod(max(frame, 0.0), count));
    float col = fmod(f, cols);
    float row = rows - 1.0 - floor(f / cols);
    return (frac(uv) + float2(col, row)) / float2(cols, rows);
}

#endif // PORT_SF_GLASS_WET_INCLUDED
