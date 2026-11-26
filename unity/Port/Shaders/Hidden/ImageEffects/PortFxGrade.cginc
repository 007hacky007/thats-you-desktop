// From-scratch helpers for the port's reimplementation of the Standard Assets
// image effects (vignette, aberration, blur, grading, grain, fog, sun shafts).
// Written for the built-in render pipeline; included by the shaders in this folder.
#ifndef PORT_FX_GRADE_INCLUDED
#define PORT_FX_GRADE_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;
float4 _MainTex_ST;

struct PortGradeAppdata
{
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
};

struct PortGradeV2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;   // _MainTex space
    float2 uv2 : TEXCOORD1;  // screen space (matches depth and secondary RTs)
};

// True when the source colour buffer is stored upside down relative to
// other screen-space textures (depth, temporaries).
inline bool PortGradeSourceFlipped()
{
#if UNITY_UV_STARTS_AT_TOP
    return _MainTex_TexelSize.y < 0.0;
#else
    return false;
#endif
}

inline float2 PortGradeScreenUV(float2 uv)
{
    if (PortGradeSourceFlipped())
        uv.y = 1.0 - uv.y;
    return uv;
}

PortGradeV2f PortGradeVert(PortGradeAppdata v)
{
    PortGradeV2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    o.uv2 = PortGradeScreenUV(v.texcoord);
    return o;
}

inline float2 PortGradeTexel()
{
    return abs(_MainTex_TexelSize.xy);
}

// Rec. 709 relative luminance of an RGB triple.
inline float PortGradeLuma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// Convert between the working colour space and a perceptual (gamma) value.
inline float3 PortGradeToPerceptual(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return LinearToGammaSpace(max(c, 0.0));
#endif
}

inline float3 PortGradeFromPerceptual(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return GammaToLinearSpace(max(c, 0.0));
#endif
}

// Normalised 7-tap gaussian weights (sigma about 1.5 taps).
#define PORT_GRADE_W0 0.2710
#define PORT_GRADE_W1 0.2168
#define PORT_GRADE_W2 0.1110
#define PORT_GRADE_W3 0.0367

// Per-channel curve lookup in a 256xN ramp whose rows 0, 1 and 2 hold the
// red, green and blue curves (ColorCorrectionCurves builds 256x4 textures).
// The rows are sampled at their texel centres.
#define PORT_GRADE_CURVE_ROW_R 0.125
#define PORT_GRADE_CURVE_ROW_G 0.375
#define PORT_GRADE_CURVE_ROW_B 0.625
inline float PortGradeCurveU(float x)
{
    // Map 0..1 onto the centres of the first and last of 256 texels.
    return saturate(x) * (255.0 / 256.0) + (0.5 / 256.0);
}
#define PORT_GRADE_CURVES(ramp, rgb) float3( \
    tex2D(ramp, float2(PortGradeCurveU((rgb).r), PORT_GRADE_CURVE_ROW_R)).r, \
    tex2D(ramp, float2(PortGradeCurveU((rgb).g), PORT_GRADE_CURVE_ROW_G)).g, \
    tex2D(ramp, float2(PortGradeCurveU((rgb).b), PORT_GRADE_CURVE_ROW_B)).b)

inline float3 PortGradeSaturation(float3 c, float s)
{
    float l = PortGradeLuma(c);
    return max(lerp(float3(l, l, l), c, s), 0.0);
}

// Cheap screen-space hash in [0,1).
inline float PortGradeHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

#endif // PORT_FX_GRADE_INCLUDED
