// the clouds of a planet in its natural look, on their own sphere just above the ground: the cover NaturalPlanet bakes from the planet's weather, moved
// along the longitude by the drift (so that the clouds turn a little faster than the ground), lit by the same reddening sunlight as the ground, a little
// brighter where the light comes through their thin edges, seen through the air in front of them and brought down to the screen by the same tone curve
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Natural Clouds"
{
	Properties
	{
		_Clouds( "Cloud Cover", 2D ) = "black" {}
		_CloudDrift( "Cloud Drift", Float ) = 0
		_CloudColor( "Cloud Colour", Color ) = ( 1, 1, 1, 1 )
		_Opacity( "Opacity", Float ) = 0.95
		_Brightness( "Brightness", Float ) = 0.5
		_SunIntensity( "Sun Intensity", Float ) = 1.9
		_AirThickness( "Air Thickness (reddens the sunlight)", Float ) = 1
		_CloudScale( "Cloud Sphere Radius (planet radii)", Float ) = 1.01
		_AirColor( "Air Colour", Color ) = ( 0.35, 0.55, 1, 1 )
		_AirTint( "Share Of The Air Colour", Float ) = 0
		_AirDensity( "Air Density", Float ) = 0
		_AirHeight( "Air Height (planet radii)", Float ) = 0.06
		_AirScaleHeight( "Scale Height (share of the air's height)", Float ) = 0.15
		_AirLight( "Sunlight In The Air", Float ) = 14
	}

	SubShader
	{
		Tags { "Queue" = "Transparent-2" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

		Pass
		{
			Tags { "LightMode" = "ForwardBase" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#include "UnityCG.cginc"
			#include "Natural Planet.cginc"

			sampler2D _Clouds;
			float _CloudDrift;
			float4 _CloudColor;
			float _Opacity;
			float _Brightness;
			float _SunIntensity;
			float _AirThickness;
			float _CloudScale;
			float4 _AirColor;
			float _AirTint;
			float _AirDensity;
			float _AirHeight;
			float _AirScaleHeight;
			float _AirLight;
			float4 _LightColor0;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 normalWorld : TEXCOORD1;
				float3 worldPos : TEXCOORD2;
				float4 sphere : TEXCOORD3;
			};

			v2f vert( appdata_base v )
			{
				v2f o;

				o.pos = UnityObjectToClipPos( v.vertex );
				o.uv = v.texcoord.xy;
				o.normalWorld = UnityObjectToWorldNormal( v.normal );
				o.worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;

				// the planet's middle (the cloud sphere's own), and the radius of its ground (under the cloud sphere's)
				float3 center = mul( unity_ObjectToWorld, float4( 0.0, 0.0, 0.0, 1.0 ) ).xyz;

				o.sphere = float4( center, length( o.worldPos - center ) / max( _CloudScale, 1e-3 ) );

				return o;
			}

			float4 frag( v2f i ) : SV_Target
			{
				float cover = tex2D( _Clouds, i.uv + float2( _CloudDrift, 0.0 ) ).r;
				float3 normal = normalize( i.normalWorld );
				float3 light = NP_LightDirection( i.worldPos );
				float3 view = normalize( _WorldSpaceCameraPos - i.worldPos );

				float sunHeight = dot( normal, light );
				float daylight = smoothstep( -0.06, 0.1, sunHeight );

				// lit from above, with a little light through the thin edges
				float lit = saturate( sunHeight * 0.85 + 0.15 ) + ( 1.0 - cover ) * 0.25 * saturate( dot( -view, light ) + 0.3 );
				float3 color = _CloudColor.rgb * _LightColor0.rgb * _SunIntensity * NP_SunTint( sunHeight, _AirThickness ) * lit * daylight * _Brightness;

				// the air in front of the clouds
				NP_Air air = NP_MakeAir( _AirHeight, _AirScaleHeight, _AirColor.rgb, _AirTint, _AirDensity );
				float3 inscatter;
				float3 transmittance;

				// (from the point on the cloud sphere: between its vertices the mesh's triangles are a little inside it)
				float3 cloudPoint = i.sphere.xyz + normalize( i.worldPos - i.sphere.xyz ) * i.sphere.w * _CloudScale;

				NP_AirInFront( air, _AirDensity, _WorldSpaceCameraPos, cloudPoint, i.sphere.xyz, i.sphere.w, light, _AirLight * dot( _LightColor0.rgb, 0.3333 ), inscatter, transmittance );

				color = color * transmittance + inscatter;

				return float4( NP_ToneMap( color ), saturate( cover * _Opacity ) );
			}
			ENDCG
		}
	}
}
