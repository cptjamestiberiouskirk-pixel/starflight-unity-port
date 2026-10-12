// the glow of the player ship's engines: a soft round glow drawn on a flat quad at the exhaust (PlayerShip places it and sets how strong it is)
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Engine Glow"
{
	Properties
	{
		_Color( "Colour", Color ) = ( 0.45, 0.65, 1.0, 1.0 )
		_Strength( "Strength", Float ) = 1
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

		Pass
		{
			Blend One One
			ZWrite Off
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			fixed4 _Color;
			float _Strength;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			v2f vert( appdata v )
			{
				v2f o;
				o.vertex = UnityObjectToClipPos( v.vertex );
				o.uv = v.uv;
				return o;
			}

			fixed4 frag( v2f i ) : SV_Target
			{
				// brightest in the middle, nothing at the edge of the quad
				float distance = length( i.uv - 0.5 ) * 2.0;
				float glow = exp( -distance * distance * 7.0 ) * ( 1.0 - smoothstep( 0.75, 1.0, distance ) );

				// a white-hot core
				float core = exp( -distance * distance * 60.0 );

				float3 color = _Color.rgb * glow + float3( 1.0, 1.0, 1.0 ) * core * 0.6;

				return fixed4( color * _Strength, 0.0 );
			}
			ENDCG
		}
	}
}
