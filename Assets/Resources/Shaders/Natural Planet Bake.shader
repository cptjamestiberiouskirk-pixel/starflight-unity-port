// makes the maps of a planet's natural look on the graphics card (NaturalPlanet blits each pass into a render texture), from the planet's own maps as a
// blueprint (its elevation, and the colours of its albedo map for variety) and its climate in the game data
// pass 0: the height: the blueprint's elevation (the sea below 0), with finer coastlines, ridged mountain ranges and fine detail
// pass 1: the colours (rgb) and where the open sea is (a): the sea by its depth and what it is made of, the land by temperature, moisture and life,
//         bare regolith on a world without air, ice on a frozen world, rock on steep ground, snow and ice where it is cold
// pass 2: the normal map (x along u, y along v), from the height, flat on the sea
// pass 3: the cloud cover, as much as the planet's weather: latitude bands and cyclones
// pass 4: the colours of a gas giant: bands of cloud in the colours of its gases, laid out as its blueprint's bands, torn into eddies, with oval storms
// pass 5: the colours of a molten world (rgb) and how brightly it glows (a): a dark crust, glowing lava in its low ground and in the cracks of the crust
// every map is equirectangular (u the longitude, v the latitude), as the game's own planet maps are, and its noise comes from the direction on the sphere,
// so that it has no seam
// it is in a Resources folder so that it is always in a build: the material is made in code
Shader "Starflight/Natural Planet Bake"
{
	Properties
	{
		_Elevation( "Elevation (the blueprint: the sea below 0, -1 deepest, 1 highest)", 2D ) = "black" {}
		_Blueprint( "Blueprint Colours", 2D ) = "gray" {}
		_HeightMap( "Height (baked by pass 0)", 2D ) = "black" {}
		_Seed( "Seed", Vector ) = ( 0, 0, 0, 0 )
		_TexelSize( "Height Texel Size", Vector ) = ( 0.000244, 0.000488, 0, 0 )
		_CoastAmount( "Coastline Detail", Float ) = 0.1
		_NormalStrength( "Normal Strength", Float ) = 3
		_TemperatureMin( "Temperature At The Poles (0 sub-arctic .. 1 inferno)", Float ) = 0.2
		_TemperatureMax( "Temperature At The Equator", Float ) = 0.6
		_Moisture( "Moisture", Float ) = 1
		_Vegetation( "Vegetation", Float ) = 0
		_HasSea( "Has A Sea", Float ) = 1
		_SeaFrozen( "Sea Frozen", Float ) = 0
		_SeaShallow( "Sea Over The Shallows", Color ) = ( 0.06, 0.48, 0.58, 1 )
		_SeaDeep( "Open Sea", Color ) = ( 0.02, 0.16, 0.38, 1 )
		_SeaIce( "Sea Ice", Color ) = ( 0.88, 0.93, 0.97, 1 )
		_Airless( "Airless", Float ) = 0
		_Icy( "Frozen World", Float ) = 0
		_Weather( "Weather", Float ) = 0.5
		_GasLight( "Gas Giant Zones", Color ) = ( 0.92, 0.86, 0.74, 1 )
		_GasDark( "Gas Giant Belts", Color ) = ( 0.66, 0.48, 0.34, 1 )
		_GasAccent( "Gas Giant Storms", Color ) = ( 0.80, 0.42, 0.28, 1 )
		_DepositAmount( "Deposits On A Molten Crust", Float ) = 0.5
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	sampler2D _Elevation;
	sampler2D _Blueprint;
	sampler2D _HeightMap;
	float4 _Seed;
	float4 _TexelSize;
	float _CoastAmount;
	float _NormalStrength;
	float _TemperatureMin;
	float _TemperatureMax;
	float _Moisture;
	float _Vegetation;
	float _HasSea;
	float _SeaFrozen;
	float4 _SeaShallow;
	float4 _SeaDeep;
	float4 _SeaIce;
	float _Airless;
	float _Icy;
	float _Weather;
	float4 _GasLight;
	float4 _GasDark;
	float4 _GasAccent;
	float _DepositAmount;

	float3 Hash3( float3 p )
	{
		p = float3( dot( p, float3( 127.1, 311.7, 74.7 ) ), dot( p, float3( 269.5, 183.3, 246.1 ) ), dot( p, float3( 113.5, 271.9, 124.6 ) ) );

		return -1.0 + 2.0 * frac( sin( p ) * 43758.5453123 );
	}

	// gradient noise (about -0.7 to 0.7)
	float Noise( float3 p )
	{
		float3 i = floor( p );
		float3 f = frac( p );
		float3 u = f * f * f * ( f * ( f * 6.0 - 15.0 ) + 10.0 );

		return lerp( lerp( lerp( dot( Hash3( i + float3( 0, 0, 0 ) ), f - float3( 0, 0, 0 ) ), dot( Hash3( i + float3( 1, 0, 0 ) ), f - float3( 1, 0, 0 ) ), u.x ),
			lerp( dot( Hash3( i + float3( 0, 1, 0 ) ), f - float3( 0, 1, 0 ) ), dot( Hash3( i + float3( 1, 1, 0 ) ), f - float3( 1, 1, 0 ) ), u.x ), u.y ),
			lerp( lerp( dot( Hash3( i + float3( 0, 0, 1 ) ), f - float3( 0, 0, 1 ) ), dot( Hash3( i + float3( 1, 0, 1 ) ), f - float3( 1, 0, 1 ) ), u.x ),
			lerp( dot( Hash3( i + float3( 0, 1, 1 ) ), f - float3( 0, 1, 1 ) ), dot( Hash3( i + float3( 1, 1, 1 ) ), f - float3( 1, 1, 1 ) ), u.x ), u.y ), u.z );
	}

	// signed fractal noise (about -0.6 to 0.6)
	float Fbm( float3 p, int octaves )
	{
		float sum = 0.0;
		float amplitude = 0.5;

		for ( int i = 0; i < octaves; i++ )
		{
			sum += amplitude * Noise( p );
			p = p * 2.03 + float3( 1.7, 9.2, 4.1 );
			amplitude *= 0.5;
		}

		return sum;
	}

	// ridged fractal noise (0 to about 1): sharp crests, as mountain ranges have
	float Ridged( float3 p, int octaves )
	{
		float sum = 0.0;
		float amplitude = 0.5;
		float weight = 1.0;

		for ( int i = 0; i < octaves; i++ )
		{
			float n = 1.0 - abs( Noise( p ) * 1.6 );

			n = n * n * weight;
			weight = saturate( n * 1.8 );
			sum += n * amplitude;
			p = p * 2.07 + float3( 4.3, 1.1, 7.9 );
			amplitude *= 0.5;
		}

		return sum;
	}

	// thin dark lines that wander across ice (as the cracks on the icy moons): 1 on a line, 0 away from it
	float Cracks( float3 p )
	{
		float3 warp = float3( Fbm( p * 0.8, 3 ), Fbm( p * 0.8 + 4.1, 3 ), Fbm( p * 0.8 + 8.3, 3 ) );
		float lines = 1.0 - abs( Noise( p * 2.5 + warp * 2.0 ) ) * 6.0;

		return saturate( lines ) * saturate( lines ) * smoothstep( -0.1, 0.25, Fbm( p * 1.3 + 2.0, 3 ) );
	}

	// the direction on the sphere of a point of an equirectangular map
	float3 SphereDirection( float2 uv )
	{
		float longitude = uv.x * 6.2831853;
		float latitude = ( uv.y - 0.5 ) * 3.1415927;

		return float3( cos( latitude ) * cos( longitude ), sin( latitude ), cos( latitude ) * sin( longitude ) );
	}

	// a colour written as on screen, in the linear light the maps are worked out in
	float3 Srgb( float3 c )
	{
		return pow( c, 2.2 );
	}

	struct v2f
	{
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};

	v2f vert( appdata_img v )
	{
		v2f o;

		o.pos = UnityObjectToClipPos( v.vertex );
		o.uv = v.texcoord;

		return o;
	}

	// pass 0: the height
	float4 fragHeight( v2f i ) : SV_Target
	{
		// the blueprint, its low land and shallow sea lifted away from the coast, so that the coast's detail moves the shore only a little
		float rawBase = tex2D( _Elevation, i.uv ).r;
		float base = sign( rawBase ) * sqrt( abs( rawBase ) );
		float3 p = SphereDirection( i.uv ) + _Seed.xyz;

		float3 warp = float3( Fbm( p * 2.0, 3 ), Fbm( p * 2.0 + 5.2, 3 ), Fbm( p * 2.0 + 9.1, 3 ) );
		float coast = Fbm( p * 4.5 + warp * 1.2, 7 ) * _CoastAmount;
		float landish = base + coast;

		// mountain ranges on the land (much lower ones under the sea), and fine detail everywhere
		float mountains = Ridged( p * 6.0 + warp * 1.8, 7 );
		float mountainAmount = smoothstep( 0.0, 0.6, landish ) * 0.4 + 0.03;
		float fine = Fbm( p * 40.0, 4 ) * 0.02;
		float height = landish + ( mountains - 0.3 ) * mountainAmount * ( ( landish > 0.0 ) ? 1.0 : 0.25 ) + fine;

		// never so low on the land that it turns into sea, nor so high in the sea that it turns into land, but where the coast's detail does it
		height = ( landish > 0.0 ) ? max( height, landish * 0.5 ) : min( height, landish * 0.5 );

		return float4( height, 0, 0, 1 );
	}

	// pass 1: the colours, and where the open sea is
	float4 fragAlbedo( v2f i ) : SV_Target
	{
		float height = tex2D( _HeightMap, i.uv ).r;
		float3 direction = SphereDirection( i.uv );
		float3 p = direction + _Seed.xyz;
		float latitude = abs( i.uv.y - 0.5 ) * 2.0;

		// the slope (rock shows where the ground is steep)
		float heightEast = tex2D( _HeightMap, i.uv + float2( _TexelSize.x, 0 ) ).r;
		float heightNorth = tex2D( _HeightMap, i.uv + float2( 0, _TexelSize.y ) ).r;
		float slope = saturate( length( float2( ( heightEast - height ) / max( cos( ( i.uv.y - 0.5 ) * 3.1415927 ), 0.15 ), heightNorth - height ) ) * 90.0 );

		// a little variety everywhere, and the blueprint's own patterns
		float variation = Fbm( p * 18.0, 4 );
		float blueprint = dot( tex2D( _Blueprint, i.uv ).rgb, float3( 0.3, 0.59, 0.11 ) );
		float mineral = saturate( 0.5 + variation * 0.9 + ( blueprint - 0.4 ) * 0.8 );

		// temperature: from the equator's to the poles', cooler with height, a little uneven
		float temperature = lerp( _TemperatureMax, _TemperatureMin, pow( latitude, 1.3 ) ) - max( height, 0.0 ) * 0.3 + Fbm( p * 3.0, 3 ) * 0.08;

		float3 color;

		// the sea
		if ( ( height < 0.0 ) && ( _HasSea > 0.5 ) )
		{
			// a frozen sea: ice of the sea's own stuff, cracked
			if ( _SeaFrozen > 0.5 )
			{
				color = _SeaIce.rgb * ( 0.92 + variation * 0.2 );
				color = lerp( color, color * float3( 0.62, 0.50, 0.42 ), Cracks( p * 3.0 ) * 0.55 );

				return float4( color, 0 );
			}

			// open sea: lighter over the shallows, darker far out, ice where it is cold
			float depth = saturate( -height * 2.0 );

			color = lerp( _SeaShallow.rgb, _SeaDeep.rgb, smoothstep( 0.0, 0.12, depth ) );
			color = lerp( color, _SeaDeep.rgb * 0.45, smoothstep( 0.1, 0.45, depth ) );

			float seaIce = smoothstep( 0.1, 0.03, temperature + variation * 0.05 );

			color = lerp( color, _SeaIce.rgb, seaIce );

			return float4( color, 1.0 - seaIce );
		}

		// bare rock: from rust-brown to blue-grey basalt, varied by the blueprint's patterns
		float rockHue = saturate( 0.5 + Fbm( p * 1.7 + 11.0, 3 ) * 1.4 );
		float3 rock = lerp( Srgb( float3( 0.46, 0.26, 0.17 ) ), Srgb( float3( 0.44, 0.34, 0.25 ) ), rockHue );

		rock = lerp( rock, Srgb( float3( 0.24, 0.25, 0.28 ) ), smoothstep( 0.62, 0.85, rockHue ) );
		rock = lerp( Srgb( float3( 0.14, 0.12, 0.11 ) ), rock, smoothstep( 0.12, 0.4, mineral ) ) * ( 0.85 + 0.3 * mineral );

		// a frozen world: ice over most of it in broad brighter and darker terrains, rock on steep ground and the heights
		if ( _Icy > 0.5 )
		{
			float terrain = saturate( 0.5 + Fbm( p * 1.6 + 3.0, 4 ) * 1.3 );
			float3 frost = _SeaIce.rgb * lerp( 0.72, 1.04, terrain );

			frost = lerp( frost, frost * float3( 0.80, 0.68, 0.58 ), smoothstep( 0.62, 0.92, terrain ) * 0.7 );
			frost = lerp( frost, frost * float3( 0.62, 0.50, 0.42 ), Cracks( p * 2.2 + 5.0 ) * 0.45 );

			color = lerp( frost, Srgb( float3( 0.36, 0.34, 0.33 ) ) * ( 0.8 + 0.4 * mineral ), saturate( slope * 1.5 + smoothstep( 0.55, 0.9, height ) * 0.7 ) );

			return float4( color, 0 );
		}

		// a world without air: bare regolith, grey to brown, with dark plains in the low ground (as the moon's seas)
		if ( _Airless > 0.5 )
		{
			float3 regolith = lerp( Srgb( float3( 0.43, 0.41, 0.39 ) ), Srgb( float3( 0.52, 0.43, 0.34 ) ), rockHue ) * ( 0.72 + 0.5 * mineral );

			regolith = lerp( regolith, regolith * 0.45, smoothstep( 0.05, -0.2, height ) );

			// frost in the coldest places
			regolith = lerp( regolith, Srgb( float3( 0.86, 0.88, 0.92 ) ), smoothstep( 0.08, 0.02, temperature + variation * 0.04 ) );

			return float4( regolith, 0 );
		}

		// moisture: wet at the equator and near the sea, dry in the subtropics, patchy, and drier on a world with little water
		float coastal = smoothstep( 0.25, 0.0, height ) * _HasSea;
		float moisture = 0.45 + Fbm( p * 2.6, 4 ) * 0.7 + coastal * 0.25 + 0.25 * exp( -pow( latitude / 0.15, 2.0 ) ) - 0.4 * exp( -pow( ( latitude - 0.3 ) / 0.1, 2.0 ) );

		moisture -= ( 1.0 - _Moisture ) * 0.5;

		// deserts: yellow sand, orange and red, in broad swathes
		float sandHue = saturate( 0.5 + Fbm( p * 2.2 + 7.0, 4 ) * 1.5 );
		float3 desert = lerp( Srgb( float3( 0.88, 0.72, 0.44 ) ), Srgb( float3( 0.84, 0.52, 0.27 ) ), smoothstep( 0.2, 0.55, sandHue ) );

		desert = lerp( desert, Srgb( float3( 0.66, 0.29, 0.16 ) ), smoothstep( 0.55, 0.9, sandHue ) );

		// a hot world: rust and ochre plains with dark lava fields
		float3 scorched = lerp( Srgb( float3( 0.60, 0.32, 0.18 ) ), Srgb( float3( 0.80, 0.58, 0.34 ) ), saturate( 0.5 + variation * 1.4 ) );

		scorched = lerp( Srgb( float3( 0.16, 0.13, 0.12 ) ), scorched, smoothstep( 0.2, 0.45, mineral ) );

		float arid = smoothstep( 0.55, 0.25, moisture ) * smoothstep( 0.3, 0.55, temperature );

		color = lerp( rock, desert, arid );
		color = lerp( color, scorched, smoothstep( 0.72, 0.9, temperature ) );

		// salt flats beside the sea where it is hot and dry
		color = lerp( color, Srgb( float3( 0.92, 0.90, 0.84 ) ), coastal * arid * smoothstep( 0.1, 0.3, Fbm( p * 9.0, 3 ) ) * 0.8 );

		// life: forest, jungle, grassland and savanna where it is wet and mild, as much as the planet has life
		float3 forest = lerp( Srgb( float3( 0.05, 0.22, 0.06 ) ), Srgb( float3( 0.10, 0.30, 0.08 ) ), saturate( 0.5 + variation ) );
		float3 jungle = Srgb( float3( 0.03, 0.19, 0.05 ) );
		float3 boreal = Srgb( float3( 0.08, 0.18, 0.10 ) );
		float3 grass = Srgb( float3( 0.44, 0.52, 0.18 ) );
		float3 savanna = Srgb( float3( 0.64, 0.58, 0.28 ) );
		float3 plants = lerp( savanna, grass, smoothstep( 0.35, 0.5, moisture ) );

		plants = lerp( plants, forest, smoothstep( 0.5, 0.7, moisture ) );
		plants = lerp( plants, jungle, smoothstep( 0.55, 0.8, temperature ) * smoothstep( 0.6, 0.8, moisture ) );
		plants = lerp( boreal, plants, smoothstep( 0.2, 0.35, temperature ) );

		float growing = _Vegetation * smoothstep( 0.3, 0.55, moisture ) * smoothstep( 0.12, 0.25, temperature ) * smoothstep( 0.85, 0.65, temperature );

		color = lerp( color, plants, growing );

		// tundra between the forests and the ice
		color = lerp( color, Srgb( float3( 0.46, 0.42, 0.34 ) ), smoothstep( 0.22, 0.12, temperature ) * 0.7 );

		// rock on steep ground and high up
		color = lerp( color, rock * 0.9, saturate( slope * 1.2 + smoothstep( 0.45, 0.8, height ) * 0.6 ) );

		// snow and ice where it is cold enough (the high peaks too)
		float snow = smoothstep( 0.12, 0.04, temperature + variation * 0.06 - slope * 0.08 );

		color = lerp( color, Srgb( float3( 0.94, 0.96, 0.99 ) ), snow );

		// a little more colour, as a planet seen through clean air has
		float luminance = dot( color, float3( 0.2126, 0.7152, 0.0722 ) );

		color = max( luminance + ( color - luminance ) * 1.25, 0.0 );

		return float4( color, 0 );
	}

	// pass 2: the normal map (x along u, y along v), from the height, flat on the sea
	float4 fragNormal( v2f i ) : SV_Target
	{
		float height = tex2D( _HeightMap, i.uv ).r;
		float east = tex2D( _HeightMap, i.uv + float2( _TexelSize.x, 0 ) ).r;
		float west = tex2D( _HeightMap, i.uv - float2( _TexelSize.x, 0 ) ).r;
		float north = tex2D( _HeightMap, i.uv + float2( 0, _TexelSize.y ) ).r;
		float south = tex2D( _HeightMap, i.uv - float2( 0, _TexelSize.y ) ).r;

		float latitudeScale = max( cos( ( i.uv.y - 0.5 ) * 3.1415927 ), 0.15 );
		float land = ( ( height > 0.0 ) || ( _HasSea < 0.5 ) ) ? 1.0 : 0.0;
		float seaLevel = ( _HasSea > 0.5 ) ? 0.0 : -10.0;
		float2 gradient = float2( ( max( east, seaLevel ) - max( west, seaLevel ) ) / latitudeScale, max( north, seaLevel ) - max( south, seaLevel ) ) * _NormalStrength * land;
		float3 normal = normalize( float3( -gradient, 1.0 ) );

		return float4( normal * 0.5 + 0.5, 1 );
	}

	// pass 3: the cloud cover
	float4 fragClouds( v2f i ) : SV_Target
	{
		if ( _Weather < 0.01 )
		{
			return 0;
		}

		float3 direction = SphereDirection( i.uv );
		float latitude = abs( i.uv.y - 0.5 ) * 2.0;

		// cyclones: the air turns round a few centres in the middle latitudes (anticlockwise in the north), more and stronger in rough weather
		float3 turned = direction;
		float boost = 0.0;

		for ( int c = 0; c < 8; c++ )
		{
			float3 r = frac( sin( float3( c * 12.9898 + _Seed.x, c * 78.233 + _Seed.y, c * 37.719 + _Seed.z ) ) * 43758.5453 );
			float centreLatitude = ( 0.3 + 0.4 * r.x ) * ( ( r.y > 0.5 ) ? 1.0 : -1.0 ) * 1.5707963;
			float centreLongitude = r.z * 6.2831853;
			float3 centre = float3( cos( centreLatitude ) * cos( centreLongitude ), sin( centreLatitude ), cos( centreLatitude ) * sin( centreLongitude ) );
			float radius = 0.22 + 0.12 * frac( r.x * 7.0 );
			float distance = acos( saturate( dot( turned, centre ) ) );
			float strength = exp( -pow( distance / radius, 2.0 ) ) * step( c, 2.0 + _Weather * 6.0 );
			float angle = strength * ( 1.5 + 2.5 * _Weather ) * ( ( centreLatitude > 0.0 ) ? 1.0 : -1.0 );

			// turn the point round the centre's axis (Rodrigues' rotation formula)
			float cosine = cos( angle );
			float sine = sin( angle );

			turned = turned * cosine + cross( centre, turned ) * sine + centre * dot( centre, turned ) * ( 1.0 - cosine );
			boost += strength * 0.25;
		}

		float3 p = turned * 3.0 + _Seed.xyz;
		float3 warp = float3( Fbm( p * 1.5, 3 ), Fbm( p * 1.5 + 3.3, 3 ), Fbm( p * 1.5 + 7.1, 3 ) );
		float field = Fbm( p * 1.8 + warp * 1.6, 6 ) + Fbm( p * 9.0 + warp, 3 ) * 0.25;

		// streaks along the bands
		field += Fbm( float3( turned.x * 3.0, turned.y * 10.0, turned.z * 3.0 ) + _Seed.xyz, 3 ) * 0.15;

		// the bands: a cloudy equator, clear subtropics, stormy middle latitudes, some cloud over the poles
		float bands = 0.35 * exp( -pow( latitude / 0.07, 2.0 ) ) + 0.35 * exp( -pow( ( latitude - 0.6 ) / 0.16, 2.0 ) ) - 0.3 * exp( -pow( ( latitude - 0.28 ) / 0.09, 2.0 ) ) + 0.12 * smoothstep( 0.75, 1.0, latitude );

		float cover = field + bands * _Weather + boost * _Weather * 1.5;
		float threshold = lerp( 0.42, 0.14, _Weather );

		return float4( smoothstep( threshold, threshold + 0.28, cover ), 0, 0, 1 );
	}

	// an oval storm: how strongly a point of the sphere is inside it (1 in its middle), and the point turned round its middle as the storm turns it
	float Storm( float3 direction, float3 centre, float2 size, float spin, inout float3 turned )
	{
		// the storm is an ellipse on the sphere, wider along the longitude than across it
		float3 east = normalize( cross( float3( 0, 1, 0 ), centre ) );
		float3 north = cross( centre, east );
		float2 offset = float2( dot( direction - centre, east ), dot( direction - centre, north ) ) / size;
		float inside = exp( -dot( offset, offset ) * 2.0 ) * step( 0.0, dot( direction, centre ) );
		float angle = inside * spin;
		float cosine = cos( angle );
		float sine = sin( angle );

		turned = turned * cosine + cross( centre, turned ) * sine + centre * dot( centre, turned ) * ( 1.0 - cosine );

		return inside;
	}

	// pass 4: the colours of a gas giant: bands of cloud along the latitudes, light zones and dark belts in the colours of its gases, laid out as its
	// blueprint's own bands, their edges torn into eddies as the bands slide past each other, and a few oval storms, one of them great
	float4 fragGasGiant( v2f i ) : SV_Target
	{
		float3 direction = SphereDirection( i.uv );
		float3 turned = direction;

		// the storms (where they are comes from the seed: the great one in the southern half, as on Jupiter, the small white ones in the bands)
		float3 r = frac( sin( _Seed.xyz * float3( 12.9898, 78.233, 37.719 ) ) * 43758.5453 );
		float greatLatitude = -( 0.25 + 0.2 * r.x ) * 1.5707963;
		float greatLongitude = r.y * 6.2831853;
		float3 greatCentre = float3( cos( greatLatitude ) * cos( greatLongitude ), sin( greatLatitude ), cos( greatLatitude ) * sin( greatLongitude ) );
		float great = Storm( direction, greatCentre, float2( 0.22, 0.12 ), 2.5, turned );
		float white = 0.0;

		for ( int s = 0; s < 6; s++ )
		{
			float3 q = frac( sin( float3( s * 12.9898 + _Seed.x, s * 78.233 + _Seed.y, s * 37.719 + _Seed.z ) ) * 43758.5453 );
			float stormLatitude = ( q.x * 1.4 - 0.7 ) * 1.5707963;
			float stormLongitude = q.y * 6.2831853;
			float3 centre = float3( cos( stormLatitude ) * cos( stormLongitude ), sin( stormLatitude ), cos( stormLatitude ) * sin( stormLongitude ) );

			white = max( white, Storm( direction, centre, float2( 0.05, 0.03 ) * ( 0.6 + q.z ), 2.0, turned ) );
		}

		// the flow: the bands slide along the longitude, and tear into eddies where they meet
		float3 p = turned * 2.0 + _Seed.xyz;
		float latitude = asin( clamp( turned.y, -1.0, 1.0 ) ) / 1.5707963;
		float eddies = Fbm( float3( p.x * 1.5, latitude * 18.0, p.z * 1.5 ), 5 );
		float bandLatitude = latitude + eddies * 0.035 + Fbm( p * 6.0, 3 ) * 0.008;

		// the band at that latitude: the blueprint's elevation says zone (light) or belt (dark), and a little more variety
		float band = tex2D( _Elevation, float2( i.uv.x, bandLatitude * 0.5 + 0.5 ) ).r;
		float zone = saturate( 0.5 + band * 0.5 + sin( bandLatitude * 19.0 + _Seed.x ) * 0.28 + sin( bandLatitude * 43.0 + _Seed.y ) * 0.14 + eddies * 0.6 );

		float3 color = lerp( _GasDark.rgb, _GasLight.rgb, smoothstep( 0.25, 0.75, zone ) );

		// streaks of the accent colour in the belts, and lighter haze toward the poles
		color = lerp( color, _GasAccent.rgb, smoothstep( 0.55, 0.85, Fbm( float3( p.x * 3.0, latitude * 40.0, p.z * 3.0 ) + 3.0, 4 ) + 0.5 ) * ( 1.0 - zone ) * 0.6 );
		color = lerp( color, _GasLight.rgb * 0.9, smoothstep( 0.6, 0.95, abs( latitude ) ) * 0.5 );

		// the great storm in the accent colour with a lighter rim, the small storms white
		color = lerp( color, _GasAccent.rgb * 0.95, smoothstep( 0.35, 0.8, great ) );
		color = lerp( color, _GasLight.rgb * 1.08, smoothstep( 0.15, 0.35, great ) * smoothstep( 0.6, 0.35, great ) * 0.5 );
		color = lerp( color, float3( 0.95, 0.94, 0.92 ), smoothstep( 0.3, 0.7, white ) * 0.85 );

		return float4( color, 0 );
	}

	// pass 5: the colours of a molten world: a dark crust of cooled rock (with the deposits of what its seas are made of), glowing lava in its low ground
	// and in the cracks of the crust; the alpha is how brightly it glows
	float4 fragMolten( v2f i ) : SV_Target
	{
		float height = tex2D( _HeightMap, i.uv ).r;
		float3 direction = SphereDirection( i.uv );
		float3 p = direction + _Seed.xyz;

		float variation = Fbm( p * 18.0, 4 );
		float blueprint = dot( tex2D( _Blueprint, i.uv ).rgb, float3( 0.3, 0.59, 0.11 ) );
		float mineral = saturate( 0.5 + variation * 0.9 + ( blueprint - 0.4 ) * 0.8 );

		// the crust: black to dark grey basalt, with broad patches of the deposits of the seas' stuff
		float3 crust = lerp( Srgb( float3( 0.06, 0.05, 0.05 ) ), Srgb( float3( 0.22, 0.20, 0.19 ) ), mineral );
		float deposits = smoothstep( 0.1, 0.5, Fbm( p * 2.5 + 7.0, 4 ) ) * _DepositAmount;

		crust = lerp( crust, _SeaShallow.rgb * ( 0.7 + 0.4 * mineral ), deposits );

		// thin glowing cracks in the crust of the land, a little wider in the low ground
		float3 warp = float3( Fbm( p * 3.0, 3 ), Fbm( p * 3.0 + 4.1, 3 ), Fbm( p * 3.0 + 8.3, 3 ) );
		float crack = 1.0 - abs( Noise( p * 9.0 + warp * 1.5 ) ) * ( 10.0 - 4.0 * smoothstep( 0.4, 0.0, height ) );

		crack = saturate( crack ) * saturate( crack );

		// the lava: the low ground, as the blueprint has its seas there (they are lava on a molten world), crusted over in dark plates that drift apart
		// along glowing seams, the seams wider and brighter far from the shore, where the lava is hotter
		float lava = ( _HasSea > 0.5 ) ? smoothstep( 0.0, -0.06, height ) : 0.0;
		float hot = smoothstep( -0.05, -0.4, height );
		float seam = 1.0 - abs( Noise( p * 8.0 + warp * 2.0 ) ) * lerp( 20.0, 10.0, hot );
		float seams = saturate( seam ) * saturate( seam );
		float glow = max( lava * seams, crack * 0.8 * ( 1.0 - lava ) );

		// (the colours as well as the glow: the terrain draws the ground with the colours alone, so the lava has to read as lava in them, dark red plates
		// with orange seams)
		float3 color = lerp( crust, Srgb( float3( 0.30, 0.08, 0.03 ) ), lava );

		color = lerp( color, Srgb( float3( 0.95, 0.42, 0.10 ) ), saturate( seams * lava + crack * 0.7 * ( 1.0 - lava ) ) );

		return float4( color, saturate( glow ) );
	}
	ENDCG

	SubShader
	{
		ZTest Always Cull Off ZWrite Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragHeight
			#pragma target 3.5
			ENDCG
		}

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragAlbedo
			#pragma target 3.5
			ENDCG
		}

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragNormal
			#pragma target 3.5
			ENDCG
		}

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragClouds
			#pragma target 3.5
			ENDCG
		}

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragGasGiant
			#pragma target 3.5
			ENDCG
		}

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragMolten
			#pragma target 3.5
			ENDCG
		}
	}
}
