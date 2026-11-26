// From-scratch reimplementation for the port.
// NGUI masked sprite shader: texture times vertex colour, alpha also multiplied by _Mask alpha.
// No panel clipping. Resolved by name from UIDrawCall.CreateMaterial.
Shader "Unlit/Transparent Masked" {
    Properties {
       _MainTex ("Base (RGB), Alpha (A)", 2D) = "black" {}
       _Mask ("Alpha (A)", 2D) = "white" {}
    }

    SubShader {
        LOD 100
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Pass {
            Cull Off
            Lighting Off
            ZWrite Off
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex PortNguiVert
            #pragma fragment PortNguiFrag
            #pragma target 3.0
            #define PORT_NGUI_MASKED 1
            #define PORT_NGUI_CLIP_COUNT 0
            #include "../PortNgui.cginc"
            ENDCG
        }
    }
}
