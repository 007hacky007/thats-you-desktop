// From-scratch reimplementation for the port.
// Full screen overlay for FullScreenOverlay: cross-fades the camera image towards a
// tinted overlay texture (usually a screen grab used during scenario transitions).
// With no overlay assigned the "black" default turns this into a fade to black.
Shader "Wish/SimpleOverlay" {
    Properties {
        _MainTex ("Screen Blended", 2D) = "" {}
        _Overlay ("Color", 2D) = "black" {}
        _Mix ("Mix", Range(0, 1)) = 0.5
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _UV_Transform ("UV Transform (2x2 rows)", Vector) = (1, 0, 0, 1)
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma vertex PortWishVertBlit
            #pragma fragment frag
            #pragma target 3.0
            #include "PortWishScreen.cginc"

            sampler2D _Overlay;
            float _Mix;
            fixed4 _Tint;
            float4 _UV_Transform;

            fixed4 frag(PortWishBlitV2F i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                float2 ouv = PortWishApplyUVTransform(i.uvAux, _UV_Transform);
                // The overlay alpha is ignored: screen grabs carry arbitrary alpha.
                fixed3 overlay = tex2D(_Overlay, ouv).rgb * _Tint.rgb;
                src.rgb = lerp(src.rgb, overlay, saturate(_Mix));
                return src;
            }
            ENDCG
        }
    }
    Fallback Off
}
