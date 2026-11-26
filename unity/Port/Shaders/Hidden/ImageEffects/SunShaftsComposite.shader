// From-scratch reimplementation for the port: "Hidden/SunShaftsComposite".
// Sun shafts for SunShafts:
//   Pass 0: screen-blend the blurred shafts (_ColorBuffer * _SunColor) over the frame.
//   Pass 1: radial blur of the shaft buffer towards _SunPosition (_BlurRadius4.xy step).
//   Pass 2: shaft source mask: sky pixels (far depth) brighter than _SunThreshold,
//           faded out beyond _SunPosition.w (max radius) from the sun.
//   Pass 3: same mask without depth: sky pixels are those matching _Skybox.
//   Pass 4: additive blend of the shafts over the frame.
Shader "Hidden/SunShaftsComposite" {
Properties {
 _MainTex ("Base", 2D) = "" { }
 _ColorBuffer ("Color", 2D) = "" { }
 _Skybox ("Skybox", 2D) = "" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "PortFxGrade.cginc"

    sampler2D _ColorBuffer;
    sampler2D _Skybox;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float4 _SunColor;
    float4 _SunPosition;   // xy: viewport position, z: in front (>= 0), w: max radius
    float4 _SunThreshold;
    float4 _BlurRadius4;

    #define PORT_SHAFT_TAPS 6

    float3 Shafts(float2 uvScreen)
    {
        return tex2D(_ColorBuffer, uvScreen).rgb * _SunColor.rgb;
    }

    float4 fragScreen(PortGradeV2f i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        float3 s = saturate(Shafts(i.uv2));
        c.rgb = 1.0 - (1.0 - saturate(c.rgb)) * (1.0 - s);
        return c;
    }

    float4 fragAdd(PortGradeV2f i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        c.rgb += Shafts(i.uv2);
        return c;
    }

    float4 fragRadialBlur(PortGradeV2f i) : SV_Target
    {
        float2 stepUV = (_SunPosition.xy - i.uv2) * _BlurRadius4.xy;
        float2 uv = i.uv;
        float4 sum = 0.0;
        [unroll]
        for (int k = 0; k < PORT_SHAFT_TAPS; k++)
        {
            sum += tex2D(_MainTex, uv);
            uv += stepUV;
        }
        return sum / PORT_SHAFT_TAPS;
    }

    float SunFalloff(float2 uvScreen)
    {
        float2 d = _SunPosition.xy - uvScreen;
        float r = max(_SunPosition.w, 1e-3);
        return saturate(1.0 - length(d) / r);
    }

    float4 MaskValue(float3 color, float sky, float2 uvScreen)
    {
        float3 over = max(color - _SunThreshold.rgb, 0.0);
        float v = dot(over, float3(1.0, 1.0, 1.0)) * sky * SunFalloff(uvScreen);
        return float4(v, v, v, 1.0);
    }

    float4 fragMaskDepth(PortGradeV2f i) : SV_Target
    {
        float3 c = tex2D(_MainTex, i.uv).rgb;
        float dpth = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv2));
        float sky = dpth > 0.99 ? 1.0 : 0.0;
        return MaskValue(c, sky, i.uv2);
    }

    float4 fragMaskSkybox(PortGradeV2f i) : SV_Target
    {
        float3 c = tex2D(_MainTex, i.uv).rgb;
        float3 sb = tex2D(_Skybox, i.uv2).rgb;
        float3 d = abs(c - sb);
        float sky = saturate(1.0 - (d.r + d.g + d.b) * 4.0);
        return MaskValue(c, sky, i.uv2);
    }
    ENDCG

    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragScreen
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragRadialBlur
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragMaskDepth
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragMaskSkybox
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragAdd
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
