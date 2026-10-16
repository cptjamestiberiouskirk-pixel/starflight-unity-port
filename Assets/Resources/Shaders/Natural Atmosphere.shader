// the halo of a planet's air in its natural look, drawn on the atmosphere's quad just above the planet (Planet.FitAtmosphere places it): each pixel
// follows its line of sight through the shell of air round the planet and adds the sunlight the air scatters towards the camera, dimming what lies
// behind by the light lost on the way; where the planet's own natural shader has been drawn (it marks the stencil) it adds nothing, since that shader
// works out the air in front of the ground itself
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Natural Atmosphere"
{
	Properties
	{
		_PlanetRadius( "Planet Radius (world)", Float ) = 1
		_CenterDepth( "Planet Centre Below The Quad (world)", Float ) = 1
		_AirColor( "Air Colour", Color ) = ( 0.35, 0.55, 1, 1 )
		_AirTint( "Share Of The Air Colour", Float ) = 0
		_AirDensity( "Air Density", Float ) = 1
		_AirHeight( "Air Height (planet radii)", Float ) = 0.06
		_AirScaleHeight( "Scale Height (share of the air's height)", Float ) = 0.15
		_AirLight( "Sunlight In The Air", Float ) = 14
	}

	SubShader
	{
		Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

		Pass
		{
			Tags { "LightMode" = "ForwardBase" }

			Blend One OneMinusSrcAlpha
			ZWrite Off
			ZTest LEqual
			Cull Off

			// not over the planet's natural surface (Natural Planet sets this bit)
			Stencil
			{
				Ref 64
				ReadMask 64
				Comp NotEqual
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#include "UnityCG.cginc"
			#include "Natural Planet.cginc"

			float _PlanetRadius;
			float _CenterDepth;
			float4 _AirColor;
			float _AirTint;
			float _AirDensity;
			float _AirHeight;
			float _AirScaleHeight;
			float _AirLight;
			float4 _LightColor0;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float3 worldPos : TEXCOORD0;
			};

			v2f vert( float4 vertex : POSITION )
			{
				v2f o;

				o.pos = UnityObjectToClipPos( vertex );
				o.worldPos = mul( unity_ObjectToWorld, vertex ).xyz;

				return o;
			}

			float4 frag( v2f i ) : SV_Target
			{
				// the middle of the planet, straight below the quad
				float3 quadPosition = float3( unity_ObjectToWorld[ 0 ][ 3 ], unity_ObjectToWorld[ 1 ][ 3 ], unity_ObjectToWorld[ 2 ][ 3 ] );
				float3 center = quadPosition - float3( 0.0, _CenterDepth, 0.0 );
				float radius = max( _PlanetRadius, 1e-3 );

				// the line of sight, in planet radii round the planet's middle
				float3 origin = ( _WorldSpaceCameraPos - center ) / radius;
				float3 direction = normalize( i.worldPos - _WorldSpaceCameraPos );
				float3 light = NP_LightDirection( center );

				NP_Air air = NP_MakeAir( _AirHeight, _AirScaleHeight, _AirColor.rgb, _AirTint, _AirDensity );

				float2 shell = NP_RaySphere( origin, direction, air.top );

				if ( ( shell.y < shell.x ) || ( shell.y < 0.0 ) )
				{
					return 0;
				}

				// through the air to the far side of the shell, or to the ground (a sliver at the edge of the planet's mesh, which is a little inside the sphere)
				float start = max( shell.x, 0.0 );
				float end = shell.y;
				float2 ground = NP_RaySphere( origin, direction, 1.0 );

				if ( ( ground.x < ground.y ) && ( ground.x > 0.0 ) )
				{
					end = min( end, ground.x );
				}

				float3 inscatter;
				float3 transmittance;

				NP_Scatter( air, origin, direction, start, end, light, _AirLight * dot( _LightColor0.rgb, 0.3333 ), inscatter, transmittance );

				return float4( NP_ToneMap( inscatter ), saturate( 1.0 - dot( transmittance, 0.3333 ) ) );
			}
			ENDCG
		}
	}
}
