// From-scratch reimplementation for the port.
// Hidden/Deluxe/EyeAdaptation: luminance histogram and adapted brightness for DeluxeEyeAdaptation.
// Pass order follows DeluxeEyeAdaptationLogic.RenderHistogram:
//   0 = histogram accumulation. DrawMeshNow draws one thin clip-space triangle per pixel of
//       _FrameTex (its UV is the pixel centre); the vertex shader reads that pixel, picks a bin
//       from the log2 luminance and slides the triangle onto that bin's texel of the 64x1 float
//       target. Additive blending turns the overlapping triangles into per-bin pixel counts.
//   1 = brightness. Reads the 64 bins of _HistogramTex, averages the luminance of the pixels that
//       lie between the _90PixelCount and _98PixelCount ranks, applies the exposure offset and
//       limits, and eases from _PreviousBrightness towards it. Output goes to a 1x1 float target.
Shader "Hidden/Deluxe/EyeAdaptation" {
    Properties {
        _FrameTex ("Base (RGB) Trans (A)", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    static const float3 PORT_EA_LUMA = float3(0.3, 0.59, 0.11);
    ENDCG

    SubShader {
        ZTest Always Cull Off ZWrite Off

        // Pass 0: histogram accumulation
        Pass {
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            sampler2D _FrameTex;
            float4 _HistogramCoefs; // x = 1 / log2 range, y = -log2(min) * x, z = min luminance, w = range
            float _StepSize;        // clip-space width of one bin
            float _BinCount;

            struct appdata {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 c = tex2Dlod(_FrameTex, float4(v.texcoord, 0, 0)).rgb;
                float lum = dot(max(c, 0.0), PORT_EA_LUMA);
                float t = saturate(log2(max(lum, 1e-8)) * _HistogramCoefs.x + _HistogramCoefs.y);
                float bin = min(floor(t * _BinCount), _BinCount - 1.0);
                // The mesh is already in clip space; z sits inside the depth range on every API.
                o.pos = float4(v.vertex.x + bin * _StepSize, v.vertex.y, 0.5, 1.0);
                // Pixels darker than the histogram floor are not counted: push them off target.
                if (lum < _HistogramCoefs.z)
                    o.pos.x = -4.0;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(1, 1, 1, 1);
            }
            ENDCG
        }

        // Pass 1: adapted brightness
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            sampler2D _HistogramTex;
            sampler2D _PreviousBrightness;
            float _90PixelCount;
            float _98PixelCount;
            float4 _MinMaxSpeedDt; // x = min brightness, y = max brightness, z = speed down * dt, w = speed up * dt
            float _ExposureOffset;  // already negated by the script, in stops
            float _ValueRange;
            float4 _Coefs;          // same as _HistogramCoefs
            float _BinCount;

            float4 frag(v2f_img i) : SV_Target
            {
                const int bins = 64;
                float rank = 0.0;
                float weight = 0.0;
                float sum = 0.0;
                for (int b = 0; b < bins; b++)
                {
                    float u = (b + 0.5) / bins;
                    float n = tex2Dlod(_HistogramTex, float4(u, 0.5, 0, 0)).r;
                    float lo = rank;
                    rank += n;
                    float w = max(0.0, min(rank, _98PixelCount) - max(lo, _90PixelCount));
                    float lum = exp2((u - _Coefs.y) / max(_Coefs.x, 1e-6));
                    weight += w;
                    sum += w * lum;
                }

                float previous = tex2Dlod(_PreviousBrightness, float4(0.5, 0.5, 0, 0)).r;
                float target = (weight > 0.0) ? (sum / weight) : previous;
                target *= exp2(_ExposureOffset);
                target = clamp(target, _MinMaxSpeedDt.x, _MinMaxSpeedDt.y);

                float speed = (target > previous) ? _MinMaxSpeedDt.w : _MinMaxSpeedDt.z;
                float result = lerp(previous, target, 1.0 - exp(-max(speed, 0.0)));
                if (!(result == result))
                    result = target; // recover from a NaN history
                return float4(result, result, result, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
