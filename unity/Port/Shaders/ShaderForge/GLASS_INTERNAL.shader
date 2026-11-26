// From-scratch reimplementation for the port.
// Inner wall of bottles and glasses (and the microwave plate). Drawn just before the
// transparent queue so it is already in the screen grab that the outer glass refracts.
// It only adds light: reflection probe reflections scaled by REFLECTION STRENGTH (stronger at
// grazing angles) plus small light highlights, so whatever is inside stays visible.
Shader "Shader Forge/GLASS_INTERNAL" {
    Properties {
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _REFLECTIONSTRENGTH ("REFLECTION STRENGTH", Range(0,1)) = 0.25
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    CGINCLUDE
    #include "PortSFGlassWet.cginc"

    sampler2D _BumpMap; float4 _BumpMap_ST;
    half _REFLECTIONSTRENGTH;

    half3 InnerNormal(float2 uv, float3 wT, float3 wB, float3 wN)
    {
        half3 tn = UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
        return PortSF_TangentToWorld(tn, wT, wB, normalize(wN));
    }

    half3 InnerSpec(half3 n, half3 l, half3 v)
    {
        return _LightColor0.rgb * PortSF_BlinnSpec(n, l, v, 0.85h) * _REFLECTIONSTRENGTH;
    }
    ENDCG

    SubShader {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 300

        Pass {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
            Blend One One

            CGPROGRAM
            #pragma vertex PortSF_GlassVert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            half4 frag(PortSFGlassV2F i) : SV_Target
            {
                half3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half3 n = InnerNormal(i.uv, i.wT, i.wB, i.wN);
                half weight = _REFLECTIONSTRENGTH * (0.25h + 0.75h * PortSF_Fresnel(n, v, 3.0h));
                half3 c = PortSF_EnvReflection(n, v, 0.95h) * weight;
                c += InnerSpec(n, PortSF_LightDir(i.worldPos), v);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, half4(0, 0, 0, 0));
                return half4(c, 0.0h);
            }
            ENDCG
        }

        Pass {
            Name "FORWARD_DELTA"
            Tags { "LightMode" = "ForwardAdd" }
            ZWrite Off
            Blend One One

            CGPROGRAM
            #pragma vertex PortSF_GlassAddVert
            #pragma fragment fragAdd
            #pragma target 3.0
            #pragma multi_compile_fwdadd
            #pragma multi_compile_fog
            #include "AutoLight.cginc"
            PORT_SF_GLASS_ADD_V2F

            half4 fragAdd(PortSFGlassAddV2F i) : SV_Target
            {
                half3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half3 n = InnerNormal(i.uv, i.wT, i.wB, i.wN);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                half3 c = InnerSpec(n, PortSF_LightDir(i.worldPos), v) * atten;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, half4(0, 0, 0, 0));
                return half4(c, 0.0h);
            }
            ENDCG
        }
    }

    Fallback Off
}
