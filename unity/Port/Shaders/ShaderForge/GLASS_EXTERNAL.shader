// From-scratch reimplementation for the port.
// Outer glass surface (bottles, drinking glasses, windows, clock and picture glass, coffee pot,
// fish tank water). The scene behind is taken from a screen grab made just before this object,
// bent by the blended normal maps (and slightly by the curvature near silhouettes), optionally
// blurred, tinted and darkened towards the edges like thick glass. A sharp specular highlight
// from the lights, a fresnel-weighted reflection probe term and an additive dirt texture sit
// on top.
Shader "Shader Forge/GLASS_EXTERNAL" {
    Properties {
        _TINT ("TINT", Color) = (0.224911,0.588235,0.134083,1)
        _EDGEDARKEN ("EDGE DARKEN", Range(0,20)) = 10
        _EDGEDARKENSCALE ("EDGE DARKEN SCALE", Range(1,4)) = 2
        _DISTORTIONSTRENGTH ("DISTORTION STRENGTH", Range(10,0)) = 5
        [Toggle] _BlurToggle ("Blur Toggle", Float) = 0
        _BLURAMOUNT ("BLUR AMOUNT", Range(0,0.005)) = 0.002
        _GLOSS ("GLOSS", Range(0,1)) = 1
        _NORMALMAP ("NORMAL MAP", 2D) = "bump" {}
        _NORMALMAPSTRENGTH ("NORMAL MAP STRENGTH", Range(0,1)) = 1
        _SECNORMALMAP ("SEC. NORMAL MAP", 2D) = "bump" {}
        _SECNORMALMAPSTRENGTH ("SEC. NORMAL MAP STRENGTH", Range(0,1)) = 0
        _ADDITIVEDIRT ("ADDITIVE DIRT", 2D) = "black" {}
        _GLOSSMASK ("GLOSS MASK", 2D) = "white" {}
    }

    CGINCLUDE
    #include "PortSFGlassWet.cginc"

    sampler2D _NORMALMAP;      float4 _NORMALMAP_ST;
    sampler2D _SECNORMALMAP;   float4 _SECNORMALMAP_ST;
    sampler2D _ADDITIVEDIRT;   float4 _ADDITIVEDIRT_ST;
    sampler2D _GLOSSMASK;      float4 _GLOSSMASK_ST;
    half4 _TINT;
    half _EDGEDARKEN;
    half _EDGEDARKENSCALE;
    half _DISTORTIONSTRENGTH;
    float _BLURAMOUNT;
    half _GLOSS;
    half _NORMALMAPSTRENGTH;
    half _SECNORMALMAPSTRENGTH;

    // Blended tangent-space normal of both normal maps.
    half3 GlassTangentNormal(float2 uv)
    {
        half3 n1 = PortSF_UnpackNormalStrength(tex2D(_NORMALMAP, TRANSFORM_TEX(uv, _NORMALMAP)), _NORMALMAPSTRENGTH);
        half3 n2 = PortSF_UnpackNormalStrength(tex2D(_SECNORMALMAP, TRANSFORM_TEX(uv, _SECNORMALMAP)), _SECNORMALMAPSTRENGTH);
        return PortSF_BlendNormals(n1, n2);
    }

    half GlassGloss(float2 uv)
    {
        return _GLOSS * tex2D(_GLOSSMASK, TRANSFORM_TEX(uv, _GLOSSMASK)).r;
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
                half3 tn = GlassTangentNormal(i.uv);
                half3 n = PortSF_TangentToWorld(tn, i.wT, i.wB, normalize(i.wN));

                float2 uv = PortSF_GrabUV(i.grabPos) + PortSF_RefractionOffset(tn.xy, n, v, _DISTORTIONSTRENGTH);
                #if defined(_BLURTOGGLE_ON)
                half3 behind = PortSF_SampleGrabBlurred(uv, _BLURAMOUNT);
                #else
                half3 behind = PortSF_SampleGrab(uv);
                #endif

                // Thick glass: the tint deepens and the glass darkens towards grazing angles.
                half edge = saturate(PortSF_Fresnel(n, v, _EDGEDARKENSCALE) * _EDGEDARKEN * 0.1h);
                half3 transmit = _TINT.rgb * (1.0h - edge);

                half gloss = GlassGloss(i.uv);
                half3 l = PortSF_LightDir(i.worldPos);
                half3 spec = _LightColor0.rgb * PortSF_BlinnSpec(n, l, v, gloss) * gloss;
                half reflWeight = 0.04h + 0.96h * PortSF_Fresnel(n, v, 5.0h);
                half3 refl = PortSF_EnvReflection(n, v, gloss) * reflWeight * saturate(gloss);
                half3 dirt = tex2D(_ADDITIVEDIRT, TRANSFORM_TEX(i.uv, _ADDITIVEDIRT)).rgb;
                half3 add = spec + refl + dirt;

                // The grabbed background is already fogged: fog fades the glass towards clear
                // instead of fogging the background a second time.
                { UNITY_APPLY_FOG_COLOR(i.fogCoord, transmit, half4(1, 1, 1, 1)); }
                { UNITY_APPLY_FOG_COLOR(i.fogCoord, add, half4(0, 0, 0, 0)); }
                return half4(behind * transmit + add, 1.0h);
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
                half3 tn = GlassTangentNormal(i.uv);
                half3 n = PortSF_TangentToWorld(tn, i.wT, i.wB, normalize(i.wN));
                half3 l = PortSF_LightDir(i.worldPos);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                half gloss = GlassGloss(i.uv);
                half3 spec = _LightColor0.rgb * atten * PortSF_BlinnSpec(n, l, v, gloss) * gloss;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, spec, half4(0, 0, 0, 0));
                return half4(spec, 0.0h);
            }
            ENDCG
        }
    }

    Fallback "Legacy Shaders/Transparent/Diffuse"
}
