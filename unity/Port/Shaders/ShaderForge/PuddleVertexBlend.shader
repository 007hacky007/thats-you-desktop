// From-scratch reimplementation for the port.
// Rainy floor with puddles painted in the red vertex colour. The dry floor uses the Primary
// textures (Metallic: R metallic, A smoothness) with parallax from the Height map
// (ParrallaxScale). Puddles fill the low parts of the height map first (HeightScale sharpens
// the water line), the floor under the water is tinted by PuddleTint and the surface turns
// into smooth water with raindrop ripples from a FramesX x FramesY_ flipbook (AnimatedNormal,
// tiled by TilingOffset, AnimSpeed frames per second, RippleScale strength) and two slowly
// counter-scrolling wave normals (Secondary_Normal, Secondary_Normal_02). A darker wet band
// (PuddleEdgeTint, width from PuddleEdgeThreshold) surrounds each puddle.
Shader "Shader Forge/PuddleVertexBlend" {
    Properties {
        _PrimaryAlbedo ("PrimaryAlbedo", 2D) = "white" {}
        _PrimaryMetallic ("PrimaryMetallic", 2D) = "white" {}
        _PrimaryNormal ("PrimaryNormal", 2D) = "bump" {}
        _HeightScale ("HeightScale", Float) = 2
        _PuddleEdgeTint ("PuddleEdgeTint", Color) = (0.5,0.5,0.5,1)
        _PuddleEdgeThreshold ("PuddleEdgeThreshold", Float) = 3
        _PuddleTint ("PuddleTint", Color) = (1,1,1,1)
        _Height ("Height", 2D) = "white" {}
        _ParrallaxScale ("ParrallaxScale", Float) = 0.15
        _AnimatedNormal ("AnimatedNormal", 2D) = "bump" {}
        _SecondarySpeed ("SecondarySpeed", Range(0,0.01)) = 0.001
        _SecondaryNormalScale ("SecondaryNormalScale", Range(0,1)) = 0.25
        _AnimSpeed ("AnimSpeed", Float) = 1
        _IGNORETHISBUG ("IGNORETHIS(BUG)!!!", 2D) = "bump" {}
        _FramesX ("FramesX", Float) = 4
        _FramesY_ ("FramesY_", Float) = 4
        _TilingOffset ("TilingOffset", Vector) = (1,1,0,0)
        _RippleScale ("RippleScale", Range(0,1)) = 0
        _Secondary_Normal ("Secondary_Normal", 2D) = "bump" {}
        _Secondary_Normal_02 ("Secondary_Normal_02", 2D) = "bump" {}
    }

    SubShader {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #include "PortSFGlassWet.cginc"

        sampler2D _PrimaryAlbedo;       float4 _PrimaryAlbedo_ST;
        sampler2D _PrimaryMetallic;     float4 _PrimaryMetallic_ST;
        sampler2D _PrimaryNormal;       float4 _PrimaryNormal_ST;
        sampler2D _Height;              float4 _Height_ST;
        sampler2D _AnimatedNormal;
        sampler2D _Secondary_Normal;    float4 _Secondary_Normal_ST;
        sampler2D _Secondary_Normal_02; float4 _Secondary_Normal_02_ST;
        half _HeightScale;
        half4 _PuddleEdgeTint;
        half _PuddleEdgeThreshold;
        half4 _PuddleTint;
        float _ParrallaxScale;
        float _SecondarySpeed;
        half _SecondaryNormalScale;
        float _AnimSpeed;
        float _FramesX;
        float _FramesY_;
        float4 _TilingOffset;
        half _RippleScale;

        struct Input {
            float2 baseUV;
            float3 viewDir;
            float4 color : COLOR;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.baseUV = v.texcoord.xy;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.baseUV;
            half mask = saturate(IN.color.r);

            // Water level against the floor height (sampled before parallax: the water line is
            // where the water surface is, not where the floor below appears).
            half h = tex2D(_Height, TRANSFORM_TEX(uv, _Height)).r;
            half puddle = PortSF_HeightBlend(mask, h, _HeightScale);
            // The same blend pushed outwards gives the wet band around the water.
            half level = (mask * 1.5h - h * 0.5h - 0.5h) * _HeightScale + 0.5h;
            half wetZone = saturate(level + 0.25h * _PuddleEdgeThreshold);
            half edgeBand = saturate(wetZone - puddle);

            // Parallax on the dry floor; flattened under the water.
            float2 offset = ParallaxOffset(h, _ParrallaxScale * (1.0h - puddle), normalize(IN.viewDir));
            float2 uvAlb = TRANSFORM_TEX(uv, _PrimaryAlbedo) + offset;
            float2 uvMet = TRANSFORM_TEX(uv, _PrimaryMetallic) + offset;
            float2 uvNrm = TRANSFORM_TEX(uv, _PrimaryNormal) + offset;

            half3 albedo = tex2D(_PrimaryAlbedo, uvAlb).rgb;
            half4 ms = tex2D(_PrimaryMetallic, uvMet);
            half3 floorN = UnpackNormal(tex2D(_PrimaryNormal, uvNrm));

            // Raindrop ripples: flipbook sampled with the gradients of the continuous uv so
            // the cell wrap does not pick the smallest mip.
            float2 rippleUV = uv * _TilingOffset.xy + _TilingOffset.zw;
            float frame = _Time.y * _AnimSpeed;
            float2 cellUV = PortSF_FlipbookUV(rippleUV, frame, _FramesX, _FramesY_);
            float2 cellScale = 1.0 / float2(max(_FramesX, 1.0), max(_FramesY_, 1.0));
            half4 rippleSample = tex2Dgrad(_AnimatedNormal, cellUV, ddx(rippleUV) * cellScale, ddy(rippleUV) * cellScale);
            half3 ripples = PortSF_UnpackNormalStrength(rippleSample, _RippleScale);

            float drift = _Time.y * _SecondarySpeed;
            half3 waveA = PortSF_UnpackNormalStrength(
                tex2D(_Secondary_Normal, TRANSFORM_TEX(uv, _Secondary_Normal) + float2(drift, drift * 0.7)), _SecondaryNormalScale);
            half3 waveB = PortSF_UnpackNormalStrength(
                tex2D(_Secondary_Normal_02, TRANSFORM_TEX(uv, _Secondary_Normal_02) * 0.83 - float2(drift * 0.8, drift)), _SecondaryNormalScale);
            half3 waterN = PortSF_BlendNormals(ripples, PortSF_BlendNormals(waveA, waveB));

            half3 wetAlbedo = albedo * lerp(half3(1, 1, 1), _PuddleEdgeTint.rgb, 0.5h);
            half3 dryOrWet = lerp(albedo, wetAlbedo, edgeBand);
            o.Albedo = lerp(dryOrWet, albedo * _PuddleTint.rgb, puddle);
            o.Metallic = ms.r * (1.0h - puddle);
            half drySmooth = ms.a;
            half wetSmooth = lerp(drySmooth, max(drySmooth, 0.75h), edgeBand);
            o.Smoothness = lerp(wetSmooth, 0.96h, puddle);
            o.Normal = normalize(lerp(floorN, waterN, puddle));
            o.Occlusion = 1.0h;
            o.Alpha = 1.0h;
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Diffuse"
}
