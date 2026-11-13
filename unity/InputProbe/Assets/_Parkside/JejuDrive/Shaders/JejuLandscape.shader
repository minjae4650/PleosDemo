Shader "PARKSIDE/Jeju/Landscape"
{
    Properties
    {
        _MainTex ("Ground detail", 2D) = "white" {}
        _RockTex ("Rock detail", 2D) = "gray" {}
        _CanopyTex ("Distant forest",2D)="white"{}
        _SoilTex ("Forest floor",2D)="gray"{}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _RockTex, _CanopyTex, _SoilTex;
        fixed4 _Color;
        struct Input { float3 worldPos; float3 worldNormal; float4 color : COLOR; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 detail = tex2D(_MainTex, IN.worldPos.xz * .075).rgb;
            float rock = saturate((1 - IN.worldNormal.y - .25) * 3.0);
            float3 stone = tex2D(_RockTex, IN.worldPos.xz * .08).rgb * .52;
            float macro = .89 + .11 * sin(IN.worldPos.x * .013 + sin(IN.worldPos.z*.008)) * sin(IN.worldPos.z * .014);
            float3 naturalDetail = lerp(float3(.72,.82,.55), detail * 1.55, .58);
            o.Albedo = lerp(IN.color.rgb * naturalDetail * macro, stone, rock) * _Color.rgb;
            float distanceToViewer=length(_WorldSpaceCameraPos.xz-IN.worldPos.xz);
            float forest=saturate(IN.color.a);
            float3 floor=tex2D(_SoilTex,IN.worldPos.xz*.11).rgb*.58;
            o.Albedo=lerp(o.Albedo,floor,forest*.48*(1-smoothstep(40,160,distanceToViewer)));
            float3 canopy=tex2D(_CanopyTex,IN.worldPos.xz*.0075).rgb*.78;
            o.Albedo=lerp(o.Albedo,canopy,forest*smoothstep(90,420,distanceToViewer)*.94);
            o.Smoothness = .05;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
