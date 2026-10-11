
using UnityEngine;

/// <summary>
/// The materials, textures and meshes the combat effects (explosions, hull and shield hits, lasers, missiles) share, made once in code,
/// and the particle system setup they have in common.
/// </summary>
public static class EffectMaterials
{
	// the size of the textures that are made for the effects
	const int c_textureSize = 128;

	// the materials and the debris meshes, made once and shared by every effect (they are made in code, so nothing in the build has to refer to them)
	public static Material m_glowMaterial;
	public static Material m_fireMaterial;
	public static Material m_smokeMaterial;
	public static Material m_ringMaterial;
	public static Material m_debrisMaterial;
	public static Material m_beamMaterial;
	public static Mesh[] m_debrisMeshes;

	/// <summary>
	/// Make the materials, textures and debris meshes the effects share (once: they are kept when a scene unloads).
	/// </summary>
	public static void Initialize()
	{
		// already made (a destroyed material compares equal to null, so they are made again if anything destroyed them)
		if ( ( m_glowMaterial != null ) && ( m_fireMaterial != null ) && ( m_smokeMaterial != null ) && ( m_ringMaterial != null ) && ( m_debrisMaterial != null ) && ( m_beamMaterial != null ) && ( m_debrisMeshes != null ) )
		{
			return;
		}

		// the shader is in a Resources folder, so it is in every build
		var shader = Resources.Load<Shader>( "Shaders/Explosion Particle" );

		if ( shader == null )
		{
			Debug.LogWarning( "EffectMaterials: the explosion particle shader was not found, using the sprite shader" );

			shader = Shader.Find( "Sprites/Default" );
		}

		// the flash, the sparks and the shockwave only add light, the fire also hides some of what is behind it (so it stays orange over a blue planet), smoke and debris cover it
		m_glowMaterial = CreateMaterial( "Explosion Glow", shader, MakeTexture( "Explosion Glow", 64, GlowAlpha ), 0.0f );
		m_fireMaterial = CreateMaterial( "Explosion Fire", shader, MakeTexture( "Explosion Fire", c_textureSize, FireAlpha ), 0.6f );
		m_ringMaterial = CreateMaterial( "Explosion Shockwave", shader, MakeTexture( "Explosion Shockwave", c_textureSize, RingAlpha ), 0.0f );
		m_smokeMaterial = CreateMaterial( "Explosion Smoke", shader, MakeTexture( "Explosion Smoke", c_textureSize, SmokeAlpha ), 1.0f );
		m_debrisMaterial = CreateMaterial( "Explosion Debris", shader, MakeShadeTexture(), 1.0f );

		// a laser beam only adds light
		m_beamMaterial = CreateMaterial( "Laser Beam", shader, MakeBeamTexture(), 0.0f );

		m_debrisMeshes = new Mesh[] { MakeShardMesh(), MakeSliverMesh() };
	}

	// make a material for the explosion particles (occlusion: how much of what is behind a particle it hides, from 0 for pure glow to 1 for ordinary transparency)
	static Material CreateMaterial( string name, Shader shader, Texture2D texture, float occlusion )
	{
		var material = new Material( shader );
		material.name = name;
		material.mainTexture = texture;
		material.SetFloat( "_Occlusion", occlusion );
		material.renderQueue = 3000;

		// kept for the whole session, like the textures (Resources.UnloadUnusedAssets would otherwise free them between scenes)
		material.hideFlags = HideFlags.DontUnloadUnusedAsset;

		return material;
	}

	// make a white texture whose alpha is the shape of a particle (alphaAt gets the distance from the middle, 0 to 1 at the edge, and the position from -1 to 1)
	static Texture2D MakeTexture( string name, int size, System.Func<float, float, float, float> alphaAt )
	{
		var texture = new Texture2D( size, size, TextureFormat.RGBA32, true );
		texture.name = name;
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Bilinear;
		texture.hideFlags = HideFlags.DontUnloadUnusedAsset;

		var pixels = new Color32[ size * size ];

		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				// position from -1 to 1, through the middle of the pixel
				var u = ( ( x + 0.5f ) / size ) * 2.0f - 1.0f;
				var v = ( ( y + 0.5f ) / size ) * 2.0f - 1.0f;

				var distance = Mathf.Sqrt( u * u + v * v );

				// nothing outside the circle, so no particle shows the corners of its square
				var alpha = ( distance >= 1.0f ) ? 0.0f : Mathf.Clamp01( alphaAt( distance, u, v ) );

				pixels[ y * size + x ] = new Color32( 255, 255, 255, (byte) Mathf.RoundToInt( alpha * 255.0f ) );
			}
		}

		texture.SetPixels32( pixels );
		texture.Apply( true, true );

		return texture;
	}

	// a soft glow with a hot middle
	static float GlowAlpha( float distance, float u, float v )
	{
		var falloff = 1.0f - distance;

		return falloff * falloff * 0.6f + Mathf.Pow( falloff, 6.0f ) * 0.4f;
	}

	// a puff of fire: a round glow with ragged, billowing edges and brighter and darker wisps inside
	static float FireAlpha( float distance, float u, float v )
	{
		var edgeNoise = FractalNoise( u * 3.0f, v * 3.0f, 11 );
		var innerNoise = FractalNoise( u * 6.0f + 17.0f, v * 6.0f + 5.0f, 23 );

		var ragged = distance + ( edgeNoise - 0.5f ) * 0.75f;
		var shape = 1.0f - Mathf.SmoothStep( 0.25f, 0.9f, ragged );

		return shape * ( 0.3f + 0.7f * innerNoise * innerNoise ) * Mathf.Clamp01( ( 1.0f - distance ) * 4.0f );
	}

	// a puff of smoke: softer and lumpier than the fire
	static float SmokeAlpha( float distance, float u, float v )
	{
		var edgeNoise = FractalNoise( u * 2.0f + 31.0f, v * 2.0f + 7.0f, 37 );
		var innerNoise = FractalNoise( u * 4.0f + 3.0f, v * 4.0f + 41.0f, 53 );

		var ragged = distance + ( edgeNoise - 0.5f ) * 0.6f;
		var shape = 1.0f - Mathf.SmoothStep( 0.15f, 1.0f, ragged );

		return shape * ( 0.45f + 0.55f * innerNoise ) * Mathf.Clamp01( ( 1.0f - distance ) * 4.0f );
	}

	// the shockwave: a bright thin ring near the edge with a faint haze inside it
	static float RingAlpha( float distance, float u, float v )
	{
		var ringOffset = ( distance - 0.85f ) / 0.035f;
		var ring = Mathf.Exp( -ringOffset * ringOffset );
		var haze = 0.06f * Mathf.SmoothStep( 0.0f, 0.85f, distance ) * ( ( distance < 0.85f ) ? 1.0f : 0.0f );

		return ( ring + haze ) * Mathf.Clamp01( ( 1.0f - distance ) * 8.0f );
	}

	// the debris texture: a strip of greys, so that each face of a shard, which takes its whole colour from one place in the strip, is shaded differently
	static Texture2D MakeShadeTexture()
	{
		var texture = new Texture2D( c_shadeCount, 1, TextureFormat.RGBA32, false );
		texture.name = "Explosion Debris";
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Point;
		texture.hideFlags = HideFlags.DontUnloadUnusedAsset;

		var pixels = new Color32[ c_shadeCount ];

		for ( int i = 0; i < c_shadeCount; i++ )
		{
			var shade = (byte) Mathf.RoundToInt( Mathf.Lerp( 0.35f, 1.0f, i / (float) ( c_shadeCount - 1 ) ) * 255.0f );

			pixels[ i ] = new Color32( shade, shade, shade, 255 );
		}

		texture.SetPixels32( pixels );
		texture.Apply( false, true );

		return texture;
	}

	// the laser beam texture: across the beam (v), a bright narrow core inside a soft glow; along it (u), the same everywhere
	static Texture2D MakeBeamTexture()
	{
		var texture = new Texture2D( 4, 64, TextureFormat.RGBA32, true );
		texture.name = "Laser Beam";
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Bilinear;
		texture.hideFlags = HideFlags.DontUnloadUnusedAsset;

		var pixels = new Color32[ 4 * 64 ];

		for ( int y = 0; y < 64; y++ )
		{
			// how far from the middle of the beam, 0 to 1 at its edge
			var distance = Mathf.Abs( ( ( y + 0.5f ) / 64.0f ) * 2.0f - 1.0f );

			var core = Mathf.Exp( -( distance / 0.12f ) * ( distance / 0.12f ) );
			var glow = Mathf.Exp( -( distance / 0.45f ) * ( distance / 0.45f ) );
			var alpha = Mathf.Clamp01( core * 0.85f + glow * 0.4f ) * Mathf.Clamp01( ( 1.0f - distance ) * 6.0f );

			for ( int x = 0; x < 4; x++ )
			{
				pixels[ y * 4 + x ] = new Color32( 255, 255, 255, (byte) Mathf.RoundToInt( alpha * 255.0f ) );
			}
		}

		texture.SetPixels32( pixels );
		texture.Apply( true, true );

		return texture;
	}

	// how many shades the debris texture has
	const int c_shadeCount = 8;

	// a shard of hull: an uneven tetrahedron
	static Mesh MakeShardMesh()
	{
		var a = new Vector3( 0.0f, 0.6f, 0.0f );
		var b = new Vector3( -0.5f, -0.3f, 0.35f );
		var c = new Vector3( 0.55f, -0.25f, 0.3f );
		var d = new Vector3( 0.05f, -0.2f, -0.6f );

		return MakeFacetedMesh( "Explosion Shard", new Vector3[] { a, b, c, a, c, d, a, d, b, b, d, c }, new int[] { 7, 4, 2, 0 } );
	}

	// a sliver of plating: a thin triangular slab
	static Mesh MakeSliverMesh()
	{
		var top0 = new Vector3( 0.0f, 0.12f, 0.7f );
		var top1 = new Vector3( -0.45f, 0.12f, -0.4f );
		var top2 = new Vector3( 0.5f, 0.12f, -0.3f );
		var bottom0 = new Vector3( 0.0f, -0.12f, 0.7f );
		var bottom1 = new Vector3( -0.45f, -0.12f, -0.4f );
		var bottom2 = new Vector3( 0.5f, -0.12f, -0.3f );

		var corners = new Vector3[]
		{
			// the two flat faces
			top0, top2, top1,
			bottom0, bottom1, bottom2,

			// the three edges, two triangles each
			top0, bottom0, bottom2, top0, bottom2, top2,
			top2, bottom2, bottom1, top2, bottom1, top1,
			top1, bottom1, bottom0, top1, bottom0, top0
		};

		return MakeFacetedMesh( "Explosion Sliver", corners, new int[] { 7, 1, 5, 5, 3, 3, 6, 6 } );
	}

	// make a mesh whose every triangle has corners of its own, all at the same place in the shade texture, so each face is flat and has its own shade
	static Mesh MakeFacetedMesh( string name, Vector3[] corners, int[] shadeOfTriangle )
	{
		var triangleCount = corners.Length / 3;

		var uvs = new Vector2[ corners.Length ];
		var triangles = new int[ corners.Length ];

		for ( int i = 0; i < corners.Length; i++ )
		{
			// guard against a shade list shorter than the triangle list
			var triangle = i / 3;
			var shade = ( triangle < shadeOfTriangle.Length ) ? shadeOfTriangle[ triangle ] : 0;

			// the middle of the shade's pixel
			uvs[ i ] = new Vector2( ( Mathf.Clamp( shade, 0, c_shadeCount - 1 ) + 0.5f ) / c_shadeCount, 0.5f );
			triangles[ i ] = i;
		}

		var mesh = new Mesh();
		mesh.name = name;
		mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
		mesh.vertices = corners;
		mesh.uv = uvs;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();

		Debug.Assert( triangleCount == shadeOfTriangle.Length, "EffectMaterials: " + name + " has " + triangleCount + " triangles and " + shadeOfTriangle.Length + " shades" );

		return mesh;
	}

	// fractal value noise from 0 to 1, from a hash of the lattice points (not from Random: the effects are made while the Spaceflight scene starts, and the planets need the game's random numbers to stay where they are)
	static float FractalNoise( float x, float y, int seed )
	{
		var total = 0.0f;
		var amplitude = 0.5f;
		var amplitudeSum = 0.0f;

		for ( int octave = 0; octave < 4; octave++ )
		{
			total += ValueNoise( x, y, seed + octave * 101 ) * amplitude;
			amplitudeSum += amplitude;

			x *= 2.0f;
			y *= 2.0f;
			amplitude *= 0.5f;
		}

		return total / amplitudeSum;
	}

	// value noise from 0 to 1: the hashes of the four lattice points around the position, blended smoothly
	static float ValueNoise( float x, float y, int seed )
	{
		var x0 = Mathf.FloorToInt( x );
		var y0 = Mathf.FloorToInt( y );

		var fx = x - x0;
		var fy = y - y0;

		// smoothstep, so the lattice does not show
		fx = fx * fx * ( 3.0f - 2.0f * fx );
		fy = fy * fy * ( 3.0f - 2.0f * fy );

		var bottom = Mathf.Lerp( Hash( x0, y0, seed ), Hash( x0 + 1, y0, seed ), fx );
		var top = Mathf.Lerp( Hash( x0, y0 + 1, seed ), Hash( x0 + 1, y0 + 1, seed ), fx );

		return Mathf.Lerp( bottom, top, fy );
	}

	// a number from 0 to 1 that depends only on the lattice point and the seed
	static float Hash( int x, int y, int seed )
	{
		unchecked
		{
			var h = (uint) ( x * 374761393 + y * 668265263 + seed * 2147483647 );

			h = ( h ^ ( h >> 13 ) ) * 1274126177u;
			h ^= h >> 16;

			return ( h & 0xFFFFFF ) / (float) 0x1000000;
		}
	}

	/// <summary>
	/// Make a particle system under the parent that bursts once when it plays.
	/// </summary>
	public static ParticleSystem CreateParticleSystem( Transform parent, string name, Material material, int sortingOrder, int maxParticles )
	{
		var particleObject = new GameObject( name );
		particleObject.transform.SetParent( parent );
		particleObject.transform.localPosition = Vector3.zero;

		var particleSystem = particleObject.AddComponent<ParticleSystem>();

		// a new particle system is playing, and its duration can only be set while it is stopped
		particleSystem.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		var main = particleSystem.main;
		main.duration = 2.0f;
		main.loop = false;
		main.playOnAwake = false;
		main.maxParticles = maxParticles;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		// follow the scale of the effect (a missile hit is half the size of a ship blowing up)
		main.scalingMode = ParticleSystemScalingMode.Hierarchy;

		// every particle comes from the burst
		var emission = particleSystem.emission;
		emission.rateOverTime = 0;

		var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
		renderer.sharedMaterial = material;
		renderer.sortingOrder = sortingOrder;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;

		return particleSystem;
	}

	/// <summary>
	/// A gradient from a start colour through a middle colour to an end colour, with the alpha going from start through middle to end.
	/// </summary>
	public static Gradient MakeGradient( Color startColor, Color middleColor, Color endColor, float startAlpha, float middleAlpha, float endAlpha )
	{
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( startColor, 0.0f ), new GradientColorKey( middleColor, 0.3f ), new GradientColorKey( endColor, 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( startAlpha, 0.0f ), new GradientAlphaKey( middleAlpha, 0.6f ), new GradientAlphaKey( endAlpha, 1.0f ) }
		);
		return gradient;
	}
}
