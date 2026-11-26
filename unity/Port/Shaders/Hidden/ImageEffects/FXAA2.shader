// From-scratch reimplementation for the port: "Hidden/FXAA II".
// Single-pass luma-edge FXAA with fixed thresholds (the script sets no uniforms).
Shader "Hidden/FXAA II" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxAA.cginc"

        float4 frag(PortFxV2f i) : SV_Target
        {
            return PortFxaaFast(i.uv, PortFxTexel(), 1.0 / 32.0, 1.0 / 8.0, 8.0);
        }
        ENDCG
    }
}
Fallback Off
}
