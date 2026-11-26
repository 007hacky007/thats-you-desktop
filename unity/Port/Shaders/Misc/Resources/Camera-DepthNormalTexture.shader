// From-scratch reimplementation for the port: "Hidden/Camera-DepthNormalTexture".
// Replacement shader that renders linear depth and view space normals packed with
// EncodeDepthNormal, one SubShader per RenderType tag. Opaque and bark geometry
// write every pixel; cutout geometry first discards texels whose alpha
// (_MainTex.a * _Color.a) is below _Cutoff.
Shader "Hidden/Camera-DepthNormalTexture" {
Properties {
 _MainTex ("", 2D) = "white" { }
 _Cutoff ("", Float) = 0.5
 _Color ("", Color) = (1,1,1,1)
}

CGINCLUDE
#include "../PortMisc.cginc"

sampler2D _MainTex;
float4 _MainTex_ST;
float _Cutoff;
float4 _Color;

PortDNV2f vertDN(PortDNAppData v)
{
    return PortDNVert(v, _MainTex_ST);
}

float4 fragOpaque(PortDNV2f i) : SV_Target
{
    return PortDNEncode(i);
}

float4 fragCutout(PortDNV2f i) : SV_Target
{
    float a = tex2D(_MainTex, i.uv).a * _Color.a;
    clip(a - _Cutoff);
    return PortDNEncode(i);
}
ENDCG

SubShader {
    Tags { "RenderType"="Opaque" }
    Pass {
        CGPROGRAM
        #pragma vertex vertDN
        #pragma fragment fragOpaque
        #pragma multi_compile_instancing
        ENDCG
    }
}

SubShader {
    Tags { "RenderType"="TransparentCutout" }
    Pass {
        Cull Off
        CGPROGRAM
        #pragma vertex vertDN
        #pragma fragment fragCutout
        #pragma multi_compile_instancing
        ENDCG
    }
}

SubShader {
    Tags { "RenderType"="TreeBark" }
    Pass {
        CGPROGRAM
        #pragma vertex vertDN
        #pragma fragment fragOpaque
        #pragma multi_compile_instancing
        ENDCG
    }
}

SubShader {
    Tags { "RenderType"="TreeLeaf" }
    Pass {
        Cull Off
        CGPROGRAM
        #pragma vertex vertDN
        #pragma fragment fragCutout
        #pragma multi_compile_instancing
        ENDCG
    }
}

SubShader {
    Tags { "RenderType"="TreeOpaque" }
    Pass {
        CGPROGRAM
        #pragma vertex vertDN
        #pragma fragment fragOpaque
        #pragma multi_compile_instancing
        ENDCG
    }
}

SubShader {
    Tags { "RenderType"="TreeTransparentCutout" }
    Pass {
        Cull Off
        CGPROGRAM
        #pragma vertex vertDN
        #pragma fragment fragCutout
        #pragma multi_compile_instancing
        ENDCG
    }
}

Fallback Off
}
