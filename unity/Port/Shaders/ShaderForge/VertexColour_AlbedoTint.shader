// From-scratch reimplementation for the port.
// Shader Forge/VertexColour_AlbedoTint: Standard metallic PBR where the albedo tint is painted
// per vertex: the blue vertex colour channel blends _BaseTint towards _B_Tint (used to shade
// the ends of the corridor floor tiles). Metallic map R = metallic, A * _Gloss = smoothness.
Shader "Shader Forge/VertexColour_AlbedoTint" {
    Properties {
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BaseTint ("BaseTint", Color) = (0.5019608, 0.5019608, 0.5019608, 1)
        _MainTex ("Base Color", 2D) = "white" {}
        _Gloss ("Gloss", Range(0, 1)) = 0.8
        _B_Tint ("B_Tint", Color) = (1, 0, 0, 1)
        _Metallic ("Metallic", 2D) = "white" {}
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        #include "PortSFSurface.cginc"

        sampler2D _MainTex;  float4 _MainTex_ST;
        sampler2D _Metallic; float4 _Metallic_ST;
        sampler2D _BumpMap;  float4 _BumpMap_ST;
        fixed4 _BaseTint;
        fixed4 _B_Tint;
        half _Gloss;

        struct Input {
            float4 rawUV;
            float4 color : COLOR;
        };

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUV = PortSF_RawUV(v);
        }

        void surf(Input IN, inout SurfaceOutputStandard o) {
            float2 uv = IN.rawUV.xy;
            fixed3 tint = lerp(_BaseTint.rgb, _B_Tint.rgb, saturate(IN.color.b));
            o.Albedo = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb * tint;
            half4 m = tex2D(_Metallic, TRANSFORM_TEX(uv, _Metallic));
            o.Metallic = m.r;
            o.Smoothness = m.a * _Gloss;
            o.Normal = PortSF_UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Standard"
}
