// From-scratch reimplementation for the port: "Hidden/ChromaticAberration".
// Driven by VignetteAndChromaticAberration:
//   Pass 0: plain copy (used to downsample before the vignette blur).
//   Pass 1: simple lateral aberration, red and blue pushed apart radially.
//   Pass 2: advanced mode, lateral aberration plus luminance-dependent axial
//           fringing sampled along the radial direction (_BlurDistance range).
Shader "Hidden/ChromaticAberration" {
Properties {
 _MainTex ("Base", 2D) = "" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "PortFxGrade.cginc"

    float _ChromaticAberration;
    float _AxialAberration;
    float4 _BlurDistance;
    float _Luminance;

    float4 fragCopy(PortGradeV2f i) : SV_Target
    {
        return tex2D(_MainTex, i.uv);
    }

    // Radial displacement (UV units) that grows with distance from centre.
    float2 LateralShift(float2 uv)
    {
        float2 p = (uv - 0.5) * 2.0;
        float r2 = dot(p, p);
        return p * r2 * _ChromaticAberration * PortGradeTexel();
    }

    float4 fragSimple(PortGradeV2f i) : SV_Target
    {
        float2 s = LateralShift(i.uv);
        float4 c = tex2D(_MainTex, i.uv);
        c.r = tex2D(_MainTex, i.uv - s).r;
        c.b = tex2D(_MainTex, i.uv + s).b;
        return c;
    }

    float4 fragAdvanced(PortGradeV2f i) : SV_Target
    {
        float2 s = LateralShift(i.uv);
        float4 c = tex2D(_MainTex, i.uv);
        float3 lateral = float3(tex2D(_MainTex, i.uv - s).r, c.g, tex2D(_MainTex, i.uv + s).b);

        // Axial (longitudinal) aberration: bright neighbours along the radial
        // direction bleed a purple fringe into darker pixels.
        float2 p = (i.uv - 0.5) * 2.0;
        float len = max(length(p), 1e-4);
        float2 dir = (p / len) * PortGradeTexel() * (1.0 + len);
        float lumC = PortGradeLuma(c.rgb);

        float3 fringe = 0.0;
        float wsum = 0.0;
        [unroll]
        for (int k = 0; k < 6; k++)
        {
            float t = lerp(_BlurDistance.x, _BlurDistance.y, (k + 0.5) / 6.0);
            float3 n = tex2D(_MainTex, i.uv + dir * t).rgb;
            float w = saturate((PortGradeLuma(n) - lumC) * _Luminance);
            fringe += n * w;
            wsum += w;
        }
        float3 outc = lateral;
        if (wsum > 1e-4)
        {
            fringe /= wsum;
            float amt = saturate(_AxialAberration * wsum / 6.0);
            outc.rb = lerp(outc.rb, max(outc.rb, fringe.rb), amt);
        }
        return float4(outc, c.a);
    }
    ENDCG

    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragCopy
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragSimple
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragAdvanced
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
