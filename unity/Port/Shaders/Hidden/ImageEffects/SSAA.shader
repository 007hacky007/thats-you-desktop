// From-scratch reimplementation for the port: "Hidden/SSAA".
// Single-pass contrast-adaptive resolve: on high-contrast pixels the image is
// replaced by a rotated-grid set of sub-texel bilinear taps (an approximation
// of supersampling); flat areas pass through unchanged.
Shader "Hidden/SSAA" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxAA.cginc"

        float4 frag(PortFxV2f i) : SV_Target
        {
            float2 tx = PortFxTexel();
            float4 c = PortFxTap(i.uv);
            float lM = PortFxLuma(c.rgb);
            float lN = PortFxLumaAt(i.uv + float2(0.0, -tx.y));
            float lS = PortFxLumaAt(i.uv + float2(0.0,  tx.y));
            float lW = PortFxLumaAt(i.uv + float2(-tx.x, 0.0));
            float lE = PortFxLumaAt(i.uv + float2( tx.x, 0.0));
            float lMax = max(max(max(lN, lS), max(lW, lE)), lM);
            float lMin = min(min(min(lN, lS), min(lW, lE)), lM);
            float w = saturate((lMax - lMin - 0.04) * 6.0);
            if (w <= 0.0)
                return c;

            // Rotated-grid sub-sample pattern (4 taps at 1/8 and 3/8 texel).
            float4 rg = PortFxTap(i.uv + float2( 0.125, -0.375) * tx);
            rg += PortFxTap(i.uv + float2( 0.375,  0.125) * tx);
            rg += PortFxTap(i.uv + float2(-0.125,  0.375) * tx);
            rg += PortFxTap(i.uv + float2(-0.375, -0.125) * tx);
            rg *= 0.25;
            return lerp(c, rg, w);
        }
        ENDCG
    }
}
Fallback Off
}
