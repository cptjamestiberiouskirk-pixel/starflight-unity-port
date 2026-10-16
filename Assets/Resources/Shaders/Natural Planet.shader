// a planet in its natural look, from the maps NaturalPlanet bakes (and, close up, finer detail of the ground worked out on the spot): the ground lit by
// sunlight that reddens where it has crossed more air (near the line
// between day and night), the sun's glint on the open sea and a sheen of sky at grazing angles (on a molten world the albedo's alpha is the glow of its
// lava instead, which shines by itself, day and night), the shadows of the clouds, and the air in front of the
// ground (the haze that thickens towards the planet's edge, worked out by the same scattering as the halo round it), brought down to the screen by a
// filmic tone curve
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Natural Planet"
{
	Properties
	{
		_Albedo( "Albedo (rgb), Open Sea (a)", 2D ) = "white" {}
		_NormalMap( "Normal", 2D ) = "bump" {}
		_Clouds( "Cloud Cover", 2D ) = "black" {}
		_CloudDrift( "Cloud Drift", Float ) = 0
		_CloudShadow( "Cloud Shadow", Float ) = 0.6
		_SunIntensity( "Sun Intensity", Float ) = 1.9
		_AirThickness( "Air Thickness (reddens the sunlight)", Float ) = 1
		_SkyColor( "Sky Colour (on the sea)", Color ) = ( 0.35, 0.55, 1, 1 )
		_AirColor( "Air Colour", Color ) = ( 0.35, 0.55, 1, 1 )
		_AirTint( "Share Of The Air Colour", Float ) = 0
		_AirDensity( "Air Density", Float ) = 0
		_AirHeight( "Air Height (planet radii)", Float ) = 0.06
		_AirScaleHeight( "Scale Height (share of the air's height)", Float ) = 0.15
		_AirLight( "Sunlight In The Air", Float ) = 14
		_Molten( "Molten (the albedo's alpha is the lava's glow)", Float ) = 0
		_LavaColor( "Lava Colour", Color ) = ( 1, 0.38, 0.08, 1 )
		_LavaIntensity( "Lava Brightness", Float ) = 2.2
		_MapSize( "Map Size (texels)", Vector ) = ( 4096, 2048, 0, 0 )
		_GasGiant( "Gas Giant (no detail close up)", Float ) = 0
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

		Pass
		{
			Tags { "LightMode" = "ForwardBase" }

			// marks where the planet is, so that the halo (Natural Atmosphere) leaves it alone: this shader works out the air in front of the ground
			Stencil
			{
				Ref 64
				WriteMask 64
				Comp Always
				Pass Replace
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#include "UnityCG.cginc"
			#include "Natural Planet.cginc"

			sampler2D _Albedo;
			sampler2D _NormalMap;
			sampler2D _Clouds;
			float _CloudDrift;
			float _CloudShadow;
			float _SunIntensity;
			float _AirThickness;
			float4 _SkyColor;
			float4 _AirColor;
			float _AirTint;
			float _AirDensity;
			float _AirHeight;
			float _AirScaleHeight;
			float _AirLight;
			float _Molten;
			float4 _LavaColor;
			float _LavaIntensity;
			float4 _MapSize;
			float _GasGiant;
			float4 _LightColor0;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
				float3 normalWorld : TEXCOORD2;
				float3 tangentWorld : TEXCOORD3;
				float3 binormalWorld : TEXCOORD4;
				float4 sphere : TEXCOORD5;
				float3 objectPos : TEXCOORD6;
			};

			v2f vert( appdata v )
			{
				v2f o;

				o.pos = UnityObjectToClipPos( v.vertex );
				o.uv = v.uv;
				o.worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				o.normalWorld = UnityObjectToWorldNormal( v.normal );
				o.tangentWorld = UnityObjectToWorldDir( v.tangent.xyz );
				o.binormalWorld = cross( o.normalWorld, o.tangentWorld ) * v.tangent.w * unity_WorldTransformParams.w;

				// the planet's middle, and its radius (the mesh is a sphere round its own origin, so every vertex is on it)
				float3 center = mul( unity_ObjectToWorld, float4( 0.0, 0.0, 0.0, 1.0 ) ).xyz;

				o.sphere = float4( center, length( o.worldPos - center ) );
				o.objectPos = v.vertex.xyz;

				return o;
			}

			// a hash without sines, which stays exact at the large coordinates of the finest detail
			float3 DetailHash( float3 p )
			{
				p = frac( p * float3( 0.1031, 0.1030, 0.0973 ) );
				p += dot( p, p.yxz + 33.33 );

				return frac( ( p.xxy + p.yxx ) * p.zyx ) * 2.0 - 1.0;
			}

			// gradient noise (about -0.7 to 0.7)
			float DetailNoise( float3 p )
			{
				float3 i = floor( p );
				float3 f = frac( p );
				float3 u = f * f * ( 3.0 - 2.0 * f );

				return lerp( lerp( lerp( dot( DetailHash( i + float3( 0, 0, 0 ) ), f - float3( 0, 0, 0 ) ), dot( DetailHash( i + float3( 1, 0, 0 ) ), f - float3( 1, 0, 0 ) ), u.x ),
					lerp( dot( DetailHash( i + float3( 0, 1, 0 ) ), f - float3( 0, 1, 0 ) ), dot( DetailHash( i + float3( 1, 1, 0 ) ), f - float3( 1, 1, 0 ) ), u.x ), u.y ),
					lerp( lerp( dot( DetailHash( i + float3( 0, 0, 1 ) ), f - float3( 0, 0, 1 ) ), dot( DetailHash( i + float3( 1, 0, 1 ) ), f - float3( 1, 0, 1 ) ), u.x ),
					lerp( dot( DetailHash( i + float3( 0, 1, 1 ) ), f - float3( 0, 1, 1 ) ), dot( DetailHash( i + float3( 1, 1, 1 ) ), f - float3( 1, 1, 1 ) ), u.x ), u.y ), u.z );
			}

			// the fine detail of the ground (about -1 to 1) at a direction on the sphere: three octaves, the coarsest a little finer than a texel of the maps
			float DetailHeight( float3 direction )
			{
				float3 p = direction * 900.0;

				return DetailNoise( p ) * 0.8 + DetailNoise( p * 2.13 + 3.7 ) * 0.45 + DetailNoise( p * 4.37 + 7.1 ) * 0.25;
			}

			// a little noise for the ripples of the sea
			float Hash( float3 p )
			{
				p = frac( p * 0.3183099 + 0.1 );
				p *= 17.0;

				return frac( p.x * p.y * p.z * ( p.x + p.y + p.z ) );
			}

			float4 frag( v2f i ) : SV_Target
			{
				float4 albedoAndSea = tex2D( _Albedo, i.uv );
				float3 albedo = albedoAndSea.rgb;
				float water = albedoAndSea.a * ( 1.0 - _Molten );
				float glow = albedoAndSea.a * _Molten;

				// the normal of the ground, from its map (x along u, y along v)
				float3 normalTangent = tex2D( _NormalMap, i.uv ).xyz * 2.0 - 1.0;
				float3 normalGeometry = normalize( i.normalWorld );
				float3 tangent = normalize( i.tangentWorld - normalGeometry * dot( i.tangentWorld, normalGeometry ) );
				float3 binormal = cross( normalGeometry, tangent ) * sign( dot( cross( normalGeometry, tangent ), i.binormalWorld ) );
				float3 normal = normalize( tangent * normalTangent.x + binormal * normalTangent.y + normalGeometry * normalTangent.z );

				// close up, where a texel of the maps covers more than a pixel (the first seconds of a launch from the surface), finer detail of the ground worked
				// out on the spot, so that it stays sharp: a little light and shade in its colour, and bumps, whose slope comes from how the height changes from
				// pixel to pixel (worked out everywhere and faded in, since such changes are not known inside a branch)
				float2 texels = fwidth( i.uv * _MapSize.xy );
				float magnification = 1.0 / max( max( texels.x, texels.y ), 1e-6 );
				float detail = saturate( ( magnification - 1.5 ) * 0.5 ) * ( 1.0 - water ) * ( 1.0 - _GasGiant );
				float bump = DetailHeight( normalize( i.objectPos ) );
				float bumpHeight = bump * i.sphere.w * 0.0003 * detail;
				float3 dpdx = ddx( i.worldPos );
				float3 dpdy = ddy( i.worldPos );
				float3 r1 = cross( dpdy, normal );
				float3 r2 = cross( normal, dpdx );
				float determinant = dot( dpdx, r1 );
				float3 surfaceGradient = sign( determinant ) * ( ddx( bumpHeight ) * r1 + ddy( bumpHeight ) * r2 );

				if ( detail > 0.001 )
				{
					normal = normalize( abs( determinant ) * normal - surfaceGradient );
				}

				albedo *= 1.0 + bump * 0.25 * detail;

				float3 light = NP_LightDirection( i.worldPos );
				float3 view = normalize( _WorldSpaceCameraPos - i.worldPos );

				// the sunlight that reaches the ground: redder where it has crossed more air (where the sun is low)
				float sunHeight = dot( normalGeometry, light );
				float3 sun = _LightColor0.rgb * _SunIntensity * NP_SunTint( sunHeight, _AirThickness );
				float daylight = smoothstep( -0.04, 0.08, sunHeight );

				// the ground, and the sea (which is flat)
				float3 color = albedo * saturate( dot( lerp( normal, normalGeometry, water ), light ) ) * daylight * sun;

				if ( water > 0.01 )
				{
					// the sun's glint on the ripples, and the sky in the sea at grazing angles
					float3 ripple = normalize( normalGeometry + ( float3( Hash( i.worldPos * 3.1 ), Hash( i.worldPos * 3.1 + 7.0 ), Hash( i.worldPos * 3.1 + 13.0 ) ) - 0.5 ) * 0.03 );
					float3 halfway = normalize( light + view );
					float nh = saturate( dot( ripple, halfway ) );
					float roughness = 0.14;
					float a2 = roughness * roughness * roughness * roughness;
					float d = a2 / ( 3.1415927 * pow( nh * nh * ( a2 - 1.0 ) + 1.0, 2.0 ) );
					float nv = saturate( dot( ripple, view ) );
					float fresnel = 0.02 + 0.98 * pow( 1.0 - nv, 5.0 );
					float glint = d * fresnel * saturate( dot( ripple, light ) ) * 0.25;

					color += water * ( glint * sun + fresnel * _SkyColor.rgb * 0.2 * daylight * _SunIntensity );
				}

				// the shadows of the clouds (the cover a little way towards the sun)
				float2 towardsSun = float2( dot( light, tangent ), dot( light, binormal ) );
				float shadow = tex2D( _Clouds, i.uv + float2( _CloudDrift, 0.0 ) + towardsSun * 0.004 ).r;

				// (only clouds thick enough to see cast a shadow, so that the faint wisps between them leave none)
				color *= 1.0 - smoothstep( 0.15, 0.75, shadow ) * _CloudShadow;

				// the lava's own light (dimmed a little under thick clouds)
				color += glow * _LavaColor.rgb * _LavaIntensity * ( 1.0 - smoothstep( 0.15, 0.75, shadow ) * _CloudShadow * 0.5 );

				// the air in front of the ground
				NP_Air air = NP_MakeAir( _AirHeight, _AirScaleHeight, _AirColor.rgb, _AirTint, _AirDensity );
				float3 inscatter;
				float3 transmittance;

				// (from the point on the sphere: between its vertices the mesh's triangles are a little inside it, and the air would be thicker in their middle)
				float3 surfacePoint = i.sphere.xyz + normalize( i.worldPos - i.sphere.xyz ) * i.sphere.w;

				NP_AirInFront( air, _AirDensity, _WorldSpaceCameraPos, surfacePoint, i.sphere.xyz, i.sphere.w, light, _AirLight * dot( _LightColor0.rgb, 0.3333 ), inscatter, transmittance );

				color = color * transmittance + inscatter;

				return float4( NP_ToneMap( color ), 1.0 );
			}
			ENDCG
		}
	}
}
