using System.Collections.Generic;
using UnityEngine;

// places the lifeforms of a planet: some of each of its species (GD_Species.ForPlanet), where the manual (page 21) says they live - "lifeforms are not distributed
// evenly over the planet's surface, but tend to clump in the lower elevations near the equator or other temperate areas" - never on liquid, mud or lava
// (planet.txt) - with the planet's random numbers, after every other populator, so nothing else moves; they are placed again on every visit (the notes:
// the lifeforms of an encounter area are made as it is entered) - how many of each species and where exactly are the port's
public class TerrainLifeforms : TerrainGridPopulator
{
	// how many of a species are placed
	const int c_fewestOfASpecies = 3;
	const int c_mostOfASpecies = 8;

	// how many places to try for each lifeform (the lowest is taken)
	const int c_tries = 6;

	// the size of the smallest lifeform's marker, in world units (and as much again for each step of size)
	const float c_markerScale = 1.5f;

	// the elevation scale of the planet (for the lifeforms that move)
	float m_elevationScale;

	// "You must be directly beside an object to put it into Cargo" (the manual, page 21): within the notes' adjacent square
	public const float c_besideRange = 12.0f;

	// a recording (planet.txt, the lifeform options of Cargo): "recording takes about 3" seconds and "uses 2 units of energy" (of a full terrain vehicle's 999); "If
	// the alien runs away before the process is completed then the attempt is aborted and the message is given :"it got away"" - once it is half again as far as
	// beside the vehicle (the port's) - and "If the alien attacks before the process is completed, completion of the process is delayed" - by a second for each
	// attack (the port's)
	const float c_recordingTime = 3.0f;
	const float c_recordingEnergy = 2.0f / 999.0f;
	const float c_getAwayRange = c_besideRange * 1.5f;
	const float c_attackDelay = 1.0f;

	// a capture (planet.txt): "Capturing takes about 6 seconds", "Capturing does not require energy" and "Capturing raises the aliens EDL" - by 1 to 20, as the
	// laser angers a lifeform (the port's) - and "If the lifeform was captured multiply its value by 5"
	const float c_captureTime = 6.0f;
	const int c_captureValueFactor = 5;

	// the lifeform Cargo offered (the console shows Record and Ignore for it)
	public TerrainLifeform m_offeredLifeform;

	// the recording under way
	bool m_recording;
	bool m_capturing;
	TerrainLifeform m_recordingTarget;
	float m_recordingTimeLeft;

	// the lifeforms placed (for the pairing of mates: lf-behav.txt, "Pair all un-paired lifeforms in the encounter area")
	public readonly List<TerrainLifeform> m_lifeformList = new List<TerrainLifeform>();

	// place the lifeforms of this planet
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
		m_elevationScale = elevationScale;

		// remove the lifeforms of the last planet
		Tools.DestroyChildrenOf( gameObject );

		m_lifeformList.Clear();

		var speciesList = GD_Species.ForPlanet( planetGenerator.GetPlanet() );

		if ( speciesList.Count == 0 )
		{
			return;
		}

		// remember where the game's random numbers are, so that they can carry on from there when we are done (as the other populators do)
		var randomState = Random.state;

		Random.InitState( randomSeed );

		var minimumElevation = ( planetGenerator.m_maximumElevation - planetGenerator.m_waterElevation ) * 0.05f + planetGenerator.m_waterElevation;

		foreach ( var species in speciesList )
		{
			var count = Random.Range( c_fewestOfASpecies, c_mostOfASpecies + 1 );

			for ( var i = 0; i < count; i++ )
			{
				// the lowest dry place of a few near the equator
				var found = false;
				var bestX = 0.0f;
				var bestY = 0.0f;
				var bestElevation = float.MaxValue;

				for ( var j = 0; j < c_tries; j++ )
				{
					var mapX = Random.Range( 0.0f, planetGenerator.m_textureMapWidth );
					var mapY = Random.Range( planetGenerator.m_textureMapHeight * 0.35f, planetGenerator.m_textureMapHeight * 0.65f );
					var elevation = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY );

					if ( ( elevation >= minimumElevation ) && ( elevation < bestElevation ) )
					{
						found = true;
						bestX = mapX;
						bestY = mapY;
						bestElevation = elevation;
					}
				}

				if ( found )
				{
					PlaceLifeform( species, bestX, bestY, elevationScale );
				}
			}
		}

		// give the game its random numbers back
		Random.state = randomState;
	}

	/// <summary>
	/// Works out where Initialize places the lifeforms of a planet, without placing any (PlanetSurvey: the science officer's survey from orbit): the same random
	/// numbers in the same order, so the same places (keep the two loops the same; the probe's orbitsurvey compares them with the lifeforms on the surface).
	/// Each place is its species' index in GD_Species.ForPlanet and its place on the planet's map. The game's random numbers are put back afterwards.
	/// </summary>
	public static List<KeyValuePair<int, Vector2>> ReplayPlaces( PlanetGenerator planetGenerator, int randomSeed )
	{
		var places = new List<KeyValuePair<int, Vector2>>();

		if ( ( planetGenerator == null ) || ( planetGenerator.m_elevation == null ) )
		{
			return places;
		}

		var speciesList = GD_Species.ForPlanet( planetGenerator.GetPlanet() );

		if ( speciesList.Count == 0 )
		{
			return places;
		}

		var randomState = Random.state;

		Random.InitState( randomSeed );

		var minimumElevation = ( planetGenerator.m_maximumElevation - planetGenerator.m_waterElevation ) * 0.05f + planetGenerator.m_waterElevation;

		for ( var speciesIndex = 0; speciesIndex < speciesList.Count; speciesIndex++ )
		{
			var count = Random.Range( c_fewestOfASpecies, c_mostOfASpecies + 1 );

			for ( var i = 0; i < count; i++ )
			{
				var found = false;
				var bestX = 0.0f;
				var bestY = 0.0f;
				var bestElevation = float.MaxValue;

				for ( var j = 0; j < c_tries; j++ )
				{
					var mapX = Random.Range( 0.0f, planetGenerator.m_textureMapWidth );
					var mapY = Random.Range( planetGenerator.m_textureMapHeight * 0.35f, planetGenerator.m_textureMapHeight * 0.65f );
					var elevation = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY );

					if ( ( elevation >= minimumElevation ) && ( elevation < bestElevation ) )
					{
						found = true;
						bestX = mapX;
						bestY = mapY;
						bestElevation = elevation;
					}
				}

				if ( found )
				{
					places.Add( new KeyValuePair<int, Vector2>( speciesIndex, new Vector2( bestX, bestY ) ) );
				}
			}
		}

		Random.state = randomState;

		return places;
	}

	// the height of the ground at this place, and whether it is dry ground a lifeform can walk on (planet.txt: lifeforms on the ground never move onto "liquid" or
	// "lava") - false off the planet's maps - over a liquid the height is that of its surface, where the terrain grid draws it
	public bool GetGround( Vector3 worldPosition, out float height )
	{
		var planetGenerator = GetPlanetGenerator();

		height = 0.0f;

		if ( planetGenerator == null )
		{
			return false;
		}

		Tools.WorldToMapCoordinates( worldPosition, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

		var elevation = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY );

		height = Mathf.Max( elevation, planetGenerator.m_waterElevation ) * m_elevationScale;

		return elevation >= planetGenerator.m_waterElevation;
	}

	// the height of what is under this place, the ground or the surface of a liquid - false off the planet's maps
	public bool GetSurface( Vector3 worldPosition, out float height )
	{
		GetGround( worldPosition, out height );

		return GetPlanetGenerator() != null;
	}

	// the models of the body plans (Resources/Lifeforms, made with Blender by DevTools/LifeformModels), loaded once
	static readonly Dictionary<string, GameObject> s_models = new Dictionary<string, GameObject>();

	// the model of a body plan, or null if there is none
	static GameObject LoadModel( string name )
	{
		if ( !s_models.TryGetValue( name, out var model ) )
		{
			model = Resources.Load<GameObject>( "Lifeforms/" + name );

			s_models[ name ] = model;
		}

		return model;
	}

	// the name of a species' model: its body plan, and for a plant one of the two models of its size (by its planet and its place among the planet's species,
	// so that no random number is drawn for it)
	public static string GetModelName( GD_Species species, int planetId )
	{
		var name = species.m_bodyPlan.ToString();

		if ( species.IsPlant() )
		{
			name += ( ( ( planetId + species.m_index ) % 2 ) == 0 ) ? "A" : "B";
		}

		return name;
	}

	// a lifeform at this place: the model of its body plan, tinted with its species' colour (TerrainLifeform), as large as its size - the models stand on their
	// base and face along +Z, and each one is turned by its place on the map (no random number is drawn for it); without a model a plain shape stands in for
	// it (a pillar for a plant, a ball for a radial animal, a box for an amorphous one, and a capsule for a bilateral one)
	void PlaceLifeform( GD_Species species, float mapX, float mapY, float elevationScale )
	{
		var planetGenerator = GetPlanetGenerator();
		var size = c_markerScale * ( 1.0f + species.m_size * 0.5f );
		var position = Tools.MapToWorldCoordinates( mapX, mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );
		var ground = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY ) * elevationScale;
		var model = LoadModel( GetModelName( species, planetGenerator.GetPlanet().m_id ) );

		if ( model != null )
		{
			var modelObject = Instantiate( model );

			modelObject.transform.SetParent( transform, false );
			modelObject.transform.position = new Vector3( position.x, ground, position.z );
			modelObject.transform.rotation = Quaternion.Euler( 0.0f, Mathf.Repeat( mapX * 37.0f + mapY * 91.0f, 360.0f ), 0.0f );
			modelObject.transform.localScale = Vector3.one * size;
			modelObject.name = "Lifeform " + species.m_index + " (" + species.GetBodyPlanText() + ")";

			var modelLifeform = modelObject.AddComponent<TerrainLifeform>();

			modelLifeform.m_groundOffset = 0.0f;
			modelLifeform.Initialize( species, this );

			m_lifeformList.Add( modelLifeform );

			return;
		}

		PrimitiveType shape;

		if ( species.IsPlant() )
		{
			shape = PrimitiveType.Cylinder;
		}
		else if ( ( species.m_bodyPlan >= GD_Species.BodyPlan.RadialNoLegs ) && ( species.m_bodyPlan <= GD_Species.BodyPlan.RadialManyLegs ) )
		{
			shape = PrimitiveType.Sphere;
		}
		else if ( species.m_bodyPlan >= GD_Species.BodyPlan.AmorphousNoLegs )
		{
			shape = PrimitiveType.Cube;
		}
		else
		{
			shape = PrimitiveType.Capsule;
		}

		var lifeformObject = GameObject.CreatePrimitive( shape );

		// no collider: nothing collides with the objects on the ground
		var collider = lifeformObject.GetComponent<Collider>();

		if ( collider != null )
		{
			Destroy( collider );
		}

		// the pillar and the capsule are two units tall and the ball and the box one, so their middle is that far above the ground (it was half as far, which
		// sank the pillars and the capsules halfway into it)
		var groundOffset = ( ( shape == PrimitiveType.Cylinder ) || ( shape == PrimitiveType.Capsule ) ) ? size : size * 0.5f;

		position.y = ground + groundOffset;

		lifeformObject.transform.SetParent( transform, false );
		lifeformObject.transform.position = position;
		lifeformObject.transform.localScale = Vector3.one * size;
		lifeformObject.name = "Lifeform " + species.m_index + " (" + species.GetBodyPlanText() + ")";

		var lifeform = lifeformObject.AddComponent<TerrainLifeform>();

		lifeform.m_groundOffset = groundOffset;

		lifeform.Initialize( species, this );

		m_lifeformList.Add( lifeform );
	}

	// the lifeform nearest the terrain vehicle, if one is beside it (null otherwise)
	public TerrainLifeform FindLifeformBeside( Vector3 vehiclePosition )
	{
		TerrainLifeform nearest = null;
		var nearestDistance = c_besideRange;

		foreach ( var lifeform in m_lifeformList )
		{
			// nothing in the air: "Airborne creatures may not be dealt with by the player" (planet.txt)
			if ( ( lifeform == null ) || lifeform.IsAirborne() )
			{
				continue;
			}

			var delta = lifeform.transform.position - vehiclePosition;

			delta.y = 0.0f;

			if ( delta.magnitude <= nearestDistance )
			{
				nearest = lifeform;
				nearestDistance = delta.magnitude;
			}
		}

		return nearest;
	}

	// true while a recording is under way
	public bool IsRecording()
	{
		return m_recording;
	}

	// true while the process under way is a capture
	public bool IsCapturing()
	{
		return m_recording && m_capturing;
	}

	// starts capturing this lifeform (no energy is needed) - it is angered
	public void StartCapture( TerrainLifeform lifeform )
	{
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();

		m_recording = true;
		m_capturing = true;
		m_recordingTarget = lifeform;
		m_recordingTimeLeft = c_captureTime;

		lifeform.m_aggression = Mathf.Clamp( lifeform.m_aggression + Random.Range( 1, 21 ), 1, 100 );

		// "If the response is capture then there is a suspenseful noise"
		messages.AddText( "<color=white>Capturing...</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// starts recording this lifeform's data, if the terrain vehicle has the energy for it - returns true if it started
	public bool StartRecording( TerrainLifeform lifeform )
	{
		var terrainVehicle = DataController.m_instance.m_playerData.m_terrainVehicle;
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();

		if ( terrainVehicle.m_fuelRemaining < c_recordingEnergy )
		{
			messages.AddText( "<color=red>There is not enough energy to record the lifeform.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			return false;
		}

		terrainVehicle.UseUpFuel( c_recordingEnergy );

		m_recording = true;
		m_capturing = false;
		m_recordingTarget = lifeform;
		m_recordingTimeLeft = c_recordingTime;

		// "there is a whiring noise while the word "Recording" is presented"
		messages.AddText( "<color=white>Recording...</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		return true;
	}

	// the recording under way: it fails if the lifeform gets away, waits while it attacks, and when it is done the data is Interstel's
	void Update()
	{
		if ( !m_recording || ( SpaceflightController.m_instance == null ) || SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		var playerData = DataController.m_instance.m_playerData;
		var messages = SpaceflightController.m_instance.m_messages;
		var vehicle = SpaceflightController.m_instance.m_terrainVehicle;

		// back in the ship: the recording is abandoned
		if ( ( playerData.m_general.m_location != PD_General.Location.Disembarked ) || ( vehicle == null ) )
		{
			m_recording = false;

			return;
		}

		// gone (eaten) or out of reach: "it got away"
		var gotAway = m_recordingTarget == null;

		if ( !gotAway )
		{
			var delta = m_recordingTarget.transform.position - vehicle.transform.position;

			delta.y = 0.0f;

			gotAway = delta.magnitude > c_getAwayRange;
		}

		if ( gotAway )
		{
			m_recording = false;

			messages.AddText( "<color=yellow>It got away.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			return;
		}

		// an attack delays it
		if ( Time.time - m_recordingTarget.m_lastAttackTime < c_attackDelay )
		{
			return;
		}

		m_recordingTimeLeft -= Time.deltaTime;

		if ( m_recordingTimeLeft > 0.0f )
		{
			return;
		}

		m_recording = false;

		// a capture: the lifeform goes into the terrain vehicle's hold, if there is room for it
		if ( m_capturing )
		{
			FinishCapture( playerData, messages );

			return;
		}

		// the data is worth what is new in it, paid at the Starport
		playerData.m_general.m_unpaidLifeformDataValue += playerData.m_general.LearnLifeform( m_recordingTarget.m_species, playerData.m_general.m_currentPlanetId, m_recordingTarget.m_status == TerrainLifeform.Status.Dead );

		messages.AddText( "<color=white>Data collection completed, sir.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// a capture is done: "The alien lifeform has been captured and put in stasis. Now loading into T.V." and, "If there is enough room in cargo", "The lifeform is
	// stored as item # XX" - with no room it stays where it is (the notes do not say)
	void FinishCapture( PlayerData playerData, Messages messages )
	{
		var lifeform = m_recordingTarget;
		var species = lifeform.m_species;
		var volume = Mathf.Max( 1, species.m_volume );

		messages.AddText( "<color=white>The alien lifeform has been captured and put in stasis. Now loading into T.V.</color>" );

		if ( playerData.m_terrainVehicle.GetRemainingVolume() < volume )
		{
			messages.AddText( "<color=red>There is not enough room for it in the terrain vehicle.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			return;
		}

		var specimen = new PD_Specimen
		{
			m_planetId = playerData.m_general.m_currentPlanetId,
			m_speciesIndex = species.m_index,
			m_name = species.GetBodyPlanText(),
			m_volume = volume,
			m_value = playerData.m_general.LearnLifeform( species, playerData.m_general.m_currentPlanetId, false ) * c_captureValueFactor
		};

		playerData.m_terrainVehicle.AddSpecimen( specimen );

		var terrainVehicle = playerData.m_terrainVehicle;
		var itemNumber = ( ( terrainVehicle.m_elementStorage != null ) && ( terrainVehicle.m_elementStorage.m_elementList != null ) ? terrainVehicle.m_elementStorage.m_elementList.Count : 0 ) + ( ( terrainVehicle.m_artifactStorage != null ) && ( terrainVehicle.m_artifactStorage.m_artifactList != null ) ? terrainVehicle.m_artifactStorage.m_artifactList.Count : 0 ) + terrainVehicle.GetSpecimenStorage().GetCount();

		messages.AddText( "<color=white>The lifeform is stored as item # " + itemNumber + ".</color>" );

		// it is off the surface
		RemoveEaten( lifeform );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// a lifeform has been eaten: it is gone at once (taken out of the list and off the surface, so that neither Scan nor Fire finds it before it is destroyed)
	public void RemoveEaten( TerrainLifeform lifeform )
	{
		m_lifeformList.Remove( lifeform );

		lifeform.gameObject.SetActive( false );
		lifeform.transform.SetParent( null, false );

		Destroy( lifeform.gameObject );
	}
}
