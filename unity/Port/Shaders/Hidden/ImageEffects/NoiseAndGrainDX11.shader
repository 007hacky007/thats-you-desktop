// From-scratch reimplementation for the port: "Hidden/NoiseAndGrainDX11".
// Procedural variant of NoiseAndGrain (dx11Grain): grain comes from a per-pixel
// hash re-seeded every frame by _DX11NoiseTime instead of noise texture tiles.
// The script still draws its quad grid for passes 0-3.
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

    // Roughly gaussian grain (sum of two uniforms), centred on 0, range +-0.5.
    float GrainAt(float2 pix, float seed)
    {
        float t = frac(_DX11NoiseTime * 0.618034) * 251.0 + seed * 37.0;
        float a = PortGradeHash(pix + float2(t, t * 1.37));
        float b = PortGradeHash(pix.yx + float2(t * 0.71, t + 17.0));
        return (a + b) * 0.5 - 0.5;
    }

    float3 ColourGrain(float2 pix)
    {
        float3 g = float3(GrainAt(pix, 0.0), GrainAt(pix, 1.0), GrainAt(pix, 2.0));
        return g * _NoisePerChannel.xyz;
    }

    float3 MonoGrain(float2 pix)
    {
        float g = GrainAt(pix, 0.0);
        return float3(g, g, g) * _NoisePerChannel.xyz;
    }

    float4 ApplyGrain(float4 c, float3 g)
    {
        float3 p = PortGradeToPerceptual(c.rgb);
        float l = PortGradeLuma(saturate(p));
        float dark = saturate((l - _MidGrey.x) * _MidGrey.z);
        float bright = saturate((l - _MidGrey.x) * _MidGrey.y);
        float amount = _NoiseAmount.x + _NoiseAmount.y * dark + _NoiseAmount.z * bright;
        // The procedural grain has a wider spread than the texture tiles.
        p += g * amount;
        c.rgb = PortGradeFromPerceptual(max(p, 0.0));
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
        return float4(saturate(ColourGrain(floor(i.pos.xy)) + 0.5), 1.0);
    }

    float4 fragMonoOnly(v2fGrid i) : SV_Target
    {
        return float4(saturate(MonoGrain(floor(i.pos.xy)) + 0.5), 1.0);
    }

    float4 fragComposite(PortGradeV2f i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        float3 g = tex2D(_NoiseTex, i.uv2).rgb - 0.5;
        return ApplyGrain(c, g);
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
