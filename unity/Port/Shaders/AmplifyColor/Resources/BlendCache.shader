// From-scratch reimplementation for the port.
// Hidden/Amplify Color/BlendCache: writes lerp(_RgbTex, _LerpRgbTex, _lerpAmount) per texel into
// the 1024x32 blend cache LUT that the Blend shaders then sample. Single pass.
Shader "Hidden/Amplify Color/BlendCache" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    SubShader {
        ZTest Always Cull Off ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex PortACVert
            #pragma fragment PortACBlendCache
            #pragma target 3.0
            #pragma multi_compile AC_QUALITY_STANDARD AC_QUALITY_MOBILE
            #include "../PortAmplifyColor.cginc"
            ENDCG
        }
    }

    Fallback Off
}
