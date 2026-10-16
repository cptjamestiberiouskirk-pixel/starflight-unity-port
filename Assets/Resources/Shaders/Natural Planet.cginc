// what the shaders of the natural look of the planets share: the air round a planet, worked out by single scattering in planet radii round the planet's
// middle (Rayleigh for the colour of the sky, Mie for the bright haze on the sun's side), and the filmic tone curve that brings the light down to the screen
#ifndef NATURAL_PLANET_INCLUDED
#define NATURAL_PLANET_INCLUDED

// where a ray (origin o, unit direction d) goes into and out of a sphere of radius r round the origin: (in, out), out < in when it misses
float2 NP_RaySphere( float3 o, float3 d, float r )
{
	float b = dot( o, d );
	float c = dot( o, o ) - r * r;
	float h = b * b - c;

	if ( h < 0.0 )
	{
		return float2( 1.0, -1.0 );
	}

	h = sqrt( h );

	return float2( -b - h, -b + h );
}

// the air of a planet: how high it reaches and how fast it thins out (in planet radii), and how strongly it scatters and dims each colour
struct NP_Air
{
	float top;
	float scaleHeight;
	float3 betaRayleigh;
	float3 betaMie;
	float3 betaExtinction;
};

// height: how high the air reaches (planet radii); scaleShare: its scale height as a share of that; color and tint: the colour the air is drawn
// towards from a clean sky's blue, and how far; density: how thick it is
NP_Air NP_MakeAir( float height, float scaleShare, float3 color, float tint, float density )
{
	NP_Air air;

	air.top = 1.0 + height;
	air.scaleHeight = max( height * scaleShare, 1e-4 );

	float3 rayleighBlue = normalize( float3( 5.8, 13.5, 33.1 ) );
	float3 tintColor = normalize( max( color, 0.001 ) );

	air.betaRayleigh = lerp( rayleighBlue, tintColor, tint ) * 9.0 * density;
	air.betaMie = 0.6 * density;
	air.betaExtinction = air.betaRayleigh + air.betaMie * 1.1;

	return air;
}

// the sunlight the air scatters towards the eye along a line of sight (from origin in direction, between start and end, all in planet radii) and how
// much of what lies behind gets through it
void NP_Scatter( NP_Air air, float3 origin, float3 direction, float start, float end, float3 light, float sunIntensity, out float3 inscatter, out float3 transmittance )
{
	const int steps = 12;
	const int sunSteps = 4;

	float stepLength = max( end - start, 0.0 ) / steps;
	float3 opticalDepth = 0.0;
	float3 scattered = 0.0;

	for ( int s = 0; s < steps; s++ )
	{
		float3 p = origin + direction * ( start + ( s + 0.5 ) * stepLength );
		float height = length( p ) - 1.0;
		float density = exp( -height / air.scaleHeight ) * stepLength;

		opticalDepth += density;

		// the light from the sun to this point: none where the planet is in the way
		float2 sunGround = NP_RaySphere( p, light, 1.0 );

		if ( ( sunGround.x < sunGround.y ) && ( sunGround.x > 0.0 ) )
		{
			continue;
		}

		float sunLength = NP_RaySphere( p, light, air.top ).y / sunSteps;
		float sunDepth = 0.0;

		for ( int k = 0; k < sunSteps; k++ )
		{
			float3 q = p + light * ( ( k + 0.5 ) * sunLength );
			sunDepth += exp( -( length( q ) - 1.0 ) / air.scaleHeight ) * sunLength;
		}

		scattered += density * exp( -air.betaExtinction * ( opticalDepth + sunDepth ) );
	}

	float mu = dot( direction, light );
	float phaseRayleigh = 0.0596831 * ( 1.0 + mu * mu );
	float g = 0.76;
	float phaseMie = 0.0795775 * ( 1.0 - g * g ) / pow( max( 1.0 + g * g - 2.0 * g * mu, 1e-4 ), 1.5 );

	inscatter = sunIntensity * scattered * ( air.betaRayleigh * phaseRayleigh + air.betaMie * phaseMie );
	transmittance = exp( -air.betaExtinction * opticalDepth );
}

// the air between the eye and a point (both in world space) of a planet with this middle and radius: what it adds to the point's light and how much of
// the point's light gets through (nothing added and everything through for a planet without air)
void NP_AirInFront( NP_Air air, float density, float3 eye, float3 worldPoint, float3 center, float radius, float3 light, float sunIntensity, out float3 inscatter, out float3 transmittance )
{
	inscatter = 0.0;
	transmittance = 1.0;

	if ( ( density <= 0.0 ) || ( radius <= 0.0 ) )
	{
		return;
	}

	float3 origin = ( eye - center ) / radius;
	float3 toPoint = ( worldPoint - eye ) / radius;
	float distance = length( toPoint );
	float3 direction = toPoint / max( distance, 1e-5 );
	float2 shell = NP_RaySphere( origin, direction, air.top );

	if ( shell.y < shell.x )
	{
		return;
	}

	float start = max( shell.x, 0.0 );
	float end = min( distance, shell.y );

	if ( end <= start )
	{
		return;
	}

	NP_Scatter( air, origin, direction, start, end, light, sunIntensity, inscatter, transmittance );
}

// the direction to the sun from a point (the main light: a directional light, or a point light where the star is)
float3 NP_LightDirection( float3 worldPoint )
{
	return normalize( _WorldSpaceLightPos0.xyz - worldPoint * _WorldSpaceLightPos0.w );
}

// sunlight reddened by the air it crosses on the way down (more air when the sun is low)
float3 NP_SunTint( float sunHeight, float airThickness )
{
	float airMass = 1.0 / ( saturate( sunHeight ) + 0.06 );

	return exp( -float3( 0.012, 0.03, 0.075 ) * ( airMass - 1.0 ) * airThickness );
}

// the filmic tone curve (ACES, as fitted by Krzysztof Narkowicz): soft highlights that never clip, a little more contrast in the middle
float3 NP_ToneMap( float3 x )
{
	x = max( x, 0.0 );

	return saturate( ( x * ( 2.51 * x + 0.03 ) ) / ( x * ( 2.43 * x + 0.59 ) + 0.14 ) );
}

#endif
