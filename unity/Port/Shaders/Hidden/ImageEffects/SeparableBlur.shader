// From-scratch reimplementation for the port: "Hidden/SeparableBlur".
// One-dimensional 7-tap gaussian blur. The direction and tap spacing come from
// the "offsets" vector (xy, in UV units per tap); scripts run it twice, once
// vertically and once horizontally.
Shader "Hidden/SeparableBlur" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxGrade.cginc"

        float4 offsets;

        float4 frag(PortGradeV2f i) : SV_Target
        {
            float2 d = offsets.xy;
            float4 c = tex2D(_MainTex, i.uv) * PORT_GRADE_W0;
            c += (tex2D(_MainTex, i.uv + d) + tex2D(_MainTex, i.uv - d)) * PORT_GRADE_W1;
            c += (tex2D(_MainTex, i.uv + d * 2.0) + tex2D(_MainTex, i.uv - d * 2.0)) * PORT_GRADE_W2;
            c += (tex2D(_MainTex, i.uv + d * 3.0) + tex2D(_MainTex, i.uv - d * 3.0)) * PORT_GRADE_W3;
            return c;
        }
        ENDCG
    }
}
Fallback Off
}
