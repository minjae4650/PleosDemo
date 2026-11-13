// Walls, glass, frames, signs and street furniture of the Gwangalli scene: mesh UVs in metres, a tint per vertex (one material
// serves every building colour), an optional GL normal map and smoothness from the texture's alpha (distant window facades).
Shader "BusanEnvironment/BlockoutSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Smooth ("Smoothness", Range(0, 1)) = 0.1
        _Metal ("Metallic", Range(0, 1)) = 0
        _BumpScale ("Normal strength", Range(0, 2)) = 1
        _FlipG ("GL normal map (flip green)", Float) = 1
        _SmoothFromAlpha ("Smoothness from texture alpha", Float) = 0
        _Grime ("Weathering (rain streaks, blotches)", Range(0, 1)) = 0
        _Emit ("Self-lit share of the colour (lit signs)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap;
        fixed4 _Color;
        half _Smooth, _Metal, _BumpScale, _FlipG, _SmoothFromAlpha, _Grime, _Emit;
        struct Input { float2 uv_MainTex; float4 color : COLOR; float3 worldPos; };
        float h21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float vn (float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
        }
        float fbm (float2 p) { return (vn(p) * 0.5 + vn(p * 2.03 + 7.1) * 0.25 + vn(p * 4.01 + 3.3) * 0.125 + vn(p * 8.1 + 1.7) * 0.0625) / 0.9375; }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            half4 t = tex2D(_MainTex, IN.uv_MainTex);
            // weathering in world space: streaks that run down a wall (stretched along the height) and broad blotches.
            // A clean, flat colour was what made the walls read as a model.
            float along = (IN.worldPos.x + IN.worldPos.z) * 0.7;
            half streak = smoothstep(0.48, 0.85, fbm(float2(along * 2.4, IN.worldPos.y * 0.2)));
            half blot = fbm(float2(along * 0.33, IN.worldPos.y * 0.37) + 7.3) - 0.5;
            t.rgb *= 1.0 - _Grime * saturate(streak * 0.5 + blot * 0.55) * 0.5;
            o.Albedo = t.rgb * _Color.rgb * IN.color.rgb;
            o.Emission = o.Albedo * _Emit;      // a lit sign keeps its colour on the side turned away from the sun
            half3 n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.y *= lerp(1.0, -1.0, _FlipG);
            n.xy *= _BumpScale;
            o.Normal = normalize(n);
            o.Metallic = _Metal * lerp(1.0, t.a, _SmoothFromAlpha);
            o.Smoothness = _Smooth * lerp(1.0, t.a, _SmoothFromAlpha);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
