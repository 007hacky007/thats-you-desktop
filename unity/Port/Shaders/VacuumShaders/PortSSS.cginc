// From-scratch reimplementation for the port.
// Shared code for the "VacuumShaders/Subsurface Scattering" family (Bumped Diffuse / Bumped Specular
// and their Hidden/ambient and Hidden/dynlightmap twins). Built-in pipeline surface shader with a
// custom light model:
//   - Lambert diffuse, or Blinn-Phong (_SpecColor, _Shininess, gloss in _MainTex.a) when
//     PORT_SSS_SPECULAR is defined by the wrapper.
//   - Translucency tinted by _TransMap * _TransColor. The basic term looks through the surface at the
//     light (light vector bent by the normal via _TransDistortion, raised to _TransPower, scaled by
//     _TransScale). V_SSS_ADVANCED_TRANSLUSENCY_ON adds a backface wrap term (_TransBackfaceIntensity).
//     The result is weighted by _TransDirectianalLightStrength for directional lights and by
//     _TransOtherLightsStrength for point/spot lights. Directional light translucency ignores the
//     shadow term so thin, self-shadowing cloth still glows when backlit.
//   - V_SSS_RIM_ON adds a view rim (_V_SSS_Rim_Color, _V_SSS_Rim_Pow), _V_SSS_Emission adds
//     self-illumination from the albedo.
// Wrappers must declare: #pragma surface PortSSSSurf PortSSS fullforwardshadows
// and #pragma shader_feature_local V_SSS_ADVANCED_TRANSLUSENCY_ON / V_SSS_RIM_ON.

#ifndef PORT_SSS_INCLUDED
#define PORT_SSS_INCLUDED

#include "UnityCG.cginc"
#include "Lighting.cginc"

sampler2D _MainTex;
sampler2D _BumpMap;
sampler2D _TransMap;

fixed4 _Color;
half _BumpSize;
#if defined(PORT_SSS_SPECULAR)
half _Shininess;
#endif

fixed4 _TransColor;
half _TransDistortion;
half _TransPower;
half _TransScale;
half _TransBackfaceIntensity;
half _TransDirectianalLightStrength;
half _TransOtherLightsStrength;

half _V_SSS_Emission;
fixed4 _V_SSS_Rim_Color;
half _V_SSS_Rim_Pow;

struct Input
{
    float2 uv_MainTex;
    float2 uv_BumpMap;
    float3 viewDir;
};

struct SurfaceOutputPortSSS
{
    fixed3 Albedo;
    fixed3 Normal;
    fixed3 Emission;
    half Specular;
    fixed Gloss;
    fixed Alpha;
    fixed3 Trans;
};

// Tangent space normal with an adjustable strength (_BumpSize).
inline half3 PortSSSNormal(half4 packed, half strength)
{
    half3 n = UnpackNormal(packed);
    n.xy *= strength;
    return normalize(half3(n.xy, max(n.z, 1e-3)));
}

inline half3 PortSSSTranslucency(SurfaceOutputPortSSS s, half3 viewDir, UnityLight light)
{
    // _WorldSpaceLightPos0.w is 0 for directional lights, 1 for point and spot lights.
    half isDirectional = 1.0 - saturate(_WorldSpaceLightPos0.w);
    half strength = lerp(_TransOtherLightsStrength, _TransDirectianalLightStrength, isDirectional);
    half3 lightColor = lerp(light.color, _LightColor0.rgb, isDirectional);

    half3 N = s.Normal;
    half3 L = light.dir;

    // Light leaving the far side of the surface towards the viewer.
    half3 throughDir = normalize(L + N * _TransDistortion);
    half through = saturate(dot(viewDir, -throughDir));
    half amount = pow(max(through, 1e-4), max(_TransPower, 1e-3)) * _TransScale;

    #if defined(V_SSS_ADVANCED_TRANSLUSENCY_ON)
    // Diffuse light arriving at the back face.
    amount += saturate(dot(-N, L)) * _TransBackfaceIntensity;
    #endif

    return s.Trans * lightColor * (amount * strength);
}

inline half4 LightingPortSSS(SurfaceOutputPortSSS s, half3 viewDir, UnityGI gi)
{
    UnityLight light = gi.light;
    half3 N = s.Normal;
    half diff = max(0, dot(N, light.dir));

    half4 c;
    c.rgb = s.Albedo * light.color * diff;

    #if defined(PORT_SSS_SPECULAR)
    half3 h = normalize(light.dir + viewDir);
    float nh = max(0, dot(N, h));
    float spec = pow(nh, s.Specular * 128.0) * s.Gloss;
    c.rgb += light.color * _SpecColor.rgb * spec;
    #endif

    c.rgb += PortSSSTranslucency(s, viewDir, light);

    #ifdef UNITY_LIGHT_FUNCTION_APPLY_INDIRECT
    c.rgb += s.Albedo * gi.indirect.diffuse;
    #endif

    c.a = s.Alpha;
    return c;
}

inline void LightingPortSSS_GI(SurfaceOutputPortSSS s, UnityGIInput data, inout UnityGI gi)
{
    gi = UnityGlobalIllumination(data, 1.0, s.Normal);
}

void PortSSSSurf(Input IN, inout SurfaceOutputPortSSS o)
{
    fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);
    fixed3 albedo = tex.rgb * _Color.rgb;

    o.Albedo = albedo;
    o.Alpha = tex.a * _Color.a;
    o.Normal = PortSSSNormal(tex2D(_BumpMap, IN.uv_BumpMap), _BumpSize);

    #if defined(PORT_SSS_SPECULAR)
    o.Specular = _Shininess;
    o.Gloss = tex.a;
    #else
    o.Specular = 0;
    o.Gloss = 0;
    #endif

    // The translucency map shares the main texture coordinates.
    o.Trans = tex2D(_TransMap, IN.uv_MainTex).rgb * _TransColor.rgb;

    half3 emission = albedo * _V_SSS_Emission;
    #if defined(V_SSS_RIM_ON)
    half nv = saturate(dot(normalize(IN.viewDir), o.Normal));
    emission += _V_SSS_Rim_Color.rgb * pow(1.0 - nv, max(_V_SSS_Rim_Pow, 1e-3));
    #endif
    o.Emission = emission;
}

#endif // PORT_SSS_INCLUDED
