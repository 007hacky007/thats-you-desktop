// From-scratch reimplementation for the port.
// Shared vertex/fragment code for the NGUI unlit UI shaders. Each wrapper shader picks one
// base type and one clip mode with defines before including this file:
//   base type   PORT_NGUI_COLORED | PORT_NGUI_TEXT | PORT_NGUI_PREMUL | PORT_NGUI_MASKED | PORT_NGUI_PACKED
//   clip mode   PORT_NGUI_CLIP_COUNT 0..3 (rectangular soft clip by nested panels)
//               or PORT_NGUI_TEXTURE_CLIP (alpha mask texture over the panel rectangle)
//
// Uniforms set by UIDrawCall (all in draw-call object space):
//   _ClipRangeN  xy = -center / halfSize, zw = 1 / halfSize, so pos * zw + xy is -1..1 inside
//   _ClipArgsN   xy = edge sharpness (halfSize / softness, 1000 = hard edge),
//                zw = sin / cos of the panel angle relative to this draw call
//   _ClipTex     texture-mask clip, sampled over the panel rectangle (TextureClip only)
#ifndef PORT_NGUI_INCLUDED
#define PORT_NGUI_INCLUDED

#include "UnityCG.cginc"

#ifndef PORT_NGUI_CLIP_COUNT
#define PORT_NGUI_CLIP_COUNT 0
#endif

sampler2D _MainTex;

#if defined(PORT_NGUI_COLORED)
float _GreyScale;
#endif

#if defined(PORT_NGUI_MASKED)
sampler2D _Mask;
#endif

#if defined(PORT_NGUI_TEXTURE_CLIP)
sampler2D _ClipTex;
float4 _ClipRange0;
#elif PORT_NGUI_CLIP_COUNT >= 1
float4 _ClipRange0;
float4 _ClipArgs0;
#if PORT_NGUI_CLIP_COUNT >= 2
float4 _ClipRange1;
float4 _ClipArgs1;
#endif
#if PORT_NGUI_CLIP_COUNT >= 3
float4 _ClipRange2;
float4 _ClipArgs2;
#endif
#endif

struct PortNguiAppData
{
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct PortNguiV2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
#if defined(PORT_NGUI_TEXTURE_CLIP)
    float2 clipUV : TEXCOORD1;
#elif PORT_NGUI_CLIP_COUNT >= 1
    float2 clip0 : TEXCOORD1;
#if PORT_NGUI_CLIP_COUNT >= 2
    float4 clip12 : TEXCOORD2;
#endif
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};

// Turns a 2D point by the angle given as (sin, cos).
inline float2 PortNguiTurn(float2 p, float2 sinCos)
{
    return float2(p.x * sinCos.y - p.y * sinCos.x,
                  p.x * sinCos.x + p.y * sinCos.y);
}

// Edge fade for one clip rectangle: 1 well inside, falling to 0 at the edge over the softness.
inline float PortNguiEdge(float2 clipPos, float2 sharpness)
{
    float2 f = (1.0 - abs(clipPos)) * sharpness;
    return min(f.x, f.y);
}

PortNguiV2f PortNguiVert(PortNguiAppData v)
{
    PortNguiV2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    o.color = v.color;
#if defined(PORT_NGUI_TEXTURE_CLIP)
    o.clipUV = (v.vertex.xy * _ClipRange0.zw + _ClipRange0.xy) * 0.5 + 0.5;
#elif PORT_NGUI_CLIP_COUNT >= 1
    o.clip0 = v.vertex.xy * _ClipRange0.zw + _ClipRange0.xy;
#if PORT_NGUI_CLIP_COUNT >= 2
    o.clip12.xy = PortNguiTurn(v.vertex.xy, _ClipArgs1.zw) * _ClipRange1.zw + _ClipRange1.xy;
#if PORT_NGUI_CLIP_COUNT >= 3
    o.clip12.zw = PortNguiTurn(v.vertex.xy, _ClipArgs2.zw) * _ClipRange2.zw + _ClipRange2.xy;
#else
    o.clip12.zw = 0;
#endif
#endif
#endif
    return o;
}

// Combined clip visibility (0..1) for this fragment.
inline half PortNguiClipFactor(PortNguiV2f i)
{
#if defined(PORT_NGUI_TEXTURE_CLIP)
    return tex2D(_ClipTex, i.clipUV).a;
#elif PORT_NGUI_CLIP_COUNT >= 1
    float f = PortNguiEdge(i.clip0, _ClipArgs0.xy);
#if PORT_NGUI_CLIP_COUNT >= 2
    f = min(f, PortNguiEdge(i.clip12.xy, _ClipArgs1.xy));
#endif
#if PORT_NGUI_CLIP_COUNT >= 3
    f = min(f, PortNguiEdge(i.clip12.zw, _ClipArgs2.xy));
#endif
    return (half)saturate(f);
#else
    return 1.0h;
#endif
}

half4 PortNguiFrag(PortNguiV2f i) : SV_Target
{
    half4 vcol = i.color;
    half4 col;

#if defined(PORT_NGUI_TEXT)
    // Dynamic font atlas: coverage lives in alpha, colour comes from the vertices.
    col.rgb = vcol.rgb;
    col.a = vcol.a * tex2D(_MainTex, i.uv).a;
#elif defined(PORT_NGUI_PACKED)
    // Packed bitmap font: each glyph lives in one channel of the atlas. The label colour is
    // stored scaled by 0.49 with 0.51 added on the glyph's channel, so the channel above one
    // half is the one to read and the original colour is recovered by undoing that encoding.
    half4 tex = tex2D(_MainTex, i.uv);
    half4 pick = saturate((vcol - 0.5h) * 1000.0h);
    half4 orig = saturate((vcol - pick * 0.51h) / 0.49h);
    col.rgb = orig.rgb;
    col.a = orig.a * dot(tex, pick);
#else
    col = tex2D(_MainTex, i.uv) * vcol;
#if defined(PORT_NGUI_MASKED)
    col.a *= tex2D(_Mask, i.uv).a;
#endif
#if defined(PORT_NGUI_COLORED)
    // Game addition: _GreyScale pushes the colour toward its luminance.
    half grey = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
    col.rgb = lerp(col.rgb, grey.xxx, saturate(_GreyScale));
#endif
#endif

    half clipFactor = PortNguiClipFactor(i);
#if defined(PORT_NGUI_PREMUL)
    // Premultiplied colour: fade colour together with alpha.
    col *= clipFactor;
#else
    col.a *= clipFactor;
#endif
    return col;
}

#endif
