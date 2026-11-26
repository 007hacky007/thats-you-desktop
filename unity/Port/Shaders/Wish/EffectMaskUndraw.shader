// From-scratch reimplementation for the port.
// Used as an override material by EffectExcludeMask (CommandBuffer.DrawRenderer) for
// "EffectNonMasked" renderers: they are drawn after the masked ones and write 0 into the
// R8 _EffectedMask target, cutting holes where they cover masked objects.
Shader "Wish/Effect Unmask" {
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
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
    Fallback Off
}
