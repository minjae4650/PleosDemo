// Cars baked into one mesh: the vertex colour carries each part's own albedo (rgb) and a paint mask (a),
// uv2 carries smoothness and metallic. Paint parts take the per-material paint colour instead, so seven paint
// materials colour every model and instancing batches all cars of one model and colour into one draw call.
Shader "BusanEnvironment/CarBaked"
{
    Properties
    {
        _PaintColor ("Paint colour", Color) = (0.9, 0.9, 0.9, 1)
        _PaintSmoothness ("Paint smoothness", Range(0, 1)) = 0.74
        _PaintMetallic ("Paint metallic", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma multi_compile_instancing
        #pragma target 3.0
        fixed4 _PaintColor;
        half _PaintSmoothness;
        half _PaintMetallic;
        struct Input { float4 color : COLOR; half2 sm; };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.sm = v.texcoord1.xy;
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            half mask = IN.color.a;
            o.Albedo = lerp(IN.color.rgb, _PaintColor.rgb, mask);
            o.Smoothness = lerp(IN.sm.x, _PaintSmoothness, mask);
            o.Metallic = lerp(IN.sm.y, _PaintMetallic, mask);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
