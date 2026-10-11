using UnityEngine;

// places the ruins of a planet: where the original game has its messages (GameData.m_planetMessageList, recovered from STRINFO 3.1)
// a message at a site lies in a ruin at its latitude and longitude, and one STRINFO puts at "random locations" or "several locations" lies in one ruin of its own,
// at a place picked with random numbers seeded from the planet (the same every time) - and the random ruins of the 1984 design notes (see PlaceRandomRuins)
public class TerrainRuins : TerrainGridPopulator
{
	// the ruin templates (the five ancient ruin models for now)
	public GameObject[] m_ruinTemplates;

	// reference to the terrain vehicle
	public TerrainVehicle m_terrainVehicle;

	// rocks and trees closer than this to a ruin are taken away, so that the ruin stands on its own
	const float c_clearRadius = 16.0f;

	// the size of something dropped by the terrain vehicle, as a part of a ruin model (a stand-in until there are models)
	const float c_droppedCargoScale = 0.2f;

	// the elevation scale of the planet the ruins were placed on (to place what the terrain vehicle drops later)
	float m_elevationScale;

	// how many places to try for a ruin at a random place on the planet
	const int c_maxTries = 50;

	// place the ruins of this planet
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
		// remember the elevation scale (for what the terrain vehicle drops)
		m_elevationScale = elevationScale;

		// remove the ruins of the last planet
		Tools.DestroyChildrenOf( gameObject );

		// nothing to place without templates
		if ( ( m_ruinTemplates == null ) || ( m_ruinTemplates.Length == 0 ) )
		{
			return;
		}

		// get to this planet
		var planet = planetGenerator.GetPlanet();

		var gameData = DataController.m_instance.m_gameData;

		// remember where the game's random numbers are, so that they can carry on from there when we are done (as the other populators do)
		var randomState = Random.state;

		Random.InitState( randomSeed );

		// the ruins placed at a site so far, by latitude and longitude (several messages can lie at the same site - Mardan 2 has three)
		var ruinsAtSites = new System.Collections.Generic.Dictionary<long, TerrainRuin>();

		// go through the messages of this planet in their order (the order the random places are picked in must be the same every time)
		foreach ( var planetMessage in gameData.m_planetMessageList )
		{
			if ( planetMessage.m_planetId != planet.m_id )
			{
				continue;
			}

			float mapX;
			float mapY;

			var siteKey = (long) planetMessage.m_latitude * 1000 + planetMessage.m_longitude;

			if ( planetMessage.m_placement == "Site" )
			{
				// is there a ruin at this site already?
				if ( ruinsAtSites.TryGetValue( siteKey, out var ruinAtSite ) )
				{
					// yes - the message lies in it too
					ruinAtSite.m_messageIds.Add( planetMessage.m_id );

					continue;
				}

				// at its latitude and longitude (the port's world x is east-west and z north-south - see Tools.LatLongToWorldCoordinates)
				var worldCoordinates = Tools.LatLongToWorldCoordinates( planetMessage.m_longitude, planetMessage.m_latitude );

				Tools.WorldToMapCoordinates( worldCoordinates, out mapX, out mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );
			}
			else
			{
				// at a place of its own on the planet
				FindPlaceForRuin( planetGenerator, elevationScale, out mapX, out mapY );
			}

			// one of the templates, by the message (no random number, so nothing else moves)
			var template = m_ruinTemplates[ planetMessage.m_id % m_ruinTemplates.Length ];

			var ruinObject = PlaceObjectAt( template, mapX, mapY, elevationScale, ( planetMessage.m_id * 137 ) % 360 );

			ruinObject.name = "Ruin of message " + planetMessage.m_id;

			var ruin = ruinObject.AddComponent<TerrainRuin>();

			ruin.m_messageIds.Add( planetMessage.m_id );

			if ( planetMessage.m_placement == "Site" )
			{
				ruinsAtSites[ siteKey ] = ruin;
			}
		}

		// the special artifacts of this planet (STRINFO 4.1) - after the messages, so that the places of the ruins of the messages do not change, and with no random
		// numbers: an artifact lies in the ruin of its site, which a message can share (the Hypercube lies at the site of the invoice on Earth)
		foreach ( var artifactSite in gameData.m_artifactSiteList )
		{
			if ( artifactSite.m_planetId != planet.m_id )
			{
				continue;
			}

			var siteKey = (long) artifactSite.m_latitude * 1000 + artifactSite.m_longitude;

			// is there a ruin at this site already?
			if ( !ruinsAtSites.TryGetValue( siteKey, out var ruin ) )
			{
				// no - a ruin of its own, at its latitude and longitude
				var worldCoordinates = Tools.LatLongToWorldCoordinates( artifactSite.m_longitude, artifactSite.m_latitude );

				Tools.WorldToMapCoordinates( worldCoordinates, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

				var template = m_ruinTemplates[ artifactSite.m_id % m_ruinTemplates.Length ];

				var ruinObject = PlaceObjectAt( template, mapX, mapY, elevationScale, ( artifactSite.m_id * 211 ) % 360 );

				ruinObject.name = "Ruin of artifact site " + artifactSite.m_id;

				ruin = ruinObject.AddComponent<TerrainRuin>();

				ruinsAtSites[ siteKey ] = ruin;
			}

			ruin.m_artifactSiteIds.Add( artifactSite.m_id );
		}

		// the formations of ancient ruins around the ruins of two of the artifacts (STRINFO 4.1) - also with no random numbers, and they hold nothing
		foreach ( var ruinFormation in gameData.m_ruinFormationList )
		{
			if ( ( ruinFormation.m_artifactSiteId < 0 ) || ( ruinFormation.m_artifactSiteId >= gameData.m_artifactSiteList.Length ) )
			{
				continue;
			}

			var artifactSite = gameData.m_artifactSiteList[ ruinFormation.m_artifactSiteId ];

			if ( artifactSite.m_planetId != planet.m_id )
			{
				continue;
			}

			// the middle of the formation is the ruin of the artifact
			var center = Tools.LatLongToWorldCoordinates( artifactSite.m_longitude, artifactSite.m_latitude );

			// the number of columns of a block (a block of 15 has rows of 4, with the artifact's ruin in the first place of the first row)
			var columns = Mathf.Max( 1, Mathf.CeilToInt( Mathf.Sqrt( ruinFormation.m_count + 1 ) ) );

			for ( var i = 0; i < ruinFormation.m_count; i++ )
			{
				Vector3 offset;

				if ( ruinFormation.m_shape == "Ring" )
				{
					// in a circle around the artifact's ruin, the first one to the north
					var angle = i * Mathf.PI * 2.0f / ruinFormation.m_count;

					offset = new Vector3( Mathf.Sin( angle ), 0.0f, Mathf.Cos( angle ) ) * ruinFormation.m_spacing;
				}
				else
				{
					// in rows to the east and the north of the artifact's ruin, which has the southwest corner (place 0) of the block
					var place = i + 1;

					offset = new Vector3( ( place % columns ) * ruinFormation.m_spacing, 0.0f, ( place / columns ) * ruinFormation.m_spacing );
				}

				Tools.WorldToMapCoordinates( center + offset, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

				var template = m_ruinTemplates[ ( ruinFormation.m_id * 7 + i ) % m_ruinTemplates.Length ];

				var ruinObject = PlaceObjectAt( template, mapX, mapY, elevationScale, ( i * 60 + ruinFormation.m_id * 30 ) % 360 );

				ruinObject.name = "Ruin " + ( i + 1 ) + " of " + ruinFormation.m_name;

				ruinObject.AddComponent<TerrainRuin>();
			}
		}

		// the random ruins (their random numbers come after everything else, so nothing above moves)
		PlaceRandomRuins( planetGenerator, elevationScale, planet );

		// what the terrain vehicle has dropped on this planet (no random numbers either)
		var playerData = DataController.m_instance.m_playerData;

		if ( playerData.m_planetSurfaces != null )
		{
			playerData.m_planetSurfaces.Validate();

			foreach ( var droppedCargo in playerData.m_planetSurfaces.m_droppedCargoList )
			{
				if ( droppedCargo.m_planetId == planet.m_id )
				{
					PlaceDroppedCargo( droppedCargo );
				}
			}
		}

		// give the game its random numbers back
		Random.state = randomState;
	}

	// places something the terrain vehicle has dropped where it lies (call it when it is dropped, and the ruins place everything dropped on a planet again on every visit)
	public GameObject PlaceDroppedCargo( PD_PlanetSurfaces.DroppedCargo droppedCargo )
	{
		if ( ( m_ruinTemplates == null ) || ( m_ruinTemplates.Length == 0 ) || ( GetPlanetGenerator() == null ) )
		{
			return null;
		}

		Tools.WorldToMapCoordinates( new Vector3( droppedCargo.m_x, 0.0f, droppedCargo.m_z ), out var mapX, out var mapY, GetPlanetGenerator().m_textureMapWidth, GetPlanetGenerator().m_textureMapHeight );

		var droppedObject = PlaceObjectAt( m_ruinTemplates[ 0 ], mapX, mapY, m_elevationScale, ( droppedCargo.m_id * 47 ) % 360 );

		// at the height the terrain vehicle was at when it dropped it (the vehicle floats on water, where the ground is lower)
		var position = droppedObject.transform.position;

		position.y = droppedCargo.m_y;

		droppedObject.transform.position = position;

		droppedObject.transform.localScale = Vector3.one * c_droppedCargoScale;
		droppedObject.name = "Dropped cargo " + droppedCargo.m_id;

		droppedObject.AddComponent<TerrainDroppedCargo>().m_droppedCargoId = droppedCargo.m_id;

		return droppedObject;
	}

	// takes away the rocks and the trees that are too close to a ruin (call this once all the populators have placed their objects - taking them away moves nothing else)
	public void ClearAroundRuins( TerrainGridPopulator[] populators )
	{
		foreach ( Transform ruin in transform )
		{
			foreach ( var populator in populators )
			{
				if ( populator == null )
				{
					continue;
				}

				foreach ( Transform child in populator.transform )
				{
					var delta = child.position - ruin.position;

					delta.y = 0.0f;

					if ( delta.magnitude < c_clearRadius )
					{
						// destroyed, not hidden: the cargo and scan buttons go through the children of a populator, hidden ones too
						Destroy( child.gameObject );
					}
				}
			}
		}
	}

	// the random ruins of the 1984 design notes (planet.txt; the original's kernel has a class of ruins, and its SEED-OV overlay seeds a planet's "mineral, lifeform
	// & ruin" objects, but its code is not in the sources): ancient crystal ruins with lumps of Endurium or "truly amazing" artifacts worth 500 to 5000 MU, and
	// Old Empire ruins with "amazing" artifacts worth 50 to 1000 MU, one to four of them (60%, 25%, 10% and 5%), whose value the crew estimates to within half of
	// it - the notes do not say how many ruins a planet has, how much Endurium a ruin holds or which artifacts, so those are the port's: a quarter of the planets
	// has one or two, half of them ancient, half of the ancient ones hold 1 to 5 cubic meters of Endurium, and the artifacts are the game data's that are found
	// nowhere else, in the range of value of the ruin - the planets the original's messages and artifacts lie on have none, as STRINFO describes them
	public static int GetRandomRuinCount( GD_Planet planet )
	{
		var gameData = DataController.m_instance.m_gameData;

		foreach ( var planetMessage in gameData.m_planetMessageList )
		{
			if ( planetMessage.m_planetId == planet.m_id )
			{
				return 0;
			}
		}

		foreach ( var artifactSite in gameData.m_artifactSiteList )
		{
			if ( artifactSite.m_planetId == planet.m_id )
			{
				return 0;
			}
		}

		// from the planet's id (so that a planet's ruins can be known without placing them), 18 in 100 with one and 7 with two
		var hash = ( planet.m_id * 7919 + 104729 ) % 100;

		return ( hash < 18 ) ? 1 : ( ( hash < 25 ) ? 2 : 0 );
	}

	// the artifacts of the game data that random ruins hold: found nowhere else (no site, not sold) and in this range of value
	static System.Collections.Generic.List<int> FindRandomRuinArtifacts( int lowestValue, int highestValue )
	{
		var gameData = DataController.m_instance.m_gameData;
		var artifactIds = new System.Collections.Generic.List<int>();

		for ( var artifactId = 0; artifactId < gameData.m_artifactList.Length; artifactId++ )
		{
			var artifact = gameData.m_artifactList[ artifactId ];

			if ( artifact.m_availableInStarport || ( artifact.m_name == "Black Box" ) || ( artifact.m_actualValue < lowestValue ) || ( artifact.m_actualValue > highestValue ) )
			{
				continue;
			}

			var hasSite = false;

			foreach ( var artifactSite in gameData.m_artifactSiteList )
			{
				hasSite = hasSite || ( artifactSite.m_artifactId == artifactId );
			}

			if ( !hasSite )
			{
				artifactIds.Add( artifactId );
			}
		}

		return artifactIds;
	}

	// how many artifacts a random ruin holds: "60% theres 1, 25% theres 2, 10% theres 3, 5% theres 4"
	static int GetRandomArtifactCount()
	{
		var chance = Random.value;

		return ( chance < 0.6f ) ? 1 : ( ( chance < 0.85f ) ? 2 : ( ( chance < 0.95f ) ? 3 : 4 ) );
	}

	void PlaceRandomRuins( PlanetGenerator planetGenerator, float elevationScale, GD_Planet planet )
	{
		var count = GetRandomRuinCount( planet );
		var gameData = DataController.m_instance.m_gameData;

		for ( var ruinIndex = 0; ruinIndex < count; ruinIndex++ )
		{
			FindPlaceForRuin( planetGenerator, elevationScale, out var mapX, out var mapY );

			var isAncient = Random.value < 0.5f;
			var template = m_ruinTemplates[ Random.Range( 0, m_ruinTemplates.Length ) ];
			var ruinObject = PlaceObjectAt( template, mapX, mapY, elevationScale, Random.Range( 0, 360 ) );

			ruinObject.name = ( isAncient ? "Ancient ruin " : "Old Empire ruin " ) + ( ruinIndex + 1 );

			var ruin = ruinObject.AddComponent<TerrainRuin>();

			ruin.m_isAncient = isAncient;

			// an ancient ruin: lumps of Endurium, or truly amazing artifacts
			if ( isAncient && ( Random.value < 0.5f ) )
			{
				ruin.m_randomItems.Add( new TerrainRuin.RandomItem { m_key = TerrainRuin.GetRandomItemKey( planet.m_id, ruinIndex, 0 ), m_enduriumVolume = Random.Range( 1, 6 ) * 10 } );

				continue;
			}

			var artifactIds = isAncient ? FindRandomRuinArtifacts( 500, 5000 ) : FindRandomRuinArtifacts( 50, 1000 );

			if ( artifactIds.Count == 0 )
			{
				continue;
			}

			var artifactCount = GetRandomArtifactCount();

			for ( var itemIndex = 0; itemIndex < artifactCount; itemIndex++ )
			{
				var artifactId = artifactIds[ Random.Range( 0, artifactIds.Count ) ];
				var value = gameData.m_artifactList[ artifactId ].m_actualValue;

				// the estimate is off by up to half of the value, most often by little ("a normal distribution with the peak at 0"), rounded to the nearest 100
				var error = ( Random.value + Random.value - 1.0f ) * 0.5f;
				var estimate = Mathf.Max( 100, Mathf.RoundToInt( value * ( 1.0f + error ) / 100.0f ) * 100 );

				ruin.m_randomItems.Add( new TerrainRuin.RandomItem { m_key = TerrainRuin.GetRandomItemKey( planet.m_id, ruinIndex, itemIndex ), m_artifactId = artifactId, m_estimatedValue = estimate } );
			}
		}
	}

	// picks a place for a ruin with the planet's random numbers: not steep, and above the water - the last place tried if no place is good
	void FindPlaceForRuin( PlanetGenerator planetGenerator, float elevationScale, out float mapX, out float mapY )
	{
		var minimumElevation = ( planetGenerator.m_maximumElevation - planetGenerator.m_waterElevation ) * 0.05f + planetGenerator.m_waterElevation;

		mapX = 0.0f;
		mapY = 0.0f;

		for ( var i = 0; i < c_maxTries; i++ )
		{
			mapX = Random.Range( 0.0f, planetGenerator.m_textureMapWidth );
			mapY = Random.Range( planetGenerator.m_textureMapHeight * 0.125f, planetGenerator.m_textureMapHeight * 0.875f );

			var normal = planetGenerator.GetBilinearSmoothedNormal( mapX, mapY, elevationScale * 0.125f );

			if ( normal.y < 0.707f )
			{
				continue;
			}

			if ( planetGenerator.GetBilinearSmoothedElevation( mapX, mapY ) < minimumElevation )
			{
				continue;
			}

			return;
		}
	}
}
