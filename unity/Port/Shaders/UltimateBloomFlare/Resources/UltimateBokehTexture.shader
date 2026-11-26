// From-scratch reimplementation for the port: "Hidden/Ultimate/BokehTexture".
// Package leftover with no caller in the game. Kept as a single pass that
// outputs _MainTex scaled by _Intensity.
Shader "Hidden/Ultimate/BokehTexture" {
Properties {
 _MainTex ("Base (RGB) Trans (A)", 2D) = "white" { }
 _Intensity ("Intensity", Float) = 1
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortFlareVert
        #pragma fragment PortBokehTextureFrag
        #pragma target 3.0
        #include "../PortUltimateFlare.cginc"

        float4 PortBokehTextureFrag(PortFlareV2F i) : SV_Target
        {
            return tex2D(_MainTex, i.uv) * _Intensity;
        }
        ENDCG
    }
}
Fallback Off
}
