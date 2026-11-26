// From-scratch reimplementation for the port.
// Rain-soaked surfaces of the funfair (boards, stalls, signage, chairs, bins). Standard
// metallic lighting from Base Color, Metallic (R metallic, A smoothness) and Normal, with
// optional ambient occlusion (node_3999, AO_Strenght). On top, the rain layer: a static field
// of droplets (DropsNormal, RippleStrength) and running streaks (RippleStreakTexture,
// StreakStrength) that scroll down at ScrollSpeed. DripIntensity is the master amount of rain;
// it also darkens the surface slightly and makes it glossier, most of all on the droplets.
Shader "Shader Forge/DripShader" {
    Properties {
        _Color ("Color", Color) = (0.501961,0.501961,0.501961,1)
        _MainTex ("Base Color", 2D) = "white" {}
        _Metallic ("Metallic", 2D) = "white" {}
        _Normal ("Normal", 2D) = "bump" {}
        _RippleStrength ("RippleStrength", Range(0,10)) = 1
        _StreakStrength ("StreakStrength", Range(0,5)) = 1
        _DropsNormal ("DropsNormal", 2D) = "bump" {}
        _ScrollSpeed ("ScrollSpeed", Range(-0.5,0.5)) = 0.01
        _RippleStreakTexture ("RippleStreakTexture", 2D) = "bump" {}
        _DripIntensity ("DripIntensity", Range(0,1)) = 1
        _node_3999 ("node_3999", 2D) = "white" {}
        _AO_Strenght ("AO_Strenght", Range(0,1)) = 1
    }

    SubShader {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #include "PortSFGlassWet.cginc"

        sampler2D _MainTex;
        sampler2D _Metallic;            float4 _Metallic_ST;
        sampler2D _Normal;              float4 _Normal_ST;
        sampler2D _DropsNormal;         float4 _DropsNormal_ST;
        sampler2D _RippleStreakTexture; float4 _RippleStreakTexture_ST;
        sampler2D _node_3999;           float4 _node_3999_ST;
        half4 _Color;
        half _RippleStrength;
        half _StreakStrength;
        float _ScrollSpeed;
        half _DripIntensity;
        half _AO_Strenght;

        struct Input {
            float2 uv_MainTex;
            float2 baseUV;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.baseUV = v.texcoord.xy;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.baseUV;
            half4 base = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            half4 ms = tex2D(_Metallic, TRANSFORM_TEX(uv, _Metallic));
            half3 n = UnpackNormal(tex2D(_Normal, TRANSFORM_TEX(uv, _Normal)));
            half ao = lerp(1.0h, tex2D(_node_3999, TRANSFORM_TEX(uv, _node_3999)).r, _AO_Strenght);

            half rain = _DripIntensity;
            half3 drops = PortSF_UnpackNormalStrength(
                tex2D(_DropsNormal, TRANSFORM_TEX(uv, _DropsNormal)), _RippleStrength * 0.1h * rain);
            float2 streakUV = TRANSFORM_TEX(uv, _RippleStreakTexture) + float2(0.0, _Time.y * _ScrollSpeed);
            half3 streaks = PortSF_UnpackNormalStrength(tex2D(_RippleStreakTexture, streakUV), _StreakStrength * rain);
            half3 water = PortSF_BlendNormals(drops, streaks);

            // Droplets and streaks are where the water stands: glossier there.
            half waterAmount = saturate(length(water.xy) * 4.0h);
            half wet = saturate(rain * (0.35h + waterAmount));

            o.Albedo = base.rgb * lerp(1.0h, 0.8h, rain);
            o.Metallic = ms.r;
            o.Smoothness = lerp(ms.a, max(ms.a, 0.92h), wet);
            o.Normal = PortSF_BlendNormals(n, water);
            o.Occlusion = ao;
            o.Alpha = 1.0h;
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Diffuse"
}
