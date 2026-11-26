// From-scratch reimplementation for the port: "Hidden/ColorCorrectionCurvesSimple".
// Per-channel curves from the 256x4 ramp _RgbTex (row 0 red, row 1 green,
// row 2 blue) built by ColorCorrectionCurves, followed by _Saturation.
Shader "Hidden/ColorCorrectionCurvesSimple" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "" { }
 _RgbTex ("_RgbTex (RGB)", 2D) = "" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxGrade.cginc"

        sampler2D _RgbTex;
        float _Saturation;

        float4 frag(PortGradeV2f i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            c.rgb = PORT_GRADE_CURVES(_RgbTex, c.rgb);
            c.rgb = PortGradeSaturation(c.rgb, _Saturation);
            return c;
        }
        ENDCG
    }
}
Fallback Off
}
