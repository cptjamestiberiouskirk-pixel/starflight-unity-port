
using UnityEngine;

public class InOrbit : MonoBehaviour
{
	// the nebula overlay
	public GameObject m_nebula;

	// the planet model
	public MeshRenderer m_planetModel;

	// the planet cloud
	public MeshRenderer m_clouds;

	// the planet atmosphere
	public GameObject m_planetAtmosphere;

	// current planet spin
	float m_spin;

	// the planet the ship orbits (found when the orbit is shown)
	Planet m_planetController;

	// true from the moment the ship goes into orbit until we have looked for an encounter that waits in orbit around this planet (we look once every time)
	bool m_lookForEncounter;

	// the heat of the Crystal Planet, as the original's (HYPERMSG-OV OSET, WED4B, HEAT and HCASE): a ship that goes into orbit around it without the Crystal Orb
	// vibrates, loses its armor, and then on every tick a random system loses 3 points, the hull is halved at the 70th, 140th and 210th tick and melts at the
	// 280th. A tick is one pass of the original's main loop (its external events), taken as Encounter.c_passSeconds, the pass the port measured at GOG's speed
	const int c_heatSystemLoss = 3;
	const int c_heatHullWarm = 70;
	const int c_heatHullRed = 140;
	const int c_heatHullMelting = 210;
	const int c_heatShipMelts = 280;

	// true while the heat of the Crystal Planet works on the ship (from the moment it starts to vibrate until it leaves orbit)
	bool m_heatIsActive;

	// how many ticks of heat the ship has taken in this orbit, and the time until the next one
	int m_heatTicks;
	float m_heatTimer;

	// true from the moment the ship goes into orbit until the Crystal Planet has been looked for (once every time, when the ship is in orbit and not still coming up)
	bool m_checkCrystalPlanet;

	// true from the moment the ship goes into orbit until the Crystal Cone has had its chance to report the control nexus of the Crystal Planet (once every time)
	bool m_reportNexus;

	// unity awake
	void Awake()
	{
		// the planet takes the look the player picks at once
		PlanetLook.Changed += MaterialUpdated;
	}

	// unity on destroy
	void OnDestroy()
	{
		PlanetLook.Changed -= MaterialUpdated;
	}

	// unity start
	void Start()
	{
	}

	// unity update
	void Update()
	{
		// don't do anything if the game is paused
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// has the ship just gone into orbit? then an encounter that waits in orbit around this planet begins now
		// (not while the launch from the surface is still bringing the ship up - its camera animation and the events of that animation have to finish first)
		if ( m_lookForEncounter && !SpaceflightController.m_instance.m_playerCamera.IsLaunchingOrLanding() )
		{
			// we look only once every time the ship goes into orbit
			m_lookForEncounter = false;

			if ( SpaceflightController.m_instance.BeginInOrbitEncounter() )
			{
				// the encounter location has taken over
				return;
			}
		}

		// the Crystal Cone reports the control nexus of the Crystal Planet once the ship is in orbit around it (not while the launch from the surface is still bringing it up)
		// (not while a Black Egg counts down in this star system: its numbers follow its first line, and the planet is about to go)
		if ( m_reportNexus && !SpaceflightController.m_instance.m_playerCamera.IsLaunchingOrLanding() )
		{
			m_reportNexus = false;

			if ( !BlackEgg.IsArmedEggInStarSystem( DataController.m_instance.m_playerData.m_general.m_currentStarId ) )
			{
				CrystalCone.ReportNexus();
			}
		}

		// around the Crystal Planet the Crystal Orb glows, or the ship begins to vibrate and heat up
		UpdateCrystalPlanetHeat();

		// slowly spin the planet
		m_spin += Time.deltaTime * SpaceflightController.m_instance.m_planetRotationSpeed;

		// wrap the spin around to avoid FP issues
		if ( m_spin >= 360.0f )
		{
			m_spin -= 360.0f;
		}

		// calculate the new rotation quaternion
		var newRotation = Quaternion.Euler( 0.0f, 0.0f, m_spin );

		// apply it to the planet
		m_planetModel.transform.localRotation = newRotation;

		// the clouds of the natural look go round a little faster than the ground
		if ( m_planetController != null )
		{
			m_planetController.SetCloudDrift( m_spin );
		}
	}

	// true while the heat of the Crystal Planet works on the ship: the landing computer does not respond and a distress call is jammed (LAND-OV ?LAND and COMM-OV
	// WEFC3 look for the heat among the external events)
	public bool IsCrystalHeatActive()
	{
		return m_heatIsActive && gameObject.activeInHierarchy;
	}

	// the Crystal Planet generates a powerful magnetic force that only the Crystal Orb negates (STRINFO 5.1): in orbit around it the orb glows, and a ship without
	// it begins to vibrate, its armor gone, and heats up until it melts unless it leaves (HYPERMSG-OV OSET)
	void UpdateCrystalPlanetHeat()
	{
		// nothing while the ship comes up from the surface or goes down to it
		if ( SpaceflightController.m_instance.m_playerCamera.IsLaunchingOrLanding() )
		{
			return;
		}

		// once every time the ship goes into orbit: is this the Crystal Planet?
		if ( m_checkCrystalPlanet )
		{
			m_checkCrystalPlanet = false;

			CheckCrystalPlanet();
		}

		if ( !m_heatIsActive )
		{
			return;
		}

		// a tick of heat every pass of the original's main loop
		m_heatTimer -= Time.deltaTime;

		for ( var tick = 0; ( tick < 8 ) && m_heatIsActive && ( m_heatTimer <= 0.0f ); tick++ )
		{
			m_heatTimer += Encounter.c_passSeconds;

			HeatTick();
		}
	}

	// the ship has gone into orbit: around the Crystal Planet the Crystal Orb glows, or the ship begins to vibrate and its armor is gone (HYPERMSG-OV OSET, WED4B:
	// "CAPTAIN, THE SHIP IS BEGINNING TO VIBRATE!", the ship's ACNT to 0, the heat's count from 0)
	void CheckCrystalPlanet()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		// get the planet we are orbiting (guard against an id that is not in the list)
		var planetId = playerData.m_general.m_currentPlanetId;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) || !gameData.m_planetList[ planetId ].IsCrystalPlanet() )
		{
			return;
		}

		// is the Crystal Orb in the ship's hold?
		var orbId = gameData.FindArtifactId( "Crystal Orb" );
		var artifactStorage = playerData.m_playerShip.m_artifactStorage;
		var shipHasTheOrb = ( artifactStorage != null ) && ( artifactStorage.Find( orbId ) != null );

		var messages = SpaceflightController.m_instance.m_messages;

		if ( shipHasTheOrb )
		{
			messages.AddText( "<color=white>The Crystal Orb is glowing.</color>" );

			return;
		}

		messages.AddText( "<color=white>Captain,</color>" );
		messages.AddText( "<color=red>The ship is beginning to vibrate!</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Alarm );

		playerData.m_playerShip.m_armorPoints = 0;

		m_heatIsActive = true;
		m_heatTicks = 0;
		m_heatTimer = Encounter.c_passSeconds;
	}

	// one tick of the Crystal Planet's heat (HYPERMSG-OV HEAT and HCASE): the hull warns and is halved at the 70th, 140th and 210th tick (WED0B), the ship melts
	// at the 280th (WEE3B: "**SHIP MELTED**", and the game is over), and on every other tick a random system loses 3 points (WED94: one of the six, never below 0)
	void HeatTick()
	{
		var playerShip = DataController.m_instance.m_playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;

		m_heatTicks++;

		switch ( m_heatTicks )
		{
			case c_heatHullWarm:
				messages.AddText( "<color=red>The hull temperature is increasing!</color>" );
				HalveHull( playerShip );
				break;

			case c_heatHullRed:
				messages.AddText( "<color=red>The hull is glowing red!</color>" );
				HalveHull( playerShip );
				break;

			case c_heatHullMelting:
				messages.AddText( "<color=red>The hull is melting!</color>" );
				HalveHull( playerShip );
				break;

			case c_heatShipMelts:
				messages.AddText( "<color=red>The hull is ... AHHHHHH!</color>" );
				ShipMelts();
				break;

			default:
				var system = (PD_PlayerShip.ShipSystem) Random.Range( 0, (int) PD_PlayerShip.ShipSystem.Hull );
				playerShip.SetSystemHealth( system, playerShip.GetSystemHealth( system ) - c_heatSystemLoss );
				break;
		}
	}

	// the hull's integrity is halved (WED0B); a hull that the heat brings down to nothing has melted
	void HalveHull( PD_PlayerShip playerShip )
	{
		SoundController.m_instance.PlaySound( SoundController.Sound.Alarm );

		playerShip.SetSystemHealth( PD_PlayerShip.ShipSystem.Hull, playerShip.GetHullIntegrity() / 2 );

		if ( playerShip.GetHullIntegrity() <= 0 )
		{
			ShipMelts();
		}
	}

	// the ship has melted, and the game is over (WEE3B)
	void ShipMelts()
	{
		m_heatIsActive = false;

		SpaceflightController.m_instance.m_combatController.EndGame( "Ship melted!" );
	}

	// call this to hide the in orbit objects
	public void Hide()
	{
		if ( !gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Hiding the in orbit location." );

		// hide the in orbit location
		gameObject.SetActive( false );

		// the heat of the Crystal Planet ends when the ship leaves orbit (the original puts its external events back, and counts from 0 in the next orbit)
		m_heatIsActive = false;
	}

	// call this to show the in orbit objects
	public void Show()
	{
		if ( gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Showing the in orbit location." );

		// the ship has just gone into orbit - look for an encounter that waits here (in the next update, not in the middle of this switch of location)
		m_lookForEncounter = true;

		// look for the Crystal Planet once the ship is in orbit (the heat of an earlier orbit is over)
		m_checkCrystalPlanet = true;
		m_heatIsActive = false;

		// and the Crystal Cone reports the nexus
		m_reportNexus = true;

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the star data
		var star = gameData.m_starList[ playerData.m_general.m_currentStarId ];

		// show the in orbit objects
		gameObject.SetActive( true );

		// get the planet controller
		var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

		m_planetController = planetController;

		// set the scale of the planet model and atmosphere
		var scale = planetController.m_planet.GetScale();
		m_planetModel.transform.localScale = scale * 3.0f;
		m_planetAtmosphere.transform.localScale = m_planetModel.transform.localScale * 1.055f;

		// position the planet and atmosphere
		var position = Vector3.up * -m_planetModel.transform.localScale.y;
		m_planetModel.transform.localPosition = position;
		m_planetAtmosphere.transform.localPosition = position * 0.85f;

		// move the player object
		SpaceflightController.m_instance.m_playerShip.transform.position = playerData.m_general.m_coordinates = Vector3.zero;

		// freeze the player
		SpaceflightController.m_instance.m_playerShip.Freeze();

		// are we in the middle of the launching animation?
		if ( !SpaceflightController.m_instance.m_playerCamera.IsCurrentlyPlaying( "Launching (Planetside)" ) )
		{
			// no - play the in orbit animation
			SpaceflightController.m_instance.m_playerCamera.StartAnimation( "In Space" );

			// reset the buttons
			SpaceflightController.m_instance.m_buttonController.SetBridgeButtons();
		}

		// fade in the map
		SpaceflightController.m_instance.m_viewport.StartFade( 1.0f, 2.0f );

		// show / hide the nebula depending on if we are in one
		m_nebula.SetActive( star.m_insideNebula );

		// play the docking bay music track
		MusicController.m_instance.ChangeToTrack( MusicController.Track.InOrbit );

		// let the player know we've established orbit, under the lines that led up to it, in the original's words (starflt_143: the lines scroll on from
		// "...COMMENCING SYSTEM TRAVEL" to "...ORBIT ESTABLISHED")
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>...Orbit established.</color>" );

		// say so if this is a planet whose maps could not be generated (that is why it is a plain grey ball, and why we can't land on it)
		if ( planetController.CouldNotBeMapped() )
		{
			SpaceflightController.m_instance.m_messages.AddText( Planet.c_couldNotBeMappedMessage );
		}

		// set up the clouds and atmosphere
		planetController.SetupClouds( m_clouds, m_planetAtmosphere, true, false, m_planetModel.gameObject );

		// make sure skybox blend is off
		StarflightSkybox.m_instance.m_currentBlendFactor = 0.0f;

		// turn skybox autorotate off
		StarflightSkybox.m_instance.m_autorotateSkybox = false;

		// make sure skybox rotation is reset
		StarflightSkybox.m_instance.m_currentRotation = Quaternion.identity;

		// apply the material to the planet model
		MaterialUpdated();
	}

	// puts the planet's look on the planet model, its clouds and its atmosphere (when its maps are ready, and when the player picks another look): its
	// natural look, with maps made as detailed as orbit needs, or its original material
	public void MaterialUpdated()
	{
		if ( gameObject.activeInHierarchy )
		{
			// get to the player data
			var playerData = DataController.m_instance.m_playerData;

			// get the planet controller
			var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

			if ( planetController == null )
			{
				return;
			}

			// apply the material to the planet model
			planetController.ApplyOrbitLook( m_planetModel, m_clouds, m_planetAtmosphere );
			planetController.SetCloudDrift( m_spin );
		}
	}
}
