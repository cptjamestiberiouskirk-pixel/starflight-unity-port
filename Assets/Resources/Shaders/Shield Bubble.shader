// the shield bubble that lights up around the player ship when a hit lands on its shields (ShieldHitEffect)
// it is in a Resources folder so that it is always in a build: Shader.Find only finds a shader that something in the build refers to
// it only adds light: a faint rim where the bubble curves away from the camera, a hot spot where the shot struck, and a ripple that runs from there across the bubble
Shader "Starflight/Shield Bubble"
{
	Properties
	{
		_Color( "Colour", Color ) = ( 0.35, 0.7, 1.0, 1.0 )
		_ImpactDirection( "Impact Direction (object space)", Vector ) = ( 0, 0, 1, 0 )
		_Progress( "Progress", Range( 0, 1 ) ) = 0
		_Intensity( "Intensity", Range( 0, 4 ) ) = 1
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

		Blend One One
		ZWrite Off
		Cull Back
		Lighting Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			fixed4 _Color;
			float4 _ImpactDirection;
			float _Progress;
			float _Intensity;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float3 objectNormal : TEXCOORD0;
				float3 worldNormal : TEXCOORD1;
				float3 viewDirection : TEXCOORD2;
			};

			v2f vert( appdata v )
			{
				v2f o;
				o.vertex = UnityObjectToClipPos( v.vertex );
				o.objectNormal = v.normal;
				o.worldNormal = UnityObjectToWorldNormal( v.normal );
				o.viewDirection = WorldSpaceViewDir( v.vertex );
				return o;
			}

			fixed4 frag( v2f i ) : SV_Target
			{
				// the rim: brightest where the bubble curves away from the camera
				float facing = saturate( dot( normalize( i.worldNormal ), normalize( i.viewDirection ) ) );
				float rim = pow( 1.0 - facing, 3.0 );

				// how far round the bubble this point is from where the shot struck, from 0 there to 1 on the far side
				float impactCosine = clamp( dot( normalize( i.objectNormal ), normalize( _ImpactDirection.xyz ) ), -1.0, 1.0 );
				float distance = acos( impactCosine ) / 3.14159265;

				// the hot spot where the shot struck, which fades fast
				float spot = exp( -distance * distance / 0.006 ) * ( 1.0 - _Progress ) * ( 1.0 - _Progress );

				// a ripple that runs out from the hot spot and thins as it goes
				float rippleOffset = ( distance - _Progress * 0.75 ) / 0.045;
				float ripple = exp( -rippleOffset * rippleOffset ) * ( 1.0 - distance );

				// the whole bubble fades out over the effect
				float fade = 1.0 - _Progress;

				float brightness = ( rim * 0.35 + ripple * 0.9 + spot * 2.0 ) * fade * _Intensity;

				return fixed4( _Color.rgb * brightness, 0.0 );
			}
			ENDCG
		}
	}
}
