Shader "PARKSIDE/Jeju/CoastFoam"
{
    Properties { _Color("Foam",Color)=(.86,.94,.92,.65) _MainTex("Foam field",2D)="white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off Cull Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        fixed4 _Color;
        struct Input { float2 uv_MainTex; float3 worldPos; };
        void surf(Input i,inout SurfaceOutputStandard o)
        {
            float n=sin(i.worldPos.x*2.6+sin(i.worldPos.z*1.7)+_Time.y*.6)*.5+.5;
            float edge=sin(saturate(i.uv_MainTex.y)*3.14159);
            o.Albedo=_Color.rgb;o.Alpha=_Color.a*edge*lerp(.28,.95,n);o.Smoothness=.25;
            o.Emission=_Color.rgb*.06;
        }
        ENDCG
    }
    Fallback "Transparent/Diffuse"
}
