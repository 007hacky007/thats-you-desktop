// From-scratch reimplementation for the port.
// Image effect material for the Flipper component (Graphics.Blit in OnRenderImage): draws the
// camera image upside down, alpha blended over the destination. The Flipper components in
// Scenario_01/05/09 ship disabled; this keeps the material valid if one gets enabled.
Shader "Wish/Flipper" {
    Properties {
        _MainTex ("Screen Blended", 2D) = "" {}
    }

    SubShader {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        LOD 100

        Pass {
            Cull Off
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "PortWishUnlit.cginc"

            sampler2D _MainTex;

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(PortWishUnlitAppData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = float2(v.texcoord.x, 1.0 - v.texcoord.y);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
    Fallback Off
}
