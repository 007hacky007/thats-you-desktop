// From-scratch reimplementation for the port: "Hidden/ColorCorrectionSelective".
// Selective colour replacement for ColorCorrectionCurves (selectiveCc): pixels
// close to "selColor" are pulled towards "targetColor", with a soft falloff so
// near matches are shifted proportionally and distant colours stay unchanged.
Shader "Hidden/ColorCorrectionSelective" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxGrade.cginc"

        float4 selColor;
        float4 targetColor;

        float4 frag(PortGradeV2f i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            float d = distance(c.rgb, selColor.rgb);
            float w = 1.0 - smoothstep(0.05, 0.45, d);
            // Move the matched pixel by the colour delta so shading survives.
            float3 shifted = saturate(c.rgb + (targetColor.rgb - selColor.rgb));
            c.rgb = lerp(c.rgb, shifted, w);
            return c;
        }
        ENDCG
    }
}
Fallback Off
}
