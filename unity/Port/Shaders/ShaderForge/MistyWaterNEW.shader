// From-scratch reimplementation for the port.
// Fish tank front and side panes. The tank interior comes from a per-object screen grab (slightly
// bent near the edges, optionally blurred). The pane gets more opaque and glows with
// EmissiveTint towards grazing angles (fresnel with FresnelExp), like the green edge of thick
// glass. OpacityThreshold sets how clear the pane is face on (1 = fully clear). The surface is
// lit with Gloss / Metallic, and the Occlusion map darkens the glow and reflections.
Shader "Shader Forge/MistyWaterNEW" {
    Properties {
        _EmissiveTint ("EmissiveTint", Color) = (0,1,0.131035,1)
        _FresnelExp ("FresnelExp", Range(0,10)) = 1.84537
        _Gloss ("Gloss", Range(0,1)) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
        _OpacityThreshold ("OpacityThreshold", Range(0,1)) = 0
        _Occlusion ("Occlusion", 2D) = "white" {}
        _DISTORTIONSTRENGTH ("DISTORTION STRENGTH", Range(10,0)) = 5
        _BLURAMOUNT ("BLUR AMOUNT", Range(0,0.005)) = 0.002
        [Toggle] _BlurToggle ("Blur Toggle", Float) = 0
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    CGINCLUDE
    #include "UnityPBSLighting.cginc"
    #include "PortSFGlassWet.cginc"

    sampler2D _Occlusion; float4 _Occlusion_ST;
    half4 _EmissiveTint;
    half _FresnelExp;
    half _Gloss;
    half _Metallic;
    half _OpacityThreshold;
    half _DISTORTIONSTRENGTH;
    float _BLURAMOUNT;

    // Lit pane layer. Diffuse is premultiplied by opacity; coverage is how much of the
    // background the layer hides.
    half3 PaneShade(half3 n, half3 v, half opacity, UnityLight light, UnityIndirect ind, out half coverage)
    {
        half3 specColor;
        half omr;
        half3 diffColor = DiffuseAndSpecularFromMetallic(_EmissiveTint.rgb, _Metallic, specColor, omr);
        diffColor *= opacity;
        coverage = 1.0h - omr + opacity * omr;
        return UNITY_BRDF_PBS(diffColor, specColor, omr, _Gloss, n, v, light, ind).rgb;
    }

    half PaneOpacity(half fresnel)
    {
        return lerp(fresnel, 1.0h, 1.0h - _OpacityThreshold);
    }
    ENDCG

    SubShader {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 300

        GrabPass { }

        Pass {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex PortSF_GlassVert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma shader_feature_local _ _BLURTOGGLE_ON

            half4 frag(PortSFGlassV2F i) : SV_Target
            {
                half3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half3 n = normalize(i.wN);
                half occ = tex2D(_Occlusion, TRANSFORM_TEX(i.uv, _Occlusion)).r;
                half f = PortSF_Fresnel(n, v, _FresnelExp);
                half opacity = PaneOpacity(f);

                float2 uv = PortSF_GrabUV(i.grabPos) + PortSF_RefractionOffset(half2(0, 0), n, v, _DISTORTIONSTRENGTH);
                #if defined(_BLURTOGGLE_ON)
                half3 behind = PortSF_SampleGrabBlurred(uv, _BLURAMOUNT);
                #else
                half3 behind = PortSF_SampleGrab(uv);
                #endif

                UnityLight light;
                light.color = _LightColor0.rgb;
                light.dir = PortSF_LightDir(i.worldPos);
                light.ndotl = saturate(dot(n, light.dir));

                half3 specColor;
                half omr;
                DiffuseAndSpecularFromMetallic(_EmissiveTint.rgb, _Metallic, specColor, omr);
                Unity_GlossyEnvironmentData g = UnityGlossyEnvironmentSetup(_Gloss, v, n, specColor);
                UnityIndirect ind;
                ind.diffuse = max(0, ShadeSH9(half4(n, 1.0h))) * occ;
                ind.specular = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, g) * occ;

                half coverage;
                half3 surface = PaneShade(n, v, opacity, light, ind, coverage);
                surface += _EmissiveTint.rgb * f * occ;
                half3 transmit = (1.0h - coverage).xxx;

                { UNITY_APPLY_FOG_COLOR(i.fogCoord, transmit, half4(1, 1, 1, 1)); }
                { UNITY_APPLY_FOG_COLOR(i.fogCoord, surface, unity_FogColor * coverage); }
                return half4(behind * transmit + surface, 1.0h);
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
                half3 n = normalize(i.wN);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                half opacity = PaneOpacity(PortSF_Fresnel(n, v, _FresnelExp));

                UnityLight light;
                light.color = _LightColor0.rgb * atten;
                light.dir = PortSF_LightDir(i.worldPos);
                light.ndotl = saturate(dot(n, light.dir));
                UnityIndirect ind;
                ind.diffuse = 0;
                ind.specular = 0;

                half coverage;
                half3 c = PaneShade(n, v, opacity, light, ind, coverage);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, half4(0, 0, 0, 0));
                return half4(c, 0.0h);
            }
            ENDCG
        }
    }

    Fallback "Legacy Shaders/Transparent/Diffuse"
}
