// From-scratch reimplementation for the port: "Hidden/NoiseAndGrainDX11".
// Procedural variant of NoiseAndGrain (dx11Grain): grain comes from a per-pixel
// hash re-seeded every frame by _DX11NoiseTime instead of noise texture tiles.
// The grain value (0..1, 0.5 = neutral) is overlay-blended per channel onto
// the raw source colour and mixed back by a luminance-dependent strength, the
// same way as Hidden/NoiseAndGrain. The script still draws its quad grid for
// passes 0-3.
//   Pass 0: colour grain onto _MainTex.     Pass 1: monochrome grain onto _MainTex.
//   Pass 2: colour grain only (to buffer).  Pass 3: monochrome grain only.
//   Pass 4: full-screen blit applying the grain buffer bound as _NoiseTex.
Shader "Hidden/NoiseAndGrainDX11" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
 _NoiseTex ("Noise (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "PortFxGrade.cginc"

    sampler2D _NoiseTex;
    float _DX11NoiseTime;
    float4 _NoisePerChannel;
    float4 _MidGrey;
    float4 _NoiseAmount;

    struct appdataGrid
    {
        float4 vertex : POSITION;
    };

    struct v2fGrid
    {
        float4 pos : SV_POSITION;
        float2 uvScreen : TEXCOORD0;
    };

    v2fGrid vertGrid(appdataGrid v)
    {
        v2fGrid o;
        o.pos = UnityObjectToClipPos(float4(v.vertex.xy, 0.1, 1.0));
        float2 s = v.vertex.xy;
        if (PortGradeSourceFlipped())
            s.y = 1.0 - s.y;
        o.uvScreen = s;
        return o;
    }

    // Roughly gaussian grain (mean of two uniforms) in 0..1, centred on 0.5.
    float GrainAt(float2 pix, float seed)
    {
        float t = frac(_DX11NoiseTime * 0.618034) * 251.0 + seed * 37.0;
        float a = PortGradeHash(pix + float2(t, t * 1.37));
        float b = PortGradeHash(pix.yx + float2(t * 0.71, t + 17.0));
        return (a + b) * 0.5;
    }

    float3 ColourGrain(float2 pix)
    {
        float3 g = float3(GrainAt(pix, 0.0), GrainAt(pix, 1.0), GrainAt(pix, 2.0));
        return lerp(0.5, g, _NoisePerChannel.xyz);
    }

    float3 MonoGrain(float2 pix)
    {
        float g = GrainAt(pix, 0.0);
        return lerp(0.5, float3(g, g, g), _NoisePerChannel.xyz);
    }

    // Per-channel overlay of n onto base b: multiply-like (2bn) in the lower
    // half, screen-like above. n = 0.5 returns b unchanged.
    float3 OverlayBlend(float3 b, float3 n)
    {
        float3 lo = 2.0 * b * n;
        float3 hi = 1.0 - 2.0 * (1.0 - b) * (1.0 - n);
        return lerp(lo, hi, step(0.5, b));
    }

    // Applies grain value n (0..1, 0.5 neutral) to colour c.
    float4 ApplyGrain(float4 c, float3 n)
    {
        float3 b = saturate(c.rgb);
        float l = PortGradeLuma(b);
        float bright = saturate((l - _MidGrey.x) * _MidGrey.y);
        float dark = saturate((l - _MidGrey.x) * _MidGrey.z);
        float amount = _NoiseAmount.x + max(0.0, _NoiseAmount.z * bright + _NoiseAmount.y * dark);
        c.rgb = max(lerp(b, OverlayBlend(b, saturate(n)), amount), 0.0);
        return c;
    }

    float4 fragColour(v2fGrid i) : SV_Target
    {
        return ApplyGrain(tex2D(_MainTex, i.uvScreen), ColourGrain(floor(i.pos.xy)));
    }

    float4 fragMono(v2fGrid i) : SV_Target
    {
        return ApplyGrain(tex2D(_MainTex, i.uvScreen), MonoGrain(floor(i.pos.xy)));
    }

    float4 fragColourOnly(v2fGrid i) : SV_Target
    {
        return float4(saturate(ColourGrain(floor(i.pos.xy))), 1.0);
    }

    float4 fragMonoOnly(v2fGrid i) : SV_Target
    {
        return float4(saturate(MonoGrain(floor(i.pos.xy))), 1.0);
    }

    float4 fragComposite(PortGradeV2f i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        return ApplyGrain(c, tex2D(_NoiseTex, i.uv2).rgb);
    }
    ENDCG

    Pass {
        CGPROGRAM
        #pragma vertex vertGrid
        #pragma fragment fragColour
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex vertGrid
        #pragma fragment fragMono
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex vertGrid
        #pragma fragment fragColourOnly
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex vertGrid
        #pragma fragment fragMonoOnly
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex PortGradeVert
        #pragma fragment fragComposite
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
