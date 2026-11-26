// From-scratch reimplementation for the port: "Hidden/Dof/DX11Dof".
// The bokeh-sprite path of DepthOfField (blurType DX11) spawns sprites into an
// append buffer and expands them with a geometry shader, which Metal does not
// have. This version keeps the two passes the script expects but degrades to a
// sprite-free result:
//  0  blends the half-res CoC image towards _BlurredColor by its CoC and
//     appends no sprites, so the indirect draw count copied afterwards is 0.
//  1  point-sprite pass for DrawProceduralIndirectNow; with no sprites it draws
//     nothing, and any point it might receive is placed outside the clip volume.
// The remaining blur and composite passes of Hidden/Dof/DepthOfFieldHdr then
// produce a normal disc-style depth of field.
Shader "Hidden/Dof/DX11Dof" {
Properties {
 _MainTex ("", 2D) = "white" { }
 _BlurredColor ("", 2D) = "white" { }
 _FgCocMask ("", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    // 0
    Pass {
        CGPROGRAM
        #pragma vertex PortFxVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxAA.cginc"

        sampler2D _BlurredColor;

        float4 frag(PortFxV2f i) : SV_Target
        {
            float4 c = PortFxTap(i.uv);
            float4 blurred = tex2Dlod(_BlurredColor, float4(i.uvAux, 0.0, 0.0));
            return float4(lerp(c.rgb, blurred.rgb, saturate(c.a)), c.a);
        }
        ENDCG
    }

    // 1
    Pass {
        ZTest Always Cull Off ZWrite Off
        Blend One One
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.5
        #include "UnityCG.cginc"

        struct v2f
        {
            float4 pos : SV_POSITION;
            float size : PSIZE;
        };

        v2f vert(uint id : SV_VertexID)
        {
            v2f o;
            o.pos = float4(2.0, 2.0, 2.0, 1.0) + (float)id * 0.0;
            o.size = 1.0;
            return o;
        }

        float4 frag() : SV_Target
        {
            return float4(0.0, 0.0, 0.0, 0.0);
        }
        ENDCG
    }
}
Fallback Off
}
