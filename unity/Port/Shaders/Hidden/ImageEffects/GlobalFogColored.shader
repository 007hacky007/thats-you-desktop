// From-scratch reimplementation for the port of the coloured fog variant used
// by GlobalFogColored (shader name "Hidden/GlobalFog", same as GlobalFog; the
// script references this one by GUID). Distance fog from the depth texture and
// the far-plane frustum corner rays in _FrustumCornersWS (rows: 0 top-left,
// 1 top-right, 2 bottom-right, 3 bottom-left). The fog strength follows the
// RenderSettings fog mode/density (_SceneFogParams/_SceneFogMode), scaled by
// _FogIntensity, and its colour is read from the horizontal ramp
// _ColourGradiant: near haze takes the left end, dense fog the right end.
// _DistanceScale multiplies the fog distance, _StartDistance (negative) pushes
// the fog away from the camera. Sky / far-plane pixels are left untouched.
Shader "Hidden/GlobalFog" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "black" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex vertFog
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxGrade.cginc"

        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        sampler2D _ColourGradiant;
        float4x4 _FrustumCornersWS;
        float4 _CameraWS;
        float _DistanceScale;
        float _FogIntensity;
        float _StartDistance;
        float4 _SceneFogParams;
        float4 _SceneFogMode;

        struct v2fFog
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 uvDepth : TEXCOORD1;
            float3 ray : TEXCOORD2;
        };

        v2fFog vertFog(PortGradeAppdata v)
        {
            v2fFog o;
            o.pos = UnityObjectToClipPos(float4(v.vertex.xy, 0.1, 1.0));
            o.uv = v.texcoord;
            o.uvDepth = PortGradeScreenUV(v.texcoord);
            float3 top = lerp(_FrustumCornersWS[0].xyz, _FrustumCornersWS[1].xyz, o.uvDepth.x);
            float3 bottom = lerp(_FrustumCornersWS[3].xyz, _FrustumCornersWS[2].xyz, o.uvDepth.x);
            o.ray = lerp(bottom, top, o.uvDepth.y);
            return o;
        }

        float FogTransmittance(float d)
        {
            float f = 1.0;
            if (_SceneFogMode.x == 1.0)
                f = d * _SceneFogParams.z + _SceneFogParams.w;
            else if (_SceneFogMode.x == 2.0)
                f = exp2(-_SceneFogParams.y * d);
            else if (_SceneFogMode.x == 3.0)
            {
                float k = _SceneFogParams.x * d;
                f = exp2(-k * k);
            }
            return saturate(f);
        }

        float4 frag(v2fFog i) : SV_Target
        {
            float4 sceneColor = tex2D(_MainTex, i.uv);
            float dpth = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uvDepth));
            if (dpth >= 0.9999)
                return sceneColor;

            float dist = dpth * _ProjectionParams.z;
            dist = max(dist + _StartDistance, 0.0) * max(_DistanceScale, 1.0);

            float fog = 1.0 - FogTransmittance(dist);
            // The ramp is imported with Repeat wrapping: stay on texel centres.
            float3 fogColor = tex2D(_ColourGradiant, float2(PortGradeCurveU(fog), 0.5)).rgb;
            float amount = saturate(fog * _FogIntensity);
            return float4(lerp(sceneColor.rgb, fogColor, amount), sceneColor.a);
        }
        ENDCG
    }
}
Fallback Off
}
