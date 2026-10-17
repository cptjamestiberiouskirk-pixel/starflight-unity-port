using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// grass and pebbles on the ground about the terrain vehicle (since 2026-10-17, ROADMAP D7): small meshes drawn with GPU instancing out to a radius that the ground
// detail sets (none at Low), in the colour of the ground they stand on (the shader samples the ground's own albedo map, "Starflight/Ground Clutter"). Grass grows
// in patches, and only on a planet with life; pebbles lie everywhere on solid ground, more on a planet rich in minerals; nothing in water or lava. Each 8 unit
// cell of the ground is filled from an integer hash of the cell and the planet, so the same blades and pebbles are in the same places every time, and no
// random numbers are drawn (everything else on a planet is placed from them). They are only drawn: nothing collides with them
public class GroundClutter : MonoBehaviour
{
	// the size of a cell of the ground, and how many grass tufts and pebbles a cell can hold at most
	public const float c_cellSize = 8.0f;
	const int c_mostGrass = 14;
	const int c_mostPebbles = 10;

	// how far from the vehicle they are drawn at each level (none at Low), and where they start to shrink away toward that edge
	static readonly float[] c_radius = { 0.0f, 70.0f, 110.0f };
	const float c_fadeStart = 0.8f;

	// how big a grass tuft and a pebble are drawn (world units), and the scale of the grass's patches
	const float c_grassHeight = 1.6f;
	const float c_pebbleSize = 0.55f;
	const float c_patchScale = 1.0f / 36.0f;

	// a draw call's most instances
	const int c_batchSize = 1023;

	// the planet the clutter is for
	PlanetGenerator m_planetGenerator;
	float m_elevationScale;
	int m_planetId;
	float m_grassDensity;
	float m_pebbleDensity;
	Material m_groundMaterial;

	// the cells filled so far (dropped once they are well out of reach)
	readonly Dictionary<Vector2Int, Matrix4x4[]> m_grassCells = new Dictionary<Vector2Int, Matrix4x4[]>();
	readonly Dictionary<Vector2Int, Matrix4x4[]> m_pebbleCells = new Dictionary<Vector2Int, Matrix4x4[]>();
	readonly List<Vector2Int> m_dropped = new List<Vector2Int>();

	// what is drawn this frame
	readonly List<Matrix4x4> m_grassDrawn = new List<Matrix4x4>( 4096 );
	readonly List<Matrix4x4> m_pebblesDrawn = new List<Matrix4x4>( 4096 );

	// the meshes and materials (made the first time they are needed)
	static Mesh s_grassMesh;
	static Mesh s_pebbleMesh;
	Material m_grassMaterial;
	Material m_pebbleMaterial;

	// sets the clutter up for a planet (Disembarked, when the terrain grid is set up for it)
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, GD_Planet planet, Material groundMaterial )
	{
		m_planetGenerator = planetGenerator;
		m_elevationScale = elevationScale;
		m_planetId = planet.m_id;
		m_groundMaterial = groundMaterial;

		// grass only on a planet with life, thicker the more life there is; pebbles on every planet, more the richer in minerals it is
		m_grassDensity = ( planet.m_bioDensity > 0 ) ? Mathf.Clamp( 0.35f + planet.m_bioDensity / 40.0f, 0.35f, 1.0f ) : 0.0f;
		m_pebbleDensity = Mathf.Clamp( 0.3f + planet.m_mineralDensity / 100.0f, 0.3f, 1.0f );

		m_grassCells.Clear();
		m_pebbleCells.Clear();
	}

	// how far from the vehicle they are drawn at the level in use
	public static float GetRadius()
	{
		return c_radius[ (int) GroundDetail.GetLevel() ];
	}

	// how many grass tufts and pebbles were drawn in the last frame (for the probe)
	public int GetGrassDrawn()
	{
		return m_grassDrawn.Count;
	}

	public int GetPebblesDrawn()
	{
		return m_pebblesDrawn.Count;
	}

	// the grass tufts and pebbles of one cell of the ground (filled the first time it is asked for): their places, turns and sizes
	public void GetCell( Vector2Int cell, out Matrix4x4[] grass, out Matrix4x4[] pebbles )
	{
		if ( !m_grassCells.TryGetValue( cell, out grass ) )
		{
			FillCell( cell );

			grass = m_grassCells[ cell ];
		}

		pebbles = m_pebbleCells[ cell ];
	}

	// (queued in Update, so that every camera that renders this frame draws them, a picture taken after the updates too)
	void Update()
	{
		m_grassDrawn.Clear();
		m_pebblesDrawn.Clear();

		var radius = GetRadius();

		if ( ( radius <= 0.0f ) || ( m_planetGenerator == null ) || ( SpaceflightController.m_instance == null ) )
		{
			return;
		}

		MakeMeshesAndMaterials();

		if ( m_grassMaterial == null )
		{
			return;
		}

		var vehicle = SpaceflightController.m_instance.m_terrainVehicle.transform.position;
		var cellRange = Mathf.CeilToInt( radius / c_cellSize );
		var centerCell = new Vector2Int( Mathf.FloorToInt( vehicle.x / c_cellSize ), Mathf.FloorToInt( vehicle.z / c_cellSize ) );
		var fadeFrom = radius * c_fadeStart;

		for ( var z = -cellRange; z <= cellRange; z++ )
		{
			for ( var x = -cellRange; x <= cellRange; x++ )
			{
				var cell = new Vector2Int( centerCell.x + x, centerCell.y + z );

				GetCell( cell, out var grass, out var pebbles );

				Gather( grass, m_grassDrawn, vehicle, radius, fadeFrom );
				Gather( pebbles, m_pebblesDrawn, vehicle, radius, fadeFrom );
			}
		}

		// drop the cells that are well out of reach
		m_dropped.Clear();

		foreach ( var cell in m_grassCells.Keys )
		{
			if ( ( Mathf.Abs( cell.x - centerCell.x ) > cellRange * 2 ) || ( Mathf.Abs( cell.y - centerCell.y ) > cellRange * 2 ) )
			{
				m_dropped.Add( cell );
			}
		}

		foreach ( var cell in m_dropped )
		{
			m_grassCells.Remove( cell );
			m_pebbleCells.Remove( cell );
		}

		// in the colour of the ground, in the look the player has picked
		if ( m_groundMaterial != null )
		{
			TakeTheGroundsColour( m_grassMaterial );
			TakeTheGroundsColour( m_pebbleMaterial );
		}

		var bounds = new Bounds( vehicle, new Vector3( radius * 2.0f, 1024.0f, radius * 2.0f ) );

		Draw( s_grassMesh, m_grassMaterial, m_grassDrawn, bounds, ShadowCastingMode.Off );
		Draw( s_pebbleMesh, m_pebbleMaterial, m_pebblesDrawn, bounds, ( GroundDetail.GetLevel() == GroundDetail.Level.High ) ? ShadowCastingMode.On : ShadowCastingMode.Off );
	}

	// the materials are this clutter's own (the meshes are shared by the whole session); the scene is unloaded on every visit to the Starport
	void OnDestroy()
	{
		if ( m_grassMaterial != null )
		{
			Destroy( m_grassMaterial );
		}

		if ( m_pebbleMaterial != null )
		{
			Destroy( m_pebbleMaterial );
		}
	}

	// gives a clutter material the ground's albedo map and the way from the world to it (the terrain grid's: TRANSFORM_TEX of the world's XZ by the elevation
	// map's scale and offset, then by the albedo map's)
	void TakeTheGroundsColour( Material material )
	{
		var elevationScale = m_groundMaterial.GetTextureScale( "SF_ElevationMap" );
		var elevationOffset = m_groundMaterial.GetTextureOffset( "SF_ElevationMap" );

		material.SetTexture( "_GroundMap", m_groundMaterial.GetTexture( "_MainTex" ) );
		material.SetTextureScale( "_GroundMap", m_groundMaterial.GetTextureScale( "_MainTex" ) );
		material.SetTextureOffset( "_GroundMap", m_groundMaterial.GetTextureOffset( "_MainTex" ) );
		material.SetVector( "_GroundMapTransform", new Vector4( elevationScale.x, elevationScale.y, elevationOffset.x, elevationOffset.y ) );
	}

	// the instances of a cell within the radius, shrunk toward its edge
	static void Gather( Matrix4x4[] instances, List<Matrix4x4> drawn, Vector3 vehicle, float radius, float fadeFrom )
	{
		for ( var i = 0; i < instances.Length; i++ )
		{
			var position = instances[ i ].GetColumn( 3 );
			var dx = position.x - vehicle.x;
			var dz = position.z - vehicle.z;
			var distance = Mathf.Sqrt( dx * dx + dz * dz );

			if ( distance >= radius )
			{
				continue;
			}

			if ( distance <= fadeFrom )
			{
				drawn.Add( instances[ i ] );
			}
			else
			{
				var shrink = 1.0f - ( distance - fadeFrom ) / ( radius - fadeFrom );

				drawn.Add( instances[ i ] * Matrix4x4.Scale( new Vector3( shrink, shrink, shrink ) ) );
			}
		}
	}

	// draws the instances in batches
	void Draw( Mesh mesh, Material material, List<Matrix4x4> instances, Bounds bounds, ShadowCastingMode shadows )
	{
		if ( instances.Count == 0 )
		{
			return;
		}

		var renderParams = new RenderParams( material )
		{
			worldBounds = bounds,
			shadowCastingMode = shadows,
			receiveShadows = true,
			layer = gameObject.layer
		};

		for ( var start = 0; start < instances.Count; start += c_batchSize )
		{
			Graphics.RenderMeshInstanced( renderParams, mesh, 0, instances, Mathf.Min( c_batchSize, instances.Count - start ), start );
		}
	}

	// fills a cell from the hash of the cell and the planet: grass where the patches are, if the planet has life, and pebbles, both on solid ground only
	void FillCell( Vector2Int cell )
	{
		var grass = new List<Matrix4x4>();
		var pebbles = new List<Matrix4x4>();

		var patch = PatchStrength( ( cell.x + 0.5f ) * c_cellSize, ( cell.y + 0.5f ) * c_cellSize );
		var grassCount = Mathf.RoundToInt( c_mostGrass * m_grassDensity * patch );
		var pebbleCount = Mathf.RoundToInt( c_mostPebbles * m_pebbleDensity );

		for ( var i = 0; i < grassCount + pebbleCount; i++ )
		{
			var isGrass = i < grassCount;
			var x = ( cell.x + Hash01( cell, i, 1 ) ) * c_cellSize;
			var z = ( cell.y + Hash01( cell, i, 2 ) ) * c_cellSize;

			if ( !GroundHeightAt( x, z, out var y ) )
			{
				continue;
			}

			var turn = Quaternion.Euler( 0.0f, Hash01( cell, i, 3 ) * 360.0f, 0.0f );
			var size = isGrass ? c_grassHeight * ( 0.6f + 0.8f * Hash01( cell, i, 4 ) ) : c_pebbleSize * ( 0.45f + 1.1f * Hash01( cell, i, 4 ) );
			var matrix = Matrix4x4.TRS( new Vector3( x, y, z ), turn, new Vector3( size, size, size ) );

			if ( isGrass )
			{
				grass.Add( matrix );
			}
			else
			{
				pebbles.Add( matrix );
			}
		}

		m_grassCells[ cell ] = grass.ToArray();
		m_pebbleCells[ cell ] = pebbles.ToArray();
	}

	// the height of the ground here as the terrain vehicle stands on it (false in water or lava)
	bool GroundHeightAt( float x, float z, out float height )
	{
		Tools.WorldToMapCoordinates( new Vector3( x, 0.0f, z ), out var mapX, out var mapY, m_planetGenerator.m_textureMapWidth, m_planetGenerator.m_textureMapHeight );

		var elevation = m_planetGenerator.GetBicubicSmoothedElevation( mapX, mapY );

		height = elevation * m_elevationScale;

		return elevation > m_planetGenerator.m_waterElevation;
	}

	// how much of a patch of grass a place is in: slow value noise from the hash, nothing below a threshold
	float PatchStrength( float x, float z )
	{
		var px = x * c_patchScale;
		var pz = z * c_patchScale;
		var x0 = Mathf.FloorToInt( px );
		var z0 = Mathf.FloorToInt( pz );
		var tx = Mathf.SmoothStep( 0.0f, 1.0f, px - x0 );
		var tz = Mathf.SmoothStep( 0.0f, 1.0f, pz - z0 );

		var a = Hash01( new Vector2Int( x0, z0 ), 0, 7 );
		var b = Hash01( new Vector2Int( x0 + 1, z0 ), 0, 7 );
		var c = Hash01( new Vector2Int( x0, z0 + 1 ), 0, 7 );
		var d = Hash01( new Vector2Int( x0 + 1, z0 + 1 ), 0, 7 );

		var value = Mathf.Lerp( Mathf.Lerp( a, b, tx ), Mathf.Lerp( c, d, tx ), tz );

		return Mathf.Clamp01( ( value - 0.35f ) / 0.4f );
	}

	// a number from 0 to 1 for a cell, an index and a salt, and the planet (an integer hash: no random numbers)
	float Hash01( Vector2Int cell, int index, int salt )
	{
		unchecked
		{
			var h = (uint) ( cell.x * 73856093 ^ cell.y * 19349663 ^ index * 83492791 ^ salt * 2654435761u ^ m_planetId * 40503 );

			h = ( h ^ ( h >> 16 ) ) * 2246822519u;
			h = ( h ^ ( h >> 13 ) ) * 3266489917u;
			h ^= h >> 16;

			return ( h & 0xFFFFFF ) / (float) 0x1000000;
		}
	}

	// the meshes of a grass tuft and a pebble, and their materials
	void MakeMeshesAndMaterials()
	{
		if ( s_grassMesh == null )
		{
			s_grassMesh = MakeGrassTuft();
		}

		if ( s_pebbleMesh == null )
		{
			s_pebbleMesh = MakePebble();
		}

		if ( m_grassMaterial == null )
		{
			var shader = Resources.Load<Shader>( "Shaders/Ground Clutter" );

			// nothing to draw them with (Update draws nothing while the materials are missing)
			if ( shader == null )
			{
				return;
			}

			m_grassMaterial = new Material( shader ) { name = "Ground Clutter Grass", enableInstancing = true, hideFlags = HideFlags.DontSave };
			m_grassMaterial.SetColor( "_Tint", new Color( 0.95f, 1.15f, 0.8f, 1.0f ) );
			m_grassMaterial.SetFloat( "_Variation", 0.25f );
			m_grassMaterial.SetFloat( "_Sway", 0.12f );

			m_pebbleMaterial = new Material( shader ) { name = "Ground Clutter Pebbles", enableInstancing = true, hideFlags = HideFlags.DontSave };
			m_pebbleMaterial.SetColor( "_Tint", new Color( 0.62f, 0.6f, 0.58f, 1.0f ) );
			m_pebbleMaterial.SetFloat( "_Variation", 0.5f );
			m_pebbleMaterial.SetFloat( "_Desaturation", 0.45f );
			m_pebbleMaterial.SetFloat( "_Sway", 0.0f );
		}
	}

	// a tuft of seven blades, each a thin triangle leaning out from the middle, darker at the root; the vertex colour's alpha is how far up the blade it is
	static Mesh MakeGrassTuft()
	{
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var colours = new List<Color>();
		var triangles = new List<int>();

		for ( var b = 0; b < 7; b++ )
		{
			var angle = b * Mathf.PI * 2.0f / 7.0f + 0.3f * b;
			var outward = new Vector3( Mathf.Cos( angle ), 0.0f, Mathf.Sin( angle ) );
			var side = new Vector3( -outward.z, 0.0f, outward.x );
			var root = outward * ( 0.08f + 0.04f * ( b % 3 ) );
			var height = 0.7f + 0.3f * ( ( b * 5 ) % 7 ) / 6.0f;
			var tip = root + outward * 0.35f * height + Vector3.up * height;
			var normal = Vector3.Cross( side, tip - root ).normalized;

			var start = vertices.Count;

			vertices.Add( root - side * 0.06f );
			vertices.Add( root + side * 0.06f );
			vertices.Add( tip );

			for ( var k = 0; k < 3; k++ )
			{
				normals.Add( normal );
			}

			colours.Add( new Color( 0.55f, 0.55f, 0.55f, 0.0f ) );
			colours.Add( new Color( 0.55f, 0.55f, 0.55f, 0.0f ) );
			colours.Add( new Color( 1.1f, 1.1f, 1.1f, 1.0f ) );

			triangles.Add( start );
			triangles.Add( start + 1 );
			triangles.Add( start + 2 );
		}

		var mesh = new Mesh { name = "Grass Tuft", hideFlags = HideFlags.DontSave };

		mesh.SetVertices( vertices );
		mesh.SetNormals( normals );
		mesh.SetColors( colours );
		mesh.SetTriangles( triangles, 0 );
		mesh.RecalculateBounds();

		return mesh;
	}

	// a pebble: an icosahedron squashed flat, with a face's normal on each face
	static Mesh MakePebble()
	{
		var t = ( 1.0f + Mathf.Sqrt( 5.0f ) ) * 0.5f;
		var corners = new[]
		{
			new Vector3( -1, t, 0 ), new Vector3( 1, t, 0 ), new Vector3( -1, -t, 0 ), new Vector3( 1, -t, 0 ),
			new Vector3( 0, -1, t ), new Vector3( 0, 1, t ), new Vector3( 0, -1, -t ), new Vector3( 0, 1, -t ),
			new Vector3( t, 0, -1 ), new Vector3( t, 0, 1 ), new Vector3( -t, 0, -1 ), new Vector3( -t, 0, 1 )
		};
		var faces = new[]
		{
			0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
			3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
		};

		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var colours = new List<Color>();
		var triangles = new List<int>();

		for ( var f = 0; f < faces.Length; f += 3 )
		{
			var a = Squash( corners[ faces[ f ] ] );
			var b = Squash( corners[ faces[ f + 1 ] ] );
			var c = Squash( corners[ faces[ f + 2 ] ] );
			var normal = Vector3.Cross( b - a, c - a ).normalized;
			var start = vertices.Count;

			vertices.Add( a );
			vertices.Add( b );
			vertices.Add( c );

			for ( var k = 0; k < 3; k++ )
			{
				normals.Add( normal );
				colours.Add( new Color( 1.0f, 1.0f, 1.0f, 0.0f ) );
			}

			triangles.Add( start );
			triangles.Add( start + 1 );
			triangles.Add( start + 2 );
		}

		var mesh = new Mesh { name = "Pebble", hideFlags = HideFlags.DontSave };

		mesh.SetVertices( vertices );
		mesh.SetNormals( normals );
		mesh.SetColors( colours );
		mesh.SetTriangles( triangles, 0 );
		mesh.RecalculateBounds();

		return mesh;
	}

	// an icosahedron's corner, to a pebble's: half as tall as it is wide, its bottom sunk a little into the ground
	static Vector3 Squash( Vector3 corner )
	{
		var unit = corner.normalized;

		return new Vector3( unit.x, unit.y * 0.5f + 0.15f, unit.z ) * 0.5f;
	}
}
