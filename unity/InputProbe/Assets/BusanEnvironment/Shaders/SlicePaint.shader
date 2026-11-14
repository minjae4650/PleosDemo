// Lane paint: flat colour with grain and worn-off patches so it never reads as a saturated decal.
// The polygon offset keeps it on the asphalt at distance, so the quads can sit almost on the surface.
Shader "BusanEnvironment/SlicePaint"
{
    Properties
    {
        _Color ("Paint", Color) = (0.8, 0.8, 0.78, 1)
        _Smooth ("Smoothness", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Offset -2, -2
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        fixed4 _Color;
        half _Smooth;
        struct Input { float3 wpos; };
        float h21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float vn (float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
        }
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.wpos.xz;
            float dist = distance(_WorldSpaceCameraPos, IN.wpos);
            float fine = lerp(vn(p * 11.0), 0.5, saturate((dist - 25.0) / 60.0));
            float patch = vn(p * 0.9 + 3.7);
            float worn = smoothstep(0.62, 0.8, patch) * 0.45;
            half3 c = _Color.rgb * (0.80 + 0.26 * fine);
            o.Albedo = lerp(c, half3(0.20, 0.20, 0.21), worn);
            o.Metallic = 0;
            o.Smoothness = _Smooth;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
