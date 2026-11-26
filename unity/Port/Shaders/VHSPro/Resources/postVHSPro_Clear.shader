// From-scratch reimplementation for the port.
// Hidden/postVHSPro_Clear: clears a render texture to transparent black (mat_clear).
// Single pass, blitted by postVHSPro.OnRenderImage.
Shader "Hidden/postVHSPro_Clear" {
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
            #define PORT_VHS_CLEAR 1
            #include "../PortVHSPro.cginc"
            ENDCG
        }
    }

    Fallback Off
}
