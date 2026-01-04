
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

		// are we in the star system or hyperspace locations?
		if ( ( playerData.m_general.m_location == PD_General.Location.StarSystem ) || ( playerData.m_general.m_location == PD_General.Location.Hyperspace ) )
		{
			// yes - update the game time
			playerData.m_general.UpdateGameTime( Time.deltaTime );
		}

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

				PanelController.m_instance.m_saveGamePanel.SetCallbackObject( this );

				PanelController.m_instance.Open( PanelController.m_instance.m_saveGamePanel );

				m_gameIsPaused = true;
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
			var combatController = m_combatController ?? CombatController.m_instance;
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

		// unpause the game
		m_gameIsPaused = false;
	}

	// call this to restart the game (e.g. after game over)
	public void RestartGame()
	{
		// unpause the game
		m_gameIsPaused = false;
		
		// reset game over flag
		m_gameOver = false;

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

		// get the current star id
		var starId = playerData.m_general.m_currentStarId;

		// get the coordinates
		var coordinates = playerData.m_general.m_coordinates;

		// get the correct alien radar distance
		var alienRadarDistance = ( location == PD_General.Location.Hyperspace ) ? m_alienHyperspaceRadarDistance : m_alienStarSystemRadarDistance;

		// go through each potential encounter
		foreach ( var encounter in playerData.m_encounterList )
		{
			// assume this encounter is in a different location
			encounter.SetDistance( float.MaxValue );

			// get the encounter location
			var encounterLocation = encounter.GetLocation();

			// orbit encounters are handled in maneuver
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
			if ( distance < m_encounterRange )
			{
				// yes - save encounter information in the player data
				playerData.m_general.m_currentEncounterId = encounter.m_encounterId;

				// put the player in the middle of the encounter
				playerData.m_general.m_lastEncounterCoordinates = Vector3.zero;

				// let the encounter system know we are now entering this encounter
				m_encounter.JustEntered();

				// switch to the encounter location
				SwitchLocation( PD_General.Location.Encounter );
			}
		}
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

		// reset the encounter to ensure it's properly initialized
		playerData.m_encounterList[ testEncounterId ].Reset( testEncounterId );

		// get the encounter
		var pdEncounter = playerData.m_encounterList[ testEncounterId ];

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
