using UnityEngine;

// places the ruins of a planet: where the original game has its messages (GameData.m_planetMessageList, recovered from STRINFO 3.1)
// a message at a site lies in a ruin at its latitude and longitude, and one STRINFO puts at "random locations" or "several locations" lies in one ruin of its own,
// at a place picked with random numbers seeded from the planet (the same every time) - and the random ruins of the shipped game's regions (RandomRuinRegions),
// placed only in the regions around the terrain vehicle and moved along with it (PlaceRegionsAround)
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

	// the ruins of an ancient ruin site (STRINFO 2.1.4, 2.5.4) take the places after a planet's random ruins (at most 2) in the keys of the items they hold
	const int c_firstSiteRuinIndex = 5;
	const int c_maxSiteRuins = 5;

	// true if this place among a planet's ruins is a ruin of an ancient ruin site (its Endurium is taken once: the site is not seeded again, see
	// PD_PlanetSurfaces.RefillVisitedPlanets)
	public static bool IsSiteRuinIndex( int ruinIndex )
	{
		return ruinIndex >= c_firstSiteRuinIndex;
	}

	// the random ruins are placed in the regions this near the terrain vehicle (a region is 64 by 96 units), and taken away beyond one more: up to 81 regions
	// with ruins at a time, out of the 4096 of a planet (the shipped game seeds a region when the vehicle first comes into it)
	const int c_regionPlaceDistance = 4;
	const int c_regionKeepDistance = 5;

	// how often the regions about the vehicle are looked at, in seconds
	const float c_regionCheckSeconds = 0.5f;

	// the planet the ruins are placed on, the regions whose random ruins are placed and what was placed for each
	GD_Planet m_planet;
	readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<GameObject>> m_regionObjects = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<GameObject>>();
	float m_regionTimer;

	// the populators whose objects a ruin clears from about itself (ClearAroundRuins), for the ruins placed later
	TerrainGridPopulator[] m_clearedPopulators;

	// place the ruins of this planet
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
		// remember the elevation scale (for what the terrain vehicle drops)
		m_elevationScale = elevationScale;

		// remove the ruins of the last planet
		Tools.DestroyChildrenOf( gameObject );

		m_planet = null;
		m_regionObjects.Clear();

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

		// the ancient ruins whose place and Endurium STRINFO's editors give (2.1.4, 2.5.4) - also with no random numbers, and each holds its Endurium as a random
		// ancient ruin holds its own (taken once, by the key of its item)
		foreach ( var ancientRuinSite in gameData.m_ancientRuinSiteList )
		{
			if ( ( ancientRuinSite.m_planetId != planet.m_id ) || ( ancientRuinSite.m_enduriumVolumes == null ) )
			{
				continue;
			}

			// the first ruin at the site's place, the others in a row to the east of it
			var center = Tools.LatLongToWorldCoordinates( ancientRuinSite.m_longitude, ancientRuinSite.m_latitude );
			var count = Mathf.Min( ancientRuinSite.m_enduriumVolumes.Length, c_maxSiteRuins );

			for ( var i = 0; i < count; i++ )
			{
				Tools.WorldToMapCoordinates( center + Vector3.right * ( i * ancientRuinSite.m_spacing ), out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

				var template = m_ruinTemplates[ ( ancientRuinSite.m_id * 5 + i ) % m_ruinTemplates.Length ];

				var ruinObject = PlaceObjectAt( template, mapX, mapY, elevationScale, ( i * 90 + ancientRuinSite.m_id * 45 ) % 360 );

				ruinObject.name = "Ancient ruin " + ( i + 1 ) + " of site " + ancientRuinSite.m_id;

				var ruin = ruinObject.AddComponent<TerrainRuin>();

				ruin.m_isAncient = true;
				ruin.m_randomItems.Add( new TerrainRuin.RandomItem { m_key = TerrainRuin.GetRandomItemKey( planet.m_id, c_firstSiteRuinIndex + i, ancientRuinSite.m_id % 10 ), m_enduriumVolume = ancientRuinSite.m_enduriumVolumes[ i ] } );
			}
		}

		// the random ruins of the regions about the place the terrain vehicle goes out at (with generators of their own: they draw none of the game's random numbers)
		m_planet = planet;
		m_regionTimer = c_regionCheckSeconds;

		PlaceRegionsAround( DataController.m_instance.m_playerData.m_general.m_lastDisembarkedCoordinates );

		// the special artifacts beside the ruins that hold them (no random numbers)
		foreach ( var ruin in GetComponentsInChildren<TerrainRuin>( true ) )
		{
			ShowArtifacts( ruin );
		}

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

	// how far from the middle of its ruin a special artifact shows (a ruin stands on a square of 24), how big it shows, and how high over the ground it hovers
	const float c_artifactDistance = 16.0f;
	const float c_artifactSize = 4.0f;
	const float c_artifactHover = 2.5f;

	// the soft light over an artifact, which lights it and the ground about it, so that it catches the eye (its colour, brightness and reach)
	static readonly Color c_artifactLightColor = new Color( 0.75f, 0.9f, 1.0f );
	const float c_artifactLightIntensity = 3.0f;
	const float c_artifactLightRange = 28.0f;

	// shows each special artifact the ruin still holds beside it, turning and hovering (ArtifactDisplay) - no random numbers: it is called once the ruins are
	// placed, and again when the terrain vehicle takes something from the ruin; the curios have no model, as the original showed no artifact at all
	public void ShowArtifacts( TerrainRuin ruin )
	{
		if ( ruin == null )
		{
			return;
		}

		foreach ( var display in ruin.m_artifactDisplays )
		{
			if ( display != null )
			{
				Destroy( display );
			}
		}

		ruin.m_artifactDisplays.Clear();

		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		if ( ( playerData.m_planetSurfaces == null ) || ( GetPlanetGenerator() == null ) )
		{
			return;
		}

		foreach ( var artifactSiteId in ruin.m_artifactSiteIds )
		{
			if ( ( artifactSiteId < 0 ) || ( artifactSiteId >= gameData.m_artifactSiteList.Length ) || playerData.m_planetSurfaces.IsArtifactSiteTaken( artifactSiteId ) )
			{
				continue;
			}

			ShowArtifact( ruin, gameData.m_artifactSiteList[ artifactSiteId ].m_artifactId );
		}

		foreach ( var item in ruin.m_randomItems )
		{
			if ( ( item.m_artifactId >= 0 ) && !playerData.m_planetSurfaces.IsRandomRuinItemTaken( item.m_key ) )
			{
				ShowArtifact( ruin, item.m_artifactId );
			}
		}
	}

	// one artifact beside its ruin, on the ground there and hovering over it (each further one of the same ruin a sixth of the way round)
	void ShowArtifact( TerrainRuin ruin, int artifactId )
	{
		var gameData = DataController.m_instance.m_gameData;

		if ( ( artifactId < 0 ) || ( artifactId >= gameData.m_artifactList.Length ) || ( ArtifactModels.Get( gameData.m_artifactList[ artifactId ] ) == null ) )
		{
			return;
		}

		// beside the ruin, by the ruin's own turn about the vertical
		var planetGenerator = GetPlanetGenerator();
		var yaw = ruin.transform.eulerAngles.y;
		var angle = yaw + 30.0f + ruin.m_artifactDisplays.Count * 60.0f;
		var position = ruin.transform.position + Quaternion.Euler( 0.0f, angle, 0.0f ) * Vector3.forward * c_artifactDistance;

		// on the ground there (over water, at its surface)
		Tools.WorldToMapCoordinates( position, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

		position.y = Mathf.Max( planetGenerator.GetBilinearSmoothedElevation( mapX, mapY ), planetGenerator.m_waterElevation ) * m_elevationScale + c_artifactHover + c_artifactSize * 0.5f;

		var holder = new GameObject( "Artifact " + gameData.m_artifactList[ artifactId ].m_name );

		holder.layer = gameObject.layer;
		holder.transform.SetParent( transform, false );
		holder.transform.position = position;
		holder.AddComponent<ArtifactDisplay>().m_artifactId = artifactId;

		ArtifactModels.Make( gameData.m_artifactList[ artifactId ], holder.transform, c_artifactSize, gameObject.layer );

		// the light, a little above it
		var lightObject = new GameObject( "Artifact Light" );

		lightObject.layer = gameObject.layer;
		lightObject.transform.SetParent( holder.transform, false );
		lightObject.transform.localPosition = Vector3.up * c_artifactSize * 1.5f;

		var light = lightObject.AddComponent<Light>();

		light.type = LightType.Point;
		light.color = c_artifactLightColor;
		light.intensity = c_artifactLightIntensity;
		light.range = c_artifactLightRange;

		ruin.m_artifactDisplays.Add( holder );
	}

	// places something the terrain vehicle has dropped where it lies (call it when it is dropped, and the ruins place everything dropped on a planet again on every visit)
	public GameObject PlaceDroppedCargo( PD_PlanetSurfaces.DroppedCargo droppedCargo )
	{
		// the wreck of a terrain vehicle that ran out of fuel looks like the vehicle
		if ( droppedCargo.m_isVehicleWreck )
		{
			return PlaceVehicleWreck( droppedCargo );
		}

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

	// places the wreck of a terrain vehicle that ran out of fuel where it stopped (MOVE-OV WEF48): the terrain vehicle's meshes as they are shown, each a new object
	// with the same mesh and materials, so nothing of the vehicle that runs (its scripts, beams, lights and colliders) comes with it
	GameObject PlaceVehicleWreck( PD_PlanetSurfaces.DroppedCargo droppedCargo )
	{
		var controller = SpaceflightController.m_instance;
		var model = ( ( controller != null ) && ( controller.m_terrainVehicle != null ) ) ? controller.m_terrainVehicle.m_terrainVehicleModel : null;

		if ( model == null )
		{
			return null;
		}

		var wreck = new GameObject( "Terrain vehicle wreck " + droppedCargo.m_id );

		wreck.transform.SetParent( transform, false );
		wreck.transform.position = new Vector3( droppedCargo.m_x, droppedCargo.m_y, droppedCargo.m_z );
		wreck.transform.rotation = Quaternion.Euler( 0.0f, ( droppedCargo.m_id * 47 ) % 360, 0.0f );

		// the model's own size in the world
		var modelScale = model.transform.lossyScale;
		var parentScale = transform.lossyScale;

		wreck.transform.localScale = new Vector3( modelScale.x / Mathf.Max( parentScale.x, 0.0001f ), modelScale.y / Mathf.Max( parentScale.y, 0.0001f ), modelScale.z / Mathf.Max( parentScale.z, 0.0001f ) );

		var toModel = model.transform.worldToLocalMatrix;

		foreach ( var meshFilter in model.GetComponentsInChildren<MeshFilter>( true ) )
		{
			var meshRenderer = meshFilter.GetComponent<MeshRenderer>();

			if ( ( meshRenderer == null ) || !meshRenderer.enabled || ( meshFilter.sharedMesh == null ) || !IsShownInModel( meshFilter.transform, model.transform ) )
			{
				continue;
			}

			// the part where it sits on the model
			var place = toModel * meshFilter.transform.localToWorldMatrix;
			var part = new GameObject( meshFilter.name );

			part.transform.SetParent( wreck.transform, false );
			part.transform.localPosition = place.GetColumn( 3 );
			part.transform.localRotation = place.rotation;
			part.transform.localScale = place.lossyScale;

			part.AddComponent<MeshFilter>().sharedMesh = meshFilter.sharedMesh;
			part.AddComponent<MeshRenderer>().sharedMaterials = meshRenderer.sharedMaterials;
		}

		wreck.AddComponent<TerrainDroppedCargo>().m_droppedCargoId = droppedCargo.m_id;

		return wreck;
	}

	// true if a part of the model is switched on along the way up to the model (a beam or an effect that is off is not part of the vehicle's look)
	static bool IsShownInModel( Transform part, Transform model )
	{
		for ( var current = part; ( current != null ) && ( current != model ); current = current.parent )
		{
			if ( !current.gameObject.activeSelf )
			{
				return false;
			}
		}

		return true;
	}

	// takes away the rocks and the trees that are too close to a ruin (call this once all the populators have placed their objects - taking them away moves nothing else)
	public void ClearAroundRuins( TerrainGridPopulator[] populators )
	{
		// (remembered for the random ruins placed later, as the vehicle drives)
		m_clearedPopulators = populators;

		foreach ( Transform ruin in transform )
		{
			ClearAroundRuin( ruin );
		}
	}

	// takes the rocks and trees next to one ruin away
	void ClearAroundRuin( Transform ruin )
	{
		if ( m_clearedPopulators == null )
		{
			return;
		}

		foreach ( var populator in m_clearedPopulators )
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

	// the random ruins of the regions within c_regionPlaceDistance of this place are placed, and those beyond c_regionKeepDistance taken away - they come back
	// the same when the vehicle comes back (RandomRuinRegions works a region out the same every time, and what was taken from a ruin is saved by its key)
	void PlaceRegionsAround( Vector3 worldPosition )
	{
		var planetGenerator = GetPlanetGenerator();

		if ( ( m_planet == null ) || ( planetGenerator == null ) || !RandomRuinRegions.HasRuins( m_planet ) || ( m_ruinTemplates == null ) || ( m_ruinTemplates.Length == 0 ) )
		{
			return;
		}

		Tools.WorldToMapCoordinates( worldPosition, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

		var centre = RandomRuinRegions.GetRegion( mapX, mapY, planetGenerator );
		var kept = new System.Collections.Generic.HashSet<int>( RandomRuinRegions.GetRegionsAround( centre, c_regionKeepDistance ) );

		// the regions too far away go (their ruins with what shows beside them)
		var gone = new System.Collections.Generic.List<int>();

		foreach ( var region in m_regionObjects.Keys )
		{
			if ( !kept.Contains( region ) )
			{
				gone.Add( region );
			}
		}

		foreach ( var region in gone )
		{
			RemoveRegion( region );
		}

		// the regions near enough come (each once)
		foreach ( var region in RandomRuinRegions.GetRegionsAround( centre, c_regionPlaceDistance ) )
		{
			if ( !m_regionObjects.ContainsKey( region ) )
			{
				PlaceRegion( region );
			}
		}
	}

	// places the random ruins of one region
	void PlaceRegion( int region )
	{
		var placed = new System.Collections.Generic.List<GameObject>();

		m_regionObjects[ region ] = placed;

		foreach ( var regionRuin in RandomRuinRegions.ForRegion( GetPlanetGenerator(), m_elevationScale, m_planet, region, m_ruinTemplates.Length ) )
		{
			// (not in the spawn lists: nothing is placed after the ruins, and a list that took every ruin the vehicle passed would only grow)
			var ruinObject = PlaceObjectAt( m_ruinTemplates[ Mathf.Clamp( regionRuin.m_templateIndex, 0, m_ruinTemplates.Length - 1 ) ], regionRuin.m_mapX, regionRuin.m_mapY, m_elevationScale, regionRuin.m_rotation, false );

			ruinObject.name = ( regionRuin.m_isAncient ? "Ancient ruin" : "Recent ruin" ) + " of region " + region;

			var ruin = ruinObject.AddComponent<TerrainRuin>();

			ruin.m_isAncient = regionRuin.m_isAncient;
			ruin.m_randomItems.AddRange( regionRuin.m_items );

			ShowArtifacts( ruin );
			ClearAroundRuin( ruinObject.transform );

			placed.Add( ruinObject );
		}
	}

	// takes away the random ruins of one region
	void RemoveRegion( int region )
	{
		if ( m_regionObjects.TryGetValue( region, out var placed ) )
		{
			foreach ( var ruinObject in placed )
			{
				if ( ruinObject == null )
				{
					continue;
				}

				if ( ruinObject.TryGetComponent<TerrainRuin>( out var ruin ) )
				{
					foreach ( var display in ruin.m_artifactDisplays )
					{
						if ( display != null )
						{
							Destroy( display );
						}
					}
				}

				// (out of the populator at once, so that Scan and Cargo, which go through its children, no longer find it)
				ruinObject.SetActive( false );
				ruinObject.transform.SetParent( null, false );

				Destroy( ruinObject );
			}
		}

		m_regionObjects.Remove( region );
	}

	// unity update: the random ruins follow the terrain vehicle
	void Update()
	{
		if ( ( m_planet == null ) || ( m_terrainVehicle == null ) || ( SpaceflightController.m_instance == null ) || ( DataController.m_instance.m_playerData.m_general.m_location != PD_General.Location.Disembarked ) )
		{
			return;
		}

		m_regionTimer -= Time.deltaTime;

		if ( m_regionTimer > 0.0f )
		{
			return;
		}

		m_regionTimer = c_regionCheckSeconds;

		PlaceRegionsAround( m_terrainVehicle.transform.position );
	}

	// the places of a planet's random ruins, worked out as the surface places them, in degrees as the landing map gives them (x east-west, y north-south), and
	// whether each one is ancient - every region of the planet (the game's random numbers are not used)
	public static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Vector2, bool>> GetRandomRuinPlaces( PlanetGenerator planetGenerator, float elevationScale, GD_Planet planet )
	{
		var places = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Vector2, bool>>();

		foreach ( var regionRuin in RandomRuinRegions.ForPlanet( planetGenerator, elevationScale, planet ) )
		{
			var world = Tools.MapToWorldCoordinates( regionRuin.m_mapX, regionRuin.m_mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

			Tools.WorldToLatLongCoordinates( world, out var eastWest, out var northSouth );

			places.Add( new System.Collections.Generic.KeyValuePair<Vector2, bool>( new Vector2( eastWest, northSouth ), regionRuin.m_isAncient ) );
		}

		return places;
	}

	// the places of a planet's fixed ancient ruins, in degrees as the landing map gives them: the formations of ancient ruins (STRINFO 4.1) and the ancient ruin
	// sites (STRINFO 2.1.4, 2.5.4) - its random ancient ruins go by region (RandomRuinRegions.GetAncientRegions: too many for a mark each on a planet of ruins)
	public static System.Collections.Generic.List<Vector2> GetAncientRuinPlaces( PlanetGenerator planetGenerator, float elevationScale, GD_Planet planet )
	{
		var gameData = DataController.m_instance.m_gameData;
		var places = new System.Collections.Generic.List<Vector2>();

		foreach ( var ruinFormation in gameData.m_ruinFormationList )
		{
			if ( ( ruinFormation.m_artifactSiteId < 0 ) || ( ruinFormation.m_artifactSiteId >= gameData.m_artifactSiteList.Length ) )
			{
				continue;
			}

			var artifactSite = gameData.m_artifactSiteList[ ruinFormation.m_artifactSiteId ];

			if ( artifactSite.m_planetId == planet.m_id )
			{
				// (a site's longitude is the map's east-west, as the formations above are placed)
				places.Add( new Vector2( artifactSite.m_longitude, artifactSite.m_latitude ) );
			}
		}

		// the ancient ruin sites (STRINFO 2.1.4, 2.5.4)
		foreach ( var ancientRuinSite in gameData.m_ancientRuinSiteList )
		{
			if ( ancientRuinSite.m_planetId == planet.m_id )
			{
				places.Add( new Vector2( ancientRuinSite.m_longitude, ancientRuinSite.m_latitude ) );
			}
		}

		return places;
	}

	// picks a place for a ruin with the planet's random numbers: not steep, and above the water - the last place tried if no place is good
	static void FindPlaceForRuin( PlanetGenerator planetGenerator, float elevationScale, out float mapX, out float mapY )
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
