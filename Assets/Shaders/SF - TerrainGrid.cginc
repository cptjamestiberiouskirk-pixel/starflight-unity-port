
#ifndef SF_SHADER_TERRAIN_GRID
#define SF_SHADER_TERRAIN_GRID

#include "SF - Core.cginc"

float4 SF_ElevationMap_TexelSize;

// the Catmull-Rom spline through four heights, as PlanetGenerator.InterpolateHermite draws it on the CPU
float catmullRomElevation( float v0, float v1, float v2, float v3, float t )
{
	return 0.5 * ( 2.0 * v1 + ( v2 - v0 ) * t + ( 2.0 * v0 - 5.0 * v1 + 4.0 * v2 - v3 ) * t * t + ( -v0 + 3.0 * v1 - 3.0 * v2 + v3 ) * t * t * t );
}

// the height of the ground smoothed through the 16 texels about this place, as PlanetGenerator.GetBicubicSmoothedElevation works it out for the terrain
// vehicle and everything placed on the ground (one bilinear tap made the hills of flat facets between the texels, a little off the heights they stand at);
// each tap is at a texel's middle, so it reads that texel alone, and the map repeats east to west and stops at the poles as the CPU's does
float getSmoothedElevation( float2 texCoord )
{
	float2 position = texCoord * SF_ElevationMap_TexelSize.zw - 0.5;
	float2 corner = floor( position );
	float2 t = position - corner;

	float rows[ 4 ];

	[unroll]
	for ( int y = 0; y < 4; y++ )
	{
		float v[ 4 ];

		[unroll]
		for ( int x = 0; x < 4; x++ )
		{
			v[ x ] = tex2Dlod( SF_ElevationMap, float4( ( corner + float2( x - 1, y - 1 ) + 0.5 ) * SF_ElevationMap_TexelSize.xy, 0, 0 ) ).r;
		}

		rows[ y ] = catmullRomElevation( v[ 0 ], v[ 1 ], v[ 2 ], v[ 3 ], t.x );
	}

	return catmullRomElevation( rows[ 0 ], rows[ 1 ], rows[ 2 ], rows[ 3 ], t.y );
}

float SF_GroundCavityStrength;

// how much the ground about a place curves in (a valley, a crater's floor, a crease) or out (a ridge, a crown): the heights 2, 4 and 8 texels away on each side
// (8, 16 and 32 world units) against the height here, as a change of slope. The ground is darkened where it curves in and lightened a little where it curves
// out, so that the shape of the land reads from the camera (GroundDetail: none at Low)
float getCavityOcclusion( float2 texCoord )
{
	if ( SF_GroundCavityStrength <= 0 )
	{
		return 1;
	}

	float center = tex2Dlod( SF_ElevationMap, float4( texCoord, 0, 0 ) ).r;
	float cavity = 0;

	[unroll]
	for ( int s = 1; s <= 3; s++ )
	{
		float r = exp2( s );
		float2 dx = float2( SF_ElevationMap_TexelSize.x * r, 0 );
		float2 dy = float2( 0, SF_ElevationMap_TexelSize.y * r );

		float around = tex2Dlod( SF_ElevationMap, float4( texCoord + dx, 0, 0 ) ).r + tex2Dlod( SF_ElevationMap, float4( texCoord - dx, 0, 0 ) ).r;

		around += tex2Dlod( SF_ElevationMap, float4( texCoord + dy, 0, 0 ) ).r + tex2Dlod( SF_ElevationMap, float4( texCoord - dy, 0, 0 ) ).r;

		// the rise of the ground about it, in world units over the distance (4 units a texel)
		cavity += ( around * 0.25 - center ) * SF_ElevationScale / ( r * 4 );
	}

	cavity /= 3;

	return 1 - SF_GroundCavityStrength * clamp( cavity * 4, -0.15, 0.6 );
}

float4 getTerrainGridWorldCoordinates( SF_VertexShaderInput v, out float2 texCoord )
{
	float4 positionWorld = mul( unity_ObjectToWorld, v.position );

	texCoord = TRANSFORM_TEX( positionWorld.xz, SF_ElevationMap );

	float elevation = getSmoothedElevation( texCoord ) * SF_ElevationScale;

	positionWorld.y += elevation;

	return positionWorld;
}

SF_VertexShaderOutput vertTerrainGrid_SF( SF_VertexShaderInput v )
{
	SF_VertexShaderOutput o;

	float2 texCoord;

	float4 positionWorld = getTerrainGridWorldCoordinates( v, texCoord );
	float3 normalWorld = float3( 0, 1, 0 );

	o.positionClip = mul( UNITY_MATRIX_VP, positionWorld );
	o.color = v.color;
	o.color.rgb *= getCavityOcclusion( texCoord );
	o.texCoord0 = float4( texCoord, 0, 1 );
	o.texCoord1 = float4( v.texCoord1, 0, 1 );
	o.positionWorld = positionWorld;
	o.eyeDir = normalize( positionWorld.xyz - _WorldSpaceCameraPos );
	o.normalWorld = normalWorld;

	#if SF_NORMALMAP_ON || SF_DETAILNORMALMAP_ON

		float3 tangentWorld = normalize( mul( v.tangent.xyz, (float3x3) unity_WorldToObject ) );
		float3 binormalWorld = cross( normalWorld, tangentWorld ) * v.tangent.w * unity_WorldTransformParams.w;

		o.tangentWorld = tangentWorld;
		o.binormalWorld = binormalWorld;

	#endif // SF_NORMALMAP_ON || SF_DETAILNORMALMAP_ON
	
	#if SF_BEHINDEVERYTHING_ON

		o.texCoord1 = ComputeScreenPos( o.positionClip );

	#endif // SF_BEHINDEVERYTHING_ON

	return o;
}

SF_VertexShaderOutput vertTerrainGridShadowCaster_SF( SF_VertexShaderInput v )
{
	SF_VertexShaderOutput o;

	o = vertTerrainGrid_SF( v );

	if ( unity_LightShadowBias.z != 0 )
	{
		float3 wLight = normalize( UnityWorldSpaceLightDir( o.positionWorld.xyz ) );

		float shadowCos = dot( o.normalWorld, wLight );

		float shadowSine = sqrt( 1 - shadowCos * shadowCos );

		float normalBias = unity_LightShadowBias.z * shadowSine;

		o.positionWorld.xyz -= o.normalWorld * normalBias;
	}

	o.positionClip = UnityApplyLinearShadowBias( mul( UNITY_MATRIX_VP, o.positionWorld ) );

	return o;
}

#endif
