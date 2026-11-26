// From-scratch reimplementation for the port.
// Shared code for the Hidden/Deluxe/Tonemapper* image effect shaders driven by DeluxeTonemapper.
//
// The script uploads a two-piece rational filmic curve (see FilmicCurve.ComputeShaderCoefficients):
//   x <= _Crossover : y = (_Toe.x * x + _Toe.z) / (_Toe.y * x + _Toe.w)
//   x >  _Crossover : y = (_Shoulder.x * x + _Shoulder.z) / (_Shoulder.y * x + _Shoulder.w) + _K
// Both pieces meet at (crossover, _K); the toe reaches 0 at the black point and the shoulder
// reaches 1 at the white point. _LuminosityWhite is the squared luminosity saturation point.
//
// Per-shader switch (define before including):
//   PORT_DELUXE_MODE 0  per-channel curve (Color mode, also the unused Tonemapper shader)
//   PORT_DELUXE_MODE 1  curve on luminance, colour scaled by the luminance ratio (Luminance mode)
//   PORT_DELUXE_MODE 2  as 1, plus a highlight-only roll of bright colours towards white that
//                       completes at the saturation point (_LuminosityWhite = point^2)
// The result is multiplied by _Tint; alpha passes through.

#ifndef PORT_DELUXE_INCLUDED
#define PORT_DELUXE_INCLUDED

#include "UnityCG.cginc"

#ifndef PORT_DELUXE_MODE
#define PORT_DELUXE_MODE 0
#endif

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _Toe;
float4 _Shoulder;
float _K;
float _Crossover;
float4 _Tint;
float _LuminosityWhite;

static const float3 PORT_DELUXE_LUMA = float3(0.3, 0.59, 0.11);

struct PortDeluxeV2F
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PortDeluxeV2F PortDeluxeVert(appdata_img v)
{
    PortDeluxeV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = UnityStereoScreenSpaceUVAdjust(v.texcoord.xy, _MainTex_ST);
    return o;
}

// Evaluates the filmic curve for any number of channels at once.
// Guards:
//  - x <= 0 maps to 0 (the toe meets 0 at the black point >= 0), so a black channel with
//    crossover 0 and black point 0 never evaluates 0/0.
//  - With a negative shoulder strength the shoulder denominator crosses zero somewhere past
//    the white point and the rational piece flips sign there. The curve is already >= 1 at the
//    white point and rises monotonically up to that pole, so a non-positive denominator means
//    "past white" and maps to 1 instead of letting saturate() clamp a huge negative value to 0.
float3 PortDeluxe_Curve(float3 x)
{
    float3 toeDen = _Toe.y * x + _Toe.w;
    toeDen = (abs(toeDen) < 1e-8) ? 1e-8 : toeDen;
    float3 toe = (_Toe.x * x + _Toe.z) / toeDen;
    toe = (x > 0.0) ? toe : 0.0;

    float3 shDen = _Shoulder.y * x + _Shoulder.w;
    float3 shoulder = (_Shoulder.x * x + _Shoulder.z) / max(shDen, 1e-8) + _K;
    shoulder = (shDen > 0.0) ? shoulder : 1.0;

    float3 y = (x <= _Crossover) ? toe : shoulder;
    return saturate(y);
}

float PortDeluxe_Curve1(float x)
{
    return PortDeluxe_Curve(x.xxx).x;
}

float3 PortDeluxe_Tonemap(float3 c)
{
    c = max(c, 0.0);
#if PORT_DELUXE_MODE == 0
    return PortDeluxe_Curve(c);
#else
    float lum = dot(c, PORT_DELUXE_LUMA);
    if (lum <= 1e-6)
        return 0.0;
    float mapped = PortDeluxe_Curve1(lum);
    // Chromaticity with unit luminance: keeps the hue while the curve acts on brightness only.
    float3 chroma = c / lum;
  #if PORT_DELUXE_MODE == 2
    // Highlight burn-out towards white. _LuminosityWhite is the squared saturation point, so
    // f = lum^2 / white^2 is 0 at black, stays small through the midtones (a quarter of the way
    // at half the saturation point) and reaches exactly 1 (pure white at the mapped brightness)
    // when the input luminance hits the saturation point. Lerping a unit-luminance chroma
    // towards (1,1,1) keeps unit luminance because the weights sum to one.
    float f = saturate(lum * lum / max(_LuminosityWhite, 1e-6));
    chroma = lerp(chroma, 1.0, f);
  #endif
    return saturate(chroma * mapped);
#endif
}

float4 PortDeluxeFrag(PortDeluxeV2F i) : SV_Target
{
    float4 src = tex2D(_MainTex, i.uv);
    float3 c = PortDeluxe_Tonemap(src.rgb) * _Tint.rgb;
    return float4(c, src.a);
}

#endif
