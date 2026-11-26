// From-scratch reimplementation for the port of the light shaft shadow map shader.
// Used as a replacement shader (Camera.RenderWithShader, tag "RenderType") by the light's
// depth camera. Writes the view depth divided by the depth camera's far plane into the
// RFloat shadow map; the clear colour (white) means "no occluder".
Shader "Hidden/Depth"
{
    Properties
    {
        _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    fixed4 _Color;
    fixed _Cutoff;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float depth : TEXCOORD1;
    };

    v2f vert(appdata_base v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
        o.depth = -UnityObjectToViewPos(v.vertex).z * _ProjectionParams.w;
        return o;
    }

    float4 frag_opaque(v2f i) : SV_Target
    {
        return i.depth.xxxx;
    }

    float4 frag_cutout(v2f i) : SV_Target
    {
        fixed a = tex2D(_MainTex, i.uv).a * _Color.a;
        clip(a - _Cutoff);
        return i.depth.xxxx;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_opaque
            #pragma target 3.0
            ENDCG
        }
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_cutout
            #pragma target 3.0
            ENDCG
        }
    }

    Fallback Off
}
