// From-scratch reimplementation for the port: "Hidden/Ultimate/FlareMesh".
// Bokeh flare sprites. BokehRenderer builds one quad per pixel of the
// bright-pass image (positions in pixels, uv = sprite corner, uv2 = the source
// pixel) and draws them with SetPass(0) + DrawMeshNow into a cleared target.
// Each quad reads its source pixel's colour in the vertex shader, is placed
// with _FlareProj (orthographic, pixel space) and splats the _MainTex flare
// shape tinted by that colour times _Intensity, accumulated additively.
Shader "Hidden/Ultimate/FlareMesh" {
Properties {
 _MainTex ("Base (RGB) Trans (A)", 2D) = "black" { }
 _BrightTexture ("Base (RGB) Trans (A)", 2D) = "black" { }
}
SubShader {
    Pass {
        Cull Off ZWrite Off ZTest Always
        Blend One One

        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.0
        #include "../PortUltimateFlare.cginc"

        sampler2D _BrightTexture;
        float4x4 _FlareProj;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv     : TEXCOORD0;
            float2 source : TEXCOORD1;
        };

        struct v2f
        {
            float4 pos   : SV_POSITION;
            float2 uv    : TEXCOORD0;
            float3 color : TEXCOORD1;
        };

        v2f vert(appdata v)
        {
            v2f o;
            float3 bright = tex2Dlod(_BrightTexture, float4(v.source, 0.0, 0.0)).rgb;
            o.color = bright * _Intensity;
            o.uv = v.uv;

            float4 p = mul(_FlareProj, float4(v.vertex.xyz, 1.0));
            // _FlareProj is used raw (no GL.GetGPUProjectionMatrix). The mesh puts
            // sprite row r at y = r but samples its source at v = 1 - r/h, which
            // only lines up with the source image on top-origin APIs (D3D, Metal,
            // PS4) without a flip. Bottom-origin GL needs the flip instead.
            #if !UNITY_UV_STARTS_AT_TOP
            p.y = -p.y;
            #endif
            p.z = 0.5 * p.w; // keep inside the clip depth range of every API
            // Collapse sprites of black pixels outside the clip volume so they
            // cost no fill.
            if (dot(bright, float3(1.0, 1.0, 1.0)) <= 1e-5)
                p = float4(2.0, 2.0, 2.0, 1.0);
            o.pos = p;
            return o;
        }

        float4 frag(v2f i) : SV_Target
        {
            float4 shape = tex2D(_MainTex, i.uv);
            return float4(shape.rgb * i.color, 0.0);
        }
        ENDCG
    }
}
Fallback Off
}
