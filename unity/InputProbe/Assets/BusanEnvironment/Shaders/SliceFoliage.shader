// Leaf cards (cutout). The card texture already carries baked light and shade, so the card is lit with a sky-biased normal
// (set in the mesh) and the albedo is darkened with a gamma and multiplied by a per-tree vertex colour (hue / value variation).
// _Mask = 1 renders flat magenta: used by the capture tool to measure foliage pixels.
Shader "BusanEnvironment/SliceFoliage"
{
    Properties
    {
        _MainTex ("Card", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Cutoff", Range(0, 1)) = 0.45
        _Darken ("Gamma darken", Range(0.5, 2.5)) = 1.3
        _Sat ("Saturation", Range(0, 3)) = 1.9
        _Mask ("Mask (tools)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows alphatest:_Cutoff addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Darken, _Mask, _Sat;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            half3 lin = pow(max(c.rgb, 0.0001), _Darken);
            lin = lerp(dot(lin, half3(0.3, 0.59, 0.11)).xxx, lin, _Sat);
            o.Albedo = saturate(lin) * IN.color.rgb * _Color.rgb;
            o.Alpha = c.a;
            o.Metallic = 0;
            o.Smoothness = 0.04;
            if (_Mask > 0.5) { o.Albedo = 0; o.Emission = half3(1, 0, 1); }
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/VertexLit"
}
