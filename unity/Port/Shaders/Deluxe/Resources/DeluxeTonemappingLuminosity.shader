// From-scratch reimplementation for the port.
// Hidden/Deluxe/TonemapperLuminosity: filmic curve applied to luminance, colour scaled by the ratio, then tinted.
// DeluxeTonemapper blits with a single pass (pass 0).
Shader "Hidden/Deluxe/TonemapperLuminosity" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Toe ("Toe", Vector) = (0.3,0.59,0.11,1)
        _Shoulder ("Shoulder", Vector) = (0.3,0.59,0.11,1)
        _K ("K", Float) = 0.2
        _Crossover ("Crossover", Float) = 0.2
        _Tint ("Tint", Color) = (1,1,1,1)
    }

    SubShader {
        ZTest Always Cull Off ZWrite Off

        // Pass 0: tonemap
        Pass {
            CGPROGRAM
            #pragma vertex PortDeluxeVert
            #pragma fragment PortDeluxeFrag
            #pragma target 3.0
            #define PORT_DELUXE_MODE 1
            #include "../PortDeluxe.cginc"
            ENDCG
        }
    }

    Fallback Off
}
