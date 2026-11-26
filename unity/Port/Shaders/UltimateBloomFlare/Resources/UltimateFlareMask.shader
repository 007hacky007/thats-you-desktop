// From-scratch reimplementation for the port: "Hidden/Ultimate/FlareMask".
// Multiplies the bokeh flare image by the lens-flare mask texture before the
// ghosts are generated (UltimateBloom, pass 0).
Shader "Hidden/Ultimate/FlareMask" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
 _MaskTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortFlareVert
        #pragma fragment PortFlareMaskFrag
        #pragma target 3.0
        #include "../PortUltimateFlare.cginc"

        sampler2D _MaskTex;

        float4 PortFlareMaskFrag(PortFlareV2F i) : SV_Target
        {
            return tex2D(_MainTex, i.uv) * tex2D(_MaskTex, i.uv);
        }
        ENDCG
    }
}
Fallback Off
}
