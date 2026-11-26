// From-scratch reimplementation for the port: "Hidden/ScreenSpaceReflection".
// Driven by UnityStandardAssets.CinematicEffects.ScreenSpaceReflection; the pass
// order matches its PassIndex enum (0-4 ray trace with 1/2/4/8/16 pixel strides,
// 5 final composite, 6 blur, 7 SSR resolve, 8 blit, 9 edges, 10 min mip,
// 11 hit points to colour, 12 normal/roughness pack, 13 depth as CSZ,
// 14 temporal filter, 15 average ray distance, 16 poisson blur).
// Only runs on deferred cameras; reads _CameraDepthTexture and GBuffer1/2.
Shader "Hidden/ScreenSpaceReflection" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "../PortCinematicSsr.cginc"
    ENDCG

    // 0: RayTraceStep1
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragTrace1
        #pragma target 3.0
        ENDCG
    }

    // 1: RayTraceStep2
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragTrace2
        #pragma target 3.0
        ENDCG
    }

    // 2: RayTraceStep4
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragTrace4
        #pragma target 3.0
        ENDCG
    }

    // 3: RayTraceStep8
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragTrace8
        #pragma target 3.0
        ENDCG
    }

    // 4: RayTraceStep16
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragTrace16
        #pragma target 3.0
        ENDCG
    }

    // 5: CompositeFinal
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragCompositeFinal
        #pragma target 3.0
        ENDCG
    }

    // 6: Blur
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragBlur
        #pragma target 3.0
        ENDCG
    }

    // 7: CompositeSSR
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragCompositeSSR
        #pragma target 3.0
        ENDCG
    }

    // 8: Blit
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragBlit
        #pragma target 3.0
        ENDCG
    }

    // 9: EdgeGeneration
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragEdgeGeneration
        #pragma target 3.0
        ENDCG
    }

    // 10: MinMipGeneration
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragMinMip
        #pragma target 3.0
        ENDCG
    }

    // 11: HitPointToReflections
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragHitPointToReflections
        #pragma target 3.0
        ENDCG
    }

    // 12: BilateralKeyPack
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragBilateralKeyPack
        #pragma target 3.0
        ENDCG
    }

    // 13: BlitDepthAsCSZ
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragBlitDepthAsCSZ
        #pragma target 3.0
        ENDCG
    }

    // 14: TemporalFilter
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragTemporalFilter
        #pragma target 3.0
        ENDCG
    }

    // 15: AverageRayDistanceGeneration
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragAverageRayDistance
        #pragma target 3.0
        ENDCG
    }

    // 16: PoissonBlur
    Pass {
        CGPROGRAM
        #pragma vertex SsrVert
        #pragma fragment SsrFragPoissonBlur
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
