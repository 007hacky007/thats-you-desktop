// From-scratch reimplementation for the port.
// Hidden/Deluxe/EyeAdaptationBright: applies and visualises the brightness DeluxeEyeAdaptation measures.
// Pass order follows DeluxeEyeAdaptation.OnRenderImage:
//   0 = exposure: colour * _BrightnessMultiplier / adapted brightness (1x1 _BrightnessTex)
//   1 = downsample: four bilinear taps of _UpTex, _PixelSize (half a source texel) apart
//   2 = debug overlay: _ColorTex with the _Histogram bins drawn as bars inside the _MinMax rect
Shader "Hidden/Deluxe/EyeAdaptationBright" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    sampler2D _BrightnessTex;
    sampler2D _UpTex;
    sampler2D _ColorTex;
    sampler2D _Histogram;
    float _BrightnessMultiplier;
    float4 _PixelSize;
    float4 _HistogramCoefs;
    float4 _MinMax;         // x = left, y = right, z = bottom, w = top (UV)
    float _LuminanceRange;
    float _TotalPixelNumber;

    float PortEA_Brightness()
    {
        return max(tex2Dlod(_BrightnessTex, float4(0.5, 0.5, 0, 0)).r, 1e-4);
    }

    float4 fragExposure(v2f_img i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        c.rgb *= _BrightnessMultiplier / PortEA_Brightness();
        return c;
    }

    float4 fragDownsample(v2f_img i) : SV_Target
    {
        float2 d = _PixelSize.xy;
        float4 c = tex2D(_UpTex, i.uv + float2(-d.x, -d.y));
        c += tex2D(_UpTex, i.uv + float2( d.x, -d.y));
        c += tex2D(_UpTex, i.uv + float2(-d.x,  d.y));
        c += tex2D(_UpTex, i.uv + float2( d.x,  d.y));
        return c * 0.25;
    }

    float4 fragHistogram(v2f_img i) : SV_Target
    {
        float4 c = tex2D(_ColorTex, i.uv);
        float2 lo = _MinMax.xz;
        float2 hi = _MinMax.yw;
        float2 p = (i.uv - lo) / max(hi - lo, 1e-5);
        if (any(p < 0.0) || any(p > 1.0))
            return c;

        const float bins = 64.0;
        float bin = min(floor(p.x * bins), bins - 1.0);
        float n = tex2Dlod(_Histogram, float4((bin + 0.5) / bins, 0.5, 0, 0)).r;
        // Bar height: share of all pixels in this bin, stretched so a typical frame is readable.
        float h = saturate(sqrt(n / max(_TotalPixelNumber, 1.0)) * 2.0);

        float3 o = lerp(c.rgb, float3(0, 0, 0), 0.6);
        if (p.y <= h)
            o = float3(0.85, 0.85, 0.85);

        // Marker at the bin of the adapted brightness.
        float t = saturate(log2(PortEA_Brightness()) * _HistogramCoefs.x + _HistogramCoefs.y);
        if (abs(p.x - t) < 0.5 / bins)
            o = float3(1.0, 0.35, 0.1);
        // Marker at the bin of the tonemapper white point.
        float tw = saturate(log2(max(_LuminanceRange, 1e-4)) * _HistogramCoefs.x + _HistogramCoefs.y);
        if (abs(p.x - tw) < 0.25 / bins)
            o = float3(0.2, 0.6, 1.0);
        return float4(o, c.a);
    }
    ENDCG

    SubShader {
        ZTest Always Cull Off ZWrite Off

        // Pass 0: exposure
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragExposure
            #pragma target 3.0
            ENDCG
        }

        // Pass 1: downsample
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragDownsample
            #pragma target 3.0
            ENDCG
        }

        // Pass 2: histogram overlay
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragHistogram
            #pragma target 3.0
            ENDCG
        }
    }

    Fallback Off
}
