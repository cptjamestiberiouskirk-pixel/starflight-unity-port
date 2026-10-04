
using UnityEngine;

using System.Diagnostics;
using System.Threading.Tasks;

public class PG_Craters
{
	static float[][,] m_craterTextureMaps;

	public static void Initialize()
	{
		// the crater texture maps never change, and they stay with us from one spaceflight scene to the next - so they are
		// only read the first time (this ran again on every start of the scene, about six million pixels each time)
		if ( m_craterTextureMaps != null )
		{
			return;
		}

		// load crater texture maps
		var craterTextureMaps = new float[ 3 ][,];

		for ( var i = 0; i < craterTextureMaps.Length; i++ )
		{
			var texture = Resources.Load<Texture2D>( "Craters " + ( i + 1 ) );

			craterTextureMaps[ i ] = new float[ texture.height, texture.width ];

			// one pixel at a time on purpose: GetPixels is faster, but for about one pixel in a hundred of these 16 bit textures
			// it gives a number that differs in the last bit, and every planet is worked out from these numbers
			for ( var y = 0; y < texture.height; y++ )
			{
				for ( var x = 0; x < texture.width; x++ )
				{
					var color = texture.GetPixel( x, y );

					craterTextureMaps[ i ][ y, x ] = color.r;
				}
			}
		}

		// everything has been read, so the maps can be used now
		m_craterTextureMaps = craterTextureMaps;
	}

	public float[,] Process( float[,] sourceElevation, int planetId, float craterGain, float waterElevation )
	{
		// UnityEngine.Debug.Log( "*** Craters Process ***" );

		// var stopwatch = new Stopwatch();

		// stopwatch.Start();

		var outputElevationWidth = sourceElevation.GetLength( 1 );
		var outputElevationHeight = sourceElevation.GetLength( 0 );

		var outputElevation = new float[ outputElevationHeight, outputElevationWidth ];

		var parallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = -1 };

		var numParallelThreads = 32;

		var rowsPerThread = outputElevationHeight / numParallelThreads;

		var craterStart = waterElevation;
		var craterRange = 1.0f - craterStart;

		var numCraterMaps = m_craterTextureMaps.Length;
		var texture = m_craterTextureMaps[ planetId % numCraterMaps ];

		// we'll flip half of the crater textures horizontally, and half of them vertically, which will give us more variation
		var flipHorizontal = ( planetId % ( numCraterMaps * 2 ) ) >= numCraterMaps;
		var flipVertical = ( planetId % ( numCraterMaps * 4 ) ) >= ( numCraterMaps * 2 );

		var startingY = flipVertical ? ( outputElevationHeight - 1 ) : 0;
		var startingX = flipHorizontal ? ( outputElevationWidth - 1 ) : 0;

		Parallel.For( 0, numParallelThreads, parallelOptions, j =>
		{
			for ( var row = 0; row < rowsPerThread; row++ )
			{
				var y = j * rowsPerThread + row;

				var craterY = Mathf.Abs( startingY - y );

				for ( var x = 0; x < outputElevationWidth; x++ )
				{
					var craterX = Mathf.Abs( startingX - x );

					var craterMultiplier = Mathf.Sqrt( Mathf.Lerp( 0.0f, 1.0f, ( sourceElevation[ y, x ] - craterStart ) / craterRange ) );

					outputElevation[ y, x ] = sourceElevation[ y, x ] + texture[ craterY, craterX ] * craterMultiplier * craterGain;
				}
			}
		} );

		// UnityEngine.Debug.Log( "Output - " + stopwatch.ElapsedMilliseconds + " milliseconds" );

		return outputElevation;
	}
}
