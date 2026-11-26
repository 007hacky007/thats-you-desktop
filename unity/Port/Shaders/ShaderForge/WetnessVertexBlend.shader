// From-scratch reimplementation for the port.
// Beer mats soaked where drinks were spilled. The red vertex colour paints the wet areas; the
// Mix Mask Noise breaks the edge up into a blotchy soak line (Edge Hardness sharpens it).
// Dry areas use Albedo x White; wet areas are darkened (Darkening, tinted by Darken Tint) and
// made glossier (Wetness pulls smoothness towards 1). Metallic comes from the Metallic slider,
// smoothness from the alpha of the Metalic map.
Shader "Shader Forge/WetnessVertexBlend" {
    Properties {
        _Metallic ("Metallic", Range(0,1)) = 0
        _Albedo ("Albedo ", 2D) = "white" {}
        _Normal ("Normal ", 2D) = "bump" {}
        _MixMaskNoise ("Mix Mask Noise", 2D) = "white" {}
        _EdgeHardness ("Edge Hardness", Range(0.8,20)) = 0.8
        _Metalic ("Metalic", 2D) = "white" {}
        _DarkenTint ("Darken Tint", Color) = (0.5,0.5,0.5,1)
        _White ("White", Color) = (0.5,0.5,0.5,1)
        _Darkening ("Darkening", Range(0,2)) = 0.281764
        _Wetness ("Wetness", Range(0,1)) = 0
    }

    SubShader {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #include "PortSFGlassWet.cginc"

        sampler2D _Albedo;       float4 _Albedo_ST;
        sampler2D _Normal;       float4 _Normal_ST;
        sampler2D _MixMaskNoise; float4 _MixMaskNoise_ST;
        sampler2D _Metalic;      float4 _Metalic_ST;
        half _Metallic;
        half _EdgeHardness;
        half4 _DarkenTint;
        half4 _White;
        half _Darkening;
        half _Wetness;

        struct Input {
            float2 baseUV;
            float4 color : COLOR;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.baseUV = v.texcoord.xy;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.baseUV;
            half3 albedo = tex2D(_Albedo, TRANSFORM_TEX(uv, _Albedo)).rgb;
            half4 ms = tex2D(_Metalic, TRANSFORM_TEX(uv, _Metalic));
            half noise = tex2D(_MixMaskNoise, TRANSFORM_TEX(uv, _MixMaskNoise)).r;
            half wet = PortSF_HeightBlend(saturate(IN.color.r), noise, _EdgeHardness);

            half3 dry = albedo * _White.rgb;
            half3 soaked = albedo * _DarkenTint.rgb / (1.0h + _Darkening);
            o.Albedo = lerp(dry, soaked, wet);
            o.Metallic = _Metallic;
            o.Smoothness = lerp(ms.a, lerp(ms.a, 1.0h, _Wetness), wet);
            o.Normal = UnpackNormal(tex2D(_Normal, TRANSFORM_TEX(uv, _Normal)));
            o.Alpha = 1.0h;
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Diffuse"
}
