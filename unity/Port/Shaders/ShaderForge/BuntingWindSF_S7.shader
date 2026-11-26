// From-scratch reimplementation for the port.
// Shader Forge/BuntingWindSF_S7: seaside bunting flags (scenario 7). Alpha tested, double sided
// Standard metallic PBR with a vertex wind flutter. The mesh paints a sway weight into the red
// vertex colour (0 at the cord, 1 at the flag tip); vertices are pushed along their normal by a
// layered sine whose phase varies with world position and the green/blue vertex colours so
// neighbouring flags do not move in sync. _node_7966 is the metallic map (R metallic,
// A smoothness), _Normal the normal map.
Shader "Shader Forge/BuntingWindSF_S7" {
    Properties {
        _MainTex ("Base Color", 2D) = "white" {}
        _Normal ("Normal", 2D) = "bump" {}
        _node_7966 ("node_7966", 2D) = "white" {}
        _WindStrength ("Wind Strength", Float) = 0.025
        _WindSpeed ("Wind Speed", Float) = 3
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 300
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert alphatest:_Cutoff addshadow
        #pragma target 3.0

        #include "PortSFSurface.cginc"

        sampler2D _MainTex;   float4 _MainTex_ST;
        sampler2D _Normal;    float4 _Normal_ST;
        sampler2D _node_7966; float4 _node_7966_ST;
        float _WindStrength;
        float _WindSpeed;

        struct Input {
            float4 rawUV;
        };

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUV = PortSF_RawUV(v);

            float weight = saturate(v.color.r);
            float3 worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
            float phase = dot(worldPos, float3(1.7, 0.9, 1.3)) + (v.color.g - v.color.b) * 2.0;
            float t = _Time.y * _WindSpeed;
            float sway = sin(t + phase) * 0.7 + sin(t * 2.3 + phase * 1.6) * 0.3;
            v.vertex.xyz += v.normal * (sway * weight * _WindStrength);
        }

        void surf(Input IN, inout SurfaceOutputStandard o) {
            float2 uv = IN.rawUV.xy;
            fixed4 albedo = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex));
            o.Albedo = albedo.rgb;
            o.Alpha = albedo.a;
            PortSF_MetallicSmoothness(tex2D(_node_7966, TRANSFORM_TEX(uv, _node_7966)), o.Metallic, o.Smoothness);
            o.Normal = PortSF_UnpackNormal(tex2D(_Normal, TRANSFORM_TEX(uv, _Normal)));
        }
        ENDCG
    }

    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
