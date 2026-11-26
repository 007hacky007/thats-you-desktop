// From-scratch reimplementation for the port.
// Unlit combine of two textures through a mask, all four channels:
// lerp(_MainTex, _OverTex, _MaskTex * _Over). Each texture keeps its own tiling and offset, which
// UvScroll / UvScrollOver animate. In Scenario_05 a camera renders this into the rain light
// cookie render texture, so the alpha channel is written as well.
Shader "Wish/RawCombineMasked" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _OverTex ("Overlay", 2D) = "black" {}
        _MaskTex ("Texture", 2D) = "white" {}
        _Over ("Overlay Mix", Range(0, 1)) = 1
    }

    SubShader {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        LOD 100

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "PortWishUnlit.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _OverTex;
            float4 _OverTex_ST;
            sampler2D _MaskTex;
            float4 _MaskTex_ST;
            half _Over;

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uvMain : TEXCOORD0;
                float2 uvOver : TEXCOORD1;
                float2 uvMask : TEXCOORD2;
            };

            v2f vert(PortWishUnlitAppData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvMain = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.uvOver = TRANSFORM_TEX(v.texcoord, _OverTex);
                o.uvMask = TRANSFORM_TEX(v.texcoord, _MaskTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 mainCol = tex2D(_MainTex, i.uvMain);
                half4 overCol = tex2D(_OverTex, i.uvOver);
                half4 mask = tex2D(_MaskTex, i.uvMask);
                return lerp(mainCol, overCol, saturate(mask * _Over));
            }
            ENDCG
        }
    }
    Fallback Off
}
