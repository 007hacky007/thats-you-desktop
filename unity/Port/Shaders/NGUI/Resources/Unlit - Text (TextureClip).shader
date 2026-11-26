// From-scratch reimplementation for the port.
// NGUI dynamic font shader: vertex colour with the glyph coverage from texture alpha.
// Clipped by the panel's texture mask (_ClipTex over _ClipRange0). Resolved by name from UIDrawCall.CreateMaterial.
Shader "Hidden/Unlit/Text (TextureClip)" {
    Properties {
       _MainTex ("Alpha (A)", 2D) = "white" {}
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
            #define PORT_NGUI_TEXT 1
            #define PORT_NGUI_TEXTURE_CLIP 1
            #include "../PortNgui.cginc"
            ENDCG
        }
    }
}
