using System.Collections.Generic;
using UnityEngine;

// the random ruins of a planet as the shipped game places them (SEED-OV POPULA, WF492): the planet's surface of 2304 by 960 units (SITE-OV keeps the landing
// site within those) is cut into regions of 18 by 30 (MOVE-OV WED42), and each region, seeded from its place (WED34 !XYSEED), may hold an ancient ruin and a
// recent ruin by two bytes of the planet's type (the PLANET file's bytes 0x0a and 0x0b, WEF65 and WEF6D; "recent" after the game data's planet types named
// "Ancient/Recent Ruins", which Look calls the Old Empire's, after the 1984 notes) - the generic planet types have none, the planets of ruins have many; the
// shipped game seeds a region when the terrain vehicle first comes into it, the port works every region out the same way at any time
public static class RandomRuinRegions
{
	// the regions across the map (2304 / 18) and from pole to pole (960 / 30)
	public const int c_columns = 128;
	public const int c_rows = 32;

	// the original's units of a region and of the surface
	public const int c_regionWidth = 18;
	public const int c_regionHeight = 30;
	public const int c_surfaceWidth = 2304;
	public const int c_surfaceHeight = 960;

	// the chance bytes of each planet type (GD_Planet.m_planetTypeId is the original's planet species, the PLANET file's record): ancient ruins (0x0a, WEF65,
	// the ruin species 2 with an Endurium deposit beside it) and recent ruins (0x0b, WEF6D, the ruin species 1) - types 1 to 17 are the generic kinds and have
	// neither
	static readonly byte[] c_ancientChance =
	{
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 60, 100, 150, 175, 0, 120, 200, 51, 250, 128, 150, 80, 30, 50, 70, 100, 75, 0, 120, 200, 150, 150, 80, 175, 150, 255, 50, 150, 75, 100, 130, 150, 255,
		200, 200, 150, 180, 0, 255, 0, 200, 50
	};

	static readonly byte[] c_recentChance =
	{
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		255, 128, 51, 89, 153, 0, 179, 51, 153, 128, 153, 77, 179, 102, 153, 38, 0, 128, 0, 32, 0, 40, 60, 35, 45, 0, 0, 0, 0, 0, 175, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 255, 0, 0
	};

	// the curios a ruin can hold, the original's artifact species 16 to 50 in its order (the ARTIFACT file), by the port's names (its artifact list is in
	// alphabetical order) - 47 to 50 are the rare ones (SEED-OV WF245)
	static readonly string[] c_curioNames =
	{
		"Amazing Artifact", "Armalcolite Relic", "Bladed Toy", "Blue Bauble", "Buttoned Box", "Complex Machine", "Cute Doodad", "Frightening Apparatus",
		"Glowing Disk", "Golden Globe", "Hot P.Y.T.", "Humming Gizzy", "Metal Ball", "Mobius Device", "Nice Thing", "Octagonal Lens", "Oval Object",
		"Plastic Thing-a-ma-jig", "Pretty Picture", "Pyramid Device", "Quivering Lump", "Rubber Widget", "Silver Gadget", "Small Obelisk", "Spiral Tube",
		"Strange Cloth", "Surprising Utensil", "Throbbing Mass", "Ticking Sphere", "Translucent Cube", "Wee Green Blobbie", "Adrynna's Gold", "Pink Tube-a-tron",
		"A Mulligan", "Red Herring"
	};

	const int c_firstCurioSpecies = 16;

	// the items of a ruin, by their place in its key: the curio and the Endurium
	public const int c_curioItem = 0;
	public const int c_enduriumItem = 1;

	// a ruin of a region
	public class RegionRuin
	{
		public int m_region;
		public bool m_isAncient;
		public float m_mapX;
		public float m_mapY;
		public int m_templateIndex;
		public int m_rotation;
		public List<TerrainRuin.RandomItem> m_items = new List<TerrainRuin.RandomItem>();
	}

	// the chance bytes of a planet (0 when it has none)
	static int GetChance( byte[] chances, GD_Planet planet )
	{
		return ( ( planet != null ) && ( planet.m_planetTypeId >= 0 ) && ( planet.m_planetTypeId < chances.Length ) ) ? chances[ planet.m_planetTypeId ] : 0;
	}

	// true if this planet has random ruins at all
	public static bool HasRuins( GD_Planet planet )
	{
		return ( GetChance( c_ancientChance, planet ) > 0 ) || ( GetChance( c_recentChance, planet ) > 0 );
	}

	// the region of this place on the planet's map (column + row * c_columns), and back
	public static int GetRegion( float mapX, float mapY, PlanetGenerator planetGenerator )
	{
		ToSurface( mapX, mapY, planetGenerator, out var x, out var y );

		var column = ( ( Mathf.FloorToInt( x ) / c_regionWidth ) % c_columns + c_columns ) % c_columns;
		var row = Mathf.Clamp( Mathf.FloorToInt( y ) / c_regionHeight, 0, c_rows - 1 );

		return column + row * c_columns;
	}

	// the original's surface units of a place on the port's map: the map goes round the planet once from left to right, and from pole to pole over its middle
	// three quarters (as Tools.LatLongToWorldCoordinates maps latitude 90 to a quarter of the way from the middle)
	public static void ToSurface( float mapX, float mapY, PlanetGenerator planetGenerator, out float x, out float y )
	{
		x = mapX * c_surfaceWidth / planetGenerator.m_textureMapWidth;
		y = ( mapY - planetGenerator.m_textureMapHeight * 0.125f ) * c_surfaceHeight / ( planetGenerator.m_textureMapHeight * 0.75f );
	}

	public static void ToMap( float x, float y, PlanetGenerator planetGenerator, out float mapX, out float mapY )
	{
		mapX = x * planetGenerator.m_textureMapWidth / c_surfaceWidth;
		mapY = y * ( planetGenerator.m_textureMapHeight * 0.75f ) / c_surfaceHeight + planetGenerator.m_textureMapHeight * 0.125f;
	}

	// the shipped game's chance of a ruin in a region (SEED-OV WF468): none for a byte of 0, else one in (320 - byte) / 64, so 193 to 255 is always
	static bool RollRuin( System.Random random, int chance )
	{
		if ( chance <= 0 )
		{
			return false;
		}

		var outOf = Mathf.Max( 1, ( 320 - chance ) / 64 );

		return random.Next( 0, outOf ) == 0;
	}

	/// <summary>
	/// The ruins of one region of a planet (none, one or two), worked out with a generator of their own seeded from the planet and the region, so the same every
	/// time and without the game's random numbers. templateCount only picks a model.
	/// </summary>
	public static List<RegionRuin> ForRegion( PlanetGenerator planetGenerator, float elevationScale, GD_Planet planet, int region, int templateCount )
	{
		var ruins = new List<RegionRuin>();

		if ( ( planetGenerator == null ) || ( planet == null ) || !HasRuins( planet ) || ( region < 0 ) || ( region >= c_columns * c_rows ) )
		{
			return ruins;
		}

		var column = region % c_columns;
		var row = region / c_columns;
		var random = new System.Random( unchecked( planet.m_id * 1000003 + row * 7919 + column * 104729 + 31 ) );

		// the ancient ruin first, then the recent one (WF492)
		for ( var kind = 0; kind < 2; kind++ )
		{
			var isAncient = kind == 0;

			if ( !RollRuin( random, GetChance( isAncient ? c_ancientChance : c_recentChance, planet ) ) )
			{
				continue;
			}

			// a place in the region (WF29F: x from its left edge and up to 18 on, y up to 30 on) - on land (WED79: above the contour of the sea), and not on a
			// slope steeper than 45 degrees (the port's: a ruin model stands on level ground)
			var x = column * c_regionWidth + random.Next( 0, c_regionWidth + 1 );
			var y = row * c_regionHeight + random.Next( 0, c_regionHeight + 1 );

			ToMap( x + 0.5f, Mathf.Min( y + 0.5f, c_surfaceHeight - 0.5f ), planetGenerator, out var mapX, out var mapY );

			var templateIndex = random.Next( 0, Mathf.Max( 1, templateCount ) );
			var rotation = random.Next( 0, 360 );
			var curioRoll = random.Next( 1, 100 );
			var species = random.Next( c_firstCurioSpecies, 48 );
			var rareSpecies = random.Next( 47, 51 );
			var enduriumVolume = random.Next( 10, 100 );

			if ( planetGenerator.GetBilinearSmoothedElevation( mapX, mapY ) <= planetGenerator.m_waterElevation )
			{
				continue;
			}

			if ( planetGenerator.GetBilinearSmoothedNormal( mapX, mapY, elevationScale * 0.125f ).y < 0.707f )
			{
				continue;
			}

			var ruin = new RegionRuin { m_region = region, m_isAncient = isAncient, m_mapX = mapX, m_mapY = mapY, m_templateIndex = templateIndex, m_rotation = rotation };

			// three times in ten a curio (WF26B: a roll of 1 to 99 under 30), of species 16 to 47, and a 47 is one of the four rare ones (WF245)
			if ( curioRoll < 30 )
			{
				var artifactId = FindCurio( ( species > 46 ) ? rareSpecies : species );

				if ( artifactId >= 0 )
				{
					var gameData = DataController.m_instance.m_gameData;
					var value = gameData.m_artifactList[ artifactId ].m_actualValue;

					// the crew's estimate, off by up to half of the value (the port's, as before: Cargo shows it before the artifact is taken)
					var error = ( (float) random.NextDouble() + (float) random.NextDouble() - 1.0f ) * 0.5f;
					var estimate = Mathf.Max( 100, Mathf.RoundToInt( value * ( 1.0f + error ) / 100.0f ) * 100 );

					ruin.m_items.Add( new TerrainRuin.RandomItem { m_key = TerrainRuin.GetRegionItemKey( planet.m_id, region, kind, c_curioItem ), m_artifactId = artifactId, m_estimatedValue = estimate } );
				}
			}

			// an ancient ruin has Endurium beside it, 1.0 to 9.9 cubic meters (WF19B always, as WEC36 is 1; WF163 element 6 with RRND 10 100 tenths)
			if ( isAncient )
			{
				ruin.m_items.Add( new TerrainRuin.RandomItem { m_key = TerrainRuin.GetRegionItemKey( planet.m_id, region, kind, c_enduriumItem ), m_enduriumVolume = enduriumVolume } );
			}

			ruins.Add( ruin );
		}

		return ruins;
	}

	// the port's artifact of an original curio species (-1 if it has none)
	static int FindCurio( int species )
	{
		var index = species - c_firstCurioSpecies;

		if ( ( index < 0 ) || ( index >= c_curioNames.Length ) )
		{
			return -1;
		}

		var gameData = DataController.m_instance.m_gameData;

		for ( var artifactId = 0; artifactId < gameData.m_artifactList.Length; artifactId++ )
		{
			if ( gameData.m_artifactList[ artifactId ].m_name == c_curioNames[ index ] )
			{
				return artifactId;
			}
		}

		return -1;
	}

	/// <summary>
	/// The ruins of every region of a planet (the Red Cylinder and the survey work them out from orbit): as many as the planet has, none on a generic planet.
	/// </summary>
	public static List<RegionRuin> ForPlanet( PlanetGenerator planetGenerator, float elevationScale, GD_Planet planet )
	{
		var ruins = new List<RegionRuin>();

		if ( !HasRuins( planet ) )
		{
			return ruins;
		}

		for ( var region = 0; region < c_columns * c_rows; region++ )
		{
			ruins.AddRange( ForRegion( planetGenerator, elevationScale, planet, region, 1 ) );
		}

		return ruins;
	}

	/// <summary>
	/// The regions of a planet that hold an ancient ruin (the landing map tints them with the Red Cylinder aboard: decided 2026-10-19, a box for each would
	/// cover a planet of ruins).
	/// </summary>
	public static List<int> GetAncientRegions( PlanetGenerator planetGenerator, float elevationScale, GD_Planet planet )
	{
		var regions = new List<int>();

		foreach ( var ruin in ForPlanet( planetGenerator, elevationScale, planet ) )
		{
			if ( ruin.m_isAncient && !regions.Contains( ruin.m_region ) )
			{
				regions.Add( ruin.m_region );
			}
		}

		return regions;
	}

	// the edges of a region on the landing map, in degrees: east-west from -180 across, north-south from -90 up (the region's 18 by 30 of the original's 2304 by
	// 960 units, the map going round once and from pole to pole)
	public static void GetRegionDegrees( int region, out float westEdge, out float eastEdge, out float southEdge, out float northEdge )
	{
		var column = region % c_columns;
		var row = region / c_columns;

		westEdge = column * 360.0f / c_columns - 180.0f;
		eastEdge = ( column + 1 ) * 360.0f / c_columns - 180.0f;
		southEdge = row * 180.0f / c_rows - 90.0f;
		northEdge = ( row + 1 ) * 180.0f / c_rows - 90.0f;
	}

	// the regions within this many regions of one (across the map going round, from pole to pole not), the region itself first
	public static List<int> GetRegionsAround( int region, int distance )
	{
		var regions = new List<int>();
		var column = region % c_columns;
		var row = region / c_columns;

		for ( var dy = -distance; dy <= distance; dy++ )
		{
			var r = row + dy;

			if ( ( r < 0 ) || ( r >= c_rows ) )
			{
				continue;
			}

			for ( var dx = -distance; dx <= distance; dx++ )
			{
				var c = ( ( column + dx ) % c_columns + c_columns ) % c_columns;

				regions.Add( c + r * c_columns );
			}
		}

		return regions;
	}
}
