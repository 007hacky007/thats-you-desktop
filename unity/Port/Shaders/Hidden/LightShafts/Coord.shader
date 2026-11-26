// From-scratch reimplementation for the port of the light shaft "Coord" pass.
// Renders the epipolar sample layout: MRT 0 (RGFloat) receives the screen uv of every
// epipolar sample, MRT 1 (RFloat) the linear 0..1 scene depth at that position.
Shader "Hidden/Coord"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex ls_vert_epi
            #pragma fragment frag
            #pragma target 3.0
            #include "PortLightShafts.cginc"

            struct CoordOut
            {
                float4 coord : SV_Target0;
                float4 depth : SV_Target1;
            };

            CoordOut frag(ls_v2f i)
            {
                float samples = _CoordTexDim.x;
                float2 L = _LightPos.xy;
                float2 E = LS_BorderPoint(i.uv.y);
                float2 S = LS_LineStart(L, E);
                float j = i.uv.x * samples - 0.5;
                float s = saturate(j / max(samples - 1.0, 1.0));
                float2 suv = lerp(S, E, s) * 0.5 + 0.5;

                CoordOut o;
                o.coord = float4(suv, 0.0, 0.0);
                o.depth = LS_SceneDepth01(suv).xxxx;
                return o;
            }
            ENDCG
        }
    }
    Fallback Off
}
