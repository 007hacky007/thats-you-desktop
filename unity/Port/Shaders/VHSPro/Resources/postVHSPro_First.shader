// From-scratch reimplementation for the port.
// Hidden/postVHSPro_First: RGB to YIQ with scanline quantisation and optional signal noise (mat1).
// Single pass, blitted by postVHSPro.OnRenderImage.
Shader "Hidden/postVHSPro_First" {
    Properties {
        _MainTex ("Render Input", 2D) = "white" {}
    }

    SubShader {
        ZTest Always Cull Off ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex PortVHSVert
            #pragma fragment PortVHSFrag
            #pragma target 3.0
            #pragma multi_compile_local __ VHS_YIQNOISE_ON
            #define PORT_VHS_FIRST 1
            #include "../PortVHSPro.cginc"
            ENDCG
        }
    }

    Fallback Off
}
