
using System.Collections.Generic;
using UnityEngine;

// the science officer's survey of a planet from orbit, made by the analysis of a sensor reading of the planet: where its mineral deposits are, and the
// latitude bands its lifeforms live in. The deposits' places are worked out from the planet's own maps and seeds exactly as the surface places them
// (TerrainGridPopulator.ReplayPlaces) and the bands from the regions of its lifeforms (GD_Species.GetLife: the shipped game places a block's lifeforms only when
// the terrain vehicle comes into it, so their places are not known from orbit), then found as well as the officer's science skill and the sensors' health
// allow: a skilled officer with sound sensors finds most of the mineral fields and bands with life and puts the fields close to where they are, an unskilled
// one finds a few and puts them roughly (decided 2026-10-17, ROADMAP D6: the shipped game's sensors give only the densities; the 1984 notes have life readings
// as dots on the sensor picture, and minerals and ruins as marks on the map; the ancient ruins are left to the Red Cylinder, the device the original has for
// finding them from orbit; the bands with life, more strongly the more crowded, decided 2026-10-19)
public class PlanetSurvey
{
	public enum Kind
	{
		Minerals
	}

	// a latitude band with life that the survey found: its edges (north-south, in degrees) and how crowded its region is (0 to 256, the shipped game's crowding)
	public struct LifeBand
	{
		public float m_southEdge;
		public float m_northEdge;
		public int m_crowding;
	}

	// the degrees of latitude of one of the original's surface rows (960 from pole to pole)
	const float c_degreesPerRow = 180.0f / 960.0f;

	// a mark of the survey: what, where (east-west and north-south, in degrees, as the landing map's coordinates), how much (the deposits of a mineral field,
	// 1 otherwise) and the element most of a field's deposits are of, if the officer can tell (-1 if not, and for anything but minerals)
	public struct Mark
	{
		public Kind m_kind;
		public float m_eastWest;
		public float m_northSouth;
		public int m_size;
		public int m_elementId;
	}

	// a mineral field is a concentration of deposits: the deposits in one cell of a grid over the planet (32 by 12 cells: 11.25 degrees east-west by 15
	// north-south) when they hold at least half again the planet's average for a cell, the richest 15 at most (deposits are spread over the whole planet:
	// marking every cell that has some would mark nearly all of them)
	const int c_fieldColumns = 32;
	const int c_fieldRows = 12;
	const float c_fieldConcentration = 1.5f;
	const int c_mostFields = 15;

	// the skill at which an officer finds everything there is to find (the crew's skills go up to 250)
	const float c_fullSkill = 250.0f;

	// how many of the mineral fields and the bands with life the least skilled officer with sound sensors finds, and the most
	const float c_leastFound = 0.15f;
	const float c_lifeLeastFound = 0.2f;

	// how far off a mark can be, in degrees, for the least skilled officer and for the most
	const float c_roughestError = 6.0f;
	const float c_finestError = 0.3f;

	// the planet surveyed, the marks, and the counts the analysis reports
	public readonly int m_planetId;
	public readonly List<Mark> m_marks = new List<Mark>();
	public int m_mineralFieldsFound;
	public int m_mineralFields;
	public int m_speciesCount;

	// the bands with life found (a band a hemisphere, and one about the equator), and how many there are
	public readonly List<LifeBand> m_lifeBands = new List<LifeBand>();
	public int m_lifeBandCount;

	// the element most of the mineral fields found are of (-1 if none was told)
	public int m_mainElementId = -1;

	// a deposit where the surface places it
	struct Deposit
	{
		public float m_eastWest;
		public float m_northSouth;
		public int m_elementId;
		public int m_volume;
		public int m_depositIndex;
	}

	// the places of the last planet worked out (it takes a moment: every rock is placed again first, since the deposits must not land on them)
	static int s_placesPlanetId = -1;
	static List<Deposit> s_deposits;

	PlanetSurvey( int planetId )
	{
		m_planetId = planetId;
	}

	/// <summary>
	/// The survey a science officer of this skill makes of this planet, with sensors working this well (0 to 1). The terrain grid is the terrain vehicle's
	/// (its populators and its elevation scale are the ones that place the planet's objects). Returns null if the planet has no maps.
	/// </summary>
	public static PlanetSurvey Make( GD_Planet planet, PlanetGenerator planetGenerator, TerrainGrid terrainGrid, int scienceSkill, float sensorsFactor )
	{
		if ( ( planet == null ) || ( planetGenerator == null ) || !planetGenerator.m_mapsGenerated || ( planetGenerator.m_elevation == null ) || ( terrainGrid == null ) )
		{
			return null;
		}

		WorkOutPlaces( planet, planetGenerator, terrainGrid );

		var survey = new PlanetSurvey( planet.m_id );

		// how good the officer is with these sensors (0 to 1), and how far off the marks are
		var quality = Mathf.Clamp01( scienceSkill / c_fullSkill ) * Mathf.Clamp01( sensorsFactor );
		var error = Mathf.Lerp( c_roughestError, c_finestError, quality );

		// the same officer finds the same things every time (a better one more of them, and more exactly)
		var random = new System.Random( planet.m_id * 7919 + scienceSkill * 31 + Mathf.RoundToInt( sensorsFactor * 100.0f ) );

		survey.AddMineralFields( planet, quality, error, random );
		survey.AddLifeBands( planet, quality, random );

		return survey;
	}

	// works out where the surface places the planet's deposits (once a planet)
	static void WorkOutPlaces( GD_Planet planet, PlanetGenerator planetGenerator, TerrainGrid terrainGrid )
	{
		if ( ( s_placesPlanetId == planet.m_id ) && ( s_deposits != null ) )
		{
			return;
		}

		s_deposits = new List<Deposit>();
		s_placesPlanetId = planet.m_id;

		var elevationScale = terrainGrid.m_elevationScale;
		var spawnLists = TerrainGridPopulator.MakeSpawnLists();

		// the rocks first (the deposits are not placed on them), as TerrainGrid.SetElevationMap places them
		if ( terrainGrid.m_terrainRocks != null )
		{
			var rocks = terrainGrid.m_terrainRocks;
			var numRocks = ( planet.m_mineralDensity * rocks.m_maxNumRocks ) / 100;

			TerrainGridPopulator.ReplayPlaces( planetGenerator, spawnLists, elevationScale, ( rocks.m_rockTemplates != null ) ? rocks.m_rockTemplates.Length : 1, numRocks, planet.m_id + 1, true, 1.0f, 1.0f, null );
		}

		// then the deposits, each of the element TerrainElements gives it (TerrainElement.ChooseDepositElement)
		if ( terrainGrid.m_terrainElements != null )
		{
			var numElements = ( planet.m_mineralDensity * terrainGrid.m_terrainElements.m_maxNumElements ) / 100;
			var numElementKinds = DataController.m_instance.m_gameData.m_elementList.Length;
			var places = TerrainGridPopulator.ReplayPlaces( planetGenerator, spawnLists, elevationScale, numElementKinds, numElements, planet.m_id + 2, true, 1.0f, 1.0f, TerrainElement.DrawVolume, i => TerrainElement.ChooseDepositElement( planet, i ) );

			for ( var i = 0; i < places.Count; i++ )
			{
				Tools.WorldToLatLongCoordinates( places[ i ].m_position, out var eastWest, out var northSouth );

				s_deposits.Add( new Deposit { m_eastWest = eastWest, m_northSouth = northSouth, m_elementId = places[ i ].m_templateIndex, m_volume = places[ i ].m_volume, m_depositIndex = i } );
			}
		}
	}

	// the exact places of the planet's deposits (east-west, north-south and the element; for the probe, which compares them with the objects on the surface)
	public static List<Vector3> GetDepositPlaces( GD_Planet planet, PlanetGenerator planetGenerator, TerrainGrid terrainGrid )
	{
		var places = new List<Vector3>();

		if ( ( planet == null ) || ( planetGenerator == null ) || ( planetGenerator.m_elevation == null ) || ( terrainGrid == null ) )
		{
			return places;
		}

		WorkOutPlaces( planet, planetGenerator, terrainGrid );

		foreach ( var deposit in s_deposits )
		{
			places.Add( new Vector3( deposit.m_eastWest, deposit.m_northSouth, deposit.m_elementId ) );
		}

		return places;
	}

	// the mineral fields: the deposits still there gathered into the cells of a grid, the richest found first, as many as the officer finds
	void AddMineralFields( GD_Planet planet, float quality, float error, System.Random random )
	{
		var playerData = DataController.m_instance.m_playerData;
		var cellVolume = new int[ c_fieldRows, c_fieldColumns ];
		var cellCount = new int[ c_fieldRows, c_fieldColumns ];
		var cellEastWest = new float[ c_fieldRows, c_fieldColumns ];
		var cellNorthSouth = new float[ c_fieldRows, c_fieldColumns ];
		var cellElements = new Dictionary<int, int>[ c_fieldRows, c_fieldColumns ];

		foreach ( var deposit in s_deposits )
		{
			// a deposit that has been emptied is not there any more (one partly taken shows what is left)
			var volume = deposit.m_volume;
			var volumeLeft = ( playerData.m_planetSurfaces != null ) ? playerData.m_planetSurfaces.GetDepositVolumeLeft( planet.m_id, deposit.m_depositIndex ) : -1;

			if ( volumeLeft == 0 )
			{
				continue;
			}

			if ( volumeLeft > 0 )
			{
				volume = Mathf.Min( volume, volumeLeft );
			}

			var column = Mathf.Clamp( Mathf.FloorToInt( ( deposit.m_eastWest + 180.0f ) / 360.0f * c_fieldColumns ), 0, c_fieldColumns - 1 );
			var row = Mathf.Clamp( Mathf.FloorToInt( ( deposit.m_northSouth + 90.0f ) / 180.0f * c_fieldRows ), 0, c_fieldRows - 1 );

			cellVolume[ row, column ] += volume;
			cellCount[ row, column ]++;
			cellEastWest[ row, column ] += deposit.m_eastWest * volume;
			cellNorthSouth[ row, column ] += deposit.m_northSouth * volume;

			if ( cellElements[ row, column ] == null )
			{
				cellElements[ row, column ] = new Dictionary<int, int>();
			}

			cellElements[ row, column ].TryGetValue( deposit.m_elementId, out var elementCount );
			cellElements[ row, column ][ deposit.m_elementId ] = elementCount + 1;
		}

		// the fields, richest first
		var fields = new List<Vector2Int>();

		for ( var row = 0; row < c_fieldRows; row++ )
		{
			for ( var column = 0; column < c_fieldColumns; column++ )
			{
				if ( cellCount[ row, column ] > 0 )
				{
					fields.Add( new Vector2Int( column, row ) );
				}
			}
		}

		fields.Sort( ( a, b ) => cellVolume[ b.y, b.x ].CompareTo( cellVolume[ a.y, a.x ] ) );

		// only the concentrations are fields
		var totalVolume = 0;

		foreach ( var cell in fields )
		{
			totalVolume += cellVolume[ cell.y, cell.x ];
		}

		var concentration = ( fields.Count > 0 ) ? totalVolume / (float) fields.Count * c_fieldConcentration : 0.0f;

		var hadDeposits = fields.Count > 0;
		var richest = hadDeposits ? fields[ 0 ] : Vector2Int.zero;

		fields.RemoveAll( cell => cellVolume[ cell.y, cell.x ] < concentration );

		// (a planet whose deposits are spread evenly still has its richest cell)
		if ( hadDeposits && ( fields.Count == 0 ) )
		{
			fields.Add( richest );
		}

		if ( fields.Count > c_mostFields )
		{
			fields.RemoveRange( c_mostFields, fields.Count - c_mostFields );
		}

		m_mineralFields = fields.Count;

		var found = Mathf.RoundToInt( fields.Count * Mathf.Lerp( c_leastFound, 1.0f, quality ) );
		var elementFields = new Dictionary<int, int>();

		for ( var i = 0; ( i < found ) && ( i < fields.Count ); i++ )
		{
			var cell = fields[ i ];
			var volume = Mathf.Max( cellVolume[ cell.y, cell.x ], 1 );

			// the element most of the field's deposits are of, if the officer can tell
			var mainElement = -1;
			var mostDeposits = 0;

			foreach ( var element in cellElements[ cell.y, cell.x ] )
			{
				if ( element.Value > mostDeposits )
				{
					mainElement = element.Key;
					mostDeposits = element.Value;
				}
			}

			var told = random.NextDouble() < quality;

			if ( told && ( mainElement >= 0 ) )
			{
				elementFields.TryGetValue( mainElement, out var count );
				elementFields[ mainElement ] = count + 1;
			}

			m_marks.Add( new Mark
			{
				m_kind = Kind.Minerals,
				m_eastWest = Wrap( cellEastWest[ cell.y, cell.x ] / volume + Offset( random, error ) ),
				m_northSouth = Mathf.Clamp( cellNorthSouth[ cell.y, cell.x ] / volume + Offset( random, error ), -90.0f, 90.0f ),
				m_size = cellCount[ cell.y, cell.x ],
				m_elementId = told ? mainElement : -1
			} );

			m_mineralFieldsFound++;
		}

		// the element most of the fields told are of
		var mostFields = 0;

		foreach ( var element in elementFields )
		{
			if ( element.Value > mostFields )
			{
				m_mainElementId = element.Key;
				mostFields = element.Value;
			}
		}
	}

	// the latitude bands with life, each found by the officer's chance: the bands of the planet's regions as the shipped game picks a region by latitude
	// (ShippedLifeforms.Result.GetRegionAt: the coldest at the poles, the hottest about the equator), one each side of the equator and one across it, those
	// whose region has lifeforms placed in it
	void AddLifeBands( GD_Planet planet, float quality, System.Random random )
	{
		var life = GD_Species.GetLife( planet );

		m_speciesCount = life.m_species.Count;

		if ( m_speciesCount == 0 )
		{
			return;
		}

		var bands = life.m_highestRegion - life.m_lowestRegion + 1;
		var rowsPerBand = 480 / bands + 1;
		var chance = Mathf.Lerp( c_lifeLeastFound, 1.0f, quality );

		for ( var band = 0; band < bands; band++ )
		{
			var region = life.m_lowestRegion + band;
			var polewardEdge = 90.0f - band * rowsPerBand * c_degreesPerRow;
			var equatorwardEdge = Mathf.Max( 0.0f, 90.0f - ( band + 1 ) * rowsPerBand * c_degreesPerRow );
			var acrossTheEquator = equatorwardEdge <= 0.0f;

			// the north band, then the south one (one band across the equator)
			for ( var hemisphere = 0; hemisphere < ( acrossTheEquator ? 1 : 2 ); hemisphere++ )
			{
				// (drawn for every band, so that what one officer finds does not move what is found after it)
				var roll = random.NextDouble();

				if ( !life.HasLifeIn( region ) )
				{
					continue;
				}

				m_lifeBandCount++;

				if ( roll >= chance )
				{
					continue;
				}

				var lifeBand = new LifeBand { m_crowding = life.m_crowding[ region ] };

				if ( acrossTheEquator )
				{
					lifeBand.m_southEdge = -polewardEdge;
					lifeBand.m_northEdge = polewardEdge;
				}
				else if ( hemisphere == 0 )
				{
					lifeBand.m_southEdge = equatorwardEdge;
					lifeBand.m_northEdge = polewardEdge;
				}
				else
				{
					lifeBand.m_southEdge = -polewardEdge;
					lifeBand.m_northEdge = -equatorwardEdge;
				}

				m_lifeBands.Add( lifeBand );
			}
		}
	}

	// how far off a mark is along one axis (degrees, either way)
	static float Offset( System.Random random, float error )
	{
		return (float) ( random.NextDouble() * 2.0 - 1.0 ) * error;
	}

	// an east-west coordinate back into -180 to 180
	static float Wrap( float eastWest )
	{
		return Mathf.Repeat( eastWest + 180.0f, 360.0f ) - 180.0f;
	}

	// forgets the places worked out (a game loaded in another state of the planet's surface, or the probe)
	public static void Forget()
	{
		s_placesPlanetId = -1;
		s_deposits = null;
	}
}
