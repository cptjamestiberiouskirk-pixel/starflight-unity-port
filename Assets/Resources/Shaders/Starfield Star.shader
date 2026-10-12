// a star of the starfield (InfiniteStarfield), drawn on its particle's quad with no texture: a sharp core and a soft halo,
// a cross shaped glint for a bright star, and a gentle twinkle for some of them
// the particle's colour is the star's colour times its brightness; its alpha is how strong its glint is (0 for most stars)
// the particle's stable random numbers (a custom vertex stream) say whether it twinkles, how fast and from what point, so the twinkle costs nothing on the CPU
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Starfield Star"
{
	Properties
	{
		_TwinkleShare( "Share Of Stars That Twinkle", Range( 0, 1 ) ) = 0.45
		_TwinkleDepth( "Twinkle Depth", Range( 0, 1 ) ) = 0.45
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

		Blend One One
		ZWrite Off
		Cull Off
		Lighting Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			float _TwinkleShare;
			float _TwinkleDepth;

			struct appdata
			{
				float4 vertex : POSITION;
				float4 color : COLOR;

				// xy: the corner of the quad; zw: the particle's stable random numbers (InfiniteStarfield sets the vertex streams)
				float4 texcoord : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
			};

			v2f vert( appdata v )
			{
				v2f o;
				o.vertex = UnityObjectToClipPos( v.vertex );
				o.uv = v.texcoord.xy * 2.0 - 1.0;

				// the twinkle: some of the stars, each at its own speed and from its own point, two waves so that it never repeats exactly
				float twinkles = step( v.texcoord.z, _TwinkleShare );
				float speed = lerp( 1.2, 3.5, v.texcoord.w );
				float phase = v.texcoord.z * 40.0;
				float wave = sin( _Time.y * speed + phase ) * sin( _Time.y * speed * 1.73 + phase * 1.31 );
				float twinkle = 1.0 + twinkles * _TwinkleDepth * wave;

				o.color = float4( v.color.rgb * twinkle, v.color.a );
				return o;
			}

			fixed4 frag( v2f i ) : SV_Target
			{
				float2 p = i.uv;
				float r2 = dot( p, p );

				// a sharp core and a soft halo
				float core = exp( -r2 / 0.012 );
				float halo = exp( -r2 / 0.07 ) * 0.14;

				// a cross shaped glint along the screen's axes, for a bright star (its colour's alpha)
				float spikeX = exp( -abs( p.y ) * 60.0 ) * exp( -abs( p.x ) * 3.0 );
				float spikeY = exp( -abs( p.x ) * 60.0 ) * exp( -abs( p.y ) * 3.0 );
				float glint = ( spikeX + spikeY ) * 0.4 * i.color.a;

				// nothing at the edge of the quad, so no star shows the square it is drawn on
				float edge = saturate( ( 1.0 - max( abs( p.x ), abs( p.y ) ) ) * 6.0 );

				float brightness = ( core + halo + glint ) * edge;

				return fixed4( i.color.rgb * brightness, 0.0 );
			}
			ENDCG
		}
	}
}
