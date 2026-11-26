// From-scratch reimplementation for the port.
// Shared code for Hidden/SSAO Pro V1 and V2, the screen space ambient occlusion image effect
// driven by SSAOPro.cs. Pass layout expected by the script:
//   0 clear to white, 1-4 occlusion (+ noise rotation, + luminance contribution),
//   5 separable gaussian blur, 6 separable depth/normal aware blur, 7 composite onto the source.
// The occlusion term is the usual normal-oriented one: every sample point near the pixel occludes
// in proportion to how far it lies above the surface (dot of the normal with the direction to it,
// minus Bias), attenuated by its distance (1 / (1 + d * Distance)), scaled by Intensity.
// Define PORT_SSAO_V2 before including to reconstruct world space positions (V2: the script passes
// inverse(projection * view) and cameraToWorld); otherwise positions are view space (V1: inverse
// projection only).

#ifndef PORT_SSAO_PRO_INCLUDED
#define PORT_SSAO_PRO_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
sampler2D _CameraDepthNormalsTexture;
sampler2D _NoiseTex;
sampler2D _SSAOTex;

// x noise texture size (0 when none), y radius, z intensity, w distance attenuation
float4 _Params1;
// x bias, y luminance contribution, z cutoff distance, w cutoff falloff
float4 _Params2;
float4x4 _InverseViewProject;
float4x4 _CameraModelView;
half4 _OcclusionColor;
float4 _Direction;

#if defined(SAMPLES_ULTRA)
    #define PORT_SSAO_SAMPLES 20
#elif defined(SAMPLES_HIGH)
    #define PORT_SSAO_SAMPLES 16
#elif defined(SAMPLES_MEDIUM)
    #define PORT_SSAO_SAMPLES 12
#elif defined(SAMPLES_LOW)
    #define PORT_SSAO_SAMPLES 8
#else
    #define PORT_SSAO_SAMPLES 4
#endif

// The depth buffer is laid out like the source image; when the source arrives upside down
// (D3D-style platforms with MSAA) depth lookups must be flipped to line up with it.
float2 PortSSAODepthUV(float2 uv)
{
#if UNITY_UV_STARTS_AT_TOP
    if (_MainTex_TexelSize.y < 0)
        uv.y = 1.0 - uv.y;
#endif
    return uv;
}

float PortSSAORawDepth(float2 uv)
{
    return SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0));
}

bool PortSSAOIsFar(float raw)
{
#if defined(UNITY_REVERSED_Z)
    return raw <= 1e-6;
#else
    return raw >= 1.0 - 1e-6;
#endif
}

// Position from uv + raw depth through the inverse matrix the script supplies. The matrix is
// built from Camera.projectionMatrix (OpenGL convention), so the raw value is first mapped to
// OpenGL NDC depth.
float3 PortSSAOPosition(float2 uv, float raw)
{
#if defined(UNITY_REVERSED_Z)
    float z01 = 1.0 - raw;
#else
    float z01 = raw;
#endif
    float4 ndc = float4(uv * 2.0 - 1.0, z01 * 2.0 - 1.0, 1.0);
    float4 p = mul(_InverseViewProject, ndc);
    return p.xyz / p.w;
}

// Distance along the view direction, in world units.
float PortSSAOEyeDepth(float3 p)
{
#ifdef PORT_SSAO_V2
    float3 camPos = float3(_CameraModelView._m03, _CameraModelView._m13, _CameraModelView._m23);
    float3 camFwd = -float3(_CameraModelView._m02, _CameraModelView._m12, _CameraModelView._m22);
    return dot(p - camPos, camFwd);
#else
    return -p.z;
#endif
}

// Surface normal in the same space as PortSSAOPosition.
float3 PortSSAONormal(float2 uv)
{
    float3 n = DecodeViewNormalStereo(tex2Dlod(_CameraDepthNormalsTexture, float4(uv, 0, 0)));
#ifdef PORT_SSAO_V2
    n = mul((float3x3)_CameraModelView, n);
#endif
    return normalize(n);
}

// Per pixel rotation vector from the noise texture, one texel per screen pixel.
float2 PortSSAONoise(float2 uv)
{
    float size = max(_Params1.x, 1.0);
    float2 texel = fmod(floor(uv * _ScreenParams.xy), size) + 0.5;
    float3 rnd = tex2Dlod(_NoiseTex, float4(texel / size, 0, 0)).rgb;
#if !defined(UNITY_COLORSPACE_GAMMA)
    // The noise texture is imported as sRGB; undo the decode so the vectors keep their spread.
    rnd = LinearToGammaSpace(rnd);
#endif
    float2 v = rnd.xy * 2.0 - 1.0;
    float len = length(v);
    return len > 1e-3 ? v / len : float2(1, 0);
}

half4 PortSSAOOcclusion(float2 uv, bool useNoise, bool useLum)
{
    float2 duv = PortSSAODepthUV(uv);
    float raw = PortSSAORawDepth(duv);
    if (PortSSAOIsFar(raw))
        return half4(1, 1, 1, 1);

    float3 p = PortSSAOPosition(duv, raw);
    float3 n = PortSSAONormal(duv);
    float depth = max(PortSSAOEyeDepth(p), 1e-3);

    float cutoffStart = _Params2.z - _Params2.w;
    if (depth >= _Params2.z)
        return half4(1, 1, 1, 1);

    float2 rot = useNoise ? PortSSAONoise(uv) : float2(1, 0);

    // Radius is a screen fraction at unit depth: shrinks with distance so the occluding
    // neighbourhood keeps a roughly constant world size. Kept square in pixels.
    float r = min(_Params1.y / depth, 0.2);
    float2 scale = float2(r * _ScreenParams.y / _ScreenParams.x, r);

    float occ = 0.0;
    [unroll]
    for (int s = 0; s < PORT_SSAO_SAMPLES; s++)
    {
        // Golden angle spiral, denser near the centre.
        float t = (s + 0.5) / PORT_SSAO_SAMPLES;
        float a = s * 2.39996323;
        float2 k = float2(cos(a), sin(a)) * t;
        if (useNoise)
            k = reflect(k, rot);

        float2 suv = duv + k * scale;
        float sraw = PortSSAORawDepth(suv);
        float3 sp = PortSSAOPosition(suv, sraw);
        float3 diff = sp - p;
        float len = length(diff);
        float3 dir = diff / max(len, 1e-5);
        occ += max(0.0, dot(n, dir) - _Params2.x) / (1.0 + len * _Params1.w);
    }

    occ = occ * _Params1.z / PORT_SSAO_SAMPLES;
    float ao = 1.0 - saturate(occ);

    if (useLum)
    {
        half3 c = saturate(tex2D(_MainTex, uv).rgb);
        ao = lerp(ao, 1.0, saturate(Luminance(c) * _Params2.y));
    }

    float fade = saturate((depth - cutoffStart) / max(_Params2.w, 1e-4));
    ao = lerp(ao, 1.0, fade);
    return half4(ao, ao, ao, 1);
}

// 9 tap separable blur weights (centre, then +-1..4 taps).
static const float kPortSSAOBlurW[5] = { 0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162 };

half4 PortSSAOBlurGaussian(float2 uv)
{
    float sum = tex2D(_MainTex, uv).r * kPortSSAOBlurW[0];
    [unroll]
    for (int j = 1; j < 5; j++)
    {
        float2 o = _Direction.xy * j;
        sum += tex2D(_MainTex, uv + o).r * kPortSSAOBlurW[j];
        sum += tex2D(_MainTex, uv - o).r * kPortSSAOBlurW[j];
    }
    return half4(sum, sum, sum, 1);
}

void PortSSAOBlurTap(float2 uv, float w, float d0, float3 n0, inout float sum, inout float wsum)
{
    float d;
    float3 n;
    DecodeDepthNormal(tex2Dlod(_CameraDepthNormalsTexture, float4(uv, 0, 0)), d, n);
    float wd = saturate(1.0 - abs(d - d0) / max(d0, 1e-4) * 24.0);
    float wn = pow(saturate(dot(n, n0)), 16.0);
    float ww = w * wd * wn;
    sum += tex2D(_MainTex, uv).r * ww;
    wsum += ww;
}

half4 PortSSAOBlurBilateral(float2 uv)
{
    float d0;
    float3 n0;
    DecodeDepthNormal(tex2Dlod(_CameraDepthNormalsTexture, float4(uv, 0, 0)), d0, n0);
    float wsum = kPortSSAOBlurW[0];
    float sum = tex2D(_MainTex, uv).r * wsum;
    [unroll]
    for (int j = 1; j < 5; j++)
    {
        float2 o = _Direction.xy * j;
        PortSSAOBlurTap(uv + o, kPortSSAOBlurW[j], d0, n0, sum, wsum);
        PortSSAOBlurTap(uv - o, kPortSSAOBlurW[j], d0, n0, sum, wsum);
    }
    float ao = sum / max(wsum, 1e-4);
    return half4(ao, ao, ao, 1);
}

half4 PortSSAOComposite(float2 uv)
{
    half4 c = tex2D(_MainTex, uv);
    // The occlusion target was written in source orientation (depth lookups were flipped instead).
    half ao = tex2D(_SSAOTex, uv).r;
    c.rgb *= lerp(_OcclusionColor.rgb, half3(1, 1, 1), ao);
    return c;
}

half4 PortSSAOFragClear(v2f_img i) : SV_Target { return half4(1, 1, 1, 1); }
half4 PortSSAOFragAO(v2f_img i) : SV_Target { return PortSSAOOcclusion(i.uv, false, false); }
half4 PortSSAOFragAONoise(v2f_img i) : SV_Target { return PortSSAOOcclusion(i.uv, true, false); }
half4 PortSSAOFragAOLum(v2f_img i) : SV_Target { return PortSSAOOcclusion(i.uv, false, true); }
half4 PortSSAOFragAONoiseLum(v2f_img i) : SV_Target { return PortSSAOOcclusion(i.uv, true, true); }
half4 PortSSAOFragGaussian(v2f_img i) : SV_Target { return PortSSAOBlurGaussian(i.uv); }
half4 PortSSAOFragBilateral(v2f_img i) : SV_Target { return PortSSAOBlurBilateral(i.uv); }
half4 PortSSAOFragComposite(v2f_img i) : SV_Target { return PortSSAOComposite(i.uv); }

#endif
