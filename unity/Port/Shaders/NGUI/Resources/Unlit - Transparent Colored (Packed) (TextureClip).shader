// From-scratch reimplementation for the port.
// NGUI packed bitmap font shader: the glyph channel is picked from the encoded vertex colour.
// Clipped by the panel's texture mask (_ClipTex over _ClipRange0). Resolved by name from UIDrawCall.CreateMaterial.
Shader "Unlit/Transparent Colored (Packed) (TextureClip)" {
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
            #define PORT_NGUI_TEXTURE_CLIP 1
            #include "../PortNgui.cginc"
            ENDCG
        }
    }
}
