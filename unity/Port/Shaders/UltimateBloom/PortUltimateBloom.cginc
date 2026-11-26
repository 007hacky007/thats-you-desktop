// From-scratch reimplementation for the port: shared math for the UltimateBloom
// core shaders (Hidden/Ultimate/Sampling, BrightpassMask, BloomMixer and the
// four BloomCombine variants). Written from the behaviour of the driving
// UltimateBloom / DeluxeFilmicCurve scripts, not from any shader source.
#ifndef PORT_ULTIMATE_BLOOM_INCLUDED
#define PORT_ULTIMATE_BLOOM_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;

struct PortUBAppdata
{
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
};

struct PortUBV2F
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PortUBV2F PortUBVert(PortUBAppdata v)
{
    PortUBV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    return o;
}

// ---------------------------------------------------------------------------
// Filmic intensity curve (DeluxeFilmicCurve). The script uploads the
// precomputed rational coefficients: below the crossover the toe
// (Toe.x*x + Toe.z) / (Toe.y*x + Toe.w), above it the shoulder
// (Shoulder.x*x + Shoulder.z) / (Shoulder.y*x + Shoulder.w) + K. The curve maps
// [black point, white point (_MaxValue)] to [0, 1] and with the strong toe the
// game uses it acts as a soft knee threshold for the bloom input.
// ---------------------------------------------------------------------------
float _K;
float _Crossover;
float4 _Toe;
float4 _Shoulder;
float _MaxValue;

float PortUBCurve1(float x)
{
    x = clamp(x, 0.0, max(_MaxValue, 1e-4));
    float toe = (_Toe.x * x + _Toe.z) / max(_Toe.y * x + _Toe.w, 1e-5);
    float shoulder = (_Shoulder.x * x + _Shoulder.z) / max(_Shoulder.y * x + _Shoulder.w, 1e-5) + _K;
    return max(x <= _Crossover ? toe : shoulder, 0.0);
}

float4 PortUBCurve(float4 c)
{
    return float4(PortUBCurve1(c.r), PortUBCurve1(c.g), PortUBCurve1(c.b), c.a);
}

float PortUBLuma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// ---------------------------------------------------------------------------
// Downsampling filters. _OffsetInfos.xy is the texel size of the source.
// ---------------------------------------------------------------------------
float4 _OffsetInfos;

// Four bilinear taps on the diagonals, one source texel out: each tap
// averages a 2x2 block, so the result is a 4x4 box around the destination
// pixel.
float4 PortUBDown4(float2 uv)
{
    float2 t = _OffsetInfos.xy;
    float4 c = tex2D(_MainTex, uv + float2(-t.x, -t.y));
    c += tex2D(_MainTex, uv + float2( t.x, -t.y));
    c += tex2D(_MainTex, uv + float2(-t.x,  t.y));
    c += tex2D(_MainTex, uv + float2( t.x,  t.y));
    return c * 0.25;
}

// 3x3 tent (1 2 1 x 1 2 1).
float4 PortUBDown9(float2 uv)
{
    float2 t = _OffsetInfos.xy;
    float4 c = tex2D(_MainTex, uv) * 4.0;
    c += (tex2D(_MainTex, uv + float2(-t.x, 0)) + tex2D(_MainTex, uv + float2(t.x, 0))
        + tex2D(_MainTex, uv + float2(0, -t.y)) + tex2D(_MainTex, uv + float2(0, t.y))) * 2.0;
    c += tex2D(_MainTex, uv + float2(-t.x, -t.y)) + tex2D(_MainTex, uv + float2(t.x, -t.y))
       + tex2D(_MainTex, uv + float2(-t.x,  t.y)) + tex2D(_MainTex, uv + float2(t.x,  t.y));
    return c / 16.0;
}

// Thirteen taps grouped into five overlapping 2x2 boxes (one centred box and
// four quadrant boxes). Each box is weighted by 1 / (1 + luma) before the
// boxes are blended, which keeps single very bright pixels from flickering
// from frame to frame (temporally stable).
float PortUBBoxWeight(float4 box)
{
    return 1.0 / (1.0 + PortUBLuma(box.rgb));
}

float4 PortUBDown13Temporal(float2 uv)
{
    float2 t = _OffsetInfos.xy;
    float4 s00 = tex2D(_MainTex, uv + t * float2(-2, -2));
    float4 s10 = tex2D(_MainTex, uv + t * float2( 0, -2));
    float4 s20 = tex2D(_MainTex, uv + t * float2( 2, -2));
    float4 s01 = tex2D(_MainTex, uv + t * float2(-2,  0));
    float4 s11 = tex2D(_MainTex, uv);
    float4 s21 = tex2D(_MainTex, uv + t * float2( 2,  0));
    float4 s02 = tex2D(_MainTex, uv + t * float2(-2,  2));
    float4 s12 = tex2D(_MainTex, uv + t * float2( 0,  2));
    float4 s22 = tex2D(_MainTex, uv + t * float2( 2,  2));
    float4 i0 = tex2D(_MainTex, uv + t * float2(-1, -1));
    float4 i1 = tex2D(_MainTex, uv + t * float2( 1, -1));
    float4 i2 = tex2D(_MainTex, uv + t * float2(-1,  1));
    float4 i3 = tex2D(_MainTex, uv + t * float2( 1,  1));

    float4 bc = (i0 + i1 + i2 + i3) * 0.25;
    float4 b0 = (s00 + s10 + s01 + s11) * 0.25;
    float4 b1 = (s10 + s20 + s11 + s21) * 0.25;
    float4 b2 = (s01 + s11 + s02 + s12) * 0.25;
    float4 b3 = (s11 + s21 + s12 + s22) * 0.25;

    float wc = 0.5 * PortUBBoxWeight(bc);
    float w0 = 0.125 * PortUBBoxWeight(b0);
    float w1 = 0.125 * PortUBBoxWeight(b1);
    float w2 = 0.125 * PortUBBoxWeight(b2);
    float w3 = 0.125 * PortUBBoxWeight(b3);
    return (bc * wc + b0 * w0 + b1 * w1 + b2 * w2 + b3 * w3) / (wc + w0 + w1 + w2 + w3);
}

// ---------------------------------------------------------------------------
// Directional blurs. _OffsetInfos.xy is the step between taps (one source
// texel times the requested scale, along one axis or both). The result is
// tinted by _Tint * _Intensity and the previous (lower) level in
// _AdditiveTexture is added on top. When _OffsetInfos.zw is set (second half
// of the separable upsample blur, zw = destination texel size) that lower
// level is read with four diagonal taps to soften the bilinear upscale;
// otherwise all four taps land on the same spot.
// ---------------------------------------------------------------------------
sampler2D _AdditiveTexture;
float4 _Tint;
float _Intensity;

float4 PortUBGauss(float2 uv, int radius, float sigma)
{
    float2 stepUV = _OffsetInfos.xy;
    float inv2s2 = 1.0 / (2.0 * sigma * sigma);
    float4 acc = 0;
    float wsum = 0;
    [unroll(32)]
    for (int i = -radius; i <= radius; i++)
    {
        float w = exp(-(float)(i * i) * inv2s2);
        acc += tex2D(_MainTex, uv + stepUV * (float)i) * w;
        wsum += w;
    }
    return acc / wsum;
}

// Four equal taps at -1.5, -0.5, 0.5 and 1.5 steps (small cheap blur).
float4 PortUBBlur4(float2 uv)
{
    float2 s = _OffsetInfos.xy;
    float4 c = tex2D(_MainTex, uv - s * 1.5);
    c += tex2D(_MainTex, uv - s * 0.5);
    c += tex2D(_MainTex, uv + s * 0.5);
    c += tex2D(_MainTex, uv + s * 1.5);
    return c * 0.25;
}

float4 PortUBAdditive(float2 uv)
{
    float2 d = _OffsetInfos.zw;
    float4 c = tex2D(_AdditiveTexture, uv + float2(-d.x, -d.y));
    c += tex2D(_AdditiveTexture, uv + float2( d.x, -d.y));
    c += tex2D(_AdditiveTexture, uv + float2(-d.x,  d.y));
    c += tex2D(_AdditiveTexture, uv + float2( d.x,  d.y));
    return c * 0.25;
}

float4 PortUBFinishBlur(float4 blurred, float2 uv)
{
    return blurred * _Tint * _Intensity + PortUBAdditive(uv);
}

// Sigma for an N-tap kernel of the given radius: wide enough that the outer
// taps still contribute (soft, wide bloom), narrow enough to avoid a boxy edge.
#define PORT_UB_SIGMA(r) (((float)(r) + 1.0) * 0.42)

// ---------------------------------------------------------------------------
// Final combine. _MainTex is the last bloom upsample, _ColorBuffer the scene,
// _FlareTexture the flare/anamorphic image, _AdditiveTexture the lens dust.
// The dust is lit by the bloom that reaches it (_DirtIntensity) and by the
// flares (_DirtLightIntensity).
// ---------------------------------------------------------------------------
sampler2D _ColorBuffer;
float4 _ColorBuffer_TexelSize;
sampler2D _FlareTexture;
sampler2D _brightTexture;
float _FlareIntensity;
float _DirtIntensity;
float _DirtLightIntensity;
float _ScreenMaxIntensity;

// Pass 1 ("inverted image") exists for render paths where the scene buffer
// is stored upside down relative to the bloom targets. Unity 6 keeps the
// OnRenderImage chain consistently oriented, so only the classic
// flipped-texel-size signal is honoured and the bloom inputs are realigned to
// the scene; otherwise it matches pass 0.
float2 PortUBBloomUV(float2 uv, bool invert)
{
#if UNITY_UV_STARTS_AT_TOP
    if (invert && _ColorBuffer_TexelSize.y < 0)
        uv.y = 1.0 - uv.y;
#endif
    return uv;
}

float3 PortUBBloomLight(float2 uv, bool invert, bool flareDirt, out float sceneAlpha, out float3 scene)
{
    float4 sc = tex2D(_ColorBuffer, uv);
    scene = sc.rgb;
    sceneAlpha = sc.a;
    float2 buv = PortUBBloomUV(uv, invert);
    float3 bloom = tex2D(_MainTex, buv).rgb * _Intensity;
    float3 flare = tex2D(_FlareTexture, buv).rgb * _FlareIntensity;
    float3 light = bloom + flare;
    if (flareDirt)
    {
        float3 dust = tex2D(_AdditiveTexture, uv).rgb;
        light += dust * (bloom * _DirtIntensity + flare * _DirtLightIntensity);
    }
    return max(light, 0.0);
}

float4 PortUBCombineAdd(float2 uv, bool invert, bool flareDirt)
{
    float a;
    float3 scene;
    float3 light = PortUBBloomLight(uv, invert, flareDirt, a, scene);
    return float4(scene + light, a);
}

// Screen blend: the added light fades out as the scene approaches white, and
// the light itself is capped at _ScreenMaxIntensity (a cap of 0 or less means
// no cap).
float4 PortUBCombineScreen(float2 uv, bool invert, bool flareDirt)
{
    float a;
    float3 scene;
    float3 light = PortUBBloomLight(uv, invert, flareDirt, a, scene);
    if (_ScreenMaxIntensity > 0.0)
        light = min(light, _ScreenMaxIntensity);
    return float4(scene + light * (1.0 - saturate(scene)), a);
}

#endif
