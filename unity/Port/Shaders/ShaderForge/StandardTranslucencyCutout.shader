// From-scratch reimplementation for the port.
// Shader Forge/StandardTranslucencyCutout: alpha-tested, double-sided variant (dead plants, dream
// catcher) of StandardTranslucency, without the detail normal. Inputs as the materials use them:
//   _MainTex * _AlbedoTint albedo (alpha tested against _Cutoff), _Metallic map (R metallic,
//   A smoothness), _BumpMap, _Occlusion map applied by _Amount. Back faces flip the normal.
//   Transmission: _Translucency map * light, strongest when the light is behind the surface and
//   the viewer looks towards it (shaped by _TranslucencyPower), focused towards the silhouette by
//   _TransmissionEdgeAmount and scaled by _Transmission.
//   Rim: _RimColour * fresnel^_RimPower when _RimToggle is set.
Shader "Shader Forge/StandardTranslucencyCutout" {
    Properties {
        _MainTex ("Albedo", 2D) = "white" {}
        _AlbedoTint ("Albedo Tint", Color) = (0.501961,0.501961,0.501961,1)
        _Metallic ("Metallic", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Occlusion ("Occlusion", 2D) = "white" {}
        _Amount ("Amount", Range(0,1)) = 0
        _Translucency ("Translucency", 2D) = "black" {}
        _RimPower ("Rim Power", Range(0.5,5)) = 1.76379
        _RimColour ("Rim Colour", Color) = (0.493323,0.536765,0.370999,1)
        [MaterialToggle] _RimToggle ("Rim Toggle", Float) = 0
        _Transmission ("Transmission", Range(0,40)) = 1.12925
        _TranslucencyPower ("Translucency Power", Range(0,5)) = 4.30497
        _TransmissionEdgeAmount ("TransmissionEdgeAmount", Range(0,10)) = 1
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    SubShader {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 300
        Cull Off

        CGPROGRAM
        #pragma surface surf PortSFTrans fullforwardshadows alphatest:_Cutoff addshadow
        #pragma target 3.0

        #include "UnityCG.cginc"
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex;
        sampler2D _Metallic;
        sampler2D _BumpMap;
        sampler2D _Occlusion;
        sampler2D _Translucency;
        fixed4 _AlbedoTint;
        half _Amount;
        half _RimPower;
        fixed4 _RimColour;
        half _RimToggle;
        half _Transmission;
        half _TranslucencyPower;
        half _TransmissionEdgeAmount;

        struct Input {
            float2 uv_MainTex;
            float3 viewDir;
            float facing : VFACE;
        };

        struct SurfaceOutputPortSFTrans {
            fixed3 Albedo;
            float3 Normal;
            half3 Emission;
            half Metallic;
            half Smoothness;
            half Occlusion;
            fixed Alpha;
            half3 Transmit;
        };

        inline SurfaceOutputStandard PortSFToStandard(SurfaceOutputPortSFTrans s) {
            SurfaceOutputStandard o;
            o.Albedo = s.Albedo;
            o.Normal = s.Normal;
            o.Emission = s.Emission;
            o.Metallic = s.Metallic;
            o.Smoothness = s.Smoothness;
            o.Occlusion = s.Occlusion;
            o.Alpha = s.Alpha;
            return o;
        }

        inline half4 LightingPortSFTrans(SurfaceOutputPortSFTrans s, float3 viewDir, UnityGI gi) {
            half4 c = LightingStandard(PortSFToStandard(s), viewDir, gi);

            half3 N = normalize(s.Normal);
            half3 L = gi.light.dir;
            // Directional light transmission skips the shadow term: the lit back of the object
            // would otherwise shadow its own front.
            half isDirectional = 1.0 - saturate(_WorldSpaceLightPos0.w);
            half3 lightColor = lerp(gi.light.color, _LightColor0.rgb, isDirectional);

            half back = saturate(dot(-N, L));
            half towards = saturate(dot(viewDir, -L));
            half t = pow(max(back * (0.5 + 0.5 * towards), 1e-4), max(_TranslucencyPower, 0.05));
            half edge = pow(max(1.0 - saturate(dot(N, viewDir)), 1e-4), _TransmissionEdgeAmount);
            c.rgb += s.Transmit * lightColor * (t * edge * _Transmission);
            return c;
        }

        inline void LightingPortSFTrans_GI(SurfaceOutputPortSFTrans s, UnityGIInput data, inout UnityGI gi) {
            LightingStandard_GI(PortSFToStandard(s), data, gi);
        }

        void surf(Input IN, inout SurfaceOutputPortSFTrans o) {
            float2 uv = IN.uv_MainTex;
            fixed4 albedo = tex2D(_MainTex, uv) * _AlbedoTint;
            fixed4 mg = tex2D(_Metallic, uv);

            half3 n = UnpackNormal(tex2D(_BumpMap, uv));
            n = IN.facing > 0 ? n : -n;

            o.Albedo = albedo.rgb;
            o.Alpha = albedo.a;
            o.Normal = n;
            o.Metallic = mg.r;
            o.Smoothness = mg.a;
            o.Occlusion = lerp(1.0, tex2D(_Occlusion, uv).r, _Amount);
            o.Transmit = tex2D(_Translucency, uv).rgb;

            half nv = saturate(dot(normalize(IN.viewDir), n));
            o.Emission = _RimColour.rgb * pow(max(1.0 - nv, 1e-4), _RimPower) * saturate(_RimToggle);
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Transparent/Cutout/VertexLit"
}
