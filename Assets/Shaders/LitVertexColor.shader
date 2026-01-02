Shader "Starflight/LitVertexColor" {
    Properties {
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Blend One Zero // Explicitly set to opaque
        LOD 200
        
        CGPROGRAM
        // Standard lighting model, enable shadows, use 'vert' function to grab colors
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        struct Input {
            float4 vertColor;
        };

        half _Glossiness;
        half _Metallic;

        void vert (inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertColor = v.color; // Grab the color our code generated
        }

        void surf (Input IN, inout SurfaceOutputStandard o) {
            o.Albedo = IN.vertColor.rgb; // Paint the pixel
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1.0; // --- FIX: Force Opaque ---
        }
        ENDCG
    }
    FallBack "Diffuse"
}