using UnityEngine;

// the grain of the ground up close (since 2026-10-17, ROADMAP D7): a seamless greyscale texture of fine, rough noise that the terrain shader lays over the planet's
// albedo map near the terrain vehicle, at two scales, so that the ground has grit and pebbly texture where the planet's map, four units to a pixel, is a blur
// (SF - Core.cginc, SF_GROUND_DETAIL). It is made once a session, from an integer hash: no random numbers are drawn, so nothing placed on a planet moves
public static class GroundGrain
{
	// the texture's size, and the number of octaves of noise (each twice as fine as the one before, every period dividing the size, so the texture tiles)
	const int c_size = 256;
	const int c_octaves = 5;
	const int c_firstCells = 8;

	// how strongly each level lays the grain over the ground (none at Low), how far from the camera it fades out, and its two scales (texture repeats a world
	// unit: one fine, one coarser, in a ratio that is not a whole number, so the tiling does not show)
	static readonly float[] c_strength = { 0.0f, 0.5f, 0.65f };
	static readonly float[] c_fadeDistance = { 1.0f, 160.0f, 260.0f };
	public const float c_fineScale = 1.0f / 5.0f;
	public const float c_coarseScale = 1.0f / 19.0f;

	static Texture2D s_texture;

	// the grain texture (made the first time it is asked for)
	public static Texture2D Get()
	{
		if ( s_texture != null )
		{
			return s_texture;
		}

		var pixels = new Color32[ c_size * c_size ];
		var values = new float[ c_size * c_size ];
		var smallest = float.MaxValue;
		var largest = float.MinValue;

		for ( var y = 0; y < c_size; y++ )
		{
			for ( var x = 0; x < c_size; x++ )
			{
				var value = 0.0f;
				var amplitude = 1.0f;
				var cells = c_firstCells;

				for ( var octave = 0; octave < c_octaves; octave++ )
				{
					value += amplitude * PeriodicNoise( x * cells / (float) c_size, y * cells / (float) c_size, cells, octave );

					amplitude *= 0.55f;
					cells *= 2;
				}

				values[ y * c_size + x ] = value;
				smallest = Mathf.Min( smallest, value );
				largest = Mathf.Max( largest, value );
			}
		}

		// spread over the whole range
		var range = Mathf.Max( 0.0001f, largest - smallest );

		for ( var i = 0; i < values.Length; i++ )
		{
			var level = (byte) Mathf.RoundToInt( 255.0f * ( values[ i ] - smallest ) / range );

			pixels[ i ] = new Color32( level, level, level, 255 );
		}

		s_texture = new Texture2D( c_size, c_size, TextureFormat.RGBA32, true, true )
		{
			name = "Ground Grain",
			wrapMode = TextureWrapMode.Repeat,
			filterMode = FilterMode.Trilinear,
			anisoLevel = 8,
			hideFlags = HideFlags.DontSave
		};

		s_texture.SetPixels32( pixels );
		s_texture.Apply( true, true );

		return s_texture;
	}

	// how strongly the grain is laid over the ground at the level in use (0 at Low), and how far from the camera it fades out
	public static float GetStrength()
	{
		return c_strength[ (int) GroundDetail.GetLevel() ];
	}

	public static float GetFadeDistance()
	{
		return c_fadeDistance[ (int) GroundDetail.GetLevel() ];
	}

	// value noise on a lattice of cells by cells that wraps round, smoothly interpolated
	static float PeriodicNoise( float x, float y, int cells, int octave )
	{
		var x0 = Mathf.FloorToInt( x );
		var y0 = Mathf.FloorToInt( y );
		var tx = x - x0;
		var ty = y - y0;

		tx = tx * tx * ( 3.0f - 2.0f * tx );
		ty = ty * ty * ( 3.0f - 2.0f * ty );

		var a = Lattice( x0, y0, cells, octave );
		var b = Lattice( x0 + 1, y0, cells, octave );
		var c = Lattice( x0, y0 + 1, cells, octave );
		var d = Lattice( x0 + 1, y0 + 1, cells, octave );

		return Mathf.Lerp( Mathf.Lerp( a, b, tx ), Mathf.Lerp( c, d, tx ), ty );
	}

	// the value at a lattice point, from an integer hash of it (wrapped, so the texture tiles)
	static float Lattice( int x, int y, int cells, int octave )
	{
		x = ( ( x % cells ) + cells ) % cells;
		y = ( ( y % cells ) + cells ) % cells;

		unchecked
		{
			var h = (uint) ( x * 374761393 + y * 668265263 + octave * 2147483647 );

			h = ( h ^ ( h >> 13 ) ) * 1274126177u;
			h ^= h >> 16;

			return ( h & 0xFFFF ) / 65535.0f;
		}
	}
}
