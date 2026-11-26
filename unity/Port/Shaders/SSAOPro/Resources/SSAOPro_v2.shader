// From-scratch reimplementation for the port.
// Hidden/SSAO Pro V2: screen space ambient occlusion driven by SSAOPro.cs ([ImageEffectOpaque]).
// V2 (AOMode.V12): world space reconstruction from inverse(projection * view) and cameraToWorld.
// Pass order must match the script: 0 clear, 1-4 occlusion variants, 5 gaussian blur,
// 6 bilateral blur, 7 composite. See PortSSAOPro.cginc.
Shader "Hidden/SSAO Pro V2" {
    Properties {
        _MainTex ("", 2D) = "white" {}
    }

    SubShader {
        ZTest Always
        Cull Off
        ZWrite Off

        // 0: clear to white (no occlusion)
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragClear
                #pragma target 3.0
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
        // 1: occlusion
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragAO
                #pragma target 3.0
                #pragma multi_compile HIGH_PRECISION_DEPTHMAP_OFF HIGH_PRECISION_DEPTHMAP_ON
                #pragma multi_compile SAMPLES_VERY_LOW SAMPLES_LOW SAMPLES_MEDIUM SAMPLES_HIGH SAMPLES_ULTRA
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
        // 2: occlusion, noise rotated kernel
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragAONoise
                #pragma target 3.0
                #pragma multi_compile HIGH_PRECISION_DEPTHMAP_OFF HIGH_PRECISION_DEPTHMAP_ON
                #pragma multi_compile SAMPLES_VERY_LOW SAMPLES_LOW SAMPLES_MEDIUM SAMPLES_HIGH SAMPLES_ULTRA
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
        // 3: occlusion, luminance contribution
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragAOLum
                #pragma target 3.0
                #pragma multi_compile HIGH_PRECISION_DEPTHMAP_OFF HIGH_PRECISION_DEPTHMAP_ON
                #pragma multi_compile SAMPLES_VERY_LOW SAMPLES_LOW SAMPLES_MEDIUM SAMPLES_HIGH SAMPLES_ULTRA
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
        // 4: occlusion, noise + luminance contribution
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragAONoiseLum
                #pragma target 3.0
                #pragma multi_compile HIGH_PRECISION_DEPTHMAP_OFF HIGH_PRECISION_DEPTHMAP_ON
                #pragma multi_compile SAMPLES_VERY_LOW SAMPLES_LOW SAMPLES_MEDIUM SAMPLES_HIGH SAMPLES_ULTRA
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
        // 5: gaussian blur along _Direction
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragGaussian
                #pragma target 3.0
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
        // 6: depth/normal aware blur along _Direction
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragBilateral
                #pragma target 3.0
                #pragma multi_compile HIGH_PRECISION_DEPTHMAP_OFF HIGH_PRECISION_DEPTHMAP_ON
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
        // 7: composite: source * lerp(_OcclusionColor, 1, ao)
        Pass {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment PortSSAOFragComposite
                #pragma target 3.0
                #define PORT_SSAO_V2 1
                #include "../PortSSAOPro.cginc"
            ENDCG
        }
    }

    Fallback Off
}
