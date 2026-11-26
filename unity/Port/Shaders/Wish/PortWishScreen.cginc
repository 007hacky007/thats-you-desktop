// From-scratch reimplementation for the port: shared helpers for the Wish screen-space shaders
// (exclusion mask merge, full screen overlay, transition border, mask blur).
#ifndef PORT_WISH_SCREEN_INCLUDED
#define PORT_WISH_SCREEN_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;

struct PortWishBlitV2F
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;    // uv for _MainTex (the blit source)
    float2 uvAux : TEXCOORD1; // uv for any other screen-sized texture
};

// When the blit source arrives upside down (D3D/Metal style targets with MSAA or a
// flipped intermediate), Unity reports a negative texel height. The source uv is
// already correct in that case, but every other screen texture must be flipped to match.
inline float2 PortWishAuxUV(float2 uv)
{
#if UNITY_UV_STARTS_AT_TOP
    if (_MainTex_TexelSize.y < 0.0)
        uv.y = 1.0 - uv.y;
#endif
    return uv;
}

// _UV_Transform is a 2x2 matrix stored row-wise (xy = first row, zw = second row),
// applied around the screen centre. The scripts always pass identity (1,0,0,1).
inline float2 PortWishApplyUVTransform(float2 uv, float4 m)
{
    float2 c = uv - 0.5;
    return float2(dot(c, m.xy), dot(c, m.zw)) + 0.5;
}

// Normalised screen coordinate (0,0 bottom-left), matching Camera.WorldToScreenPoint / size.
inline float2 PortWishScreenUV(PortWishBlitV2F i)
{
    return i.uvAux;
}

PortWishBlitV2F PortWishVertBlit(appdata_img v)
{
    PortWishBlitV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord.xy;
    o.uvAux = PortWishAuxUV(v.texcoord.xy);
    return o;
}

// Mask geometry pass: plain object-space position, no lighting, no textures.
struct PortWishMaskV2F
{
    float4 pos : SV_POSITION;
};

PortWishMaskV2F PortWishVertMask(appdata_base v)
{
    PortWishMaskV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    return o;
}

#endif // PORT_WISH_SCREEN_INCLUDED
