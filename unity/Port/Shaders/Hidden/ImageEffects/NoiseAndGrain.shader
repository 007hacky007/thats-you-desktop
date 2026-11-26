// From-scratch reimplementation for the port: "Hidden/NoiseAndGrain".
// Filmic grain for NoiseAndGrain. The script draws a grid of screen quads
// (GL.LoadOrtho, vertices in 0..1) whose UV0 picks a random tile of _NoiseTex;
// the grain strength follows the pixel's brightness relative to _MidGrey,
// mixing _NoiseAmount.x (general), .y (shadows) and .z (highlights).
//   Pass 0: quad grid, apply grain straight onto _MainTex.
//   Pass 1: full-screen blit, apply a pre-rendered (softened) grain buffer
//           bound as _NoiseTex.
//   Pass 2: quad grid, write the grain alone (0.5 = neutral) for pass 1.
Shader "Hidden/NoiseAndGrain" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "white" { }
 _NoiseTex ("Noise (RGB)", 2D) = "white" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "PortFxGrade.cginc"

    sampler2D _NoiseTex;
    float4 _NoisePerChannel;
    float4 _NoiseTilingPerChannel;
    float4 _MidGrey;
    float4 _NoiseAmount;

    float4 _NoiseTex_TexelSize;

    // UV0: random noise tile origin plus a one-texel span across the quad.
    // UV1: 0..1 across each quad.
    struct appdataGrid
    {
        float4 vertex : POSITION;
        float2 texcoord : TEXCOORD0;
        float2 texcoord1 : TEXCOORD1;
    };

    struct v2fGrid
    {
        float4 pos : SV_POSITION;
        float2 uvScreen : TEXCOORD0;
        float2 uvOrigin : TEXCOORD1;
        float2 uvLocal : TEXCOORD2;
    };

    v2fGrid vertGrid(appdataGrid v)
    {
        v2fGrid o;
        o.pos = UnityObjectToClipPos(float4(v.vertex.xy, 0.1, 1.0));
        float2 s = v.vertex.xy;
        if (PortGradeSourceFlipped())
            s.y = 1.0 - s.y;
        o.uvScreen = s;
        o.uvLocal = v.texcoord1;
        o.uvOrigin = v.texcoord - v.texcoord1 * _NoiseTex_TexelSize.xy;
        return o;
    }

    // Grain in perceptual units, centred on 0. Each 64-pixel quad shows
    // _NoiseTilingPerChannel noise texels per channel, starting at the
    // quad's random origin (texture wraps).
    float3 SampleGrain(float2 origin, float2 local)
    {
        float2 span = local * _NoiseTex_TexelSize.xy;
        float3 n;
        n.r = tex2D(_NoiseTex, origin + span * _NoiseTilingPerChannel.x).r;
        n.g = tex2D(_NoiseTex, origin + span * _NoiseTilingPerChannel.y).g;
        n.b = tex2D(_NoiseTex, origin + span * _NoiseTilingPerChannel.z).b;
        n = PortGradeToPerceptual(n);
        return (n - 0.5) * _NoisePerChannel.xyz;
    }

    // Applies centred grain g (perceptual units) to colour c.
    float4 ApplyGrain(float4 c, float3 g)
    {
        float3 p = PortGradeToPerceptual(c.rgb);
        float l = PortGradeLuma(saturate(p));
        float dark = saturate((l - _MidGrey.x) * _MidGrey.z);
        float bright = saturate((l - _MidGrey.x) * _MidGrey.y);
        float amount = _NoiseAmount.x + _NoiseAmount.y * dark + _NoiseAmount.z * bright;
        p += g * 2.0 * amount;
        c.rgb = PortGradeFromPerceptual(max(p, 0.0));
        return c;
    }

    float4 fragDirect(v2fGrid i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uvScreen);
        return ApplyGrain(c, SampleGrain(i.uvOrigin, i.uvLocal));
    }

    float4 fragGrainOnly(v2fGrid i) : SV_Target
    {
        return float4(saturate(SampleGrain(i.uvOrigin, i.uvLocal) + 0.5), 1.0);
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
        #pragma fragment fragDirect
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
    Pass {
        CGPROGRAM
        #pragma vertex vertGrid
        #pragma fragment fragGrainOnly
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
