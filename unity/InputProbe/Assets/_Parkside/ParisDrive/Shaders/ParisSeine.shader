Shader "PARKSIDE/Paris/Seine"
{
    Properties
    {
        _Color("River tint", Color) = (.17,.32,.33,1)
        _Glossiness("Smoothness", Range(0,1)) = .54
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        fixed4 _Color;
        half _Glossiness;
        struct Input { float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p=IN.worldPos.xz;
            float a=dot(p,float2(.62,.21)) + .9*sin(dot(p,float2(.053,-.039))) + _Time.y*.83;
            float b=dot(p,float2(-.52,.93)) + .6*sin(dot(p,float2(.084,.072))) - _Time.y*.68;
            float c=dot(p,float2(.14,2.31)) + .5*sin(dot(p,float2(-.062,.082))) + _Time.y*1.26;
            float wide=sin(a)*.48+sin(b)*.27+sin(c)*.12;
            float2 normal=cos(a)*float2(.021,.011)+cos(b)*float2(-.013,.022)+cos(c)*float2(.007,.012);
            o.Normal=normalize(float3(normal,1));
            o.Albedo=_Color.rgb*(1+wide*.095);
            o.Metallic=.11;
            o.Smoothness=_Glossiness;
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
