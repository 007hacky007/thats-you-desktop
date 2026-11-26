// From-scratch reimplementation for the port.
// Hidden/Amplify Color/Blend: grades the frame through _RgbBlendCacheTex, the LUT pre-lerped by BlendCache.
// Pass order follows AmplifyColorBase: 0 = LDR camera, 1 = HDR camera.
Shader "Hidden/Amplify Color/Blend" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    SubShader {
        ZTest Always Cull Off ZWrite Off

        // Pass 0: LDR camera
        Pass {
            CGPROGRAM
            #pragma vertex PortACVert
            #pragma fragment PortACGrade
            #pragma target 3.0
            #pragma multi_compile AC_QUALITY_STANDARD AC_QUALITY_MOBILE
            #define PORT_AC_BLEND 1
            #include "../PortAmplifyColor.cginc"
            ENDCG
        }

        // Pass 1: HDR camera (input clamped to [0, 1])
        Pass {
            CGPROGRAM
            #pragma vertex PortACVert
            #pragma fragment PortACGrade
            #pragma target 3.0
            #pragma multi_compile AC_QUALITY_STANDARD AC_QUALITY_MOBILE
            #define PORT_AC_BLEND 1
            #define PORT_AC_HDR 1
            #include "../PortAmplifyColor.cginc"
            ENDCG
        }
    }

    Fallback Off
}
