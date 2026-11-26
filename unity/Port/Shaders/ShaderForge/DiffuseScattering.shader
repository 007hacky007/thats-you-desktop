// From-scratch reimplementation for the port.
// Shader Forge/DiffuseScattering: Standard metallic PBR plus a soft scattering rim for cloth,
// plush toys, food and feathers. A fresnel edge mask (exponent _EdgeTreshlod, scaled by
// _TintBoost) adds _EdgeTint coloured albedo light at grazing angles; _LightDirectionContribution
// focuses that rim towards the lit side. AO map (first UV set) mixed by _AO_Strength, albedo
// self illumination by _Emissive_Amount. Metallic map: R metallic, A smoothness.
Shader "Shader Forge/DiffuseScattering" {
    Properties {
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Color ("Color", Color) = (0.5019608, 0.5019608, 0.5019608, 1)
        _MainTex ("Base Color", 2D) = "white" {}
        _Metallic ("Metallic", 2D) = "white" {}
        _EdgeTint ("EdgeTint", Color) = (1, 0, 0, 1)
        _EdgeTreshlod ("EdgeTreshlod", Range(0, 5)) = 1
        _TintBoost ("TintBoost", Range(1, 50)) = 1
        _AO ("AO", 2D) = "white" {}
        _AO_Strength ("AO_Strength", Range(0, 1)) = 1
        _LightDirectionContribution ("LightDirectionContribution", Range(1, 10)) = 1
        _Emissive_Amount ("Emissive_Amount", Range(0, 1)) = 0
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf PortSFScatter fullforwardshadows vertex:vert
        #pragma target 3.0

        #include "PortSFSurface.cginc"

        sampler2D _MainTex;  float4 _MainTex_ST;
        sampler2D _Metallic; float4 _Metallic_ST;
        sampler2D _BumpMap;  float4 _BumpMap_ST;
        sampler2D _AO;       float4 _AO_ST;
        fixed4 _Color;
        fixed4 _EdgeTint;
        half _EdgeTreshlod;
        half _TintBoost;
        half _AO_Strength;
        half _LightDirectionContribution;
        half _Emissive_Amount;

        struct Input {
            float4 rawUV;
        };

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUV = PortSF_RawUV(v);
        }

        void surf(Input IN, inout SurfaceOutputPortScatter o) {
            float2 uv = IN.rawUV.xy;
            fixed4 albedo = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)) * _Color;
            o.Albedo = albedo.rgb;
            PortSF_MetallicSmoothness(tex2D(_Metallic, TRANSFORM_TEX(uv, _Metallic)), o.Metallic, o.Smoothness);
            o.Normal = PortSF_UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
            o.Occlusion = PortSF_Occlusion(tex2D(_AO, TRANSFORM_TEX(uv, _AO)).r, _AO_Strength);
            o.Emission = albedo.rgb * _Emissive_Amount;
            o.EdgeTint = _EdgeTint.rgb;
            o.EdgeExponent = _EdgeTreshlod;
            o.EdgeBoost = _TintBoost;
            o.LightPower = _LightDirectionContribution;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Standard"
}
