// a planet on the system map: the planet's own albedo map on a sphere, lit from the star (a day side and a night side), with the
// darkening of the limb and an atmosphere on the lit rim
// (SystemDisplay sets the light direction every frame: the image is kept upright, so the star is in the plane of the map)
Shader "Custom/UI/System Map Planet"
{
	Properties
	{
		[PerRendererData] _MainTex( "Sprite Texture", 2D ) = "white" {}

		_PlanetTex( "Albedo Map", 2D ) = "grey" {}

		_Color( "Tint", Color ) = ( 1, 1, 1, 1 )

		_Spin( "Spin (turns)", Float ) = 0
		_LightDirection( "Light Direction (x, y in the map)", Vector ) = ( 0, -1, 0, 0 )
		_LightColor( "Light Color", Color ) = ( 1, 1, 1, 1 )
		_Ambient( "Ambient", Color ) = ( 0.022, 0.025, 0.035, 1 )
		_AtmosphereColor( "Atmosphere Color (alpha is the strength)", Color ) = ( 0, 0, 0, 0 )

		_StencilComp( "Stencil Comparison", Float ) = 8
		_Stencil( "Stencil ID", Float ) = 0
		_StencilOp( "Stencil Operation", Float ) = 0
		_StencilWriteMask( "Stencil Write Mask", Float ) = 255
		_StencilReadMask( "Stencil Read Mask", Float ) = 255

		_ColorMask( "Color Mask", Float ) = 15

		[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip( "Use Alpha Clip", Float ) = 0
	}

	SubShader
	{
		Tags
		{
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
			"PreviewType"="Plane"
			"CanUseSpriteAtlas"="True"
		}

		Stencil
		{
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		Blend SrcAlpha OneMinusSrcAlpha
		ColorMask [_ColorMask]

		Pass
		{
			Name "Default"

			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma target 3.0

				#include "UnityCG.cginc"
				#include "UnityUI.cginc"

				#pragma multi_compile __ UNITY_UI_CLIP_RECT
				#pragma multi_compile __ UNITY_UI_ALPHACLIP

				struct appdata_t
				{
					float4 vertex: POSITION;
					float4 color: COLOR;
					float2 texcoord: TEXCOORD0;
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f
				{
					float4 vertex: SV_POSITION;
					fixed4 color: COLOR;
					float2 texcoord: TEXCOORD0;
					float4 worldPosition: TEXCOORD1;
					UNITY_VERTEX_OUTPUT_STEREO
				};

				sampler2D _PlanetTex;

				fixed4 _Color;

				float _Spin;
				float4 _LightDirection;
				fixed4 _LightColor;
				fixed4 _Ambient;
				fixed4 _AtmosphereColor;

				float4 _ClipRect;

				v2f vert( appdata_t v )
				{
					v2f OUT;

					UNITY_SETUP_INSTANCE_ID( v );
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( OUT );

					OUT.worldPosition = v.vertex;
					OUT.vertex = UnityObjectToClipPos( OUT.worldPosition );
					OUT.texcoord = v.texcoord;
					OUT.color = v.color * _Color;

					return OUT;
				}

				fixed4 frag( v2f IN ) : SV_Target
				{
					// where this pixel is on the disc of the image (-1 to 1 across, y up)
					float2 disc = IN.texcoord * 2.0 - 1.0;
					float distance = length( disc );

					// the edge of the disc, anti-aliased over one pixel
					float edge = saturate( ( 1.0 - distance ) / max( fwidth( distance ), 0.0001 ) + 0.5 );

					// the very edge of the disc takes the rim of the sphere
					float discSquared = min( dot( disc, disc ), 0.9999 );
					disc *= ( distance > 0.99995 ) ? 0.99995 / distance : 1.0;

					// the normal of the sphere, facing the viewer along +z (the planet's north pole is up)
					float3 normal = float3( disc, sqrt( 1.0 - discSquared ) );

					// latitude and longitude of that point on the planet
					float latitude = asin( clamp( normal.y, -1.0, 1.0 ) );
					float longitude = atan2( normal.x, normal.z );

					// the map coordinates (the maps run west to east and south to north, and the planet turns to the east)
					float2 mapCoordinates = float2( longitude * ( 0.5 / UNITY_PI ) + 0.5 - _Spin, latitude / UNITY_PI + 0.5 );

					// the gradients of the map coordinates, without the jump where the longitude wraps around (or that seam would take the smallest mip)
					float2 dx = ddx( mapCoordinates );
					float2 dy = ddy( mapCoordinates );
					dx.x -= round( dx.x );
					dy.x -= round( dy.x );

					float3 albedo = tex2Dgrad( _PlanetTex, mapCoordinates, dx, dy ).rgb;

					// the star is in the plane of the map
					float3 lightDirection = normalize( float3( _LightDirection.xy, 0.0 ) + float3( 0.0, 0.0, 0.0001 ) );
					float normalDotLight = dot( normal, lightDirection );

					// a soft terminator, and a dim night side that still reads
					float diffuse = pow( saturate( ( normalDotLight + 0.22 ) / 1.22 ), 1.6 );

					float3 planet = albedo * ( _LightColor.rgb * diffuse * 1.5 + _Ambient.rgb );

					// the limb is a little darker
					float facing = 1.0 - normal.z;

					planet *= 1.0 - 0.35 * facing * facing;

					// the atmosphere: a coloured rim on the day side
					float day = saturate( ( normalDotLight + 0.35 ) / 1.35 );
					float rim = facing * facing * facing * day * 0.8;

					planet += _AtmosphereColor.rgb * rim * _AtmosphereColor.a;

					half4 color;

					color.rgb = planet * IN.color.rgb;
					color.a = edge * IN.color.a;

					#ifdef UNITY_UI_CLIP_RECT
					color.a *= UnityGet2DClipping( IN.worldPosition.xy, _ClipRect );
					#endif

					#ifdef UNITY_UI_ALPHACLIP
					clip( color.a - 0.001 );
					#endif

					return color;
				}

			ENDCG
		}
	}
}
