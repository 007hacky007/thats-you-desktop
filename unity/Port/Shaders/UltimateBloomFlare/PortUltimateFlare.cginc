// From-scratch reimplementation for the port: shared helpers for the
// UltimateBloom lens-flare shaders (Hidden/Ultimate/Flare*) and the unused
// package leftovers (Hidden/Ultimate/Bloom, BokehMisc, BokehTexture).
#ifndef PORT_ULTIMATE_FLARE_INCLUDED
#define PORT_ULTIMATE_FLARE_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;

struct PortFlareV2F
{
    float4 pos : SV_POSITION;
    float2 uv  : TEXCOORD0;
};

PortFlareV2F PortFlareVert(appdata_img v)
{
    PortFlareV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord.xy;
    return o;
}

// ---------------------------------------------------------------------------
// Ghost generation (FlareSingle / FlareDouble).
// UltimateBloom.RenderFlares feeds a masked bright-pass image and blits it
// through pass 0. Each ghost is that image reflected through the screen
// centre and shrunk by its scale factor, then tinted. Samples are faded out
// towards the border of the source so the clamp sampler never smears edge
// texels into long streaks.
// ---------------------------------------------------------------------------
float4 _FlareScales;
float4 _FlareScalesNear;
float4 _FlareTint0;
float4 _FlareTint1;
float4 _FlareTint2;
float4 _FlareTint3;
float4 _FlareTint4;
float4 _FlareTint5;
float4 _FlareTint6;
float4 _FlareTint7;
float _Intensity;

float PortFlareEdgeFade(float2 uv)
{
    // 1 in the interior, 0 outside [0,1], soft ramp over the outer 10 percent.
    float2 d = saturate(min(uv, 1.0 - uv) * 10.0);
    return d.x * d.y;
}

float3 PortFlareGhost(float2 centered, float scale, float4 tint)
{
    float2 uv = 0.5 - centered * scale;
    float3 c = tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)).rgb;
    return c * tint.rgb * PortFlareEdgeFade(uv);
}

float3 PortFlareGhostSet(float2 centered, float4 scales,
                         float4 t0, float4 t1, float4 t2, float4 t3)
{
    float3 sum = PortFlareGhost(centered, scales.x, t0);
    sum += PortFlareGhost(centered, scales.y, t1);
    sum += PortFlareGhost(centered, scales.z, t2);
    sum += PortFlareGhost(centered, scales.w, t3);
    return sum;
}

float4 PortFlareSingleFrag(PortFlareV2F i) : SV_Target
{
    float2 c = i.uv - 0.5;
    float3 sum = PortFlareGhostSet(c, _FlareScales, _FlareTint0, _FlareTint1, _FlareTint2, _FlareTint3);
    return float4(sum * _Intensity, 1.0);
}

float4 PortFlareDoubleFrag(PortFlareV2F i) : SV_Target
{
    float2 c = i.uv - 0.5;
    float3 sum = PortFlareGhostSet(c, _FlareScales, _FlareTint0, _FlareTint1, _FlareTint2, _FlareTint3);
    sum += PortFlareGhostSet(c, _FlareScalesNear, _FlareTint4, _FlareTint5, _FlareTint6, _FlareTint7);
    return float4(sum * _Intensity, 1.0);
}

#endif
