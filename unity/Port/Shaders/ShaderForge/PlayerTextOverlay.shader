// From-scratch reimplementation for the port.
// Player name text printed onto the UGC notebook page. _MainTex is a render texture with dark
// text on a white background (TextToTexture). Its brightness is pushed through a contrast
// remap (_ContrastMin.._ContrastMax) to give crisp, heavy ink, the dark specks of _Dirt_Mask
// wear the ink away, and the result multiplies the page underneath (white leaves it untouched).
Shader "Shader Forge/PlayerTextOverlay" {
    Properties {
        _MainTex ("TextMask", 2D) = "white" {}
        _Dirt_Mask ("Dirt_Mask", 2D) = "white" {}
        _ContrastMin ("ContrastMin", Range(-3, 1)) = 0
        _ContrastMax ("ContrastMax", Range(0, 3)) = 1
    }

    SubShader {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Pass {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
            Blend DstColor Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "../Wish/PortWishUnlit.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _Dirt_Mask;
            float4 _Dirt_Mask_ST;
            half _ContrastMin;
            half _ContrastMax;

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uvText : TEXCOORD0;
                float2 uvDirt : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(PortWishUnlitAppData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvText = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.uvDirt = TRANSFORM_TEX(v.texcoord, _Dirt_Mask);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half text = Luminance(tex2D(_MainTex, i.uvText).rgb);
                half paper = saturate(lerp(_ContrastMin, _ContrastMax, text));
                half dirt = Luminance(tex2D(_Dirt_Mask, i.uvDirt).rgb);
                half ink = (1.0 - paper) * dirt;
                fixed4 col = fixed4((1.0 - ink).xxx, 1.0);
                // Multiplicative blend: fog fades the print out towards white (no change).
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(1, 1, 1, 1));
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
