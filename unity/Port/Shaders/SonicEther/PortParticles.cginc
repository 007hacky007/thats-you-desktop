// From-scratch reimplementation for the port.
// Shared helpers for the particle shaders (Sonic Ether additive, Shader Forge clip-safe and
// refraction, dust motes): one vertex layout, soft particle depth fade, camera near fade and
// the HDR emission boost used for bloom.
#ifndef PORT_PARTICLES_INCLUDED
#define PORT_PARTICLES_INCLUDED

#include "UnityCG.cginc"

// Vertex input produced by the legacy ParticleSystemRenderer (billboards and meshes).
struct PortParticleAppData {
    float4 vertex : POSITION;
    fixed4 color : COLOR;
    float2 texcoord : TEXCOORD0;
};

// Eye depth of the vertex and its screen position, for soft particles and near fades.
// projPos.z holds the positive view-space depth.
inline float4 PortParticleProjPos(float4 clipPos, float4 objVertex)
{
    float4 projPos = ComputeScreenPos(clipPos);
    projPos.z = -UnityObjectToViewPos(objVertex.xyz).z;
    return projPos;
}

#if defined(SOFTPARTICLES_ON)
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
#endif

// 1 where the particle is far in front of the scene, falling to 0 where it touches geometry.
// invFade is the material's "Soft Particles Factor"; larger values give a tighter fade.
inline float PortSoftParticleFade(float4 projPos, float invFade)
{
#if defined(SOFTPARTICLES_ON)
    float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(projPos)));
    return saturate(invFade * (sceneZ - projPos.z));
#else
    return 1.0;
#endif
}

// 0 at the camera, 1 once the particle is at least threshold units away, shaped by power.
// Keeps dust from filling the screen and popping when it crosses the near clip plane.
inline float PortNearFade(float eyeDepth, float threshold, float power)
{
    float d = saturate((eyeDepth - _ProjectionParams.y) / max(threshold, 1e-4));
    return pow(d, max(power, 1e-3));
}

// Brightness multiplier for HDR bloom. 0 keeps the plain particle look, 1 is about 5.7x,
// enough to push bright particles over the bloom threshold when the camera renders HDR.
inline half PortEmissionBoost(half gain)
{
    return exp2(gain * 2.5h);
}

// Sonic Ether style additive pass, shared by Additive and AdditiveUnderlay.
// colour = 2 * vertex colour * tint * texture, alpha drives the additive blend (SrcAlpha One),
// softened against scene depth and boosted by _EmissionGain for bloom. Fog fades to black.
#if defined(PORT_SE_ADDITIVE_PASS)
sampler2D _MainTex;
float4 _MainTex_ST;
fixed4 _TintColor;
float _InvFade;
half _EmissionGain;

struct PortSEV2F {
    float4 pos : SV_POSITION;
    fixed4 color : COLOR;
    float2 uv : TEXCOORD0;
    UNITY_FOG_COORDS(1)
#if defined(SOFTPARTICLES_ON)
    float4 projPos : TEXCOORD2;
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};

PortSEV2F PortSEVert(PortParticleAppData v)
{
    PortSEV2F o;
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.pos = UnityObjectToClipPos(v.vertex);
#if defined(SOFTPARTICLES_ON)
    o.projPos = PortParticleProjPos(o.pos, v.vertex);
#endif
    o.color = v.color;
    o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
    UNITY_TRANSFER_FOG(o, o.pos);
    return o;
}

half4 PortSEFrag(PortSEV2F i) : SV_Target
{
    half4 col = 2.0h * (half4)i.color * (half4)_TintColor * (half4)tex2D(_MainTex, i.uv);
#if defined(SOFTPARTICLES_ON)
    col.a *= PortSoftParticleFade(i.projPos, _InvFade);
#endif
    col.a = saturate(col.a);
    col.rgb *= PortEmissionBoost(_EmissionGain);
    UNITY_APPLY_FOG_COLOR(i.fogCoord, col, half4(0, 0, 0, 0));
    return col;
}
#endif // PORT_SE_ADDITIVE_PASS

#endif // PORT_PARTICLES_INCLUDED
