// From-scratch reimplementation for the port: "Hidden/DLAA".
// Directionally localized anti-aliasing in two steps.
// Pass 0: copy colour and store an edge mask (luma Laplacian) in alpha.
// Pass 1: blur masked pixels along the edge tangent (soft).
// Pass 2: same with shorter taps and a lower blend (Antialiasing.dlaaSharp).
Shader "Hidden/DLAA" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "PortFxAA.cginc"

    float4 fragEdge(PortFxV2f i) : SV_Target
    {
        float2 tx = PortFxTexel();
        float4 c = PortFxTap(i.uv);
        float lM = PortFxLuma(c.rgb);
        float lN = PortFxLumaAt(i.uv + float2(0.0, -tx.y));
        float lS = PortFxLumaAt(i.uv + float2(0.0,  tx.y));
        float lW = PortFxLumaAt(i.uv + float2(-tx.x, 0.0));
        float lE = PortFxLumaAt(i.uv + float2( tx.x, 0.0));
        float lap = abs(4.0 * lM - (lN + lS + lW + lE));
        return float4(c.rgb, smoothstep(0.03, 0.3, lap));
    }

    float4 DlaaBlur(float2 uv, float reach, float amount)
    {
        float2 tx = PortFxTexel();
        float4 c = PortFxTap(uv);
        float mask = c.a;
        if (mask < 1.0 / 255.0)
            return float4(c.rgb, 1.0);

        float strength;
        float2 t = PortFxEdgeTangent(uv, tx, 1.0, strength);
        if (strength < 1e-4)
            return float4(c.rgb, 1.0);
        float2 u = t / strength;
        float2 d = u * tx;

        // Taps along the edge, each weighted by its own edge mask so that the
        // blur stays on the edge and does not bleed into flat areas.
        float3 sum = c.rgb;
        float wsum = 1.0;
        [unroll]
        for (int k = 1; k <= 3; k++)
        {
            float dist = reach * k;
            float4 a = PortFxTap(uv + d * dist);
            float4 b = PortFxTap(uv - d * dist);
            float wa = saturate(a.a * 2.0);
            float wb = saturate(b.a * 2.0);
            sum += a.rgb * wa + b.rgb * wb;
            wsum += wa + wb;
        }
        // Half-texel shift across the edge softens short steps.
        float2 n = float2(u.y, -u.x) * tx;
        float3 across = 0.5 * (PortFxTap(uv + n * 0.5).rgb + PortFxTap(uv - n * 0.5).rgb);
        float3 blurred = lerp(sum / wsum, across, 0.25);
        return float4(lerp(c.rgb, blurred, mask * amount), 1.0);
    }

    float4 fragBlur(PortFxV2f i) : SV_Target
    {
        return DlaaBlur(i.uv, 1.0, 0.75);
    }

    float4 fragSharp(PortFxV2f i) : SV_Target
    {
        return DlaaBlur(i.uv, 0.5, 0.5);
    }
    ENDCG

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragEdge
        #pragma target 3.0
        ENDCG
    }

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragBlur
        #pragma target 3.0
        ENDCG
    }

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragSharp
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
