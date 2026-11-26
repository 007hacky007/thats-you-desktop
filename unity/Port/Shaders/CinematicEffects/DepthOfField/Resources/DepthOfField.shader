// From-scratch reimplementation for the port: "Hidden/DepthOfField/DepthOfField".
// Cinematic depth of field driven by UnityStandardAssets.CinematicEffects.DepthOfField.
// Written for this port from the driving script's pass layout and uniforms;
// not derived from any original shader source. Shared code lives in
// ../PortCinematicDof.cginc.
//
// Pass order matches DepthOfField.Passes (0..28):
//  0 BlurAlphaWeighted   1 BoxBlur   2 DilateFgCocFromColor   3 DilateFgCoc
//  4 CaptureCoc   5 CaptureCocExplicit   6 VisualizeCoc   7 VisualizeCocExplicit
//  8 CocPrefilter   9 CircleBlur   10 CircleBlurWithDilatedFg
//  11 CircleBlurLowQuality   12 CircleBlurLowQualityWithDilatedFg
//  13 Merge   14 MergeExplicit   15 MergeBicubic   16 MergeExplicitBicubic
//  17-20 Shape low quality (blur, +dilated fg, merge, merge +dilated fg)
//  21-24 Shape medium quality, 25-28 Shape high quality (same order)
Shader "Hidden/DepthOfField/DepthOfField" {
Properties {
 _MainTex ("-", 2D) = "black" { }
 _SecondTex ("-", 2D) = "black" { }
 _ThirdTex ("-", 2D) = "black" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #pragma target 3.5
    #include "../PortCinematicDof.cginc"
    ENDCG

    // 0 BlurAlphaWeighted
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofBlurAlphaWeighted(i.uv); }
        ENDCG
    }

    // 1 BoxBlur
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofBoxBlur(i.uv); }
        ENDCG
    }

    // 2 DilateFgCocFromColor
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { float d = PortDofDilate(i.uv, true); return float4(d, d, 0.0, 0.0); }
        ENDCG
    }

    // 3 DilateFgCoc
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { float d = PortDofDilate(i.uv, false); return float4(d, d, 0.0, 0.0); }
        ENDCG
    }

    // 4 CaptureCoc
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        #pragma shader_feature USE_SPECIAL_FETCH_FOR_COC
        float4 frag(PortDofV2f i) : SV_Target { return PortDofCapture(i, false); }
        ENDCG
    }

    // 5 CaptureCocExplicit
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        #pragma shader_feature USE_SPECIAL_FETCH_FOR_COC
        float4 frag(PortDofV2f i) : SV_Target { return PortDofCapture(i, true); }
        ENDCG
    }

    // 6 VisualizeCoc
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofVisualize(i, false); }
        ENDCG
    }

    // 7 VisualizeCocExplicit
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofVisualize(i, true); }
        ENDCG
    }

    // 8 CocPrefilter
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofPrefilter(i.uv); }
        ENDCG
    }

    // 9 CircleBlur
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofCircle(i, 4, false); }
        ENDCG
    }

    // 10 CircleBlurWithDilatedFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofCircle(i, 4, true); }
        ENDCG
    }

    // 11 CircleBlurLowQuality
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofCircle(i, 3, false); }
        ENDCG
    }

    // 12 CircleBlurLowQualityWithDilatedFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofCircle(i, 3, true); }
        ENDCG
    }

    // 13 Merge
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofMerge(i, false, false); }
        ENDCG
    }

    // 14 MergeExplicit
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofMerge(i, true, false); }
        ENDCG
    }

    // 15 MergeBicubic
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofMerge(i, false, true); }
        ENDCG
    }

    // 16 MergeExplicitBicubic
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofMerge(i, true, true); }
        ENDCG
    }

    // 17 ShapeLowQuality
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLine(i, 4, false); }
        ENDCG
    }

    // 18 ShapeLowQualityDilateFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLine(i, 4, true); }
        ENDCG
    }

    // 19 ShapeLowQualityMerge
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLineMerge(i, 4, false); }
        ENDCG
    }

    // 20 ShapeLowQualityMergeDilateFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLineMerge(i, 4, true); }
        ENDCG
    }

    // 21 ShapeMediumQuality
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLine(i, 8, false); }
        ENDCG
    }

    // 22 ShapeMediumQualityDilateFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLine(i, 8, true); }
        ENDCG
    }

    // 23 ShapeMediumQualityMerge
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLineMerge(i, 8, false); }
        ENDCG
    }

    // 24 ShapeMediumQualityMergeDilateFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLineMerge(i, 8, true); }
        ENDCG
    }

    // 25 ShapeHighQuality
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLine(i, 12, false); }
        ENDCG
    }

    // 26 ShapeHighQualityDilateFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLine(i, 12, true); }
        ENDCG
    }

    // 27 ShapeHighQualityMerge
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLineMerge(i, 12, false); }
        ENDCG
    }

    // 28 ShapeHighQualityMergeDilateFg
    Pass {
        CGPROGRAM
        #pragma vertex PortDofVert
        #pragma fragment frag
        float4 frag(PortDofV2f i) : SV_Target { return PortDofLineMerge(i, 12, true); }
        ENDCG
    }
}
Fallback Off
}
