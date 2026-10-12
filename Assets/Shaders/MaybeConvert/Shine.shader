Shader "Custom/Shine"
{
	Properties
	{
		_Color( "Color", Color ) = ( 1, 1, 1, 1 )
		_MainTex( "Albedo (RGB)", 2D ) = "white" {}
		_LerpTime( "LerpTime", Float ) = 0.0
		_Size( "Size", Vector ) = ( 5, 10, 0, 0 )
		_Flare( "Flare (angle, height, width, strength)", Vector ) = ( 0, 0, 0.3, 0 )
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
					#pragma target 3.0

					#include "UnityCG.cginc"

					float4 _Color;
					sampler2D _MainTex;
					float4 _MainTex_ST;
					float _LerpTime;
					float4 _Size;
					float4 _Flare;

					struct vs_in
					{
						float4 position : POSITION;
						float2 texcoord : TEXCOORD0;
					};

					struct vs_out
					{
						float4 position : SV_POSITION;
						float2 texcoord : TEXCOORD0;
						float2 local : TEXCOORD1;
					};

					// how far an angle is from the angle of the flare, the short way round (radians)
					float FromFlare( float angle )
					{
						float difference = angle - _Flare.x;

						return difference - UNITY_TWO_PI * round( difference / UNITY_TWO_PI );
					}

					// smooth value noise, and three octaves of it
					float Hash( float2 p )
					{
						p = frac( p * float2( 123.34, 456.21 ) );
						p += dot( p, p + 45.32 );

						return frac( p.x * p.y );
					}

					float Noise( float2 p )
					{
						float2 i = floor( p );
						float2 f = frac( p );
						float2 u = f * f * ( 3.0 - 2.0 * f );

						return lerp( lerp( Hash( i ), Hash( i + float2( 1.0, 0.0 ) ), u.x ), lerp( Hash( i + float2( 0.0, 1.0 ) ), Hash( i + float2( 1.0, 1.0 ) ), u.x ), u.y );
					}

					float Fbm( float2 p )
					{
						return 0.5 * Noise( p ) + 0.3 * Noise( p * 2.1 + 7.3 ) + 0.2 * Noise( p * 4.3 + 1.7 );
					}

					vs_out vert( vs_in v )
					{
						vs_out o;

						float s = ( _Size.x + sin( v.position.y + _LerpTime ) * _Size.y ) * v.position.z;

						// a small flare lifts the rim near its angle (the middle vertex has no rim, z = 0)
						float fromFlare = FromFlare( v.position.x ) / max( _Flare.z, 0.001 );

						s += _Flare.y * exp( -fromFlare * fromFlare ) * v.position.z;

						float x = sin( v.position.x ) * s;
						float y = cos( v.position.x ) * s;

						float3 position = float3( x, y, 0.0f );

						o.position = UnityObjectToClipPos( position );
						o.texcoord = TRANSFORM_TEX( v.texcoord, _MainTex );

						// where this vertex is, in sizes of the shine (for the patterns of the surface and the corona)
						o.local = position.xy / max( _Size.x, 0.001 );

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

						float alpha = gradient.a * _Color.a;

						// the surface: two layers of cells that drift against each other, so that it slowly churns (only on the disc)
						float time = _Time.y;
						float2 flow = float2( time * 0.05, -time * 0.03 );
						float cells = 0.5 * ( Fbm( v.local * 9.0 + flow ) + Fbm( v.local * 9.7 - flow + 3.1 ) );
						float disc = 1.0 - smoothstep( 0.35, 0.6, v.texcoord.x );

						color *= lerp( 1.0, 0.8 + 0.25 * cells, disc );

						// the corona: streamers round the rim that flicker (the noise is read on a circle, so it has no seam)
						float angle = atan2( v.local.x, v.local.y );
						float2 onCircle = float2( sin( angle ), cos( angle ) );
						float streamers = Fbm( onCircle * 2.2 + float2( time * 0.12, time * 0.07 ) ) * ( 0.85 + 0.15 * sin( time * 3.0 + angle * 5.0 ) );
						float corona = smoothstep( 0.45, 0.8, v.texcoord.x );

						alpha *= lerp( 1.0, 0.55 + 0.9 * streamers, corona );
						color *= lerp( 1.0, 0.85 + 0.3 * streamers, corona );

						// the flare is brighter where it rises
						float fromFlare = FromFlare( angle ) / max( _Flare.z, 0.001 );
						float flare = _Flare.w * exp( -fromFlare * fromFlare ) * corona;

						alpha = saturate( alpha * ( 1.0 + 0.8 * flare ) );
						color *= 1.0 + 0.4 * flare;

						return float4( color, alpha );
					}

				ENDCG
			}
		}
	}
}
