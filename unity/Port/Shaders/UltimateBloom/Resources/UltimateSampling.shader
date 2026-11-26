// From-scratch reimplementation for the port: "Hidden/Ultimate/Sampling".
// Downsample and blur passes for UltimateBloom. The pass order is fixed by the
// script (RenderSimple / GaussianBlur1 / GaussianBlur2 / GaussianBlurSeparate):
//  0 four-tap downsample          7 FourSimple 4-tap blur
//  1 nine-tap downsample          8 13-tap gaussian
//  2 31-tap gaussian              9 23-tap gaussian
//  3 17-tap gaussian             10 27-tap gaussian
//  4 9-tap gaussian              11 13-tap temporally stable downsample
//  5 four-tap + filmic curve     12 13-tap temporal + filmic curve
//  6 9-tap gaussian + curve
// Downsamples: _OffsetInfos.xy = source texel size. Blurs: _OffsetInfos.xy is
// the tap step, result = blur * _Tint * _Intensity + _AdditiveTexture.
Shader "Hidden/Ultimate/Sampling" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    // Pass 0: Four-tap downsample
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBDown4(i.uv);
        }
        ENDCG
    }

    // Pass 1: Nine-tap downsample
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBDown9(i.uv);
        }
        ENDCG
    }

    // Pass 2: 31-tap gaussian
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBGauss(i.uv, 15, PORT_UB_SIGMA(15)), i.uv);
        }
        ENDCG
    }

    // Pass 3: 17-tap gaussian
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBGauss(i.uv, 8, PORT_UB_SIGMA(8)), i.uv);
        }
        ENDCG
    }

    // Pass 4: 9-tap gaussian
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBGauss(i.uv, 4, PORT_UB_SIGMA(4)), i.uv);
        }
        ENDCG
    }

    // Pass 5: Four-tap downsample + filmic curve
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBCurve(PortUBDown4(i.uv));
        }
        ENDCG
    }

    // Pass 6: 9-tap gaussian + filmic curve
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBCurve(PortUBGauss(i.uv, 4, PORT_UB_SIGMA(4))), i.uv);
        }
        ENDCG
    }

    // Pass 7: FourSimple 4-tap blur
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBBlur4(i.uv), i.uv);
        }
        ENDCG
    }

    // Pass 8: 13-tap gaussian
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBGauss(i.uv, 6, PORT_UB_SIGMA(6)), i.uv);
        }
        ENDCG
    }

    // Pass 9: 23-tap gaussian
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBGauss(i.uv, 11, PORT_UB_SIGMA(11)), i.uv);
        }
        ENDCG
    }

    // Pass 10: 27-tap gaussian
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBFinishBlur(PortUBGauss(i.uv, 13, PORT_UB_SIGMA(13)), i.uv);
        }
        ENDCG
    }

    // Pass 11: 13-tap temporally stable downsample
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBDown13Temporal(i.uv);
        }
        ENDCG
    }

    // Pass 12: 13-tap temporally stable downsample + filmic curve
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBCurve(PortUBDown13Temporal(i.uv));
        }
        ENDCG
    }
}
Fallback Off
}
