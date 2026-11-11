Shader "PARKSIDE/Paris/LimestoneFacade"
{
    Properties
    {
        _MainTex("Limestone", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; fixed4 color : COLOR; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 m=IN.uv_MainTex;
            float2 cell=frac(m);
            float2 bay=floor(m);
            float shade=frac(sin(dot(bay,float2(37.1,18.9)))*43758.54);
            float x=abs(cell.x-.5);
            float top=.79-.34*x*x;
            float opening=step(x,.249)*step(.21,cell.y)*step(cell.y,top);
            float surround=step(x,.300)*step(.17,cell.y)*step(cell.y,top+.056);
            float jamb=step(x,.233)*step(.231,cell.y)*step(cell.y,top-.021);
            float crossbar=max(step(x,.015),step(abs(cell.y-.57),.015));
            float courses=frac(m.y*5.5);
            float joint=max(1-smoothstep(.012,.036,courses),
                (1-smoothstep(.011,.038,frac(m.x*1.32+floor(m.y*5.5)*.53)))*.82);
            float base=1-smoothstep(.35,1.07,m.y);
            float3 grain=tex2D(_MainTex,m*float2(.63,.55)).rgb;
            float3 limestone=lerp(grain,1,.23)*IN.color.rgb*(1-joint*(.12+base*.055));
            limestone*=.97+.05*shade-.14*base;
            limestone=lerp(limestone,limestone*.76,surround);
            float curtainMask=smoothstep(.13,.19,x)*step(.22,shade);
            float folds=.82+.18*cos(cell.x*168+shade*12);
            float3 panes=lerp(float3(.14,.18,.19),float3(.37,.43,.43),shade);
            panes=lerp(panes,float3(.54,.52,.47)*folds,curtainMask*.76);
            panes+=smoothstep(.49,.76,cell.y)*float3(.08,.10,.12);
            panes=lerp(panes,limestone*.75,max(crossbar,1-jamb));
            o.Albedo=lerp(limestone,panes,opening);
            o.Smoothness=lerp(.14,.58,opening*jamb*(1-crossbar));
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
