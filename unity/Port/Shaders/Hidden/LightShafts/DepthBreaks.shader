// From-scratch reimplementation for the port of the light shaft "DepthBreaks" pass.
// For every epipolar sample writes the column indices of the nearest raymarched samples on
// its left and right as whole numbers in a float (RGFloat) texture. Raymarched samples are
// placed every _InterpolationStep samples and on both sides of every depth discontinuity.
Shader "Hidden/DepthBreaks"
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
            #define LS_PASS_DEPTHBREAKS
            #define LS_INT_INTERP
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                float2 lr = LS_DepthBreaks(LS_SelfTexel(i, _DepthEpiTexDim));
                return float4(floor(lr + 0.5), 0.0, 0.0);
            }
            ENDCG
        }
    }

    // Fallback for SM3.0 hardware: same output, texels addressed through a sampler.
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
            #define LS_PASS_DEPTHBREAKS
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                float2 lr = LS_DepthBreaks(LS_SelfTexel(i, _DepthEpiTexDim));
                return float4(floor(lr + 0.5), 0.0, 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
