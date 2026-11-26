// From-scratch reimplementation for the port: shared helpers for the full-screen
// anti-aliasing and depth-of-field image effects (built-in render pipeline).
// Written for this port; not derived from any original shader source.

#ifndef PORT_FX_AA_INCLUDED
#define PORT_FX_AA_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_TexelSize;

struct PortFxAppData
{
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
};

struct PortFxV2f
{
    float4 pos : SV_POSITION;
    // uv samples _MainTex; uvAux samples every other screen-space texture
    // (depth, secondary render targets), flipped when the source is upside down.
    float2 uv : TEXCOORD0;
    float2 uvAux : TEXCOORD1;
};

PortFxV2f PortFxVert(PortFxAppData v)
{
    PortFxV2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    o.uvAux = v.texcoord;
#if UNITY_UV_STARTS_AT_TOP
    if (_MainTex_TexelSize.y < 0)
        o.uvAux.y = 1.0 - o.uvAux.y;
#endif
    return o;
}

// Size of one source texel, always positive so that distances stay meaningful.
float2 PortFxTexel()
{
    return abs(_MainTex_TexelSize.xy);
}

// Explicit-LOD fetch: safe inside loops and branches on every backend.
float4 PortFxTap(float2 uv)
{
    return tex2Dlod(_MainTex, float4(uv, 0.0, 0.0));
}

float PortFxLuma(float3 rgb)
{
    return dot(rgb, float3(0.299, 0.587, 0.114));
}

float PortFxLumaAt(float2 uv)
{
    return PortFxLuma(PortFxTap(uv).rgb);
}

float4 PortFxCopy(PortFxV2f i) : SV_Target
{
    return PortFxTap(i.uv);
}

// ---------------------------------------------------------------------------
// Fast luma-edge FXAA (console style).
// Four half-texel diagonal bilinear taps give the local luma gradient. Pixels
// whose contrast is below the threshold are returned untouched. On edges, two
// tap pairs are taken along the edge tangent: a short pair one texel apart and
// a long pair whose length grows as the edge approaches horizontal/vertical
// (controlled by sharpness). The long blend is rejected when it leaves the
// local luma range, which avoids smearing thin features.
// ---------------------------------------------------------------------------
float4 PortFxaaFast(float2 uv, float2 tx, float thresholdMin, float threshold, float sharpness)
{
    float4 center = PortFxTap(uv);
    float lM = PortFxLuma(center.rgb);

    float lNW = PortFxLumaAt(uv + float2(-0.5, -0.5) * tx);
    float lNE = PortFxLumaAt(uv + float2( 0.5, -0.5) * tx);
    float lSW = PortFxLumaAt(uv + float2(-0.5,  0.5) * tx);
    float lSE = PortFxLumaAt(uv + float2( 0.5,  0.5) * tx);

    float lMax = max(max(max(lNW, lNE), max(lSW, lSE)), lM);
    float lMin = min(min(min(lNW, lNE), min(lSW, lSE)), lM);

    if (lMax - lMin < max(thresholdMin, lMax * threshold))
        return center;

    // Tangent of the edge: perpendicular to the luma gradient.
    float2 dir;
    dir.x = (lSW + lSE) - (lNW + lNE);
    dir.y = (lNW + lSW) - (lNE + lSE);
    float dirLen = length(dir);
    if (dirLen < 1e-5)
        return center;
    float2 dir1 = dir / dirLen;

    float4 n1 = PortFxTap(uv - dir1 * tx * 0.5);
    float4 p1 = PortFxTap(uv + dir1 * tx * 0.5);

    float minComp = min(abs(dir1.x), abs(dir1.y)) * max(sharpness, 1e-3);
    float2 dir2 = clamp(dir1 / max(minComp, 1e-4), -2.0, 2.0);

    float4 n2 = PortFxTap(uv - dir2 * tx * 2.0);
    float4 p2 = PortFxTap(uv + dir2 * tx * 2.0);

    float4 blendA = (n1 + p1) * 0.5;
    float4 blendB = (n2 + p2) * 0.25 + blendA * 0.5;

    float lB = PortFxLuma(blendB.rgb);
    if (lB < lMin || lB > lMax)
        return blendA;
    return blendB;
}

// ---------------------------------------------------------------------------
// Edge-search FXAA (quality style).
// Detects whether the local edge runs horizontally or vertically, finds the
// side with the steepest gradient, then walks along the edge in both
// directions until the luma departs from the edge average. The pixel is then
// re-sampled with a sub-texel shift that depends on where it sits along the
// edge span, plus a sub-pixel term for isolated single-pixel detail.
// ---------------------------------------------------------------------------
float PortFxaaStepSize(int i)
{
    if (i < 5) return 1.0;
    if (i < 6) return 1.5;
    if (i < 10) return 2.0;
    if (i < 11) return 4.0;
    return 8.0;
}

float4 PortFxaaQuality(float2 uv, float2 tx, int steps, float thresholdMin, float threshold, float subpixel)
{
    float4 center = PortFxTap(uv);
    float lM = PortFxLuma(center.rgb);
    float lN = PortFxLumaAt(uv + float2(0.0, -tx.y));
    float lS = PortFxLumaAt(uv + float2(0.0,  tx.y));
    float lW = PortFxLumaAt(uv + float2(-tx.x, 0.0));
    float lE = PortFxLumaAt(uv + float2( tx.x, 0.0));

    float lMax = max(max(max(lN, lS), max(lW, lE)), lM);
    float lMin = min(min(min(lN, lS), min(lW, lE)), lM);
    float range = lMax - lMin;
    if (range < max(thresholdMin, lMax * threshold))
        return center;

    float lNW = PortFxLumaAt(uv + float2(-tx.x, -tx.y));
    float lNE = PortFxLumaAt(uv + float2( tx.x, -tx.y));
    float lSW = PortFxLumaAt(uv + float2(-tx.x,  tx.y));
    float lSE = PortFxLumaAt(uv + float2( tx.x,  tx.y));

    // Sub-pixel aliasing amount from the 3x3 low-pass versus the center.
    float lowPass = ((lN + lS + lW + lE) * 2.0 + (lNW + lNE + lSW + lSE)) / 12.0;
    float sub = saturate(abs(lowPass - lM) / range);
    sub = smoothstep(0.0, 1.0, sub);
    sub = sub * sub * subpixel;

    // Second derivatives: across rows (horizontal edge) or columns (vertical edge).
    float edgeHorz = abs(lNW + lSW - 2.0 * lW) + 2.0 * abs(lN + lS - 2.0 * lM) + abs(lNE + lSE - 2.0 * lE);
    float edgeVert = abs(lNW + lNE - 2.0 * lN) + 2.0 * abs(lW + lE - 2.0 * lM) + abs(lSW + lSE - 2.0 * lS);
    bool horz = edgeHorz >= edgeVert;

    float l1 = horz ? lN : lW;
    float l2 = horz ? lS : lE;
    float stepLen = horz ? tx.y : tx.x;
    float g1 = l1 - lM;
    float g2 = l2 - lM;
    bool side1 = abs(g1) >= abs(g2);
    float gradScaled = 0.25 * max(abs(g1), abs(g2));

    float edgeLuma;
    if (side1)
    {
        stepLen = -stepLen;
        edgeLuma = 0.5 * (l1 + lM);
    }
    else
    {
        edgeLuma = 0.5 * (l2 + lM);
    }

    // Start on the boundary between this pixel and the steeper neighbour.
    float2 uvEdge = uv;
    if (horz) uvEdge.y += stepLen * 0.5;
    else      uvEdge.x += stepLen * 0.5;

    float2 along = horz ? float2(tx.x, 0.0) : float2(0.0, tx.y);
    float2 uvNeg = uvEdge - along;
    float2 uvPos = uvEdge + along;
    float eNeg = PortFxLumaAt(uvNeg) - edgeLuma;
    float ePos = PortFxLumaAt(uvPos) - edgeLuma;
    bool doneNeg = abs(eNeg) >= gradScaled;
    bool donePos = abs(ePos) >= gradScaled;

    [loop]
    for (int s = 1; s < steps; s++)
    {
        if (doneNeg && donePos)
            break;
        float k = PortFxaaStepSize(s);
        if (!doneNeg)
        {
            uvNeg -= along * k;
            eNeg = PortFxLumaAt(uvNeg) - edgeLuma;
            doneNeg = abs(eNeg) >= gradScaled;
        }
        if (!donePos)
        {
            uvPos += along * k;
            ePos = PortFxLumaAt(uvPos) - edgeLuma;
            donePos = abs(ePos) >= gradScaled;
        }
    }

    float dNeg = horz ? (uv.x - uvNeg.x) : (uv.y - uvNeg.y);
    float dPos = horz ? (uvPos.x - uv.x) : (uvPos.y - uv.y);
    bool negCloser = dNeg < dPos;
    float dMin = min(dNeg, dPos);
    float span = max(dNeg + dPos, 1e-6);
    float pixelOffset = 0.5 - dMin / span;

    // Only shift when the luma at the nearer end varies the opposite way to
    // the center, i.e. this pixel really lies on the stair step.
    bool centerBelow = lM < edgeLuma;
    float eEnd = negCloser ? eNeg : ePos;
    bool valid = (eEnd < 0.0) != centerBelow;
    float offset = valid ? pixelOffset : 0.0;
    offset = max(offset, sub);

    float2 uvFinal = uv;
    if (horz) uvFinal.y += offset * stepLen;
    else      uvFinal.x += offset * stepLen;
    return PortFxTap(uvFinal);
}

// Unit edge tangent (perpendicular to the luma gradient) from a 3x3 Sobel.
// Returns zero length when there is no edge.
float2 PortFxEdgeTangent(float2 uv, float2 tx, float spacing, out float strength)
{
    float2 o = tx * spacing;
    float lNW = PortFxLumaAt(uv + float2(-o.x, -o.y));
    float lN  = PortFxLumaAt(uv + float2( 0.0, -o.y));
    float lNE = PortFxLumaAt(uv + float2( o.x, -o.y));
    float lW  = PortFxLumaAt(uv + float2(-o.x,  0.0));
    float lE  = PortFxLumaAt(uv + float2( o.x,  0.0));
    float lSW = PortFxLumaAt(uv + float2(-o.x,  o.y));
    float lS  = PortFxLumaAt(uv + float2( 0.0,  o.y));
    float lSE = PortFxLumaAt(uv + float2( o.x,  o.y));
    float gx = (lNE + 2.0 * lE + lSE) - (lNW + 2.0 * lW + lSW);
    float gy = (lSW + 2.0 * lS + lSE) - (lNW + 2.0 * lN + lNE);
    float2 t = float2(-gy, gx) * 0.25;
    strength = length(t);
    return t;
}

#endif // PORT_FX_AA_INCLUDED
