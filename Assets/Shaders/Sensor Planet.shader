// the picture of a planet in the sensor window: the planet's own maps on a lit sphere that turns slowly, inside the disc of the planet mask
// (while the scan runs the sensor noise of the masked image shader fades into the planet; _Reveal goes from 0 to 1), with the marks of the science
// officer's survey pulsing on it once the planet has been analysed (SurveyMarks; _SurveyStrength 0 without them)
Shader "Custom/UI/Sensor Planet"
{
	Properties
	{
		[PerRendererData] _MainTex( "Noise Texture", 2D ) = "white" {}
		_MaskTex( "Mask Texture", 2D ) = "white" {}

		_PlanetTex( "Albedo Map", 2D ) = "black" {}
		_WaterTex( "Water Mask Map", 2D ) = "black" {}
		_NormalTex( "Normal Map", 2D ) = "bump" {}

		_Color( "Tint", Color ) = ( 1, 1, 1, 1 )

		_Reveal( "Reveal", Range( 0, 1 ) ) = 1
		_Spin( "Spin (turns)", Float ) = 0
		_Tilt( "Tilt toward the viewer (radians)", Float ) = 0.25
		_DiscRadius( "Disc Radius (u, v)", Vector ) = ( 0.24463, 0.48926, 0, 0 )
		_LightDirection( "Light Direction", Vector ) = ( -0.55, 0.45, 0.7, 0 )
		_Ambient( "Ambient", Range( 0, 1 ) ) = 0.06
		_Relief( "Relief", Range( 0, 2 ) ) = 1
		_WaterGloss( "Water Gloss", Range( 0, 2 ) ) = 0.6
		_AtmosphereColor( "Atmosphere Color", Color ) = ( 0, 0, 0, 1 )

		_SurveyTex( "Survey Marks", 2D ) = "white" {}
		_SurveyStrength( "Survey Marks Strength", Range( 0, 1 ) ) = 0
		_SurveyPulse( "Survey Marks Pulses per Second", Float ) = 0.8

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
					float2 mainTexcoord: TEXCOORD0;
					float2 maskTexcoord: TEXCOORD1;
					float4 worldPosition: TEXCOORD2;
					UNITY_VERTEX_OUTPUT_STEREO
				};

				sampler2D _MainTex;
				float4 _MainTex_ST;

				sampler2D _MaskTex;
				float4 _MaskTex_ST;

				sampler2D _PlanetTex;
				sampler2D _WaterTex;
				sampler2D _NormalTex;

				fixed4 _Color;

				float _Reveal;
				float _Spin;
				float _Tilt;
				float4 _DiscRadius;
				float4 _LightDirection;
				float _Ambient;
				float _Relief;
				float _WaterGloss;
				fixed4 _AtmosphereColor;

				sampler2D _SurveyTex;
				float _SurveyStrength;
				float _SurveyPulse;

				float4 _ClipRect;
				fixed4 _TextureSampleAdd;

				v2f vert( appdata_t v )
				{
					v2f OUT;

					UNITY_SETUP_INSTANCE_ID( v );
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( OUT );

					OUT.worldPosition = v.vertex;
					OUT.vertex = UnityObjectToClipPos( OUT.worldPosition );
					OUT.mainTexcoord = TRANSFORM_TEX( v.texcoord, _MainTex );
					OUT.maskTexcoord = TRANSFORM_TEX( v.texcoord, _MaskTex );
					OUT.color = v.color * _Color;

					return OUT;
				}

				fixed4 frag( v2f IN ) : SV_Target
				{
					// the sensor noise, as the masked image shader draws it
					half4 mask = tex2D( _MaskTex, IN.maskTexcoord );
					half4 noise = ( tex2D( _MainTex, IN.mainTexcoord ) * mask + _TextureSampleAdd ) * IN.color;

					// where this pixel is on the disc of the mask (-1 to 1 across, y up)
					float2 disc = ( IN.maskTexcoord - 0.5 ) / _DiscRadius.xy;
					float discSquared = dot( disc, disc );

					// the very edge of the disc, where the mask is anti-aliased, takes the rim of the sphere
					disc *= ( discSquared > 0.9999 ) ? rsqrt( discSquared ) * 0.99995 : 1.0;
					discSquared = min( discSquared, 0.9999 );

					// the normal of the sphere, facing the viewer along +z
					float3 normal = float3( disc, sqrt( 1.0 - discSquared ) );

					// the planet's north pole is tilted a little toward the viewer
					float cosTilt = cos( _Tilt );
					float sinTilt = sin( _Tilt );
					float3 north = float3( 0.0, cosTilt, sinTilt );

					// latitude and longitude of that point on the planet
					float planetY = normal.y * cosTilt + normal.z * sinTilt;
					float planetZ = -normal.y * sinTilt + normal.z * cosTilt;
					float latitude = asin( clamp( planetY, -1.0, 1.0 ) );
					float longitude = atan2( normal.x, planetZ );

					// the map coordinates (the maps run west to east and south to north, and the planet turns to the east)
					float2 mapCoordinates = float2( longitude * ( 0.5 / UNITY_PI ) + 0.5 - _Spin, latitude / UNITY_PI + 0.5 );

					// the gradients of the map coordinates, without the jump where the longitude wraps around (or that seam would take the smallest mip)
					float2 dx = ddx( mapCoordinates );
					float2 dy = ddy( mapCoordinates );
					dx.x -= round( dx.x );
					dy.x -= round( dy.x );

					float3 albedo = tex2Dgrad( _PlanetTex, mapCoordinates, dx, dy ).rgb;
					float water = tex2Dgrad( _WaterTex, mapCoordinates, dx, dy ).r;
					float3 relief = tex2Dgrad( _NormalTex, mapCoordinates, dx, dy ).rgb * 2.0 - 1.0;

					// the relief of the normal map (east along the map's x, north along its y)
					float3 east = normalize( cross( north, normal ) + float3( 0.00001, 0.0, 0.0 ) );
					float3 up = cross( normal, east );
					float3 surfaceNormal = normalize( normal + ( east * relief.x + up * relief.y ) * _Relief );

					// light from the upper left, a little in front
					float3 lightDirection = normalize( _LightDirection.xyz );
					float diffuse = saturate( dot( surfaceNormal, lightDirection ) * 0.85 + 0.15 );

					// a glint on the water
					float3 halfway = normalize( lightDirection + float3( 0.0, 0.0, 1.0 ) );
					float specular = pow( saturate( dot( normal, halfway ) ), 40.0 ) * water * _WaterGloss;

					// the atmosphere: a coloured rim on the lit side
					float rim = pow( 1.0 - normal.z, 3.0 ) * saturate( dot( normal, lightDirection ) + 0.3 ) * 1.2;

					float3 planet = albedo * ( diffuse + _Ambient ) + specular + rim * _AtmosphereColor.rgb;

					// the survey's marks, unlit and pulsing: their texture runs from north-south -90 to 90 as the landing map does, which is 0.125 to 0.875
					// up the planet's maps (nothing nearer the poles)
					if ( _SurveyStrength > 0.0 )
					{
						float surveyV = ( mapCoordinates.y - 0.125 ) / 0.75;

						if ( ( surveyV >= 0.0 ) && ( surveyV <= 1.0 ) )
						{
							// (the gradients go along with the texture's v, stretched as it is)
							float2 surveyDx = float2( dx.x, dx.y / 0.75 );
							float2 surveyDy = float2( dy.x, dy.y / 0.75 );
							half4 marks = tex2Dgrad( _SurveyTex, float2( mapCoordinates.x, surveyV ), surveyDx, surveyDy );
							float pulse = 0.7 + 0.3 * sin( _Time.y * _SurveyPulse * 2.0 * UNITY_PI );

							planet = lerp( planet, marks.rgb, marks.a * _SurveyStrength * pulse );
						}
					}

					// the noise fades into the planet as the scan goes on
					half4 color;

					color.rgb = lerp( noise.rgb, planet, _Reveal );
					color.a = mask.a * IN.color.a;

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
