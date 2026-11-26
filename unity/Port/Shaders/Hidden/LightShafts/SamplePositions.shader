// From-scratch reimplementation for the port of the light shaft "Sample Positions" debug
// pass. On hardware with random write support it marks the screen position of the epipolar
// samples in the texture bound with Graphics.SetRandomWriteTarget(1, ...); the colour target
// is left untouched.
Shader "Hidden/Sample Positions"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            ColorMask 0

            CGPROGRAM
            #pragma vertex ls_vert_epi
            #pragma fragment frag
            #pragma target 4.5
            #define LS_PASS_SAMPLE_POSITIONS
            #define LS_INT_INTERP
            #define LS_RANDOM_WRITE
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                return LS_SamplePositions(i);
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
            ColorMask 0

            CGPROGRAM
            #pragma vertex ls_vert_epi
            #pragma fragment frag
            #pragma target 3.0
            #define LS_PASS_SAMPLE_POSITIONS
            #include "PortLightShafts.cginc"

            float4 frag(ls_v2f i) : SV_Target
            {
                return LS_SamplePositions(i);
            }
            ENDCG
        }
    }

    Fallback Off
}
