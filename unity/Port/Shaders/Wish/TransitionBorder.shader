// From-scratch reimplementation for the port.
// Border shading for TransitionBorderOverlay during pan transitions between scenarios.
// _EdgePositionScreenSpace.xy holds the min/max normalised screen coordinate of the occluder
// wall that separates the two scenarios (already padded by the script). A soft dark band of
// _TransitionBandwidth is drawn centred on each of the two edges to hide the seams.
// Pass 0: horizontal pans (edges along screen x). Pass 1: vertical pans (edges along screen y).
Shader "Wish/TransitionBorder" {
    Properties {
        _MainTex ("Screen Blended", 2D) = "" {}
        _EdgePositionScreenSpace ("Transition Edge (Min/Max Normalised Screen Coord)", Vector) = (0, 0, 0, 0)
        _TransitionBandwidth ("Transition Edge Width (Normalised Screen Coord)", Float) = 0.1
    }

    CGINCLUDE
    #include "PortWishScreen.cginc"

    float4 _EdgePositionScreenSpace;
    float _TransitionBandwidth;

    // 1 away from the edges, falling smoothly to 0 on each edge line.
    float PortWishBorderShade(float p)
    {
        float halfBand = max(_TransitionBandwidth * 0.5, 1e-4);
        float d = min(abs(p - _EdgePositionScreenSpace.x), abs(p - _EdgePositionScreenSpace.y));
        return smoothstep(0.0, halfBand, d);
    }

    fixed4 PortWishBorder(PortWishBlitV2F i, float p)
    {
        fixed4 c = tex2D(_MainTex, i.uv);
        c.rgb *= PortWishBorderShade(p);
        return c;
    }

    fixed4 fragHorizontal(PortWishBlitV2F i) : SV_Target
    {
        return PortWishBorder(i, PortWishScreenUV(i).x);
    }

    fixed4 fragVertical(PortWishBlitV2F i) : SV_Target
    {
        return PortWishBorder(i, PortWishScreenUV(i).y);
    }
    ENDCG

    SubShader {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma vertex PortWishVertBlit
            #pragma fragment fragHorizontal
            #pragma target 3.0
            ENDCG
        }

        Pass {
            CGPROGRAM
            #pragma vertex PortWishVertBlit
            #pragma fragment fragVertical
            #pragma target 3.0
            ENDCG
        }
    }
    Fallback Off
}
