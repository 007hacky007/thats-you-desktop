// From-scratch reimplementation for the port.
// Custom/DustMote: soft dust cloud / mote sprites (road dust, floating motes in rooms).
// The sprite shape is a soft procedural disc (edge width _Blur) blended towards the _Shape
// texture by _DoShape. Colour runs from _ShadowColor to _LightColor with the strength of the
// main directional light, a noise texture makes individual speckles flash in _Speckles colour
// at _FlashSpeed, _Emission decides how much the colour ignores scene lighting and
// _Transparency thins the whole cloud. Alpha blended, no depth write.
Shader "Custom/DustMote" {
    Properties {
        _Shape ("Particle Shape", 2D) = "gray" {}
        _Noise ("Noise Tex", 2D) = "gray" {}
        _DoShape ("Do Shape Tex", Range(0, 1)) = 0
        _LightColor ("Light Color", Color) = (0, 1, 0, 1)
        _ShadowColor ("Shadow Color", Color) = (0, 0, 1, 1)
        _Speckles ("Speckle Color", Color) = (0.7, 0.7, 1, 1)
        _Blur ("Blur", Range(0, 0.5)) = 0.05
        _Emission ("Emission", Range(0, 1)) = 1
        _Transparency ("Transparency", Range(0, 1)) = 0
        _FlashSpeed ("Flash Speed", Range(0, 1)) = 0.86
    }

    SubShader {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }

        Pass {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "../SonicEther/PortParticles.cginc"
            #include "UnityLightingCommon.cginc"

            sampler2D _Shape;
            float4 _Shape_ST;
            sampler2D _Noise;
            float4 _Noise_ST;
            half _DoShape;
            fixed4 _LightColor;
            fixed4 _ShadowColor;
            fixed4 _Speckles;
            half _Blur;
            half _Emission;
            half _Transparency;
            half _FlashSpeed;

            struct v2f {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uvNoise : TEXCOORD1;
                half mainLight : TEXCOORD2;
                half3 sceneLight : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(PortParticleAppData v)
            {
                v2f o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.texcoord, _Shape);
                o.uvNoise = TRANSFORM_TEX(v.texcoord, _Noise);

                // Strength of the main directional light; 0 when the scene has none.
                o.mainLight = saturate(dot(_LightColor0.rgb, half3(0.2126h, 0.7152h, 0.0722h)));
                // Light reaching the mote when it is not self-lit: main light plus ambient.
                o.sceneLight = _LightColor0.rgb + ShadeSH9(half4(0, 1, 0, 1));

                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // Soft procedural disc, edge width set by _Blur. It ends at 0.9 of the UV
                // half-extent so it also fits inside the octagonal DustParticlePlane mesh
                // (UVs inscribed at about 0.9) used by the road dust, not only billboard quads.
                half r = length(i.uv - 0.5) * 2.0;
                half edge = max(_Blur * 2.0h, 0.05h);
                half disc = 1.0h - smoothstep(0.9h - edge, 0.9h, r);

                // Shape texture, softened by sampling a blurrier mip as _Blur rises.
                half4 shapeTex = tex2Dbias(_Shape, float4(i.uv, 0, _Blur * 8.0));
                half shapeMask = shapeTex.a * max(shapeTex.r, max(shapeTex.g, shapeTex.b));
                half shape = lerp(disc, shapeMask, _DoShape);

                // Shadow to light colour by main light strength.
                half3 col = lerp(_ShadowColor.rgb, _LightColor.rgb, i.mainLight) * i.color.rgb;

                // Speckles: each noise texel flashes on its own phase. A flat (gray) noise
                // texture produces no speckles.
                half n = tex2D(_Noise, i.uvNoise).r;
                half phase = n * 25.13h + _Time.y * _FlashSpeed * 8.0h;
                half flash = pow(saturate(sin(phase)), 16.0h);
                half speckle = saturate(abs(n - 0.5h) * 4.0h) * flash * _Speckles.a;
                col = lerp(col, _Speckles.rgb, speckle);

                // Unlit (emissive) versus lit by the scene.
                col *= lerp(i.sceneLight, half3(1, 1, 1), _Emission);

                half alpha = saturate(shape * i.color.a * (1.0h - _Transparency));
                half4 o = half4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }

    Fallback "Legacy Shaders/Particles/Alpha Blended"
}
