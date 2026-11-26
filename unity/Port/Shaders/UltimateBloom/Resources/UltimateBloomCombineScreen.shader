// From-scratch reimplementation for the port: "Hidden/Ultimate/BloomCombineScreen".
// Screen blend of the bloom (and flare) onto the scene, capped at
// _ScreenMaxIntensity, no lens dust.
// _MainTex = last bloom upsample (* _Intensity), _ColorBuffer = scene,
// _FlareTexture (* _FlareIntensity). Pass 0 normal, pass 1 inverted image.
Shader "Hidden/Ultimate/BloomCombineScreen" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "black" { }
 _FlareTexture ("Flare (RGB)", 2D) = "black" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    // Pass 0: normal
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBCombineScreen(i.uv, false, false);
        }
        ENDCG
    }

    // Pass 1: inverted image
    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateBloom.cginc"
        float4 frag(PortUBV2F i) : SV_Target
        {
            return PortUBCombineScreen(i.uv, true, false);
        }
        ENDCG
    }
}
Fallback Off
}
