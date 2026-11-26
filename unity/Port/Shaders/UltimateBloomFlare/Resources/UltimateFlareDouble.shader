// From-scratch reimplementation for the port: "Hidden/Ultimate/FlareDouble".
// Lens-flare ghosts, double set: four far ghosts (_FlareScales, _FlareTint0..3)
// plus four near ghosts (_FlareScalesNear, _FlareTint4..7), scaled by
// _Intensity. UltimateBloom.RenderFlares blits through pass 0 only.
Shader "Hidden/Ultimate/FlareDouble" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortFlareVert
        #pragma fragment PortFlareDoubleFrag
        #pragma target 3.0
        #include "../PortUltimateFlare.cginc"

        ENDCG
    }
}
Fallback Off
}
