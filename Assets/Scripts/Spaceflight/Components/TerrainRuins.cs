using UnityEngine;

// places the ruins of a planet: where the original game has its messages (GameData.m_planetMessageList, recovered from STRINFO 3.1)
// a message at a site lies in a ruin at its latitude and longitude, and one STRINFO puts at "random locations" or "several locations" lies in one ruin of its own,
// at a place picked with random numbers seeded from the planet (the same every time) - the random ruins of the 1984 design notes are not placed (roadmap 3.5)
public class TerrainRuins : TerrainGridPopulator
{
	// the ruin templates (the five ancient ruin models for now)
	public GameObject[] m_ruinTemplates;

	// reference to the terrain vehicle
	public TerrainVehicle m_terrainVehicle;

	// rocks and trees closer than this to a ruin are taken away, so that the ruin stands on its own
	const float c_clearRadius = 16.0f;

	// how many places to try for a ruin at a random place on the planet
	const int c_maxTries = 50;

	// place the ruins of this planet
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
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

		// give the game its random numbers back
		Random.state = randomState;
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
