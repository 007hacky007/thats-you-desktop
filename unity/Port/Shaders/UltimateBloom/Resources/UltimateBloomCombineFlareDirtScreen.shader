// From-scratch reimplementation for the port: "Hidden/Ultimate/BloomCombineFlareDirtScreen".
// Screen blend of bloom, flare and lens dust onto the scene, capped at
// _ScreenMaxIntensity. The dust is lit by the bloom and the flares.
// _MainTex = last bloom upsample (* _Intensity), _ColorBuffer = scene,
// _FlareTexture (* _FlareIntensity). Pass 0 normal, pass 1 inverted image.
Shader "Hidden/Ultimate/BloomCombineFlareDirtScreen" {
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
            return PortUBCombineScreen(i.uv, false, true);
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
            return PortUBCombineScreen(i.uv, true, true);
        }
        ENDCG
    }
}
Fallback Off
}
