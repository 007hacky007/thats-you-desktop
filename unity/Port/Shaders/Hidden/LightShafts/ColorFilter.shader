// From-scratch reimplementation for the port of the light shaft colour filter shader.
// Used as a replacement shader (Camera.RenderWithShader, no tag) by the light's depth camera
// on the colour filter layers. Writes each filter object's tint into the ARGB32 colour
// filter map, which the raymarch pass multiplies into the light. The clear colour is the
// grey "colour balance" used where no filter is present.
Shader "Hidden/ColorFilter"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                return fixed4(c.rgb, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
