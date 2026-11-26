// From-scratch reimplementation for the port: "Hidden/ContrastComposite".
// Unsharp-mask composite for ContrastEnhance: the difference between the frame
// and its blurred copy (_MainTexBlurred) is amplified by "intensity" wherever
// its luminance exceeds "threshold".
Shader "Hidden/ContrastComposite" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "" { }
 _MainTexBlurred ("Base Blurred (RGB)", 2D) = "" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxGrade.cginc"

        sampler2D _MainTexBlurred;
        float intensity;
        float threshold;

        float4 frag(PortGradeV2f i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            float4 b = tex2D(_MainTexBlurred, i.uv2);
            float3 d = c.rgb - b.rgb;
            float detail = abs(PortGradeLuma(d));
            float gate = saturate((detail - threshold) * 64.0);
            if (threshold <= 0.0)
                gate = 1.0;
            c.rgb = max(c.rgb + d * intensity * gate, 0.0);
            return c;
        }
        ENDCG
    }
}
Fallback Off
}
