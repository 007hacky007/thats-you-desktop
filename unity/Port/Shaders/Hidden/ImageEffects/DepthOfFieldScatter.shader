// From-scratch reimplementation for the port: "Hidden/Dof/DepthOfFieldHdr".
// Depth of field for UnityStandardAssets.ImageEffects.DepthOfField. The circle
// of confusion (CoC) lives in the alpha channel: it is computed from the
// camera depth texture with _CurveParams = (1, focalSize, aperture / 10,
// focalDistance01), then the colour is downsampled, gathered over a disc whose
// radius follows the CoC, and blended back over the sharp image.
//
// Pass map (indices used by the script):
//  0  CoC from depth into alpha (ColorMask A)
//  1  copy
//  2  separable foreground blur + CoC dilation along _Offsets.xy
//  3  foreground overlay onto the blurred buffer (alpha blend, RGB only)
//  4  half-res downsample with foreground-only CoC
//  5  CoC-weighted separable blur along _Offsets.xy
//  6  half-res downsample keeping CoC
//  7  copy            8  copy            10 copy            14 copy
//  9  composite (tent upsample of _LowRez, foreground from _FgOverlap)
//  11 disc gather, low sample count
//  12 composite (single tap)
//  13 merge dilated foreground CoC from _FgOverlap into alpha (ColorMask A)
//  15 half-res downsample (box)
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
    #include "PortFxAA.cginc"

    sampler2D _FgOverlap;
    sampler2D _LowRez;
    float4 _LowRez_TexelSize;
    float4 _CurveParams;
    float4 _Offsets;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    float DofLinearDepth(float2 uvDepth)
    {
        return Linear01Depth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uvDepth, 0.0, 0.0)));
    }

    // Thin-lens style CoC: proportional to aperture * |d - focus| / d, with a
    // dead zone of focalSize around the focal plane.
    float DofCoc(float d)
    {
        float coc = _CurveParams.z * abs(d - _CurveParams.w) / (d + 1e-5);
        return saturate(coc - _CurveParams.y);
    }

    float4 fragCoc(PortFxV2f i) : SV_Target
    {
        return float4(0.0, 0.0, 0.0, DofCoc(DofLinearDepth(i.uvAux)));
    }

    float4 fragMergeFg(PortFxV2f i) : SV_Target
    {
        float coc = DofCoc(DofLinearDepth(i.uvAux));
        float fg = tex2Dlod(_FgOverlap, float4(i.uvAux, 0.0, 0.0)).a;
        return float4(0.0, 0.0, 0.0, max(coc, fg));
    }

    float4 DofDownsample(float2 uv)
    {
        float2 tx = PortFxTexel();
        float4 s = PortFxTap(uv + float2(-0.5, -0.5) * tx);
        s += PortFxTap(uv + float2( 0.5, -0.5) * tx);
        s += PortFxTap(uv + float2(-0.5,  0.5) * tx);
        s += PortFxTap(uv + float2( 0.5,  0.5) * tx);
        return s * 0.25;
    }

    float4 fragDownsample(PortFxV2f i) : SV_Target
    {
        return DofDownsample(i.uv);
    }

    float4 fragDownsampleFg(PortFxV2f i) : SV_Target
    {
        float4 c = DofDownsample(i.uv);
        float d = DofLinearDepth(i.uvAux);
        float fg = d < _CurveParams.w ? DofCoc(d) : 0.0;
        return float4(c.rgb, fg);
    }

    static const float kGauss7[4] = { 0.383, 0.242, 0.061, 0.006 };

    // Foreground blur: colour is weighted by its foreground CoC, the CoC itself
    // is dilated so that blurred foreground edges overlap the background.
    float4 fragFgBlur(PortFxV2f i) : SV_Target
    {
        float2 stp = _Offsets.xy * PortFxTexel() * 0.5;
        float4 c = PortFxTap(i.uv);
        float3 sum = c.rgb * kGauss7[0] * (c.a + 0.01);
        float wsum = kGauss7[0] * (c.a + 0.01);
        float dil = c.a;
        [unroll]
        for (int k = 1; k <= 3; k++)
        {
            float4 a = PortFxTap(i.uv + stp * k);
            float4 b = PortFxTap(i.uv - stp * k);
            float wa = kGauss7[k] * (a.a + 0.01);
            float wb = kGauss7[k] * (b.a + 0.01);
            sum += a.rgb * wa + b.rgb * wb;
            wsum += wa + wb;
            float fall = 1.0 - k * 0.25;
            dil = max(dil, max(a.a, b.a) * fall);
        }
        return float4(sum / wsum, dil);
    }

    float4 fragFgOverlay(PortFxV2f i) : SV_Target
    {
        float4 fg = PortFxTap(i.uv);
        return float4(fg.rgb, saturate(fg.a * 2.0));
    }

    // Blur whose radius scales with the centre CoC; taps that are much sharper
    // than the centre are rejected so in-focus pixels do not bleed outwards.
    float4 fragCocBlur(PortFxV2f i) : SV_Target
    {
        float4 c = PortFxTap(i.uv);
        float2 stp = _Offsets.xy * PortFxTexel() * (c.a / 3.0);
        float3 sum = c.rgb * kGauss7[0];
        float wsum = kGauss7[0];
        [unroll]
        for (int k = 1; k <= 3; k++)
        {
            float need = c.a * k / 3.0 * 0.5;
            float4 a = PortFxTap(i.uv + stp * k);
            float4 b = PortFxTap(i.uv - stp * k);
            float wa = kGauss7[k] * saturate((a.a - need) * 8.0 + 1.0);
            float wb = kGauss7[k] * saturate((b.a - need) * 8.0 + 1.0);
            sum += a.rgb * wa + b.rgb * wb;
            wsum += wa + wb;
        }
        return float4(sum / wsum, c.a);
    }

    float4 fragGauss(PortFxV2f i) : SV_Target
    {
        float2 stp = _Offsets.xy * PortFxTexel() * 0.5;
        float4 sum = PortFxTap(i.uv) * kGauss7[0];
        [unroll]
        for (int k = 1; k <= 3; k++)
            sum += (PortFxTap(i.uv + stp * k) + PortFxTap(i.uv - stp * k)) * kGauss7[k];
        return sum / (kGauss7[0] + 2.0 * (kGauss7[1] + kGauss7[2] + kGauss7[3]));
    }

    // Disc gather on a golden-angle spiral. Radius in texels = 2 * _Offsets.w * CoC.
    float4 DofDisc(float2 uv, int count)
    {
        float2 tx = PortFxTexel();
        float4 c = PortFxTap(uv);
        float coc = c.a;
        float radius = 2.0 * _Offsets.w * coc;
        if (radius < 0.5)
            return c;
        float3 sum = c.rgb;
        float wsum = 1.0;
        [loop]
        for (int k = 0; k < count; k++)
        {
            float r = sqrt((k + 0.5) / count);
            float ang = k * 2.39996323;
            float sn, cs;
            sincos(ang, sn, cs);
            float4 s = PortFxTap(uv + float2(cs, sn) * (r * radius) * tx);
            float w = saturate((s.a - coc * r * 0.5) * 8.0 + 1.0);
            sum += s.rgb * w;
            wsum += w;
        }
        return float4(sum / wsum, coc);
    }

    float4 fragDiscLow(PortFxV2f i) : SV_Target
    {
        return DofDisc(i.uv, 12);
    }

    float4 fragDiscHigh(PortFxV2f i) : SV_Target
    {
        return DofDisc(i.uv, 28);
    }

    float4 DofLowRez(float2 uv, bool tent)
    {
        if (!tent)
            return tex2Dlod(_LowRez, float4(uv, 0.0, 0.0));
        float2 ltx = abs(_LowRez_TexelSize.xy) * 0.5;
        float4 s = tex2Dlod(_LowRez, float4(uv + float2(-ltx.x, -ltx.y), 0.0, 0.0));
        s += tex2Dlod(_LowRez, float4(uv + float2( ltx.x, -ltx.y), 0.0, 0.0));
        s += tex2Dlod(_LowRez, float4(uv + float2(-ltx.x,  ltx.y), 0.0, 0.0));
        s += tex2Dlod(_LowRez, float4(uv + float2( ltx.x,  ltx.y), 0.0, 0.0));
        return s * 0.25;
    }

    float4 DofCompose(PortFxV2f i, bool tent, bool useFg)
    {
        float4 c = PortFxTap(i.uv);
        float coc = c.a;
        if (useFg)
            coc = max(coc, tex2Dlod(_FgOverlap, float4(i.uvAux, 0.0, 0.0)).a);
        float4 low = DofLowRez(i.uvAux, tent);
        float blend = smoothstep(0.0, 0.4, coc);
        return float4(lerp(c.rgb, low.rgb, blend), c.a);
    }

    float4 fragComposeSimple(PortFxV2f i) : SV_Target
    {
        return DofCompose(i, false, false);
    }

    float4 fragComposeTent(PortFxV2f i) : SV_Target
    {
        return DofCompose(i, true, false);
    }

    float4 fragComposeFg(PortFxV2f i) : SV_Target
    {
        return DofCompose(i, true, true);
    }

    // In focus = green tint, out of focus = red tint, over a dimmed grey image.
    float4 fragVisualize(PortFxV2f i) : SV_Target
    {
        float4 c = PortFxTap(i.uv);
        float g = PortFxLuma(c.rgb) * 0.4;
        float coc = saturate(c.a);
        return float4(float3(g, g, g) + float3(coc, 1.0 - coc, 0.0) * 0.6, 1.0);
    }
    ENDCG

    // 0
    Pass {
        ColorMask A
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragCoc
        #pragma target 3.0
        ENDCG
    }
    // 1
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment PortFxCopy
        #pragma target 3.0
        ENDCG
    }
    // 2
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragFgBlur
        #pragma target 3.0
        ENDCG
    }
    // 3
    Pass {
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragFgOverlay
        #pragma target 3.0
        ENDCG
    }
    // 4
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragDownsampleFg
        #pragma target 3.0
        ENDCG
    }
    // 5
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragCocBlur
        #pragma target 3.0
        ENDCG
    }
    // 6
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragDownsample
        #pragma target 3.0
        ENDCG
    }
    // 7
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment PortFxCopy
        #pragma target 3.0
        ENDCG
    }
    // 8
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment PortFxCopy
        #pragma target 3.0
        ENDCG
    }
    // 9
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragComposeFg
        #pragma target 3.0
        ENDCG
    }
    // 10
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment PortFxCopy
        #pragma target 3.0
        ENDCG
    }
    // 11
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragDiscLow
        #pragma target 3.0
        ENDCG
    }
    // 12
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragComposeSimple
        #pragma target 3.0
        ENDCG
    }
    // 13
    Pass {
        ColorMask A
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragMergeFg
        #pragma target 3.0
        ENDCG
    }
    // 14
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment PortFxCopy
        #pragma target 3.0
        ENDCG
    }
    // 15
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragDownsample
        #pragma target 3.0
        ENDCG
    }
    // 16
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragVisualize
        #pragma target 3.0
        ENDCG
    }
    // 17
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragDiscHigh
        #pragma target 3.0
        ENDCG
    }
    // 18
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragComposeTent
        #pragma target 3.0
        ENDCG
    }
    // 19
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragGauss
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
