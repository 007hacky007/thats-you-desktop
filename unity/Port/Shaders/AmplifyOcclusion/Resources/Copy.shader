// From-scratch reimplementation for the port of "Hidden/Amplify Occlusion/Copy".
// Copies _AO_Source texel for texel into the bound target (the occlusion
// atlas) at pixel offset _AO_Target_Position. The blit mesh (0..1) is scaled
// to the source size; rows are addressed in memory order so the copy matches
// the Reinterleave pass of the Occlusion shader on every graphics API.
Shader "Hidden/Amplify Occlusion/Copy" {
SubShader {
    Cull Off ZWrite Off ZTest Always

    // 0: Copy
    Pass {
        Name "Copy"
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.5
        #include "UnityCG.cginc"

        sampler2D _AO_Source;
        float4 _AO_Source_TexelSize;
        float4 _AO_Target_TexelSize;
        float4 _AO_Target_Position;

        struct appdata
        {
            float4 vertex : POSITION;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
        };

        v2f vert(appdata v)
        {
            v2f o;
            float2 px = _AO_Target_Position.xy + v.vertex.xy * _AO_Source_TexelSize.zw;
            float2 ndc = px * _AO_Target_TexelSize.xy * 2.0 - 1.0;
#if UNITY_UV_STARTS_AT_TOP
            // Top-left origin APIs put memory row 0 at clip space y = +1.
            ndc.y = -ndc.y;
#endif
            o.pos = float4(ndc, 0.5, 1.0);
            return o;
        }

        float4 frag(v2f i) : SV_Target
        {
            float2 src = i.pos.xy - _AO_Target_Position.xy;
            return tex2Dlod(_AO_Source, float4(src * _AO_Source_TexelSize.xy, 0.0, 0.0));
        }
        ENDCG
    }
}
Fallback Off
}
