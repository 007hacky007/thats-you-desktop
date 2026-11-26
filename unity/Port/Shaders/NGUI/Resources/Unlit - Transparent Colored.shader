// From-scratch reimplementation for the port.
// NGUI sprite / texture shader: texture times vertex colour, alpha blended.
// No panel clipping. Resolved by name from UIDrawCall.CreateMaterial.
Shader "Unlit/Transparent Colored" {
    Properties {
       _MainTex ("Base (RGB), Alpha (A)", 2D) = "black" {}
       _GreyScale ("Grey Scale", Float) = 0
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
            #define PORT_NGUI_COLORED 1
            #define PORT_NGUI_CLIP_COUNT 0
            #include "../PortNgui.cginc"
            ENDCG
        }
    }
}
