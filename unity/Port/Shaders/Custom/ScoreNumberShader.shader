// From-scratch reimplementation for the port.
// Score digits on the rolling score cubes. _MainTex is the Numbers render texture (digits drawn
// on a transparent black clear, so already premultiplied); the material offset picks a digit
// cell. Only the part of a face inside the viewport band [_LowerCutOffBound, _UpperCuffOffBound]
// (vertical, 0 = bottom, 1 = top) is shown, faded out over _FadeDistance at both edges, so a
// rolling cube only shows its digits inside the score strip. _AlphaOverride fades everything.
Shader "Custom/ScoreNumberShader" {
    Properties {
        _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
        _LowerCutOffBound ("Lower Cutoff Bound", Float) = 0.66
        _UpperCuffOffBound ("Upper Cutoff Bound", Float) = 0.735
        _FadeDistance ("Fade Distance", Float) = 0.01
        _AlphaOverride ("Alpha Override", Float) = 1
    }

    SubShader {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Pass {
            ZWrite Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "../Wish/PortWishUnlit.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _LowerCutOffBound;
            float _UpperCuffOffBound;
            float _FadeDistance;
            float _AlphaOverride;

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(PortWishUnlitAppData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float y = PortWishViewportY(i.screenPos);
                float k = PortWishBandWeight(y, _LowerCutOffBound, _UpperCuffOffBound, _FadeDistance);
                k *= saturate(_AlphaOverride);
                clip(k - 0.001);
                // Premultiplied output: scale colour and alpha together.
                return tex2D(_MainTex, i.uv) * k;
            }
            ENDCG
        }
    }
    Fallback Off
}
