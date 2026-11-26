// From-scratch reimplementation for the port: "Hidden/ColorCorrectionCurves".
// Depth-aware curves for ColorCorrectionCurves (useDepthCorrection): the near
// ramp (_RgbTex) and the far ramp (_RgbDepthTex) are both applied, then blended
// by the 256x1 depth response curve _ZCurve evaluated at linear 0..1 depth.
// _Saturation is applied afterwards.
Shader "Hidden/ColorCorrectionCurves" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "" { }
 _RgbTex ("_RgbTex (RGB)", 2D) = "" { }
 _ZCurve ("_ZCurve (RGB)", 2D) = "" { }
 _RgbDepthTex ("_RgbDepthTex (RGB)", 2D) = "" { }
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
        sampler2D _ZCurve;
        sampler2D _RgbDepthTex;
        float _Saturation;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

        float4 frag(PortGradeV2f i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            float z = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv2));
            float zw = tex2D(_ZCurve, float2(PortGradeCurveU(z), 0.5)).r;
            float3 nearC = PORT_GRADE_CURVES(_RgbTex, c.rgb);
            float3 farC = PORT_GRADE_CURVES(_RgbDepthTex, c.rgb);
            c.rgb = lerp(nearC, farC, saturate(zw));
            c.rgb = PortGradeSaturation(c.rgb, _Saturation);
            return c;
        }
        ENDCG
    }
}
Fallback Off
}
