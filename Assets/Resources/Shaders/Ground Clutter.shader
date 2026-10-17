// the grass and the pebbles on the ground about the terrain vehicle (GroundClutter): instanced meshes, in the colour of the ground they stand on (sampled from
// the ground's own albedo map, so they match the planet in either look), lit by the sun and the sky, swaying in the wind (grass only), fogged as the ground is
Shader "Starflight/Ground Clutter"
{
	Properties
	{
		_GroundMap( "Ground Albedo Map", 2D ) = "gray" {}
		_GroundMapTransform( "World XZ to the ground map's UV (scale xy, offset zw)", Vector ) = ( 1, 1, 0, 0 )
		_Tint( "Tint", Color ) = ( 1, 1, 1, 1 )
		_Variation( "Brightness variation", Range( 0, 1 ) ) = 0.3
		_Desaturation( "Desaturation", Range( 0, 1 ) ) = 0
		_Sway( "Sway", Range( 0, 1 ) ) = 0
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

		Pass
		{
			Name "FORWARD"

			Tags { "LightMode" = "ForwardBase" }

			Cull Off

			CGPROGRAM

			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma multi_compile_fog
			#pragma multi_compile_fwdbase

			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "AutoLight.cginc"

			sampler2D _GroundMap;
			float4 _GroundMap_ST;
			float4 _GroundMapTransform;
			float4 _Tint;
			float _Variation;
			float _Desaturation;
			float _Sway;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float4 color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float3 normal : TEXCOORD0;
				float4 color : TEXCOORD1;
				float2 groundUV : TEXCOORD2;
				UNITY_FOG_COORDS( 3 )
				SHADOW_COORDS( 4 )
			};

			// a number from 0 to 1 for a place (each blade and pebble's own brightness)
			float hash( float2 p )
			{
				return frac( sin( dot( p, float2( 127.1, 311.7 ) ) ) * 43758.5453 );
			}

			v2f vert( appdata v )
			{
				v2f o;

				UNITY_SETUP_INSTANCE_ID( v );

				float4 positionWorld = mul( unity_ObjectToWorld, v.vertex );
				float3 rootWorld = mul( unity_ObjectToWorld, float4( 0, 0, 0, 1 ) ).xyz;

				// the grass sways from its root, more at the top (the vertex colour's alpha is how high up the blade it is)
				float swayPhase = _Time.y * 1.7 + rootWorld.x * 0.21 + rootWorld.z * 0.13;

				positionWorld.xz += float2( sin( swayPhase ), cos( swayPhase * 0.7 ) ) * _Sway * v.color.a;

				o.pos = mul( UNITY_MATRIX_VP, positionWorld );
				o.normal = UnityObjectToWorldNormal( v.normal );
				o.groundUV = rootWorld.xz * _GroundMapTransform.xy + _GroundMapTransform.zw;

				float brightness = 1 + ( hash( rootWorld.xz ) - 0.5 ) * 2 * _Variation;

				o.color = float4( v.color.rgb * brightness, 1 );

				UNITY_TRANSFER_FOG( o, o.pos );
				TRANSFER_SHADOW( o );

				return o;
			}

			float4 frag( v2f i ) : SV_Target
			{
				float3 ground = tex2D( _GroundMap, TRANSFORM_TEX( i.groundUV, _GroundMap ) ).rgb;

				// stones are greyer than the soil about them (the map's colour is the soil's)
				ground = lerp( ground, dot( ground, float3( 0.299, 0.587, 0.114 ) ), _Desaturation );
				float3 albedo = ground * _Tint.rgb * i.color.rgb;
				float3 normal = normalize( i.normal );

				// both faces of a blade are lit (the normal turned to the sun)
				float lambert = abs( dot( normal, _WorldSpaceLightPos0.xyz ) ) * 0.75 + 0.25 * saturate( dot( normal, _WorldSpaceLightPos0.xyz ) );
				float shadow = SHADOW_ATTENUATION( i );
				float3 colour = albedo * ( _LightColor0.rgb * lambert * shadow + ShadeSH9( float4( 0, 1, 0, 1 ) ) );

				UNITY_APPLY_FOG( i.fogCoord, colour );

				return float4( colour, 1 );
			}

			ENDCG
		}

		Pass
		{
			Name "SHADOWCASTER"

			Tags { "LightMode" = "ShadowCaster" }

			Cull Off

			CGPROGRAM

			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma multi_compile_shadowcaster

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				V2F_SHADOW_CASTER;
			};

			v2f vert( appdata v )
			{
				v2f o;

				UNITY_SETUP_INSTANCE_ID( v );
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )

				return o;
			}

			float4 frag( v2f i ) : SV_Target
			{
				SHADOW_CASTER_FRAGMENT( i )
			}

			ENDCG
		}
	}
}
