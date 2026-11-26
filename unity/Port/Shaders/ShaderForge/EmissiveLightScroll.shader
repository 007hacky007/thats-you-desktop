// From-scratch reimplementation for the port.
// Shader Forge/EmissiveLightScroll: fairground chaser lights (dodgems, dome, helter skelter).
// Standard metallic PBR (albedo * _Color, constant _Metallic and _Gloss) plus an emissive strip
// texture laid out as N cells of bulb brightness states. The emissive UVs step one cell at a
// time along the direction in _Attributes_ZSpeedScale_WColumNumbers.xy, Z cycles per second,
// W cells per cycle, so each bulb jumps between states like a light chaser. Output is scaled
// by _EmissiveScale (HDR, feeds bloom).
Shader "Shader Forge/EmissiveLightScroll" {
    Properties {
        _Color ("Color", Color) = (0.5019608, 0.5019608, 0.5019608, 1)
        _MainTex ("Base Color", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Gloss ("Gloss", Range(0, 1)) = 0.8
        _EmissiveScale ("EmissiveScale", Float) = 1
        _ScrollTexture ("ScrollTexture", 2D) = "white" {}
        _Attributes_ZSpeedScale_WColumNumbers ("Attributes_ZSpeedScale_WColumNumbers)", Vector) = (1, 0, 1, 3)
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        #include "PortSFSurface.cginc"

        sampler2D _MainTex;       float4 _MainTex_ST;
        sampler2D _ScrollTexture; float4 _ScrollTexture_ST;
        fixed4 _Color;
        half _Metallic;
        half _Gloss;
        half _EmissiveScale;
        float4 _Attributes_ZSpeedScale_WColumNumbers;

        struct Input {
            float4 rawUV;
        };

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUV = PortSF_RawUV(v);
        }

        void surf(Input IN, inout SurfaceOutputStandard o) {
            float2 uv = IN.rawUV.xy;
            fixed4 albedo = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)) * _Color;
            o.Albedo = albedo.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Gloss;

            float4 attr = _Attributes_ZSpeedScale_WColumNumbers;
            float cells = max(abs(attr.w), 1.0);
            float cycle = frac(_Time.y * attr.z);
            float cellOffset = floor(cycle * cells) / cells;
            float2 scrollUV = TRANSFORM_TEX(uv, _ScrollTexture) + attr.xy * cellOffset;
            o.Emission = tex2D(_ScrollTexture, scrollUV).rgb * _EmissiveScale;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Standard"
}
