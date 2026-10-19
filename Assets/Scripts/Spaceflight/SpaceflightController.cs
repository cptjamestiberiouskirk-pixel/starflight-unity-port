
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
		if ( KeyWentDown( Key.Escape ) && m_gameOver )
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
		// what they do was decided 2026-10-10 for roadmap 1.9)
		if ( playerData.m_playerShip.m_shieldsAreUp && ShipIsInsideANebula() )
		{
			// in the original's words (NAVIGATR >DOWN-SHIELD in a nebula), and then "Shields are down."
			m_messages.AddText( "<color=white>Magnetic disturbances in nebula</color>" );
			m_messages.AddText( "<color=white>causing energy fluctuations.</color>" );

			playerData.m_playerShip.DropShields();
		}

		// while the bearings are lost the coordinates above the main view keep changing, and they come right when the navigator has the bearings back
		var bearingsLost = HasLostBearings();

		if ( ( bearingsLost || m_bearingsWereLost ) && ( ( playerData.m_general.m_location == PD_General.Location.Hyperspace ) || ( playerData.m_general.m_location == PD_General.Location.StarSystem ) ) )
		{
			m_viewport.UpdateCoordinates();
		}

		m_bearingsWereLost = bearingsLost;

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
		if ( KeyWentDown( Key.F9 ) )
		{
			SpawnTestEncounter();
		}

		// DEBUG: Press F10 to instantly destroy player ship (test game over)
		if ( KeyWentDown( Key.F10 ) )
		{
			var combatController = ( m_combatController != null ) ? m_combatController : CombatController.m_instance;
			if ( combatController != null )
			{
				combatController.DestroyPlayerShip( null );
			}
			else
			{
				m_messages.AddText( "<color=#FF0000>DEBUG: Combat controller not available.</color>" );
			}
		}
#endif
	}

	// true in the frame a key of the keyboard goes down, like Input.GetKeyDown was (a computer can have no keyboard, and a headless run has none)
	static bool KeyWentDown( Key key )
	{
		// get to the keyboard that was used last
		var keyboard = Keyboard.current;

		return ( keyboard != null ) && keyboard[ key ].wasPressedThisFrame;
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
			playerData.m_general.GetOrbitPermissions().Clear();
		}

		// leaving orbit lets an encounter that ended back in orbit begin again the next time (the original's WD9EC, cleared by UNNEST-CLEAN on leaving orbit)
		if ( ( newLocation == PD_General.Location.StarSystem ) || ( newLocation == PD_General.Location.Hyperspace ) || ( newLocation == PD_General.Location.DockingBay ) || ( newLocation == PD_General.Location.Starport ) )
		{
			playerData.m_general.GetOrbitEncountersEnded().Clear();
		}

		// a ship in the star system or in hyperspace has left any orbit, and the next planet's landing site starts at LAT 0 LONG 0: the original's DEPART sets PLHI to
		// -1 on leaving orbit, and Select Site then puts its crosshair in the middle of the map (SITE-OV in the decompiled original, WF014) - within one orbit the
		// last site chosen stays
		if ( ( newLocation == PD_General.Location.StarSystem ) || ( newLocation == PD_General.Location.Hyperspace ) )
		{
			playerData.m_general.m_selectedLatitude = 0.0f;
			playerData.m_general.m_selectedLongitude = 0.0f;

			// and DEPART gives the planets the terrain vehicle went out on in that orbit back their minerals, lifeforms and curios (DPART-OV WF166)
			if ( playerData.m_planetSurfaces != null )
			{
				playerData.m_planetSurfaces.RefillVisitedPlanets( playerData.m_general.m_gameWon );
			}
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
					// under LEAVING STARPORT..., the original's ...STARPORT CLEAR and STANDING BY TO MANEUVER, the three kept together (HYPERMSG-OV WF0D5
					// and WF001, LAUNCH-OV &LAUNCH; starflt_139)
					m_messages.AddText( "<color=white>...Starport clear</color>" );
					m_messages.AddText( "<color=white>Standing by to maneuver.</color>" );
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

			// the coordinates above the main view are the new location's (the terrain vehicle's place stayed there in orbit and in the star system until the
			// ship moved - the original's orbit shows the ship's coordinates, "125 100")
			m_viewport.UpdateCoordinates();
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
	// after an encounter in flight ends, no encounter begins for a minute (the original's HYPERMSG-OV WF022 sets ENC-TIME to the time plus 60000 ms, and HYPER-OV
	// >ENCOUNTER begins one only when the time is past it); the aliens go on moving meanwhile
	const float c_encounterHoldOffSeconds = 60.0f;

	// the time (Time.time) before which no encounter begins
	float m_encountersResumeTime;

	// call this when an encounter in flight has ended
	public void HoldOffEncounters()
	{
		m_encountersResumeTime = Time.time + c_encounterHoldOffSeconds;
	}

	// after a flux jump the navigator has lost the ship's bearings for a while, the longer the less skilled (the original's HYPER-OV JMPSHP sets NAV-TIME to the time
	// plus 250 less the navigator's skill, in half seconds, and ?CAN-NAVIGATE is false until then: the coordinates above the main view are anything and the starmap
	// cannot be shown); not saved, as the original's starts again at launch
	float m_bearingsLostUntil;

	// whether the coordinates were shown for lost bearings in the last frame (so that they are shown right once the bearings are back)
	bool m_bearingsWereLost;

	// call this when a flux jump has thrown the ship off its bearings
	public void LoseBearings()
	{
		m_bearingsLostUntil = Time.time + Mathf.Max( 0, 250 - CombatController.GetNavigatorSkill() ) * 0.5f;
	}

	// true while the navigator has not got the ship's bearings back after a flux jump
	public bool HasLostBearings()
	{
		return Time.time < m_bearingsLostUntil;
	}

	// the planet whose gravity the science officer has analysed (the original's ?G-AWARE: only then does Land warn), and the planet Land has warned about once
	// (the original's GWF: a second Land goes down anyway) - neither is saved
	public int m_gravityAnalysedPlanetId = -1;

	// the science officer's survey of the planet last analysed from orbit: its mineral fields and lifeforms
	// (PlanetSurvey; the sensor picture and the landing map show its marks; not saved, as the gravity's analysis is not)
	public PlanetSurvey m_planetSurvey;
	public int m_landingWarnedPlanetId = -1;

	// what the landing that is under way will end in (LAND-OV WF430): a crash for want of fuel, or the hull crushed by the gravity
	public bool m_landingCrashes;
	public bool m_landingCrushes;

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

		// the aliens move only when the clock has gone 2 star hours on since they last did (the original's TRAK-ENCS: STAR-HR against TRAK-HR, then TRAK-E)
		var hourCount = playerData.m_general.m_day * 24 + playerData.m_general.m_hour;

		if ( ( m_trackedHourCount < 0 ) || ( hourCount < m_trackedHourCount ) )
		{
			m_trackedHourCount = hourCount;
		}

		var trackAliens = ( hourCount - m_trackedHourCount ) >= c_trackingHours;

		if ( trackAliens )
		{
			m_trackedHourCount = hourCount;

			// the science officer's report of this pass starts with its heading (HYPERMSG-OV TRAK-E: WF27F on)
			m_motionHeadingPending = true;
		}

		// with the Crystal Pearl aboard the aliens come farther at each move and are not held near home (the original's ?12, set when artifact 13, the Crystal Pearl,
		// is in the hold: HYPERMSG-OV (!'EXT), COMMSPEC-OV WEA50; no word of the shipped game looks for the Dodecahedron)
		var crystalPearlAboard = playerData.m_playerShip.HasArtifact( "Crystal Pearl" );

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

			// every 2 star hours the aliens jump towards the ship (they never go back home)
			if ( trackAliens && TrackTowards( encounter, coordinates, location == PD_General.Location.Hyperspace, crystalPearlAboard ) )
			{
				distance = encounter.CalculateDistance( coordinates );
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

		// did the player run into an encounter? (not within a minute of the last one)
		if ( ( encounterToBegin != null ) && ( Time.time >= m_encountersResumeTime ) )
		{
			// yes - begin it
			BeginEncounter( encounterToBegin );
		}
	}

	// a star system's alien groups are placed afresh each time the ship comes into it from hyperspace, as the original's SYSSETUP (HYPER-OV: SETPLNXY gives each
	// alien group of the system SETRNDXY); not when the ship comes back to the system from orbit, an encounter or the Starport, or when a game is loaded
	public void PlaceStarSystemEncounters( int starId )
	{
		foreach ( var encounter in DataController.m_instance.m_playerData.m_encounterList )
		{
			if ( ( encounter.GetLocation() == PD_General.Location.StarSystem ) && ( encounter.GetStarId() == starId ) )
			{
				encounter.PlaceAtRandomInStarSystem();
			}
		}
	}

	// how the aliens come after the ship, as the original's TRAK-E (HYPERMSG-OV): every 2 star hours each group jumps towards the ship, along each axis a share of the
	// way - a half in hyperspace, three quarters in a star system, four fifths and six sevenths with the Crystal Pearl aboard (WF333) - in whole map coordinates of 8 of
	// the original's units, rounded towards none, and the whole way once that comes to none, so that it lands on the ship (WF31B). In hyperspace only the groups of the
	// region around the ship move (ILOCAL, 75 of the original's units), and only while they are within 63 of them of home on both axes (WF3AB), unless the
	// Crystal Pearl is aboard; in a star system every group of it moves (IGLOBAL)
	const int c_trackingHours = 2;
	const float c_hyperspaceUnit = 32.0f;
	const float c_starSystemUnit = 8192.0f / 400.0f;
	const float c_localRegionUnits = 75.0f;

	// the game hour (days * 24 + hours) the aliens last moved at (-1 before the first: not saved, as the original's TRAK-HR is set at launch)
	int m_trackedHourCount = -1;

	// true until the first move of a tracking pass is reported, which says "Motion detected at" first (HYPERMSG-OV WF27F)
	bool m_motionHeadingPending;

	// the science skill above which the science officer reports motion (HYPERMSG-OV WF28B: 0x0096)
	const int c_motionReportSkill = 150;

	// whether the science officer is skilled enough to report the aliens' moves (the science officer's ASSIGN-CREW slot, its CREWMEMBER science byte)
	public static bool ScienceOfficerReportsMotion( PlayerData playerData )
	{
		// no science officer, no report (a ship started in the Editor's Spaceflight scene has no crew)
		if ( !playerData.m_crewAssignment.IsAssigned( PD_CrewAssignment.Role.ScienceOfficer ) )
		{
			return false;
		}

		var scienceOfficer = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.ScienceOfficer );

		return ( scienceOfficer != null ) && ( scienceOfficer.m_science > c_motionReportSkill );
	}

	// a science officer of science above 150 reports each group's move, under one "Motion detected at" a pass, as the original's (HYPERMSG-OV WF28B): each axis's step
	// in the original's units over -8, so a group 5 map coordinates east that comes 2 nearer reads X:2, the Y at the seventh column - and nothing when the step is less
	// than a map coordinate on both axes
	void ReportMotion( int unitsX, int unitsZ )
	{
		if ( ( ( unitsX / 8 ) * 8 == 0 ) && ( ( unitsZ / 8 ) * 8 == 0 ) )
		{
			return;
		}

		if ( !ScienceOfficerReportsMotion( DataController.m_instance.m_playerData ) )
		{
			return;
		}

		if ( m_motionHeadingPending )
		{
			m_messages.AddText( "<color=white>Motion detected at</color>" );

			m_motionHeadingPending = false;
		}

		m_messages.AddText( "<color=white>" + ( "X:" + ( unitsX / -8 ) ).PadRight( 7 ) + "Y:" + ( unitsZ / -8 ) + "</color>" );
	}

	// moves one group of aliens a step of the original's towards the ship - false if it did not move
	bool TrackTowards( PD_Encounter encounter, Vector3 shipCoordinates, bool inHyperspace, bool crystalPearlAboard )
	{
		var unit = inHyperspace ? c_hyperspaceUnit : c_starSystemUnit;

		if ( inHyperspace )
		{
			// only the region around the ship
			if ( Vector3.Distance( shipCoordinates, encounter.m_currentCoordinates ) > c_localRegionUnits * unit )
			{
				return false;
			}

			// and only near home (the original's displacement bytes: -63 to 63 across, -64 to 63 along)
			var displacementX = Mathf.RoundToInt( ( encounter.m_currentCoordinates.x - encounter.m_homeCoordinates.x ) / unit );
			var displacementZ = Mathf.RoundToInt( ( encounter.m_currentCoordinates.z - encounter.m_homeCoordinates.z ) / unit );

			if ( !crystalPearlAboard && ( ( displacementX < -63 ) || ( displacementX > 63 ) || ( displacementZ < -64 ) || ( displacementZ > 63 ) ) )
			{
				return false;
			}
		}

		var numerator = ( inHyperspace ? 1 : 3 ) + ( crystalPearlAboard ? 3 : 0 );
		var denominator = ( inHyperspace ? 2 : 4 ) + ( crystalPearlAboard ? 3 : 0 );

		var stepX = TrackingStep( shipCoordinates.x - encounter.m_currentCoordinates.x, unit, numerator, denominator, out var unitsX );
		var stepZ = TrackingStep( shipCoordinates.z - encounter.m_currentCoordinates.z, unit, numerator, denominator, out var unitsZ );

		encounter.m_currentCoordinates += new Vector3( stepX, 0.0f, stepZ );

		// the science officer reports the move
		ReportMotion( unitsX, unitsZ );

		return ( stepX != 0.0f ) || ( stepZ != 0.0f );
	}

	// one axis of a TRAK-E move: the share of the distance (in the original's units) in whole map coordinates, rounded towards none, or the whole distance once
	// that is none - and the step in the original's units (WF283, WF287)
	static float TrackingStep( float distance, float unit, int numerator, int denominator, out int stepUnits )
	{
		var units = Mathf.RoundToInt( distance / unit );

		if ( units == 0 )
		{
			stepUnits = 0;

			return 0.0f;
		}

		var step = units * numerator / denominator / 8 * 8;

		stepUnits = ( step != 0 ) ? step : units;

		return ( step != 0 ) ? ( step * unit ) : distance;
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
		var permissions = DataController.m_instance.m_playerData.m_general.GetOrbitPermissions();

		if ( !permissions.Contains( encounterId ) )
		{
			permissions.Add( encounterId );
		}
	}

	// returns true if this encounter in orbit has let the ship into orbit during this visit to the star system (kept in the save)
	public bool HasOrbitPermission( int encounterId )
	{
		return DataController.m_instance.m_playerData.m_general.GetOrbitPermissions().Contains( encounterId );
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

			// and one that ended back in this orbit (the Noah 9 derelict), until the ship leaves it
			if ( playerData.m_general.GetOrbitEncountersEnded().Contains( encounter.m_encounterId ) )
			{
				continue;
			}

			// and the Mechan fleet at Heaven once the Mechans have been friendly (the original makes the *HEAVEN encounter inactive, COMM-OV +!EDL)
			if ( ( encounter.m_encounterId == Encounter.c_heavenEncounterId ) && playerData.m_general.m_mechan9Unlocked )
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
