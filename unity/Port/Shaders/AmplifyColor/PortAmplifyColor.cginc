// From-scratch reimplementation for the port.
// Shared code for the Hidden/Amplify Color/* image effect shaders driven by AmplifyColorBase.
//
// LUT layout (see the default LUT the script builds): a 1024x32 strip of 32 slices, one per
// blue level. Inside a slice x is red and y is green; texel (b * 32 + r, g) holds the graded
// colour for input (r, g, b) / 31. The game LUTs are imported without sRGB conversion, so the
// strip stores gamma-encoded values and is addressed with gamma-encoded coordinates.
//
// Per-shader switches (define before including):
//   PORT_AC_LINEAR      the project runs in linear colour space: encode to sRGB before the
//                       lookup and decode the result back to linear afterwards.
//   PORT_AC_BLEND       grade through _RgbBlendCacheTex (the pre-lerped LUT the script renders
//                       with BlendCache) instead of _RgbTex.
//   PORT_AC_MASK        blend between the source and the graded colour by _MaskTex.r.
//   PORT_AC_HDR         HDR camera pass: clamp the input to [0, 1] before the lookup.
// Quality keywords: AC_QUALITY_STANDARD interpolates between the two nearest blue slices,
// AC_QUALITY_MOBILE takes a single tap from the nearest slice.

#ifndef PORT_AMPLIFY_COLOR_INCLUDED
#define PORT_AMPLIFY_COLOR_INCLUDED

#include "UnityCG.cginc"

#define PORT_AC_LUT_SIZE 32.0

sampler2D _MainTex;
float4 _MainTex_ST;
sampler2D _RgbTex;
sampler2D _LerpRgbTex;
sampler2D _RgbBlendCacheTex;
sampler2D _MaskTex;
float _lerpAmount;

float3 PortAC_LinearToSrgb(float3 c)
{
    c = max(c, 0.0);
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    return (c <= 0.0031308) ? lo : hi;
}

float3 PortAC_SrgbToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(max((c + 0.055) / 1.055, 0.0), 2.4);
    return (c <= 0.04045) ? lo : hi;
}

// Texture coordinate of the centre of texel (r, g) inside blue slice `slice`.
float2 PortAC_SliceUV(float2 rg, float slice)
{
    const float n = PORT_AC_LUT_SIZE;
    float2 texel = rg * (n - 1.0) + 0.5;
    return float2((slice * n + texel.x) / (n * n), texel.y / n);
}

// Looks a gamma-encoded colour in [0, 1] up in a strip LUT.
float3 PortAC_Lookup(sampler2D lut, float3 c)
{
    c = saturate(c);
    float blue = c.b * (PORT_AC_LUT_SIZE - 1.0);
#if defined(AC_QUALITY_MOBILE)
    float slice = floor(blue + 0.5);
    return tex2Dlod(lut, float4(PortAC_SliceUV(c.rg, slice), 0, 0)).rgb;
#else
    float slice0 = floor(blue);
    float slice1 = min(slice0 + 1.0, PORT_AC_LUT_SIZE - 1.0);
    float t = blue - slice0;
    float3 a = tex2Dlod(lut, float4(PortAC_SliceUV(c.rg, slice0), 0, 0)).rgb;
    float3 b = tex2Dlod(lut, float4(PortAC_SliceUV(c.rg, slice1), 0, 0)).rgb;
    return lerp(a, b, t);
#endif
}

struct PortACVaryings
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PortACVaryings PortACVert(appdata_img v)
{
    PortACVaryings o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord.xy;
    return o;
}

float4 PortACGrade(PortACVaryings i) : SV_Target
{
    float4 src = tex2D(_MainTex, i.uv);
    float3 c = src.rgb;
#if defined(PORT_AC_HDR)
    c = saturate(c);
#endif
#if defined(PORT_AC_LINEAR)
    c = PortAC_LinearToSrgb(c);
#endif

#if defined(PORT_AC_BLEND)
    float3 graded = PortAC_Lookup(_RgbBlendCacheTex, c);
#else
    float3 graded = PortAC_Lookup(_RgbTex, c);
#endif

#if defined(PORT_AC_LINEAR)
    graded = PortAC_SrgbToLinear(graded);
#endif

#if defined(PORT_AC_MASK)
    float mask = tex2D(_MaskTex, i.uv).r;
    graded = lerp(src.rgb, graded, mask);
#endif
    return float4(graded, src.a);
}

// BlendCache: per texel of the 1024x32 cache, mix the two LUT strips.
float4 PortACBlendCache(PortACVaryings i) : SV_Target
{
    float4 a = tex2D(_RgbTex, i.uv);
    float4 b = tex2D(_LerpRgbTex, i.uv);
    return lerp(a, b, _lerpAmount);
}

#endif
