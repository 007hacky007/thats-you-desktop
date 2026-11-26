// From-scratch reimplementation for the port: shared code of the three
// "Hidden/Amplify Occlusion/*" shaders driven by AmplifyOcclusionBase.cs.
//
// Conventions used here:
// - Passes drawn with the blit mesh (CommandBuffer.DrawMesh, vertices 0..1)
//   ignore the camera matrices and cover the whole bound target. They address
//   textures in memory pixel coordinates taken from SV_POSITION, so a texel at
//   pixel p of the target maps to texel p of any same-size render texture on
//   every graphics API (all render textures share Unity's orientation).
// - Passes drawn with CommandBuffer.Blit (the non cache-aware occlusion passes)
//   use Unity's blit quad and its texcoords.
// - "screen uv" is uv over the unpadded camera image, v = 0 at the bottom.
//   The working buffers may be padded to a multiple of 4 on the right / end
//   rows; _AO_Buffer_PadScale converts buffer uv to screen uv.
// - View space used for the estimator: x right, y up, z = positive eye depth.
//
// The estimator is a horizon based ambient occlusion: per pixel, a set of
// rotated directions is marched in screen space for a projected radius and
// every depth sample contributes a cosine weighted, distance attenuated term.

#ifndef PORT_AMPLIFY_OCCLUSION_INCLUDED
#define PORT_AMPLIFY_OCCLUSION_INCLUDED

#include "UnityCG.cginc"

#define AO_NORMALS_NONE 0
#define AO_NORMALS_CAMERA 1
#define AO_NORMALS_GBUFFER 2
#define AO_NORMALS_GBUFFER_OCTA 3

UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
sampler2D _CameraDepthNormalsTexture;
sampler2D _CameraGBufferTexture2;

float4x4 _AO_CameraView;
float4 _AO_UVToView;
float _AO_NegRcpR2;
float _AO_RadiusToScreen;
float _AO_PowExponent;
float _AO_Bias;
float _AO_Multiplier;
float4 _AO_Levels;
float _AO_BlurDepthThreshold;
float _AO_BlurFalloff;

float4 _AO_Buffer_PadScale;
float4 _AO_Buffer_TexelSize;
float4 _AO_QuarterBuffer_TexelSize;
float _AO_MaxRadiusPixels;

float4 _AO_Target_TexelSize;
float4 _AO_Target_Position;
float4 _AO_Source_TexelSize;

float4 _AO_LayerOffset;
float4 _AO_LayerOffset0;
float4 _AO_LayerOffset1;
float4 _AO_LayerOffset2;
float4 _AO_LayerOffset3;
float4 _AO_LayerRandom;

sampler2D _AO_DepthTexture;
sampler2D _AO_NormalTexture;
sampler2D _AO_RandomTexture;
sampler2D _AO_OcclusionAtlas;
sampler2D _AO_OcclusionTexture;
sampler2D _AO_Source;
sampler2D _AO_GBufferAlbedo;
sampler2D _AO_GBufferEmission;

struct AOAppdata
{
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
};

struct AOV2F
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

// Blit mesh drawn with DrawMesh: cover the bound target.
AOV2F AOVertQuad(AOAppdata v)
{
    AOV2F o;
    o.pos = float4(v.vertex.xy * 2.0 - 1.0, 0.5, 1.0);
    o.uv = v.texcoord;
    return o;
}

// Unity blit quad (CommandBuffer.Blit).
AOV2F AOVertBlit(AOAppdata v)
{
    AOV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    return o;
}

inline float4 AOTex(sampler2D s, float2 uv)
{
    return tex2Dlod(s, float4(uv, 0.0, 0.0));
}

// ---------------------------------------------------------------------------
// Geometry helpers

inline bool AOIsOrtho()
{
    return unity_OrthoParams.w > 0.5;
}

inline float2 AOFullSize()
{
    return _AO_Buffer_TexelSize.zw / _AO_Buffer_PadScale.xy;
}

inline float2 AOFullTexel()
{
    return _AO_Buffer_TexelSize.xy * _AO_Buffer_PadScale.xy;
}

float AOLinearDepth(float raw)
{
    if (AOIsOrtho())
    {
#if defined(UNITY_REVERSED_Z)
        raw = 1.0 - raw;
#endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
    }
    return LinearEyeDepth(raw);
}

inline float AOSceneDepth(float2 suv)
{
    return AOLinearDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(suv, 0.0, 0.0)));
}

inline bool AOIsFar(float z)
{
    return z >= _ProjectionParams.z * 0.999;
}

float3 AOViewPos(float2 suv, float z)
{
    float2 ndc = suv * 2.0 - 1.0;
    if (AOIsOrtho())
        return float3(ndc * unity_OrthoParams.xy, z);
    // _AO_UVToView.w is tan(fov / 2); the horizontal extent follows the
    // unpadded image aspect.
    float2 fullSize = AOFullSize();
    float tanY = _AO_UVToView.w;
    float2 tanHalf = float2(tanY * fullSize.x / max(fullSize.y, 1.0), tanY);
    return float3(ndc * tanHalf * z, z);
}

// Unity view space (right handed, camera looks down -z) to estimator space.
inline float3 AOFromUnityView(float3 n)
{
    return float3(n.x, n.y, -n.z);
}

inline float3 AOWorldNormalToView(float3 nw)
{
    float3 nv = mul((float3x3)_AO_CameraView, nw);
    return normalize(AOFromUnityView(nv) + 1e-6);
}

float3 AOOctaDecode(float2 e)
{
    float3 n = float3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
    {
        float2 s = float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
        n.xy = (1.0 - abs(n.yx)) * s;
    }
    return normalize(n);
}

float3 AONormalFromDepth(float2 suv, float3 P)
{
    float2 t = AOFullTexel();
    float2 uvR = suv + float2(t.x, 0.0);
    float2 uvL = suv - float2(t.x, 0.0);
    float2 uvU = suv + float2(0.0, t.y);
    float2 uvD = suv - float2(0.0, t.y);
    float3 pR = AOViewPos(uvR, AOSceneDepth(uvR));
    float3 pL = AOViewPos(uvL, AOSceneDepth(uvL));
    float3 pU = AOViewPos(uvU, AOSceneDepth(uvU));
    float3 pD = AOViewPos(uvD, AOSceneDepth(uvD));
    // Take the side with the smaller depth step to stay off silhouettes.
    float3 dx = abs(pR.z - P.z) < abs(P.z - pL.z) ? pR - P : P - pL;
    float3 dy = abs(pU.z - P.z) < abs(P.z - pD.z) ? pU - P : P - pD;
    float3 n = cross(dy, dx);
    n = normalize(n + float3(0.0, 0.0, -1e-6));
    return n.z > 0.0 ? -n : n;
}

float3 AOFetchNormal(int source, float2 suv, float3 P)
{
    if (source == AO_NORMALS_CAMERA)
    {
        float4 enc = AOTex(_CameraDepthNormalsTexture, suv);
        return normalize(AOFromUnityView(DecodeViewNormalStereo(enc)));
    }
    if (source == AO_NORMALS_GBUFFER)
    {
        float3 nw = AOTex(_CameraGBufferTexture2, suv).xyz * 2.0 - 1.0;
        return AOWorldNormalToView(nw);
    }
    if (source == AO_NORMALS_GBUFFER_OCTA)
    {
        float2 e = AOTex(_CameraGBufferTexture2, suv).xy * 2.0 - 1.0;
        return AOWorldNormalToView(AOOctaDecode(e));
    }
    return AONormalFromDepth(suv, P);
}

// Screen uv of the full resolution pixel a deinterleaved layer texel stands for.
inline float2 AOLayerToScreen(float2 luv, float2 layerOffset)
{
    float2 fullPx = (luv * _AO_QuarterBuffer_TexelSize.zw - 0.5) * 4.0 + layerOffset;
    return fullPx * AOFullTexel();
}

// Screen uv of the full resolution pixel read by a deinterleave pass.
inline float2 AODeinterleaveScreenUV(float2 quarterPixel, float2 layerOffset)
{
    float2 fullPx = floor(quarterPixel) * 4.0 + layerOffset;
    return fullPx * AOFullTexel();
}

// ---------------------------------------------------------------------------
// Estimator

inline float2 AORotate(float2 d, float2 cs)
{
    return float2(d.x * cs.x - d.y * cs.y, d.x * cs.y + d.y * cs.x);
}

inline float AOSampleTerm(float3 P, float3 N, float3 S)
{
    float3 V = S - P;
    float vv = dot(V, V);
    float nv = dot(N, V) * rsqrt(max(vv, 1e-10));
    return saturate(nv - _AO_Bias) * saturate(vv * _AO_NegRcpR2 + 1.0);
}

// layered: march the deinterleaved layer (_AO_DepthTexture) in layer texels,
// otherwise march the full resolution depth texture.
float AOEstimate(bool layered, float2 baseUV, float3 P, float3 N, float4 rnd, int dirs, int steps)
{
    float radiusPx = AOIsOrtho() ? _AO_RadiusToScreen : _AO_RadiusToScreen / P.z;
    radiusPx = min(radiusPx, _AO_MaxRadiusPixels);
    if (layered)
        radiusPx *= 0.25;
    if (radiusPx < 1.0)
        return 1.0;

    float stepPx = radiusPx / (steps + 1.0);
    // The random rotation spans an eighth of a turn; with four directions
    // double it so it covers their whole sector.
    float2 rot = rnd.xy;
    if (dirs == 4)
        rot = float2(rot.x * rot.x - rot.y * rot.y, 2.0 * rot.x * rot.y);

    float2 texel = layered ? _AO_QuarterBuffer_TexelSize.xy : AOFullTexel();
    float sum = 0.0;
    for (int d = 0; d < dirs; d++)
    {
        float a = (6.28318530718 / dirs) * d;
        float2 dir = AORotate(float2(cos(a), sin(a)), rot);
        float ray = rnd.z * stepPx + 1.0;
        for (int s = 0; s < steps; s++)
        {
            float2 uvS = baseUV + round(dir * ray) * texel;
            float3 S;
            if (layered)
            {
                float zS = AOTex(_AO_DepthTexture, uvS).r;
                S = AOViewPos(AOLayerToScreen(uvS, _AO_LayerOffset.xy), zS);
            }
            else
            {
                S = AOViewPos(uvS, AOSceneDepth(uvS));
            }
            sum += AOSampleTerm(P, N, S);
            ray += stepPx;
        }
    }
    sum *= _AO_Multiplier / (dirs * steps);
    float ao = saturate(1.0 - sum * 2.0);
    return pow(ao, _AO_PowExponent);
}

// Full resolution (or downsampled) occlusion, drawn with CommandBuffer.Blit.
float2 AOOcclusionFull(AOV2F i, int dirs, int steps, int normals)
{
    float2 suv = i.uv * _AO_Buffer_PadScale.xy;
    float z = AOSceneDepth(suv);
    if (AOIsFar(z))
        return float2(1.0, z);
    float3 P = AOViewPos(suv, z);
    float3 N = AOFetchNormal(normals, suv, P);
    float4 rnd = AOTex(_AO_RandomTexture, i.pos.xy * 0.25);
    return float2(AOEstimate(false, suv, P, N, rnd, dirs, steps), z);
}

// Occlusion of one deinterleaved layer (cache-aware path).
float2 AOOcclusionLayer(AOV2F i, int dirs, int steps)
{
    float2 luv = i.pos.xy * _AO_QuarterBuffer_TexelSize.xy;
    float z = AOTex(_AO_DepthTexture, luv).r;
    if (AOIsFar(z))
        return float2(1.0, z);
    float3 P = AOViewPos(AOLayerToScreen(luv, _AO_LayerOffset.xy), z);
    float3 N = normalize(AOTex(_AO_NormalTexture, luv).xyz * 2.0 - 1.0 + 1e-6);
    return float2(AOEstimate(true, luv, P, N, _AO_LayerRandom, dirs, steps), z);
}

// ---------------------------------------------------------------------------
// Deinterleave / reinterleave

inline float AODeinterleaveDepth(float2 quarterPixel, float2 layerOffset)
{
    return AOSceneDepth(AODeinterleaveScreenUV(quarterPixel, layerOffset));
}

float4 AODeinterleaveNormal(float2 quarterPixel, float2 layerOffset, int normals)
{
    float2 suv = AODeinterleaveScreenUV(quarterPixel, layerOffset);
    float3 N;
    if (normals == AO_NORMALS_NONE)
    {
        float3 P = AOViewPos(suv, AOSceneDepth(suv));
        N = AONormalFromDepth(suv, P);
    }
    else
    {
        N = AOFetchNormal(normals, suv, float3(0.0, 0.0, 1.0));
    }
    return float4(N * 0.5 + 0.5, 1.0);
}

float2 AOReinterleave(float2 pixel)
{
    float2 p = floor(pixel);
    float2 layer = p - floor(p * 0.25) * 4.0;
    float2 q = floor(p * 0.25);
    float2 atlasPx = layer * _AO_QuarterBuffer_TexelSize.zw + q + 0.5;
    return AOTex(_AO_OcclusionAtlas, atlasPx * _AO_Buffer_TexelSize.xy).rg;
}

// Depth aware upsample of the half resolution occlusion.
float2 AOCombineDownsampled(float2 pixel)
{
    float2 bufUV = pixel * _AO_Buffer_TexelSize.xy;
    float z = AOSceneDepth(bufUV * _AO_Buffer_PadScale.xy);
    float2 smallPos = bufUV * _AO_Source_TexelSize.zw - 0.5;
    float2 base = floor(smallPos);
    float2 f = smallPos - base;
    float sum = 0.0;
    float wsum = 0.0;
    for (int k = 0; k < 4; k++)
    {
        float2 o = float2(k & 1, k >> 1);
        float2 s = AOTex(_AO_Source, (base + o + 0.5) * _AO_Source_TexelSize.xy).rg;
        float2 bw = lerp(1.0 - f, f, o);
        float w = bw.x * bw.y / (1e-3 + abs(s.g - z) / max(z, 1e-3));
        sum += s.r * w;
        wsum += w;
    }
    return float2(wsum > 0.0 ? sum / wsum : 1.0, z);
}

// ---------------------------------------------------------------------------
// Bilateral blur

float2 AOBlur(float2 pixel, float2 axis, int radius)
{
    float2 t = _AO_Source_TexelSize.xy;
    float2 uv = pixel * t;
    float2 c = AOTex(_AO_Source, uv).rg;
    float sharp = _AO_BlurFalloff * 4.0 / (radius * radius);
    float rcpThr = 1.0 / max(_AO_BlurDepthThreshold, 1e-4);
    float sum = c.r;
    float wsum = 1.0;
    for (int x = 1; x <= radius; x++)
    {
        float ws = exp2(-sharp * x * x);
        float2 a = AOTex(_AO_Source, uv + axis * (x * t)).rg;
        float2 b = AOTex(_AO_Source, uv - axis * (x * t)).rg;
        float da = (a.g - c.g) * rcpThr;
        float db = (b.g - c.g) * rcpThr;
        float wa = ws * exp2(-da * da);
        float wb = ws * exp2(-db * db);
        sum += a.r * wa + b.r * wb;
        wsum += wa + wb;
    }
    return float2(sum / wsum, c.g);
}

// ---------------------------------------------------------------------------
// Apply

// Buffer uv of the occlusion texel for a full resolution target pixel. A
// camera target that is not a flipped render texture (the back buffer on
// top-left origin APIs) is stored the other way up.
float2 AOApplyBufferUV(float2 pixel)
{
    float2 p = pixel;
#if UNITY_UV_STARTS_AT_TOP
    if (_ProjectionParams.x > 0.0)
        p.y = _AO_Target_TexelSize.w - p.y;
#endif
    return p * _AO_Buffer_TexelSize.xy;
}

inline float AOApplyScalar(float2 pixel)
{
    float ao = AOTex(_AO_OcclusionTexture, AOApplyBufferUV(pixel)).r;
    return lerp(1.0, ao, _AO_Levels.a);
}

inline float3 AOTint(float occ)
{
    return lerp(_AO_Levels.rgb, float3(1.0, 1.0, 1.0), occ);
}

#endif
