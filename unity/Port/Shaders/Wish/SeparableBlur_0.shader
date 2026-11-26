// From-scratch reimplementation for the port.
// One direction of a separable blur. The caller sets the global "offsets" vector (xy = step
// in uv per tap, e.g. (2/width, 0) then (0, 2/height)) and ping-pongs between two targets.
// Seven taps with Gaussian weights (sigma 1.5 taps), normalised to sum to 1.
Shader "Hidden/SeparableGlassBlur" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma vertex PortWishVertBlit
            #pragma fragment frag
            #pragma target 3.0
            #include "PortWishScreen.cginc"

            float4 offsets;

            fixed4 frag(PortWishBlitV2F i) : SV_Target
            {
                const float w0 = 0.2710;
                const float w1 = 0.2167;
                const float w2 = 0.1109;
                const float w3 = 0.0366;

                float2 dir = offsets.xy;
                float4 sum = tex2D(_MainTex, i.uv) * w0;
                sum += (tex2D(_MainTex, i.uv + dir) + tex2D(_MainTex, i.uv - dir)) * w1;
                sum += (tex2D(_MainTex, i.uv + dir * 2.0) + tex2D(_MainTex, i.uv - dir * 2.0)) * w2;
                sum += (tex2D(_MainTex, i.uv + dir * 3.0) + tex2D(_MainTex, i.uv - dir * 3.0)) * w3;
                return sum;
            }
            ENDCG
        }
    }
    Fallback Off
}
