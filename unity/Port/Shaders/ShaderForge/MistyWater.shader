// From-scratch reimplementation for the port.
// Murky liquid in the brain jar. Two layers of the Mist Map drift through the liquid at
// MIST SPEED and modulate the colour and density of MIST COLOUR (alpha = base density). The
// silhouette picks up RIM COLOUR, with RIM THRESHOLD as the fresnel exponent (higher = thinner
// rim). Lit by the main light, ambient probes and additional lights, alpha blended.
Shader "Shader Forge/MistyWater" {
    Properties {
        _MISTCOLOUR ("MIST COLOUR", Color) = (0.138522,0.455882,0.0134083,0.584)
        _RIMCOLOUR ("RIM COLOUR", Color) = (1,0,0,1)
        _RIMTHRESHOLD ("RIM THRESHOLD", Range(0,10)) = 2.43332
        _MistMap ("Mist Map", 2D) = "white" {}
        _MISTSPEED ("MIST SPEED", Range(0,1)) = 1
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    CGINCLUDE
    #include "PortSFGlassWet.cginc"

    sampler2D _MistMap; float4 _MistMap_ST;
    half4 _MISTCOLOUR;
    half4 _RIMCOLOUR;
    half _RIMTHRESHOLD;
    half _MISTSPEED;

    // Drifting mist density, around 0.5 on average.
    half MistDensity(float2 uv)
    {
        float t = _Time.y * _MISTSPEED;
        float2 uvA = TRANSFORM_TEX(uv, _MistMap) + t * float2(0.031, 0.067);
        float2 uvB = TRANSFORM_TEX(uv, _MistMap) * 1.37 + float2(0.5, 0.25) + t * float2(-0.049, 0.023);
        return tex2D(_MistMap, uvA).r * tex2D(_MistMap, uvB).r * 2.0h;
    }

    half MistRim(half3 n, half3 v)
    {
        return saturate(PortSF_Fresnel(n, v, _RIMTHRESHOLD));
    }

    half MistAlpha(half mist, half rim)
    {
        return saturate(_MISTCOLOUR.a * (0.5h + mist) + rim * _RIMCOLOUR.a);
    }

    // Soft wrapped diffuse, liquids scatter light around the terminator.
    half MistDiffuse(half3 n, half3 l)
    {
        return saturate(dot(n, l) * 0.6h + 0.4h);
    }
    ENDCG

    SubShader {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 300

        Pass {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex PortSF_GlassVert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            half4 frag(PortSFGlassV2F i) : SV_Target
            {
                half3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half3 n = normalize(i.wN);
                half mist = MistDensity(i.uv);
                half rim = MistRim(n, v);
                half3 light = _LightColor0.rgb * MistDiffuse(n, PortSF_LightDir(i.worldPos))
                            + max(0, ShadeSH9(half4(n, 1.0h)));
                half3 c = _MISTCOLOUR.rgb * light * (0.6h + 0.8h * mist);
                c = lerp(c, _RIMCOLOUR.rgb * (light + 0.5h), rim);
                half4 col = half4(c, MistAlpha(mist, rim));
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        Pass {
            Name "FORWARD_DELTA"
            Tags { "LightMode" = "ForwardAdd" }
            ZWrite Off
            Blend SrcAlpha One

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
                half3 n = normalize(i.wN);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                half mist = MistDensity(i.uv);
                half rim = MistRim(n, v);
                half3 light = _LightColor0.rgb * atten * MistDiffuse(n, PortSF_LightDir(i.worldPos));
                half3 c = lerp(_MISTCOLOUR.rgb * (0.6h + 0.8h * mist), _RIMCOLOUR.rgb, rim) * light;
                half4 col = half4(c, MistAlpha(mist, rim));
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, half4(0, 0, 0, 0));
                return col;
            }
            ENDCG
        }
    }

    Fallback "Legacy Shaders/Transparent/Diffuse"
}
