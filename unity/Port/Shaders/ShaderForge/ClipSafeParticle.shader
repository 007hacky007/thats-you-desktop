// From-scratch reimplementation for the port.
// Shader Forge/ClipSafeParticle: glowing dust (school dust, wild dust, fear particles).
// _Texture * vertex colour * _ParticleGain, added on top of the scene. The particle fades out as
// it approaches the camera: full strength beyond _ClipThreshold units, falling to zero at the
// near plane along a _Power curve, so motes drifting past the lens never pop or fill the screen.
Shader "Shader Forge/ClipSafeParticle" {
    Properties {
        _Texture ("Texture", 2D) = "white" {}
        _ParticleGain ("Particle Gain", Range(0, 1)) = 1
        _ClipThreshold ("ClipThreshold", Range(0.01, 10)) = 0.1
        _Power ("Power", Range(0.01, 10)) = 0.01
    }

    SubShader {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Blend One One
        ColorMask RGB
        Cull Off
        Lighting Off
        ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "../SonicEther/PortParticles.cginc"

            sampler2D _Texture;
            float4 _Texture_ST;
            half _ParticleGain;
            float _ClipThreshold;
            float _Power;

            struct v2f {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(PortParticleAppData v)
            {
                v2f o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.eyeDepth = -UnityObjectToViewPos(v.vertex.xyz).z;
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.texcoord, _Texture);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 tex = tex2D(_Texture, i.uv);
                half strength = tex.a * i.color.a * _ParticleGain;
                strength *= PortNearFade(i.eyeDepth, _ClipThreshold, _Power);
                half4 col = half4(tex.rgb * i.color.rgb * strength, strength);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, half4(0, 0, 0, 0));
                return col;
            }
            ENDCG
        }
    }

    Fallback "Legacy Shaders/Particles/Additive"
}
