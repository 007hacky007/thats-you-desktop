// From-scratch reimplementation for the port.
// Textured window glass. The scene behind (per-object screen grab) is bent by the Refraction normal
// map and tinted: MidTint across the face, RimTint towards the edges. A physically lit surface
// layer (Albedo, Metal Smooth: R metallic, A smoothness scaled by Gloss) is blended over it with
// Opacity, which rises towards the edges by OpacityFresnel. Specular is never faded, as on real
// glass.
Shader "Shader Forge/Wish/JackGlarse" {
    Properties {
        _RefractionIntensity ("Refraction Intensity", Range(0,1)) = 0.1
        _Refraction ("Refraction", 2D) = "bump" {}
        _Albedo ("Albedo", 2D) = "white" {}
        _MetalSmooth ("Metal Smooth", 2D) = "white" {}
        _RimTint ("RimTint", Color) = (0.5,0.5,0.5,1)
        _Opacity ("Opacity", Range(0,1)) = 0.565563
        _Gloss ("Gloss", Range(0,1)) = 0
        _RimTintBoost ("RimTintBoost", Range(0,3)) = 0
        _RimTintPower ("RimTintPower", Range(0,3)) = 3
        _OpacityFresnel ("OpacityFresnel", Range(0,1)) = 0
        _MidTintBoost ("MidTintBoost", Range(0,1)) = 0
        _MidTint ("MidTint", Color) = (0.5,0.5,0.5,1)
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    CGINCLUDE
    #include "UnityPBSLighting.cginc"
    #include "PortSFGlassWet.cginc"

    sampler2D _Refraction;  float4 _Refraction_ST;
    sampler2D _Albedo;      float4 _Albedo_ST;
    sampler2D _MetalSmooth; float4 _MetalSmooth_ST;
    half _RefractionIntensity;
    half4 _RimTint;
    half _Opacity;
    half _Gloss;
    half _RimTintBoost;
    half _RimTintPower;
    half _OpacityFresnel;
    half _MidTintBoost;
    half4 _MidTint;

    struct JackSurface
    {
        half3 tn;          // tangent-space normal
        half3 n;           // world normal
        half3 albedo;
        half metallic;
        half smoothness;
        half opacity;
    };

    JackSurface JackGetSurface(float2 uv, float3 wT, float3 wB, float3 wN, half3 v)
    {
        JackSurface s;
        s.tn = UnpackNormal(tex2D(_Refraction, TRANSFORM_TEX(uv, _Refraction)));
        s.n = PortSF_TangentToWorld(s.tn, wT, wB, normalize(wN));
        s.albedo = tex2D(_Albedo, TRANSFORM_TEX(uv, _Albedo)).rgb;
        half4 ms = tex2D(_MetalSmooth, TRANSFORM_TEX(uv, _MetalSmooth));
        s.metallic = ms.r;
        s.smoothness = ms.a * _Gloss;
        s.opacity = saturate(_Opacity + _OpacityFresnel * PortSF_Fresnel(s.n, v, 3.0h));
        return s;
    }

    // Lit surface layer with the diffuse part premultiplied by opacity. alphaOut is how much
    // of the background this layer covers (diffuse coverage plus specular reflectivity).
    half3 JackShade(JackSurface s, half3 v, UnityLight light, UnityIndirect ind, out half alphaOut)
    {
        half3 specColor;
        half oneMinusReflectivity;
        half3 diffColor = DiffuseAndSpecularFromMetallic(s.albedo, s.metallic, specColor, oneMinusReflectivity);
        diffColor *= s.opacity;
        alphaOut = 1.0h - oneMinusReflectivity + s.opacity * oneMinusReflectivity;
        return UNITY_BRDF_PBS(diffColor, specColor, oneMinusReflectivity, s.smoothness, s.n, v, light, ind).rgb;
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

            half4 frag(PortSFGlassV2F i) : SV_Target
            {
                half3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                JackSurface s = JackGetSurface(i.uv, i.wT, i.wB, i.wN, v);

                float2 uv = PortSF_GrabUV(i.grabPos)
                          + PortSF_RefractionOffset(s.tn.xy, s.n, v, _RefractionIntensity * 20.0h);
                half3 behind = PortSF_SampleGrab(uv);
                half rim = saturate(PortSF_Fresnel(s.n, v, max(_RimTintPower, 0.01h)) * _RimTintBoost);
                half3 tint = lerp(half3(1, 1, 1), _MidTint.rgb, _MidTintBoost);
                tint = lerp(tint, _RimTint.rgb, rim);

                UnityLight light;
                light.color = _LightColor0.rgb;
                light.dir = PortSF_LightDir(i.worldPos);
                light.ndotl = saturate(dot(s.n, light.dir));

                half3 specColor;
                half omr;
                DiffuseAndSpecularFromMetallic(s.albedo, s.metallic, specColor, omr);
                Unity_GlossyEnvironmentData g = UnityGlossyEnvironmentSetup(s.smoothness, v, s.n, specColor);
                UnityIndirect ind;
                ind.diffuse = max(0, ShadeSH9(half4(s.n, 1.0h)));
                ind.specular = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, g);

                half coverage;
                half3 surface = JackShade(s, v, light, ind, coverage);
                half3 transmit = tint * (1.0h - coverage);

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
                JackSurface s = JackGetSurface(i.uv, i.wT, i.wB, i.wN, v);
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

                UnityLight light;
                light.color = _LightColor0.rgb * atten;
                light.dir = PortSF_LightDir(i.worldPos);
                light.ndotl = saturate(dot(s.n, light.dir));
                UnityIndirect ind;
                ind.diffuse = 0;
                ind.specular = 0;

                half coverage;
                half3 c = JackShade(s, v, light, ind, coverage);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, half4(0, 0, 0, 0));
                return half4(c, 0.0h);
            }
            ENDCG
        }
    }

    Fallback "Legacy Shaders/Transparent/Diffuse"
}
