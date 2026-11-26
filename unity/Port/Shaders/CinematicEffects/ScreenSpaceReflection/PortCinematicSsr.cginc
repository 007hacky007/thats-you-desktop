// From-scratch reimplementation for the port: shared code for
// "Hidden/ScreenSpaceReflection" (Cinematic Effects SSR driver).
//
// Buffer conventions used by this implementation:
//   _NormalAndRoughnessTexture  rgb = encoded world normal (GBuffer2), a = perceptual roughness
//   _HitPointTexture            xy = hit uv, z = confidence (0 miss / 1 hit), w = view-space ray length
//   _ReflectionTexture0..4      rgb = reflected radiance (not premultiplied), a = weight
//   _FinalReflectionTexture     rgb = radiance * weight (premultiplied), a = weight
#ifndef PORT_CINEMATIC_SSR_INCLUDED
#define PORT_CINEMATIC_SSR_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;

UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
sampler2D _CameraGBufferTexture0;
sampler2D _CameraGBufferTexture1;
sampler2D _CameraGBufferTexture2;

sampler2D _NormalAndRoughnessTexture;
sampler2D _HitPointTexture;
sampler2D _ReflectionTexture0;
sampler2D _ReflectionTexture1;
sampler2D _ReflectionTexture2;
sampler2D _ReflectionTexture3;
sampler2D _ReflectionTexture4;
sampler2D _FinalReflectionTexture;
sampler2D _AverageRayDistanceBuffer;
sampler2D _PreviousReflectionTexture;
sampler2D_float _PreviousCSZBuffer;

float4 _ProjInfo;
float4x4 _ProjectToPixelMatrix;
float4x4 _WorldToCameraMatrix;
float4x4 _CameraToWorldMatrix;
float4x4 _CurrentCameraToPreviousCamera;
float4 _CameraClipInfo;
float4 _ScreenSize;
float4 _InvScreenSize;
float4 _ReflectionBufferSize;
float4 _SourceToTempUV;
float4 _Axis;

float _MaxRayTraceDistance;
float _LayerThickness;
float _MaxSteps;
float _FadeDistance;
float _ScreenEdgeFading;
float _FresnelFade;
float _FresnelFadePower;
float _SSRMultiplier;
float _MaxRoughness;
float _RoughnessFalloffRange;
float _DistanceBlur;
float _CurrentMipLevel;
float _LastMip;
float _MipBias;
float _PixelsPerMeterAtOneMeter;
float _TemporalAlpha;

// Flags arrive through Material.SetInt, which stores floats.
float _EnableSSR;
float _EnableRefine;
float _AdditiveReflection;
float _TraceBehindObjects;
float _TraceEverywhere;
float _TreatBackfaceHitAsMiss;
float _AllowBackwardsRays;
float _ImproveCorners;
float _UseOcclusion;
float _BilateralUpsampling;
float _HalfResolution;
float _FullResolutionFiltering;
float _HighlightSuppression;
float _UseTemporalConfidence;
float _UseEdgeDetector;
float _UseAverageRayDistance;
float _FallbackToSky;
float _DebugMode;

#define SSR_ON(f) ((f) > 0.5)

// Explicit-LOD fetch: safe inside branches and loops on every backend.
#define SsrTex(s, uv) tex2Dlod(s, float4((uv), 0, 0))

// Full-precision uv (UnityCG's v2f_img uses half2, too coarse for depth tests).
struct SsrV2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

SsrV2f SsrVert(appdata_img v)
{
    SsrV2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord.xy;
    return o;
}

// ---------------------------------------------------------------- helpers

float SsrMax3(float3 v) { return max(v.x, max(v.y, v.z)); }

float SsrRawDepth(float2 uv)
{
    return SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0));
}

bool SsrIsSky(float raw)
{
    return Linear01Depth(raw) > 0.9999;
}

float SsrEyeDepth(float2 uv)
{
    return LinearEyeDepth(SsrRawDepth(uv));
}

// Focal lengths of the camera projection, recovered from _ProjInfo
// (x = -2 / (width * P00), y = -2 / (height * P11)).
float2 SsrFocal()
{
    return float2(-2.0 / (_ScreenSize.x * _ProjInfo.x), -2.0 / (_ScreenSize.y * _ProjInfo.y));
}

// View-space position (camera looks down -Z) from a screen uv and an eye depth.
float3 SsrViewPos(float2 uv, float eye)
{
    float2 ndc = uv * 2.0 - 1.0;
    float2 xy = ndc * eye / SsrFocal();
    return float3(xy, -eye);
}

float3 SsrWorldNormalAt(float2 uv)
{
    float3 n = tex2Dlod(_CameraGBufferTexture2, float4(uv, 0, 0)).xyz * 2.0 - 1.0;
    return normalize(n + 1e-6);
}

float3 SsrToView(float3 worldDir)
{
    return normalize(mul((float3x3)_WorldToCameraMatrix, worldDir));
}

float SsrRoughnessAt(float2 uv)
{
    return 1.0 - tex2Dlod(_CameraGBufferTexture1, float4(uv, 0, 0)).a;
}

// Project a view-space point to pixel coordinates of the trace buffer.
// Returns xy = pixel, z = 1/w.
float3 SsrProjectToTrace(float3 p)
{
    float4 h = mul(_ProjectToPixelMatrix, float4(p, 1.0));
    float k = 1.0 / h.w;
    float2 scale = _ReflectionBufferSize.xy * _InvScreenSize.xy;
    return float3(h.xy * k * scale, k);
}

// Weight of a reflected sample that lands near the screen border.
float SsrEdgeFade(float2 uv)
{
    if (_ScreenEdgeFading <= 0.0)
        return 1.0;
    float2 e = min(uv, 1.0 - uv);
    return saturate(min(e.x, e.y) / _ScreenEdgeFading);
}

float SsrDistanceFade(float dist)
{
    return saturate((_MaxRayTraceDistance - dist) / max(_FadeDistance, 1e-4));
}

float SsrRoughnessFade(float r)
{
    return saturate((_MaxRoughness - r) / max(_RoughnessFalloffRange, 1e-4));
}

// ---------------------------------------------------------------- ray march

// Perspective-correct eye depth of the ray at parameter t.
float SsrRayEye(float3 q0, float3 q1, float k0, float k1, float t)
{
    return -lerp(q0.z, q1.z, t) / lerp(k0, k1, t);
}

float4 SsrTrace(float2 uv, float stride)
{
    float4 miss = float4(0, 0, 0, 0);
    if (!SSR_ON(_EnableSSR))
        return miss;

    float raw = SsrRawDepth(uv);
    if (SsrIsSky(raw))
        return miss;

    float rough = SsrRoughnessAt(uv);
    if (!SSR_ON(_TraceEverywhere) && rough >= _MaxRoughness)
        return miss;

    float eye = LinearEyeDepth(raw);
    float3 p = SsrViewPos(uv, eye);
    float3 n = SsrToView(SsrWorldNormalAt(uv));
    float3 v = normalize(p);
    float3 r = normalize(reflect(v, n));

    if (!SSR_ON(_AllowBackwardsRays) && r.z > 0.0)
        return miss;

    // Keep the ray in front of the near plane.
    float rayLen = _MaxRayTraceDistance;
    float nearZ = -_ProjectionParams.y;
    if (r.z > 0.0 && p.z + r.z * rayLen > nearZ)
        rayLen = max((nearZ - p.z) / r.z * 0.99, 0.0);
    if (rayLen <= 1e-4)
        return miss;

    float3 p0 = p + n * (0.002 * eye);
    float3 p1 = p0 + r * rayLen;

    float3 s0 = SsrProjectToTrace(p0);
    float3 s1 = SsrProjectToTrace(p1);
    float3 q0 = p0 * s0.z;
    float3 q1 = p1 * s1.z;

    float2 delta = s1.xy - s0.xy;
    float lenPx = max(max(abs(delta.x), abs(delta.y)), 1e-3);
    float dt = stride / lenPx;

    float thickness = _LayerThickness;
    int maxSteps = (int)clamp(_MaxSteps, 1.0, 512.0);

    float prevT = 0.0;
    float prevEye = -p0.z;
    float hitT = -1.0;
    float hitLo = 0.0;

    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float t = min(prevT + dt, 1.0);
        float2 s = lerp(s0.xy, s1.xy, t);
        if (s.x < 0.0 || s.y < 0.0 || s.x > _ReflectionBufferSize.x || s.y > _ReflectionBufferSize.y)
            break;

        float rayEye = SsrRayEye(q0, q1, s0.z, s1.z, t);
        float zMin = min(prevEye, rayEye);
        float zMax = max(prevEye, rayEye);
        float sceneEye = SsrEyeDepth(s / _ReflectionBufferSize.xy);

        if (zMax >= sceneEye)
        {
            if (zMin <= sceneEye + thickness)
            {
                hitT = t;
                hitLo = prevT;
                break;
            }
            if (!SSR_ON(_TraceBehindObjects))
                break;
        }

        prevT = t;
        prevEye = rayEye;
        if (t >= 1.0)
            break;
    }

    if (hitT < 0.0)
        return miss;

    // Binary refinement of the crossing inside the last step.
    int iters = SSR_ON(_EnableRefine) ? 6 : 3;
    float lo = hitLo;
    float hi = hitT;
    [loop]
    for (int j = 0; j < iters; j++)
    {
        float mid = 0.5 * (lo + hi);
        float2 sm = lerp(s0.xy, s1.xy, mid);
        float rayEye = SsrRayEye(q0, q1, s0.z, s1.z, mid);
        float sceneEye = SsrEyeDepth(sm / _ReflectionBufferSize.xy);
        if (rayEye >= sceneEye)
            hi = mid;
        else
            lo = mid;
    }

    float2 hitUV = lerp(s0.xy, s1.xy, hi) / _ReflectionBufferSize.xy;
    if (any(hitUV < 0.0) || any(hitUV > 1.0))
        return miss;

    float hitRaw = SsrRawDepth(hitUV);
    if (SsrIsSky(hitRaw))
        return miss;

    if (SSR_ON(_TreatBackfaceHitAsMiss))
    {
        float3 hn = SsrToView(SsrWorldNormalAt(hitUV));
        if (dot(hn, r) > 0.0)
            return miss;
    }

    // q and 1/w are linear in screen space; dividing them back gives the
    // true view-space hit point, so the stored distance is in metres.
    float3 hitP = lerp(q0, q1, hi) / lerp(s0.z, s1.z, hi);
    return float4(hitUV, 1.0, length(hitP - p0));
}

// ---------------------------------------------------------------- passes

float4 SsrFragTrace1(SsrV2f i) : SV_Target { return SsrTrace(i.uv, 1.0); }
float4 SsrFragTrace2(SsrV2f i) : SV_Target { return SsrTrace(i.uv, 2.0); }
float4 SsrFragTrace4(SsrV2f i) : SV_Target { return SsrTrace(i.uv, 4.0); }
float4 SsrFragTrace8(SsrV2f i) : SV_Target { return SsrTrace(i.uv, 8.0); }
float4 SsrFragTrace16(SsrV2f i) : SV_Target { return SsrTrace(i.uv, 16.0); }

// Fresnel-weighted specular reflectance of the surface at uv, with the same
// grazing term and surface reduction the Standard BRDF applies to probes.
float3 SsrReflectance(float3 spec, float smooth, float nv)
{
    float pr = 1.0 - smooth;
    float a = pr * pr;
    float reduction = 1.0 / (a * a + 1.0);
    float grazing = saturate(smooth + SsrMax3(spec));
    float f = pow(1.0 - nv, 5.0);
    return lerp(spec, grazing.xxx, f) * reduction;
}

// Radiance of the camera's reflection probe (unity_SpecCube0) along a world
// direction, at the mip a surface of the given perceptual roughness uses.
float3 SsrProbe(float3 worldDir, float rough)
{
    float lod = rough * (1.7 - 0.7 * rough) * 6.0;
    float4 c = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, worldDir, lod);
    return DecodeHDR(c, unity_SpecCube0_HDR);
}

// Blur level (0..4) of the reflection chain for a pixel: grows with roughness
// and with the distance the ray travelled before it hit.
float SsrBlurLevel(float2 uv, float eye, float rough)
{
    float dist = SSR_ON(_UseAverageRayDistance) ? SsrTex(_AverageRayDistanceBuffer, uv).r
                                                : SsrTex(_HitPointTexture, uv).w;
    float pxPerMeter = abs(_PixelsPerMeterAtOneMeter) / max(eye, 1e-3) * (_ReflectionBufferSize.x * _InvScreenSize.x);
    float footprint = rough * 8.0 + dist * rough * 0.5 * pxPerMeter * _DistanceBlur;
    return clamp(log2(max(footprint, 1.0)) + _MipBias, 0.0, 4.0);
}

// Pass 5: add the resolved reflections to the opaque image.
float4 SsrFragCompositeFinal(SsrV2f i) : SV_Target
{
    float4 src = SsrTex(_MainTex, i.uv);
    if (!SSR_ON(_EnableSSR))
        return src;

    float raw = SsrRawDepth(i.uv);
    if (SsrIsSky(raw))
        return src;

    float4 gb0 = SsrTex(_CameraGBufferTexture0, i.uv);
    float4 gb1 = SsrTex(_CameraGBufferTexture1, i.uv);
    float eye = LinearEyeDepth(raw);
    float3 p = SsrViewPos(i.uv, eye);
    float3 v = normalize(p);
    float3 n = SsrToView(SsrWorldNormalAt(i.uv));
    float nv = saturate(dot(n, -v));
    float rough = 1.0 - gb1.a;
    float occ = gb0.a;

    // refl.rgb is premultiplied by the hit weight refl.a.
    float4 refl = SsrTex(_FinalReflectionTexture, i.uv);
    float w = saturate(refl.a);
    float3 fres = SsrReflectance(gb1.rgb, gb1.a, nv);
    float ssrOcc = SSR_ON(_UseOcclusion) ? occ : 1.0;
    float3 ssrGlossy = refl.rgb * fres * _SSRMultiplier * ssrOcc;

    // What the deferred reflection pass already put into this pixel: the
    // probe radiance along the reflected ray, times the same reflectance and
    // the GBuffer occlusion.
    float3 worldR = normalize(mul((float3x3)_CameraToWorldMatrix, reflect(v, n)));
    float3 probeGlossy = SsrProbe(worldR, rough) * fres * occ;

    int dbg = (int)(_DebugMode + 0.5);
    if (dbg != 0)
    {
        float3 ssrFull = ssrGlossy / max(w, 1e-4);
        if (dbg == 1) return float4(refl.rgb / max(w, 1e-4), 1);
        if (dbg == 2) return float4(refl.rgb * _SSRMultiplier, 1);
        if (dbg == 3)
        {
            float3 g = SSR_ON(_AdditiveReflection) ? probeGlossy + ssrGlossy
                                                   : probeGlossy * (1.0 - w) + ssrGlossy;
            return float4(g, 1);
        }
        if (dbg == 4) return float4(w.xxx, 1);
        if (dbg == 5) return float4(rough.xxx, 1);
        if (dbg == 6) return float4(gb0.rgb, 1);
        if (dbg == 7) return float4(gb1.rgb, 1);
        if (dbg == 8) return float4(fres, 1);
        if (dbg == 9) return float4(probeGlossy, 1);
        if (dbg == 10) return float4(max(probeGlossy - ssrFull, 0.0) * w, 1);
        if (dbg == 11) return float4(max(ssrFull - probeGlossy, 0.0) * w, 1);
        if (dbg == 12) return float4(max(src.rgb - probeGlossy, 0.0), 1);
        if (dbg == 13) return float4(max(probeGlossy - src.rgb, 0.0), 1);
        if (dbg == 14) return float4((SsrBlurLevel(i.uv, eye, rough) / 4.0).xxx, 1);
    }

    float3 result;
    if (SSR_ON(_AdditiveReflection))
    {
        result = src.rgb + ssrGlossy;
    }
    else
    {
        // Swap the probe reflection for the traced one where rays hit; the
        // diffuse part of the pixel is left alone, misses keep the probe.
        result = max(src.rgb - probeGlossy * w, 0.0) + ssrGlossy;
    }
    return float4(result, src.a);
}

// Pass 6: separable, alpha-weighted and normal-aware downsampling blur.
float4 SsrFragBlur(SsrV2f i) : SV_Target
{
    float2 dir = _Axis.xy * _MainTex_TexelSize.xy;
    float3 nc = SsrTex(_NormalAndRoughnessTexture, i.uv).xyz * 2.0 - 1.0;

    float3 rgbSum = 0.0;
    float aSum = 0.0;
    float wSum = 0.0;
    [unroll]
    for (int k = -3; k <= 3; k++)
    {
        float o = (float)k;
        float2 uv = i.uv + dir * o;
        float4 s = SsrTex(_MainTex, uv);
        float3 ns = SsrTex(_NormalAndRoughnessTexture, uv).xyz * 2.0 - 1.0;
        float wn = pow(saturate(dot(nc, ns)), 8.0);
        float w = exp(-o * o / 4.5) * (0.02 + 0.98 * wn);
        rgbSum += s.rgb * s.a * w;
        aSum += s.a * w;
        wSum += w;
    }
    return float4(rgbSum / max(aSum, 1e-4), aSum / max(wSum, 1e-4));
}

// Pass 7: pick the blur level per pixel and apply the surface-based fades.
float4 SsrFragCompositeSSR(SsrV2f i) : SV_Target
{
    float raw = SsrRawDepth(i.uv);
    if (SsrIsSky(raw))
        return 0;

    float rough = SsrTex(_NormalAndRoughnessTexture, i.uv).a;
    float roughFade = SsrRoughnessFade(rough);
    if (roughFade <= 0.0)
        return 0;

    float eye = LinearEyeDepth(raw);
    float3 p = SsrViewPos(i.uv, eye);
    float3 n = SsrToView(SsrWorldNormalAt(i.uv));
    float nv = saturate(dot(n, -normalize(p)));
    float fresnelFade = lerp(1.0, pow(saturate(1.0 - nv), max(_FresnelFadePower, 1e-3)), saturate(_FresnelFade));

    float mip = SsrBlurLevel(i.uv, eye, rough);

    float4 l0 = tex2Dlod(_ReflectionTexture0, float4(i.uv, 0, 0));
    float4 l1 = tex2Dlod(_ReflectionTexture1, float4(i.uv, 0, 0));
    float4 l2 = tex2Dlod(_ReflectionTexture2, float4(i.uv, 0, 0));
    float4 l3 = tex2Dlod(_ReflectionTexture3, float4(i.uv, 0, 0));
    float4 l4 = tex2Dlod(_ReflectionTexture4, float4(i.uv, 0, 0));
    float w0 = saturate(1.0 - abs(mip - 0.0));
    float w1 = saturate(1.0 - abs(mip - 1.0));
    float w2 = saturate(1.0 - abs(mip - 2.0));
    float w3 = saturate(1.0 - abs(mip - 3.0));
    float w4 = saturate(1.0 - abs(mip - 4.0));

    float3 rgb = l0.rgb * l0.a * w0 + l1.rgb * l1.a * w1 + l2.rgb * l2.a * w2 + l3.rgb * l3.a * w3 + l4.rgb * l4.a * w4;
    float a = l0.a * w0 + l1.a * w1 + l2.a * w2 + l3.a * w3 + l4.a * w4;
    rgb /= max(a, 1e-4);

    if (SSR_ON(_HighlightSuppression))
        rgb = rgb / max(1.0 - SsrMax3(rgb), 1e-3);

    float weight = saturate(a) * roughFade * fresnelFade;
    return float4(rgb * weight, weight);
}

// Pass 8: copy.
float4 SsrFragBlit(SsrV2f i) : SV_Target
{
    return SsrTex(_MainTex, i.uv);
}

// Pass 9: 1 where the 2x2 full-resolution footprint is continuous, 0 on edges.
float4 SsrFragEdgeGeneration(SsrV2f i) : SV_Target
{
    float2 d = 0.5 * _InvScreenSize.xy;
    float z0 = SsrEyeDepth(i.uv + float2(-d.x, -d.y));
    float z1 = SsrEyeDepth(i.uv + float2( d.x, -d.y));
    float z2 = SsrEyeDepth(i.uv + float2(-d.x,  d.y));
    float z3 = SsrEyeDepth(i.uv + float2( d.x,  d.y));
    float zMin = min(min(z0, z1), min(z2, z3));
    float zMax = max(max(z0, z1), max(z2, z3));
    float3 n0 = SsrWorldNormalAt(i.uv + float2(-d.x, -d.y));
    float3 n3 = SsrWorldNormalAt(i.uv + float2( d.x,  d.y));
    bool edge = (zMax - zMin) > 0.05 * zMin || dot(n0, n3) < 0.9;
    return edge ? 0.0 : 1.0;
}

// Pass 10: conservative (min) downsample of the edge buffer.
float4 SsrFragMinMip(SsrV2f i) : SV_Target
{
    float2 d = 0.5 * _MainTex_TexelSize.xy;
    float a = SsrTex(_MainTex, i.uv + float2(-d.x, -d.y)).r;
    float b = SsrTex(_MainTex, i.uv + float2( d.x, -d.y)).r;
    float c = SsrTex(_MainTex, i.uv + float2(-d.x,  d.y)).r;
    float e = SsrTex(_MainTex, i.uv + float2( d.x,  d.y)).r;
    return min(min(a, b), min(c, e));
}

// Pass 11: fetch the scene colour at each hit point.
float4 SsrFragHitPointToReflections(SsrV2f i) : SV_Target
{
    float4 h = SsrTex(_HitPointTexture, i.uv);
    if (h.z <= 0.0)
        return 0;
    float3 c = tex2Dlod(_MainTex, float4(h.xy * _SourceToTempUV.xy, 0, 0)).rgb;
    if (SSR_ON(_HighlightSuppression))
        c = c / (1.0 + SsrMax3(c));
    float w = saturate(h.z) * SsrEdgeFade(h.xy) * SsrDistanceFade(h.w);
    return float4(c, w);
}

// Pass 12: normals and roughness in one point-sampled buffer.
float4 SsrFragBilateralKeyPack(SsrV2f i) : SV_Target
{
    float3 n = SsrTex(_CameraGBufferTexture2, i.uv).xyz;
    float r = 1.0 - SsrTex(_CameraGBufferTexture1, i.uv).a;
    return float4(n, r);
}

// Pass 13: camera-space z (negative in front of the camera).
float4 SsrFragBlitDepthAsCSZ(SsrV2f i) : SV_Target
{
    return float4(-SsrEyeDepth(i.uv), 0, 0, 0);
}

// Pass 14: blend with the reprojected previous frame.
float4 SsrFragTemporalFilter(SsrV2f i) : SV_Target
{
    float4 cur = SsrTex(_FinalReflectionTexture, i.uv);
    float raw = SsrRawDepth(i.uv);
    if (SsrIsSky(raw))
        return cur;

    float3 p = SsrViewPos(i.uv, LinearEyeDepth(raw));
    float3 pp = mul(_CurrentCameraToPreviousCamera, float4(p, 1.0)).xyz;
    float4 h = mul(_ProjectToPixelMatrix, float4(pp, 1.0));
    float2 puv = h.xy / h.w * _InvScreenSize.xy;
    if (h.w <= 0.0 || any(puv < 0.0) || any(puv > 1.0))
        return cur;

    float prevZ = SsrTex(_PreviousCSZBuffer, puv).r;
    if (abs(prevZ - pp.z) > 0.05 * abs(pp.z) + 0.02)
        return cur;

    float4 prev = SsrTex(_PreviousReflectionTexture, puv);
    float alpha = saturate(_TemporalAlpha);
    if (SSR_ON(_UseTemporalConfidence))
        alpha *= saturate(1.0 - abs(cur.a - prev.a) * 2.0);
    return lerp(cur, prev, alpha);
}

// Pass 15: average hit distance of the 3x3 trace neighbourhood.
float4 SsrFragAverageRayDistance(SsrV2f i) : SV_Target
{
    float2 texel = 1.0 / _ReflectionBufferSize.xy;
    float sum = 0.0;
    float cnt = 0.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float4 h = SsrTex(_HitPointTexture, i.uv + float2(x, y) * texel);
            float hit = step(0.5, h.z);
            sum += h.w * hit;
            cnt += hit;
        }
    }
    return (sum / max(cnt, 1.0)).xxxx;
}

// Pass 16: small disk blur for the sharpest level, radius driven by roughness.
float4 SsrFragPoissonBlur(SsrV2f i) : SV_Target
{
    static const float2 disk[8] = {
        float2( 0.527,  0.085), float2(-0.040,  0.536), float2(-0.670, -0.179), float2( 0.137, -0.704),
        float2( 0.874,  0.462), float2(-0.598,  0.786), float2(-0.808, -0.514), float2( 0.490, -0.872)
    };
    float rough = SsrTex(_NormalAndRoughnessTexture, i.uv).a;
    float radius = 1.0 + rough * 3.0;

    float4 c = SsrTex(_MainTex, i.uv);
    float3 rgbSum = c.rgb * c.a;
    float aSum = c.a;
    float wSum = 1.0;
    [unroll]
    for (int k = 0; k < 8; k++)
    {
        float4 s = SsrTex(_MainTex, i.uv + disk[k] * radius * _MainTex_TexelSize.xy);
        rgbSum += s.rgb * s.a;
        aSum += s.a;
        wSum += 1.0;
    }
    return float4(rgbSum / max(aSum, 1e-4), aSum / wSum);
}

#endif
