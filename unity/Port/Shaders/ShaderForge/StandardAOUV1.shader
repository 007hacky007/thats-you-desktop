// From-scratch reimplementation for the port.
// Shader Forge/StandardAOUV1: Standard metallic PBR with an extra ambient occlusion map.
// Albedo * tint, metallic map (R metallic, A smoothness), normal map, emission map * tint.
// The occlusion map is read with the first mesh UV set (the meshes using these materials only
// carry one UV set) and mixed in by _Amount.
Shader "Shader Forge/StandardAOUV1" {
    Properties {
        _MainTex ("Albedo", 2D) = "white" {}
        _AlbedoTint ("Albedo Tint", Color) = (0.5019608, 0.5019608, 0.5019608, 1)
        _Metallic ("Metallic", 2D) = "black" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Emssion ("Emssion", 2D) = "black" {}
        _EmissionTint ("Emission Tint", Color) = (0.5, 0.5, 0.5, 1)
        _OcclusionUV1 ("Occlusion (UV1)", 2D) = "white" {}
        _Amount ("Amount", Range(0, 1)) = 0
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
        sampler2D _BumpMap;     float4 _BumpMap_ST;
        sampler2D _Emssion;     float4 _Emssion_ST;
        sampler2D _OcclusionUV1; float4 _OcclusionUV1_ST;
        fixed4 _AlbedoTint;
        fixed4 _EmissionTint;
        half _Amount;

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
            PortSF_MetallicSmoothness(tex2D(_Metallic, TRANSFORM_TEX(uv, _Metallic)), o.Metallic, o.Smoothness);
            o.Normal = PortSF_UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
            o.Emission = tex2D(_Emssion, TRANSFORM_TEX(uv, _Emssion)).rgb * _EmissionTint.rgb;
            o.Occlusion = PortSF_Occlusion(tex2D(_OcclusionUV1, TRANSFORM_TEX(uv, _OcclusionUV1)).r, _Amount);
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Standard"
}
