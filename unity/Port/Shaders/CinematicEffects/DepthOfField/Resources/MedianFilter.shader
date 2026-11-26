// From-scratch reimplementation for the port: "Hidden/DepthOfField/MedianFilter".
// Median filter used by UnityStandardAssets.CinematicEffects.DepthOfField to
// clean up the half-resolution blur. Written for this port; not derived from
// any original shader source.
//
// Pass order matches DepthOfField.MedianPasses:
//  0 Median3    three taps along _Offsets.xy (texels), per channel
//  1 Median3X3  nine taps in a 3x3 block, per channel
// The alpha (signed CoC) of the centre pixel is passed through unchanged.
Shader "Hidden/DepthOfField/MedianFilter" {
Properties {
 _MainTex ("-", 2D) = "black" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #pragma target 3.5
    #include "../PortCinematicDof.cginc"

    float3 PortMed3(float3 a, float3 b, float3 c)
    {
        return max(min(a, b), min(max(a, b), c));
    }

    float4 fragMedian3(PortDofV2f i) : SV_Target
    {
        float2 ofs = _Offsets.xy * PortDofTexel();
        float4 c = PortDofTap(i.uv);
        float3 a = PortDofTap(i.uv - ofs).rgb;
        float3 b = PortDofTap(i.uv + ofs).rgb;
        return float4(PortMed3(a, c.rgb, b), c.a);
    }

    // Exact median of nine per channel: sort each row of three, then take the
    // median of (largest row minimum, median of row medians, smallest row
    // maximum).
    float4 fragMedian3x3(PortDofV2f i) : SV_Target
    {
        float2 tx = PortDofTexel();
        float4 c = PortDofTap(i.uv);
        float3 v[9];
        [unroll]
        for (int y = 0; y < 3; y++)
        {
            [unroll]
            for (int x = 0; x < 3; x++)
            {
                v[y * 3 + x] = (x == 1 && y == 1) ? c.rgb : PortDofTap(i.uv + float2(x - 1, y - 1) * tx).rgb;
            }
        }
        float3 lo[3];
        float3 mi[3];
        float3 hi[3];
        [unroll]
        for (int r = 0; r < 3; r++)
        {
            float3 a = v[r * 3 + 0];
            float3 b = v[r * 3 + 1];
            float3 d = v[r * 3 + 2];
            lo[r] = min(min(a, b), d);
            hi[r] = max(max(a, b), d);
            mi[r] = PortMed3(a, b, d);
        }
        float3 maxLo = max(max(lo[0], lo[1]), lo[2]);
        float3 minHi = min(min(hi[0], hi[1]), hi[2]);
        float3 medMid = PortMed3(mi[0], mi[1], mi[2]);
        return float4(PortMed3(maxLo, medMid, minHi), c.a);
    }
    ENDCG

    // 0 Median3
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment fragMedian3
        ENDCG
    }

    // 1 Median3X3
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment fragMedian3x3
        ENDCG
    }
}
Fallback Off
}
