// From-scratch reimplementation for the port: "Hidden/Image Effects/Cinematic/AmbientOcclusion".
// Pass layout expected by UnityStandardAssets.CinematicEffects.AmbientOcclusion:
//   0 occlusion estimate (writes the occlusion amount, 0 = open, 1 = fully occluded, to R)
//   1 separable depth/normal aware blur along _BlurVector (in texels)
//   2 forward composite: source color * (1 - occlusion)
//   3 ambient-only composite drawn as a mesh into the GBuffer0 + light buffer MRT;
//     it multiplies (1 - occlusion) into GBuffer0 alpha (material occlusion) and the
//     ambient/emission buffer
//   4 debug view of (1 - occlusion)
// Keywords: _SAMPLECOUNT_LOWEST (fixed 3 samples, otherwise _SampleCount) and
// _SOURCE_GBUFFER (depth from _CameraDepthTexture, normals from GBuffer2 instead of
// _CameraDepthNormalsTexture).
// The estimate is a plain normal-oriented hemisphere test in camera space: random points
// within _Radius around the surface are projected back to the screen, and each scene point
// found there occludes in proportion to how far it lies above the tangent plane, falling
// off with its squared distance.
Shader "Hidden/Image Effects/Cinematic/AmbientOcclusion" {
Properties {
 _MainTex ("", 2D) = "" { }
 _OcclusionTexture ("", 2D) = "" { }
}

CGINCLUDE
#include "../PortMisc.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;
sampler2D _OcclusionTexture;
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
sampler2D _CameraDepthNormalsTexture;
sampler2D _CameraGBufferTexture2;

float _Intensity;
float _Radius;
float _TargetScale;
int _SampleCount;
float2 _BlurVector;

// Camera space (z forward) eye depth and normal of the scene at uv.
void SampleScene(float2 uv, out float depth, out float3 normal)
{
#if defined(_SOURCE_GBUFFER)
    depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
    float3 nw = tex2D(_CameraGBufferTexture2, uv).xyz * 2.0 - 1.0;
    normal = mul((float3x3)unity_WorldToCamera, nw);
#else
    float d01;
    float3 nv;
    DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, uv), d01, nv);
    depth = d01 * _ProjectionParams.z;
    // Encoded normals are in GL view space (z towards the viewer).
    normal = float3(nv.xy, -nv.z);
#endif
    normal = normalize(normal + 1e-6);
}

float SceneDepth(float2 uv)
{
#if defined(_SOURCE_GBUFFER)
    return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
#else
    float d01;
    float3 nv;
    DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, uv), d01, nv);
    return d01 * _ProjectionParams.z;
#endif
}

// Random offset inside the unit sphere for sample i of count, per pixel seed.
float3 SampleOffset(int i, int count, float2 seed)
{
    float fi = (float)i;
    float u = PortHash(seed + fi * float2(1.37, 7.11)) * 2.0 - 1.0;
    float theta = PortHash(seed * 1.91 + fi * float2(3.17, 0.53)) * UNITY_TWO_PI;
    float3 dir = float3(sqrt(1.0 - u * u) * float2(cos(theta), sin(theta)), u);
    // Spread lengths over the radius, denser close to the center.
    float len = (fi + 1.0) / (float)count;
    len = lerp(0.1, 1.0, len * len);
    return dir * len;
}

float4 fragEstimate(v2f_img i) : SV_Target
{
    float depth;
    float3 n;
    SampleScene(i.uv, depth, n);
    if (depth >= _ProjectionParams.z * 0.999)
        return 0;

    float3 p = PortViewPosFromDepth(i.uv, depth);
    float2 seed = floor(i.uv * _ScreenParams.xy / max(_TargetScale, 1e-3));

#if defined(_SAMPLECOUNT_LOWEST)
    const int count = 3;
#else
    int count = max(_SampleCount, 1);
#endif

    float occ = 0.0;
    [loop]
    for (int s = 0; s < count; s++)
    {
        float3 d = SampleOffset(s, count, seed);
        // Flip into the hemisphere around the normal.
        d = dot(d, n) < 0.0 ? -d : d;
        float3 q = p + d * _Radius;
        float2 quv = PortUVFromViewPos(q);
        float3 sp = PortViewPosFromDepth(quv, SceneDepth(quv));
        float3 v = sp - p;
        float vv = dot(v, v);
        float a = max(dot(v, n) - 0.002 * depth, 0.0) / (vv + 0.001 * _Radius * _Radius + 1e-5);
        // Ignore geometry far outside the radius (avoids halos around silhouettes).
        a *= saturate(1.0 - vv / (4.0 * _Radius * _Radius));
        occ += a;
    }
    occ *= _Radius * _Intensity / (float)count;
    return saturate(occ);
}

float4 fragBlur(v2f_img i) : SV_Target
{
    float2 texStep = _MainTex_TexelSize.xy * _BlurVector * 2.0;
    float depth0;
    float3 n0;
    SampleScene(i.uv, depth0, n0);

    float sum = tex2D(_MainTex, i.uv).r * 0.3;
    float wsum = 0.3;
    [unroll]
    for (int k = 1; k <= 3; k++)
    {
        float wg = k == 1 ? 0.22 : (k == 2 ? 0.1 : 0.03);
        [unroll]
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            float2 uv = i.uv + texStep * (float)(k * sgn);
            float d;
            float3 nn;
            SampleScene(uv, d, nn);
            float wd = saturate(1.0 - abs(d - depth0) / max(depth0 * 0.05, 1e-3));
            float wn = saturate(dot(nn, n0));
            float w = wg * wd * wn * wn;
            sum += tex2D(_MainTex, uv).r * w;
            wsum += w;
        }
    }
    return sum / wsum;
}

float4 fragComposite(v2f_img i) : SV_Target
{
    float4 c = tex2D(_MainTex, i.uv);
    c.rgb *= 1.0 - tex2D(_OcclusionTexture, i.uv).r;
    return c;
}

float4 fragDebug(v2f_img i) : SV_Target
{
    float ao = 1.0 - tex2D(_OcclusionTexture, i.uv).r;
    return float4(ao, ao, ao, 1.0);
}

struct v2fQuad
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

// The quad is given directly in clip space.
v2fQuad vertQuad(float4 vertex : POSITION)
{
    v2fQuad o;
    o.pos = float4(vertex.xy, 0.0, 1.0);
    o.uv = vertex.xy * 0.5 + 0.5;
    if (_ProjectionParams.x < 0.0)
        o.uv.y = 1.0 - o.uv.y;
    return o;
}

struct MRTOut
{
    float4 gbuffer0 : SV_Target0;
    float4 light : SV_Target1;
};

MRTOut fragGBuffer(v2fQuad i)
{
    float ao = 1.0 - tex2D(_OcclusionTexture, i.uv).r;
    MRTOut o;
    o.gbuffer0 = float4(1.0, 1.0, 1.0, ao);
    o.light = float4(ao, ao, ao, 1.0);
    return o;
}
ENDCG

SubShader {
    Cull Off ZWrite Off ZTest Always

    // 0 occlusion estimate
    Pass {
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment fragEstimate
        #pragma target 3.0
        #pragma multi_compile _ _SAMPLECOUNT_LOWEST
        #pragma multi_compile _ _SOURCE_GBUFFER
        ENDCG
    }

    // 1 bilateral blur
    Pass {
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment fragBlur
        #pragma target 3.0
        #pragma multi_compile _ _SOURCE_GBUFFER
        ENDCG
    }

    // 2 forward composite
    Pass {
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment fragComposite
        #pragma target 3.0
        ENDCG
    }

    // 3 deferred ambient-only composite (multiplicative into GBuffer0 and the light buffer)
    Pass {
        Blend DstColor Zero
        CGPROGRAM
        #pragma vertex vertQuad
        #pragma fragment fragGBuffer
        #pragma target 3.0
        ENDCG
    }

    // 4 debug view
    Pass {
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment fragDebug
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
