// From-scratch reimplementation for the port: "Hidden/FXAA Preset 2".
// Single-pass edge-search FXAA, medium search length.
Shader "Hidden/FXAA Preset 2" {
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
            return PortFxaaQuality(i.uv, PortFxTexel(), 8, 1.0 / 16.0, 1.0 / 8.0, 0.75);
        }
        ENDCG
    }
}
Fallback Off
}
