Shader "Starflight/Atmosphere"
{
    Properties
    {
        _Color ("Tint", Color) = (0.5, 0.7, 1.0, 1)
        _Power ("Rim Power", Range(0.5, 8.0)) = 3.0
        _Strength ("Strength", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert alpha:fade

        fixed4 _Color;
        float _Power;
        float _Strength;

        struct Input
        {
            float3 viewDir;
            float3 worldNormal;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            // Calculate dot product of view direction and world normal
            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), normalize(IN.worldNormal)));
            
            // Raise it to power to control thinness
            rim = pow(rim, _Power);

            // Emission = Color * Rim * Strength
            o.Emission = _Color.rgb * rim * _Strength;
            
            // Alpha = Rim (Clamped for stability)
            o.Alpha = saturate(rim);
        }
        ENDCG
    }
    FallBack "Transparent/VertexLit"
}
