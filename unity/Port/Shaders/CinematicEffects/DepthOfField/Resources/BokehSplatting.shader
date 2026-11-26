// From-scratch reimplementation for the port: "Hidden/DepthOfField/BokehSplatting".
// Texture bokeh for UnityStandardAssets.CinematicEffects.DepthOfField. Written
// for this port from the driving script's pass layout and uniforms; not
// derived from any original shader source.
//
// Pass order matches DepthOfField.BokehTexturesPasses:
//  0 Apply    draws one point per entry of 'pointBuffer' (indirect, MeshTopology.Points)
//             as a textured sprite (_MainTex = bokeh shape), added on top of the image.
//  1 Collect  full-screen over the half-res colour + CoC buffer: pixels much
//             brighter than their blurred surroundings (_BlurredColor) and
//             blurred enough (_SpawnHeuristic) are appended to 'pointBuffer'
//             (random write target 1) and replaced by the blurred colour.
// Point layout (28 bytes, written and read only here): float2 uv, float size
// in pixels, float3 colour, float spare.
//
// Needs structured buffers and random writes (shader model 4.5). The script only
// takes this path when SystemInfo reports compute support, and even then never
// in the Basic tweak mode the game uses. A trivial fallback SubShader keeps the
// shader supported elsewhere so the effect does not disable itself.
Shader "Hidden/DepthOfField/BokehSplatting" {
Properties {
 _MainTex ("", 2D) = "white" { }
 _BlurredColor ("", 2D) = "white" { }
 _FgCocMask ("", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "UnityCG.cginc"

    struct PortBokehPoint
    {
        float2 uv;
        float size;
        float3 color;
        float spare;
    };

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BlurredColor;
    sampler2D _FgCocMask;
    float _SpawnHeuristic;
    float4 _BokehParams; // scale, intensity, threshold, max radius (pixels)
    float4 _Screen;      // 1 / width, 1 / height, max radius (pixels)
    ENDCG

    // 0 Apply
    Pass {
        Blend One One
        CGPROGRAM
        #pragma target 4.5
        #pragma vertex vertApply
        #pragma fragment fragApply

        StructuredBuffer<PortBokehPoint> pointBuffer;

        struct v2fApply
        {
            float4 pos : SV_POSITION;
            float psize : PSIZE;
            nointerpolation float3 centerSize : TEXCOORD0;
            nointerpolation float3 color : TEXCOORD1;
        };

        v2fApply vertApply(uint id : SV_VertexID)
        {
            PortBokehPoint p = pointBuffer[id];
            v2fApply o;
            float2 ndc = p.uv * 2.0 - 1.0;
            o.pos = float4(ndc.x, ndc.y * _ProjectionParams.x, 0.0, 1.0);
            float size = clamp(p.size, 1.0, max(_Screen.z * 2.0, 1.0)) * 2.0;
            o.psize = size;
            float2 pix = float2(ndc.x * 0.5 + 0.5, o.pos.y * 0.5 + 0.5);
#if UNITY_UV_STARTS_AT_TOP
            pix.y = 1.0 - pix.y;
#endif
            o.centerSize = float3(pix * _ScreenParams.xy, size);
            o.color = p.color;
            return o;
        }

        float4 fragApply(v2fApply i) : SV_Target
        {
            float2 local = (i.pos.xy - i.centerSize.xy) / i.centerSize.z + 0.5;
            float4 shape = tex2Dlod(_MainTex, float4(saturate(local), 0.0, 0.0));
            return float4(i.color * shape.rgb, 0.0);
        }
        ENDCG
    }

    // 1 Collect
    Pass {
        CGPROGRAM
        #pragma target 4.5
        #pragma vertex vert_img
        #pragma fragment fragCollect

        AppendStructuredBuffer<PortBokehPoint> pointBuffer : register(u1);

        float4 fragCollect(v2f_img i) : SV_Target
        {
            float4 c = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
            float3 blurred = tex2Dlod(_BlurredColor, float4(i.uv, 0.0, 0.0)).rgb;
            float coc = abs(c.a);
            float3 excess = max(c.rgb - blurred, 0.0);
            float lumaExcess = dot(excess, float3(0.2126, 0.7152, 0.0722));
            if (coc > _SpawnHeuristic && lumaExcess > _BokehParams.z)
            {
                PortBokehPoint p;
                p.uv = i.uv;
                p.size = max(coc * _BokehParams.w * _BokehParams.x, 1.0);
                // The excess energy of one half-res pixel (four screen pixels)
                // is spread over the sprite; intensity 50 is the neutral value.
                float area = max(p.size * p.size * 3.14159, 1.0);
                p.color = excess * (4.0 / area) * (_BokehParams.y / 50.0);
                p.spare = coc;
                pointBuffer.Append(p);
                return float4(blurred, c.a);
            }
            return c;
        }
        ENDCG
    }
}

// Fallback without structured buffers: Apply adds nothing, Collect passes the
// image through. The CGINCLUDE block above is prepended to these programs too,
// so they already get UnityCG.cginc and the _MainTex declaration from it.
SubShader {
    Cull Off ZWrite Off ZTest Always

    Pass {
        Blend One One
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        float4 frag(v2f_img i) : SV_Target { return 0.0; }
        ENDCG
    }

    Pass {
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        float4 frag(v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv); }
        ENDCG
    }
}
Fallback Off
}
