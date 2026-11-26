// From-scratch reimplementation for the port: shared helpers for the epipolar light shaft
// passes driven by LightShafts.cs. Written only from the uniform names, render texture
// formats and draw calls of the driver script.
//
// Conventions used by every pass:
// - Epipolar textures are (samples x lines). Row k is one epipolar line: it runs from the
//   light's screen position (or the point where the line enters the screen when the light is
//   off screen) to a point on the screen border. Rows are grouped into four quarters that
//   end on the top, right, bottom and left screen edge, walking the border clockwise, which
//   matches the quarter culling in LightShafts.RenderQuadSections.
// - Column j of a row sits at parameter s = j / (samples - 1) along the line.
// - "Screen uv" is the camera viewport position in [0,1] (y up), the same space as
//   Camera.WorldToViewportPoint, so it can be used directly with _CameraDepthTexture.
// - _InterpolationEpi stores, per epipolar texel, the column indices of the raymarched
//   samples on its left and right. It is always a float texture (RGFloat, point filtered)
//   holding whole numbers; readers round what they fetch to integer indices. An integer
//   render target is not used because Metal rejects an int4 fragment output whenever the
//   pipeline is validated against a non-integer color attachment. LS_INT_INTERP only
//   selects exact texel Load() addressing on SM4.5+ instead of point-sampled tex2Dlod.

#ifndef PORT_LIGHTSHAFTS_INCLUDED
#define PORT_LIGHTSHAFTS_INCLUDED

#include "UnityCG.cginc"

float4 _LightPos;        // light position in viewport space remapped to [-1,1]
float4 _CoordTexDim;     // (samples, lines, 1/samples, 1/lines)
float4 _ScreenTexDim;    // (width, height, 1/width, 1/height)
float4x4 _FrustumRays;   // rows: camera to far plane corners, uv (0,0) (1,0) (1,1) (0,1), volume local space
float4 _CameraPosLocal;  // camera position in volume local space (unit cube centred at 0)
float _FrustumApex;      // local z of the spot cone apex

UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

// ---------------------------------------------------------------------------------------
// Vertex stage for the epipolar passes (GL quads with positions in [-1,1]).
// Clip y is flipped on platforms whose textures start at the top so that the interpolated
// uv addresses exactly the texel being rendered on every API.

struct ls_appdata
{
    float4 vertex : POSITION;
};

struct ls_v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

ls_v2f ls_vert_epi(ls_appdata v)
{
    ls_v2f o;
    float2 p = v.vertex.xy;
    o.uv = p * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    p.y = -p.y;
#endif
    o.pos = float4(p, 0.5, 1.0);
    return o;
}

// ---------------------------------------------------------------------------------------
// Epipolar line geometry (all in [-1,1] screen space).

float2 LS_SafeDir(float2 d)
{
    float2 s = step(0.0, d) * 2.0 - 1.0;
    return s * max(abs(d), 1e-6);
}

float3 LS_SafeDir3(float3 d)
{
    float3 s = step(0.0, d) * 2.0 - 1.0;
    return s * max(abs(d), 1e-7);
}

// Border end point for a row, v in [0,1) is the row's centre in texture space.
float2 LS_BorderPoint(float v)
{
    float q4 = v * 4.0;
    float q = floor(q4);
    float t = q4 - q;
    float2 e;
    if (q < 0.5)
        e = float2(t * 2.0 - 1.0, 1.0);      // top, left to right
    else if (q < 1.5)
        e = float2(1.0, 1.0 - t * 2.0);      // right, top to bottom
    else if (q < 2.5)
        e = float2(1.0 - t * 2.0, -1.0);     // bottom, right to left
    else
        e = float2(-1.0, t * 2.0 - 1.0);     // left, bottom to top
    return e;
}

// Start of the visible part of the segment light -> border point.
float2 LS_LineStart(float2 L, float2 E)
{
    float2 d = E - L;
    float2 inv = 1.0 / LS_SafeDir(d);
    float2 t0 = (-1.0 - L) * inv;
    float2 t1 = (1.0 - L) * inv;
    float2 tmin = min(t0, t1);
    float k = saturate(max(tmin.x, tmin.y));
    return L + d * k;
}

// Quarter culling, mirrors LightShafts.RenderQuadSections.
float LS_QuarterActive(float q, float2 L)
{
    if (q < 0.5) return L.y > 1.0 ? 0.0 : 1.0;
    if (q < 1.5) return L.x > 1.0 ? 0.0 : 1.0;
    if (q < 2.5) return L.y < -1.0 ? 0.0 : 1.0;
    return L.x < -1.0 ? 0.0 : 1.0;
}

// Continuous row position (texture v in [0,1)) of the epipolar line through screen point p.
float LS_RowCoord(float2 p, float2 L)
{
    float2 d = LS_SafeDir(p - L);
    float2 s = step(0.0, d) * 2.0 - 1.0;
    float2 k = (s - L) / d;
    float v;
    if (k.x < k.y)
    {
        float ey = L.y + d.y * k.x;
        v = (s.x > 0.0) ? (1.0 + (1.0 - ey) * 0.5) : (3.0 + (ey + 1.0) * 0.5);
    }
    else
    {
        float ex = L.x + d.x * k.y;
        v = (s.y > 0.0) ? ((ex + 1.0) * 0.5) : (2.0 + (1.0 - ex) * 0.5);
    }
    return saturate(v * 0.25);
}

// ---------------------------------------------------------------------------------------
// Camera rays through the volume local space.

float3 LS_FrustumRay(float2 suv)
{
    float3 r0 = _FrustumRays[0].xyz;
    float3 r1 = _FrustumRays[1].xyz;
    float3 r2 = _FrustumRays[2].xyz;
    float3 r3 = _FrustumRays[3].xyz;
    return lerp(lerp(r0, r1, suv.x), lerp(r3, r2, suv.x), suv.y);
}

float LS_SceneDepth01(float2 suv)
{
    return Linear01Depth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(suv, 0.0, 0.0)));
}

// ---------------------------------------------------------------------------------------
// Epipolar texture fetches. Indices are passed as floats (column, row).

#ifdef LS_INT_INTERP
    #define LS_DECLARE_EPI(name) Texture2D<float4> name
    #define LS_FETCH_EPI(name, dim, ij) name.Load(int3(int2(ij), 0))
    Texture2D<float4> _InterpolationEpi;
    #define LS_FETCH_INTERP(dim, ij) floor(_InterpolationEpi.Load(int3(int2(ij), 0)).xy + 0.5)
#else
    #define LS_DECLARE_EPI(name) sampler2D_float name
    #define LS_FETCH_EPI(name, dim, ij) tex2Dlod(name, float4(((ij) + 0.5) * (dim).zw, 0.0, 0.0))
    sampler2D_float _InterpolationEpi;
    #define LS_FETCH_INTERP(dim, ij) floor(tex2Dlod(_InterpolationEpi, float4(((ij) + 0.5) * (dim).zw, 0.0, 0.0)).xy + 0.5)
#endif

// Fetches of the texel a fragment is shading, usable without knowing the texture size.
#ifdef LS_INT_INTERP
    #define LS_FETCH_EPI_SELF(name, i) name.Load(int3(int2(floor((i).pos.xy)), 0))
    #define LS_FETCH_INTERP_SELF(i) floor(_InterpolationEpi.Load(int3(int2(floor((i).pos.xy)), 0)).xy + 0.5)
#else
    #define LS_FETCH_EPI_SELF(name, i) tex2Dlod(name, float4((i).uv, 0.0, 0.0))
    #define LS_FETCH_INTERP_SELF(i) floor(tex2Dlod(_InterpolationEpi, float4((i).uv, 0.0, 0.0)).xy + 0.5)
#endif

// Column/row index of the texel a fragment is shading.
float2 LS_SelfTexel(ls_v2f i, float4 dim)
{
#ifdef LS_INT_INTERP
    return floor(i.pos.xy);
#else
    return floor(i.uv * dim.xy);
#endif
}

// =======================================================================================
// Pass bodies. Each shader defines LS_PASS_<NAME> (and LS_INT_INTERP for the SM4.5
// subshader, which addresses texels with Load) before including this file.

#ifdef LS_PASS_DEPTHBREAKS
// For every epipolar sample find the nearest raymarched samples to the left and right.
// A sample is raymarched when it lies on the regular _InterpolationStep grid, is the last
// sample of the line, or sits on either side of a depth discontinuity.
LS_DECLARE_EPI(_DepthEpi);
float4 _DepthEpiTexDim;
float _InterpolationStep;
float _DepthThreshold;

#define LS_MAX_INTERPOLATION_STEP 256

float LS_EpiDepthAt(float j, float row)
{
    j = clamp(j, 0.0, _DepthEpiTexDim.x - 1.0);
    return LS_FETCH_EPI(_DepthEpi, _DepthEpiTexDim, float2(j, row)).r;
}

float LS_IsRaymarchSample(float j, float stepN, float n, float dL, float dJ, float dR)
{
    float onGrid = (j - stepN * floor(j / stepN + 1e-4)) < 0.5 ? 1.0 : 0.0;
    float atEnd = j >= n - 1.5 ? 1.0 : 0.0;
    float th = _DepthThreshold;
    float brk = (abs(dJ - dL) > th || abs(dR - dJ) > th) ? 1.0 : 0.0;
    return max(max(onGrid, atEnd), brk);
}

float2 LS_DepthBreaks(float2 ij)
{
    float n = _DepthEpiTexDim.x;
    float stepN = clamp(floor(_InterpolationStep + 0.5), 1.0, (float)LS_MAX_INTERPOLATION_STEP);
    float x = ij.x;
    float row = ij.y;

    // Walk left.
    float j = x;
    float dJ = LS_EpiDepthAt(x, row);
    float dR = LS_EpiDepthAt(x + 1.0, row);
    [loop]
    for (int k = 0; k < LS_MAX_INTERPOLATION_STEP; k++)
    {
        float dL = LS_EpiDepthAt(j - 1.0, row);
        if (j <= 0.0 || LS_IsRaymarchSample(j, stepN, n, dL, dJ, dR) > 0.5)
            break;
        dR = dJ;
        dJ = dL;
        j -= 1.0;
    }
    float left = max(j, 0.0);

    // Walk right.
    j = x;
    dJ = LS_EpiDepthAt(x, row);
    float dL2 = LS_EpiDepthAt(x - 1.0, row);
    [loop]
    for (int m = 0; m < LS_MAX_INTERPOLATION_STEP; m++)
    {
        float dR2 = LS_EpiDepthAt(j + 1.0, row);
        if (j >= n - 1.0 || LS_IsRaymarchSample(j, stepN, n, dL2, dJ, dR2) > 0.5)
            break;
        dL2 = dJ;
        dJ = dR2;
        j += 1.0;
    }
    float right = min(j, n - 1.0);

    return float2(left, right);
}
#endif // LS_PASS_DEPTHBREAKS

#ifdef LS_PASS_RAYMARCH
// Integrates in-scattered light along the camera ray of every raymarched epipolar sample,
// in the volume's local space (unit cube). Shadowing comes from the light's depth map,
// optionally tinted by the colour filter map, the light cookie and the attenuation curve.
LS_DECLARE_EPI(_Coord);
sampler2D_float _Shadowmap;
float4 _ShadowmapDim;
sampler2D _ColorFilter;
sampler2D _AttenuationCurveTex;
sampler2D _Cookie;
float _Brightness;
float _Extinction;
float _MinDistFromCamera;
float4 _LightColor;

#define LS_RAYMARCH_STEPS 128
#define LS_SHADOW_BIAS 0.002

// Light space of a local point: shadow map uv, normalised light depth, inside-volume mask.
void LS_LightSpace(float3 P, out float2 luv, out float lz, out float inside)
{
#if defined(SPOT_SHAFTS)
    float zr = (P.z - _FrustumApex) / (0.5 - _FrustumApex);
    float h = 0.5 * zr;
    float2 ndc = P.xy / max(h, 1e-5);
    luv = ndc * 0.5 + 0.5;
    lz = zr;
    inside = step(abs(ndc.x), 1.0) * step(abs(ndc.y), 1.0) * step(1e-5, zr);
#else
    luv = P.xy + 0.5;
    lz = P.z + 0.5;
    inside = 1.0;
#endif
}

float LS_ShadowVisibility(float2 luv, float lz)
{
    float2 tc = luv * _ShadowmapDim.xy - 0.5;
    float2 f = frac(tc);
    float2 uv0 = (floor(tc) + 0.5) * _ShadowmapDim.zw;
    float2 du = float2(_ShadowmapDim.z, 0.0);
    float2 dv = float2(0.0, _ShadowmapDim.w);
    float z = lz - LS_SHADOW_BIAS;
    float s00 = step(z, tex2Dlod(_Shadowmap, float4(uv0, 0.0, 0.0)).r);
    float s10 = step(z, tex2Dlod(_Shadowmap, float4(uv0 + du, 0.0, 0.0)).r);
    float s01 = step(z, tex2Dlod(_Shadowmap, float4(uv0 + dv, 0.0, 0.0)).r);
    float s11 = step(z, tex2Dlod(_Shadowmap, float4(uv0 + du + dv, 0.0, 0.0)).r);
    return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
}

float4 LS_Raymarch(ls_v2f i)
{
    float2 lr = LS_FETCH_INTERP_SELF(i);
    if (abs(lr.x - lr.y) > 0.5)
        return float4(0.0, 0.0, 0.0, 1.0);

    float2 suv = LS_FETCH_EPI_SELF(_Coord, i).xy;
    float depth01 = LS_SceneDepth01(suv);
    float3 ray = LS_FrustumRay(suv);
    float3 cam = _CameraPosLocal.xyz;

    // Ray against the unit cube.
    float3 inv = 1.0 / LS_SafeDir3(ray);
    float3 t0 = (-0.5 - cam) * inv;
    float3 t1 = (0.5 - cam) * inv;
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);
    float tNear = max(max(tmin.x, tmin.y), tmin.z);
    float tFar = min(min(tmax.x, tmax.y), tmax.z);
    tNear = max(tNear, max(0.0, _MinDistFromCamera * _ProjectionParams.w));
    tFar = min(tFar, depth01);
    if (tFar <= tNear)
        return float4(0.0, 0.0, 0.0, 1.0);

    float rayLen = length(ray);
    float dt = (tFar - tNear) / LS_RAYMARCH_STEPS;
    float3 sum = 0.0;
    [loop]
    for (int k = 0; k < LS_RAYMARCH_STEPS; k++)
    {
        float t = tNear + (k + 0.5) * dt;
        float3 P = cam + ray * t;
        float2 luv;
        float lz;
        float inside;
        LS_LightSpace(P, luv, lz, inside);
        float3 c = (inside * LS_ShadowVisibility(luv, lz)).xxx;
#ifdef COLORED_ON
        c *= tex2Dlod(_ColorFilter, float4(luv, 0.0, 0.0)).rgb;
#endif
#ifdef COOKIE_TEX_ON
        c *= tex2Dlod(_Cookie, float4(luv, 0.0, 0.0)).a;
#endif
#ifdef ATTENUATION_CURVE_ON
        c *= tex2Dlod(_AttenuationCurveTex, float4(saturate(lz), 0.5, 0.0, 0.0)).r;
#endif
        // _Extinction is the negated extinction coefficient; distance is measured from the
        // point where the ray enters the volume, in volume local units.
        c *= exp(_Extinction * (t - tNear) * rayLen);
        sum += c;
    }
    sum *= dt * rayLen * _Brightness * _LightColor.rgb;
    return float4(sum, 1.0);
}
#endif // LS_PASS_RAYMARCH

#ifdef LS_PASS_INTERPOLATE_ALONG_RAYS
// Fills every epipolar sample by linear interpolation between its raymarched neighbours.
LS_DECLARE_EPI(_RaymarchedLightEpi);
float4 _RaymarchedLightEpiTexDim;
float _InterpolationStep;

float4 LS_InterpolateAlongRays(ls_v2f i)
{
    float2 ij = LS_SelfTexel(i, _RaymarchedLightEpiTexDim);
    float2 lr = LS_FETCH_INTERP(_RaymarchedLightEpiTexDim, ij);
    float4 a = LS_FETCH_EPI(_RaymarchedLightEpi, _RaymarchedLightEpiTexDim, float2(lr.x, ij.y));
    float4 b = LS_FETCH_EPI(_RaymarchedLightEpi, _RaymarchedLightEpiTexDim, float2(lr.y, ij.y));
    float span = lr.y - lr.x;
    float f = span > 0.5 ? saturate((ij.x - lr.x) / span) : 0.0;
    return lerp(a, b, f);
}
#endif // LS_PASS_INTERPOLATE_ALONG_RAYS

#ifdef LS_PASS_SAMPLE_POSITIONS
// Debug view: marks the screen pixel of every (raymarched or all) epipolar sample in a
// random write texture bound to slot 1.
LS_DECLARE_EPI(_Coord);
float4 _OutputTexDim;
float _SampleType;
float4 _Color;
#ifdef LS_RANDOM_WRITE
RWTexture2D<float4> _SamplePositionsRW : register(u1);
#endif

float4 LS_SamplePositions(ls_v2f i)
{
#ifdef LS_RANDOM_WRITE
    float2 lr = LS_FETCH_INTERP_SELF(i);
    bool raymarched = abs(lr.x - lr.y) < 0.5;
    if (_SampleType > 0.5 || raymarched)
    {
        float2 suv = LS_FETCH_EPI_SELF(_Coord, i).xy;
        int2 px = int2(floor(saturate(suv) * _OutputTexDim.xy + 0.5));
        _SamplePositionsRW[px] = _Color;
    }
#endif
    return 0.0;
}
#endif // LS_PASS_SAMPLE_POSITIONS

#ifdef LS_PASS_FINAL
// Reconstructs the shafts for every screen pixel from the two nearest epipolar lines and
// the two nearest samples on each, weighting taps by depth similarity so light does not
// bleed across silhouettes. Composited additively by the pass blend state.
LS_DECLARE_EPI(_RaymarchedLight);
LS_DECLARE_EPI(_DepthEpi);
sampler2D _SamplePositions;
float _DepthThreshold;
float _ShowSamplesBackgroundFade;

struct ls_final_appdata
{
    float4 vertex : POSITION;
};

struct ls_final_v2f
{
    float4 pos : SV_POSITION;
    float4 spos : TEXCOORD0; // clip position with y in viewport orientation
};

ls_final_v2f LS_FinalVert(ls_final_appdata v)
{
    ls_final_v2f o;
    // Negative when the camera renders with a flipped projection (render texture on
    // top-left origin APIs); keeps the quad in the same orientation as scene geometry.
    float flip = UNITY_MATRIX_P[1][1] < 0.0 ? -1.0 : 1.0;
#ifdef QUAD_SHAFTS
    o.pos = float4(v.vertex.x, v.vertex.y * flip, 0.5, 1.0);
    o.spos = float4(v.vertex.xy, 0.0, 1.0);
#else
    float4 clip = UnityObjectToClipPos(v.vertex);
    o.pos = clip;
    o.spos = float4(clip.x, clip.y * flip, 0.0, clip.w);
#endif
    return o;
}

void LS_AccumulateRow(float k, float w, float2 p, float d, float2 L, inout float3 acc, inout float wsum)
{
    if (w <= 0.0)
        return;
    float lines = _CoordTexDim.y;
    float n = _CoordTexDim.x;
    float2 E = LS_BorderPoint((k + 0.5) / lines);
    float2 S = LS_LineStart(L, E);
    float2 se = E - S;
    float s = saturate(dot(p - S, se) / max(dot(se, se), 1e-8));
    float c = s * (n - 1.0);
    float c0 = floor(c);
    float fc = c - c0;
    float c1 = min(c0 + 1.0, n - 1.0);

    float3 l0 = LS_FETCH_EPI(_RaymarchedLight, _CoordTexDim, float2(c0, k)).rgb;
    float3 l1 = LS_FETCH_EPI(_RaymarchedLight, _CoordTexDim, float2(c1, k)).rgb;
    float d0 = LS_FETCH_EPI(_DepthEpi, _CoordTexDim, float2(c0, k)).r;
    float d1 = LS_FETCH_EPI(_DepthEpi, _CoordTexDim, float2(c1, k)).r;

    float th = max(_DepthThreshold, 1e-6);
    float b0 = (1.0 - fc + 1e-3) * (abs(d0 - d) < th ? 1.0 : 1e-3);
    float b1 = (fc + 1e-3) * (abs(d1 - d) < th ? 1.0 : 1e-3);
    acc += w * (b0 * l0 + b1 * l1);
    wsum += w * (b0 + b1);
}

float4 LS_Final(ls_final_v2f i)
{
    float2 p = i.spos.xy / i.spos.w;
    float2 suv = p * 0.5 + 0.5;
    float d = LS_SceneDepth01(suv);
    float2 L = _LightPos.xy;
    float lines = _CoordTexDim.y;

    float r = LS_RowCoord(p, L) * lines - 0.5;
    float r0 = floor(r);
    float fr = r - r0;
    float k0 = r0 < 0.0 ? lines - 1.0 : r0;
    float k1 = (r0 + 1.0) >= lines ? 0.0 : r0 + 1.0;
    float w0 = (1.0 - fr) * LS_QuarterActive(floor((k0 + 0.5) / lines * 4.0), L);
    float w1 = fr * LS_QuarterActive(floor((k1 + 0.5) / lines * 4.0), L);

    float3 acc = 0.0;
    float wsum = 0.0;
    LS_AccumulateRow(k0, w0, p, d, L, acc, wsum);
    LS_AccumulateRow(k1, w1, p, d, L, acc, wsum);
    float3 light = wsum > 0.0 ? acc / wsum : float3(0.0, 0.0, 0.0);

#ifdef SHOW_SAMPLES_ON
    light = light * (1.0 - _ShowSamplesBackgroundFade) + tex2D(_SamplePositions, suv).rgb;
#endif
    // max/min also flush NaNs so a bad sample can never corrupt the additive composite.
    return float4(min(max(light, 0.0), 16.0), 0.0);
}
#endif // LS_PASS_FINAL

#endif // PORT_LIGHTSHAFTS_INCLUDED
