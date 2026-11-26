// From-scratch reimplementation for the port.
// Hidden/postVHSPro_Forth: adds the feedback trail to the frame (mat4).
// Single pass, blitted by postVHSPro.OnRenderImage.
Shader "Hidden/postVHSPro_Forth" {
    Properties {
        _MainTex ("Render Input", 2D) = "white" {}
        _FeedbackTex ("Render Input", 2D) = "white" {}
    }

    SubShader {
        ZTest Always Cull Off ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex PortVHSVert
            #pragma fragment PortVHSFrag
            #pragma target 3.0
            #define PORT_VHS_FORTH 1
            #include "../PortVHSPro.cginc"
            ENDCG
        }
    }

    Fallback Off
}
