// From-scratch reimplementation for the port of "Hidden/Amplify Occlusion/Occlusion".
// Pass layout follows the ShaderPass constants of AmplifyOcclusionBase.cs:
//  0 FullDepth, 1-4 FullNormal_*, 5 DeinterleaveDepth1, 6-9 DeinterleaveNormal1_*,
//  10 DeinterleaveDepth4 (4 MRT), 11-14 DeinterleaveNormal4_* (4 MRT),
//  15-18 OcclusionCache Low..VeryHigh (per deinterleaved layer), 19 Reinterleave,
//  20-35 Occlusion{Low,Medium,High,VeryHigh}_{None,Camera,GBuffer,GBufferOcta}
//  (drawn with CommandBuffer.Blit), 36 ApplyDebug, 37 ApplyDeferred (MRT GBuffer0 +
//  emission, multiplied), 38 ApplyDeferredLog, 39 ApplyPostEffect (multiplied),
//  40 ApplyPostEffectLog, 41 CombineDownsampledOcclusionDepth.
// Output of the occlusion passes: r = occlusion (1 = open), g = linear eye depth.
// Shared code is in PortAmplifyOcclusion.cginc.
Shader "Hidden/Amplify Occlusion/Occlusion" {
SubShader {
    Cull Off ZWrite Off ZTest Always

    // 0: FullDepth
    Pass {
        Name "FullDepth"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return AOSceneDepth(i.pos.xy * AOFullTexel()).xxxx;
        }
        ENDCG
    }

    // 1: FullNormal_None
    Pass {
        Name "FullNormal_None"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            float2 suv = i.pos.xy * AOFullTexel();
            float3 P = AOViewPos(suv, AOSceneDepth(suv));
            return float4(AOFetchNormal(AO_NORMALS_NONE, suv, P) * 0.5 + 0.5, 1.0);
        }
        ENDCG
    }

    // 2: FullNormal_Camera
    Pass {
        Name "FullNormal_Camera"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            float2 suv = i.pos.xy * AOFullTexel();
            float3 P = AOViewPos(suv, AOSceneDepth(suv));
            return float4(AOFetchNormal(AO_NORMALS_CAMERA, suv, P) * 0.5 + 0.5, 1.0);
        }
        ENDCG
    }

    // 3: FullNormal_GBuffer
    Pass {
        Name "FullNormal_GBuffer"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            float2 suv = i.pos.xy * AOFullTexel();
            float3 P = AOViewPos(suv, AOSceneDepth(suv));
            return float4(AOFetchNormal(AO_NORMALS_GBUFFER, suv, P) * 0.5 + 0.5, 1.0);
        }
        ENDCG
    }

    // 4: FullNormal_GBufferOcta
    Pass {
        Name "FullNormal_GBufferOcta"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            float2 suv = i.pos.xy * AOFullTexel();
            float3 P = AOViewPos(suv, AOSceneDepth(suv));
            return float4(AOFetchNormal(AO_NORMALS_GBUFFER_OCTA, suv, P) * 0.5 + 0.5, 1.0);
        }
        ENDCG
    }

    // 5: DeinterleaveDepth1
    Pass {
        Name "DeinterleaveDepth1"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return AODeinterleaveDepth(i.pos.xy, _AO_LayerOffset0.xy).xxxx;
        }
        ENDCG
    }

    // 6: DeinterleaveNormal1_None
    Pass {
        Name "DeinterleaveNormal1_None"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_NONE);
        }
        ENDCG
    }

    // 7: DeinterleaveNormal1_Camera
    Pass {
        Name "DeinterleaveNormal1_Camera"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_CAMERA);
        }
        ENDCG
    }

    // 8: DeinterleaveNormal1_GBuffer
    Pass {
        Name "DeinterleaveNormal1_GBuffer"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_GBUFFER);
        }
        ENDCG
    }

    // 9: DeinterleaveNormal1_GBufferOcta
    Pass {
        Name "DeinterleaveNormal1_GBufferOcta"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_GBUFFER_OCTA);
        }
        ENDCG
    }

    // 10: DeinterleaveDepth4
    Pass {
        Name "DeinterleaveDepth4"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        struct AOOut4
        {
            float4 c0 : SV_Target0;
            float4 c1 : SV_Target1;
            float4 c2 : SV_Target2;
            float4 c3 : SV_Target3;
        };
        AOOut4 frag(AOV2F i)
        {
            AOOut4 o;
            o.c0 = AODeinterleaveDepth(i.pos.xy, _AO_LayerOffset0.xy).xxxx;
            o.c1 = AODeinterleaveDepth(i.pos.xy, _AO_LayerOffset1.xy).xxxx;
            o.c2 = AODeinterleaveDepth(i.pos.xy, _AO_LayerOffset2.xy).xxxx;
            o.c3 = AODeinterleaveDepth(i.pos.xy, _AO_LayerOffset3.xy).xxxx;
            return o;
        }
        ENDCG
    }

    // 11: DeinterleaveNormal4_None
    Pass {
        Name "DeinterleaveNormal4_None"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        struct AOOut4
        {
            float4 c0 : SV_Target0;
            float4 c1 : SV_Target1;
            float4 c2 : SV_Target2;
            float4 c3 : SV_Target3;
        };
        AOOut4 frag(AOV2F i)
        {
            AOOut4 o;
            o.c0 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_NONE);
            o.c1 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset1.xy, AO_NORMALS_NONE);
            o.c2 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset2.xy, AO_NORMALS_NONE);
            o.c3 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset3.xy, AO_NORMALS_NONE);
            return o;
        }
        ENDCG
    }

    // 12: DeinterleaveNormal4_Camera
    Pass {
        Name "DeinterleaveNormal4_Camera"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        struct AOOut4
        {
            float4 c0 : SV_Target0;
            float4 c1 : SV_Target1;
            float4 c2 : SV_Target2;
            float4 c3 : SV_Target3;
        };
        AOOut4 frag(AOV2F i)
        {
            AOOut4 o;
            o.c0 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_CAMERA);
            o.c1 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset1.xy, AO_NORMALS_CAMERA);
            o.c2 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset2.xy, AO_NORMALS_CAMERA);
            o.c3 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset3.xy, AO_NORMALS_CAMERA);
            return o;
        }
        ENDCG
    }

    // 13: DeinterleaveNormal4_GBuffer
    Pass {
        Name "DeinterleaveNormal4_GBuffer"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        struct AOOut4
        {
            float4 c0 : SV_Target0;
            float4 c1 : SV_Target1;
            float4 c2 : SV_Target2;
            float4 c3 : SV_Target3;
        };
        AOOut4 frag(AOV2F i)
        {
            AOOut4 o;
            o.c0 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_GBUFFER);
            o.c1 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset1.xy, AO_NORMALS_GBUFFER);
            o.c2 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset2.xy, AO_NORMALS_GBUFFER);
            o.c3 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset3.xy, AO_NORMALS_GBUFFER);
            return o;
        }
        ENDCG
    }

    // 14: DeinterleaveNormal4_GBufferOcta
    Pass {
        Name "DeinterleaveNormal4_GBufferOcta"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        struct AOOut4
        {
            float4 c0 : SV_Target0;
            float4 c1 : SV_Target1;
            float4 c2 : SV_Target2;
            float4 c3 : SV_Target3;
        };
        AOOut4 frag(AOV2F i)
        {
            AOOut4 o;
            o.c0 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset0.xy, AO_NORMALS_GBUFFER_OCTA);
            o.c1 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset1.xy, AO_NORMALS_GBUFFER_OCTA);
            o.c2 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset2.xy, AO_NORMALS_GBUFFER_OCTA);
            o.c3 = AODeinterleaveNormal(i.pos.xy, _AO_LayerOffset3.xy, AO_NORMALS_GBUFFER_OCTA);
            return o;
        }
        ENDCG
    }

    // 15: OcclusionCache_Low
    Pass {
        Name "OcclusionCache_Low"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionLayer(i, 4, 4), 0.0, 0.0);
        }
        ENDCG
    }

    // 16: OcclusionCache_Medium
    Pass {
        Name "OcclusionCache_Medium"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionLayer(i, 8, 4), 0.0, 0.0);
        }
        ENDCG
    }

    // 17: OcclusionCache_High
    Pass {
        Name "OcclusionCache_High"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionLayer(i, 8, 6), 0.0, 0.0);
        }
        ENDCG
    }

    // 18: OcclusionCache_VeryHigh
    Pass {
        Name "OcclusionCache_VeryHigh"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionLayer(i, 8, 8), 0.0, 0.0);
        }
        ENDCG
    }

    // 19: Reinterleave
    Pass {
        Name "Reinterleave"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOReinterleave(i.pos.xy), 0.0, 0.0);
        }
        ENDCG
    }

    // 20: OcclusionLow_None
    Pass {
        Name "OcclusionLow_None"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 4, 4, AO_NORMALS_NONE), 0.0, 0.0);
        }
        ENDCG
    }

    // 21: OcclusionLow_Camera
    Pass {
        Name "OcclusionLow_Camera"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 4, 4, AO_NORMALS_CAMERA), 0.0, 0.0);
        }
        ENDCG
    }

    // 22: OcclusionLow_GBuffer
    Pass {
        Name "OcclusionLow_GBuffer"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 4, 4, AO_NORMALS_GBUFFER), 0.0, 0.0);
        }
        ENDCG
    }

    // 23: OcclusionLow_GBufferOcta
    Pass {
        Name "OcclusionLow_GBufferOcta"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 4, 4, AO_NORMALS_GBUFFER_OCTA), 0.0, 0.0);
        }
        ENDCG
    }

    // 24: OcclusionMedium_None
    Pass {
        Name "OcclusionMedium_None"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 4, AO_NORMALS_NONE), 0.0, 0.0);
        }
        ENDCG
    }

    // 25: OcclusionMedium_Camera
    Pass {
        Name "OcclusionMedium_Camera"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 4, AO_NORMALS_CAMERA), 0.0, 0.0);
        }
        ENDCG
    }

    // 26: OcclusionMedium_GBuffer
    Pass {
        Name "OcclusionMedium_GBuffer"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 4, AO_NORMALS_GBUFFER), 0.0, 0.0);
        }
        ENDCG
    }

    // 27: OcclusionMedium_GBufferOcta
    Pass {
        Name "OcclusionMedium_GBufferOcta"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 4, AO_NORMALS_GBUFFER_OCTA), 0.0, 0.0);
        }
        ENDCG
    }

    // 28: OcclusionHigh_None
    Pass {
        Name "OcclusionHigh_None"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 6, AO_NORMALS_NONE), 0.0, 0.0);
        }
        ENDCG
    }

    // 29: OcclusionHigh_Camera
    Pass {
        Name "OcclusionHigh_Camera"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 6, AO_NORMALS_CAMERA), 0.0, 0.0);
        }
        ENDCG
    }

    // 30: OcclusionHigh_GBuffer
    Pass {
        Name "OcclusionHigh_GBuffer"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 6, AO_NORMALS_GBUFFER), 0.0, 0.0);
        }
        ENDCG
    }

    // 31: OcclusionHigh_GBufferOcta
    Pass {
        Name "OcclusionHigh_GBufferOcta"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 6, AO_NORMALS_GBUFFER_OCTA), 0.0, 0.0);
        }
        ENDCG
    }

    // 32: OcclusionVeryHigh_None
    Pass {
        Name "OcclusionVeryHigh_None"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 8, AO_NORMALS_NONE), 0.0, 0.0);
        }
        ENDCG
    }

    // 33: OcclusionVeryHigh_Camera
    Pass {
        Name "OcclusionVeryHigh_Camera"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 8, AO_NORMALS_CAMERA), 0.0, 0.0);
        }
        ENDCG
    }

    // 34: OcclusionVeryHigh_GBuffer
    Pass {
        Name "OcclusionVeryHigh_GBuffer"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 8, AO_NORMALS_GBUFFER), 0.0, 0.0);
        }
        ENDCG
    }

    // 35: OcclusionVeryHigh_GBufferOcta
    Pass {
        Name "OcclusionVeryHigh_GBufferOcta"
        CGPROGRAM
        #pragma vertex AOVertBlit
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOOcclusionFull(i, 8, 8, AO_NORMALS_GBUFFER_OCTA), 0.0, 0.0);
        }
        ENDCG
    }

    // 36: ApplyDebug
    Pass {
        Name "ApplyDebug"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOTint(AOApplyScalar(i.pos.xy)), 1.0);
        }
        ENDCG
    }

    // 37: ApplyDeferred
    Pass {
        Name "ApplyDeferred"
        Blend DstColor Zero
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        struct AOOut2
        {
            float4 albedo : SV_Target0;
            float4 emission : SV_Target1;
        };
        AOOut2 frag(AOV2F i)
        {
            float occ = AOApplyScalar(i.pos.xy);
            AOOut2 o;
            o.albedo = float4(1.0, 1.0, 1.0, occ);
            o.emission = float4(AOTint(occ), 1.0);
            return o;
        }
        ENDCG
    }

    // 38: ApplyDeferredLog
    Pass {
        Name "ApplyDeferredLog"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        struct AOOut2
        {
            float4 albedo : SV_Target0;
            float4 emission : SV_Target1;
        };
        AOOut2 frag(AOV2F i)
        {
            float occ = AOApplyScalar(i.pos.xy);
            float2 tuv = i.pos.xy * _AO_Target_TexelSize.xy;
            float4 albedo = AOTex(_AO_GBufferAlbedo, tuv);
            float4 emission = AOTex(_AO_GBufferEmission, tuv);
            AOOut2 o;
            o.albedo = float4(albedo.rgb, albedo.a * occ);
            // LDR lighting buffer holds exp2(-light): scaling light by occ is a power.
            o.emission = float4(pow(max(emission.rgb, 1e-5), AOTint(occ)), emission.a);
            return o;
        }
        ENDCG
    }

    // 39: ApplyPostEffect
    Pass {
        Name "ApplyPostEffect"
        Blend DstColor Zero
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOTint(AOApplyScalar(i.pos.xy)), 1.0);
        }
        ENDCG
    }

    // 40: ApplyPostEffectLog
    Pass {
        Name "ApplyPostEffectLog"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            float occ = AOApplyScalar(i.pos.xy);
            float4 emission = AOTex(_AO_GBufferEmission, i.pos.xy * _AO_Target_TexelSize.xy);
            return float4(pow(max(emission.rgb, 1e-5), AOTint(occ)), emission.a);
        }
        ENDCG
    }

    // 41: CombineDownsampledOcclusionDepth
    Pass {
        Name "CombineDownsampledOcclusionDepth"
        CGPROGRAM
        #pragma vertex AOVertQuad
        #pragma fragment frag
        #pragma target 3.5
        #include "../PortAmplifyOcclusion.cginc"
        float4 frag(AOV2F i) : SV_Target
        {
            return float4(AOCombineDownsampled(i.pos.xy), 0.0, 0.0);
        }
        ENDCG
    }
}
Fallback Off
}
