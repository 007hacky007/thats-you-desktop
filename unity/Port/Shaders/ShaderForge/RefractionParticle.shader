// From-scratch reimplementation for the port.
// Shader Forge/RefractionParticle: heat haze / steam that bends the scene behind it.
// The scene is grabbed once per frame and re-read with a screen offset taken from the
// _RefractionMap (stored red/green as a signed direction) scaled by _RefractionAmount. The bend is
// masked by the particle texture and vertex alpha, and _MainTex * _TintColor (by tint alpha)
// is layered on top of the bent image. Fragments with almost no mask are discarded.
Shader "Shader Forge/RefractionParticle" {
    Properties {
        _MainTex ("MainTex", 2D) = "white" {}
        _TintColor ("Color", Color) = (0.5, 0.5, 0.5, 1)
        _RefractionMap ("Refraction Map", 2D) = "bump" {}
        _RefractionAmount ("Refraction Amount", Range(0, 1)) = 0
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }

        GrabPass { "_PortRefractionParticleGrab" }

        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Cull Off
            Lighting Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "../SonicEther/PortParticles.cginc"

            sampler2D _PortRefractionParticleGrab;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _RefractionMap;
            float4 _RefractionMap_ST;
            fixed4 _TintColor;
            float _RefractionAmount;

            // Screen-space offset at _RefractionAmount = 1, as a fraction of the screen.
            #define PORT_REFRACTION_SCALE 0.1

            struct v2f {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uvRefr : TEXCOORD1;
                float4 grabPos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(PortParticleAppData v)
            {
                v2f o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.uvRefr = TRANSFORM_TEX(v.texcoord, _RefractionMap);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 tex = tex2D(_MainTex, i.uv);
                // Particle textures here are either alpha-shaped or black-backed, so mask with both.
                half mask = tex.a * max(tex.r, max(tex.g, tex.b)) * i.color.a;
                clip(mask - 0.002);

                // The refraction map is a flat-centred normal map imported as an sRGB colour
                // texture, so in Linear colour space the sampler linearises it and the neutral
                // 0.5 would come back as about 0.21. Undo that so 0.5 decodes to no offset.
                half2 rg = tex2D(_RefractionMap, i.uvRefr).rg;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                rg = LinearToGammaSpace(half3(rg, 0.0h)).rg;
                #endif
                half2 dir = rg * 2.0h - 1.0h;
                float2 offset = dir * (_RefractionAmount * PORT_REFRACTION_SCALE * mask);
                // Keep the offset constant in screen space regardless of perspective divide.
                float2 grabUV = i.grabPos.xy / i.grabPos.w + offset;
                half3 scene = tex2D(_PortRefractionParticleGrab, grabUV).rgb;

                half tintAmount = saturate(_TintColor.a * mask);
                half3 col = lerp(scene, tex.rgb * _TintColor.rgb * i.color.rgb, tintAmount);
                // No fog here: the grabbed scene is already fogged.
                // Fade the edge of the bent region into the untouched scene.
                return half4(col, saturate(mask * 2.0h));
            }
            ENDCG
        }
    }

    Fallback "Legacy Shaders/Particles/Alpha Blended"
}
