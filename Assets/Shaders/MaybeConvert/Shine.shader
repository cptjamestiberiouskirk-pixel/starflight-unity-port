Shader "Custom/Shine"
{
	Properties
	{
		_Color( "Color", Color ) = ( 1, 1, 1, 1 )
		_MainTex( "Albedo (RGB)", 2D ) = "white" {}
		_LerpTime( "LerpTime", Float ) = 0.0
		_Size( "Size", Vector ) = ( 5, 10, 0, 0 )
	}

	Category
	{
		Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
		}

		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off
		Lighting Off
		ZWrite Off

		SubShader
		{
			Pass
			{
				CGPROGRAM

					#pragma vertex vert
					#pragma fragment frag
					#pragma target 2.0

					#include "UnityCG.cginc"

					float4 _Color;
					sampler2D _MainTex;
					float4 _MainTex_ST;
					float _LerpTime;
					float4 _Size;

					struct vs_in
					{
						float4 position : POSITION;
						float2 texcoord : TEXCOORD0;
					};

					struct vs_out
					{
						float4 position : SV_POSITION;
						float2 texcoord : TEXCOORD0;
					};

					vs_out vert( vs_in v )
					{
						vs_out o;

						float s = ( _Size.x + sin( v.position.y + _LerpTime ) * _Size.y ) * v.position.z;

						float x = sin( v.position.x ) * s;
						float y = cos( v.position.x ) * s;

						float3 position = float3( x, y, 0.0f );

						o.position = UnityObjectToClipPos( position );
						o.texcoord = TRANSFORM_TEX( v.texcoord, _MainTex );

						return o;
					}

					fixed4 frag( vs_out v ) : SV_Target
					{
						// the gradient gives the shape of the shine (its alpha) and how far out this pixel is: it goes from white in the
						// middle to orange at the rim, so the loss of blue is the distance to the rim
						float4 gradient = tex2D( _MainTex, v.texcoord );
						float rim = saturate( 1.0 - gradient.b );

						// the colour of the star, nearly white in the middle and deeper toward the rim (for a G star the rim is the orange
						// of the gradient, for a blue star a deep blue)
						float3 color = pow( max( _Color.rgb, 0.0001 ), lerp( 0.35, 3.0, rim ) );

						return float4( color, gradient.a * _Color.a );
					}

				ENDCG
			}
		}
	}
}
