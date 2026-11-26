// From-scratch reimplementation for the port of "Hidden/Amplify Occlusion/Blur".
// Separable depth aware blur of the occlusion buffer (r occlusion, g linear
// depth), drawn with the blit mesh from AmplifyOcclusionBase.cs. Passes are
// horizontal / vertical pairs for radius 1, 2, 4 and 8 texels; neighbours are
// weighted by _AO_BlurDepthThreshold (depth similarity) and _AO_BlurFalloff
// (spatial falloff, 0 = flat kernel).
Shader "Hidden/Amplify Occlusion/Blur" {
SubShader {
    Cull Off ZWrite Off ZTest Always

    // 0: BlurHorizontal1
    Pass {
        Name "BlurHorizontal1"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(1.0, 0.0), 1), 0.0, 0.0);
        }
        ENDCG
    }

    // 1: BlurVertical1
    Pass {
        Name "BlurVertical1"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(0.0, 1.0), 1), 0.0, 0.0);
        }
        ENDCG
    }

    // 2: BlurHorizontal2
    Pass {
        Name "BlurHorizontal2"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(1.0, 0.0), 2), 0.0, 0.0);
        }
        ENDCG
    }

    // 3: BlurVertical2
    Pass {
        Name "BlurVertical2"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(0.0, 1.0), 2), 0.0, 0.0);
        }
        ENDCG
    }

    // 4: BlurHorizontal4
    Pass {
        Name "BlurHorizontal4"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(1.0, 0.0), 4), 0.0, 0.0);
        }
        ENDCG
    }

    // 5: BlurVertical4
    Pass {
        Name "BlurVertical4"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(0.0, 1.0), 4), 0.0, 0.0);
        }
        ENDCG
    }

    // 6: BlurHorizontal8
    Pass {
        Name "BlurHorizontal8"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(1.0, 0.0), 8), 0.0, 0.0);
        }
        ENDCG
    }

    // 7: BlurVertical8
    Pass {
        Name "BlurVertical8"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOBlur(i.pos.xy, float2(0.0, 1.0), 8), 0.0, 0.0);
        }
        ENDCG
    }
}
Fallback Off
}
