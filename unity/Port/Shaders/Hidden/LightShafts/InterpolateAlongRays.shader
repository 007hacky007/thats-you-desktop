// From-scratch reimplementation for the port of the light shaft "InterpolateAlongRays"
// pass. Fills every epipolar sample by linear interpolation between the raymarched samples
// recorded for it in the interpolation texture.
Shader "Hidden/InterpolateAlongRays"
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
            #define LS_PASS_INTERPOLATE_ALONG_RAYS
            #define LS_INT_INTERP
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                return LS_InterpolateAlongRays(i);
            }
            ENDCG
        }
    }

    // Fallback for SM3.0 hardware: the RGFloat interpolation texture is read through a
    // point-sampled sampler instead of Load.
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
            #define LS_PASS_INTERPOLATE_ALONG_RAYS
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                return LS_InterpolateAlongRays(i);
            }
            ENDCG
        }
    }

    Fallback Off
}
