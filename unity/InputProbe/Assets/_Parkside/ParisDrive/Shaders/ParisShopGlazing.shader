Shader "PARKSIDE/Paris/ShopGlazing"
{
    Properties
    {
        _Color ("Glass tint", Color)=(.55,.67,.70,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color;
            struct Interpolators
            {
                float4 position : SV_POSITION;
                float3 world : TEXCOORD0;
                half3 normal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            Interpolators vert(appdata_base v)
            {
                Interpolators o;
                o.position=UnityObjectToClipPos(v.vertex);
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o,o.position);
                return o;
            }

            fixed4 frag(Interpolators i) : SV_Target
            {
                half3 normal=normalize(i.normal);
                half3 view=normalize(_WorldSpaceCameraPos-i.world);
                half fresnel=pow(1-saturate(dot(view,normal)),4);
                half reflectedSky=saturate(.5h+.75h*reflect(-view,normal).y);
                fixed4 color=fixed4(lerp(_Color.rgb*.71h,_Color.rgb,reflectedSky),.16h+.48h*fresnel);
                UNITY_APPLY_FOG(i.fogCoord,color);
                return color;
            }
            ENDCG
        }
    }
}
