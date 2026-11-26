// From-scratch reimplementation for the port.
// Used as an override material by EffectExcludeMask (CommandBuffer.DrawRenderer) to mark the
// screen coverage of "EffectMasked" renderers with 1 in the R8 _EffectedMask target.
// The override material carries no textures, so there is nothing to alpha-test against.
Shader "Wish/Effect Mask" {
    SubShader {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass {
            ZTest LEqual
            ZWrite Off
            Cull Off
            ColorMask R

            CGPROGRAM
            #pragma vertex PortWishVertMask
            #pragma fragment frag
            #include "PortWishScreen.cginc"

            fixed4 frag(PortWishMaskV2F i) : SV_Target
            {
                return fixed4(1, 1, 1, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
