// Pavement: interlocking pavers (world XZ, 1.6 m tile, albedo + normal) on the top faces, plain concrete on the sides.
// The kerb face has its own material (Kerb mesh) so its normals can be biased towards the sky.
Shader "BusanEnvironment/SliceSidewalk"
{
    Properties
    {
        _MainTex ("Paving", 2D) = "gray" {}
        _BumpMap ("Paving normal", 2D) = "bump" {}
        _Macro ("Macro (stains)", 2D) = "gray" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _SideColor ("Side concrete", Color) = (0.62, 0.61, 0.58, 1)
        _Tile ("Paving tile (m)", Float) = 1.6
        _Smooth ("Smoothness", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _Macro;
        fixed4 _Tint, _SideColor;
        half _Tile, _Smooth;
        struct Input { float3 wpos; half top; };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.top = step(0.6, v.normal.y);
            v.tangent = float4(mul((float3x3)unity_WorldToObject, float3(1, 0, 0)), -1);
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float dist = distance(_WorldSpaceCameraPos, IN.wpos);
            half3 stain = tex2D(_Macro, IN.wpos.xz / 24.0).rgb;
            half st = 0.9 + (stain.r - 0.5) * 0.9 + (tex2D(_Macro, IN.wpos.xz / 5.3 + 0.37).r - 0.5) * 0.4;
            float2 uv = IN.wpos.xz / _Tile;
            half far = saturate((dist - 30.0) / 120.0);
            half3 paving = lerp(tex2D(_MainTex, uv).rgb, half3(0.62, 0.61, 0.58), far);
            half3 side = _SideColor.rgb * (0.9 + 0.2 * tex2D(_Macro, (IN.wpos.xz + IN.wpos.y) / 3.1).g);
            half3 albedo = lerp(side * st, paving * _Tint.rgb * st, IN.top);
            o.Albedo = saturate(albedo);
            half3 n = UnpackNormal(tex2D(_BumpMap, uv));
            n.xy *= (1.0 - saturate((dist - 8.0) / 35.0)) * IN.top;
            o.Normal = normalize(half3(n.xy, n.z));
            o.Metallic = 0;
            o.Smoothness = _Smooth;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
