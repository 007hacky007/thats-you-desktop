// From-scratch reimplementation for the port.
// Shader Forge/SubstanceAOUV2: Standard metallic PBR for Substance style texture sets with a
// separate roughness map. Albedo * tint, metallic map R, smoothness = 1 - roughness map,
// normal map, emission map * tint. The occlusion map is read with the second mesh UV set
// (TEXCOORD1, the "_UV2" unwrap) and mixed in by _Occlusion.
Shader "Shader Forge/SubstanceAOUV2" {
    Properties {
        _AlbedoTint ("Albedo Tint", Color) = (0.5019608, 0.5019608, 0.5019608, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Metallic ("Metallic", 2D) = "black" {}
        _Rougness ("Rougness", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _OcclusionUV1 ("Occlusion (UV1)", 2D) = "white" {}
        _Occlusion ("Occlusion", Range(0, 1)) = 0
        _Emssion ("Emssion", 2D) = "black" {}
        _EmissionTint ("Emission Tint", Color) = (0.5, 0.5, 0.5, 1)
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        #include "PortSFSurface.cginc"

        sampler2D _MainTex;     float4 _MainTex_ST;
        sampler2D _Metallic;    float4 _Metallic_ST;
        sampler2D _Rougness;    float4 _Rougness_ST;
        sampler2D _BumpMap;     float4 _BumpMap_ST;
        sampler2D _Emssion;     float4 _Emssion_ST;
        sampler2D _OcclusionUV1; float4 _OcclusionUV1_ST;
        fixed4 _AlbedoTint;
        fixed4 _EmissionTint;
        half _Occlusion;

        struct Input {
            float4 rawUV;
        };

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUV = PortSF_RawUV(v);
        }

        void surf(Input IN, inout SurfaceOutputStandard o) {
            float2 uv = IN.rawUV.xy;
            fixed4 albedo = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex));
            o.Albedo = albedo.rgb * _AlbedoTint.rgb;
            o.Metallic = tex2D(_Metallic, TRANSFORM_TEX(uv, _Metallic)).r;
            o.Smoothness = 1.0 - tex2D(_Rougness, TRANSFORM_TEX(uv, _Rougness)).r;
            o.Normal = PortSF_UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
            o.Emission = tex2D(_Emssion, TRANSFORM_TEX(uv, _Emssion)).rgb * _EmissionTint.rgb;
            o.Occlusion = PortSF_Occlusion(tex2D(_OcclusionUV1, TRANSFORM_TEX(IN.rawUV.zw, _OcclusionUV1)).r, _Occlusion);
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Standard"
}
