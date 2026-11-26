// From-scratch reimplementation for the port.
// Hidden/postVHSPro_Third: feedback trail accumulation (mat3).
// Single pass, blitted by postVHSPro.OnRenderImage.
Shader "Hidden/postVHSPro_Third" {
    Properties {
        _MainTex ("Render Input", 2D) = "white" {}
        _LastTex ("Render Input", 2D) = "white" {}
        _FeedbackTex ("Render Input", 2D) = "white" {}
    }

    SubShader {
        ZTest Always Cull Off ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex PortVHSVert
            #pragma fragment PortVHSFrag
            #pragma target 3.0
            #define PORT_VHS_THIRD 1
            #include "../PortVHSPro.cginc"
            ENDCG
        }
    }

    Fallback Off
}
