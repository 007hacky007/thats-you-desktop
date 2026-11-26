// From-scratch reimplementation for the port.
// Shader Forge/DiffuseScatteringAlpha: alpha tested variant of DiffuseScattering (sweet jars on
// the seaside stalls). Standard metallic PBR plus the fresnel scattering rim (_01_EdgeTint,
// exponent _01_EdgeTreshlod, scaled by _01_TintBoost); albedo alpha is clipped at _Cutoff.
// Metallic map: R metallic, A smoothness.
Shader "Shader Forge/DiffuseScatteringAlpha" {
    Properties {
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Color ("Color", Color) = (0.5019608, 0.5019608, 0.5019608, 1)
        _MainTex ("Base Color", 2D) = "white" {}
        _Metallic ("Metallic", 2D) = "white" {}
        _01_EdgeTint ("01_EdgeTint", Color) = (1, 0, 0, 1)
        _01_EdgeTreshlod ("01_EdgeTreshlod", Range(0, 5)) = 2.5
        _01_TintBoost ("01_TintBoost", Range(0.1, 10)) = 2.5
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "IgnoreProjector" = "True" }
        LOD 300

        CGPROGRAM
        #pragma surface surf PortSFScatter fullforwardshadows vertex:vert alphatest:_Cutoff addshadow
        #pragma target 3.0

        #include "PortSFSurface.cginc"

        sampler2D _MainTex;  float4 _MainTex_ST;
        sampler2D _Metallic; float4 _Metallic_ST;
        sampler2D _BumpMap;  float4 _BumpMap_ST;
        fixed4 _Color;
        fixed4 _01_EdgeTint;
        half _01_EdgeTreshlod;
        half _01_TintBoost;

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
            o.Alpha = albedo.a;
            PortSF_MetallicSmoothness(tex2D(_Metallic, TRANSFORM_TEX(uv, _Metallic)), o.Metallic, o.Smoothness);
            o.Normal = PortSF_UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
            o.Occlusion = 1;
            o.Emission = 0;
            o.EdgeTint = _01_EdgeTint.rgb;
            o.EdgeExponent = _01_EdgeTreshlod;
            o.EdgeBoost = _01_TintBoost;
            o.LightPower = 1;
        }
        ENDCG
    }

    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
