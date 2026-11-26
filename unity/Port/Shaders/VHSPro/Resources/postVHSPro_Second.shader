// From-scratch reimplementation for the port.
// Hidden/postVHSPro_Second: bleed, signal tweak, vignette and YIQ to RGB (mat2).
// Single pass, blitted by postVHSPro.OnRenderImage.
Shader "Hidden/postVHSPro_Second" {
    Properties {
        _MainTex ("Render Input", 2D) = "white" {}
        _CurvesTex ("Render Input", 2D) = "white" {}
    }

    SubShader {
        ZTest Always Cull Off ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex PortVHSVert
            #pragma fragment PortVHSFrag
            #pragma target 3.0
            #pragma multi_compile_local __ VHS_BLEED_ON
            #pragma multi_compile_local __ VHS_CUSTOM_BLEED_ON
            #pragma multi_compile_local __ VHS_DEBUG_BLEEDING_ON
            #pragma multi_compile_local __ VHS_SIGNAL_TWEAK_ON
            #pragma multi_compile_local __ VHS_VIGNETTE_ON
            #define PORT_VHS_SECOND 1
            #include "../PortVHSPro.cginc"
            ENDCG
        }
    }

    Fallback Off
}
