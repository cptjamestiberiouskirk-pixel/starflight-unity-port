// the star on the system map: a white-hot core, a corona in the colour of the star and short rays, drawn additively
// (_Size is the size of the image in map units, so that the shapes keep their size in map units whatever the image is)
Shader "Custom/UI/System Map Star"
{
	Properties
	{
		[PerRendererData] _MainTex( "Sprite Texture", 2D ) = "white" {}

		_Color( "Tint", Color ) = ( 1, 1, 1, 1 )

		_StarColor( "Star Color", Color ) = ( 1, 0.9, 0.62, 1 )
		_Size( "Size (map units)", Float ) = 340

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
		Blend One One
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

				fixed4 _Color;

				fixed4 _StarColor;
				float _Size;

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

				// the brightness of a ray along the line through the middle at the given angle
				float Ray( float2 position, float distance, float angle, float length )
				{
					float across = abs( position.x * cos( angle ) + position.y * sin( angle ) );

					return exp( -( across / 0.6 ) * ( across / 0.6 ) ) * exp( -distance / length );
				}

				fixed4 frag( v2f IN ) : SV_Target
				{
					// this pixel in map units from the middle of the star
					float2 position = ( IN.texcoord - 0.5 ) * _Size;
					float distance = length( position );

					// a slow shimmer
					float shimmer = 1.0 + 0.04 * sin( _Time.y * 3.1 ) + 0.02 * sin( _Time.y * 7.3 );

					// the corona
					float corona = 1.7 * exp( -( distance / 9.0 ) * ( distance / 9.0 ) ) + 0.6 * exp( -( distance / 20.0 ) * ( distance / 20.0 ) ) + 0.2 * exp( -( distance / 50.0 ) * ( distance / 50.0 ) ) + 0.06 * exp( -( distance / 110.0 ) * ( distance / 110.0 ) );

					// four short rays, turning very slowly
					float turn = _Time.y * 0.02;
					float rays = 0.0;

					for ( int i = 0; i < 4; i++ )
					{
						rays += Ray( position, distance, radians( 45.0 * i + 22.0 ) + turn, ( i % 2 == 0 ) ? 16.0 : 9.0 ) * 0.5;
					}

					float3 light = _StarColor.rgb * ( corona + rays ) * shimmer;

					// the white-hot core, anti-aliased over one pixel, that turns to the colour of the star at its edge
					float core = saturate( ( 6.5 - distance ) / max( fwidth( distance ), 0.0001 ) + 0.5 );
					float edgeOfCore = saturate( distance / 7.0 );

					light += lerp( float3( 1.0, 1.0, 1.0 ), 0.5 + 0.5 * _StarColor.rgb, edgeOfCore * edgeOfCore ) * 1.4 * core;

					// everything fades out before the edge of the image
					float fade = saturate( 1.0 - distance / ( _Size * 0.5 ) );

					half4 color;

					color.rgb = light * fade * fade * IN.color.rgb * IN.color.a;
					color.a = 1.0;

					#ifdef UNITY_UI_CLIP_RECT
					color.rgb *= UnityGet2DClipping( IN.worldPosition.xy, _ClipRect );
					#endif

					return color;
				}

			ENDCG
		}
	}
}
