// unlit particle shader for the explosions (ExplosionEffect)
// it is in a Resources folder so that it is always in a build: Shader.Find only finds a shader that something in the build refers to
// the blend factors are properties, so one shader does both the additive glow (fire, sparks) and the alpha blended smoke and debris
Shader "Starflight/Explosion Particle"
{
	Properties
	{
		_MainTex( "Texture", 2D ) = "white" {}
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend( "Source Blend", Float ) = 5
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend( "Destination Blend", Float ) = 1
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

		Blend [_SrcBlend] [_DstBlend]
		ZWrite Off
		Cull Off
		Lighting Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;

			struct appdata
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
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
				o.color = v.color;
				o.uv = TRANSFORM_TEX( v.uv, _MainTex );
				return o;
			}

			fixed4 frag( v2f i ) : SV_Target
			{
				// the texture gives the shape, the particle gives the colour
				return tex2D( _MainTex, i.uv ) * i.color;
			}
			ENDCG
		}
	}
}
