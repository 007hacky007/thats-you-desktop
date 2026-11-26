// From-scratch reimplementation for the port.
// Shared code for the Hidden/postVHSPro_* image effect shaders driven by postVHSPro.cs.
//
// Chain (see postVHSPro.OnRenderImage):
//   First   src -> texPass12      RGB to YIQ, scanline quantisation, optional YIQ signal noise
//   Second  texPass12 -> dest     horizontal bleed, signal tweak, vignette, YIQ to RGB, gamma
//   Third   pass23 -> feedback2   phosphor-like trail: decayed old trail vs. bright new motion
//   Forth   pass23 -> texLast     pass23 + trail * feedbackAmp
//   Clear   -> black / transparent (used to initialise the feedback textures)
//   Tape    tape noise overlay (the script creates its material but never blits it)
//
// The intermediate texPass12 is a default 8 bit unsigned render target, so the signed I and Q
// channels are stored biased around 0.5 (PortVHSEncodeYIQ / PortVHSDecodeYIQ).
//
// Every scene sets screenLinesNum to 0; a non-positive line count means "no quantisation" and
// falls back to the real texture height so nothing divides by zero.
//
// Per-shader switch (define before including): PORT_VHS_FIRST, PORT_VHS_SECOND, PORT_VHS_THIRD,
// PORT_VHS_FORTH, PORT_VHS_CLEAR or PORT_VHS_TAPE selects the fragment function PortVHSFrag
// (vertex function PortVHSVert).

#ifndef PORT_VHS_PRO_INCLUDED
#define PORT_VHS_PRO_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;

float time_;
float screenLinesNum;

// Own vertex stage so the uv stays full precision (v2f_img uses half2), which matters for the
// per-pixel bleed taps at high resolutions.
struct PortVHSV2F
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PortVHSV2F PortVHSVert(appdata_img v)
{
    PortVHSV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord.xy;
    return o;
}

// ---------------------------------------------------------------- colour helpers

// NTSC YIQ (FCC coefficients).
inline float3 PortVHSRgbToYiq(float3 c)
{
    return float3(
        dot(c, float3(0.299, 0.587, 0.114)),
        dot(c, float3(0.5959, -0.2746, -0.3213)),
        dot(c, float3(0.2115, -0.5227, 0.3112)));
}

inline float3 PortVHSYiqToRgb(float3 c)
{
    return float3(
        dot(c, float3(1.0, 0.9560, 0.6190)),
        dot(c, float3(1.0, -0.2720, -0.6470)),
        dot(c, float3(1.0, -1.1060, 1.7030)));
}

// I spans about +-0.596 and Q +-0.523; 0.8 keeps both inside [0, 1] with room for noise.
#define PORT_VHS_IQ_SCALE 0.8

inline float3 PortVHSEncodeYIQ(float3 yiq)
{
    return saturate(float3(yiq.x, yiq.yz * PORT_VHS_IQ_SCALE + 0.5));
}

inline float3 PortVHSDecodeYIQ(float3 enc)
{
    return float3(enc.x, (enc.yz - 0.5) / PORT_VHS_IQ_SCALE);
}

// Cheap stateless hash, [0, 1).
inline float PortVHSHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

inline float PortVHSLines(float requested)
{
    return requested > 0.5 ? requested : _MainTex_TexelSize.w;
}

// ---------------------------------------------------------------- First

#if defined(PORT_VHS_FIRST)
float noiseLinesNum;
float noiseQuantizeX;
float signalNoiseAmount;
float signalNoisePower;

fixed4 PortVHSFrag(PortVHSV2F i) : SV_Target
{
    float2 uv = i.uv;

    // Snap the vertical sample position to the centre of the emulated scanline.
    if (screenLinesNum > 0.5)
        uv.y = (floor(uv.y * screenLinesNum) + 0.5) / screenLinesNum;

    float4 src = tex2D(_MainTex, uv);
    float3 yiq = PortVHSRgbToYiq(saturate(src.rgb));

#if defined(VHS_YIQNOISE_ON)
    // Noise cells: one row per noise line, columns get coarser as noiseQuantizeX goes to 1.
    float rows = PortVHSLines(noiseLinesNum);
    float cols = max(1.0, _MainTex_TexelSize.z * (1.0 - saturate(noiseQuantizeX)));
    float2 cell = floor(float2(i.uv.x * cols, i.uv.y * rows));
    float t = floor(time_ * 60.0); // new noise every 1/60 s like a live signal
    float3 r = float3(
        PortVHSHash(cell + float2(t * 1.37, t * 0.71)),
        PortVHSHash(cell + float2(t * 2.11 + 17.0, t * 1.13)),
        PortVHSHash(cell + float2(t * 0.53, t * 2.37 + 41.0))) * 2.0 - 1.0;
    // signalNoisePower towards 1 makes the noise sparse: most cells stay near zero and a few
    // spike. The chroma carrier is noisier than luma on tape, so I/Q get the full amount.
    float k = 1.0 / max(1.0 - saturate(signalNoisePower), 0.02);
    float3 n = sign(r) * pow(abs(r), k);
    yiq += n * signalNoiseAmount * float3(0.5, 1.0, 1.0);
#endif

    return fixed4(PortVHSEncodeYIQ(yiq), src.a);
}
#endif

// ---------------------------------------------------------------- Second

#if defined(PORT_VHS_SECOND)
sampler2D _CurvesTex;
float4 curvesOffest;
float bleedLength;
float bleedAmount;
float vignetteAmount;
float vignetteSpeed;
float signalAdjustY;
float signalAdjustI;
float signalAdjustQ;
float signalShiftY;
float signalShiftI;
float signalShiftQ;
float gammaCorection;

#define PORT_VHS_MAX_BLEED 50 // max_curve_length in the script

fixed4 PortVHSFrag(PortVHSV2F i) : SV_Target
{
    float4 enc = tex2D(_MainTex, i.uv);
    float3 yiq = PortVHSDecodeYIQ(enc.rgb);

#if defined(VHS_BLEED_ON)
    // A tape signal smears to the right: each pixel mixes in the pixels to its left with a
    // decaying weight. Chroma has much less bandwidth than luma, so it trails far longer.
    // The step is measured in pixels of a 480 line picture so the look does not depend on the
    // output resolution, and bleedAmount stretches it.
    float stepX = _MainTex_TexelSize.x * max(_MainTex_TexelSize.w / 480.0, 1.0) * bleedAmount;
    int taps = (int)clamp(bleedLength, 1.0, (float)PORT_VHS_MAX_BLEED);
    float3 acc = 0;
    float3 wsum = 0;
    [loop]
    for (int k = 0; k < PORT_VHS_MAX_BLEED; k++)
    {
        if (k >= taps)
            break;
        float fk = (float)k;
#if defined(VHS_CUSTOM_BLEED_ON)
        float3 w = saturate(tex2Dlod(_CurvesTex, float4((fk + 0.5) / PORT_VHS_MAX_BLEED, 0.5, 0, 0)).rgb - curvesOffest.xyz);
#else
        float3 w = exp2(-fk * float3(0.9, 0.22, 0.22));
#endif
        float2 suv = float2(i.uv.x - fk * stepX, i.uv.y);
        float3 s = PortVHSDecodeYIQ(tex2Dlod(_MainTex, float4(suv, 0, 0)).rgb);
        acc += s * w;
        wsum += w;
    }
    float3 bled = acc / max(wsum, 1e-4);
#if defined(VHS_DEBUG_BLEEDING_ON)
    // Show only what the bleed changed, amplified.
    float3 diffRgb = abs(PortVHSYiqToRgb(bled) - PortVHSYiqToRgb(yiq)) * 4.0;
    return fixed4(saturate(diffRgb), enc.a);
#endif
    yiq = bled;
#endif

#if defined(VHS_SIGNAL_TWEAK_ON)
    yiq += float3(signalAdjustY, signalAdjustI, signalAdjustQ);
    yiq *= float3(signalShiftY, signalShiftI, signalShiftQ);
#endif

#if defined(VHS_VIGNETTE_ON)
    // Darkened, slowly breathing corners.
    float2 c = (i.uv - 0.5) * 2.0;
    float r2 = dot(c, c) * 0.5; // 0 at the centre, 1 in the corners
    float pulse = 0.9 + 0.1 * sin(time_ * vignetteSpeed * 3.0);
    float vig = saturate(1.0 - vignetteAmount * pulse * r2 * sqrt(r2));
    yiq *= vig;
#endif

    float3 rgb = saturate(PortVHSYiqToRgb(yiq));
    rgb = pow(rgb, max(gammaCorection, 1e-3));
    return fixed4(rgb, enc.a);
}
#endif

// ---------------------------------------------------------------- Third

#if defined(PORT_VHS_THIRD)
sampler2D _LastTex;
sampler2D _FeedbackTex;
float feedbackThresh;
float feedbackAmount;
float feedbackFade;
float4 feedbackColor;

fixed4 PortVHSFrag(PortVHSV2F i) : SV_Target
{
    float3 cur = tex2D(_MainTex, i.uv).rgb;
    float3 last = tex2D(_LastTex, i.uv).rgb;
    float3 fb = tex2D(_FeedbackTex, i.uv).rgb;

    // Only what got brighter since the last frame feeds the trail, above a threshold.
    float d = dot(saturate(cur - last), float3(0.299, 0.587, 0.114));
    d = d > feedbackThresh ? d : 0.0;
    float3 fresh = d * feedbackAmount * feedbackColor.rgb;
    return fixed4(max(fb * feedbackFade, fresh), 1.0);
}
#endif

// ---------------------------------------------------------------- Forth

#if defined(PORT_VHS_FORTH)
sampler2D _FeedbackTex;
float feedbackAmp;

fixed4 PortVHSFrag(PortVHSV2F i) : SV_Target
{
    float4 c = tex2D(_MainTex, i.uv);
    float3 fb = tex2D(_FeedbackTex, i.uv).rgb;
    return fixed4(saturate(c.rgb + fb * feedbackAmp), c.a);
}
#endif

// ---------------------------------------------------------------- Clear

#if defined(PORT_VHS_CLEAR)
fixed4 PortVHSFrag(PortVHSV2F i) : SV_Target
{
    return fixed4(0, 0, 0, 0);
}
#endif

// ---------------------------------------------------------------- Tape

#if defined(PORT_VHS_TAPE)
fixed4 PortVHSFrag(PortVHSV2F i) : SV_Target
{
    // Rolling tape grain: per-line jitter with occasional bright dropout streaks.
    float lines = PortVHSLines(screenLinesNum);
    float row = floor(i.uv.y * lines);
    float t = floor(time_ * 30.0);
    float grain = PortVHSHash(float2(floor(i.uv.x * _MainTex_TexelSize.z * 0.5), row + t * 7.0));
    float streak = step(0.995, PortVHSHash(float2(row, t)));
    float n = saturate(grain * 0.15 + streak * (0.5 + 0.5 * grain));
    float4 c = tex2D(_MainTex, i.uv);
    return fixed4(saturate(c.rgb + n), c.a);
}
#endif

#endif // PORT_VHS_PRO_INCLUDED
