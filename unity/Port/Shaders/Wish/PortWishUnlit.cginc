// From-scratch reimplementation for the port.
// Shared helpers for the Wish unlit / mixed-lighting material shaders (TV screens, polaroids,
// postcards, backdrops, score digits, notebook text). Declares no material properties so each
// shader stays in control of its own property block.
#ifndef PORT_WISH_UNLIT_INCLUDED
#define PORT_WISH_UNLIT_INCLUDED

#include "UnityCG.cginc"

// Plain mesh vertex input: position and first UV set.
struct PortWishUnlitAppData
{
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Splits a colour into a lit (albedo) and an unlit (emission) share.
// mixAmount = 0 is fully Lambert lit, mixAmount = 1 shows the colour as is, ignoring lights.
inline void PortWishSplitLitUnlit(half3 color, half mixAmount, out half3 albedo, out half3 emission)
{
    half m = saturate(mixAmount);
    albedo = color * (1.0 - m);
    emission = color * m;
}

// Lays an overlay over a base colour. The overlay's own alpha shapes it (paper grime, rain
// streaks), amount fades the whole overlay in and out (polaroid developing, rain strength).
inline half3 PortWishApplyOverlay(half3 baseColor, half4 overlay, half amount)
{
    return lerp(baseColor, overlay.rgb, saturate(overlay.a * amount));
}

// Weight that is 1 inside [lower, upper] and ramps to 0 over fadeDistance at both edges.
inline float PortWishBandWeight(float v, float lower, float upper, float fadeDistance)
{
    float f = max(fadeDistance, 1e-5);
    return saturate((v - lower) / f) * saturate((upper - v) / f);
}

// Vertical viewport position (0 = bottom, 1 = top) from a ComputeScreenPos result.
inline float PortWishViewportY(float4 screenPos)
{
    return screenPos.y / max(screenPos.w, 1e-5);
}

#endif // PORT_WISH_UNLIT_INCLUDED
