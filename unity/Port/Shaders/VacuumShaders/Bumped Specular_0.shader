// From-scratch reimplementation for the port.
// Hidden/dynlightmap/VacuumShaders/Subsurface Scattering/Bumped Specular (dynamic lightmap variant, same shading): Blinn-Phong lit surface with translucency, optional rim and emission.
// All shading lives in PortSSS.cginc; this file only declares the properties the materials use.
Shader "Hidden/dynlightmap/VacuumShaders/Subsurface Scattering/Bumped Specular" {
    Properties {
        [HideInInspector] _V_MaterialTag ("", Float) = 0
        [HideInInspector] _V_MaterialTitle_Default ("", Float) = 0
        _Color ("Main Color", Color) = (1,1,1,1)
        _SpecColor ("Specular Color", Color) = (0.5,0.5,0.5,1)
        _Shininess ("Shininess", Range(0.03,1)) = 0.078125
        _MainTex ("Base (RGB) Gloss (A)", 2D) = "white" {}
        _BumpSize ("Bump Size", Float) = 1
        _BumpMap ("Normalmap", 2D) = "bump" {}
        [HideInInspector] _V_MaterialTitle_SSS ("", Float) = 0
        _TransMap ("Translucency Map", 2D) = "white" {}
        _TransColor ("Translucency Color", Color) = (1,1,1,1)
        _TransDistortion ("Translucency Distortion", Range(0,0.5)) = 0.1
        _TransPower ("Translucency Power", Range(1,16)) = 1
        _TransScale ("Translucency Scale", Float) = 1
        _TransBackfaceIntensity ("Backface Intensity", Float) = 0.15
        _TransDirectianalLightStrength ("Directional Light Strength", Range(0,1)) = 0.2
        _TransOtherLightsStrength ("Other Lights Strength", Range(0,1)) = 0.5
        _V_SSS_Emission ("Emission", Float) = 0
        _V_SSS_Rim_Color ("Rim Color", Color) = (1,1,1,1)
        _V_SSS_Rim_Pow ("Rim Power", Range(0.5,8)) = 2
    }

    SubShader {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #define PORT_SSS_SPECULAR 1
        #pragma surface PortSSSSurf PortSSS fullforwardshadows
        #pragma target 3.0
        #pragma shader_feature_local V_SSS_ADVANCED_TRANSLUSENCY_ON
        #pragma shader_feature_local V_SSS_RIM_ON
        #include "PortSSS.cginc"
        ENDCG
    }

    Fallback "Legacy Shaders/Bumped Specular"
}
