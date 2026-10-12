// the path of a planet's orbit in the 3D star system view: a faint ring round the star (Planet makes its mesh), brightest in a trail just behind the planet,
// so that the way the planets go round shows, as on the system map
// the mesh's u goes once round the orbit (the angle of GD_Planet.GetPosition, from 0 to 1), its v across the ring
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Orbit Path"
{
	Properties
	{
		_Color( "Colour", Color ) = ( 1, 1, 1, 1 )
		_PlanetAngle( "Planet Angle (radians)", Float ) = 0
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
			float _PlanetAngle;
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
				const float twoPi = 6.28318530718;

				// how far behind the planet this part of the orbit is (the planets go round to larger angles)
				float angle = i.uv.x * twoPi;
				float behind = fmod( _PlanetAngle - angle + 2.0 * twoPi, twoPi );

				// a faint ring, a trail that fades behind the planet, and a little ahead of it
				float trail = exp( -behind / 0.45 );
				float ahead = exp( -( twoPi - behind ) / 0.08 ) * 0.5;
				float brightness = 0.12 + 0.75 * trail + ahead;

				// soft across the ring
				float across = 1.0 - abs( i.uv.y * 2.0 - 1.0 );

				brightness *= smoothstep( 0.0, 1.0, across ) * _Strength;

				return fixed4( _Color.rgb * brightness, 0.0 );
			}
			ENDCG
		}
	}
}
