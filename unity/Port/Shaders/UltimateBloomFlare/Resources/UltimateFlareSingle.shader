// From-scratch reimplementation for the port: "Hidden/Ultimate/FlareSingle".
// Lens-flare ghosts, single set: four mirrored, scaled and tinted copies of
// the bright-pass image (_FlareScales, _FlareTint0..3, _Intensity).
// UltimateBloom.RenderFlares blits through pass 0 only.
Shader "Hidden/Ultimate/FlareSingle" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortFlareVert
        #pragma fragment PortFlareSingleFrag
        #pragma target 3.0
        #include "../PortUltimateFlare.cginc"

        ENDCG
    }
}
Fallback Off
}
