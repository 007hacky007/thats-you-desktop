// From-scratch reimplementation for the port.
// Like "Wish/Mix Diffuse & UnLit", plus an overlay texture laid over the base by its own alpha
// and faded by _Over. DevelopPolaroid animates _Over from 1 (blank white card) to 0 (photo),
// postcards keep a paper grime overlay on top, UvScrollOver / RainControlPM scroll and scale
// the overlay (tiling and offset of _OverTex are honoured).
Shader "Wish/Mix Diffuse & UnLit with Overlay" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _OverTex ("Overlay", 2D) = "black" {}
        _Mix ("Diffuse Mix", Range(0, 1)) = 1
        _Over ("Overlay Mix", Range(0, 1)) = 1
    }

    SubShader {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert
        #pragma target 3.0
        #include "PortWishUnlit.cginc"

        sampler2D _MainTex;
        sampler2D _OverTex;
        half _Mix;
        half _Over;

        struct Input {
            float2 uv_MainTex;
            float2 uv_OverTex;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            half4 c = tex2D(_MainTex, IN.uv_MainTex);
            half4 over = tex2D(_OverTex, IN.uv_OverTex);
            half3 color = PortWishApplyOverlay(c.rgb, over, _Over);
            half3 albedo;
            half3 emission;
            PortWishSplitLitUnlit(color, _Mix, albedo, emission);
            o.Albedo = albedo;
            o.Emission = emission;
            o.Alpha = c.a;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Diffuse"
}
