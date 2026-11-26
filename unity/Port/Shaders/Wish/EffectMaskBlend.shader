// From-scratch reimplementation for the port.
// Final merge for ExcludedEffectMerge: the post-processed frame (_MainTex) is blended back
// towards the frame grabbed before image effects (_UneffectedTexture, set globally by
// EffectExcludeMask / ExcludedEffectGrab) wherever the R8 exclusion mask (_EffectedMask) is set.
// blendFactor scales how much of the untouched frame is restored; flipBacking flips the
// grabbed frame vertically for cameras whose grab ends up upside down.
// _UneffectedTexture and _EffectedMask are globals on purpose: they are not declared as
// properties so a material value can never shadow them.
Shader "Wish/EffectMaskBlend" {
    Properties {
        _MainTex ("Current Image", 2D) = "" {}
        _UV_Transform ("UV Transform (2x2 rows)", Vector) = (1, 0, 0, 1)
        flipBacking ("Flip Backing", Float) = 0
        blendFactor ("Blend Factor", Range(0, 1)) = 1
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma vertex PortWishVertBlit
            #pragma fragment frag
            #pragma target 3.0
            #include "PortWishScreen.cginc"

            sampler2D _UneffectedTexture;
            sampler2D _EffectedMask;
            float4 _UV_Transform;
            float flipBacking;
            float blendFactor;

            fixed4 frag(PortWishBlitV2F i) : SV_Target
            {
                fixed4 effected = tex2D(_MainTex, i.uv);

                float2 auv = PortWishApplyUVTransform(i.uvAux, _UV_Transform);
                float2 buv = auv;
                if (flipBacking > 0.5)
                    buv.y = 1.0 - buv.y;

                fixed3 untouched = tex2D(_UneffectedTexture, buv).rgb;
                float mask = saturate(tex2D(_EffectedMask, auv).r);

                effected.rgb = lerp(effected.rgb, untouched, saturate(mask * blendFactor));
                return effected;
            }
            ENDCG
        }
    }
    Fallback Off
}
