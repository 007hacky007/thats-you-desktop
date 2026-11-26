// From-scratch reimplementation for the port: "Hidden/GlobalFog".
// Screen-space distance and height fog for GlobalFog, using the scene's
// RenderSettings fog (colour, mode, density, linear start/end) passed in as
// _SceneFogParams / _SceneFogMode. World positions are rebuilt from the depth
// texture and the far-plane frustum corner rays in _FrustumCornersWS
// (rows: 0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left).
//   Pass 0: distance + height fog.  Pass 1: distance only.  Pass 2: height only.
Shader "Hidden/GlobalFog" {
Properties {
 _MainTex ("Base (RGB)", 2D) = "black" { }
}
SubShader {
    Cull Off ZWrite Off ZTest Always

    CGINCLUDE
    #include "PortFxGrade.cginc"

    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float4x4 _FrustumCornersWS;
    float4 _CameraWS;
    float4 _HeightParams;    // x: fog top Y, y: camera Y - top, z: camera inside (1/0), w: density
    float4 _DistanceParams;  // x: -start distance, y: 1 excludes far plane, 2 includes it
    float4 _SceneFogParams;  // x: exp2 density, y: exp density, z: -1/(end-start), w: end/(end-start)
    float4 _SceneFogMode;    // x: FogMode (1 linear, 2 exp, 3 exp2), y: radial distance

    struct v2fFog
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 uvDepth : TEXCOORD1;
        float3 ray : TEXCOORD2;
    };

    v2fFog vertFog(PortGradeAppdata v)
    {
        v2fFog o;
        // The script encodes the corner index in z; position only needs xy.
        o.pos = UnityObjectToClipPos(float4(v.vertex.xy, 0.1, 1.0));
        o.uv = v.texcoord;
        o.uvDepth = PortGradeScreenUV(v.texcoord);
        // Rays are affine across the screen, so bilinear interpolation of the
        // corners in screen space is exact per vertex and per pixel.
        float3 top = lerp(_FrustumCornersWS[0].xyz, _FrustumCornersWS[1].xyz, o.uvDepth.x);
        float3 bottom = lerp(_FrustumCornersWS[3].xyz, _FrustumCornersWS[2].xyz, o.uvDepth.x);
        o.ray = lerp(bottom, top, o.uvDepth.y);
        return o;
    }

    // Transmittance (1 = clear, 0 = fully fogged) for an optical distance.
    float FogTransmittance(float d)
    {
        float f = 1.0;
        if (_SceneFogMode.x == 1.0)
            f = d * _SceneFogParams.z + _SceneFogParams.w;
        else if (_SceneFogMode.x == 2.0)
            f = exp2(-_SceneFogParams.y * d);
        else if (_SceneFogMode.x == 3.0)
        {
            float k = _SceneFogParams.x * d;
            f = exp2(-k * k);
        }
        return saturate(f);
    }

    // Path length below the fog top, weighted by how deep it runs: the
    // integral of (top - y) over the view segment, scaled by the density.
    float HeightFogDistance(float3 wsDelta)
    {
        float g0 = _HeightParams.y;              // camera height relative to the top
        float g1 = g0 + wsDelta.y;               // pixel height relative to the top
        float len = length(wsDelta);
        float depthSum;
        if (g0 <= 0.0 && g1 <= 0.0)
            depthSum = -(g0 + g1) * 0.5;         // whole segment inside the fog
        else if (g0 >= 0.0 && g1 >= 0.0)
            depthSum = 0.0;                      // whole segment above it
        else
        {
            float gin = min(g0, g1);             // the end that is inside
            depthSum = (gin * gin) / (2.0 * max(abs(g1 - g0), 1e-5));
        }
        return len * depthSum * _HeightParams.w;
    }

    float4 ComputeFog(v2fFog i, bool useDistance, bool useHeight)
    {
        float4 sceneColor = tex2D(_MainTex, i.uv);
        float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uvDepth);
        float dpth = Linear01Depth(rawDepth);
        float3 wsDelta = dpth * i.ray;

        float g = 0.0;
        if (useDistance)
        {
            float dist = (_SceneFogMode.y > 0.5) ? length(wsDelta) : dpth * _ProjectionParams.z;
            g += max(dist + _DistanceParams.x, 0.0);
        }
        if (useHeight)
            g += HeightFogDistance(wsDelta);

        float fogFac = FogTransmittance(g);
        // Leave the sky / clear colour alone when asked to.
        if (dpth >= 0.9999 && _DistanceParams.y < 1.5)
            fogFac = 1.0;
        return float4(lerp(unity_FogColor.rgb, sceneColor.rgb, fogFac), sceneColor.a);
    }

    float4 fragBoth(v2fFog i) : SV_Target { return ComputeFog(i, true, true); }
    float4 fragDistance(v2fFog i) : SV_Target { return ComputeFog(i, true, false); }
    float4 fragHeight(v2fFog i) : SV_Target { return ComputeFog(i, false, true); }
    ENDCG

    Pass {
        CGPROGRAM
        #pragma vertex vertFog
        #pragma fragment fragBoth
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex vertFog
        #pragma fragment fragDistance
        #pragma target 3.0
        ENDCG
    }
    Pass {
        CGPROGRAM
        #pragma vertex vertFog
        #pragma fragment fragHeight
        #pragma target 3.0
        ENDCG
    }
}
Fallback Off
}
