// From-scratch reimplementation for the port of the light shaft "Final Interpolation" pass.
// Reconstructs the shafts per screen pixel from the epipolar results and adds them to the
// camera target. Drawn as a full screen quad (ZTest Always) or as the spot light frustum
// mesh (ZTest Less), selected by the driver through _ZTest.
Shader "Hidden/Final Interpolation"
{
    Properties
    {
        _ZTest ("", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Blend One One
            ColorMask RGB
            ZWrite Off
            ZTest [_ZTest]
            Cull Back

            CGPROGRAM
            #pragma vertex LS_FinalVert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile QUAD_SHAFTS FRUSTUM_SHAFTS
            #pragma multi_compile _ SHOW_SAMPLES_ON
            #define LS_PASS_FINAL
            #define LS_INT_INTERP
            #include "PortLightShafts.cginc"

            float4 frag(ls_final_v2f i) : SV_Target
            {
                return LS_Final(i);
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
            Blend One One
            ColorMask RGB
            ZWrite Off
            ZTest [_ZTest]
            Cull Back

            CGPROGRAM
            #pragma vertex LS_FinalVert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile QUAD_SHAFTS FRUSTUM_SHAFTS
            #pragma multi_compile _ SHOW_SAMPLES_ON
            #define LS_PASS_FINAL
            #include "PortLightShafts.cginc"

            float4 frag(ls_final_v2f i) : SV_Target
            {
                return LS_Final(i);
            }
            ENDCG
        }
    }

    Fallback Off
}
