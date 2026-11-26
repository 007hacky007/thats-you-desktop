// From-scratch reimplementation for the port: "Hidden/SimpleClear".
// Writes transparent black. SunShafts uses it (through DrawBorder) to clear a
// one-pixel frame around the shaft mask so the radial blur does not smear
// clamped edge texels. Must stay a single pass: DrawBorder runs every pass.
Shader "Hidden/SimpleClear" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always

        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"

        struct v2f { float4 pos : SV_POSITION; };

        v2f vert(float4 vertex : POSITION)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(vertex);
            return o;
        }

        float4 frag(v2f i) : SV_Target
        {
            return float4(0.0, 0.0, 0.0, 0.0);
        }
        ENDCG
    }
}
Fallback Off
}
