// From-scratch reimplementation for the port: "Hidden/NFAA".
// Normal-filter anti-aliasing: a luma Sobel builds a screen-space edge normal
// (sample spacing _OffsetScale texels, gain _BlurRadius); the pixel is then
// blurred along the edge tangent. Pass 0 = filtered image, pass 1 = generated
// normals for debugging (Antialiasing.showGeneratedNormals).
Shader "Hidden/NFAA" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
 _BlurTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "PortFxAA.cginc"

    float _OffsetScale;
    float _BlurRadius;

    // Edge tangent in texels, clamped to 1.5 texels.
    float2 NfaaNormal(float2 uv, float2 tx)
    {
        float strength;
        float spacing = clamp(_OffsetScale, 0.1, 4.0);
        float2 t = PortFxEdgeTangent(uv, tx, spacing, strength);
        float2 n = t * _BlurRadius;
        float len = length(n);
        if (len < 1e-4)
            return float2(0.0, 0.0);
        return n * (min(len, 1.5) / len);
    }

    float4 fragFilter(PortFxV2f i) : SV_Target
    {
        float2 tx = PortFxTexel();
        float4 c = PortFxTap(i.uv);
        float2 n = NfaaNormal(i.uv, tx);
        if (dot(n, n) < 0.01)
            return c;
        float2 o = n * tx;
        float4 sum = c;
        sum += PortFxTap(i.uv + o);
        sum += PortFxTap(i.uv - o);
        sum += PortFxTap(i.uv + o * 0.5);
        sum += PortFxTap(i.uv - o * 0.5);
        return sum * 0.2;
    }

    float4 fragNormals(PortFxV2f i) : SV_Target
    {
        float2 n = NfaaNormal(i.uv, PortFxTexel()) / 1.5;
        return float4(n * 0.5 + 0.5, 1.0, 1.0);
    }
    ENDCG

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragFilter
        #pragma target 3.0
        ENDCG
    }

    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment fragNormals
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
