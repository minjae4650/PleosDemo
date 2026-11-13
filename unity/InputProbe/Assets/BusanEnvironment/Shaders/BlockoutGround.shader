// Ground of the Gwangalli scene: asphalt, paving, sand, paint, the sea. Mapped in world space along the road direction (_Angle),
// so paving joints follow the street and the meshes need no UVs. A second, coarser sampling and two noise layers (large patches,
// stains) keep a 2 m tile from reading as a repeat; the normal map fades with distance so nothing shimmers.
Shader "BusanEnvironment/BlockoutGround"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal (GL)", 2D) = "bump" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Tile ("Tile (m)", Float) = 2
        _Angle ("Direction (radians)", Float) = 0
        _BumpScale ("Normal strength", Range(0, 2)) = 1
        _Smooth ("Smoothness", Range(0, 1)) = 0.1
        _Vary ("Large-scale variation", Range(0, 1)) = 0.3
        _Stain ("Stains", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap;
        fixed4 _Tint;
        half _Tile, _Angle, _BumpScale, _Smooth, _Vary, _Stain;
        struct Input { float3 wpos; };
        float h21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float vn (float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
        }
        float fbm (float2 p) { return (vn(p) * 0.5 + vn(p * 2.03 + 7.1) * 0.25 + vn(p * 4.01 + 3.3) * 0.125 + vn(p * 8.1 + 1.7) * 0.0625) / 0.9375; }
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
            // tangent frame of flat ground: u along the road, v across it (the meshes carry no tangents; -1 for GL normal maps)
            v.tangent = float4(mul((float3x3)unity_WorldToObject, float3(cos(_Angle), 0, sin(_Angle))), -1);
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.wpos.xz;
            float c = cos(_Angle), s = sin(_Angle);
            float2 uv = float2(p.x * c + p.y * s, -p.x * s + p.y * c) / _Tile;
            float dist = distance(_WorldSpaceCameraPos, IN.wpos);
            half3 a = tex2D(_MainTex, uv).rgb;
            half3 a2 = tex2D(_MainTex, uv * 0.173 + float2(0.31, 0.17)).rgb;
            half far = saturate((dist - 30.0) / 150.0);
            a = lerp(a, a2, 0.35 + 0.4 * far);                       // the fine tile dissolves into the coarse one with distance
            half patches = fbm(p / 11.0 + 3.7);
            a *= 1.0 + _Vary * (patches - 0.5) * 0.9;
            half stain = smoothstep(0.58, 0.82, fbm(p / 2.7 + 17.0));
            a *= 1.0 - _Stain * stain * 0.38;
            o.Albedo = a * _Tint.rgb;
            half3 n = UnpackNormal(tex2D(_BumpMap, uv));
            n.xy *= _BumpScale * (1.0 - saturate((dist - 14.0) / 70.0));
            o.Normal = normalize(n);
            o.Metallic = 0;
            o.Smoothness = saturate(_Smooth * (1.0 + (patches - 0.5) * 0.8 * _Vary) - stain * _Stain * 0.05);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
