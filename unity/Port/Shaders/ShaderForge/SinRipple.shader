// From-scratch reimplementation for the port.
// Swaying fish tank plants. A sine wave travels up the plant (WaveFrequency waves per world
// unit of height, moving at SwayRate) and pushes vertices sideways by Wave_Magnitude world
// units. Standard metallic lighting (Base Color x Color, Metallic: R metallic, A smoothness,
// Normal Map), alpha tested on the base colour alpha, double sided, with a self-lit term of
// a quarter of the base colour times EmissonTint x EmissionValue
// (a soft fill from the tank lighting).
Shader "Shader Forge/SinRipple" {
    Properties {
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Base Color", 2D) = "white" {}
        _SwayRate ("SwayRate", Range(0,1)) = 0.0974951
        _Metallic ("Metallic", 2D) = "white" {}
        _EmissonTint ("EmissonTint", Color) = (1,1,1,2)
        _EmissionValue ("EmissionValue", Float) = 1
        _WaveFrequency ("WaveFrequency", Float) = 2
        _Wave_Magnitude ("Wave_Magnitude", Float) = 0.03
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    SubShader {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 300
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert alphatest:_Cutoff addshadow
        #pragma target 3.0
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;  float4 _BumpMap_ST;
        sampler2D _Metallic; float4 _Metallic_ST;
        half4 _Color;
        float _SwayRate;
        half4 _EmissonTint;
        half _EmissionValue;
        float _WaveFrequency;
        float _Wave_Magnitude;

        struct Input {
            float2 uv_MainTex;
            float2 baseUV;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.baseUV = v.texcoord.xy;

            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            // SwayRate 0..1 as wave cycles per 0.1 s (0.019 gives a slow ~5 s sway).
            float phase = _Time.y * _SwayRate * 62.8318 - wp.y * _WaveFrequency * 6.28318;
            float3 push = float3(sin(phase), 0.0, cos(phase * 0.73 + wp.x * 3.1) * 0.5) * _Wave_Magnitude;
            wp += push;
            v.vertex.xyz = mul(unity_WorldToObject, float4(wp, 1.0)).xyz;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 base = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            half4 ms = tex2D(_Metallic, TRANSFORM_TEX(IN.baseUV, _Metallic));
            o.Albedo = base.rgb;
            o.Metallic = ms.r;
            o.Smoothness = ms.a;
            o.Normal = UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(IN.baseUV, _BumpMap)));
            // A soft self-lit fill from the tank light, not full brightness: with the material
            // values (tint white, value 1) the plants keep their lit shading and gain about a
            // quarter of their albedo on top.
            o.Emission = base.rgb * _EmissonTint.rgb * (_EmissionValue * 0.25h);
            o.Alpha = base.a;
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}
