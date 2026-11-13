Shader "PARKSIDE/Paris/BuildingStone"
{
    Properties
    {
        _MainTex("Stone", 2D)="white" {}
        _Color("Tint", Color)=(1,1,1,1)
        _Glossiness("Smoothness", Range(0,1))=.16
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            fixed4 color : COLOR;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 position=IN.worldPos;
            float3 weights=pow(abs(IN.worldNormal),6);
            weights/=max(.001,weights.x+weights.y+weights.z);
            float3 stone=tex2D(_MainTex,position.zy*.48).rgb*weights.x
                        +tex2D(_MainTex,position.xz*.48).rgb*weights.y
                        +tex2D(_MainTex,position.xy*.48).rgb*weights.z;
            float course=frac(position.y*2.3);
            float grout=(1-smoothstep(.009,.035,course))*(1-weights.y);
            float groundWeathering=.91+.09*smoothstep(.20,2.45,position.y);
            o.Albedo=lerp(.70,1,stone)*IN.color.rgb*_Color.rgb
                *(1-.105*grout)*groundWeathering;
            o.Smoothness=_Glossiness;
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
