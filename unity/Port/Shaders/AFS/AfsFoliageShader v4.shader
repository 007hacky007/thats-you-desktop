// From-scratch reimplementation for the port.
// AFS/Foliage Shader v4: alpha-tested, double-sided foliage bent by the scene wind.
// All shading and bending lives in PortAfs.cginc; this file only declares the properties the materials use.
Shader "AFS/Foliage Shader v4" {
    Properties {
        _MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.3
        _BumpTransSpecMap ("Normal (GA) Trans(R) Smoothness(B)", 2D) = "bump" {}
        _SpecularReflectivity ("Specular Reflectivity", Color) = (0.2,0.2,0.2,1)
        _TranslucencyColor ("Translucency Color", Color) = (0.73,0.85,0.41,1)
        _TranslucencyViewDependency ("Translucency View Dependency", Range(0,1)) = 0.5
        _LeafTurbulence ("Leaf Turbulence", Range(0,1)) = 0.5
        [KeywordEnum(Vertex Colors, Vertex Colors And UV4)] _BendingControls ("Bending Parameters", Float) = 0
        [ToggleUI] _PortNtsIsSRGB ("Port: NTS map imported as sRGB", Float) = 1
    }

    SubShader {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" "DisableBatching" = "True" }
        LOD 300
        Cull Off

        CGPROGRAM
        #pragma surface PortAfsSurf PortAfs vertex:PortAfsVert alphatest:_Cutoff addshadow fullforwardshadows
        #pragma target 3.0
        #pragma shader_feature_local _BENDINGCONTROLS_VERTEX_COLORS _BENDINGCONTROLS_VERTEX_COLORS_AND_UV4
        #include "PortAfs.cginc"
        ENDCG
    }

    Fallback "Legacy Shaders/Transparent/Cutout/VertexLit"
}
