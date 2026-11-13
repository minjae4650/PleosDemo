// Ground of the Busan drive slice. The vertex colour carries land-use WEIGHTS (r grass, g forest, b beach, a sea floor;
// whatever is left is urban), so the edges between land uses stay blended; the albedo itself is procedural:
// multi-scale grass with dry and dirt patches, forest floor, sand, and urban ground with paving joints, stains and gravel.
Shader "BusanEnvironment/SliceGround"
{
    Properties
    {
        _Detail ("Grass detail (world XZ)", 2D) = "gray" {}
        _Gloss ("Smoothness", Range(0, 1)) = 0.04
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        sampler2D _Detail;
        half _Gloss;
        struct Input { float4 color : COLOR; float3 wpos; float3 worldNormal; };
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
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.wpos.xz;
            float dist = distance(_WorldSpaceCameraPos, IN.wpos);
            float nearF = 1.0 - saturate((dist - 40.0) / 160.0);
            // ragged land-use edges: the weights are bilinear over 18 m cells, noise decides where the edge really is
            float jitter = (fbm(p / 11.0 + 5.0) - 0.5) * 0.7;
            float wg = smoothstep(0.38, 0.62, IN.color.r + jitter);
            float wf = smoothstep(0.38, 0.62, IN.color.g + jitter);
            float wb = smoothstep(0.38, 0.62, IN.color.b + jitter);
            float ws = smoothstep(0.38, 0.62, IN.color.a);
            // grass
            float big = fbm(p / 60.0), mid = fbm(p / 14.0 + 11.0), small = fbm(p / 3.0 + 4.0);
            half3 grass = lerp(half3(0.27, 0.34, 0.15), half3(0.36, 0.40, 0.19), big);
            grass = lerp(grass, half3(0.45, 0.42, 0.23), smoothstep(0.58, 0.8, fbm(p / 23.0 + 31.0)) * 0.7);
            grass = lerp(grass, half3(0.38, 0.30, 0.21), smoothstep(0.62, 0.8, fbm(p / 9.0 + 23.0)) * 0.9);
            grass *= (0.74 + 0.42 * mid + 0.22 * small + 0.12 * lerp(0.5, vn(p * 1.9), nearF)) * 0.80;
            half tex = dot(tex2D(_Detail, p * 0.23).rgb, 0.3333) / 0.35;
            grass *= lerp(1.0, 0.7 + 0.5 * tex, nearF);
            half3 wood = lerp(half3(0.11, 0.16, 0.075), half3(0.16, 0.19, 0.09), mid) * (0.8 + 0.4 * small);
            half3 sand = lerp(half3(0.27, 0.21, 0.13), half3(0.32, 0.25, 0.15), mid) * (0.9 + 0.2 * small);
            // urban ground: slab joints every 2.4 m (near only), stains, gravel, a little weed growth
            // a Korean block interior is mostly old asphalt (parking, yards) with concrete slabs and planted strips, not one bright slab
            // (the base was a bright slab, 0.30-0.36: beside a road with no paving of its own it read as a blank white plaza, and
            // the tan and green patches as smeared paint from above. Darker, closer to worn asphalt, with fainter patches.)
            half3 urban = lerp(half3(0.20, 0.20, 0.198), half3(0.245, 0.24, 0.232), big);
            float lot = smoothstep(0.36, 0.52, fbm(p / 38.0 + 17.0));
            half3 asphaltLot = lerp(half3(0.16, 0.163, 0.168), half3(0.20, 0.20, 0.205), mid);
            urban = lerp(urban, asphaltLot, lot * 0.85);
            float2 g = abs(frac(p / 2.4) - 0.5) * 2.4;
            float joint = (1.0 - smoothstep(0.0, 0.05, min(g.x, g.y))) * nearF * 0.18 * (1.0 - lot);
            urban *= 1.0 - joint;
            urban = lerp(urban, urban * 0.72, smoothstep(0.6, 0.78, fbm(p / 6.0 + 3.0)) * 0.8);
            urban = lerp(urban, half3(0.36, 0.33, 0.29), smoothstep(0.70, 0.86, fbm(p / 15.0 + 41.0)) * 0.35);
            urban = lerp(urban, half3(0.27, 0.31, 0.19), smoothstep(0.80, 0.92, fbm(p / 4.0 + 9.0)) * 0.35);
            urban = lerp(urban, half3(0.23, 0.28, 0.15), smoothstep(0.68, 0.80, fbm(p / 19.0 + 57.0)) * 0.4);   // planted strips and weedy corners
            urban *= 0.9 + 0.2 * small;
            // a bank between two road levels is planted, not paved: from about 10 degrees of slope the urban ground turns to
            // grass (it read as a tilted slab of paving beside the road)
            float bank = saturate((0.985 - IN.worldNormal.y) / 0.045);
            urban = lerp(urban, grass * 0.9, bank);
            half3 seafloor = half3(0.10, 0.17, 0.19);
            half3 c = lerp(urban, grass, wg);
            c = lerp(c, wood, wf);
            c = lerp(c, sand, wb);
            c = lerp(c, seafloor, ws);
            o.Albedo = c;
            o.Metallic = 0;
            o.Smoothness = _Gloss;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
