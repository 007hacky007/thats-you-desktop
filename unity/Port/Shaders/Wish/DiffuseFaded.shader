// From-scratch reimplementation for the port.
// Texture that can be faded between Lambert lit (_Mix = 0) and fully unlit (_Mix = 1).
// Used by the LiveFeed_P* TV screens, which show render textures and keep _Mix at 1.
Shader "Wish/Mix Diffuse & UnLit" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Mix ("Diffuse Mix", Range(0, 1)) = 1
    }

    SubShader {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert
        #pragma target 3.0
        #include "PortWishUnlit.cginc"

        sampler2D _MainTex;
        half _Mix;

        struct Input {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            half4 c = tex2D(_MainTex, IN.uv_MainTex);
            half3 albedo;
            half3 emission;
            PortWishSplitLitUnlit(c.rgb, _Mix, albedo, emission);
            o.Albedo = albedo;
            o.Emission = emission;
            o.Alpha = c.a;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Diffuse"
}
