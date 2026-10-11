// an orbit on the system map: a thin anti-aliased ring that is brighter in a trail behind its planet
// (the image turns with its orbit and the planet is at the top of it, so the trail is fixed in the image: it runs clockwise from the top)
Shader "Custom/UI/System Map Orbit"
{
	Properties
	{
		[PerRendererData] _MainTex( "Sprite Texture", 2D ) = "white" {}

		_Color( "Tint", Color ) = ( 1, 1, 1, 1 )

		_RingColor( "Ring Color", Color ) = ( 0.62, 0.74, 0.95, 0.22 )
		_TrailColor( "Trail Color", Color ) = ( 0.75, 0.85, 1, 0.55 )
		_Radius( "Radius (fraction of the half size)", Range( 0, 1 ) ) = 0.9
		_LineWidth( "Line Width (pixels)", Float ) = 1.4
		_TrailLength( "Trail Length (radians)", Float ) = 0.66

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

				fixed4 _Color;

				fixed4 _RingColor;
				fixed4 _TrailColor;
				float _Radius;
				float _LineWidth;
				float _TrailLength;

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
					// this pixel from the middle of the image (-1 to 1 across, y up)
					float2 position = IN.texcoord * 2.0 - 1.0;
					float distance = length( position );

					// the ring, anti-aliased, the same width in pixels at any size
					float pixel = max( fwidth( distance ), 0.00001 );
					float ring = saturate( _LineWidth * 0.5 + 0.5 - abs( distance - _Radius ) / pixel );

					// the angle around the ring (0 at the top, counter-clockwise), and how far behind the planet this pixel is
					float angle = atan2( -position.x, position.y );
					float behind = ( angle > 0.0 ) ? ( UNITY_TWO_PI - angle ) : -angle;
					float trail = exp( -behind / max( _TrailLength, 0.001 ) );

					half4 color;

					color.rgb = lerp( _RingColor.rgb, _TrailColor.rgb, trail ) * IN.color.rgb;
					color.a = ring * saturate( _RingColor.a + _TrailColor.a * trail ) * IN.color.a;

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
