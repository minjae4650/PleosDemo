// Asphalt for the Busan drive slice (world XZ). Three layers keep it from reading as a tiled photo:
// the 6 m asphalt diffuse and normal map, a second sampling of the diffuse at another scale
// (breaks repetition), and the 96 m seamless road_macro texture (R brightness: blotches, repair patches,
// cracks, oil; G smoothness; B crack mask). Detail and normal fade with distance so nothing shimmers.
Shader "BusanEnvironment/SliceRoad"
{
    Properties
    {
        _MainTex ("Asphalt diffuse (world XZ)", 2D) = "gray" {}
        _BumpMap ("Asphalt normal (GL)", 2D) = "bump" {}
        _Macro ("Macro variation (96 m)", 2D) = "gray" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Tile ("Detail tile (m)", Float) = 6
        _MacroTile ("Macro tile (m)", Float) = 96
        _MacroStrength ("Macro strength", Range(0, 2.5)) = 1
        _BumpScale ("Normal strength", Range(0, 2)) = 0.8
        _MacroNear ("Close-range patches (9 m macro)", Range(0, 2)) = 0
        _Smooth ("Smoothness", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _Macro;
        fixed4 _Tint;
        half _Tile, _MacroTile, _MacroStrength, _BumpScale, _Smooth, _MacroNear;
        struct Input { float3 wpos; };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
            // tangent frame of a flat road in world space: u = +x, v = +z (the meshes carry no tangents)
            v.tangent = float4(mul((float3x3)unity_WorldToObject, float3(1, 0, 0)), -1);
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float dist = distance(_WorldSpaceCameraPos, IN.wpos);
            float2 uv = IN.wpos.xz / _Tile;
            half3 d1 = tex2D(_MainTex, uv).rgb;
            half3 d2 = tex2D(_MainTex, uv * 0.2137 + float2(0.31, 0.17)).rgb;
            half far = saturate((dist - 35.0) / 140.0);
            half3 avg = half3(0.27, 0.275, 0.28);
            half3 detail = lerp(d1, avg, far);
            half var2 = lerp(0.85 + 0.3 * saturate(dot(d2, 0.3333) / 0.275 - 0.5), 1.0, far);
            half3 m = tex2D(_Macro, IN.wpos.xz / _MacroTile).rgb;
            // the same macro texture sampled at 9 m: repair patches and cracks become visible at close range (alleys)
            half3 mn = tex2D(_Macro, IN.wpos.xz / 9.0 + 0.37).rgb;
            half nearFade = 1.0 - saturate((dist - 25.0) / 60.0);
            half bright = 1.0 + (m.r - 0.5) * 2.0 * _MacroStrength + (mn.r - 0.5) * 2.0 * _MacroNear * nearFade - mn.b * 0.14 * _MacroNear * nearFade;   // the 9 m tile magnifies the crack mask: at full strength the cracks read as thick black lines
            o.Albedo = detail * var2 * saturate(bright) * _Tint.rgb;
            half3 n = UnpackNormal(tex2D(_BumpMap, uv));
            half fade = 1.0 - saturate((dist - 12.0) / 50.0);
            n.xy *= _BumpScale * fade;
            o.Normal = normalize(half3(n.xy, n.z));
            o.Metallic = 0;
            o.Smoothness = saturate(_Smooth + (m.g - 0.5) * 0.9 * _MacroStrength);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
