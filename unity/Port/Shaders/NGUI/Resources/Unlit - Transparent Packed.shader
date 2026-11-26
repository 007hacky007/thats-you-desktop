// From-scratch reimplementation for the port.
// NGUI packed bitmap font shader: the glyph channel is picked from the encoded vertex colour.
// No panel clipping. Resolved by name from UIDrawCall.CreateMaterial.
Shader "Unlit/Transparent Packed" {
    Properties {
       _MainTex ("Base (RGB), Alpha (A)", 2D) = "black" {}
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
            #define PORT_NGUI_PACKED 1
            #define PORT_NGUI_CLIP_COUNT 0
            #include "../PortNgui.cginc"
            ENDCG
        }
    }
}
