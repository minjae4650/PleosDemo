// Trees as crossed picture cards (tools/districts/trees.py, baked by BusanLandmarkTrees): cut out by the picture's alpha, seen
// from both sides, casting shadows through the same cut-out. The light comes mostly from above (a card's own normal would make
// it flash bright or dark as the car turns past it) and the side away from the sun is lit through the leaves a little.
Shader "BusanEnvironment/FoliageCards"
{
    Properties
    {
        _MainTex ("Picture (alpha = leaves)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cut-out", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 200
        Cull Off
        CGPROGRAM
        #pragma surface surf WrapLambert alphatest:_Cutoff addshadow vertex:vert
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        void vert (inout appdata_full v)
        {
            // the normal tilted toward the sky: cards are lit like a crown, not like a flat board
            v.normal = normalize(v.normal * 0.35 + float3(0, 1, 0));
        }
        half4 LightingWrapLambert (SurfaceOutput s, half3 lightDir, half atten)
        {
            half d = dot(s.Normal, lightDir) * 0.5 + 0.5;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (d * atten * 0.85 + 0.15);
            c.a = s.Alpha;
            return c;
        }
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = t.rgb * _Color.rgb * IN.color.rgb;
            o.Alpha = t.a;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
