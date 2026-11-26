// From-scratch reimplementation for the port: "Hidden/Ultimate/BloomCombineFlareDirt".
// Additive blend of bloom, flare and lens dust onto the scene. The dust
// (_AdditiveTexture) is lit by the bloom (_DirtIntensity) and the flares
// (_DirtLightIntensity).
// _MainTex = last bloom upsample (* _Intensity), _ColorBuffer = scene,
// _FlareTexture (* _FlareIntensity). Pass 0 normal, pass 1 inverted image.
Shader "Hidden/Ultimate/BloomCombineFlareDirt" {
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
            return PortUBCombineAdd(i.uv, false, true);
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
            return PortUBCombineAdd(i.uv, true, true);
        }
        ENDCG
    }
}
Fallback Off
}
