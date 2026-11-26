// From-scratch reimplementation for the port: "Hidden/NoiseAndGrain".
// Filmic grain for NoiseAndGrain. The script draws a grid of screen quads
// (GL.LoadOrtho, vertices in 0..1) whose UV0 picks a random tile of _NoiseTex.
// The noise texel (0..1, 0.5 = neutral) is overlay-blended per channel onto
// the source colour (raw render target values, no perceptual conversion), and
// the result is mixed back with a strength that follows the pixel's luminance
// relative to _MidGrey: _NoiseAmount.x (general) everywhere, plus .y (blacks)
// below mid grey and .z (whites) above it.
//   Pass 0: quad grid, apply grain straight onto _MainTex.
//   Pass 1: full-screen blit, apply a pre-rendered (softened) grain buffer
//           bound as _NoiseTex.
//   Pass 2: quad grid, write the noise texel alone for pass 1.
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

    // Noise texel for this pixel, 0..1 with 0.5 neutral. Each 64-pixel quad
    // shows _NoiseTilingPerChannel noise texels per channel, starting at the
    // quad's random origin (texture wraps). _NoisePerChannel scales each
    // channel's deviation from neutral.
    float3 SampleGrain(float2 origin, float2 local)
    {
        float2 span = local * _NoiseTex_TexelSize.xy;
        float3 n;
        n.r = tex2D(_NoiseTex, origin + span * _NoiseTilingPerChannel.x).r;
        n.g = tex2D(_NoiseTex, origin + span * _NoiseTilingPerChannel.y).g;
        n.b = tex2D(_NoiseTex, origin + span * _NoiseTilingPerChannel.z).b;
        return lerp(0.5, n, _NoisePerChannel.xyz);
    }

    // Per-channel overlay of n onto base b: multiply-like (2bn) in the lower
    // half, screen-like above. n = 0.5 returns b unchanged.
    float3 OverlayBlend(float3 b, float3 n)
    {
        float3 lo = 2.0 * b * n;
        float3 hi = 1.0 - 2.0 * (1.0 - b) * (1.0 - n);
        return lerp(lo, hi, step(0.5, b));
    }

    // Applies noise texel n (0..1, 0.5 neutral) to colour c.
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

    float4 fragDirect(v2fGrid i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uvScreen);
        return ApplyGrain(c, SampleGrain(i.uvOrigin, i.uvLocal));
    }

    float4 fragGrainOnly(v2fGrid i) : SV_Target
    {
        return float4(saturate(SampleGrain(i.uvOrigin, i.uvLocal)), 1.0);
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
