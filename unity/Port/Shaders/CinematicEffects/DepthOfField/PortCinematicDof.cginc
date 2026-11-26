// From-scratch reimplementation for the port: shared code for the cinematic
// depth of field shaders (Hidden/DepthOfField/*), driven by
// UnityStandardAssets.CinematicEffects.DepthOfField. Written for this port from
// the observable behaviour of the driving script; not derived from any
// original shader source.
//
// Conventions used by every pass:
//  * The half-resolution working buffers carry colour in RGB and a signed,
//    normalised circle of confusion (CoC) in A: -1 = full near blur,
//    0 = in focus, +1 = full far blur. The CoC is monotonic in depth.
//  * _BlurCoe.xy hold the near / far blur radius in half-resolution texels
//    during the blur and merge passes.
//  * The dilated foreground CoC (RG half texture) stores the near blur
//    amount (0..1) that spreads over neighbouring pixels.

#ifndef PORT_CINEMATIC_DOF_INCLUDED
#define PORT_CINEMATIC_DOF_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;
sampler2D _SecondTex;
float4 _SecondTex_TexelSize;
sampler2D _ThirdTex;
float4 _ThirdTex_TexelSize;

float4 _BlurParams;
float4 _BlurCoe;
float4 _BoostParams;
float4 _Offsets;
float4 _Convolved_TexelSize;

UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

struct PortDofAppData
{
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
};

struct PortDofV2f
{
    float4 pos : SV_POSITION;
    // uv samples _MainTex; uvAux samples every other screen-space texture
    // (depth, secondary targets), flipped when the source is upside down.
    float2 uv : TEXCOORD0;
    float2 uvAux : TEXCOORD1;
};

PortDofV2f PortDofVert(PortDofAppData v)
{
    PortDofV2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    o.uvAux = v.texcoord;
#if UNITY_UV_STARTS_AT_TOP
    if (_MainTex_TexelSize.y < 0)
        o.uvAux.y = 1.0 - o.uvAux.y;
#endif
    return o;
}

float2 PortDofTexel()
{
    return abs(_MainTex_TexelSize.xy);
}

float4 PortDofTap(float2 uv)
{
    return tex2Dlod(_MainTex, float4(uv, 0.0, 0.0));
}

float PortDofLuma(float3 rgb)
{
    return dot(rgb, float3(0.2126, 0.7152, 0.0722));
}

float PortDofDepth01(float2 uv)
{
    return Linear01Depth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0.0, 0.0)));
}

// ---------------------------------------------------------------------------
// Circle of confusion
// ---------------------------------------------------------------------------

// Basic / Advanced: _BlurParams = (lens / fStops, lens, focus01, range^4).
// Thin-lens style: the CoC grows with aperture * (d - focus) / d. The range
// value is a tolerance below which the CoC is treated as sharp, so a larger
// range widens the in-focus band.
float PortDofCocBasic(float d)
{
    float raw = _BlurParams.x * (d - _BlurParams.z) / max(d, 1e-5);
    float mag = saturate(abs(raw) - _BlurParams.w);
    return raw < 0.0 ? -mag : mag;
}

// Explicit: _BlurParams.xy give a line that reaches -1 at the near plane and
// 0 at (focus - range); _BlurParams.zw a line that is 0 at (focus + range) and
// +1 at the far plane. _BlurCoe.z is the depth where the two lines cross.
float PortDofCocExplicit(float d)
{
    float nearC = clamp(_BlurParams.x * d + _BlurParams.y, -1.0, 0.0);
    float farC = clamp(_BlurParams.z * d + _BlurParams.w, 0.0, 1.0);
    return d < _BlurCoe.z ? nearC : farC;
}

float PortDofCoc(float d, bool isExplicit)
{
    return isExplicit ? PortDofCocExplicit(d) : PortDofCocBasic(d);
}

// Blur radius in half-resolution texels for a signed CoC.
float PortDofRadius(float coc)
{
    return coc < 0.0 ? -coc * _BlurCoe.x : coc * _BlurCoe.y;
}

float PortDofMaxRadius()
{
    return max(max(_BlurCoe.x, _BlurCoe.y), 1e-3);
}

// ---------------------------------------------------------------------------
// CoC capture: full-res colour + depth -> half-res colour with CoC in alpha
// ---------------------------------------------------------------------------
float4 PortDofCapture(PortDofV2f i, bool isExplicit)
{
    float2 tx = PortDofTexel();
    float2 o[4] = { float2(-0.5, -0.5), float2(0.5, -0.5), float2(-0.5, 0.5), float2(0.5, 0.5) };

    float4 col = 0.0;
    float wsum = 0.0;
    float cocMin = 1.0;
    float cocSum = 0.0;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float c = PortDofCoc(PortDofDepth01(i.uvAux + o[k] * tx), isExplicit);
        float3 rgb = PortDofTap(i.uv + o[k] * tx).rgb;
#if defined(USE_SPECIAL_FETCH_FOR_COC)
        // Prevent haloing: sharp pixels contribute little to the colour that
        // gets blurred, so a bright in-focus edge does not bleed into the bokeh.
        float w = 0.05 + abs(c);
#else
        float w = 1.0;
#endif
        col.rgb += rgb * w;
        wsum += w;
        cocMin = min(cocMin, c);
        cocSum += c;
    }
    col.rgb /= wsum;

    // Foreground blur is kept conservative (nearest tap wins), otherwise the
    // average of the four taps.
    float coc = cocMin < 0.0 ? cocMin : cocSum * 0.25;

    // Luminosity boost of bright out-of-focus areas (_BoostParams.z = threshold).
    float boost = coc < 0.0 ? coc * _BoostParams.x : coc * _BoostParams.y;
    float bright = saturate(PortDofLuma(col.rgb) - _BoostParams.z);
    col.rgb *= 1.0 + bright * max(boost, 0.0);

    col.a = coc;
    return col;
}

// ---------------------------------------------------------------------------
// Coc visualisation: yellow = near blur, blue = far blur
// ---------------------------------------------------------------------------
float4 PortDofVisualize(PortDofV2f i, bool isExplicit)
{
    float c = PortDofCoc(PortDofDepth01(i.uvAux), isExplicit);
    float3 base = float3(0.15, 0.15, 0.15);
    float3 nearCol = float3(1.0, 0.85, 0.0);
    float3 farCol = float3(0.0, 0.35, 1.0);
    float3 rgb = c < 0.0 ? lerp(base, nearCol, -c) : lerp(base, farCol, c);
    return float4(rgb, 1.0);
}

// ---------------------------------------------------------------------------
// Foreground CoC dilation (separable, along _Offsets.xy in texels). Each tap
// spreads its near blur amount over a distance proportional to that amount,
// with a linear falloff, so the dilated mask has soft edges.
// ---------------------------------------------------------------------------
#define PORT_DOF_DILATE_TAPS 8

float PortDofDilate(float2 uv, bool fromColor)
{
    float2 tx = PortDofTexel();
    float len = max(length(_Offsets.xy), 1e-3);
    float2 dir = _Offsets.xy / len;
    float best = 0.0;
    [unroll]
    for (int k = -PORT_DOF_DILATE_TAPS; k <= PORT_DOF_DILATE_TAPS; k++)
    {
        float t = abs((float)k) / PORT_DOF_DILATE_TAPS;
        float4 s = PortDofTap(uv + dir * (k * len / PORT_DOF_DILATE_TAPS) * tx);
        float fg = fromColor ? saturate(-s.a) : s.r;
        best = max(best, fg * saturate(1.0 - t / max(fg, 1e-3)));
    }
    return best;
}

// ---------------------------------------------------------------------------
// Prefilter: small CoC-aware smoothing of the half-res colour
// ---------------------------------------------------------------------------
float4 PortDofPrefilter(float2 uv)
{
    float2 tx = PortDofTexel();
    float4 c0 = PortDofTap(uv);
    float2 o[4] = { float2(-1.0, -1.0), float2(1.0, -1.0), float2(-1.0, 1.0), float2(1.0, 1.0) };
    float3 sum = c0.rgb;
    float wsum = 1.0;
    float spread = saturate(abs(c0.a) * 4.0);
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float4 s = PortDofTap(uv + o[k] * 0.75 * tx);
        float w = spread * saturate(1.0 - abs(s.a - c0.a) * 4.0);
        sum += s.rgb * w;
        wsum += w;
    }
    return float4(sum / wsum, c0.a);
}

// ---------------------------------------------------------------------------
// Scatter-as-gather weight. A sample nearer than the centre (smaller signed
// CoC) spreads over its own radius; a sample behind the centre may only
// reach as far as the centre's own radius, so background blur never leaks
// over a sharper foreground.
// ---------------------------------------------------------------------------
float PortDofWeight(float cocS, float cocC, float rC, float dist, float soft)
{
    float rS = PortDofRadius(cocS);
    float rEff = cocS <= cocC ? rS : min(rS, rC);
    return saturate((rEff - dist) / soft + 1.0);
}

// Alpha written by the blur passes: with a dilated foreground mask the
// stronger of the own CoC and the spread near blur is kept for the merge.
float PortDofOutAlpha(float cocC, float fg, bool dilated)
{
    if (dilated && fg > abs(cocC))
        return -fg;
    return cocC;
}

// ---------------------------------------------------------------------------
// Circular gather on concentric rings (ring r holds 8 * r samples).
// ---------------------------------------------------------------------------
float4 PortDofCircle(PortDofV2f i, int rings, bool dilated)
{
    float2 tx = PortDofTexel();
    float4 center = PortDofTap(i.uv);
    float cocC = center.a;
    float rC = PortDofRadius(cocC);
    float fg = 0.0;
    if (dilated)
    {
        // The dilated mask only decides where the merge shows the blurred
        // result; nearer samples already spread over their own radius.
        fg = tex2Dlod(_SecondTex, float4(i.uvAux, 0.0, 0.0)).r;
    }

    float maxR = PortDofMaxRadius();
    float ringStep = maxR / rings;
    float soft = max(ringStep, 1.0);

    float3 sum = center.rgb;
    float wsum = 1.0;
    [loop]
    for (int r = 1; r <= rings; r++)
    {
        float dist = r * ringStep;
        int count = 8 * r;
        float angStep = 6.2831853 / count;
        float angOfs = (r % 2 == 1) ? 0.0 : angStep * 0.5;
        [loop]
        for (int k = 0; k < count; k++)
        {
            float a = angOfs + k * angStep;
            float2 ofs = float2(cos(a), sin(a)) * dist;
            float4 s = PortDofTap(i.uv + ofs * tx);
            float w = PortDofWeight(s.a, cocC, rC, dist, soft);
            sum += s.rgb * w;
            wsum += w;
        }
    }
    return float4(sum / wsum, PortDofOutAlpha(cocC, fg, dilated));
}

// ---------------------------------------------------------------------------
// Directional (line) gather used for the hexagonal / octogonal apertures.
// _Offsets.xy is the direction (length 0.5); the line spans the full blur
// radius on both sides of the pixel. Two such passes in different
// directions build a parallelogram; the merge variant averages it with the
// other parallelogram in _ThirdTex to approximate the polygon.
// ---------------------------------------------------------------------------
float4 PortDofLine(PortDofV2f i, int taps, bool dilated)
{
    float2 tx = PortDofTexel();
    float4 center = PortDofTap(i.uv);
    float cocC = center.a;
    float rC = PortDofRadius(cocC);
    float fg = 0.0;
    if (dilated)
    {
        // The dilated mask only decides where the merge shows the blurred
        // result; nearer samples already spread over their own radius.
        fg = tex2Dlod(_SecondTex, float4(i.uvAux, 0.0, 0.0)).r;
    }

    float len = max(length(_Offsets.xy), 1e-3);
    float2 dir = _Offsets.xy / len;
    float maxR = PortDofMaxRadius();
    float stepLen = maxR / taps;
    float soft = max(stepLen, 1.0);

    float3 sum = center.rgb;
    float wsum = 1.0;
    [loop]
    for (int k = 1; k <= taps; k++)
    {
        float dist = k * stepLen;
        float2 ofs = dir * dist * tx;
        float4 s0 = PortDofTap(i.uv + ofs);
        float4 s1 = PortDofTap(i.uv - ofs);
        float w0 = PortDofWeight(s0.a, cocC, rC, dist, soft);
        float w1 = PortDofWeight(s1.a, cocC, rC, dist, soft);
        sum += s0.rgb * w0 + s1.rgb * w1;
        wsum += w0 + w1;
    }
    return float4(sum / wsum, PortDofOutAlpha(cocC, fg, dilated));
}

float4 PortDofLineMerge(PortDofV2f i, int taps, bool dilated)
{
    float4 a = PortDofLine(i, taps, dilated);
    float4 b = tex2Dlod(_ThirdTex, float4(i.uvAux, 0.0, 0.0));
    return float4((a.rgb + b.rgb) * 0.5, a.a);
}

// ---------------------------------------------------------------------------
// Bokeh-path helpers
// ---------------------------------------------------------------------------
float4 PortDofBlurAlphaWeighted(float2 uv)
{
    float2 tx = PortDofTexel();
    float4 center = PortDofTap(uv);
    float gw[4] = { 0.383, 0.242, 0.061, 0.006 };
    float w0 = gw[0] * (abs(center.a) + 1e-3);
    float3 sum = center.rgb * w0;
    float wsum = w0;
    [unroll]
    for (int k = 1; k < 4; k++)
    {
        float2 ofs = _Offsets.xy * k * tx;
        float4 s0 = PortDofTap(uv + ofs);
        float4 s1 = PortDofTap(uv - ofs);
        float wa = gw[k] * (abs(s0.a) + 1e-3);
        float wb = gw[k] * (abs(s1.a) + 1e-3);
        sum += s0.rgb * wa + s1.rgb * wb;
        wsum += wa + wb;
    }
    return float4(sum / wsum, center.a);
}

float4 PortDofBoxBlur(float2 uv)
{
    float2 tx = PortDofTexel();
    float4 s = PortDofTap(uv + float2(-1.0, -1.0) * tx);
    s += PortDofTap(uv + float2(1.0, -1.0) * tx);
    s += PortDofTap(uv + float2(-1.0, 1.0) * tx);
    s += PortDofTap(uv + float2(1.0, 1.0) * tx);
    return s * 0.25;
}

// ---------------------------------------------------------------------------
// Merge: full-res sharp _MainTex with half-res blurred _SecondTex
// ---------------------------------------------------------------------------

// Cubic B-spline upsampling from four bilinear fetches.
float4 PortDofBicubic(float2 uv)
{
    float2 size = _Convolved_TexelSize.xy;
    float2 inv = _Convolved_TexelSize.zw;
    float2 p = uv * size - 0.5;
    float2 f = frac(p);
    float2 base = floor(p);

    float2 f2 = f * f;
    float2 f3 = f2 * f;
    float2 w0 = (1.0 - 3.0 * f + 3.0 * f2 - f3) / 6.0;
    float2 w1 = (4.0 - 6.0 * f2 + 3.0 * f3) / 6.0;
    float2 w2 = (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3) / 6.0;
    float2 w3 = f3 / 6.0;

    float2 g0 = w0 + w1;
    float2 g1 = w2 + w3;
    float2 h0 = (base - 0.5 + w1 / g0) * inv;
    float2 h1 = (base + 1.5 + w3 / g1) * inv;

    float4 t00 = tex2Dlod(_SecondTex, float4(h0.x, h0.y, 0.0, 0.0));
    float4 t10 = tex2Dlod(_SecondTex, float4(h1.x, h0.y, 0.0, 0.0));
    float4 t01 = tex2Dlod(_SecondTex, float4(h0.x, h1.y, 0.0, 0.0));
    float4 t11 = tex2Dlod(_SecondTex, float4(h1.x, h1.y, 0.0, 0.0));
    return g0.y * (g0.x * t00 + g1.x * t10) + g1.y * (g0.x * t01 + g1.x * t11);
}

float4 PortDofMerge(PortDofV2f i, bool isExplicit, bool bicubic)
{
    float4 sharp = PortDofTap(i.uv);
    float4 blurred = bicubic ? PortDofBicubic(i.uvAux)
                             : tex2Dlod(_SecondTex, float4(i.uvAux, 0.0, 0.0));
    float coc = blurred.a;
    if (!isExplicit)
    {
        // The full-resolution depth refines the bilinear half-res CoC:
        // the far side follows it for crisp silhouettes of in-focus objects
        // in front of a blurred background, and near pixels never get less
        // blur than their own depth asks for. _BlurParams still holds the
        // capture values here.
        float full = PortDofCocBasic(PortDofDepth01(i.uvAux));
        if (full < 0.0)
            coc = min(coc, full);
        else if (coc >= 0.0)
            coc = full;
    }
    float r = PortDofRadius(coc);
    float blend = smoothstep(0.25, 1.5, r);
    return float4(lerp(sharp.rgb, blurred.rgb, blend), sharp.a);
}

#endif
