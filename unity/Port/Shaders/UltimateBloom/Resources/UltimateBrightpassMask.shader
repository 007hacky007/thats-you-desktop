// From-scratch reimplementation for the port: "Hidden/Ultimate/BrightpassMask".
// Pass 0: keeps what lies above the (per channel) threshold _Threshhold and
// multiplies it by the screen-space mask _MaskTex (white when no mask is used).
Shader "Hidden/Ultimate/BrightpassMask" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
 _MaskTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"

        sampler2D _MaskTex;
        float4 _Threshhold;

        float4 frag(PortUBV2F i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            float4 m = tex2D(_MaskTex, i.uv);
            return max(c - _Threshhold, 0.0) * m;
        }
        ENDCG
    }
}
Fallback Off
}
