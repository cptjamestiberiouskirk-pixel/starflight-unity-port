// a planet's atmosphere, drawn on a flat quad just above the planet, facing the camera (Planet.FitAtmosphere places it)
// for each pixel it works out how close the line of sight passes to the middle of the planet: outside the planet that gives a halo that fades to nothing,
// over the planet a haze that thickens towards its edge; both are lit by the sun (the main directional light), with a brighter fringe where day turns to night
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Planet Atmosphere"
{
	Properties
	{
		SF_AlbedoColor( "Atmosphere Colour", Color ) = ( 0.5, 0.7, 1.0, 1.0 )
		_AtmosphereStrength( "Strength", Range( 0, 2 ) ) = 1
		_PlanetRadius( "Planet Radius (world)", Float ) = 1
		_AtmosphereHeight( "Atmosphere Height (world)", Float ) = 0.2
		_CenterDepth( "Planet Centre Below The Quad (world)", Float ) = 1
	}

	SubShader
	{
		Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

		Pass
		{
			// the main forward pass, so that the sun's direction is the one the planet is lit by
			Tags { "LightMode" = "ForwardBase" }

			Blend One One
			ZWrite Off
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			fixed4 SF_AlbedoColor;
			float _AtmosphereStrength;
			float _PlanetRadius;
			float _AtmosphereHeight;
			float _CenterDepth;

			struct appdata
			{
				float4 vertex : POSITION;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float3 worldPosition : TEXCOORD0;
			};

			v2f vert( appdata v )
			{
				v2f o;
				o.vertex = UnityObjectToClipPos( v.vertex );
				o.worldPosition = mul( unity_ObjectToWorld, v.vertex ).xyz;
				return o;
			}

			fixed4 frag( v2f i ) : SV_Target
			{
				// the middle of the planet, straight below the quad
				float3 quadPosition = float3( unity_ObjectToWorld[ 0 ][ 3 ], unity_ObjectToWorld[ 1 ][ 3 ], unity_ObjectToWorld[ 2 ][ 3 ] );
				float3 center = quadPosition - float3( 0.0, _CenterDepth, 0.0 );

				// the line of sight through this pixel, and the point on it closest to the middle of the planet
				float3 eye = _WorldSpaceCameraPos;
				float3 ray = normalize( i.worldPosition - eye );
				float along = dot( center - eye, ray );
				float3 closest = eye + ray * along;
				float distance = length( closest - center );

				// the direction from the middle of the planet to that point, and how much sunlight falls there
				float3 up = normalize( closest - center );
				float3 sun = normalize( _WorldSpaceLightPos0.xyz );
				float facing = dot( up, sun );
				float daylight = saturate( facing * 1.5 + 0.35 );

				// a brighter fringe where day turns to night (faint: with the sun overhead, as in the star system, every point of the edge is at that line)
				float twilight = exp( -facing * facing / 0.02 ) * 0.2;

				float glow;

				if ( distance >= _PlanetRadius )
				{
					// the halo: brightest at the planet's edge, fading to nothing at the top of the atmosphere
					float height = saturate( ( distance - _PlanetRadius ) / _AtmosphereHeight );
					glow = exp( -height * 4.0 ) * ( 1.0 - smoothstep( 0.6, 1.0, height ) );
				}
				else
				{
					// the haze over the planet: thin over the middle, thick towards the edge
					float across = distance / _PlanetRadius;
					glow = pow( across, 8.0 ) * 0.85;
				}

				float brightness = glow * ( daylight + twilight ) * _AtmosphereStrength;

				return fixed4( SF_AlbedoColor.rgb * brightness, 0.0 );
			}
			ENDCG
		}
	}
}
