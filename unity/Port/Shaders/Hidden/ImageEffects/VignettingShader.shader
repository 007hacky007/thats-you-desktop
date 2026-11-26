// From-scratch reimplementation for the port: "Hidden/Vignetting".
// Used by VignetteAndChromaticAberration (pass 0). Darkens the frame towards
// the corners (_Intensity) and fades towards a pre-blurred half-resolution
// copy (_VignetteTex) at the borders (_Blur).
Shader "Hidden/Vignetting" {
Properties {
 _MainTex ("Base", 2D) = "white" { }
 _VignetteTex ("Vignette", 2D) = "white" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment frag
        #pragma target 3.0
        #include "PortFxGrade.cginc"

        sampler2D _VignetteTex;
        float _Intensity;
        float _Blur;

        float4 frag(PortGradeV2f i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);

            // Centred coordinates, -1..1 across the frame; r2 reaches 2 in
            // the corners and 1 at the middle of each edge.
            float2 p = (i.uv2 - 0.5) * 2.0;
            float r2 = dot(p, p);

            // Soft border blur. The weight is zero when _Blur is zero, which is
            // also when the script leaves _VignetteTex unset.
            float4 b = tex2D(_VignetteTex, i.uv2);
            c.rgb = lerp(c.rgb, b.rgb, saturate(_Blur * r2));

            float shade = saturate(1.0 - r2 * _Intensity * 0.08);
            c.rgb *= shade * shade * (3.0 - 2.0 * shade) * 0.5 + shade * 0.5;
            return c;
        }
        ENDCG
    }
}
Fallback Off
}
