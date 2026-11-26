// From-scratch reimplementation for the port.
// Shader Forge/StandardDetailMask: Standard metallic PBR with a masked, separately tiled detail
// layer (used for the patterned table top). The detail albedo and detail normal are tiled by
// _2Tliing (xy tiling, zw offset) and laid over the base where _DetailMask is white. The AO map
// (first UV set) is mixed in by _AOAmount. Base metallic map: R metallic, A smoothness.
Shader "Shader Forge/StandardDetailMask" {
    Properties {
        _MainTex ("Base Albedo", 2D) = "white" {}
        _BaseTint ("BaseTint", Color) = (1, 1, 1, 1)
        _BaseMetallic ("BaseMetallic", 2D) = "black" {}
        _BumpMap ("Base Normal Map", 2D) = "bump" {}
        _Detail ("Detail", 2D) = "black" {}
        _2Tliing ("2 - Tliing", Vector) = (1, 1, 0, 0)
        _AO ("AO", 2D) = "white" {}
        _AOAmount ("AO Amount", Range(0, 1)) = 1
        _DetailNormal ("Detail Normal", 2D) = "bump" {}
        _DetailNormalAmount ("Detail-Normal Amount", Range(0, 1)) = 1
        _DetailMask ("DetailMask", 2D) = "white" {}
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        #include "PortSFSurface.cginc"

        sampler2D _MainTex;      float4 _MainTex_ST;
        sampler2D _BaseMetallic; float4 _BaseMetallic_ST;
        sampler2D _BumpMap;      float4 _BumpMap_ST;
        sampler2D _Detail;       float4 _Detail_ST;
        sampler2D _AO;           float4 _AO_ST;
        sampler2D _DetailNormal; float4 _DetailNormal_ST;
        sampler2D _DetailMask;   float4 _DetailMask_ST;
        fixed4 _BaseTint;
        float4 _2Tliing;
        half _AOAmount;
        half _DetailNormalAmount;

        struct Input {
            float4 rawUV;
        };

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUV = PortSF_RawUV(v);
        }

        void surf(Input IN, inout SurfaceOutputStandard o) {
            float2 uv = IN.rawUV.xy;
            float2 detailUV = uv * _2Tliing.xy + _2Tliing.zw;

            half mask = tex2D(_DetailMask, TRANSFORM_TEX(uv, _DetailMask)).r;

            fixed3 baseAlbedo = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb * _BaseTint.rgb;
            fixed3 detailAlbedo = tex2D(_Detail, TRANSFORM_TEX(detailUV, _Detail)).rgb;
            o.Albedo = lerp(baseAlbedo, detailAlbedo, mask);

            PortSF_MetallicSmoothness(tex2D(_BaseMetallic, TRANSFORM_TEX(uv, _BaseMetallic)), o.Metallic, o.Smoothness);

            half3 baseNormal = PortSF_UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
            half3 detailNormal = PortSF_UnpackNormal(tex2D(_DetailNormal, TRANSFORM_TEX(detailUV, _DetailNormal)));
            detailNormal = normalize(lerp(half3(0, 0, 1), detailNormal, mask * _DetailNormalAmount));
            o.Normal = BlendNormals(baseNormal, detailNormal);

            o.Occlusion = PortSF_Occlusion(tex2D(_AO, TRANSFORM_TEX(uv, _AO)).r, _AOAmount);
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Standard"
}
