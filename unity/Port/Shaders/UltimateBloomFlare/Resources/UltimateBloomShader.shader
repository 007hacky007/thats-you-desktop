// From-scratch reimplementation for the port: "Hidden/Ultimate/Bloom".
// Package leftover with no caller in the game. Kept as a single-pass blit
// that adds _AdditiveTexture onto _MainTex. _OffsetInfos is accepted but unused.
Shader "Hidden/Ultimate/Bloom" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
 _AdditiveTexture ("Base (RGB)", 2D) = "black" { }
 _OffsetInfos ("HorizontalOffset", Vector) = (0,0,0,0)
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortFlareVert
        #pragma fragment PortBloomAddFrag
        #pragma target 3.0
        #include "../PortUltimateFlare.cginc"

        sampler2D _AdditiveTexture;
        float4 _OffsetInfos;

        float4 PortBloomAddFrag(PortFlareV2F i) : SV_Target
        {
            return tex2D(_MainTex, i.uv) + tex2D(_AdditiveTexture, i.uv);
        }
        ENDCG
    }
}
Fallback Off
}
