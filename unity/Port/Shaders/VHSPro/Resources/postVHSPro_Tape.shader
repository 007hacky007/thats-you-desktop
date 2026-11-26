// From-scratch reimplementation for the port.
// Hidden/postVHSPro_Tape: tape noise overlay (mat_tape, created but never blitted).
// Single pass, blitted by postVHSPro.OnRenderImage.
Shader "Hidden/postVHSPro_Tape" {
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
            #define PORT_VHS_TAPE 1
            #include "../PortVHSPro.cginc"
            ENDCG
        }
    }

    Fallback Off
}
