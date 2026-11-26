// From-scratch reimplementation for the port: "Hidden/Ultimate/BloomMixer".
// Small blend helpers used when the flare/anamorphic/star images are merged:
//  pass 0: _ColorBuffer + src * _Intensity            (RenderTextureAdditive)
//  pass 1: src * _Intensity0 + _ColorBuffer * _Intensity1   (CombineAdditive)
//  pass 2: src * _Intensity                           (BlitIntensity)
Shader "Hidden/Ultimate/BloomMixer" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "../PortUltimateBloom.cginc"
    float _Intensity0;
    float _Intensity1;
    ENDCG

    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        float4 frag(PortUBV2F i) : SV_Target
        {
            return tex2D(_ColorBuffer, i.uv) + tex2D(_MainTex, i.uv) * _Intensity;
        }
        ENDCG
    }

    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        float4 frag(PortUBV2F i) : SV_Target
        {
            return tex2D(_MainTex, i.uv) * _Intensity0 + tex2D(_ColorBuffer, i.uv) * _Intensity1;
        }
        ENDCG
    }

    Pass {
        CGPROGRAM
        #pragma vertex PortUBVert
        #pragma fragment frag
        #pragma target 3.0
        float4 frag(PortUBV2F i) : SV_Target
        {
            return tex2D(_MainTex, i.uv) * _Intensity;
        }
        ENDCG
    }
}
Fallback Off
}
