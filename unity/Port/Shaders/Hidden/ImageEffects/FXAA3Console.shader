// From-scratch reimplementation for the port: "Hidden/FXAA III (Console)".
// Fast luma-edge FXAA driven by _EdgeThresholdMin, _EdgeThreshold and
// _EdgeSharpness, as set by UnityStandardAssets.ImageEffects.Antialiasing.
Shader "Hidden/FXAA III (Console)" {
Properties {
 _MainTex ("-", 2D) = "white" { }
 _EdgeThresholdMin ("Edge threshold min", Float) = 0.125
 _EdgeThreshold ("Edge Threshold", Float) = 0.25
 _EdgeSharpness ("Edge sharpness", Float) = 4
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxAA.cginc"

        float _EdgeThresholdMin;
        float _EdgeThreshold;
        float _EdgeSharpness;

        float4 frag(PortFxV2f i) : SV_Target
        {
            return PortFxaaFast(i.uv, PortFxTexel(), _EdgeThresholdMin, _EdgeThreshold, _EdgeSharpness);
        }
        ENDCG
    }
}
Fallback Off
}
