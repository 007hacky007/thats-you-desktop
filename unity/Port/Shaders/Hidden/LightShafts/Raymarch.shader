// From-scratch reimplementation for the port of the light shaft "Raymarch" pass.
// For the epipolar samples selected by the DepthBreaks pass, marches the camera ray through
// the light volume (box for directional lights, pyramid for spot lights) and integrates the
// unshadowed light, optionally tinted by the colour filter map, cookie and attenuation curve.
Shader "Hidden/Raymarch"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex ls_vert_epi
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile DIRECTIONAL_SHAFTS SPOT_SHAFTS
            #pragma multi_compile _ COLORED_ON
            #pragma multi_compile _ ATTENUATION_CURVE_ON
            #pragma multi_compile _ COOKIE_TEX_ON
            #define LS_PASS_RAYMARCH
            #define LS_INT_INTERP
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                return LS_Raymarch(i);
            }
            ENDCG
        }
    }

    // Fallback for hardware without integer render targets: the driver then uses an RGFloat
    // interpolation texture, read through a sampler.
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex ls_vert_epi
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile DIRECTIONAL_SHAFTS SPOT_SHAFTS
            #pragma multi_compile _ COLORED_ON
            #pragma multi_compile _ ATTENUATION_CURVE_ON
            #pragma multi_compile _ COOKIE_TEX_ON
            #define LS_PASS_RAYMARCH
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                return LS_Raymarch(i);
            }
            ENDCG
        }
    }

    Fallback Off
}
