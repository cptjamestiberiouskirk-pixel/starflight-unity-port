using System.Collections.Generic;
using UnityEngine;

// places the lifeforms of a planet as the shipped game does (roadmap 8.18, SEED-OV POPULA): the surface is cut into blocks of 18 by 30 of the original's cells
// (RandomRuinRegions' regions), and a block's lifeforms are placed when the terrain vehicle comes near it - the species of the block's region (its
// temperature band by latitude, GD_Species.GetLife), each a random number of times up to the region's individuals per block, each at a random cell of the
// block on low dry ground (the original's contour heights 0x21 to 0x3f), never on liquid, mud or lava, and the last species record never (an original quirk)
// - only the blocks around the vehicle hold lifeforms, and those it leaves behind are taken away (decided 2026-10-19); the numbers are the kernel's, with a
// seed per block, so a block holds the same lifeforms every time and the game's random numbers do not move
public class TerrainLifeforms : TerrainGridPopulator
{
	// the blocks whose lifeforms are placed: those this near the vehicle's (the 3 by 3 about it), and they are taken away beyond one more
	const int c_blockPlaceDistance = 1;
	const int c_blockKeepDistance = 2;

	// how often the blocks about the vehicle are looked at, in seconds
	const float c_blockCheckSeconds = 0.5f;

	// the low ground lifeforms are placed on: the original's contour above 0x20 (its sea level: the port's water elevation, as for the ruins) and below 0x40,
	// out of land heights up to 0x7f - the lowest third of the heights between the water and the planet's highest point (the port's reading of the scale)
	const float c_lowGroundShare = 0x1f / (float) 0x5f;

	// a lifeform's place index is its block times this, plus the number of the attempt that placed it in the block (always fewer than this: a block's species
	// times its individuals per block stay under 256)
	const int c_placesPerBlock = 256;

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

	// a capture (planet.txt): "Capturing takes about 6 seconds" and "Capturing does not require energy"; the specimen holds the lifeform's value (the shipped game's
	// ITEMS-OV WE75B: GD_Species.GetValue)
	const float c_captureTime = 6.0f;

	// the species recorded and captured on this landing (the shipped game keeps it in each species' record, biod, which VITA-OV makes again on every landing): a
	// species already recorded gives no more data ("LIFEFORM ALREADY RECORDED", WE6B8) and one already captured no more specimens ("DUPLICATE SPECIMEN", WE75B)
	// - not saved
	static readonly HashSet<int> s_recordedSpecies = new HashSet<int>();
	static readonly HashSet<int> s_capturedSpecies = new HashSet<int>();
	static int s_landingPlanetId = -1;

	// call this when the ship lands (PlayerCamera.PlayerHasLanded): a new landing has new records
	public static void NewLanding()
	{
		s_recordedSpecies.Clear();
		s_capturedSpecies.Clear();
		s_landingPlanetId = -1;
	}

	// true if this species has been recorded, or captured, on this landing
	public static bool WasRecorded( GD_Species species )
	{
		return ( species != null ) && s_recordedSpecies.Contains( species.m_index );
	}

	public static bool WasCaptured( GD_Species species )
	{
		return ( species != null ) && s_capturedSpecies.Contains( species.m_index );
	}

	// the capture beam that holds the lifeform while a capture runs (since 2026-10-17, TVCaptureButton): pulses of the combat lasers' beam, drawn small and pale
	// blue, renewed before each fades
	const float c_capturePulseSeconds = 0.15f;
	const float c_capturePulseLifetime = 0.25f;
	const float c_captureBeamScale = 0.12f;
	const float c_vehicleBeamHeight = 3.0f;
	const float c_lifeformBeamHeight = 1.0f;
	static readonly Color c_captureBeamColor = new Color( 0.45f, 0.85f, 1.0f, 1.0f );

	LaserBeam m_captureBeam;
	float m_capturePulseTimer;

	// the lifeform Cargo offered (the console shows Record and Ignore for it)
	public TerrainLifeform m_offeredLifeform;

	// the recording under way
	bool m_recording;
	bool m_capturing;
	TerrainLifeform m_recordingTarget;
	float m_recordingTimeLeft;

	// the lifeforms placed (for the pairing of mates: lf-behav.txt, "Pair all un-paired lifeforms in the encounter area")
	public readonly List<TerrainLifeform> m_lifeformList = new List<TerrainLifeform>();

	// the planet the lifeforms are placed on, what the generation of its lifeforms gave, its species, and the lifeforms placed in each block
	int m_planetId = -1;
	GD_Planet m_planet;
	ShippedLifeforms.Result m_life;
	List<GD_Species> m_speciesList;
	readonly Dictionary<int, List<TerrainLifeform>> m_blockLifeforms = new Dictionary<int, List<TerrainLifeform>>();
	float m_blockTimer;

	// place the lifeforms of this planet about the place the terrain vehicle goes out at (the random seed of the other populators is not needed: each block
	// has a generator of its own)
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
		m_elevationScale = elevationScale;

		// remove the lifeforms of the last planet
		Tools.DestroyChildrenOf( gameObject );

		m_lifeformList.Clear();
		m_blockLifeforms.Clear();

		m_planet = planetGenerator.GetPlanet();
		m_planetId = m_planet.m_id;

		// (the records of another planet are not this one's: a game loaded on another planet, say)
		if ( s_landingPlanetId != m_planetId )
		{
			s_recordedSpecies.Clear();
			s_capturedSpecies.Clear();
			s_landingPlanetId = m_planetId;
		}
		m_speciesList = GD_Species.ForPlanet( m_planet );
		m_life = GD_Species.GetLife( m_planet );

		// the weapons change a species' aggression for the rest of a run (TerrainLifeform.Behave), as the original's for the rest of a landing: its record's is
		// put back (VITA-OV makes the records again on each landing)
		foreach ( var species in m_speciesList )
		{
			if ( species.m_record != null )
			{
				species.m_aggression = Mathf.Clamp( species.m_record.m_aggression, 1, 100 );
			}
		}
		m_blockTimer = c_blockCheckSeconds;

		PlaceBlocksAround( DataController.m_instance.m_playerData.m_general.m_lastDisembarkedCoordinates );
	}

	// the blocks within c_blockPlaceDistance of this place get their lifeforms, and those beyond c_blockKeepDistance lose theirs - a block's lifeforms come back
	// the same when the vehicle comes back (but for those killed, captured or eaten, PD_PlanetSurfaces.LoseLifeform)
	void PlaceBlocksAround( Vector3 worldPosition )
	{
		var planetGenerator = GetPlanetGenerator();

		if ( ( m_speciesList == null ) || ( m_speciesList.Count == 0 ) || ( m_life == null ) || ( planetGenerator == null ) || ( planetGenerator.m_elevation == null ) )
		{
			return;
		}

		Tools.WorldToMapCoordinates( worldPosition, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

		var centre = RandomRuinRegions.GetRegion( mapX, mapY, planetGenerator );
		var kept = new HashSet<int>( RandomRuinRegions.GetRegionsAround( centre, c_blockKeepDistance ) );

		// the blocks too far away lose their lifeforms
		var gone = new List<int>();

		foreach ( var block in m_blockLifeforms.Keys )
		{
			if ( !kept.Contains( block ) )
			{
				gone.Add( block );
			}
		}

		foreach ( var block in gone )
		{
			RemoveBlock( block );
		}

		// the blocks near enough get theirs (each once)
		foreach ( var block in RandomRuinRegions.GetRegionsAround( centre, c_blockPlaceDistance ) )
		{
			if ( !m_blockLifeforms.ContainsKey( block ) )
			{
				PlaceBlock( block );
			}
		}
	}

	// places the lifeforms of one block (SEED-OV WF079, WEE91 and WEE4D)
	void PlaceBlock( int block )
	{
		var planetGenerator = GetPlanetGenerator();
		var placed = new List<TerrainLifeform>();

		m_blockLifeforms[ block ] = placed;

		var column = block % RandomRuinRegions.c_columns;
		var row = block / RandomRuinRegions.c_columns;
		var left = column * RandomRuinRegions.c_regionWidth;
		var top = row * RandomRuinRegions.c_regionHeight;

		// the block's region (the original takes the vehicle's row as it comes into the block; the port, which places a block before the vehicle reaches it,
		// the block's middle row)
		var region = m_life.GetRegionAt( top + RandomRuinRegions.c_regionHeight / 2 );
		var most = m_life.m_individualsPerBlock[ region ];
		var random = new ShippedLifeforms.Generator( m_life.GetBlockSeed( column, row ) );
		var water = planetGenerator.m_waterElevation;
		var highest = water + ( planetGenerator.m_maximumElevation - water ) * c_lowGroundShare;
		var attempt = 0;

		// the species of the region, from the highest record down (WEE91 stacks them, WF079 takes them off the stack), each a number of times from 0 to one less
		// than the individuals per block
		for ( var index = Mathf.Min( m_life.GetPlacedSpeciesCount(), m_speciesList.Count ) - 1; index >= 0; index-- )
		{
			if ( m_life.m_species[ index ].m_region != region )
			{
				continue;
			}

			var count = random.Range( 0, most );

			for ( var i = 0; i < count; i++ )
			{
				// a cell of the block (WECDD and WECFB reach one cell past it across and down)
				var x = random.Range( left, left + RandomRuinRegions.c_regionWidth + 1 );
				var y = random.Range( top, top + RandomRuinRegions.c_regionHeight + 1 );
				var placeIndex = block * c_placesPerBlock + Mathf.Min( attempt, c_placesPerBlock - 1 );

				attempt++;

				RandomRuinRegions.ToMap( Mathf.Repeat( x + 0.5f, RandomRuinRegions.c_surfaceWidth ), Mathf.Min( y + 0.5f, RandomRuinRegions.c_surfaceHeight - 0.5f ), planetGenerator, out var mapX, out var mapY );

				var elevation = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY );

				// only on low dry ground (WED79 and WED8B): an attempt anywhere else places nothing
				if ( ( elevation <= water ) || ( elevation >= highest ) )
				{
					continue;
				}

				placed.Add( PlaceLifeform( m_speciesList[ index ], mapX, mapY, m_elevationScale, placeIndex ) );
			}
		}

		ApplyLostLifeforms( placed );
	}

	// takes the lifeforms of one block off the surface
	void RemoveBlock( int block )
	{
		if ( m_blockLifeforms.TryGetValue( block, out var placed ) )
		{
			foreach ( var lifeform in placed )
			{
				// (one eaten or captured is gone already: a destroyed object compares equal to null)
				if ( lifeform != null )
				{
					RemoveFromSurface( lifeform );
				}
			}
		}

		m_blockLifeforms.Remove( block );
	}

	// the lifeforms killed, captured or eaten on earlier runs of the terrain vehicle in this orbit stay so until the ship leaves it, as the original's stay in
	// the regions of the planet until DEPART deletes them (DPART-OV WF166): a dead one lies dead, a captured or eaten one is gone
	void ApplyLostLifeforms( List<TerrainLifeform> placed )
	{
		var planetSurfaces = DataController.m_instance.m_playerData.m_planetSurfaces;

		if ( planetSurfaces == null )
		{
			return;
		}

		foreach ( var lifeform in placed.ToArray() )
		{
			var lostLifeform = planetSurfaces.FindLostLifeform( m_planetId, lifeform.m_placeIndex );

			if ( lostLifeform == null )
			{
				continue;
			}

			if ( lostLifeform.m_isDead )
			{
				lifeform.LieDead();
			}
			else
			{
				placed.Remove( lifeform );

				RemoveFromSurface( lifeform );
			}
		}
	}

	// call this when a lifeform dies
	public void LifeformDied( TerrainLifeform lifeform )
	{
		var planetSurfaces = DataController.m_instance.m_playerData.m_planetSurfaces;

		if ( planetSurfaces != null )
		{
			planetSurfaces.LoseLifeform( m_planetId, lifeform.m_placeIndex, true );
		}
	}

	// the blocks follow the terrain vehicle
	void UpdateBlocks()
	{
		var controller = SpaceflightController.m_instance;

		if ( ( m_planet == null ) || ( controller == null ) || controller.m_gameIsPaused || ( controller.m_terrainVehicle == null ) || ( DataController.m_instance.m_playerData.m_general.m_location != PD_General.Location.Disembarked ) )
		{
			return;
		}

		m_blockTimer -= Time.deltaTime;

		if ( m_blockTimer > 0.0f )
		{
			return;
		}

		m_blockTimer = c_blockCheckSeconds;

		PlaceBlocksAround( controller.m_terrainVehicle.transform.position );
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

	// the name of a species' model: its body plan, and for a leaved plant one of the two models of its size (A or B as its icon is even or odd: the shipped
	// game's set.icon draws the pair's icon with a coin)
	public static string GetModelName( GD_Species species, int planetId )
	{
		var name = species.m_bodyPlan.ToString();

		if ( ( species.m_bodyPlan >= GD_Species.BodyPlan.LeavedSmall ) && ( species.m_bodyPlan <= GD_Species.BodyPlan.LeavedLarge ) )
		{
			var icon = ( species.m_record != null ) ? species.m_record.m_icon : planetId + species.m_index;

			name += ( ( icon % 2 ) == 0 ) ? "A" : "B";
		}

		return name;
	}

	// a lifeform at this place: the model of its body plan, tinted with its species' colour (TerrainLifeform), as large as its size - the models stand on their
	// base and face along +Z, and each one is turned by its place on the map (no random number is drawn for it); without a model a plain shape stands in for
	// it (a pillar for a plant, a ball for a radial animal, a box for an amorphous one, and a capsule for a bilateral one)
	TerrainLifeform PlaceLifeform( GD_Species species, float mapX, float mapY, float elevationScale, int placeIndex )
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
			modelLifeform.m_placeIndex = placeIndex;
			modelLifeform.Initialize( species, this );

			m_lifeformList.Add( modelLifeform );

			return modelLifeform;
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
		lifeform.m_placeIndex = placeIndex;

		lifeform.Initialize( species, this );

		m_lifeformList.Add( lifeform );

		return lifeform;
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

	// starts capturing this lifeform (no energy is needed)
	public void StartCapture( TerrainLifeform lifeform )
	{
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();

		m_recording = true;
		m_capturing = true;
		m_recordingTarget = lifeform;
		m_recordingTimeLeft = c_captureTime;

		// "If the response is capture then there is a suspenseful noise": the capture beam's hum, for as long as the beam holds the lifeform
		messages.AddText( "<color=white>Capturing...</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.CaptureBeam, c_captureBeamVolume, 1.0f, true );
	}

	// how loud the capture beam's hum plays
	const float c_captureBeamVolume = 0.7f;

	// the capture beam's hum stops (the capture has ended, one way or another)
	void StopCaptureBeam()
	{
		if ( m_capturing && ( SoundController.m_instance != null ) )
		{
			SoundController.m_instance.StopSound( SoundController.Sound.CaptureBeam );
		}
	}

	// starts recording this lifeform's data, if the terrain vehicle has the energy for it - returns true if it started
	public bool StartRecording( TerrainLifeform lifeform )
	{
		var terrainVehicle = DataController.m_instance.m_playerData.m_terrainVehicle;
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();

		if ( WasRecorded( lifeform.m_species ) )
		{
			messages.AddText( "<color=yellow>Lifeform already recorded.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			return false;
		}

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

	// the blocks about the terrain vehicle, and the recording under way: it fails if the lifeform gets away, waits while it attacks, and when it is done the data is Interstel's
	void Update()
	{
		UpdateBlocks();

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

			StopCaptureBeam();

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

			StopCaptureBeam();

			messages.AddText( "<color=yellow>It got away.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			return;
		}

		// the capture beam holds it while a capture runs
		if ( m_capturing )
		{
			PulseCaptureBeam( vehicle.transform.position );
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
			StopCaptureBeam();

			FinishCapture( playerData, messages );

			return;
		}

		// the data goes into the terrain vehicle's hold, worth an eighth of the lifeform's value (a tenth of that for a dead one), and taking no room (the shipped
		// game's BIO-DATA: ITEMS-OV WE6B8 and WE67C) - it is sold at the Trade Depot, like a specimen
		var species = m_recordingTarget.m_species;
		var data = MakeSpecimen( playerData, species, true, 0, GetLifeformValue( playerData, species, m_recordingTarget.m_status == TerrainLifeform.Status.Dead ) / 8 );

		s_recordedSpecies.Add( species.m_index );

		playerData.m_terrainVehicle.AddSpecimen( data );

		messages.AddText( "<color=white>Lifeform recorded.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// a pulse of the capture beam from the terrain vehicle to the lifeform being captured
	void PulseCaptureBeam( Vector3 vehiclePosition )
	{
		m_capturePulseTimer -= Time.deltaTime;

		if ( m_capturePulseTimer > 0.0f )
		{
			return;
		}

		m_capturePulseTimer = c_capturePulseSeconds;

		if ( m_captureBeam == null )
		{
			var beamObject = new GameObject( "Capture Beam" );

			beamObject.transform.SetParent( transform, false );

			m_captureBeam = beamObject.AddComponent<LaserBeam>();
			m_captureBeam.SetScale( c_captureBeamScale );
			m_captureBeam.SetColor( c_captureBeamColor );
		}

		m_captureBeam.Fire( vehiclePosition + Vector3.up * c_vehicleBeamHeight, m_recordingTarget.transform.position + Vector3.up * c_lifeformBeamHeight, c_capturePulseLifetime );
	}

	// a capture is done: "The alien lifeform has been captured and put in stasis. Now loading into T.V." and, "If there is enough room in cargo", "The lifeform is
	// stored as item # XX" - with no room it stays where it is (the notes do not say)
	void FinishCapture( PlayerData playerData, Messages messages )
	{
		var lifeform = m_recordingTarget;
		var species = lifeform.m_species;
		var volume = Mathf.Max( 1, species.m_volume );

		// (one specimen of a species a landing)
		if ( WasCaptured( species ) )
		{
			messages.AddText( "<color=yellow>Duplicate specimen.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			return;
		}

		messages.AddText( "<color=white>The alien lifeform has been captured and put in stasis. Now loading into T.V.</color>" );

		if ( playerData.m_terrainVehicle.GetRemainingVolume() < volume )
		{
			messages.AddText( "<color=red>There is not enough room for it in the terrain vehicle.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			return;
		}

		var specimen = MakeSpecimen( playerData, species, false, volume, GetLifeformValue( playerData, species, false ) );

		s_capturedSpecies.Add( species.m_index );

		playerData.m_terrainVehicle.AddSpecimen( specimen );

		var terrainVehicle = playerData.m_terrainVehicle;
		var itemNumber = ( ( terrainVehicle.m_elementStorage != null ) && ( terrainVehicle.m_elementStorage.m_elementList != null ) ? terrainVehicle.m_elementStorage.m_elementList.Count : 0 ) + ( ( terrainVehicle.m_artifactStorage != null ) && ( terrainVehicle.m_artifactStorage.m_artifactList != null ) ? terrainVehicle.m_artifactStorage.m_artifactList.Count : 0 ) + terrainVehicle.GetSpecimenStorage().GetCount();

		messages.AddText( "<color=white>The lifeform is stored as item # " + itemNumber + ".</color>" );

		// it is off the surface
		RemoveEaten( lifeform );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// a lifeform's value (GD_Species.GetValue) on the planet the ship is on
	static int GetLifeformValue( PlayerData playerData, GD_Species species, bool dead )
	{
		var gameData = DataController.m_instance.m_gameData;
		var starId = playerData.m_general.m_currentStarId;
		var star = ( ( starId >= 0 ) && ( starId < gameData.m_starList.Length ) ) ? gameData.m_starList[ starId ] : null;

		return species.GetValue( star, dead );
	}

	// a specimen, or a recording of a lifeform's data, of this species and value, with where and when it was taken (the terrain vehicle's latitude and longitude,
	// as the coordinates above the main view read them, and the stardate)
	static PD_Specimen MakeSpecimen( PlayerData playerData, GD_Species species, bool isBioData, int volume, int value )
	{
		var specimen = new PD_Specimen
		{
			m_planetId = playerData.m_general.m_currentPlanetId,
			m_speciesIndex = species.m_index,
			m_name = ( isBioData ? "Data: " : "" ) + species.GetBodyPlanText(),
			m_isBioData = isBioData,
			m_volume = volume,
			m_value = value
		};

		Tools.WorldToLatLongCoordinates( playerData.m_general.m_lastDisembarkedCoordinates, out var eastWest, out var northSouth );

		specimen.m_hasCapturePlace = true;
		specimen.m_capturedNorthSouth = Mathf.RoundToInt( northSouth );
		specimen.m_capturedEastWest = Mathf.RoundToInt( eastWest );
		specimen.m_capturedStardateYMD = playerData.m_general.m_currentStardateYMD;

		return specimen;
	}

	// a lifeform has been eaten (or captured): it is gone at once (taken out of the list and off the surface, so that neither Scan nor Fire finds it before it is
	// destroyed), and stays gone on later runs of the terrain vehicle until the ship leaves the orbit
	public void RemoveEaten( TerrainLifeform lifeform )
	{
		var planetSurfaces = DataController.m_instance.m_playerData.m_planetSurfaces;

		if ( planetSurfaces != null )
		{
			planetSurfaces.LoseLifeform( m_planetId, lifeform.m_placeIndex, false );
		}

		RemoveFromSurface( lifeform );
	}

	// takes a lifeform out of the list and off the surface
	void RemoveFromSurface( TerrainLifeform lifeform )
	{
		m_lifeformList.Remove( lifeform );

		lifeform.gameObject.SetActive( false );
		lifeform.transform.SetParent( null, false );

		Destroy( lifeform.gameObject );
	}
}
