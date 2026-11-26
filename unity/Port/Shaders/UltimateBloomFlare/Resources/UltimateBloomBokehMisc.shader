// From-scratch reimplementation for the port: "Hidden/Ultimate/BokehMisc".
// Package leftover with no caller in the game. Kept as a plain copy.
Shader "Hidden/Ultimate/BokehMisc" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortFlareVert
        #pragma fragment PortBokehCopyFrag
        #pragma target 3.0
        #include "../PortUltimateFlare.cginc"

        float4 PortBokehCopyFrag(PortFlareV2F i) : SV_Target
        {
            return tex2D(_MainTex, i.uv);
        }
        ENDCG
    }
}
Fallback Off
}
