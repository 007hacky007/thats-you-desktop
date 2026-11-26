// From-scratch reimplementation for the port.
// NGUI premultiplied-alpha atlas shader: texture times vertex colour, blended One OneMinusSrcAlpha.
// Clipped by the panel's texture mask (_ClipTex over _ClipRange0). Resolved by name from UIDrawCall.CreateMaterial.
Shader "Hidden/Unlit/Premultiplied Colored (TextureClip)" {
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
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex PortNguiVert
            #pragma fragment PortNguiFrag
            #pragma target 3.0
            #define PORT_NGUI_PREMUL 1
            #define PORT_NGUI_TEXTURE_CLIP 1
            #include "../PortNgui.cginc"
            ENDCG
        }
    }
}
