// From-scratch reimplementation for the port.
// Shared code for "AFS/Foliage Shader v4" and its touch bending variant: alpha-tested,
// double-sided foliage (dry grass, ferns) with wind bending, translucency and specular.
//
// Wind globals come from SetupAdvancedFoliageShader (one per scene):
//   _Wind             xyz world wind direction, w strength
//   _AfsTimeFrequency x main wind time, y secondary bending time, zw leaf turbulence frequencies
//   _AfsWaveSize      spatial frequency of the gust wave rolling over the scene
// Vertex colours drive the bending (_BendingControls "Vertex Colors"):
//   r phase variation, g edge flutter, b main and secondary bending, a ambient occlusion.
// "Vertex Colors And UV4" takes main (x) and secondary (y) bending from the fourth UV set.
// _BumpTransSpecMap packs normal X/Y in A/G, translucency mask in R and smoothness in B.
// The texture is imported as sRGB in this project, so in linear colour space the colour channels are
// converted back to their stored values (_PortNtsIsSRGB, default on).
// With PORT_AFS_TOUCHBENDING the plant also bends away from a touching player: touchBendingCollisionGS
// writes _RotMatrix (world rotation around the plant pivot) and _TouchBendingForce
// (xyz direction, w push strength).
// Wrappers declare: #pragma surface PortAfsSurf PortAfs vertex:PortAfsVert alphatest:_Cutoff addshadow fullforwardshadows

#ifndef PORT_AFS_INCLUDED
#define PORT_AFS_INCLUDED

#include "UnityCG.cginc"
#include "UnityPBSLighting.cginc"

sampler2D _MainTex;
sampler2D _BumpTransSpecMap;
fixed4 _SpecularReflectivity;
fixed4 _TranslucencyColor;
half _TranslucencyViewDependency;
half _LeafTurbulence;
half _PortNtsIsSRGB;

float4 _Wind;
float4 _AfsTimeFrequency;
float _AfsWaveSize;

#if defined(PORT_AFS_TOUCHBENDING)
float4x4 _RotMatrix;
float4 _TouchBendingForce;
#endif

struct Input
{
    float2 uv_MainTex;
    float4 color : COLOR;
    float facing : VFACE;
};

struct SurfaceOutputPortAfs
{
    fixed3 Albedo;
    fixed3 Specular;
    float3 Normal;
    half3 Emission;
    half Smoothness;
    half Occlusion;
    fixed Alpha;
    half Translucency;
};

// Smoothed triangle wave in [-1, 1] with period 1.
inline float4 PortAfsWave(float4 x)
{
    float4 t = abs(frac(x + 0.5) * 2.0 - 1.0);
    return t * t * (3.0 - 2.0 * t) * 2.0 - 1.0;
}

void PortAfsVert(inout appdata_full v)
{
    float3 pos = v.vertex.xyz;

    float phase = v.color.r;
    float flutter = v.color.g;
    #if defined(_BENDINGCONTROLS_VERTEX_COLORS_AND_UV4)
    float mainBend = v.texcoord3.x;
    float secondaryBend = v.texcoord3.y;
    #else
    float mainBend = v.color.b;
    float secondaryBend = v.color.b;
    #endif

    float3 pivotWS = float3(unity_ObjectToWorld[0].w, unity_ObjectToWorld[1].w, unity_ObjectToWorld[2].w);
    float3 posWS = mul(unity_ObjectToWorld, float4(pos, 1.0)).xyz;
    float objPhase = dot(pivotWS, float3(1.0, 1.0, 1.0));

    float3 windOS = mul((float3x3)unity_WorldToObject, _Wind.xyz);
    float strength = _Wind.w;

    // Main bending: the whole plant leans with the wind, pulsing with a gust wave that travels
    // across the world. Length is kept so the plant bends instead of stretching.
    float gust = sin(_AfsTimeFrequency.x + (posWS.x + posWS.z) * _AfsWaveSize + objPhase * 0.25);
    float3 bent = pos + windOS * (mainBend * strength * (0.7 + 0.3 * gust));
    float len = length(pos);
    if (len > 1e-4)
        bent = normalize(bent) * len;

    // Secondary bending and leaf flutter.
    float branchPhase = objPhase + phase;
    float vertexPhase = dot(pos, float3(1.0, 1.0, 1.0) * (flutter + branchPhase));
    float2 t = _AfsTimeFrequency.yy + float2(vertexPhase, branchPhase);
    float4 freq = float4(1.975, 0.793, _AfsTimeFrequency.z, _AfsTimeFrequency.w);
    float4 waves = PortAfsWave(t.xxyy * freq);
    float2 sum = waves.xz + waves.yw;

    float3 detail = v.normal.xyz * (flutter * 0.1 * (0.5 + _LeafTurbulence) * sum.x);
    detail.y += secondaryBend * 0.3 * sum.y;
    detail += windOS * (secondaryBend * sum.y * 0.5);
    bent += detail * strength;

    #if defined(PORT_AFS_TOUCHBENDING)
    // The script only writes _RotMatrix while the plant is touched; ignore an unset (zero) matrix.
    float3x3 rot = (float3x3)_RotMatrix;
    if (dot(rot[0], rot[0]) > 0.5)
    {
        float3 offWS = mul((float3x3)unity_ObjectToWorld, bent);
        offWS = mul(rot, offWS);
        offWS += _TouchBendingForce.xyz * (_TouchBendingForce.w * mainBend * 0.25);
        bent = mul((float3x3)unity_WorldToObject, offWS);
    }
    #endif

    v.vertex.xyz = bent;
}

inline SurfaceOutputStandardSpecular PortAfsToStandard(SurfaceOutputPortAfs s)
{
    SurfaceOutputStandardSpecular o;
    o.Albedo = s.Albedo;
    o.Specular = s.Specular;
    o.Normal = s.Normal;
    o.Emission = s.Emission;
    o.Smoothness = s.Smoothness;
    o.Occlusion = s.Occlusion;
    o.Alpha = s.Alpha;
    return o;
}

inline half4 LightingPortAfs(SurfaceOutputPortAfs s, float3 viewDir, UnityGI gi)
{
    half4 c = LightingStandardSpecular(PortAfsToStandard(s), viewDir, gi);

    half3 N = normalize(s.Normal);
    half3 L = gi.light.dir;
    // Thin blades shadow themselves when backlit; let half of the sun reach the translucency.
    half isDirectional = 1.0 - saturate(_WorldSpaceLightPos0.w);
    half3 lightColor = lerp(gi.light.color, lerp(gi.light.color, _LightColor0.rgb, 0.5), isDirectional);

    half back = saturate(dot(-N, L));
    half towards = saturate(dot(viewDir, -L));
    half amount = lerp(back, towards * towards, _TranslucencyViewDependency);
    c.rgb += s.Albedo * _TranslucencyColor.rgb * lightColor * (amount * s.Translucency);
    return c;
}

inline void LightingPortAfs_GI(SurfaceOutputPortAfs s, UnityGIInput data, inout UnityGI gi)
{
    LightingStandardSpecular_GI(PortAfsToStandard(s), data, gi);
}

void PortAfsSurf(Input IN, inout SurfaceOutputPortAfs o)
{
    fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
    half4 nts = tex2D(_BumpTransSpecMap, IN.uv_MainTex);
    #if !defined(UNITY_COLORSPACE_GAMMA)
    nts.rgb = lerp(nts.rgb, LinearToGammaSpace(nts.rgb), saturate(_PortNtsIsSRGB));
    #endif

    half3 n;
    n.xy = nts.ag * 2.0 - 1.0;
    n.z = sqrt(saturate(1.0 - dot(n.xy, n.xy)));
    n = IN.facing > 0 ? n : -n;

    o.Albedo = c.rgb;
    o.Alpha = c.a;
    o.Normal = n;
    o.Specular = _SpecularReflectivity.rgb;
    o.Smoothness = nts.b;
    o.Occlusion = IN.color.a;
    o.Translucency = nts.r;
    o.Emission = 0;
}

#endif // PORT_AFS_INCLUDED
