Shader "PARKSIDE/Jeju/Ocean"
{
    Properties { _Color ("Water", Color) = (.025,.26,.32,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        fixed4 _Color;
        struct Input { float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz;
            float t = _Time.y;
            float a = sin(p.x*.25+p.y*.18+t*.6);
            float b = sin(p.x*.12-p.y*.29+t*.4);
            float ripple = a*b;
            o.Normal = normalize(float3(a*.10,b*.09,1));
            o.Albedo = _Color.rgb * (.88+ripple*.12);
            o.Smoothness = .84;
            o.Metallic = .12;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
