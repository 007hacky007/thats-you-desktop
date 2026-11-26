// From-scratch reimplementation for the port.
// Sonic Ether/Particles/AdditiveUnderlay: the same additive look drawn in an earlier transparent queue so it sits under
// other transparent effects (beer bubbles, smoke in the air).
// Vertex colour * _TintColor * texture * 2, blended SrcAlpha One with no depth write, soft
// particles via _InvFade and the camera depth texture, _EmissionGain boosts HDR output for bloom.
Shader "Sonic Ether/Particles/AdditiveUnderlay" {
    Properties {
        _TintColor ("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        _MainTex ("Particle Texture", 2D) = "white" {}
        _InvFade ("Soft Particles Factor", Range(0.01, 3)) = 1
        _EmissionGain ("Emission Gain", Range(0, 1)) = 0.3
    }

    Category {
        Tags { "Queue" = "Transparent-5" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Blend SrcAlpha One
        ColorMask RGB
        Cull Off
        Lighting Off
        ZWrite Off

        SubShader {
            Pass {
                CGPROGRAM
                #pragma vertex PortSEVert
                #pragma fragment PortSEFrag
                #pragma target 3.0
                #pragma multi_compile_particles
                #pragma multi_compile_fog
                #define PORT_SE_ADDITIVE_PASS 1
                #include "PortParticles.cginc"
                ENDCG
            }
        }
    }

    Fallback "Legacy Shaders/Particles/Additive"
}
