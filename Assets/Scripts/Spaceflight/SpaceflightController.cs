
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class SpaceflightController : MonoBehaviour
{
	// the different locations
	public DockingBay m_dockingBay;
	public StarSystem m_starSystem;
	public InOrbit m_inOrbit;
	public Planetside m_planetside;
	public Disembarked m_disembarked;
	public Hyperspace m_hyperspace;
	public Encounter m_encounter;

	// controllers
	public ButtonController m_buttonController;
	public DisplayController m_displayController;

	// components
	public PlayerShip m_playerShip;
	public PlayerCamera m_playerCamera;
	public Viewport m_viewport;
	public Countdown m_countdown;
	public Radar m_radar;
	public Scanner m_scanner;
	public Starmap m_starmap;
	public Messages m_messages;
	public TerrainVehicle m_terrainVehicle;
	public ShipsLog m_shipsLog;
	public CombatController m_combatController;

	// the Black Egg that has been dropped on a planet of this star system, if any
	readonly BlackEgg m_blackEgg = new BlackEgg();

	// the flares of the stars
	readonly StellarFlares m_stellarFlares = new StellarFlares();

	// some settings
	public float m_alienHyperspaceRadarDistance;
	public float m_alienStarSystemRadarDistance;
	public float m_encounterRange;
	public float m_planetRotationSpeed;
	public float m_starportRotationSpeedMultiplier;

	// true if the game is paused
	public bool m_gameIsPaused;

	// true if the game is over (player destroyed)
	public bool m_gameOver;

	// save game timer
	float m_timer;

	// remember whether or not we have faded in the scene already
	bool m_alreadyFadedIn;

	// the encounters in orbit that have let the ship into orbit (a veloxi drone that was answered correctly) - this lasts until the ship leaves the star system, and is not saved
	readonly System.Collections.Generic.List<int> m_orbitPermissions = new System.Collections.Generic.List<int>();

	// static instance to this spaceflight controller
	public static SpaceflightController m_instance;

	// constructor
	SpaceflightController()
	{
		// make me accessible to everyone
		m_instance = this;
	}

	// unity awake
	void Awake()
	{
		// check if we loaded the persistent scene
		if ( DataController.m_instance == null )
		{
			// nope - so then do it now and tell it to skip the intro scene
			DataController.m_sceneToLoad = "Spaceflight";

			// debug info
			Debug.Log( "Loading scene Persistent" );

			// load the persistent scene
			SceneManager.LoadScene( "Persistent" );
		}
	}

	// unity on destroy
	void OnDestroy()
	{
		// let go of this controller when its scene is unloaded, unless the controller of a new spaceflight scene has already taken over.
		// The static kept the controller, and through it every object of the scene and the maps of its planets, in memory in the other scenes
		if ( ReferenceEquals( m_instance, this ) )
		{
			m_instance = null;
		}

		// the planet the terrain vehicle was last on is held by a static as well
		TerrainGridPopulator.ForgetPlanet();
	}

	// unity start
	void Start()
	{
		// initialize some static stuff
		PG_AlbedoMap.Initialize();
		PG_Craters.Initialize();

		// hide everything
		HideEverything();

		// turn off controller navigation of the UI
		EventSystem.current.sendNavigationEvents = false;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// show the player in case we had it hidden in the editor
		m_playerShip.Show();

		// reset the buttons to default
		m_buttonController.SetBridgeButtons();

		// switch to the current location
		SwitchLocation( playerData.m_general.m_location );

		// make sure the scene is blacked out
		SceneFadeController.m_instance.BlackOut();

		// reset the save game timer
		m_timer = 0.0f;

		// connect the persistent ui canvas to the main camera
		var persistentUI = GameObject.FindWithTag( "Persistent UI" );
		var uiCamera = GameObject.FindWithTag( "UI Camera" );
		var canvas = persistentUI.GetComponent<Canvas>();
		var camera = uiCamera.GetComponent<Camera>();
		canvas.worldCamera = camera;
		canvas.planeDistance = 15.0f;
	}

	// unity update
	void Update()
	{
		// Debug.Log( "Update()" );

		// are we generating planets?
		if ( m_starSystem.GeneratingPlanets() )
		{
			// continue generating planets
			var totalProgress = m_starSystem.GeneratePlanets();

			// show / update the pop up dialog
			PopupController.m_instance.ShowPopup( "Commencing System Penetration", totalProgress );
		}
		else
		{
			// hide the pop up dialog
			PopupController.m_instance.HidePopup();

			// fade in the scene if we haven't already
			if ( !m_alreadyFadedIn )
			{
				// fade in the scene
				SceneFadeController.m_instance.FadeIn();

				// remember that we have faded in the scene
				m_alreadyFadedIn = true;
			}
		}

		// handle ESC key for game over (must check BEFORE the pause return!)
		if ( Input.GetKeyDown( KeyCode.Escape ) && m_gameOver )
		{
			RestartGame();
			return;
		}

		// don't do anything if the game is paused
		if ( m_gameIsPaused )
		{
			return;
		}

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// the game time passes wherever the ship is out in space: in the star system, in hyperspace, in orbit, on a planet's surface and in an encounter. In the original
		// the clock, the repairs and the crew's treatment run in one list of tasks behind one switch (disys.txt, PARALLEL-TASKS), and the doctor treats the crew on a
		// planet's surface too (the manual, page 7). This is also what makes raised shields cost their fuel every star hour during a fight. Not in the docking bay, which
		// is part of the starport
		if ( ( playerData.m_general.m_location != PD_General.Location.DockingBay ) && ( playerData.m_general.m_location != PD_General.Location.Starport ) )
		{
			// update the game time
			playerData.m_general.UpdateGameTime( Time.deltaTime );
		}

		// the shields slowly regain their charge
		playerData.m_playerShip.UpdateShields( Time.deltaTime );

		// a nebula keeps the shields down: shields that are up when the ship is in one collapse (the design notes have nebulae act on the shields, priority.txt;
		// what they do is the owner's ruling of 2026-10-10 for roadmap 1.9)
		if ( playerData.m_playerShip.m_shieldsAreUp && ShipIsInsideANebula() )
		{
			playerData.m_playerShip.DropShields();

			m_messages.AddText( "<color=white>The nebula has collapsed the shields.</color>" );
		}

		// the engineer and the doctor carry on with the repairs and the treatment they were told to do
		playerData.m_playerShip.UpdateRepairs( Time.deltaTime );
		playerData.m_crewAssignment.UpdateTreatment( Time.deltaTime );

		// a Black Egg that has been dropped on a planet counts down once the ship is back up above it
		m_blackEgg.Update();

		// the stars flare on their days (Arth's destroys the Starport), unless the game has been won by then
		m_stellarFlares.Update();

		// save the game once in a while
		m_timer += Time.deltaTime;

		if ( m_timer >= 30.0f )
		{
			m_timer -= 30.0f;

			// DataController.m_instance.SaveActiveGame();
		}

		// when player hits cancel (esc) show the save game panel (except when using the starmap or the ships log)
		if ( !m_starmap.IsOpen() && !m_shipsLog.IsOpen() )
		{
			if ( InputController.m_instance.m_cancel )
			{
				InputController.m_instance.Debounce();

				// not while the ship is launching or landing: the camera animation and its events carry on behind the panel, so the player would miss
				// the rest of it. And not while the ship is exploding: there is nothing left to save, and the game over screen is on its way
				if ( !m_playerCamera.IsLaunchingOrLanding() && !PlayerShipIsDestroyed() )
				{
					PanelController.m_instance.m_saveGamePanel.SetCallbackObject( this );

					PanelController.m_instance.Open( PanelController.m_instance.m_saveGamePanel );

					m_gameIsPaused = true;
				}
			}
		}

#if UNITY_EDITOR
		// DEBUG: Press F9 to spawn a test encounter (Spemin - weak enemy)
		if ( Input.GetKeyDown( KeyCode.F9 ) )
		{
			SpawnTestEncounter();
		}

		// DEBUG: Press F10 to instantly destroy player ship (test game over)
		if ( Input.GetKeyDown( KeyCode.F10 ) )
		{
			playerData.m_playerShip.m_shieldPoints = 0;
			playerData.m_playerShip.m_armorPoints = 0;
			var combatController = ( m_combatController != null ) ? m_combatController : CombatController.m_instance;
			if ( combatController != null )
			{
				combatController.ApplyDamageToPlayer( 1, Vector3.forward );
			}
			else
			{
				m_messages.AddText( "<color=#FF0000>DEBUG: Combat controller not available.</color>" );
			}
		}
#endif
	}

	// call this to hide everything
	void HideEverything()
	{
		// hide all of the locations
		m_dockingBay.Hide();
		m_starSystem.Hide();
		m_inOrbit.Hide();
		m_planetside.Hide();
		m_disembarked.Hide();
		m_hyperspace.Hide();
		m_encounter.Hide();

		// hide various components
		m_playerShip.Hide();
		m_countdown.Hide();
		m_radar.Hide();
		m_scanner.Hide();
		m_starmap.Hide();
		m_shipsLog.Hide();
	}

	// call this to switch to the correct location
	public void SwitchLocation( PD_General.Location newLocation )
	{
		// switch to the correct mode
		Debug.Log( "Switching to location " + newLocation );

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// are we switching to a new location?
		bool locationIsDifferent = false;

		if ( playerData.m_general.m_location != newLocation )
		{
			// yes - remember the last location
			playerData.m_general.m_lastLocation = playerData.m_general.m_location;

			// update the player location
			playerData.m_general.m_location = newLocation;

			// remember that the new location is different
			locationIsDifferent = true;
		}

		// leaving the star system ends every permission to orbit that was granted in it
		if ( newLocation == PD_General.Location.Hyperspace )
		{
			m_orbitPermissions.Clear();
		}

		// make sure the display is updated (in case we are loading from a save game)
		m_messages.Refresh();

		// stop all looping sounds
		SoundController.m_instance.StopAllLoopingSounds();

		// switching to starport is a special case
		if ( playerData.m_general.m_location == PD_General.Location.Starport )
		{
			// force shields to lower and weapons to disarm
			playerData.m_playerShip.DropShields();
			playerData.m_playerShip.DisarmWeapons();

			// starport recharges the shields while the ship is docked
			playerData.m_playerShip.RechargeShieldsFully();

			// start fading out the spaceflight scene
			SceneFadeController.m_instance.FadeOut( "Starport" );
		}
		else
		{
			// hide everything
			HideEverything();

			// make sure the map is visible
			m_viewport.StartFade( 1.0f, 2.0f );

			// switch the location
			switch ( playerData.m_general.m_location )
			{
				case PD_General.Location.DockingBay:
					// the ship is still docked, so the shields leave with a full charge (and with the charge of any shielding that was just installed)
					playerData.m_playerShip.RechargeShieldsFully();
					m_dockingBay.Show();
					break;

				case PD_General.Location.JustLaunched:
					// show the star system (starfield background) but hide the radar
					m_starSystem.Initialize();
					m_starSystem.Show();
					m_playerShip.Show();
					m_radar.Hide(); // hide the radar for JustLaunched - player hasn't entered the system yet
					m_messages.Clear();
					m_messages.AddText( "<color=white>Starport clear.</color>" );
					m_messages.AddText( "<color=#FFFF00>Select Navigation → Maneuver to enter the star system.</color>" );
					break;

				case PD_General.Location.StarSystem:
					m_starSystem.Initialize();
					m_starSystem.Show();
					m_playerShip.Show();
					break;

				case PD_General.Location.InOrbit:
					m_starSystem.Initialize();
					m_inOrbit.Show();
					break;

				case PD_General.Location.Planetside:
					m_starSystem.Initialize();
					m_planetside.Show();
					break;

				case PD_General.Location.Disembarked:
					m_starSystem.Initialize();
					m_disembarked.Show();
					break;

				case PD_General.Location.Hyperspace:
					m_hyperspace.Show();
					m_playerShip.Show();
					break;

				case PD_General.Location.Encounter:
					m_encounter.Show();
					m_playerShip.Show();
					break;
			}
		}

		// did we switch to a new location?
		if ( locationIsDifferent )
		{
			// yes - save the player data since the location was changed
			DataController.m_instance.SaveActiveGame();
		}
	}
	
	// call this when a the save game panel was closed
	public void PanelWasClosed()
	{
		// save the player data in case something has been updated
		DataController.m_instance.SaveActiveGame();

		// unpause the game (a game that is over stays paused - the game over screen paused it for good)
		m_gameIsPaused = m_gameOver;
	}

	// true once the player ship has been destroyed (its explosion may still be playing)
	bool PlayerShipIsDestroyed()
	{
		var combatController = ( m_combatController != null ) ? m_combatController : CombatController.m_instance;

		return ( combatController != null ) && combatController.PlayerIsDestroyed();
	}

	// call this to restart the game (e.g. after game over)
	public void RestartGame()
	{
		// reset game over flag (the game stays paused - this scene must not run against the reloaded player data in its last frame)
		m_gameOver = false;

		// throw away the lost game and go back to the last save (a destroyed ship is never saved)
		DataController.m_instance.ReloadActiveGame();

		// go to the intro screen (not Persistent, which would load the save)
		SceneManager.LoadScene( "Intro" );
	}

	// updates the encounters (call only from hyperspace or starsystem locations)
	public void UpdateEncounters()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get the current player location
		var location = playerData.m_general.m_location;

		// a ship that has just launched has not entered the star system yet, and nothing comes after it there. The star system location is on
		// screen then and calls this too, and every location that was not the star system used to be taken for hyperspace - so the hyperspace
		// encounters were measured against the ship's place in the star system, came after it, and could begin above starport
		if ( ( location != PD_General.Location.StarSystem ) && ( location != PD_General.Location.Hyperspace ) )
		{
			return;
		}

		// get the current star id
		var starId = playerData.m_general.m_currentStarId;

		// get the coordinates
		var coordinates = playerData.m_general.m_coordinates;

		// get the correct alien radar distance
		var alienRadarDistance = ( location == PD_General.Location.Hyperspace ) ? m_alienHyperspaceRadarDistance : m_alienStarSystemRadarDistance;

		// the Dodecahedron is "an ancient distress beacon which attracts any ships in the area" (the Thrynn, STRINFO 2.5; the Starport's analysis: "it will attract the attention
		// of anyone in the area") - with it in the hold the aliens notice the ship from twice as far (how far is the port's choice)
		if ( playerData.m_playerShip.HasArtifact( "Dodecahedron" ) )
		{
			alienRadarDistance *= 2.0f;
		}

		// the encounter the player has run into in this frame (the nearest one, if more than one is within range)
		PD_Encounter encounterToBegin = null;

		var distanceToEncounterToBegin = float.MaxValue;

		// go through each potential encounter
		foreach ( var encounter in playerData.m_encounterList )
		{
			// assume this encounter is in a different location
			encounter.SetDistance( float.MaxValue );

			// skip encounters that have no living ships left (there is nobody to detect the player, chase the player, or meet the player)
			if ( !encounter.HasLivingAlienShips() )
			{
				continue;
			}

			// get the encounter location
			var encounterLocation = encounter.GetLocation();

			// orbit encounters do not move and are not met in flight - they begin when the ship goes into orbit around their planet (see BeginInOrbitEncounter)
			if ( encounterLocation == PD_General.Location.InOrbit )
			{
				continue;
			}

			// are we in the star system location?
			if ( location == PD_General.Location.StarSystem )
			{
				// yes - is this encounter a star system encounter and in the same star system?
				if ( ( encounterLocation != PD_General.Location.StarSystem ) || (  starId != encounter.GetStarId() ) )
				{
					// no - skip it
					continue;
				}
			}
			else
			{
				// no - is this encounter a hyperspace encounter?
				if ( encounterLocation != PD_General.Location.Hyperspace )
				{
					// no - skip it
					continue;
				}
			}

			// calculate the distance from the player to the encounter
			var distance = encounter.CalculateDistance( coordinates );

			// can the aliens detect the player?
			if ( distance < alienRadarDistance )
			{
				// yes - move the aliens towards the player (at a fixed speed)
				encounter.MoveTowards( coordinates );
			}
			else
			{
				// no - move the aliens towards their home coordinates (at a fixed speed)
				encounter.GoHome();
			}

			// are the aliens and the player within encounter range?
			if ( ( distance < m_encounterRange ) && ( distance < distanceToEncounterToBegin ) )
			{
				// yes - remember this encounter. It begins after the loop, so that only one can begin in a frame (beginning it in here
				// let a second encounter within range begin as well, on top of the first)
				encounterToBegin = encounter;

				distanceToEncounterToBegin = distance;
			}
		}

		// did the player run into an encounter?
		if ( encounterToBegin != null )
		{
			// yes - begin it
			BeginEncounter( encounterToBegin );
		}
	}

	// true if the ship is inside a nebula: in hyperspace, by the nebulae of the game data; anywhere in a star system, if that star is inside one; in an encounter, where
	// the encounter began
	public bool ShipIsInsideANebula()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var location = playerData.m_general.m_location;

		// an encounter is where it began
		if ( location == PD_General.Location.Encounter )
		{
			location = playerData.m_general.m_lastLocation;
		}

		switch ( location )
		{
			case PD_General.Location.Hyperspace:

				// (in hyperspace the ship's own position, which flux travel moves without updating the saved coordinates)
				var hyperspaceCoordinates = ( ( playerData.m_general.m_location == PD_General.Location.Hyperspace ) && ( m_playerShip != null ) ) ? m_playerShip.transform.position : playerData.m_general.m_lastHyperspaceCoordinates;

				foreach ( var nebula in gameData.m_nebulaList )
				{
					if ( nebula.Contains( hyperspaceCoordinates ) )
					{
						return true;
					}
				}

				return false;

			case PD_General.Location.DockingBay:
			case PD_General.Location.Starport:

				return false;

			default:

				var starId = playerData.m_general.m_currentStarId;

				return ( starId >= 0 ) && ( starId < gameData.m_starList.Length ) && gameData.m_starList[ starId ].m_insideNebula;
		}
	}

	// begins an encounter - the player is put in the middle of it and the encounter location takes over
	void BeginEncounter( PD_Encounter encounter )
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// save encounter information in the player data
		playerData.m_general.m_currentEncounterId = encounter.m_encounterId;

		// put the player in the middle of the encounter
		playerData.m_general.m_lastEncounterCoordinates = Vector3.zero;

		// let the encounter system know we are now entering this encounter
		m_encounter.JustEntered();

		// switch to the encounter location
		SwitchLocation( PD_General.Location.Encounter );
	}

	// call this when an encounter in orbit lets the ship into orbit (a veloxi drone that was answered correctly) - it does not begin again until the ship leaves the star system
	public void GrantOrbitPermission( int encounterId )
	{
		if ( !m_orbitPermissions.Contains( encounterId ) )
		{
			m_orbitPermissions.Add( encounterId );
		}
	}

	// returns true if this encounter in orbit has let the ship into orbit during this visit to the star system
	public bool HasOrbitPermission( int encounterId )
	{
		return m_orbitPermissions.Contains( encounterId );
	}

	// begins the encounter that waits in orbit around the planet the ship is orbiting, if there is one with living ships (the in orbit location calls this once every time the ship goes into orbit)
	// the ship drops out of orbit into the encounter, and when the encounter ends it is back at the level of the star system (see Encounter.LeaveEncounter) - as in the original game,
	// unless the encounter has let the ship into orbit
	// returns true if an encounter has begun
	public bool BeginInOrbitEncounter()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// this is only for a ship that is in orbit
		if ( playerData.m_general.m_location != PD_General.Location.InOrbit )
		{
			return false;
		}

		// get the planet the ship is orbiting
		var planetController = m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

		if ( ( planetController == null ) || ( planetController.m_planet == null ) )
		{
			return false;
		}

		// go through each potential encounter
		foreach ( var encounter in playerData.m_encounterList )
		{
			// only encounters that are in orbit around a planet of this star
			if ( ( encounter.GetLocation() != PD_General.Location.InOrbit ) || ( encounter.GetStarId() != playerData.m_general.m_currentStarId ) )
			{
				continue;
			}

			// skip an encounter the game data does not have (the orbit position is in the game data)
			if ( ( encounter.m_encounterId < 0 ) || ( encounter.m_encounterId >= gameData.m_encounterList.Length ) )
			{
				continue;
			}

			// is this encounter in orbit around the planet the ship is orbiting?
			if ( gameData.m_encounterList[ encounter.m_encounterId ].m_orbitPosition != planetController.m_planet.m_orbitPosition )
			{
				// no - skip it
				continue;
			}

			// skip encounters that have no living ships left (there is nobody to meet the player)
			if ( !encounter.HasLivingAlienShips() )
			{
				continue;
			}

			// skip an encounter that has already let the ship into orbit during this visit to the star system
			if ( HasOrbitPermission( encounter.m_encounterId ) )
			{
				continue;
			}

			// begin this encounter (there is only ever one in the same orbit)
			BeginEncounter( encounter );

			return true;
		}

		// nobody is waiting here
		return false;
	}

#if UNITY_EDITOR

	// draw gizmos to help debug the game
	void OnDrawGizmos()
	{
		if ( DataController.m_instance == null )
		{
			return;
		}

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get the current player location
		var location = playerData.m_general.m_location;

		// if we are in hyperspace draw the map grid
		if ( location == PD_General.Location.Hyperspace )
		{
			Gizmos.color = Color.green;

			for ( var x = 0; x < 250; x += 10 )
			{
				var start = Tools.GameToWorldCoordinates( new Vector3( x, 0.0f, 0.0f ) );
				var end = Tools.GameToWorldCoordinates( new Vector3( x, 0.0f, 250.0f ) );

				Gizmos.DrawLine( start, end );
			}

			for ( var z = 0; z < 250; z += 10 )
			{
				var start = Tools.GameToWorldCoordinates( new Vector3( 0.0f, 0.0f, z ) );
				var end = Tools.GameToWorldCoordinates( new Vector3( 250.0f, 0.0f, z ) );

				Gizmos.DrawLine( start, end );
			}
		}

		// if we are in hyperspace draw the flux paths
		if ( location == PD_General.Location.Hyperspace )
		{
			Gizmos.color = Color.cyan;

			foreach ( var flux in gameData.m_fluxList )
			{
				Gizmos.DrawLine( flux.GetFrom(), flux.GetTo() );
			}
		}

		// if we are in hyperspace draw the coordinates around the cursor point
		if ( location == PD_General.Location.Hyperspace )
		{
			var ray = UnityEditor.HandleUtility.GUIPointToWorldRay( Event.current.mousePosition );

			var plane = new Plane( Vector3.up, 0.0f );

			float enter;

			if ( plane.Raycast( ray, out enter ) )
			{
				var worldCoordinates = ray.origin + ray.direction * enter;

				var gameCoordinates = Tools.WorldToGameCoordinates( worldCoordinates );

				UnityEditor.Handles.color = Color.blue;

				UnityEditor.Handles.Label( worldCoordinates + Vector3.forward * 64.0f + Vector3.right * 64.0f, Mathf.RoundToInt( gameCoordinates.x ) + "," + Mathf.RoundToInt( gameCoordinates.z ) );
			}
		}

		// if we are in either hyperspace or a star system then draw the alien encounters
		if ( ( location == PD_General.Location.StarSystem ) || ( location == PD_General.Location.Hyperspace ) )
		{
			// draw the encounter radius
			UnityEditor.Handles.color = Color.red;
			UnityEditor.Handles.DrawWireDisc( playerData.m_general.m_coordinates, Vector3.up, m_encounterRange );

			// draw the radar range
			UnityEditor.Handles.color = Color.magenta;

			if ( location == PD_General.Location.Hyperspace )
			{
				UnityEditor.Handles.DrawWireDisc( playerData.m_general.m_coordinates, Vector3.up, m_radar.m_maxHyperspaceDetectionDistance );
			}
			else
			{
				UnityEditor.Handles.DrawWireDisc( playerData.m_general.m_coordinates, Vector3.up, m_radar.m_maxStarSystemDetectionDistance );
			}

			// draw the positions on each encounter
			UnityEditor.Handles.color = Color.red;

			foreach ( var pdEncounter in playerData.m_encounterList )
			{
				var encounterLocation = pdEncounter.GetLocation();

				if ( ( encounterLocation == location ) && ( location == PD_General.Location.Hyperspace ) || ( pdEncounter.GetStarId() == playerData.m_general.m_currentStarId ) )
				{
					// get access to the encounter
					var gdEncounter = gameData.m_encounterList[ pdEncounter.m_encounterId ];

					// draw alien position
					UnityEditor.Handles.color = Color.red;
					UnityEditor.Handles.DrawWireDisc( pdEncounter.m_currentCoordinates, Vector3.up, 16.0f );

					// print alien race
					UnityEditor.Handles.Label( pdEncounter.m_currentCoordinates + Vector3.up * 16.0f, gdEncounter.m_race.ToString() );

					// draw the alien radar range
					UnityEditor.Handles.color = Color.yellow;

					if ( location == PD_General.Location.Hyperspace )
					{
						UnityEditor.Handles.DrawWireDisc( pdEncounter.m_currentCoordinates, Vector3.up, m_alienHyperspaceRadarDistance );
					}
					else
					{
						UnityEditor.Handles.DrawWireDisc( pdEncounter.m_currentCoordinates, Vector3.up, m_alienStarSystemRadarDistance );
					}
				}
			}
		}
	}

	// DEBUG: Spawn a test encounter near the player
	void SpawnTestEncounter()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;

		// only works in hyperspace or star system
		if ( playerData.m_general.m_location != PD_General.Location.Hyperspace &&
			 playerData.m_general.m_location != PD_General.Location.StarSystem )
		{
			m_messages.AddText( "<color=yellow>DEBUG: Can only spawn encounter in hyperspace or star system.</color>" );
			return;
		}

		// determine what location type we need (0 = hyperspace, 1 = star system in game data)
		int requiredLocationCode = playerData.m_general.m_location == PD_General.Location.Hyperspace ? 0 : 1;

		// find an encounter that matches our current location
		int testEncounterId = -1;
		
		// first try to find a Spemin encounter that matches location
		for ( int i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			var gdEnc = gameData.m_encounterList[ i ];
			
			if ( gdEnc.m_location == requiredLocationCode && gdEnc.m_race == GameData.Race.Spemin )
			{
				// for star system encounters, also check we're at the right star
				if ( requiredLocationCode == 1 )
				{
					// find if any star matches this encounter's coordinates
					bool starMatches = false;
					foreach ( var star in gameData.m_starList )
					{
						if ( star.m_xCoordinate == gdEnc.m_xCoordinate && 
							 star.m_yCoordinate == gdEnc.m_yCoordinate &&
							 star.m_id == playerData.m_general.m_currentStarId )
						{
							starMatches = true;
							break;
						}
					}
					if ( !starMatches ) continue;
				}
				
				testEncounterId = i;
				break;
			}
		}

		// fallback: find any encounter that matches location (hyperspace only for simplicity)
		if ( testEncounterId < 0 && requiredLocationCode == 0 )
		{
			for ( int i = 0; i < gameData.m_encounterList.Length; i++ )
			{
				var gdEnc = gameData.m_encounterList[ i ];
				if ( gdEnc.m_location == 0 ) // hyperspace
				{
					testEncounterId = i;
					break;
				}
			}
		}

		if ( testEncounterId < 0 )
		{
			m_messages.AddText( "<color=yellow>DEBUG: No encounter for this location. Try hyperspace.</color>" );
			return;
		}

		// get the encounter (search by id, the list is kept sorted by distance)
		var pdEncounter = playerData.FindEncounter( testEncounterId );

		// reset the encounter to ensure it's properly initialized
		pdEncounter.Reset( testEncounterId );

		// teleport the encounter right next to the player
		Vector3 playerCoords = playerData.m_general.m_location == PD_General.Location.Hyperspace
			? playerData.m_general.m_lastHyperspaceCoordinates
			: playerData.m_general.m_coordinates;

		pdEncounter.SetCoordinates( playerCoords );
		pdEncounter.SetDistance( 0.0f );

		// set up the encounter
		playerData.m_general.m_currentEncounterId = testEncounterId;
		playerData.m_general.m_lastEncounterCoordinates = Vector3.zero;

		// let the encounter system know we are entering
		m_encounter.JustEntered();

		Debug.Log( $"[F9] pdEncounter hashcode = {pdEncounter.GetHashCode()}" );
		Debug.Log( $"[F9] BEFORE SwitchLocation: pdEncounter.m_alienStance = {pdEncounter.m_alienStance}" );

		// switch to the encounter location
		SwitchLocation( PD_General.Location.Encounter );

		Debug.Log( $"[F9] AFTER SwitchLocation: pdEncounter.m_alienStance = {pdEncounter.m_alienStance}" );
		Debug.Log( $"[F9] m_encounter.m_pdEncounter hashcode = {m_encounter.m_pdEncounter?.GetHashCode()}" );
		Debug.Log( $"[F9] m_encounter.m_pdEncounter reference = {(m_encounter.m_pdEncounter == pdEncounter ? "SAME" : "DIFFERENT!")}" );
		Debug.Log( $"[F9] m_encounter.m_pdEncounter.m_alienStance = {m_encounter.m_pdEncounter?.m_alienStance}" );

		// make them hostile for testing combat - set AFTER SwitchLocation so it doesn't get reset
		pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

		Debug.Log( $"[F9] AFTER setting Hostile: pdEncounter.m_alienStance = {pdEncounter.m_alienStance}" );
		Debug.Log( $"[F9] AFTER setting Hostile: m_encounter.m_pdEncounter.m_alienStance = {m_encounter.m_pdEncounter?.m_alienStance}" );

		var raceName = gameData.m_encounterList[ testEncounterId ].m_race.ToString();
		m_messages.AddText( $"<color=#00FF00>DEBUG: Spawned {raceName} encounter! (HOSTILE)</color>" );
	}

#endif // UNITY_EDITOR
}
