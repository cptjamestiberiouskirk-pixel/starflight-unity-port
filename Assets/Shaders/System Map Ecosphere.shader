// the ecosphere on the system map: a faint band between two radii with soft edge lines, drawn additively
// (the radii and the softness are fractions of the half size of the image; SystemDisplay sets them from the star's spectral class;
// the light is worked out in gamma space, as it looks on the screen, and goes out in the colour space of the project)
Shader "Custom/UI/System Map Ecosphere"
{
	Properties
	{
		[PerRendererData] _MainTex( "Sprite Texture", 2D ) = "white" {}

		_Color( "Tint", Color ) = ( 1, 1, 1, 1 )

		_FillColor( "Fill Color (alpha is the strength)", Color ) = ( 0.1, 0.45, 0.26, 0.085 )
		_EdgeColor( "Edge Color (alpha is the strength)", Color ) = ( 0.25, 0.8, 0.5, 0.07 )
		_InnerRadius( "Inner Radius", Range( 0, 1 ) ) = 0.5
		_OuterRadius( "Outer Radius", Range( 0, 1 ) ) = 0.95
		_Softness( "Softness", Range( 0.001, 1 ) ) = 0.03
		_EdgeWidth( "Edge Width", Range( 0.001, 1 ) ) = 0.006

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

				fixed4 _FillColor;
				fixed4 _EdgeColor;
				float _InnerRadius;
				float _OuterRadius;
				float _Softness;
				float _EdgeWidth;

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

				// the shapes of this shader are designed as light added on the screen (in gamma space): its colours are taken
				// back to gamma space, and the result goes out in the colour space of the project
				float3 ToGamma( float3 color )
				{
					#ifdef UNITY_COLORSPACE_GAMMA
					return color;
					#else
					return LinearToGammaSpace( max( color, 0.0 ) );
					#endif
				}

				float3 FromGamma( float3 color )
				{
					#ifdef UNITY_COLORSPACE_GAMMA
					return color;
					#else
					return GammaToLinearSpace( max( color, 0.0 ) );
					#endif
				}

				fixed4 frag( v2f IN ) : SV_Target
				{
					// this pixel from the middle of the image (-1 to 1 across, y up)
					float distance = length( IN.texcoord * 2.0 - 1.0 );

					// the band, with soft edges
					float fill = saturate( ( distance - _InnerRadius ) / _Softness ) * saturate( ( _OuterRadius - distance ) / _Softness );

					// a faint line along each edge
					float innerEdge = ( distance - _InnerRadius ) / _EdgeWidth;
					float outerEdge = ( distance - _OuterRadius ) / _EdgeWidth;
					float edges = exp( -innerEdge * innerEdge ) + exp( -outerEdge * outerEdge );

					half4 color;

					color.rgb = FromGamma( ToGamma( _FillColor.rgb ) * fill * _FillColor.a + ToGamma( _EdgeColor.rgb ) * edges * _EdgeColor.a ) * IN.color.rgb * IN.color.a;
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
