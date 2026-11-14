// Traffic signal lamps: uv.x = head index, uv.y = lens (0 red, 1 yellow, 2 left arrow, 3 green, 4 always dark).
// The state of every head is one texel of the global _SignalStates (r red, g yellow, b arrow, a green), set by TrafficSignals;
// it is a global value (not a material property) so the material asset never holds a runtime texture.
Shader "BusanEnvironment/SignalLamp"
{
    Properties
    {
        _Glow ("Glow", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Standard vertex:vert
        #pragma target 3.0
        sampler2D _SignalStates;
        float _SignalCount;
        half _Glow;
        struct Input { float2 data; };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.data = v.texcoord.xy;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float head = IN.data.x;
            float kind = IN.data.y;
            fixed4 s = tex2D(_SignalStates, float2((head + 0.5) / max(_SignalCount, 1.0), 0.5));
            half on = kind < 0.5 ? s.r : kind < 1.5 ? s.g : kind < 2.5 ? s.b : kind < 3.5 ? s.a : 0;
            half3 lit = kind < 0.5 ? half3(1.0, 0.10, 0.06) : kind < 1.5 ? half3(1.0, 0.62, 0.05) : half3(0.10, 1.0, 0.55);
            half3 dark = kind < 0.5 ? half3(0.16, 0.03, 0.03) : kind < 1.5 ? half3(0.16, 0.11, 0.02) : kind < 3.5 ? half3(0.02, 0.10, 0.06) : half3(0.03, 0.03, 0.035);
            o.Albedo = dark;
            o.Emission = lit * on * _Glow;
            o.Smoothness = 0.85;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
