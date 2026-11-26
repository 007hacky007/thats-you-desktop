// From-scratch reimplementation for the port: "Hidden/Dof/DepthOfFieldHdr".
// Depth of field for UnityStandardAssets.ImageEffects.DepthOfField. The circle
// of confusion (CoC, 0..1) lives in the alpha channel: it is computed from the
// camera depth texture with _CurveParams = (1, focalSize, aperture / 10,
// focalDistance01), then the colour is downsampled, gathered over a disc whose
// radius follows the CoC, and blended back over the sharp image.
//
// Conventions taken from the driving script:
//  - disc passes 11/17 read the half-res buffer; _Offsets.w = internalBlurWidth
//    is the largest disc radius in texels of that buffer (reached at CoC 1).
//  - composite passes 9/12/18 read the full-res source; _Offsets.xyzw =
//    (full width / low width) * internalBlurWidth, i.e. the same largest
//    radius expressed in full-res pixels. _LowRez holds the blurred buffer.
//  - separable passes 2/5/19 step along _Offsets.xy (in texels).
//
// Every texture except the depth texture is read through an inline
// linear/clamp sampler so that the result does not depend on the filter mode
// a pooled temporary render texture happens to carry. Dense, bilinear disc
// sampling (tap spacing about one texel) keeps the blur smooth: a sparse fixed
// kernel turns regular high-frequency detail into a moire mesh and ghost
// contours.
//
// Pass map (indices used by the script):
//  0  CoC from depth into alpha (ColorMask A)
//  1  copy
//  2  separable foreground blur + CoC dilation along _Offsets.xy
//  3  foreground overlay onto the blurred buffer (alpha blend, RGB only)
//  4  half-res downsample with foreground-only CoC
//  5  CoC-weighted separable blur along _Offsets.xy
//  6  half-res downsample keeping CoC (2x2 box)
//  7  copy            8  copy            10 copy            14 copy
//  9  composite (tent upsample of _LowRez, foreground from _FgOverlap)
//  11 disc gather, low sample count
//  12 composite (bilinear upsample)
//  13 merge dilated foreground CoC from _FgOverlap into alpha (ColorMask A)
//  15 half-res downsample (2x2 box)
//  16 focus visualisation
//  17 disc gather, high sample count
//  18 composite (tent upsample)
//  19 separable gaussian along _Offsets.xy
Shader "Hidden/Dof/DepthOfFieldHdr" {
Properties {
 _MainTex ("-", 2D) = "black" { }
 _FgOverlap ("-", 2D) = "black" { }
 _LowRez ("-", 2D) = "black" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "UnityCG.cginc"

    Texture2D _MainTex;
    float4 _MainTex_TexelSize;
    Texture2D _FgOverlap;
    Texture2D _LowRez;
    float4 _LowRez_TexelSize;
    SamplerState port_linear_clamp_sampler;

    float4 _CurveParams;
    float4 _Offsets;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    struct DofAppData
    {
        float4 vertex : POSITION;
        float2 texcoord : TEXCOORD0;
    };

    struct DofV2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;     // _MainTex
        float2 uvAux : TEXCOORD1;  // depth and secondary screen-space buffers
    };

    DofV2f DofVert(DofAppData v)
    {
        DofV2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        o.uvAux = v.texcoord;
    #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0)
            o.uvAux.y = 1.0 - o.uvAux.y;
    #endif
        return o;
    }

    float2 DofTexel()
    {
        return abs(_MainTex_TexelSize.xy);
    }

    float4 DofTap(float2 uv)
    {
        return _MainTex.SampleLevel(port_linear_clamp_sampler, uv, 0.0);
    }

    float4 DofLowTap(float2 uv)
    {
        return _LowRez.SampleLevel(port_linear_clamp_sampler, uv, 0.0);
    }

    float4 DofFgTap(float2 uv)
    {
        return _FgOverlap.SampleLevel(port_linear_clamp_sampler, uv, 0.0);
    }

    // HDR sources can hold NaN or overflowed values on isolated specular
    // pixels; one of those would spread into a dark or bright blotch over the
    // whole disc, so the downsample entry points clean them up.
    float4 DofSanitize(float4 c)
    {
        c = (c == c) ? c : 0.0;
        return clamp(c, 0.0, 65000.0);
    }

    // Cleared depth (nothing drawn on the camera's layers) reads as the far
    // plane, Linear01Depth = 1, which gives the full background CoC.
    float DofLinearDepth(float2 uvDepth)
    {
        float raw = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uvDepth, 0.0, 0.0));
        return saturate(Linear01Depth(raw));
    }

    // Thin-lens style CoC: aperture * |d - focus| / d, minus a dead zone of
    // focalSize around the focal plane.
    float DofCoc(float d)
    {
        float coc = _CurveParams.z * abs(d - _CurveParams.w) / max(d, 1e-5);
        return saturate(coc - _CurveParams.y);
    }

    float4 fragCoc(DofV2f i) : SV_Target
    {
        return float4(0.0, 0.0, 0.0, DofCoc(DofLinearDepth(i.uvAux)));
    }

    float4 fragMergeFg(DofV2f i) : SV_Target
    {
        float coc = DofCoc(DofLinearDepth(i.uvAux));
        float fg = DofFgTap(i.uvAux).a;
        return float4(0.0, 0.0, 0.0, max(coc, fg));
    }

    float4 fragCopy(DofV2f i) : SV_Target
    {
        return DofTap(i.uv);
    }

    // 2x2 box: the output pixel centre sits on the corner shared by four source
    // texels, and each tap lands on one of their centres.
    float4 DofDownsample(float2 uv)
    {
        float2 h = DofTexel() * 0.5;
        float4 s = DofSanitize(DofTap(uv + float2(-h.x, -h.y)));
        s += DofSanitize(DofTap(uv + float2( h.x, -h.y)));
        s += DofSanitize(DofTap(uv + float2(-h.x,  h.y)));
        s += DofSanitize(DofTap(uv + float2( h.x,  h.y)));
        return s * 0.25;
    }

    float4 fragDownsample(DofV2f i) : SV_Target
    {
        return DofDownsample(i.uv);
    }

    float4 fragDownsampleFg(DofV2f i) : SV_Target
    {
        float4 c = DofDownsample(i.uv);
        float d = DofLinearDepth(i.uvAux);
        float fg = d < _CurveParams.w ? DofCoc(d) : 0.0;
        return float4(c.rgb, fg);
    }

    static const float kGauss7[4] = { 0.383, 0.242, 0.061, 0.006 };

    // Foreground blur: colour is weighted by its foreground CoC, the CoC itself
    // is dilated so that blurred foreground edges overlap the background.
    float4 fragFgBlur(DofV2f i) : SV_Target
    {
        float2 stp = _Offsets.xy * DofTexel() * 0.5;
        float4 c = DofTap(i.uv);
        float3 sum = c.rgb * kGauss7[0] * (c.a + 0.01);
        float wsum = kGauss7[0] * (c.a + 0.01);
        float dil = c.a;
        [unroll]
        for (int k = 1; k <= 3; k++)
        {
            float4 a = DofTap(i.uv + stp * k);
            float4 b = DofTap(i.uv - stp * k);
            float wa = kGauss7[k] * (a.a + 0.01);
            float wb = kGauss7[k] * (b.a + 0.01);
            sum += a.rgb * wa + b.rgb * wb;
            wsum += wa + wb;
            float fall = 1.0 - k * 0.25;
            dil = max(dil, max(a.a, b.a) * fall);
        }
        return float4(sum / wsum, dil);
    }

    float4 fragFgOverlay(DofV2f i) : SV_Target
    {
        float4 fg = DofTap(i.uv);
        return float4(fg.rgb, saturate(fg.a * 2.0));
    }

    // Separable blur whose radius scales with the centre CoC. A tap counts only
    // as far as its own CoC reaches back to the centre (soft over one step), so
    // sharp pixels do not bleed into blurred ones.
    float4 fragCocBlur(DofV2f i) : SV_Target
    {
        float4 c = DofTap(i.uv);
        float2 stp = _Offsets.xy * DofTexel() * (c.a / 3.0);
        float3 sum = c.rgb * kGauss7[0];
        float wsum = kGauss7[0];
        [unroll]
        for (int k = 1; k <= 3; k++)
        {
            float need = c.a * k / 3.0;
            float4 a = DofTap(i.uv + stp * k);
            float4 b = DofTap(i.uv - stp * k);
            float wa = kGauss7[k] * saturate((a.a - need) * 3.0 + 1.0);
            float wb = kGauss7[k] * saturate((b.a - need) * 3.0 + 1.0);
            sum += a.rgb * wa + b.rgb * wb;
            wsum += wa + wb;
        }
        return float4(sum / wsum, c.a);
    }

    float4 fragGauss(DofV2f i) : SV_Target
    {
        float2 stp = _Offsets.xy * DofTexel() * 0.5;
        float4 sum = DofTap(i.uv) * kGauss7[0];
        [unroll]
        for (int k = 1; k <= 3; k++)
            sum += (DofTap(i.uv + stp * k) + DofTap(i.uv - stp * k)) * kGauss7[k];
        return sum / (kGauss7[0] + 2.0 * (kGauss7[1] + kGauss7[2] + kGauss7[3]));
    }

    // Disc gather over a Vogel (golden-angle) spiral on the half-res buffer.
    // Radius in texels of that buffer = CoC * _Offsets.w. The tap count grows
    // with the disc area so neighbouring bilinear taps stay about `spacing`
    // texels apart; minCount is the floor for small discs.
    // Each tap is weighted by whether its own blur radius reaches the centre
    // (soft over one texel), so in-focus pixels do not smear outwards, and the
    // centre always keeps weight 1 so the sum never collapses.
    float4 DofDisc(float2 uv, float minCount, float maxCount, float spacing)
    {
        float2 tx = DofTexel();
        float4 c = DofTap(uv);
        float rMax = max(_Offsets.w, 0.0);
        float radius = c.a * rMax;
        if (radius < 0.5)
            return c;

        float n = clamp(ceil(3.14159265 * radius * radius / (spacing * spacing)), minCount, maxCount);
        int count = (int)n;
        float3 sum = c.rgb;
        float wsum = 1.0;
        [loop]
        for (int k = 0; k < count; k++)
        {
            float f = (k + 0.5) / n;
            float dist = sqrt(f) * radius;
            float sn, cs;
            sincos(k * 2.39996323, sn, cs);
            float4 s = DofTap(uv + float2(cs, sn) * dist * tx);
            float w = saturate(s.a * rMax - dist + 1.0);
            // Slight falloff towards the rim softens the disc edge.
            w *= 1.0 - 0.25 * f;
            sum += s.rgb * w;
            wsum += w;
        }
        return float4(sum / wsum, c.a);
    }

    float4 fragDiscLow(DofV2f i) : SV_Target
    {
        return DofDisc(i.uv, 12.0, 96.0, 1.4);
    }

    float4 fragDiscHigh(DofV2f i) : SV_Target
    {
        return DofDisc(i.uv, 28.0, 192.0, 1.0);
    }

    float4 DofLowRez(float2 uv, bool tent)
    {
        if (!tent)
            return DofLowTap(uv);
        // Four bilinear taps half a low-res texel apart: a tent filter.
        float2 h = abs(_LowRez_TexelSize.xy) * 0.5;
        float4 s = DofLowTap(uv + float2(-h.x, -h.y));
        s += DofLowTap(uv + float2( h.x, -h.y));
        s += DofLowTap(uv + float2(-h.x,  h.y));
        s += DofLowTap(uv + float2( h.x,  h.y));
        return s * 0.25;
    }

    // Lerp the sharp full-res source towards the upsampled blur by how far the
    // CoC spreads in full-res pixels: fully blurred from two pixels upwards.
    float4 DofCompose(DofV2f i, bool tent, bool useFg)
    {
        float4 c = DofTap(i.uv);
        float coc = c.a;
        if (useFg)
            coc = max(coc, DofFgTap(i.uvAux).a);
        float4 low = DofLowRez(i.uvAux, tent);
        float radiusPx = saturate(coc) * abs(_Offsets.x);
        float blend = smoothstep(0.0, 2.0, radiusPx);
        return float4(lerp(c.rgb, low.rgb, blend), c.a);
    }

    float4 fragComposeSimple(DofV2f i) : SV_Target
    {
        return DofCompose(i, false, false);
    }

    float4 fragComposeTent(DofV2f i) : SV_Target
    {
        return DofCompose(i, true, false);
    }

    float4 fragComposeFg(DofV2f i) : SV_Target
    {
        return DofCompose(i, true, true);
    }

    // In focus = green tint, out of focus = red tint, over a dimmed grey image.
    float4 fragVisualize(DofV2f i) : SV_Target
    {
        float4 c = DofTap(i.uv);
        float g = dot(c.rgb, float3(0.299, 0.587, 0.114)) * 0.4;
        float coc = saturate(c.a);
        return float4(float3(g, g, g) + float3(coc, 1.0 - coc, 0.0) * 0.6, 1.0);
    }
    ENDCG

    // 0
    Pass {
        ColorMask A
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragCoc
        #pragma target 3.5
        ENDCG
    }
    // 1
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragCopy
        #pragma target 3.5
        ENDCG
    }
    // 2
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragFgBlur
        #pragma target 3.5
        ENDCG
    }
    // 3
    Pass {
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragFgOverlay
        #pragma target 3.5
        ENDCG
    }
    // 4
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragDownsampleFg
        #pragma target 3.5
        ENDCG
    }
    // 5
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragCocBlur
        #pragma target 3.5
        ENDCG
    }
    // 6
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragDownsample
        #pragma target 3.5
        ENDCG
    }
    // 7
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragCopy
        #pragma target 3.5
        ENDCG
    }
    // 8
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragCopy
        #pragma target 3.5
        ENDCG
    }
    // 9
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragComposeFg
        #pragma target 3.5
        ENDCG
    }
    // 10
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragCopy
        #pragma target 3.5
        ENDCG
    }
    // 11
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragDiscLow
        #pragma target 3.5
        ENDCG
    }
    // 12
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragComposeSimple
        #pragma target 3.5
        ENDCG
    }
    // 13
    Pass {
        ColorMask A
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragMergeFg
        #pragma target 3.5
        ENDCG
    }
    // 14
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragCopy
        #pragma target 3.5
        ENDCG
    }
    // 15
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragDownsample
        #pragma target 3.5
        ENDCG
    }
    // 16
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragVisualize
        #pragma target 3.5
        ENDCG
    }
    // 17
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragDiscHigh
        #pragma target 3.5
        ENDCG
    }
    // 18
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragComposeTent
        #pragma target 3.5
        ENDCG
    }
    // 19
    Pass {
        CGPROGRAM
        #pragma vertex DofVert
        #pragma fragment fragGauss
        #pragma target 3.5
        ENDCG
    }
}
Fallback Off
}
