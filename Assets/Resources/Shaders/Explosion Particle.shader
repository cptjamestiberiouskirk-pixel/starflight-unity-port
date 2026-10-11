// unlit particle shader for the explosions (ExplosionEffect)
// it is in a Resources folder so that it is always in a build: Shader.Find only finds a shader that something in the build refers to
// it blends premultiplied: the particle adds its light, and _Occlusion says how much of what is behind it the particle hides
// (0 is pure glow that only adds light, for the flash and the sparks; 1 is ordinary transparency, for the smoke and the debris; the fire is in between,
// so that it keeps its own colour over a bright planet and still glows over black space)
Shader "Starflight/Explosion Particle"
{
	Properties
	{
		_MainTex( "Texture", 2D ) = "white" {}
		_Occlusion( "Occlusion", Range( 0, 1 ) ) = 0
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

		Blend One OneMinusSrcAlpha
		ZWrite Off
		Cull Off
		Lighting Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed _Occlusion;

			struct appdata
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
			};

			v2f vert( appdata v )
			{
				v2f o;
				o.vertex = UnityObjectToClipPos( v.vertex );
				o.color = v.color;
				o.uv = TRANSFORM_TEX( v.uv, _MainTex );
				return o;
			}

			fixed4 frag( v2f i ) : SV_Target
			{
				// the texture gives the shape, the particle gives the colour
				fixed4 colour = tex2D( _MainTex, i.uv ) * i.color;

				// the light it adds, and how much of the background it hides
				return fixed4( colour.rgb * colour.a, colour.a * _Occlusion );
			}
			ENDCG
		}
	}
}
