Shader "PARKSIDE/Jeju/PlantSurface"
{
 Properties { _MainTex("Surface detail",2D)="white"{} _Color("Tint",Color)=(.35,.48,.22,1) }
 SubShader { Tags {"RenderType"="Opaque"} Cull Off
 CGPROGRAM
 #pragma surface surf Standard fullforwardshadows addshadow
 #pragma target 3.0
 sampler2D _MainTex;fixed4 _Color;
 struct Input {float2 uv_MainTex;float facing:VFACE;float4 color:COLOR;};
 void surf(Input IN,inout SurfaceOutputStandard o){float3 t=tex2D(_MainTex,IN.uv_MainTex).rgb;o.Albedo=_Color.rgb*IN.color.rgb*lerp(.65,1.25,t);o.Smoothness=.06;o.Normal=float3(0,0,IN.facing>=0?1:-1);o.Occlusion=.85;}
 ENDCG
 }Fallback "Diffuse"
}
