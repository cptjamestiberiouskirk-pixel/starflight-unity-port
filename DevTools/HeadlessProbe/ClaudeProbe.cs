#if UNITY_EDITOR

// Headless play mode probe. It lives in DevTools/HeadlessProbe, outside Assets, so it is not part of the project.
// probe.ps1 copies it into Assets/ for one run and deletes it again. Never commit the copy in Assets/ (.gitignore keeps it out).
//
// Launch: Unity.exe -batchmode -nographics -projectPath <project> -logFile <log> -executeMethod ClaudeProbe.Run -probeScenario <name>
//
// It opens the Spaceflight scene, enters play mode, swaps DataController's save system for an in-memory one
// (so the real save files are never read or written), drives a scenario, logs "[ClaudeProbe] ..." lines and exits.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

public class ClaudeProbe : MonoBehaviour
{
	const string c_activeKey = "ClaudeProbe.Active";
	const string c_startKey = "ClaudeProbe.Start";
	const string c_tag = "[ClaudeProbe] ";

	static int s_exceptionCount;

	// errors logged with Debug.LogError or by Unity itself (not exceptions), and the last of them
	static int s_errorCount;
	static string s_lastError;
	static readonly Dictionary<string, int> s_exceptions = new Dictionary<string, int>();
	static bool s_saveSystemSwapped;
	static bool s_spaceflightReady;
	static bool s_finished;

	// save system that never touches the disk
	class MemorySaveSystem : ISaveSystem
	{
		public static int s_existsCalls;
		public static int s_saveCalls;

		public static readonly Dictionary<int, string> s_slots = new Dictionary<int, string>();

		public void Save<T>( string fileName, int slot, T data ) { s_saveCalls++; s_slots[ slot ] = JsonUtility.ToJson( data, true ); }
		public T Load<T>( string fileName, int slot ) { return s_slots.TryGetValue( slot, out var json ) ? JsonUtility.FromJson<T>( json ) : default; }
		public bool Exists( string fileName, int slot ) { s_existsCalls++; return s_slots.ContainsKey( slot ); }
		public void Delete( string fileName, int slot ) { s_slots.Remove( slot ); }
	}

	// ---------------------------------------------------------------- editor side

	public static void Run()
	{
		EditorSceneManager.OpenScene( "Assets/Scenes/" + TargetScene() + ".unity" );

		SessionState.SetBool( c_activeKey, true );
		SessionState.SetFloat( c_startKey, (float) EditorApplication.timeSinceStartup );

		Debug.Log( c_tag + "entering play mode" );

		EditorApplication.update += Watchdog;
		EditorApplication.EnterPlaymode();
	}

	[InitializeOnLoadMethod]
	static void EditorInit()
	{
		if ( SessionState.GetBool( c_activeKey, false ) )
		{
			EditorApplication.update += Watchdog;
		}
	}

	static int s_unpauseCount;

	static void Watchdog()
	{
		// the console's "Error Pause" option (a per-user editor preference) pauses play mode on any logged error, which would stall the probe
		if ( EditorApplication.isPlaying && EditorApplication.isPaused )
		{
			if ( s_unpauseCount++ < 3 )
			{
				Debug.Log( c_tag + "play mode was paused (Error Pause?) - resuming" );
			}

			EditorApplication.isPaused = false;
		}

		var start = SessionState.GetFloat( c_startKey, 0.0f );

		if ( !s_finished && ( EditorApplication.timeSinceStartup - start > 120.0 ) )
		{
			Debug.Log( c_tag + "RESULT timeout" );
			Abort( 3 );
		}
	}

	static void Abort( int code )
	{
		s_finished = true;

		// if the real save system is still in place, make sure the quit handler has nothing to save
		if ( !s_saveSystemSwapped && ( DataController.m_instance != null ) )
		{
			DataController.m_instance.m_playerData = null;
		}

		EditorApplication.Exit( code );
	}

	// ---------------------------------------------------------------- runtime side

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void RuntimeInit()
	{
		if ( !SessionState.GetBool( c_activeKey, false ) )
		{
			return;
		}

		s_exceptionCount = 0;
		s_exceptions.Clear();
		s_saveSystemSwapped = false;
		s_spaceflightReady = false;
		s_finished = false;

		// batch mode never has focus, so the game loop only advances when it is allowed to run in the background
		Application.runInBackground = true;

		// make sure exceptions come with their script stack so they can be attributed
		Application.SetStackTraceLogType( LogType.Exception, StackTraceLogType.ScriptOnly );

		Application.logMessageReceived += OnLog;
		SceneManager.sceneLoaded += OnSceneLoaded;

		var probeObject = new GameObject( "ClaudeProbe" );
		DontDestroyOnLoad( probeObject );
		probeObject.AddComponent<ClaudeProbe>();

		Debug.Log( c_tag + "runtime init" );
	}

	static void OnLog( string condition, string stackTrace, LogType type )
	{
		if ( type == LogType.Error )
		{
			s_errorCount++;
			s_lastError = condition;

			return;
		}

		if ( type != LogType.Exception )
		{
			return;
		}

		s_exceptionCount++;

		// key = message + first stack frame
		var firstFrame = "";

		if ( !string.IsNullOrEmpty( stackTrace ) )
		{
			var newline = stackTrace.IndexOf( '\n' );
			firstFrame = ( newline > 0 ) ? stackTrace.Substring( 0, newline ) : stackTrace;
		}

		var key = condition + " @ " + firstFrame;

		s_exceptions.TryGetValue( key, out var count );
		s_exceptions[ key ] = count + 1;

		// show the first occurrence of each exception with a little more of its stack
		if ( count == 0 )
		{
			var stack = ( stackTrace ?? "" ).Replace( "\n", " | " );

			Debug.Log( c_tag + "exception at frame " + Time.frameCount + ": " + condition + " @ " + ( ( stack.Length > 420 ) ? stack.Substring( 0, 420 ) : stack ) );
		}
	}

	static void OnSceneLoaded( Scene scene, LoadSceneMode mode )
	{
		Debug.Log( c_tag + "scene loaded: " + scene.name + " frame=" + Time.frameCount );

		// swap the save system as soon as the data controller exists (this runs after its Awake and before its Start)
		if ( !s_saveSystemSwapped && ( DataController.m_instance != null ) )
		{
			var field = typeof( DataController ).GetField( "_saveSystem", BindingFlags.Instance | BindingFlags.NonPublic );

			if ( field == null )
			{
				Debug.Log( c_tag + "RESULT abort: DataController._saveSystem not found" );
				Abort( 2 );
				return;
			}

			MemorySaveSystem.s_existsCalls = 0;
			MemorySaveSystem.s_saveCalls = 0;
			MemorySaveSystem.s_slots.Clear();

			field.SetValue( DataController.m_instance, new MemorySaveSystem() );

			s_saveSystemSwapped = field.GetValue( DataController.m_instance ) is MemorySaveSystem;

			Debug.Log( c_tag + "save system swapped: " + s_saveSystemSwapped );

			if ( !s_saveSystemSwapped )
			{
				Debug.Log( c_tag + "RESULT abort: save system swap failed" );
				Abort( 2 );
				return;
			}
		}

		// the scenario's scene has been loaded by the persistent scene (the data controller has started by then)
		if ( ( scene.name == TargetScene() ) && ( DataController.m_instance != null ) && s_saveSystemSwapped && ( MemorySaveSystem.s_existsCalls > 0 ) )
		{
			// put the player in hyperspace before the spaceflight controller starts
			if ( ( scene.name == "Spaceflight" ) && !s_spaceflightReady )
			{
				DataController.m_instance.m_playerData.m_general.m_location = PD_General.Location.Hyperspace;
			}

			s_spaceflightReady = true;
		}
	}

	static string TargetScene()
	{
		return GetArg( "-probeScenario", "h6" ).StartsWith( "starport" ) ? "Starport" : "Spaceflight";
	}

	static string GetArg( string name, string fallback )
	{
		var args = Environment.GetCommandLineArgs();

		for ( var i = 0; i < args.Length - 1; i++ )
		{
			if ( args[ i ] == name )
			{
				return args[ i + 1 ];
			}
		}

		return fallback;
	}

	static void Log( string text )
	{
		Debug.Log( c_tag + text );
	}

	void Finish( string result, int code )
	{
		Log( "in-memory saves=" + MemorySaveSystem.s_saveCalls + " distinct exceptions: " + s_exceptions.Count + " total: " + s_exceptionCount );

		foreach ( var pair in s_exceptions )
		{
			Log( "  x" + pair.Value + " " + pair.Key );
		}

		Log( "RESULT " + result );

		s_finished = true;

		if ( !s_saveSystemSwapped && ( DataController.m_instance != null ) )
		{
			DataController.m_instance.m_playerData = null;
		}

		EditorApplication.Exit( code );
	}

	IEnumerator Start()
	{
		// wait for the real spaceflight scene (the one loaded by the persistent scene)
		var frames = 0;

		while ( !s_spaceflightReady )
		{
			yield return null;

			if ( ++frames > 5000 )
			{
				Finish( "abort: spaceflight scene never became ready", 2 );
				yield break;
			}
		}

		if ( !s_saveSystemSwapped )
		{
			Finish( "abort: save system not swapped", 2 );
			yield break;
		}

		// the data controller asks the save system about every slot when it starts - if it never asked ours, it started before the swap and read the real slots
		if ( MemorySaveSystem.s_existsCalls != DataController.c_numSaveGameSlots )
		{
			Finish( "abort: the data controller loaded the save slots before the save system was swapped (existsCalls=" + MemorySaveSystem.s_existsCalls + ")", 2 );
			yield break;
		}

		Log( "save slots came from the in-memory save system (existsCalls=" + MemorySaveSystem.s_existsCalls + ")" );

		// let everything start up
		for ( var i = 0; i < 30; i++ )
		{
			yield return null;
		}

		// a scenario starts with the planets of the first star system generated and the game running - the game is paused while they are being generated,
		// and since the main thread no longer waits for each planet (M18) thirty frames are over long before that is done
		if ( ( TargetScene() == "Spaceflight" ) && ( SpaceflightController.m_instance != null ) )
		{
			var generationStart = Time.realtimeSinceStartup;

			while ( SpaceflightController.m_instance.m_starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - generationStart < 60.0f ) )
			{
				yield return null;
			}

			Log( "the planets of the first star system were generated " + ( Time.realtimeSinceStartup - generationStart ).ToString( "F2" ) + " s after the start up frames (still generating: " + SpaceflightController.m_instance.m_starSystem.GeneratingPlanets() + ")" );
		}

		var scenario = GetArg( "-probeScenario", "h6" );

		Log( "scenario=" + scenario + " frame=" + Time.frameCount + " location=" + DataController.m_instance.m_playerData.m_general.m_location + " exceptionsSoFar=" + s_exceptionCount );

		switch ( scenario )
		{
			case "h6":
				yield return ScenarioH6();
				break;

			case "m15":
				yield return ScenarioM15();
				break;

			case "batch1":
				yield return ScenarioBatch1();
				break;

			case "m13":
				yield return ScenarioM13();
				break;

			case "m14":
				yield return ScenarioM14();
				break;

			case "m7":
				yield return ScenarioM7();
				break;

			case "sensorpictures":
				yield return ScenarioSensorPictures();
				break;

			case "sensorplanet":
				yield return ScenarioSensorPlanet();
				break;

			case "colournames":
				yield return ScenarioColourNames();
				break;

			case "m8":
				yield return ScenarioM8();
				break;

			case "m24":
				yield return ScenarioM24();
				break;

			case "h5":
				yield return ScenarioH5();
				break;

			case "m19":
				yield return ScenarioM19();
				break;

			case "h7":
				yield return ScenarioH7();
				break;

			case "starport":
				yield return ScenarioStarport();
				break;

			case "starport-m22":
				yield return ScenarioStarportM22();
				break;

			case "starport-ship":
				yield return ScenarioStarportShip();
				break;

			case "m12":
				yield return ScenarioM12();
				break;

			case "m20":
				yield return ScenarioM20();
				break;

			case "h8":
				yield return ScenarioH8();
				break;

			case "m10":
				yield return ScenarioM10();
				break;

			case "m23":
				yield return ScenarioM23();
				break;

			case "missiles":
				yield return ScenarioMissiles();
				break;

			case "m11":
				yield return ScenarioM11();
				break;

			case "m18":
				yield return ScenarioM18();
				break;

			case "m17":
				yield return ScenarioM17();
				break;

			case "m16":
				yield return ScenarioM16();
				break;

			case "systemmap":
				yield return ScenarioSystemMap();
				break;

			case "maplife":
				yield return ScenarioMapLife();
				break;

			case "nomaps":
				yield return ScenarioNoMaps();
				break;

			case "m25":
				yield return ScenarioM25();
				break;

			case "m26":
				yield return ScenarioM26();
				break;

			case "m27":
				yield return ScenarioM27();
				break;

			case "starport-ledger":
				yield return ScenarioStarportLedger();
				break;

			case "cargo":
				yield return ScenarioCargo();
				break;

			case "pushback":
				yield return ScenarioPushBack();
				break;

			case "savedata":
				yield return ScenarioSaveData();
				break;

			case "combat":
				yield return ScenarioCombat();
				break;

			case "encounters":
				yield return ScenarioEncounters();
				break;

			case "comms":
				yield return ScenarioComms();
				break;

			case "terrain":
				yield return ScenarioTerrain();
				break;

			case "savepanel":
				yield return ScenarioSavePanel();
				break;

			case "input":
				yield return ScenarioInput();
				break;

			case "starport-input":
				yield return ScenarioStarportInput();
				break;

			case "visual":
				yield return ScenarioVisual();
				break;

			case "explosionpictures":
				yield return ScenarioExplosionPictures();
				break;

			case "hitpictures":
				yield return ScenarioHitPictures();
				break;

			case "weaponpictures":
				yield return ScenarioWeaponPictures();
				break;

			case "transporterpictures":
				yield return ScenarioTransporterPictures();
				break;

			case "debrispictures":
				yield return ScenarioDebrisPictures();
				break;

			case "atmospherepictures":
				yield return ScenarioAtmospherePictures();
				break;

			case "starfieldpictures":
				yield return ScenarioStarfieldPictures();
				break;

			case "perframe":
				yield return ScenarioPerFrame();
				break;

			case "starport-transport":
				yield return ScenarioStarportTransport();
				break;

			case "shipslog":
				yield return ScenarioShipsLog();
				break;

			case "leaks":
				yield return ScenarioLeaks();
				break;

			case "latent":
				yield return ScenarioLatent();
				break;

			case "editortools":
				yield return ScenarioEditorTools();
				break;

			case "starport-savedata":
				yield return ScenarioStarportSaveData();
				break;

			case "commlink":
				yield return ScenarioCommLink();
				break;

			case "deposits":
				yield return ScenarioDeposits();
				break;

			case "unmapped":
				yield return ScenarioUnmapped();
				break;

			case "orbit":
				yield return ScenarioOrbit();
				break;

			case "erosion":
				yield return ScenarioErosion();
				break;

			case "smallfixes":
				yield return ScenarioSmallFixes();
				break;

			case "encounterdata":
				yield return ScenarioEncounterData();
				break;

			case "drones":
				yield return ScenarioDrones();
				break;

			case "gameclock":
				yield return ScenarioGameClock();
				break;

			case "shipmodels":
				yield return ScenarioShipModels();
				break;

			case "recovereddata":
				yield return ScenarioRecoveredData();
				break;

			case "flaredata":
				yield return ScenarioFlareData();
				break;

			case "sunlife":
				yield return ScenarioSunLife();
				break;

			case "calendar":
				yield return ScenarioCalendar();
				break;

			case "pickups":
				yield return ScenarioPickups();
				break;

			case "ruins":
				yield return ScenarioRuins();
				break;

			case "artifactsites":
				yield return ScenarioArtifactSites();
				break;

			case "formations":
				yield return ScenarioFormations();
				break;

			case "dropcargo":
				yield return ScenarioDropCargo();
				break;

			case "cargodisplay":
				yield return ScenarioCargoDisplay();
				break;

			case "crystalfield":
				yield return ScenarioCrystalField();
				break;

			case "blackegg":
				yield return ScenarioBlackEgg();
				break;

			case "tvtakesegg":
				yield return ScenarioTvTakesEgg();
				break;

			case "crystalcone":
				yield return ScenarioCrystalCone();
				break;

			case "win":
				yield return ScenarioWin();
				break;

			case "starport-win":
				yield return ScenarioStarportWin();
				break;

			case "arthflare":
				yield return ScenarioArthFlare();
				break;

			case "gameover":
				yield return ScenarioGameOver();
				break;

			case "racelosses":
				yield return ScenarioRaceLosses();
				break;

			case "tesseract":
				yield return ScenarioTesseract();
				break;

			case "whiningorb":
				yield return ScenarioWhiningOrb();
				break;

			case "roddevice":
				yield return ScenarioRodDevice();
				break;

			case "crystalpearl":
				yield return ScenarioCrystalPearl();
				break;

			case "dodecahedron":
				yield return ScenarioDodecahedron();
				break;

			case "shimmeringball":
				yield return ScenarioShimmeringBall();
				break;

			case "ringdevice":
				yield return ScenarioRingDevice();
				break;

			case "starport-endurium":
				yield return ScenarioStarportEndurium();
				break;

			case "nebula":
				yield return ScenarioNebula();
				break;

			case "starport-repair":
				yield return ScenarioStarportRepair();
				break;

			case "distress":
				yield return ScenarioDistress();
				break;

			case "vesselstats":
				yield return ScenarioVesselStats();
				break;

			case "starmapfuel":
				yield return ScenarioStarmapFuel();
				break;

			case "colonies":
				yield return ScenarioColonies();
				break;

			case "systemdamage":
				yield return ScenarioSystemDamage();
				break;

			case "systemeffects":
				yield return ScenarioSystemEffects();
				break;

			case "systemrepairs":
				yield return ScenarioSystemRepairs();
				break;

			case "homeworlds":
				yield return ScenarioHomeworlds();
				break;

			case "cowardice":
				yield return ScenarioCowardice();
				break;

			case "elowangift":
				yield return ScenarioElowanGift();
				break;

			case "veloxdemands":
				yield return ScenarioVeloxDemands();
				break;

			case "thrynntrades":
				yield return ScenarioThrynnTrades();
				break;

			case "mood":
				yield return ScenarioMood();
				break;

			case "rivals":
				yield return ScenarioRivals();
				break;

			case "transmissions":
				yield return ScenarioTransmissions();
				break;

			case "starstability":
				yield return ScenarioStarStability();
				break;

			case "analysisskill":
				yield return ScenarioAnalysisSkill();
				break;

			case "salvage":
				yield return ScenarioSalvage();
				break;

			case "randomruins":
				yield return ScenarioRandomRuins();
				break;

			case "lava":
				yield return ScenarioLava();
				break;

			case "aliennames":
				yield return ScenarioAlienNames();
				break;

			case "lifeforms":
				yield return ScenarioLifeforms();
				break;

			case "tvweapons":
				yield return ScenarioTVWeapons();
				break;

			case "lifebehaviour":
				yield return ScenarioLifeBehaviour();
				break;

			case "crewdeath":
				yield return ScenarioCrewDeath();
				break;

			case "mates":
				yield return ScenarioMates();
				break;

			case "lifeformdata":
				yield return ScenarioLifeformData();
				break;

			case "capture":
				yield return ScenarioCapture();
				break;

			case "starport-specimens":
				yield return ScenarioStarportSpecimens();
				break;

			case "look":
				yield return ScenarioLook();
				break;

			case "lifeformmodels":
				yield return ScenarioLifeformModels();
				break;

			case "lifeformpictures":
				yield return ScenarioLifeformPictures();
				break;

			case "uphill":
				yield return ScenarioUphill();
				break;

			case "flatdevice":
				yield return ScenarioFlatDevice();
				break;

			case "redcylinder":
				yield return ScenarioRedCylinder();
				break;

			case "orbitplanet":
				yield return ScenarioOrbitPlanet();
				break;

			case "storms":
				yield return ScenarioStorms();
				break;

			case "screenpictures":
				yield return ScenarioScreenPictures();
				break;

			case "systemviewpictures":
				yield return ScenarioSystemViewPictures();
				break;

			case "starport-screenpictures":
				yield return ScenarioStarportScreenPictures();
				break;

			case "coordinates":
				yield return ScenarioCoordinates();
				break;

			case "alienanalysis":
				yield return ScenarioAlienAnalysis();
				break;

			case "tvdisplay":
				yield return ScenarioTVDisplay();
				break;

			case "coordinatebar":
				yield return ScenarioCoordinateBar();
				break;

			case "statusdisplay":
				yield return ScenarioStatusDisplay();
				break;

			case "distressconfirm":
				yield return ScenarioDistressConfirm();
				break;

			case "systemtravel":
				yield return ScenarioSystemTravel();
				break;

			case "landinglines":
				yield return ScenarioLandingLines();
				break;

			case "alienshields":
				yield return ScenarioAlienShields();
				break;

			case "ancientsites":
				yield return ScenarioAncientSites();
				break;

			case "disembarklines":
				yield return ScenarioDisembarkLines();
				break;

			default:
				Finish( "abort: unknown scenario " + scenario, 2 );
				break;
		}
	}

	// ---------------------------------------------------------------- helpers

	static IEnumerator Frames( int count )
	{
		for ( var i = 0; i < count; i++ )
		{
			yield return null;
		}
	}

	// find an encounter with the given location code (0 = hyperspace, 1 = star system), ship counts and race
	// (spemin ships have debris models - most other races do not, and mechans are hostile to a crewless ship)
	static int FindEncounter( int location, int minShips, int atOnce, int skip, GameData.Race race = GameData.Race.Spemin )
	{
		var gameData = DataController.m_instance.m_gameData;

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			var encounter = gameData.m_encounterList[ i ];

			if ( ( encounter.m_location == location ) && ( encounter.m_maxNumShips >= minShips ) && ( encounter.m_maxNumShipsAtOnce == atOnce ) && ( encounter.m_race == race ) )
			{
				if ( skip-- <= 0 )
				{
					return i;
				}
			}
		}

		return -1;
	}

	static void EnterEncounter( int encounterId )
	{
		var playerData = DataController.m_instance.m_playerData;
		var spaceflightController = SpaceflightController.m_instance;

		playerData.m_general.m_currentEncounterId = encounterId;
		playerData.m_general.m_lastEncounterCoordinates = Vector3.zero;

		spaceflightController.m_encounter.JustEntered();
		spaceflightController.SwitchLocation( PD_General.Location.Encounter );
	}

	static void LeaveEncounter()
	{
		var playerData = DataController.m_instance.m_playerData;

		// same thing Encounter.Update does when the player flies out of the encounter
		var exitDirection = Vector3.forward;

		if ( playerData.m_general.m_lastLocation == PD_General.Location.Hyperspace )
		{
			playerData.m_general.m_lastHyperspaceCoordinates += exitDirection * SpaceflightController.m_instance.m_encounterRange * 1.25f;
		}
		else
		{
			playerData.m_general.m_lastStarSystemCoordinates += exitDirection * SpaceflightController.m_instance.m_encounterRange * 1.25f;
		}

		SpaceflightController.m_instance.SwitchLocation( playerData.m_general.m_lastLocation );
	}

	// kill an alien ship through the real damage path
	static void Kill( int alienIndex )
	{
		var method = typeof( CombatController ).GetMethod( "ApplyDamageToAlien", BindingFlags.Instance | BindingFlags.NonPublic );

		try
		{
			method.Invoke( CombatController.m_instance, new object[] { alienIndex, 100000 } );
		}
		catch ( TargetInvocationException exception )
		{
			Log( "Kill( " + alienIndex + " ) threw " + exception.InnerException.GetType().Name + ": " + exception.InnerException.Message );
		}
	}

	static string Dump()
	{
		var encounter = SpaceflightController.m_instance.m_encounter;
		var alienShipList = encounter.m_pdEncounter.GetAlienShipList();

		var slotShipField = typeof( Encounter ).GetField( "m_alienShipList", BindingFlags.Instance | BindingFlags.NonPublic );
		var slotShips = slotShipField.GetValue( encounter ) as PD_AlienShip[];

		var getModel = typeof( Encounter ).GetMethod( "GetAlienShipModel", BindingFlags.Instance | BindingFlags.Public );

		var text = "ships[";

		for ( var i = 0; i < alienShipList.Length; i++ )
		{
			var ship = alienShipList[ i ];

			text += i + ":" + ( ship.m_addedToEncounter ? "A" : "-" ) + ( ship.m_isDead ? "D" : "-" );

			if ( getModel != null )
			{
				var model = getModel.Invoke( encounter, new object[] { i } ) as GameObject;

				text += "@" + ( ( model == null ) ? "none" : Array.IndexOf( encounter.m_alienShipModelList, model ).ToString() );
			}

			text += " ";
		}

		text += "] slots[";

		for ( var slot = 0; slot < encounter.m_alienShipModelList.Length; slot++ )
		{
			var model = encounter.m_alienShipModelList[ slot ];

			if ( !model.activeSelf )
			{
				continue;
			}

			var ship = ( slotShips != null ) ? slotShips[ slot ] : null;
			var shipIndex = ( ship == null ) ? "NULL" : Array.IndexOf( alienShipList, ship ).ToString();
			var debris = ( model.GetComponentInChildren<DebrisTumble>( false ) != null ) ? "debris" : "ship";

			text += slot + ":" + debris + "(ship " + shipIndex + ") ";
		}

		return text + "]";
	}

	// ---------------------------------------------------------------- scenarios

	// H6: 6 ships, 3 at a time. Kill wave 1, leave, come back, kill ship 3.
	IEnumerator ScenarioH6()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;

		// star system encounters are the ones that come in waves (6 ships, 3 at a time)
		var encounterId = FindEncounter( 1, 6, 3, 0 );

		if ( encounterId < 0 )
		{
			Finish( "abort: no suitable encounter", 2 );
			yield break;
		}

		var gdEncounter = gameData.m_encounterList[ encounterId ];

		Log( "encounter id=" + encounterId + " race=" + gdEncounter.m_race + " ships=" + gdEncounter.m_maxNumShips + " atOnce=" + gdEncounter.m_maxNumShipsAtOnce );

		EnterEncounter( encounterId );
		yield return Frames( 10 );
		Log( "first entry: location=" + playerData.m_general.m_location + " " + Dump() );

		Kill( 0 );
		Kill( 1 );
		Kill( 2 );
		yield return Frames( 10 );
		Log( "wave 1 dead: " + Dump() );

		var exceptionsAfterWave1 = s_exceptionCount;

		LeaveEncounter();
		yield return Frames( 10 );
		Log( "left: location=" + playerData.m_general.m_location );

		if ( playerData.m_general.m_location != PD_General.Location.Encounter )
		{
			EnterEncounter( encounterId );
		}

		yield return Frames( 10 );
		Log( "re-entered: location=" + playerData.m_general.m_location + " " + Dump() );

		var exceptionsBeforeKill = s_exceptionCount;

		Kill( 3 );
		yield return Frames( 30 );
		Log( "ship 3 dead: " + Dump() );

		var exceptionsAfterKill = s_exceptionCount - exceptionsBeforeKill;

		Finish( "scenario=h6 exceptionsUpToWave1=" + exceptionsAfterWave1 + " exceptionsBeforeKill=" + exceptionsBeforeKill + " exceptionsIn30FramesAfterKillingShip3=" + exceptionsAfterKill, 0 );
	}

	// M15: pick a target in one encounter, then enter a different encounter and look at the target
	IEnumerator ScenarioM15()
	{
		var playerData = DataController.m_instance.m_playerData;
		var combatController = CombatController.m_instance;

		var firstId = FindEncounter( 1, 6, 3, 0 );
		var secondId = FindEncounter( 1, 6, 3, 1 );

		if ( ( firstId < 0 ) || ( secondId < 0 ) )
		{
			Finish( "abort: no suitable encounters", 2 );
			yield break;
		}

		EnterEncounter( firstId );
		yield return Frames( 10 );

		combatController.SetTarget( 2 );
		Log( "first encounter " + firstId + ": target=" + combatController.GetTargetIndex() + " " + Dump() );

		LeaveEncounter();
		yield return Frames( 10 );

		if ( ( playerData.m_general.m_location != PD_General.Location.Encounter ) || ( playerData.m_general.m_currentEncounterId != secondId ) )
		{
			EnterEncounter( secondId );
		}

		yield return Frames( 10 );

		var targetAfterEntry = combatController.GetTargetIndex();

		Log( "second encounter " + playerData.m_general.m_currentEncounterId + ": target=" + targetAfterEntry + " " + Dump() );

		// point the target at a ship that is not in the encounter and try to fire at it
		playerData.m_playerShip.m_laserCannonClass = 5;
		combatController.SetTarget( 5 );

		var alienShipList = SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList();
		var hidden = alienShipList[ 5 ];

		Log( "ship 5 before firing: added=" + hidden.m_addedToEncounter + " dead=" + hidden.m_isDead );

		var fired = combatController.FirePlayerLaser();
		yield return Frames( 5 );

		Log( "fired=" + fired + " ship 5 after firing: added=" + hidden.m_addedToEncounter + " dead=" + hidden.m_isDead + " target=" + combatController.GetTargetIndex() + " " + Dump() );

		// positive control: a living, added ship within laser range must still be hittable
		var visible = alienShipList[ 0 ];

		visible.m_coordinates = new Vector3( 100.0f, 0.0f, 0.0f );
		combatController.SetTarget( 0 );

		var firedAtVisible = combatController.FirePlayerLaser();
		yield return Frames( 5 );

		Log( "ship 0 (added, alive, in range): fired=" + firedAtVisible + " dead=" + visible.m_isDead + " target=" + combatController.GetTargetIndex() + " " + Dump() );

		Finish( "scenario=m15 targetCarriedOver=" + ( targetAfterEntry >= 0 ) + " firedAtHiddenShip=" + fired + " hiddenShipKilled=" + hidden.m_isDead + " firedAtVisibleShip=" + firedAtVisible + " visibleShipKilled=" + visible.m_isDead, 0 );
	}
	// ---------------------------------------------------------------- reflection and check helpers

	const BindingFlags c_any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	static object GetField( object target, string name )
	{
		return target.GetType().GetField( name, c_any ).GetValue( target );
	}

	static void SetField( object target, string name, object value )
	{
		target.GetType().GetField( name, c_any ).SetValue( target, value );
	}

	static object Call( object target, string name, params object[] args )
	{
		var method = target.GetType().GetMethod( name, c_any );

		try
		{
			return method.Invoke( target, args );
		}
		catch ( TargetInvocationException exception )
		{
			s_exceptionCount++;

			Log( name + " threw " + exception.InnerException.GetType().Name + ": " + exception.InnerException.Message );

			return null;
		}
	}

	static int s_checksPassed;
	static int s_checksFailed;

	static void Check( string name, bool passed, string detail )
	{
		if ( passed )
		{
			s_checksPassed++;
		}
		else
		{
			s_checksFailed++;
		}

		Log( "CHECK " + ( passed ? "PASS" : "FAIL" ) + " " + name + " | " + detail );
	}

	// a ship in flight always has every role filled (the docking bay refuses to launch otherwise), so give the new game a crew
	static void EnsureCrew()
	{
		var playerData = DataController.m_instance.m_playerData;

		if ( playerData.m_personnel.m_personnelList.Count > 0 )
		{
			return;
		}

		var file = playerData.m_personnel.CreateNewPersonnel();

		file.m_name = "Probe";
		file.m_crewRaceId = 0;
		file.m_vitality = 100.0f;
		file.m_science = file.m_navigation = file.m_engineering = file.m_communications = file.m_medicine = 100;

		playerData.m_personnel.m_personnelList.Add( file );

		for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
		{
			playerData.m_crewAssignment.Assign( role, file.m_fileId );
		}
	}

	static string VesselIds()
	{
		var text = "";

		foreach ( var ship in SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList() )
		{
			text += ship.m_vesselId + " ";
		}

		return text.Trim();
	}

	static T FindPanel<T>() where T : Panel
	{
		foreach ( var panel in Resources.FindObjectsOfTypeAll<T>() )
		{
			if ( panel.gameObject.scene.IsValid() )
			{
				return panel;
			}
		}

		return null;
	}

	IEnumerator OpenPanel( Panel panel, string label )
	{
		var before = s_exceptionCount;

		try
		{
			PanelController.m_instance.Open( panel );
		}
		catch ( Exception exception )
		{
			s_exceptionCount++;

			Log( label + ": Open threw " + exception.GetType().Name + ": " + exception.Message );
		}

		yield return new WaitForSecondsRealtime( 1.5f );

		Log( label + ": after opening - active panel=" + PanelController.m_instance.HasActivePanel() + " exceptions=" + ( s_exceptionCount - before ) );
	}

	IEnumerator ClosePanel( Panel panel, string label )
	{
		if ( PanelController.m_instance.HasActivePanel() )
		{
			try
			{
				PanelController.m_instance.Close();
			}
			catch ( Exception exception )
			{
				s_exceptionCount++;

				Log( label + ": Close threw " + exception.GetType().Name + ": " + exception.Message );
			}

			yield return new WaitForSecondsRealtime( 1.5f );

			if ( PanelController.m_instance.HasActivePanel() )
			{
				Log( label + ": the close animation did not report back - finishing the close by hand" );

				try
				{
					PanelController.m_instance.Closed();
				}
				catch ( Exception exception )
				{
					s_exceptionCount++;

					Log( label + ": Closed threw " + exception.GetType().Name + ": " + exception.Message );
				}
			}
		}
		else
		{
			// the panel never became the active panel (its Open threw) - just hide it
			panel.gameObject.SetActive( false );
		}
	}

	// ---------------------------------------------------------------- batch 1 (spaceflight side)

	IEnumerator ScenarioBatch1()
	{
		var dataController = DataController.m_instance;
		var gameData = dataController.m_gameData;
		var playerData = dataController.m_playerData;
		var spaceflightController = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;

		EnsureCrew();

		// ---- H1: the same encounter id must give the same encounter however the list is ordered
		const int encounterId = 115;

		var gdEncounter = gameData.m_encounterList[ encounterId ];

		Array.Reverse( playerData.m_encounterList );

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		var id1 = spaceflightController.m_encounter.m_pdEncounter.m_encounterId;
		var vessels1 = VesselIds();
		var atIndex1 = playerData.m_encounterList[ encounterId ].m_encounterId;

		LeaveEncounter();
		yield return Frames( 10 );

		// rotate the list by one so the order is different again
		var list = playerData.m_encounterList;
		var first = list[ 0 ];
		Array.Copy( list, 1, list, 0, list.Length - 1 );
		list[ list.Length - 1 ] = first;

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		var id2 = spaceflightController.m_encounter.m_pdEncounter.m_encounterId;
		var vessels2 = VesselIds();
		var atIndex2 = playerData.m_encounterList[ encounterId ].m_encounterId;

		var vesselsBelong = true;

		foreach ( var alienShip in spaceflightController.m_encounter.m_pdEncounter.GetAlienShipList() )
		{
			if ( ( alienShip.m_vesselId != gdEncounter.m_vesselIdA ) && ( alienShip.m_vesselId != gdEncounter.m_vesselIdB ) && ( alienShip.m_vesselId != gdEncounter.m_vesselIdC ) )
			{
				vesselsBelong = false;
			}
		}

		Check( "H1 encounter found by id, not by list position", ( id1 == encounterId ) && ( id2 == encounterId ) && ( vessels1 == vessels2 ) && vesselsBelong,
			"entered " + encounterId + " twice: got ids " + id1 + " and " + id2 + ", vessels [" + vessels1 + "] and [" + vessels2 + "], vessels belong to the encounter=" + vesselsBelong + ". Indexing the list by id would have given encounters " + atIndex1 + " and " + atIndex2 );

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- H2 and PR 2: alien comms and comm progress survive a save and a load
		GD_Comm comm = null;

		foreach ( var candidate in gameData.m_commList )
		{
			var index = (int) candidate.m_subject - (int) GD_Comm.Subject.Themselves;

			if ( ( index >= 0 ) && ( index < (int) PD_ShipsLog.AlienComm.Count ) )
			{
				comm = candidate;
				break;
			}
		}

		var subject = (PD_ShipsLog.AlienComm) ( (int) comm.m_subject - (int) GD_Comm.Subject.Themselves );

		playerData.m_shipsLog.AddAlienComm( comm, "probe message" );
		playerData.m_general.SetLastCommId( comm.m_race, comm.m_subject, 42 );

		var countBefore = playerData.m_shipsLog.GetAlienComms( subject ).Count;

		// save through the game's own save path, then load the slot back the way the data controller does
		dataController.SaveActiveGame();

		var loaded = new MemorySaveSystem().Load<PlayerData>( dataController.m_playerDataFileName, dataController.m_activeSaveGameSlotNumber );

		var json = MemorySaveSystem.s_slots[ dataController.m_activeSaveGameSlotNumber ];
		var countAfter = loaded.m_shipsLog.GetAlienComms( subject ).Count;
		var messageAfter = ( countAfter > 0 ) ? loaded.m_shipsLog.GetAlienComms( subject )[ countAfter - 1 ].m_message : "";

		Check( "H2 alien comms survive save and load", ( countBefore >= 1 ) && ( countAfter == countBefore ) && ( messageAfter == "probe message" ),
			"subject " + subject + ": " + countBefore + " entries before, " + countAfter + " after, last message '" + messageAfter + "', save is " + json.Length + " chars and contains m_alienComms=" + json.Contains( "m_alienComms" ) );

		Check( "PR2 comm progress survives save and load", loaded.m_general.GetLastCommId( comm.m_race, comm.m_subject ) == 42,
			"race " + comm.m_race + " subject " + comm.m_subject + ": stored 42, loaded " + loaded.m_general.GetLastCommId( comm.m_race, comm.m_subject ) );

		// a save from before the comms were saved has no m_alienComms at all
		loaded.m_shipsLog.m_alienComms = null;

		var oldSaveResult = "ok";
		var oldSaveCount = -1;

		try
		{
			oldSaveCount = loaded.m_shipsLog.GetAlienComms( subject ).Count;
		}
		catch ( Exception exception )
		{
			oldSaveResult = exception.GetType().Name;
		}

		Check( "H2 a save without alien comms still loads", ( oldSaveResult == "ok" ) && ( oldSaveCount == 0 ), "result " + oldSaveResult + ", entries " + oldSaveCount );

		// the five ships log buttons that used to throw after a load
		var exceptionsBeforeButtons = s_exceptionCount;
		var buttonResult = "ok";

		try
		{
			new ACThemselvesButton().Execute();
			new ACOtherRacesButton().Execute();
			new ACGeneralInfoButton().Execute();
			new ACOldEmpireButton().Execute();
			new ACTheAncientsButton().Execute();
		}
		catch ( Exception exception )
		{
			buttonResult = exception.GetType().Name + ": " + exception.Message;
		}

		yield return Frames( 5 );

		Check( "H2 alien comms buttons open the ships log", ( buttonResult == "ok" ) && ( s_exceptionCount == exceptionsBeforeButtons ), "result " + buttonResult + ", exceptions " + ( s_exceptionCount - exceptionsBeforeButtons ) );

		spaceflightController.m_shipsLog.Hide();

		// ---- H4: storage can't go negative
		ship.AddElement( 6, 10 );
		ship.m_elementStorage.Remove( 6, 25 );

		var leftover = ship.m_elementStorage.Find( 6 );

		ship.m_elementStorage.Remove( 99, 1 );
		ship.RecalculateVolumeUsed();

		Check( "H4 removing more than is stored does not go negative", ( leftover == null ) && ( ship.m_elementStorage.m_volumeUsed >= 0 ),
			"had 10, removed 25: element left=" + ( ( leftover == null ) ? "none" : leftover.m_volume.ToString() ) + ", storage volume used=" + ship.m_elementStorage.m_volumeUsed );

		// ---- M6: shields need fuel
		ship.m_shieldingClass = 1;
		ship.m_shieldsAreUp = false;

		var fuel = ship.m_elementStorage.Find( 5 );

		if ( fuel != null )
		{
			ship.RemoveElement( 5, fuel.m_volume );
		}

		var exceptionsBeforeShields = s_exceptionCount;
		var shieldButtonResult = "ok";

		try
		{
			new RaiseShieldsButton().Execute();
		}
		catch ( Exception exception )
		{
			shieldButtonResult = exception.GetType().Name;
		}

		var raisedWithoutFuel = ship.m_shieldsAreUp;

		// a save can already have the shields up with an empty tank - fuel use must not throw
		ship.m_shieldsAreUp = true;

		var fuelUseResult = "ok";

		try
		{
			ship.UseUpFuel( 0.25f );
		}
		catch ( Exception exception )
		{
			fuelUseResult = exception.GetType().Name;
		}

		var droppedWhenEmpty = !ship.m_shieldsAreUp;

		// with fuel on board the button works
		ship.AddElement( 5, 50 );

		try
		{
			new RaiseShieldsButton().Execute();
		}
		catch ( Exception exception )
		{
			shieldButtonResult = exception.GetType().Name;
		}

		var raisedWithFuel = ship.m_shieldsAreUp;

		Check( "M6 shields need endurium", !raisedWithoutFuel && ( fuelUseResult == "ok" ) && droppedWhenEmpty && raisedWithFuel && ( shieldButtonResult == "ok" ) && ( s_exceptionCount == exceptionsBeforeShields ),
			"raised with no fuel=" + raisedWithoutFuel + ", fuel use with shields up and no fuel=" + fuelUseResult + ", shields dropped=" + droppedWhenEmpty + ", raised with fuel=" + raisedWithFuel + ", button=" + shieldButtonResult );

		ship.DropShields();

		// ---- M2: the missile immunity key loads
		var immune = "";
		var immuneCount = 0;

		foreach ( var vessel in gameData.m_vesselList )
		{
			if ( vessel.m_immuneToMissiles )
			{
				immuneCount++;
				immune += vessel.m_id + " ";
			}
		}

		Check( "M2 missile immunity loads from the game data", immuneCount > 0, immuneCount + " vessels immune to missiles: ids " + immune.Trim() );

		// ---- M9: quitting saves, unless the ship has been destroyed
		var quit = typeof( DataController ).GetMethod( "OnApplicationQuit", c_any );

		var saves0 = MemorySaveSystem.s_saveCalls;

		quit.Invoke( dataController, null );

		var saves1 = MemorySaveSystem.s_saveCalls;
		var armor = ship.m_armorPoints;

		ship.m_armorPoints = 0;

		quit.Invoke( dataController, null );

		var saves2 = MemorySaveSystem.s_saveCalls;

		ship.m_armorPoints = armor;

		Check( "M9 quitting saves unless the ship is destroyed", ( saves1 == saves0 + 1 ) && ( saves2 == saves1 ), "saves on quit with a living ship=" + ( saves1 - saves0 ) + ", with a destroyed ship=" + ( saves2 - saves1 ) );

		Finish( "scenario=batch1 passed=" + s_checksPassed + " failed=" + s_checksFailed, 0 );
	}

	// ---------------------------------------------------------------- M13: a destroyed encounter must not chase the player or start again

	IEnumerator ScenarioM13()
	{
		var playerData = DataController.m_instance.m_playerData;
		var spaceflightController = SpaceflightController.m_instance;

		EnsureCrew();

		var encounterId = FindEncounter( 1, 6, 3, 0 );
		var pdEncounter = playerData.FindEncounter( encounterId );

		// destroy all six ships over two visits
		EnterEncounter( encounterId );
		yield return Frames( 10 );

		Kill( 0 );
		Kill( 1 );
		Kill( 2 );
		yield return Frames( 5 );

		LeaveEncounter();
		yield return Frames( 10 );

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		Kill( 3 );
		Kill( 4 );
		Kill( 5 );
		yield return Frames( 5 );

		LeaveEncounter();
		yield return Frames( 10 );

		var living = 0;

		foreach ( var alienShip in pdEncounter.GetAlienShipList() )
		{
			if ( !alienShip.m_isDead )
			{
				living++;
			}
		}

		Log( "encounter " + encounterId + ": living ships=" + living + " location=" + playerData.m_general.m_location );

		// put the dead encounter in the player's star system, inside the aliens' radar range but outside the encounter range
		pdEncounter.m_starId = playerData.m_general.m_currentStarId;

		var radarDistance = spaceflightController.m_alienStarSystemRadarDistance;
		var encounterRange = spaceflightController.m_encounterRange;

		pdEncounter.SetCoordinates( playerData.m_general.m_coordinates + Vector3.forward * ( encounterRange + ( radarDistance - encounterRange ) * 0.5f ) );

		var startCoordinates = pdEncounter.m_currentCoordinates;

		yield return new WaitForSecondsRealtime( 1.0f );

		var moved = Vector3.Distance( startCoordinates, pdEncounter.m_currentCoordinates );
		var radarSees = pdEncounter.GetDistance() < float.MaxValue;

		Log( "radar range " + radarDistance + ", encounter range " + encounterRange + ": dead encounter moved " + moved.ToString( "F1" ) + " in one second, visible to the radar=" + radarSees + ", location=" + playerData.m_general.m_location );

		// now put it right on top of the player
		if ( playerData.m_general.m_location != PD_General.Location.Encounter )
		{
			pdEncounter.SetCoordinates( playerData.m_general.m_coordinates );

			yield return new WaitForSecondsRealtime( 1.0f );
		}

		var retriggered = playerData.m_general.m_location == PD_General.Location.Encounter;

		Finish( "scenario=m13 livingShips=" + living + " deadEncounterMoved=" + ( moved > 0.01f ) + " radarSeesIt=" + radarSees + " retriggered=" + retriggered, 0 );
	}

	// ---------------------------------------------------------------- M14: a destroyed ship must stop moving

	IEnumerator ScenarioM14()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var encounter = SpaceflightController.m_instance.m_encounter;

		EnsureCrew();

		var result = "scenario=m14";

		// a spemin encounter (its ships have debris models), then an elowan one (no debris models)
		foreach ( var race in new[] { GameData.Race.Spemin, GameData.Race.Elowan } )
		{
			var encounterId = FindEncounter( 1, 6, 3, 0, race );

			EnterEncounter( encounterId );
			yield return Frames( 10 );

			var alienShip = encounter.m_pdEncounter.GetAlienShipList()[ 0 ];
			var model = encounter.m_alienShipModelList[ 0 ];

			// put the ship well away from the player so it has somewhere to fly to
			alienShip.m_coordinates = new Vector3( 1500.0f, 0.0f, 0.0f );
			yield return Frames( 5 );

			Kill( 0 );
			yield return Frames( 5 );

			var shipStart = alienShip.m_coordinates;
			var modelStart = model.transform.position;

			yield return new WaitForSecondsRealtime( 1.0f );

			var shipMoved = Vector3.Distance( shipStart, alienShip.m_coordinates );
			var modelMoved = Vector3.Distance( modelStart, model.transform.position );
			var hasDebris = model.GetComponentInChildren<DebrisTumble>( false ) != null;

			// a living ship in the same encounter must still move
			var other = encounter.m_pdEncounter.GetAlienShipList()[ 1 ];
			var otherStart = other.m_coordinates;

			yield return new WaitForSecondsRealtime( 0.5f );

			var otherMoved = Vector3.Distance( otherStart, other.m_coordinates );

			Log( race + " encounter " + encounterId + " (vessel " + alienShip.m_vesselId + "): dead ship moved " + shipMoved.ToString( "F1" ) + ", its model moved " + modelMoved.ToString( "F1" ) + ", model visible=" + model.activeSelf + ", shows debris=" + hasDebris + "; a living ship moved " + otherMoved.ToString( "F1" ) );

			result += " " + race + ":deadShipMoved=" + ( shipMoved > 0.01f ) + ",modelVisible=" + model.activeSelf + ",debris=" + hasDebris + ",livingShipMoved=" + ( otherMoved > 0.01f );

			LeaveEncounter();
			yield return Frames( 10 );
		}

		Finish( result, 0 );
	}

	// ---------------------------------------------------------------- M7: scans keep the real scan type when the picture is missing

	static string MessagesText()
	{
		var ui = GetField( SpaceflightController.m_instance.m_messages, "m_messagesUI" );

		var text = ui.GetType().GetProperty( "text" ).GetValue( ui ) as string;

		return ( text ?? "" ).Replace( '\n', '/' );
	}

	IEnumerator ScenarioM7()
	{
		var gameData = DataController.m_instance.m_gameData;
		var spaceflightController = SpaceflightController.m_instance;
		var sensors = spaceflightController.m_displayController.m_sensorsDisplay;

		EnsureCrew();

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		yield return Frames( 3 );

		// every vessel scan type should keep its identity
		var changed = "";

		for ( var vesselId = 1; vesselId < gameData.m_vesselList.Length; vesselId++ )
		{
			sensors.StartScanning( (SensorsDisplay.ScanType) vesselId, 1, 100, 0, 0 );

			if ( (int) sensors.m_scanType != vesselId )
			{
				changed += vesselId + " ";
			}
		}

		sensors.StartScanning( SensorsDisplay.ScanType.Debris, 1, 100, 0, 40 );

		var debrisKept = sensors.m_scanType == SensorsDisplay.ScanType.Debris;

		Log( "vessel scan types that turned into something else: [" + changed.Trim() + "], debris stays debris=" + debrisKept );

		// a complete scan of an elowan scout, then the science officer's analysis (a science officer of full skill, who gives every line: roadmap 1.7)
		DataController.m_instance.m_playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.ScienceOfficer ).m_science = 250;

		var scanType = SensorsDisplay.ScanType.ElowanScout;
		var vessel = gameData.m_vesselList[ (int) scanType ];

		sensors.StartScanning( scanType, 1, vessel.m_mass, 0, 0 );

		yield return new WaitForSecondsRealtime( sensors.m_minDuration + 1.5f );

		var readout = MessagesText();
		var analysisResult = "ok";

		try
		{
			new AnalysisButton().Execute();
		}
		catch ( Exception exception )
		{
			analysisResult = exception.GetType().Name;
		}

		yield return Frames( 3 );

		var analysis = MessagesText();

		Log( "sensor readout: " + readout );
		Log( "analysis: " + analysis );

		Finish( "scenario=m7 scanTypesChanged=[" + changed.Trim() + "] debrisKept=" + debrisKept + " scanFinished=" + sensors.m_hasSensorData + " analysisSaysUnknown=" + analysis.Contains( "Unknown" ) + " analysisNamesTheVessel=" + analysis.Contains( vessel.m_object ) + " analysisResult=" + analysisResult, 0 );
	}

	// ---------------------------------------------------------------- sensor pictures: which picture each kind of scan puts in the sensor window

	// ---------------------------------------------------------------- the planet's own surface in the sensor window

	// the material the sensor picture is drawn with: its shader, the albedo map on it, how far the noise has faded into the planet, how far it has turned
	static string SensorMaterial( SensorsDisplay sensors )
	{
		var material = sensors.m_maskImage.material;

		if ( material == null )
		{
			return "material=null";
		}

		var planetTexture = material.HasProperty( "_PlanetTex" ) ? material.GetTexture( "_PlanetTex" ) : null;
		var reveal = material.HasProperty( "_Reveal" ) ? material.GetFloat( "_Reveal" ).ToString( "F3" ) : "-";
		var spin = material.HasProperty( "_Spin" ) ? material.GetFloat( "_Spin" ).ToString( "F5" ) : "-";

		return "shader=" + material.shader.name + " planetTex=" + ( ( planetTexture != null ) ? planetTexture.name + "#" + planetTexture.GetInstanceID() : "none" ) + " reveal=" + reveal + " spin=" + spin;
	}

	static bool ShowsPlanetSurface( SensorsDisplay sensors )
	{
		var material = sensors.m_maskImage.material;

		return ( material != null ) && ( material.shader.name == "Custom/UI/Sensor Planet" );
	}

	static bool ShowsNoise( SensorsDisplay sensors )
	{
		var material = sensors.m_maskImage.material;

		return ( material != null ) && ( material.shader.name == "Custom/UI/Masked Image" );
	}

	IEnumerator ScenarioSensorPlanet()
	{
		var spaceflightController = SpaceflightController.m_instance;
		var sensors = spaceflightController.m_displayController.m_sensorsDisplay;
		var starSystem = spaceflightController.m_starSystem;
		var gameData = DataController.m_instance.m_gameData;

		EnsureCrew();

		var exceptionsBefore = s_exceptionCount;

		// ---- a planet that has its maps (94, the small rock planet of the Arth system): its own surface fades in out of the noise and keeps turning
		yield return EnterOrbit( 94 );

		var planetController = starSystem.GetPlanetController( 94 );
		var hasMaps = ( planetController != null ) && planetController.HasMaps();
		var albedo = hasMaps ? planetController.GetPlanetGenerator().m_albedoTexture : null;
		var planet = gameData.m_planetList[ 94 ];

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		yield return Frames( 3 );

		sensors.m_hasSensorData = false;
		sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, planet.m_mass, planet.m_bioDensity, planet.m_mineralDensity );
		yield return Frames( 2 );

		var atStart = SensorMaterial( sensors );
		var planetTexture = ShowsPlanetSurface( sensors ) ? sensors.m_maskImage.material.GetTexture( "_PlanetTex" ) : null;

		Check( "a planet with maps is drawn with the sensor planet shader", hasMaps && ShowsPlanetSurface( sensors ), "hasMaps=" + hasMaps + " " + atStart );
		Check( "the picture has the planet's own albedo map", ( albedo != null ) && ( planetTexture == albedo ), "albedo=" + ( ( albedo != null ) ? albedo.GetInstanceID().ToString() : "none" ) + " " + atStart );
		Check( "the scan starts as the noise", ShowsPlanetSurface( sensors ) && ( sensors.m_maskImage.material.GetFloat( "_Reveal" ) < 0.2f ), atStart );
		Check( "the planet's disc keeps the size the original gives it", Mathf.Approximately( sensors.m_maskImage.transform.localScale.y, sensors.GetPlanetPictureScale( 18, planet.m_mass ) ), "scale=" + sensors.m_maskImage.transform.localScale.y + " expected=" + sensors.GetPlanetPictureScale( 18, planet.m_mass ) );

		var until = Time.realtimeSinceStartup + sensors.m_maxDuration + 5.0f;

		while ( !sensors.m_hasSensorData && ( Time.realtimeSinceStartup < until ) )
		{
			yield return null;
		}

		yield return Frames( 2 );

		var atEnd = SensorMaterial( sensors );
		var spinAtEnd = ShowsPlanetSurface( sensors ) ? sensors.m_maskImage.material.GetFloat( "_Spin" ) : -1.0f;

		Check( "at the end of the scan the planet has replaced the noise", sensors.m_hasSensorData && ShowsPlanetSurface( sensors ) && Mathf.Approximately( sensors.m_maskImage.material.GetFloat( "_Reveal" ), 1.0f ), "finished=" + sensors.m_hasSensorData + " " + atEnd );
		Check( "control: the readout is still there", sensors.m_massText.text.Contains( "Mass: " ) && sensors.m_bioMinText.text.Contains( "Min: " ), "mass=[" + sensors.m_massText.text + "] bioMin=[" + sensors.m_bioMinText.text + "]" );

		// it turns once the scan is over
		var turnUntil = Time.realtimeSinceStartup + 1.5f;

		while ( Time.realtimeSinceStartup < turnUntil )
		{
			yield return null;
		}

		var spinLater = ShowsPlanetSurface( sensors ) ? sensors.m_maskImage.material.GetFloat( "_Spin" ) : -1.0f;

		Check( "the planet keeps turning after the scan", ( spinAtEnd >= 0.0f ) && ( spinLater > spinAtEnd ), "spin at the end " + spinAtEnd.ToString( "F5" ) + ", 1.5 s later " + spinLater.ToString( "F5" ) );

		// and stands still while the game is paused
		spaceflightController.m_gameIsPaused = true;

		var spinPaused = ShowsPlanetSurface( sensors ) ? sensors.m_maskImage.material.GetFloat( "_Spin" ) : -1.0f;

		yield return Frames( 20 );

		var spinPausedLater = ShowsPlanetSurface( sensors ) ? sensors.m_maskImage.material.GetFloat( "_Spin" ) : -1.0f;

		spaceflightController.m_gameIsPaused = false;

		Check( "the planet stands still while the game is paused", ( spinPaused >= 0.0f ) && Mathf.Approximately( spinPaused, spinPausedLater ), "spin " + spinPaused.ToString( "F5" ) + " then " + spinPausedLater.ToString( "F5" ) );

		// ---- a vessel scan is the noise, as before (control)
		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.SpeminScout, 1, 400, 100, 100 );
		yield return Frames( 2 );

		Check( "control: a vessel scan is drawn with the noise", ShowsNoise( sensors ) && PictureShown( sensors ), SensorMaterial( sensors ) + " | " + SensorPicture( sensors ) );

		// ---- the maps of the planet in the picture are destroyed (the ship leaves the star system): the picture goes back to the noise instead of a missing texture
		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, planet.m_mass, planet.m_bioDensity, planet.m_mineralDensity );
		yield return Frames( 2 );

		var beforeRelease = ShowsPlanetSurface( sensors );

		if ( planetController != null )
		{
			planetController.GetPlanetGenerator().Release();
		}

		yield return Frames( 3 );

		Check( "a picture whose maps are destroyed goes back to the noise", beforeRelease && ShowsNoise( sensors ), "planet surface before " + beforeRelease + ", now " + SensorMaterial( sensors ) );

		// ---- a planet whose maps could not be generated (planet 90 is given a file that cannot be read) is the noise, not its grey placeholder ball
		var garbage = new byte[ 4096 ];

		new System.Random( 12345 ).NextBytes( garbage );

		RegeneratePlanets();

		var badPlanet = starSystem.GetPlanetController( 90 );

		StartProcessing( badPlanet.GetPlanetGenerator(), garbage );

		var start = Time.realtimeSinceStartup;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 30.0f ) )
		{
			yield return null;
		}

		yield return EnterOrbit( 90 );

		var arthSeven = gameData.m_planetList[ 90 ];

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, arthSeven.m_mass, arthSeven.m_bioDensity, arthSeven.m_mineralDensity );
		yield return Frames( 2 );

		Check( "a planet that could not be mapped is drawn with the noise", badPlanet.CouldNotBeMapped() && ShowsNoise( sensors ) && PictureShown( sensors ), "could not be mapped " + badPlanet.CouldNotBeMapped() + " " + SensorMaterial( sensors ) );

		// ---- and a planet with maps after it shows its surface again
		yield return EnterOrbit( 94 );

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, planet.m_mass, planet.m_bioDensity, planet.m_mineralDensity );
		yield return Frames( 2 );

		var newAlbedo = starSystem.GetPlanetController( 94 ).HasMaps() ? starSystem.GetPlanetController( 94 ).GetPlanetGenerator().m_albedoTexture : null;

		Check( "after it a planet with maps shows its own surface again", ShowsPlanetSurface( sensors ) && ( newAlbedo != null ) && ( sensors.m_maskImage.material.GetTexture( "_PlanetTex" ) == newAlbedo ), SensorMaterial( sensors ) );

		var exceptions = s_exceptionCount - exceptionsBefore;

		Check( "nothing throws", exceptions == 0, "exceptions " + exceptions );

		Finish( "scenario=sensorplanet start=[" + atStart + "] end=[" + atEnd + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	IEnumerator ScenarioSensorPictures()
	{
		var spaceflightController = SpaceflightController.m_instance;
		var sensors = spaceflightController.m_displayController.m_sensorsDisplay;

		EnsureCrew();

		// a planet scan needs a planet to orbit (94 is the small rock planet of the Arth system)
		yield return EnterOrbit( 94 );

		var planet = DataController.m_instance.m_gameData.m_planetList[ 94 ];

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		yield return Frames( 3 );

		sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, planet.m_mass, planet.m_bioDensity, planet.m_mineralDensity );
		yield return Frames( 2 );

		Check( "planet scan uses the planet mask", MaskName( sensors ) == "Sensors - Planet Mask", SensorPicture( sensors ) );
		Check( "planet scan shows the planet background", BackgroundName( sensors ) == "Sensors - Planet", SensorPicture( sensors ) );
		Check( "planet scan shows a picture", PictureShown( sensors ), SensorPicture( sensors ) );

		// control: a planet reads bio and minerals (the readout is written every frame while the scan runs)
		Check( "planet readout reads minerals, with no percent sign", sensors.m_bioMinText.text.Contains( "Min: " ) && !sensors.m_bioMinText.text.Contains( "Energy" ) && !sensors.m_bioMinText.text.Contains( "%" ), "readout=[" + sensors.m_bioMinText.text + "]" );

		// the planet's disc is as high in the window as the original draws it (measured in the original on 2026-10-10 and 2026-10-11, in a window 57 pixels high
		// whose pixels are 1.2 times as high as wide: 6x10^19, 2x10^20, 6x10^20 and 1x10^21 tons 10 pixels across, 3x10^21 14, 6x10^21 26, 7x10^21 30,
		// 9x10^21 38, 10^22 tons and up 62)
		var discMasses = new int[] { 60, 200, 600, 1000, 3000, 6000, 7000, 9000, 10000, 400000 };
		var discPixels = new float[] { 10.0f, 10.0f, 10.0f, 10.0f, 14.0f, 26.0f, 30.0f, 38.0f, 62.0f, 62.0f };
		var discText = "";
		var discsRight = 0;

		for ( var i = 0; i < discMasses.Length; i++ )
		{
			spaceflightController.m_displayController.ChangeDisplay( sensors );
			sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, discMasses[ i ], planet.m_bioDensity, planet.m_mineralDensity );
			yield return Frames( 2 );

			var expected = discPixels[ i ] / ( 57.0f * 1.2f );
			var measured = PlanetDiscFraction( sensors );

			discText += discMasses[ i ] + ": " + measured.ToString( "F3" ) + " (original " + expected.ToString( "F3" ) + ")  ";

			if ( Mathf.Abs( measured - expected ) < 0.01f )
			{
				discsRight++;
			}
		}

		Check( "a planet's disc is as high in the window as in the original", discsRight == discMasses.Length, discText );

		// the gas giant in orbit 3 of 123,101 (planet 99) reads 5x10^23 tons in the original (seen 2026-10-10; the survey the game data comes from says 4x10^23)
		var gasGiant = DataController.m_instance.m_gameData.m_planetList[ 99 ];

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.m_hasSensorData = false;
		sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, gasGiant.m_mass, gasGiant.m_bioDensity, gasGiant.m_mineralDensity );

		// the readout counts up to its values while the scan runs (at most m_maxDuration seconds)
		var readoutUntil = Time.realtimeSinceStartup + sensors.m_maxDuration + 5.0f;

		while ( !sensors.m_hasSensorData && ( Time.realtimeSinceStartup < readoutUntil ) )
		{
			yield return null;
		}

		Check( "the gas giant in orbit 3 of 123,101 reads 5x10^23 tons, as in the original", sensors.m_hasSensorData && sensors.m_massText.text.Contains( "5x10<sup>23</sup>" ), "finished=" + sensors.m_hasSensorData + " mass=[" + sensors.m_massText.text + "] gameData=" + gasGiant.m_mass );

		// the frozen planet in orbit 7 of Arth's system (planet 90) reads 6x10^21 tons in the original (seen 2026-10-11; the game data had 4x10^22, the survey 3.6x10^22)
		var arthSeven = DataController.m_instance.m_gameData.m_planetList[ 90 ];

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.m_hasSensorData = false;
		sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, arthSeven.m_mass, arthSeven.m_bioDensity, arthSeven.m_mineralDensity );

		readoutUntil = Time.realtimeSinceStartup + sensors.m_maxDuration + 5.0f;

		while ( !sensors.m_hasSensorData && ( Time.realtimeSinceStartup < readoutUntil ) )
		{
			yield return null;
		}

		Check( "the frozen planet in orbit 7 of Arth's system reads 6x10^21 tons, as in the original", sensors.m_hasSensorData && sensors.m_massText.text.Contains( "6x10<sup>21</sup>" ), "finished=" + sensors.m_hasSensorData + " mass=[" + sensors.m_massText.text + "] gameData=" + arthSeven.m_mass );

		// a vessel that has its own picture (control)
		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.SpeminScout, 1, 400, 100, 100 );
		yield return Frames( 2 );

		Check( "spemin scout scan shows its own picture", PictureShown( sensors ) && ( MaskName( sensors ) == "Sensors - Spemin Scout Mask" ), SensorPicture( sensors ) );

		// the pictures traced from the original by DevTools/SensorPictures: each vessel shows its own
		var tracedPictures = new[]
		{
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.ElowanTransport, "Sensors - Elowan Transport" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.ElowanScout, "Sensors - Elowan Scout" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.ElowanWarship, "Sensors - Elowan Warship" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.ThrynnTransport, "Sensors - Thrynn Transport" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.ThrynnScout, "Sensors - Thrynn Scout" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.ThrynnWarship, "Sensors - Thrynn Warship" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.VeloxiTransport, "Sensors - Velox Transport" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.VeloxiScout, "Sensors - Velox Scout" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.VeloxiWarship, "Sensors - Velox Warship" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.GazurtoidScout, "Sensors - Gazurtoid Scout" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.GazurtoidWarship, "Sensors - Gazurtoid Warship" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.UhlekScout, "Sensors - Uhlek Scout" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.UhlekWarship, "Sensors - Uhlek Warship" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.TheEnterprise, "Sensors - Enterprise" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.NoahTransport, "Sensors - Noah Derelict" ),
		};

		foreach ( var traced in tracedPictures )
		{
			spaceflightController.m_displayController.ChangeDisplay( sensors );
			sensors.StartScanning( traced.Key, 0, 100, 100, 100 );
			yield return Frames( 2 );

			Check( traced.Key + " scan shows its own picture", PictureShown( sensors ) && ( BackgroundName( sensors ) == traced.Value ) && ( MaskName( sensors ) == traced.Value + " Mask" ), SensorPicture( sensors ) );
		}

		// a picture is wider than the magenta panel (640 against 480 units): the window clips it to the panel, so the noise of a wide ship
		// (the enterprise reaches past both sides) does not show beside the window
		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.TheEnterprise, 0, 19000, 100, 100 );
		yield return Frames( 3 );

		var clipWindow = sensors.m_maskImage.transform.parent;
		var clip = clipWindow.GetComponent<UnityEngine.UI.RectMask2D>();
		var clipPanel = clipWindow.Find( "Panel" );
		var clipText = "clip=" + ( clip != null );

		if ( clip != null )
		{
			clipText += " padding=" + clip.padding + " noiseClipped=" + sensors.m_maskImage.canvasRenderer.hasRectClipping;
		}

		if ( clipPanel != null )
		{
			var panelRect = (RectTransform) clipPanel;

			clipText += " panelOffsets=" + panelRect.offsetMin + "/" + panelRect.offsetMax;
		}

		// the clip is the panel's rectangle less the transparent edge of its sprite (the magenta starts 6 sprite pixels in)
		var marginField = typeof( SensorsDisplay ).GetField( "m_panelSpriteMargin" );
		var clipPanelImage = ( clipPanel != null ) ? clipPanel.GetComponent<UnityEngine.UI.Image>() : null;
		var expectedMargin = -1.0f;

		if ( ( marginField != null ) && ( clipPanelImage != null ) && ( clipPanelImage.sprite != null ) )
		{
			expectedMargin = (float) marginField.GetValue( sensors ) * clipPanelImage.canvas.referencePixelsPerUnit / ( clipPanelImage.sprite.pixelsPerUnit * clipPanelImage.pixelsPerUnitMultiplier );
		}

		clipText += " expectedMargin=" + expectedMargin;

		Check( "the window clips its pictures to the magenta inside the panel", ( clip != null ) && ( clipPanel != null ) && ( expectedMargin > 0.0f ) && ( ( clip.padding - new Vector4( ( (RectTransform) clipPanel ).offsetMin.x + expectedMargin, ( (RectTransform) clipPanel ).offsetMin.y + expectedMargin, -( (RectTransform) clipPanel ).offsetMax.x + expectedMargin, -( (RectTransform) clipPanel ).offsetMax.y + expectedMargin ) ).magnitude < 0.01f ), clipText );
		Check( "the noise of a wide picture is clipped", ( clip != null ) && sensors.m_maskImage.canvasRenderer.hasRectClipping, clipText );

		// an object that has no picture (an unknown object; every vessel has one), scanned right after one that has (the window must not keep the last picture)
		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.Unknown, 0, 50, 100, 100 );
		yield return Frames( 2 );

		Check( "unknown object scan (no picture) leaves the window empty", !PictureShown( sensors ), SensorPicture( sensors ) );
		Check( "unknown object scan keeps its scan type", sensors.m_scanType == SensorsDisplay.ScanType.Unknown, "scanType=" + sensors.m_scanType );

		// an empty window is black inside its border (magenta in the original, the UI kit's sensor window colour now), as the original shows an unidentified object
		var window = sensors.m_backgroundImage.transform.parent;
		var emptyTransform = window.Find( "Empty" );
		var panelTransform = window.Find( "Panel" );
		var emptyImage = ( emptyTransform != null ) ? emptyTransform.GetComponent<UnityEngine.UI.Image>() : null;
		var panelImage = ( panelTransform != null ) ? panelTransform.GetComponent<UnityEngine.UI.Image>() : null;
		var emptyText = "empty=" + ( emptyImage != null );

		if ( ( emptyImage != null ) && ( panelImage != null ) )
		{
			var emptySize = emptyImage.rectTransform.rect.size;
			var panelSize = panelImage.rectTransform.rect.size;

			emptyText += " active=" + emptyImage.gameObject.activeInHierarchy + " colour=" + emptyImage.color + " size=" + emptySize + " panel=" + panelSize + " order=" + panelTransform.GetSiblingIndex() + "<" + emptyTransform.GetSiblingIndex() + "<" + sensors.m_backgroundImage.transform.GetSiblingIndex();
		}

		Check( "unknown object scan shows a black window inside its border", ( emptyImage != null ) && ( panelImage != null ) && emptyImage.gameObject.activeInHierarchy && ( emptyImage.color == Color.black ) && ( Mathf.Abs( panelImage.rectTransform.rect.width - emptyImage.rectTransform.rect.width - 32.0f ) < 0.5f ) && ( Mathf.Abs( panelImage.rectTransform.rect.height - emptyImage.rectTransform.rect.height - 32.0f ) < 0.5f ), emptyText );
		Check( "the black inside is drawn over the panel and under the pictures", ( emptyTransform != null ) && ( panelTransform != null ) && ( panelTransform.GetSiblingIndex() < emptyTransform.GetSiblingIndex() ) && ( emptyTransform.GetSiblingIndex() < sensors.m_backgroundImage.transform.GetSiblingIndex() ), emptyText );

		// the original shows no readout for an unidentified object
		Check( "unknown object scan shows no readout", !sensors.m_massText.gameObject.activeInHierarchy && !sensors.m_bioMinText.gameObject.activeInHierarchy, "mass=" + sensors.m_massText.gameObject.activeInHierarchy + " bio=" + sensors.m_bioMinText.gameObject.activeInHierarchy );

		// a scan with a picture, and a display that has just been shown, keep the coloured window
		sensors.StartScanning( SensorsDisplay.ScanType.SpeminScout, 1, 400, 100, 100 );
		yield return Frames( 2 );

		Check( "a scan with a picture hides the black inside", ( emptyImage != null ) && !emptyImage.gameObject.activeInHierarchy, "empty=" + ( emptyImage != null ) );
		Check( "a scan with a picture shows its readout", sensors.m_massText.gameObject.activeInHierarchy && sensors.m_bioMinText.gameObject.activeInHierarchy, "mass=" + sensors.m_massText.gameObject.activeInHierarchy + " bio=" + sensors.m_bioMinText.gameObject.activeInHierarchy );

		// at the end of the scan of an unknown object the science officer says what the original's says (no bio or minerals, so the scan takes the shortest time)
		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.Unknown, 0, 50, 0, 0 );

		var unknownScanEnds = Time.realtimeSinceStartup + sensors.m_maxDuration + 5.0f;

		while ( !sensors.m_hasSensorData && ( Time.realtimeSinceStartup < unknownScanEnds ) )
		{
			yield return null;
		}

		yield return Frames( 3 );

		var unknownMessage = MessagesText();

		Check( "unknown object scan ends with the original's message", sensors.m_hasSensorData && unknownMessage.Contains( "Scanners indicate unidentified object!" ), "finished=" + sensors.m_hasSensorData + " messages=[" + unknownMessage + "]" );

		spaceflightController.m_displayController.ChangeDisplay( sensors );
		yield return Frames( 2 );

		Check( "a display that has just been shown hides the black inside", ( emptyImage != null ) && !emptyImage.gameObject.activeInHierarchy, "empty=" + ( emptyImage != null ) );

		// the same without going through ChangeDisplay in between (a second scan while the window is already up)
		sensors.StartScanning( SensorsDisplay.ScanType.SpeminScout, 1, 400, 100, 100 );
		yield return Frames( 2 );
		sensors.StartScanning( SensorsDisplay.ScanType.Unknown, 1, 400, 100, 100 );
		yield return Frames( 2 );

		Check( "unknown object scan right after a spemin scout leaves the window empty", !PictureShown( sensors ), SensorPicture( sensors ) );

		// a debris scan that does not say which vessel left the debris has no picture (debris pictures are per vessel)
		spaceflightController.m_displayController.ChangeDisplay( sensors );
		sensors.StartScanning( SensorsDisplay.ScanType.Debris, 1, 400, 0, 80 );
		yield return Frames( 2 );

		Check( "debris scan without a vessel leaves the window empty", !PictureShown( sensors ), SensorPicture( sensors ) );

		// every vessel's wreck shows the debris of its own picture ("<picture>_debris", made by DevTools/SensorPictures/make-debris.ps1 or before it)
		var everyPicture = new List<KeyValuePair<SensorsDisplay.ScanType, string>>( tracedPictures )
		{
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.SpeminTransport, "Sensors - Spemin Transport" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.SpeminScout, "Sensors - Spemin Scout" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.SpeminWarship, "Sensors - Spemin Warship" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.MechanScout, "Sensors - Mechan Scout" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.VeloxDrone, "Sensors - Velox Drone" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.NomadProbe, "Sensors - Nomad Probe" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.Mysterion, "Sensors - Mysterion" ),
			new KeyValuePair<SensorsDisplay.ScanType, string>( SensorsDisplay.ScanType.Minstrel, "Sensors - Minstrel" ),
		};

		foreach ( var vessel in everyPicture )
		{
			spaceflightController.m_displayController.ChangeDisplay( sensors );
			sensors.StartScanning( SensorsDisplay.ScanType.Debris, 0, 100, 0, 50, (int) vessel.Key );
			yield return Frames( 2 );

			Check( vessel.Key + " wreck shows its own debris", PictureShown( sensors ) && ( BackgroundName( sensors ) == vessel.Value + "_debris" ) && ( MaskName( sensors ) == vessel.Value + "_debris_mask" ), SensorPicture( sensors ) );
		}

		Log( "vessels checked for their debris: " + everyPicture.Count );

		// the wreck of a ship destroyed in a real encounter, scanned the way the sensors button does: each vessel shows what is left of itself
		var wreck = "";

		yield return ScanWreck( 2, result => wreck = result );
		Check( "spemin scout wreck shows the spemin scout debris", wreck.Contains( "shown=True" ) && wreck.EndsWith( "mask=Sensors - Spemin Scout_debris_mask" ), wreck );

		yield return ScanWreck( 4, result => wreck = result );
		Check( "mechan scout wreck shows the mechan scout debris", wreck.Contains( "shown=True" ) && wreck.EndsWith( "mask=Sensors - Mechan Scout_debris_mask" ), wreck );

		yield return ScanWreck( 9, result => wreck = result );
		Check( "thrynn scout wreck shows the thrynn scout debris", wreck.Contains( "shown=True" ) && wreck.EndsWith( "mask=Sensors - Thrynn Scout_debris_mask" ), wreck );

		// control: the spemin warship wreck is the picture every wreck had before
		yield return ScanWreck( 3, result => wreck = result );
		Check( "spemin warship wreck shows the spemin warship debris", wreck.Contains( "shown=True" ) && wreck.EndsWith( "mask=Sensors - Spemin Warship_debris_mask" ), wreck );

		// the readout of a finished scan: a vessel's mass in tons, its bio density and its energy, as the original shows them (plain numbers, no percent sign)
		// (spemin scout 4x10^2 bio 100 energy 100, mechan scout 2x10^2 bio 0 energy 100, minstrel "2" with no power of ten, bio 100 energy 0)
		var scan = "";

		yield return ScanShip( 2, false, true, result => scan = result );
		Check( "spemin scout readout: 4x10^2 tons, bio 100, energy 100", scan.Contains( "finished=True" ) && scan.Contains( "4x10<sup>2</sup>" ) && scan.Contains( "Bio: <color=\"white\">100</color>" ) && scan.Contains( "Energy: <color=\"white\">100</color>" ), scan );

		yield return ScanShip( 4, false, true, result => scan = result );
		Check( "mechan scout readout: 2x10^2 tons, bio 0, energy 100", scan.Contains( "finished=True" ) && scan.Contains( "2x10<sup>2</sup>" ) && scan.Contains( "Bio: <color=\"white\">0</color>" ) && scan.Contains( "Energy: <color=\"white\">100</color>" ), scan );

		yield return ScanShip( 22, false, true, result => scan = result );
		Check( "minstrel readout: 2 tons with no power of ten, bio 100, energy 0", scan.Contains( "finished=True" ) && scan.Contains( "<color=\"white\">2</color> Tons" ) && !scan.Contains( "<sup>" ) && scan.Contains( "Bio: <color=\"white\">100</color>" ) && scan.Contains( "Energy: <color=\"white\">0</color>" ), scan );

		// a wreck has the mass of its vessel in tons (it was one power of ten too high) and its salvage potential as minerals
		yield return ScanShip( 2, true, true, result => scan = result );
		Check( "spemin scout wreck readout: 4x10^2 tons, min, with no percent sign", scan.Contains( "finished=True" ) && scan.Contains( "4x10<sup>2</sup>" ) && scan.Contains( "Min: " ) && !scan.Contains( "Energy" ) && !scan.Contains( "%" ), scan );

		// every slot of the two texture arrays is either empty or a texture that is there (a reference to a missing asset reads as null too, so count the slots that are set)
		var slotsSet = 0;

		for ( var i = 0; i < sensors.m_maskTextures.Length; i++ )
		{
			if ( ( sensors.m_maskTextures[ i ] != null ) && ( i < sensors.m_backgroundTextures.Length ) && ( sensors.m_backgroundTextures[ i ] != null ) )
			{
				slotsSet++;
			}
		}

		Log( "sensor slots with both textures: " + slotsSet + " of " + sensors.m_maskTextures.Length );

		Finish( "scenario=sensorpictures slotsWithPicture=" + slotsSet + " last=[" + SensorPicture( sensors ) + "]", 0 );
	}

	// how high the planet's disc is on the screen, as a part of the window's height (the window is the clip, the magenta inside the panel)
	// (the disc is measured in the mask's file, not taken from the code: the imported texture is not readable)
	static float PlanetDiscFraction( SensorsDisplay sensors )
	{
		var bytes = System.IO.File.ReadAllBytes( "Assets/Game Objects/UI/Sensors/Sensors - Planet Mask.png" );
		var mask = new Texture2D( 2, 2 );

		mask.LoadImage( bytes );

		// the rows of the middle column that are opaque
		var top = -1;
		var bottom = -1;

		for ( var y = 0; y < mask.height; y++ )
		{
			if ( mask.GetPixel( mask.width / 2, y ).a > 0.5f )
			{
				if ( bottom < 0 )
				{
					bottom = y;
				}

				top = y;
			}
		}

		var discRows = top - bottom + 1;
		var textureHeight = mask.height;

		UnityEngine.Object.DestroyImmediate( mask );

		var clip = sensors.m_maskImage.transform.parent.GetComponent<UnityEngine.UI.RectMask2D>();

		if ( ( clip == null ) || ( bottom < 0 ) )
		{
			return -1.0f;
		}

		var windowHeight = ( (RectTransform) clip.transform ).rect.height - clip.padding.y - clip.padding.w;
		var discHeight = sensors.m_maskImage.rectTransform.rect.height * sensors.m_maskImage.transform.localScale.y * discRows / textureHeight;

		return discHeight / windowHeight;
	}

	static string MaskName( SensorsDisplay sensors )
	{
		var texture = sensors.m_maskImage.material.GetTexture( "_MaskTex" );

		return ( texture != null ) ? texture.name : "none";
	}

	static string BackgroundName( SensorsDisplay sensors )
	{
		var texture = sensors.m_backgroundImage.material.GetTexture( "_MainTex" );

		return ( texture != null ) ? texture.name : "none";
	}

	static bool PictureShown( SensorsDisplay sensors )
	{
		return sensors.m_backgroundImage.gameObject.activeInHierarchy && sensors.m_maskImage.gameObject.activeInHierarchy;
	}

	static string SensorPicture( SensorsDisplay sensors )
	{
		return "scanType=" + sensors.m_scanType + " shown=" + PictureShown( sensors ) + " background=" + BackgroundName( sensors ) + " mask=" + MaskName( sensors );
	}

	// enter a spemin star system group whose ships are all of this vessel, destroy the first ship, scan its wreck through the encounter, report the sensor picture and leave
	IEnumerator ScanWreck( int vesselId, Action<string> result )
	{
		yield return ScanShip( vesselId, true, false, result );
	}

	// enter a spemin star system group whose ships are all of this vessel, scan the first ship (or, with destroyFirst, its wreck) through the encounter,
	// report the sensor picture (and, with waitForReadout, the mass and bio lines once the scan is over) and leave
	IEnumerator ScanShip( int vesselId, bool destroyFirst, bool waitForReadout, Action<string> result )
	{
		var spaceflightController = SpaceflightController.m_instance;
		var sensors = spaceflightController.m_displayController.m_sensorsDisplay;

		var encounterId = FindEncounter( 1, 6, 3, 0 );

		if ( encounterId < 0 )
		{
			result( "no spemin star system encounter" );
			yield break;
		}

		var pdEncounter = DataController.m_instance.m_playerData.FindEncounter( encounterId );

		pdEncounter.Reset( encounterId );

		foreach ( var alienShip in pdEncounter.GetAlienShipList() )
		{
			alienShip.m_vesselId = vesselId;
		}

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		var alienIndex = FirstLivingAlien();

		if ( alienIndex < 0 )
		{
			result( "no living ship in encounter " + encounterId );
			LeaveEncounter();
			yield return Frames( 10 );
			yield break;
		}

		if ( destroyFirst )
		{
			Kill( alienIndex );
			yield return Frames( 5 );
		}

		var text = "vessel=" + vesselId + " dead=" + pdEncounter.GetAlienShipList()[ alienIndex ].m_isDead + " ";

		try
		{
			spaceflightController.m_displayController.ChangeDisplay( sensors );
			spaceflightController.m_encounter.StartScanning( alienIndex + 1 );
		}
		catch ( Exception exception )
		{
			text += "scan threw " + exception.GetType().Name + " ";
		}

		yield return Frames( 2 );

		if ( waitForReadout )
		{
			// the readout counts up to its values while the scan runs (at most m_maxDuration seconds)
			var until = Time.realtimeSinceStartup + sensors.m_maxDuration + 5.0f;

			while ( !sensors.m_hasSensorData && ( Time.realtimeSinceStartup < until ) )
			{
				yield return null;
			}

			text += "finished=" + sensors.m_hasSensorData + " mass=[" + sensors.m_massText.text + "] readout=[" + sensors.m_bioMinText.text + "] ";
		}

		result( text + SensorPicture( sensors ) );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );
	}

	// ---------------------------------------------------------------- colour names: every named colour in rich text is one that TextMesh Pro knows

	IEnumerator ScenarioColourNames()
	{
		// the names TextMesh Pro reads in <color=name> (TMP_Text, "<color=name>"); any other name makes it print the tag as text
		var known = new HashSet<string> { "red", "lightblue", "blue", "grey", "black", "green", "white", "orange", "purple", "yellow" };

		// every <color=name> in the game's scripts
		var unknown = new List<string>();
		var named = 0;
		var pattern = new System.Text.RegularExpressions.Regex( "<color=([A-Za-z]+)>" );

		foreach ( var path in System.IO.Directory.GetFiles( System.IO.Path.Combine( Application.dataPath, "Scripts" ), "*.cs", System.IO.SearchOption.AllDirectories ) )
		{
			var lines = System.IO.File.ReadAllLines( path );

			for ( var i = 0; i < lines.Length; i++ )
			{
				foreach ( System.Text.RegularExpressions.Match match in pattern.Matches( lines[ i ] ) )
				{
					named++;

					if ( !known.Contains( match.Groups[ 1 ].Value.ToLowerInvariant() ) )
					{
						unknown.Add( System.IO.Path.GetFileName( path ) + ":" + ( i + 1 ) + " " + match.Value );
					}
				}
			}
		}

		Check( "every named colour in the scripts is one TextMesh Pro knows", ( named > 0 ) && ( unknown.Count == 0 ), "named=" + named + " unknown=[" + string.Join( ", ", unknown ) + "]" );

		// the shield message as the player sees it: shields up, a hit they absorb, then the text TextMesh Pro shows in the message box
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		// a new game has no shields: fit class 1 (500 points)
		if ( ship.m_shieldingClass < 1 )
		{
			ship.m_shieldingClass = 1;
		}

		ship.m_shieldsAreUp = true;
		ship.m_shieldPoints = ship.GetMaximumShieldPoints();

		var armorBefore = ship.m_armorPoints;
		var shown = "";

		try
		{
			CombatController.m_instance.ApplyDamageToPlayer( 10, Vector3.forward );
		}
		catch ( Exception exception )
		{
			shown = "hit threw " + exception.GetType().Name + " ";
		}

		yield return Frames( 3 );

		var messagesText = GetField( SpaceflightController.m_instance.m_messages, "m_messagesUI" ) as TMPro.TMP_Text;

		if ( messagesText != null )
		{
			messagesText.ForceMeshUpdate();
			shown += messagesText.GetParsedText().Replace( '\n', '/' );
		}

		Check( "the shield message shows its words and no tag", shown.Contains( "Shields absorb 10 damage!" ) && !shown.Contains( "<color" ), "shown=[" + shown + "] armor " + armorBefore + "->" + ship.m_armorPoints );

		Finish( "scenario=colournames named=" + named + " unknown=" + unknown.Count, 0 );
	}

	// ---------------------------------------------------------------- M8: saves survive a damaged file

	[Serializable]
	class ProbeSave
	{
		public int m_value;
		public string m_text;
	}

	IEnumerator ScenarioM8()
	{
		// a scratch directory - never the real save directory
		var directory = System.IO.Path.Combine( Application.temporaryCachePath, "claude-probe-saves" );

		if ( directory.StartsWith( Application.persistentDataPath ) )
		{
			Finish( "abort: the scratch directory is inside the real save directory", 2 );
			yield break;
		}

		var constructor = typeof( JsonSaveSystem ).GetConstructor( new[] { typeof( string ) } );

		if ( constructor == null )
		{
			Finish( "abort: JsonSaveSystem has no directory constructor on this branch, so it can only write to the real save directory - not running", 2 );
			yield break;
		}

		if ( System.IO.Directory.Exists( directory ) )
		{
			System.IO.Directory.Delete( directory, true );
		}

		System.IO.Directory.CreateDirectory( directory );

		var saves = constructor.Invoke( new object[] { directory } ) as JsonSaveSystem;

		var path = System.IO.Path.Combine( directory, "probe0.json" );
		var backup = path + ".bak";
		var temp = path + ".tmp";
		var corrupt = path + ".corrupt";

		Log( "scratch directory: " + directory );

		// first save: no backup yet
		saves.Save( "probe", 0, new ProbeSave { m_value = 1, m_text = "first" } );

		Check( "first save", System.IO.File.Exists( path ) && !System.IO.File.Exists( backup ) && !System.IO.File.Exists( temp ) && saves.Exists( "probe", 0 ) && ( saves.Load<ProbeSave>( "probe", 0 ).m_value == 1 ),
			"file=" + System.IO.File.Exists( path ) + " backup=" + System.IO.File.Exists( backup ) + " temp left behind=" + System.IO.File.Exists( temp ) );

		// second save: the first one becomes the backup
		saves.Save( "probe", 0, new ProbeSave { m_value = 2, m_text = "second" } );

		var backupValue = JsonUtility.FromJson<ProbeSave>( System.IO.File.ReadAllText( backup ) ).m_value;

		Check( "second save keeps the first as a backup", ( saves.Load<ProbeSave>( "probe", 0 ).m_value == 2 ) && ( backupValue == 1 ) && !System.IO.File.Exists( temp ),
			"loaded " + saves.Load<ProbeSave>( "probe", 0 ).m_value + ", backup holds " + backupValue );

		// damage the save the way an interrupted write does (cut it in half)
		var bytes = System.IO.File.ReadAllBytes( path );

		System.IO.File.WriteAllBytes( path, new ArraySegment<byte>( bytes, 0, bytes.Length / 2 ).ToArray() );

		var exceptionsBefore = s_exceptionCount;
		var recovered = saves.Load<ProbeSave>( "probe", 0 );

		Check( "a truncated save falls back to the backup and is kept", ( recovered != null ) && ( recovered.m_value == 1 ) && System.IO.File.Exists( corrupt ) && !System.IO.File.Exists( path ) && ( s_exceptionCount == exceptionsBefore ),
			"loaded " + ( ( recovered == null ) ? "nothing" : recovered.m_value.ToString() ) + ", damaged file kept as .corrupt=" + System.IO.File.Exists( corrupt ) + " (" + ( System.IO.File.Exists( corrupt ) ? new System.IO.FileInfo( corrupt ).Length : 0 ) + " bytes)" );

		// only the backup is left: the slot still counts as existing, and saving works again
		var existsWithBackupOnly = saves.Exists( "probe", 0 );

		saves.Save( "probe", 0, new ProbeSave { m_value = 3, m_text = "third" } );

		Check( "a slot with only its backup still loads, and saves again", existsWithBackupOnly && ( saves.Load<ProbeSave>( "probe", 0 ).m_value == 3 ), "exists=" + existsWithBackupOnly + ", loaded " + saves.Load<ProbeSave>( "probe", 0 ).m_value );

		// an empty file (a write that never got anywhere)
		System.IO.File.WriteAllText( path, "" );

		var afterEmpty = saves.Load<ProbeSave>( "probe", 0 );

		Check( "an empty save falls back to the backup", ( afterEmpty != null ) && ( afterEmpty.m_value == 1 ), "loaded " + ( ( afterEmpty == null ) ? "nothing" : afterEmpty.m_value.ToString() ) );

		// a temporary file left behind by a crashed save must not get in the way
		System.IO.File.WriteAllText( temp, "{ this is not a save" );

		saves.Save( "probe", 0, new ProbeSave { m_value = 4, m_text = "fourth" } );

		Check( "a leftover temporary file does not block saving", ( saves.Load<ProbeSave>( "probe", 0 ).m_value == 4 ) && !System.IO.File.Exists( temp ), "loaded " + saves.Load<ProbeSave>( "probe", 0 ).m_value + ", temp left behind=" + System.IO.File.Exists( temp ) );

		// both the save and the backup are damaged: nothing loads, nothing throws, the damaged save is kept
		saves.Save( "probe", 0, new ProbeSave { m_value = 5, m_text = "fifth" } );

		System.IO.File.WriteAllText( path, "{ broken" );
		System.IO.File.WriteAllText( backup, "{ broken too" );

		var loadResult = "ok";
		ProbeSave nothing = null;

		try
		{
			nothing = saves.Load<ProbeSave>( "probe", 0 );
		}
		catch ( Exception exception )
		{
			loadResult = exception.GetType().Name;
		}

		Check( "damaged save and damaged backup: load returns nothing without throwing", ( loadResult == "ok" ) && ( nothing == null ) && System.IO.File.Exists( corrupt ), "result " + loadResult + ", loaded " + ( ( nothing == null ) ? "nothing" : "something" ) );

		// a real, full size save
		var playerData = DataController.m_instance.m_playerData;
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		saves.Save( "player", 1, playerData );
		saves.Save( "player", 1, playerData );

		var saveMilliseconds = stopwatch.ElapsedMilliseconds / 2;
		var loadedPlayerData = saves.Load<PlayerData>( "player", 1 );
		var playerPath = System.IO.Path.Combine( directory, "player1.json" );

		Check( "a full player save round-trips", ( loadedPlayerData != null ) && loadedPlayerData.IsCurrentVersion() && ( loadedPlayerData.m_encounterList.Length == playerData.m_encounterList.Length ) && System.IO.File.Exists( playerPath + ".bak" ),
			new System.IO.FileInfo( playerPath ).Length + " bytes, about " + saveMilliseconds + " ms per save, encounters " + ( ( loadedPlayerData == null ) ? 0 : loadedPlayerData.m_encounterList.Length ) );

		// deleting a slot removes its backup too
		saves.Delete( "probe", 0 );

		Check( "deleting a slot removes the backup as well", !saves.Exists( "probe", 0 ) && !System.IO.File.Exists( backup ), "exists=" + saves.Exists( "probe", 0 ) );

		System.IO.Directory.Delete( directory, true );

		Finish( "scenario=m8 passed=" + s_checksPassed + " failed=" + s_checksFailed, 0 );
	}

	// ---------------------------------------------------------------- M24: gravity text

	IEnumerator ScenarioM24()
	{
		var text = "";

		foreach ( var gravity in new[] { 1, 5, 9, 10, 99, 100, 105, 250, 1770 } )
		{
			text += gravity + "=>" + new GD_Planet { m_gravity = gravity }.GetGravityText() + "  ";
		}

		Log( "gravity text: " + text.Trim() );

		// what the real planets show
		var gameData = DataController.m_instance.m_gameData;
		var wrong = 0;

		foreach ( var planet in gameData.m_planetList )
		{
			var expected = ( planet.m_gravity / 100.0f ).ToString( "F2", System.Globalization.CultureInfo.InvariantCulture ) + " G";

			if ( planet.GetGravityText() != expected )
			{
				wrong++;
			}
		}

		yield return null;

		Finish( "scenario=m24 planets=" + gameData.m_planetList.Length + " planetsWithWrongGravityText=" + wrong, 0 );
	}

	// ---------------------------------------------------------------- M22: a closing panel ignores a second exit and further clicks

	IEnumerator ScenarioStarportM22()
	{
		var playerData = DataController.m_instance.m_playerData;
		var depot = FindPanel<TradeDepotPanel>();
		var transactions = playerData.m_bank.m_transactionList;

		yield return OpenPanel( depot, "trade depot" );

		depot.SellClicked();
		yield return Frames( 3 );

		// sell something so that closing the panel has a transaction to log
		var sale = TrySell( depot, "5" );

		Log( "sold 5 cubic meters: " + sale );

		Call( depot, "SwitchToMenuBarState" );
		yield return Frames( 3 );

		var transactionsBefore = transactions.Count;

		// a double click on exit
		depot.ExitClicked();
		depot.ExitClicked();

		var stateBeforeClick = GetField( depot, "m_currentState" ).ToString();

		// a click on buy while the panel is sliding away
		ExecuteEvents.Execute( depot.m_buyButton.gameObject, new PointerEventData( EventSystem.current ), ExecuteEvents.pointerClickHandler );
		yield return Frames( 2 );

		var stateAfterClick = GetField( depot, "m_currentState" ).ToString();

		yield return new WaitForSecondsRealtime( 1.5f );

		var transactionsLogged = transactions.Count - transactionsBefore;

		if ( PanelController.m_instance.HasActivePanel() )
		{
			Log( "the panel did not finish closing - finishing the close by hand" );

			PanelController.m_instance.Closed();
		}

		// positive control: once the panel is open again the same click works
		yield return OpenPanel( depot, "trade depot again" );

		var stateBeforeSecondClick = GetField( depot, "m_currentState" ).ToString();

		ExecuteEvents.Execute( depot.m_buyButton.gameObject, new PointerEventData( EventSystem.current ), ExecuteEvents.pointerClickHandler );
		yield return Frames( 2 );

		var stateAfterSecondClick = GetField( depot, "m_currentState" ).ToString();

		Call( depot, "SwitchToMenuBarState" );

		yield return ClosePanel( depot, "trade depot again" );

		Finish( "scenario=starport-m22 transactionsLoggedByDoubleExit=" + transactionsLogged + " buyClickWhileClosing=" + stateBeforeClick + "->" + stateAfterClick + " buyClickAfterReopening=" + stateBeforeSecondClick + "->" + stateAfterSecondClick, 0 );
	}

	// ---------------------------------------------------------------- M5 and M21: ship configuration

	IEnumerator ScenarioStarportShip()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var bank = playerData.m_bank;
		var panel = FindPanel<ShipConfigurationPanel>();

		yield return OpenPanel( panel, "ship configuration" );

		// ---- M5: buy class 5 armor and sell it straight back
		var pointsBare = ship.m_armorPoints;
		var balanceBefore = bank.m_currentBalance;

		SetField( panel, "m_currentPartIndex", 3 );
		SetField( panel, "m_currentClassIndex", 4 );
		Call( panel, "BuySelectedClass" );

		var pointsWithArmor = ship.m_armorPoints;
		var classWithArmor = ship.m_armorClass;

		SetField( panel, "m_currentPartIndex", 3 );
		Call( panel, "SellSelectedPart" );

		var pointsAfterSale = ship.m_armorPoints;

		Log( "M5: bare hull " + pointsBare + " points; bought class " + classWithArmor + " armor (" + pointsWithArmor + " points); sold it: class " + ship.m_armorClass + ", " + pointsAfterSale + " points, net cost " + ( balanceBefore - bank.m_currentBalance ) );

		// damaged plating keeps its damage when it is sold
		SetField( panel, "m_currentPartIndex", 3 );
		SetField( panel, "m_currentClassIndex", 0 );
		Call( panel, "BuySelectedClass" );

		ship.m_armorPoints = 100;

		SetField( panel, "m_currentPartIndex", 3 );
		Call( panel, "SellSelectedPart" );

		var pointsAfterDamagedSale = ship.m_armorPoints;

		Log( "M5: class 1 armor damaged down to 100 points, sold: " + pointsAfterDamagedSale + " points" );

		ship.m_armorPoints = pointsBare;

		// ---- M21: sell a cargo pod that the loaded cargo needs
		SetField( panel, "m_currentPartIndex", 0 );
		Call( panel, "BuyCargoPod" );

		var podsBought = ship.m_numCargoPods;

		// fill the hold until only 10 cubic meters are free (the pod holds more than that)
		ship.AddElement( 6, ship.GetRemainingVolume() - 100 );

		var remainingBefore = ship.GetRemainingVolume();

		SetField( panel, "m_currentPartIndex", 0 );
		Call( panel, "SellSelectedPart" );

		var podsAfterFullSale = ship.m_numCargoPods;
		var remainingAfterFullSale = ship.GetRemainingVolume();
		var stateAfterFullSale = GetField( panel, "m_currentState" ).ToString();

		Log( "M21: " + podsBought + " pod, " + remainingBefore + " free; sold the pod with the hold nearly full: pods=" + podsAfterFullSale + ", free volume=" + remainingAfterFullSale + ", panel state=" + stateAfterFullSale );

		// an empty enough hold can still sell its pod
		var cargo = ship.m_elementStorage.Find( 6 );

		if ( cargo != null )
		{
			ship.RemoveElement( 6, cargo.m_volume );
		}

		if ( ship.m_numCargoPods > 0 )
		{
			Call( panel, "SwitchToSellPartState", true );
			SetField( panel, "m_currentPartIndex", 0 );
			Call( panel, "SellSelectedPart" );
		}

		var podsAfterEmptySale = ship.m_numCargoPods;

		Log( "M21: after emptying the hold and selling again: pods=" + podsAfterEmptySale + ", free volume=" + ship.GetRemainingVolume() );

		Call( panel, "SwitchToMenuBarState" );

		yield return ClosePanel( panel, "ship configuration" );

		Finish( "scenario=starport-ship armorPointsAfterSellingFullArmor=" + pointsAfterSale + " armorPointsAfterSellingDamagedArmor=" + pointsAfterDamagedSale + " podSoldWithFullHold=" + ( podsAfterFullSale < podsBought ) + " freeVolumeAfterwards=" + remainingAfterFullSale + " podSoldWithEmptyHold=" + ( podsAfterEmptySale == 0 ), 0 );
	}

	// ---------------------------------------------------------------- H5: alien ships have hit points

	// hit an alien ship through the real damage path
	static void Hit( int alienIndex, int damage )
	{
		var method = typeof( CombatController ).GetMethod( "ApplyDamageToAlien", BindingFlags.Instance | BindingFlags.NonPublic );

		try
		{
			method.Invoke( CombatController.m_instance, new object[] { alienIndex, damage } );
		}
		catch ( TargetInvocationException exception )
		{
			s_exceptionCount++;

			Log( "Hit( " + alienIndex + ", " + damage + " ) threw " + exception.InnerException.GetType().Name + ": " + exception.InnerException.Message );
		}
	}

	static int HitsToKill( int alienIndex, int damage, int limit )
	{
		var ship = SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList()[ alienIndex ];
		var hits = 0;

		while ( !ship.m_isDead && ( hits < limit ) )
		{
			Hit( alienIndex, damage );
			hits++;
		}

		return ship.m_isDead ? hits : -1;
	}

	IEnumerator ScenarioH5()
	{
		var gameData = DataController.m_instance.m_gameData;
		var encounter = SpaceflightController.m_instance.m_encounter;

		EnsureCrew();

		var result = "scenario=h5";

		foreach ( var race in new[] { GameData.Race.Spemin, GameData.Race.Elowan, GameData.Race.Thrynn } )
		{
			var encounterId = FindEncounter( 1, 6, 3, 0, race );

			EnterEncounter( encounterId );
			yield return Frames( 10 );

			for ( var index = 0; index < 3; index++ )
			{
				var ship = encounter.m_pdEncounter.GetAlienShipList()[ index ];
				var vessel = gameData.m_vesselList[ ship.m_vesselId ];
				var expected = Mathf.CeilToInt( ( Mathf.Max( 1, vessel.m_armor ) + Mathf.Max( 0, vessel.m_shields ) ) / 20.0f );
				var hits = HitsToKill( index, 20, 400 );

				Log( vessel.m_name + " (armor " + vessel.m_armorClass + ", shield " + vessel.m_shieldClass + "): " + ( ( hits < 0 ) ? "still alive after 400 hits of 20" : ( "destroyed by hit " + hits + " of 20" ) ) + ", the points of the game data would be " + expected );

				result += " " + vessel.m_name.Replace( " ", "" ) + "=" + hits + "/" + expected;
			}

			yield return Frames( 5 );

			LeaveEncounter();
			yield return Frames( 10 );
		}

		// a living ship with no hit points (a save from before they existed) gets them on its first hit, and they survive a save
		var armorField = typeof( PD_AlienShip ).GetField( "m_armorPoints" );

		if ( armorField != null )
		{
			EnterEncounter( FindEncounter( 1, 6, 3, 1, GameData.Race.Spemin ) );
			yield return Frames( 10 );

			var ship = encounter.m_pdEncounter.GetAlienShipList()[ 0 ];
			var full = (int) armorField.GetValue( ship );

			armorField.SetValue( ship, 0 );

			Hit( 0, 20 );

			var afterOldSaveHit = (int) armorField.GetValue( ship );
			var loaded = JsonUtility.FromJson<PD_AlienShip>( JsonUtility.ToJson( ship ) );
			var afterLoad = (int) armorField.GetValue( loaded );

			Log( "old save: full armor " + full + ", zeroed, one hit of 20: alive=" + !ship.m_isDead + " armor=" + afterOldSaveHit + "; after save and load armor=" + afterLoad );

			result += " oldSaveShipSurvivesFirstHit=" + ( !ship.m_isDead && ( afterOldSaveHit == full - 20 ) ) + " damageSurvivesSave=" + ( afterLoad == afterOldSaveHit );
		}
		else
		{
			result += " hitPointFields=none";
		}

		Finish( result, 0 );
	}

	// ---------------------------------------------------------------- M19: shields keep their charge and recharge slowly

	IEnumerator ScenarioM19()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;

		EnsureCrew();

		ship.m_shieldingClass = 2;
		ship.m_armorPoints = 1000;
		ship.m_shieldsAreUp = false;

		if ( ship.m_elementStorage.Find( 5 ) == null )
		{
			ship.AddElement( 5, 50 );
		}

		var full = ship.GetShielding().m_points;
		var keepField = typeof( PD_PlayerShip ).GetField( "m_shieldChargeIsKept" );
		var update = typeof( PD_PlayerShip ).GetMethod( "UpdateShields" );

		// start from a full charge, shields up, and take a hit
		ship.RaiseShields();
		ship.m_shieldPoints = full;

		combat.ApplyDamageToPlayer( 300, Vector3.forward );

		var afterHit = ship.m_shieldPoints;

		// lower the shields and take a hit: it has to go to the hull
		ship.DropShields();

		var chargeWhileDown = ship.m_shieldPoints;
		var armorBefore = ship.m_armorPoints;

		combat.ApplyDamageToPlayer( 50, Vector3.forward );

		var hullDamageWhileDown = armorBefore - ship.m_armorPoints;

		// (the hit also damaged one of the ship's systems at random - make them whole, so that a damaged shield generator does not change what follows)
		var restoreSystems = typeof( PD_PlayerShip ).GetMethod( "RestoreSystems" );

		if ( restoreSystems != null )
		{
			restoreSystems.Invoke( ship, null );
		}

		// raise them again
		ship.RaiseShields();

		var afterDropAndRaise = ship.m_shieldPoints;

		Log( "class 2 shields, full " + full + ": after a 300 hit " + afterHit + ", while lowered " + chargeWhileDown + ", a 50 hit while lowered did " + hullDamageWhileDown + " to the hull, after raising again " + afterDropAndRaise );

		// ten seconds of game time through the recharge method itself
		var rechargeInTenSeconds = -1;

		if ( update != null )
		{
			var before = ship.m_shieldPoints;

			update.Invoke( ship, new object[] { 10.0f } );

			rechargeInTenSeconds = ship.m_shieldPoints - before;
		}

		// and six real seconds, to see that the game calls it every frame
		var beforeWait = ship.m_shieldPoints;

		yield return new WaitForSecondsRealtime( 6.0f );

		var gainedWhileWaiting = ship.m_shieldPoints - beforeWait;

		// a save from before the charge was kept: shields down, no charge
		var oldSaveRaise = "n/a";

		if ( keepField != null )
		{
			ship.DropShields();
			ship.m_shieldPoints = 0;
			keepField.SetValue( ship, false );

			ship.RaiseShields();

			oldSaveRaise = ship.m_shieldPoints.ToString();
		}

		Finish( "scenario=m19 full=" + full + " afterHit=" + afterHit + " chargeWhileLowered=" + chargeWhileDown + " hullDamageWhileLowered=" + hullDamageWhileDown + " afterLowerAndRaise=" + afterDropAndRaise + " rechargeIn10GameSeconds=" + rechargeInTenSeconds + " gainedIn6RealSeconds=" + gainedWhileWaiting + " oldSaveRaise=" + oldSaveRaise, 0 );
	}

	// ---------------------------------------------------------------- H7: game over goes back to the last save

	// the copy of a slot that is in the save system (null if that slot was never saved)
	static PlayerData Stored( int slot )
	{
		return MemorySaveSystem.s_slots.TryGetValue( slot, out var json ) ? JsonUtility.FromJson<PlayerData>( json ) : null;
	}

	static string Describe( PlayerData playerData, int encounterId )
	{
		if ( playerData == null )
		{
			return "none";
		}

		var encounter = playerData.FindEncounter( encounterId );

		return "armor " + playerData.m_playerShip.m_armorPoints + " " + playerData.m_general.m_location + " " + ( ( encounter == null ) ? "?" : encounter.m_alienStance.ToString() ) + ( playerData.m_isCurrentGame ? " current" : " notCurrent" );
	}

	static IEnumerator WaitForScene( string sceneName )
	{
		for ( var i = 0; ( i < 1800 ) && ( SceneManager.GetActiveScene().name != sceneName ); i++ )
		{
			yield return null;
		}
	}

	IEnumerator ScenarioH7()
	{
		var dataController = DataController.m_instance;
		var slot = dataController.m_activeSaveGameSlotNumber;
		var reload = typeof( DataController ).GetMethod( "ReloadActiveGame", c_any );

		EnsureCrew();

		// a recognizable amount of armor, shields down so that every hit goes to the hull. The ship gets class 1 armor plating, whose 500 points can hold it:
		// a bare hull has 250, and since PR 66 a save with more armor points than its ship can have is cut back when it is loaded (this scenario reloads its save)
		dataController.m_playerData.m_playerShip.m_armorClass = 1;
		dataController.m_playerData.m_playerShip.m_armorPoints = 400;
		dataController.m_playerData.m_playerShip.m_shieldsAreUp = false;

		// entering an encounter saves the game - this is the save the player should go back to
		var encounterId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		var storedAtStart = Describe( Stored( slot ), encounterId );
		var stanceAtStart = dataController.m_playerData.FindEncounter( encounterId ).m_alienStance;

		Log( "H7 encounter " + encounterId + " entered, stored copy: " + storedAtStart );

		// ---- first loss: the aliens turn hostile and destroy the ship
		dataController.m_playerData.FindEncounter( encounterId ).m_alienStance = GD_Comm.Stance.Hostile;

		CombatController.m_instance.ApplyDamageToPlayer( 5000, Vector3.forward );

		// wait for the explosion to finish and the game over screen to come up
		for ( var i = 0; ( i < 1800 ) && !SpaceflightController.m_instance.m_gameOver; i++ )
		{
			yield return null;
		}

		// the message box is redrawn in its late update
		yield return Frames( 3 );

		var gameOverShown = SpaceflightController.m_instance.m_gameOver && SpaceflightController.m_instance.m_gameIsPaused && MessagesText().Contains( "GAME OVER" );
		var memoryAtGameOver = Describe( dataController.m_playerData, encounterId );
		var storedAtGameOver = Describe( Stored( slot ), encounterId );

		Log( "H7 game over shown=" + gameOverShown + " memory: " + memoryAtGameOver + " | stored: " + storedAtGameOver );

		// the player presses escape: back to the title screen
		SpaceflightController.m_instance.RestartGame();

		yield return WaitForScene( "Intro" );
		yield return Frames( 10 );

		var sceneAfterRestart = SceneManager.GetActiveScene().name;
		var memoryAtTitle = Describe( dataController.m_playerData, encounterId );
		var armorAtTitle = dataController.m_playerData.m_playerShip.m_armorPoints;

		Log( "H7 after restart: scene " + sceneAfterRestart + " memory: " + memoryAtTitle );

		// the player continues from the title screen (the intro loads the scene of the current location)
		var nextScene = dataController.GetCurrentSceneName();

		SceneManager.LoadScene( nextScene );

		yield return WaitForScene( nextScene );
		yield return Frames( 30 );

		var continuedIn = SceneManager.GetActiveScene().name;
		var memoryAfterContinue = Describe( dataController.m_playerData, encounterId );
		var stanceAfterContinue = dataController.m_playerData.FindEncounter( encounterId ).m_alienStance;
		var pausedAfterContinue = SpaceflightController.m_instance.m_gameIsPaused;

		// a light hit: a ship that came back from the save shrugs it off, a ship with no armor is destroyed again
		CombatController.m_instance.ApplyDamageToPlayer( 10, Vector3.forward );

		var armorAfterLightHit = dataController.m_playerData.m_playerShip.m_armorPoints;

		Log( "H7 continued in " + continuedIn + " paused=" + pausedAfterContinue + " memory: " + memoryAfterContinue + " | armor after a 10 point hit: " + armorAfterLightHit );

		Check( "H7 the title screen has the game from the last save, not the destroyed ship", ( sceneAfterRestart == "Intro" ) && ( armorAtTitle == 400 ), "scene " + sceneAfterRestart + ", " + memoryAtTitle + " (saved at the start of the encounter: " + storedAtStart + ")" );
		Check( "H7 continuing puts the player back at the start of the encounter with a living ship", ( continuedIn == "Spaceflight" ) && !pausedAfterContinue && ( stanceAfterContinue == stanceAtStart ) && ( armorAfterLightHit == 390 ), memoryAfterContinue + ", armor after a 10 point hit " + armorAfterLightHit + ", stance when the encounter began " + stanceAtStart );

		// ---- second loss: nothing is saved once the ship is destroyed, not even during the explosion (before the game over screen)
		if ( dataController.m_playerData.m_playerShip.m_armorPoints > 0 )
		{
			CombatController.m_instance.ApplyDamageToPlayer( 5000, Vector3.forward );
		}

		// closing the save game panel saves the game
		SpaceflightController.m_instance.PanelWasClosed();

		var storedAfterPanel = Describe( Stored( slot ), encounterId );

		// copying the game to another slot
		dataController.CopyActiveSaveGameSlot( 2 );

		var storedCopy = Describe( Stored( 2 ), encounterId );
		var memoryCopy = Describe( dataController.m_playerDataList[ 2 ], encounterId );

		// closing the game
		Call( dataController, "OnApplicationQuit" );

		var storedAfterQuit = Describe( Stored( slot ), encounterId );

		Log( "H7 destroyed again: stored after closing the save panel: " + storedAfterPanel + " | after quit: " + storedAfterQuit + " | copy in slot 2 stored: " + storedCopy + " memory: " + memoryCopy );

		Check( "H7 a destroyed ship is never saved", ( Stored( slot ) != null ) && ( Stored( slot ).m_playerShip.m_armorPoints == 400 ), "after closing the save panel: " + storedAfterPanel + ", after quit: " + storedAfterQuit );
		Check( "H7 a destroyed ship is never copied to another slot", ( Stored( 2 ) == null ) && ( dataController.m_playerDataList[ 2 ].m_playerShip.m_armorPoints > 0 ), "slot 2 stored: " + storedCopy + ", memory: " + memoryCopy );

		// switching to another slot while the ship is destroyed
		dataController.SetTargetSaveGameSlotNumber( 1 );

		yield return WaitForScene( "Starport" );
		yield return Frames( 10 );

		var sceneAfterSwitch = SceneManager.GetActiveScene().name;
		var storedOldSlot = Describe( Stored( slot ), encounterId );
		var memoryOldSlot = Describe( dataController.m_playerDataList[ slot ], encounterId );
		var storedNewSlot = Describe( Stored( 1 ), encounterId );

		Log( "H7 switched to slot " + dataController.m_activeSaveGameSlotNumber + " scene " + sceneAfterSwitch + " | old slot stored: " + storedOldSlot + " memory: " + memoryOldSlot + " | new slot stored: " + storedNewSlot );

		Check( "H7 switching slots leaves the old slot at its last save", ( dataController.m_playerDataList[ slot ].m_playerShip.m_armorPoints == 400 ) && ( Stored( slot ).m_playerShip.m_armorPoints == 400 ) && !Stored( slot ).m_isCurrentGame && ( Stored( 1 ) != null ) && Stored( 1 ).m_isCurrentGame, "old slot stored: " + storedOldSlot + ", memory: " + memoryOldSlot + ", new slot stored: " + storedNewSlot );

		// ---- a save that was written with a destroyed ship (the old game over screen allowed that)
		var oldSave = "n/a";

		if ( reload != null )
		{
			var active = dataController.m_activeSaveGameSlotNumber;
			var planted = Stored( active );

			planted.m_playerShip.m_armorPoints = 0;

			MemorySaveSystem.s_slots[ active ] = JsonUtility.ToJson( planted, true );

			reload.Invoke( dataController, null );

			var savesBefore = MemorySaveSystem.s_saveCalls;

			dataController.SaveActiveGame();

			var armorLoaded = dataController.m_playerData.m_playerShip.m_armorPoints;
			var savedAgain = MemorySaveSystem.s_saveCalls > savesBefore;

			oldSave = "armor" + armorLoaded + ( savedAgain ? "/saves" : "/doesNotSave" );

			Check( "H7 a save written with a destroyed ship loads and can be saved again", ( armorLoaded == 1 ) && savedAgain, "loaded with armor " + armorLoaded + ", saved again: " + savedAgain );
		}

		Finish( "scenario=h7 gameOverShown=" + gameOverShown + " storedAtStart=[" + storedAtStart + "] memoryAtTitle=[" + memoryAtTitle + "] afterContinue=[" + memoryAfterContinue + "] armorAfter10Hit=" + armorAfterLightHit + " storedAfterPanelSave=[" + storedAfterPanel + "] storedCopy=[" + storedCopy + "] oldSlotAfterSwitch=[" + storedOldSlot + "] oldSave=" + oldSave + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M12: aliens turn hostile when fired on, the uhlek are hostile on sight

	// the first living alien ship that is in the encounter (-1 if there is none)
	static int FirstLivingAlien()
	{
		var alienShipList = SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList();

		for ( var i = 0; i < alienShipList.Length; i++ )
		{
			if ( alienShipList[ i ].m_addedToEncounter && !alienShipList[ i ].m_isDead )
			{
				return i;
			}
		}

		return -1;
	}

	// keep every living alien ship close to the player so that range is never the reason for a shot not being fired
	static void BringAliensClose()
	{
		var playerData = DataController.m_instance.m_playerData;

		foreach ( var alienShip in SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList() )
		{
			if ( alienShip.m_addedToEncounter && !alienShip.m_isDead )
			{
				alienShip.m_coordinates = playerData.m_general.m_coordinates + Vector3.forward * 300.0f;
				alienShip.m_targetCoordinates = alienShip.m_coordinates;
			}
		}
	}

	// how much damage the player ship takes in the given number of real seconds (shields are down, so all of it is hull damage)
	IEnumerator DamageTaken( float seconds, System.Action<int> result )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var before = ship.m_armorPoints;
		var end = Time.realtimeSinceStartup + seconds;

		while ( Time.realtimeSinceStartup < end )
		{
			BringAliensClose();

			yield return null;
		}

		result( before - ship.m_armorPoints );
	}

	static void ForceVessel( int encounterId, int vesselId )
	{
		var pdEncounter = DataController.m_instance.m_playerData.FindEncounter( encounterId );

		if ( pdEncounter.GetAlienShipList() == null )
		{
			pdEncounter.Reset( encounterId );
		}

		foreach ( var alienShip in pdEncounter.GetAlienShipList() )
		{
			alienShip.m_vesselId = vesselId;
		}
	}

	static string Stance()
	{
		return SpaceflightController.m_instance.m_encounter.m_pdEncounter.m_alienStance.ToString();
	}

	// take every missile out of the air. Missiles in flight are not cleared when an encounter ends, so one fired in the last encounter can hit the
	// player in the next one (seen on 2026-10-04: an uhlek missile did 200 points of damage in the enterprise encounter that followed)
	static void ClearMissiles()
	{
		var missilePool = GetField( CombatController.m_instance, "m_missilePool" ) as System.Collections.IEnumerable;

		if ( missilePool == null )
		{
			return;
		}

		foreach ( var missile in missilePool )
		{
			Call( missile, "Deactivate" );
		}
	}

	IEnumerator ScenarioM12()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;
		var encounter = SpaceflightController.m_instance.m_encounter;
		var attackedField = typeof( PD_Encounter ).GetField( "m_attackedByPlayer" );
		var missilesField = typeof( PD_PlayerShip ).GetField( "m_missilesRemaining" );

		EnsureCrew();

		// a ship that can shoot and that survives being shot at for the whole scenario
		ship.m_laserCannonClass = 1;
		ship.m_missileLauncherClass = 1;
		ship.m_armorPoints = 100000;
		ship.m_shieldsAreUp = false;

		if ( missilesField != null )
		{
			missilesField.SetValue( ship, 10 );
		}

		if ( ship.m_elementStorage.Find( 5 ) == null )
		{
			ship.AddElement( 5, 50 );
		}

		var damage = 0;

		// ---- spemin: neutral until they are fired on
		var speminId = FindEncounter( 1, 6, 3, 0 );

		// all spemin scouts (they have lasers - a transport has no weapons at all)
		ForceVessel( speminId, 2 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var speminStanceAtStart = Stance();

		yield return DamageTaken( 4.0f, value => damage = value );

		var speminDamageBefore = damage;

		// fire the laser at one of them
		combat.SetTarget( FirstLivingAlien() );

		BringAliensClose();

		var laserFired = combat.FirePlayerLaser();
		var speminStanceAfterLaser = Stance();

		yield return DamageTaken( 8.0f, value => damage = value );

		var speminDamageAfter = damage;

		Log( "M12 spemin encounter " + speminId + " (" + VesselIds() + "): stance " + speminStanceAtStart + ", damage taken in 4 s before firing " + speminDamageBefore + " | laser fired=" + laserFired + " stance " + speminStanceAfterLaser + ", damage taken in the next 8 s " + speminDamageAfter );

		// (not hostile: since the aliens have moods the Spemin may start neutral or grovel to a strong ship)
		Check( "M12 aliens that have not been fired on leave the player alone", ( speminStanceAtStart != "Hostile" ) && ( speminDamageBefore == 0 ), "stance " + speminStanceAtStart + ", damage " + speminDamageBefore );
		Check( "M12 firing the laser makes the aliens hostile and they shoot back", laserFired && ( speminStanceAfterLaser == "Hostile" ) && ( speminDamageAfter > 0 ), "fired " + laserFired + ", stance " + speminStanceAfterLaser + ", damage in 8 s " + speminDamageAfter );

		// ---- they stay hostile for the rest of the encounter, whatever else changes their stance
		encounter.m_pdEncounter.m_alienStance = GD_Comm.Stance.Friendly;

		yield return Frames( 3 );

		var stanceAfterMakingFriends = Stance();

		Check( "M12 aliens that were fired on stay hostile for the rest of the encounter", stanceAfterMakingFriends == "Hostile", "stance was set to Friendly, 3 frames later it is " + stanceAfterMakingFriends );

		// ---- leaving and coming back starts over
		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var stanceAfterReturn = Stance();
		var attackedAfterReturn = ( attackedField == null ) ? "n/a" : attackedField.GetValue( encounter.m_pdEncounter ).ToString();

		yield return DamageTaken( 4.0f, value => damage = value );

		var damageAfterReturn = damage;

		Check( "M12 leaving the encounter ends the hostility", ( stanceAfterReturn != "Hostile" ) && ( damageAfterReturn == 0 ), "stance " + stanceAfterReturn + ", attacked flag " + attackedAfterReturn + ", damage in 4 s " + damageAfterReturn );

		// ---- a missile counts too, at launch
		combat.SetTarget( FirstLivingAlien() );

		BringAliensClose();

		var missileFired = combat.FirePlayerMissile();
		var stanceAfterMissile = Stance();

		// let the missile arrive before leaving
		yield return DamageTaken( 4.0f, value => damage = value );

		Log( "M12 back in the encounter: stance " + stanceAfterReturn + ", damage in 4 s " + damageAfterReturn + " | missile fired=" + missileFired + " stance " + stanceAfterMissile + ", damage in the next 4 s " + damage );

		Check( "M12 launching a missile makes the aliens hostile", missileFired && ( stanceAfterMissile == "Hostile" ), "fired " + missileFired + ", stance " + stanceAfterMissile );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- uhlek: hostile on sight, and they never talk
		var uhlekId = FindEncounter( 0, 1, 1, 0, GameData.Race.Uhlek );

		EnterEncounter( uhlekId );
		yield return Frames( 10 );

		var uhlekStance = Stance();
		var uhlekVessels = VesselIds();

		yield return DamageTaken( 10.0f, value => damage = value );

		var uhlekDamage = damage;
		var uhlekSaidSomething = MessagesText().Contains( "ERROR" ) || encounter.m_pdEncounter.m_connected;

		Log( "M12 uhlek encounter " + uhlekId + " (vessels " + uhlekVessels + "): stance " + uhlekStance + ", damage taken in 10 s " + uhlekDamage + ", tried to talk=" + uhlekSaidSomething );

		Check( "M12 the uhlek are hostile on sight and attack", ( uhlekStance == "Hostile" ) && ( uhlekDamage > 0 ) && !uhlekSaidSomething, "stance " + uhlekStance + ", damage in 10 s " + uhlekDamage + ", tried to talk " + uhlekSaidSomething );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- the enterprise has no update of its own: it leaves the player alone until it is fired on
		var enterpriseId = FindEncounter( 0, 1, 1, 0, GameData.Race.TheEnterprise );

		EnterEncounter( enterpriseId );
		yield return Frames( 10 );

		var enterpriseStance = Stance();

		yield return DamageTaken( 4.0f, value => damage = value );

		var enterpriseDamageBefore = damage;

		// the Uhlek's hits may have destroyed the laser cannon, which then does not fire (hits damage the ship's systems since PR 145)
		var laserHealthBeforeEnterprise = playerData.m_playerShip.GetSystemHealth( PD_PlayerShip.ShipSystem.Lasers );

		playerData.m_playerShip.RestoreSystems();

		combat.SetTarget( FirstLivingAlien() );

		BringAliensClose();

		var enterpriseFiredOn = combat.FirePlayerLaser();

		yield return DamageTaken( 8.0f, value => damage = value );

		var enterpriseDamageAfter = damage;

		Log( "M12 enterprise encounter " + enterpriseId + ": stance " + enterpriseStance + ", damage in 4 s before firing " + enterpriseDamageBefore + ", laser cannon health after the Uhlek " + laserHealthBeforeEnterprise + " (restored) | fired on=" + enterpriseFiredOn + " stance " + Stance() + ", damage in the next 8 s " + enterpriseDamageAfter );

		Check( "M12 a race with no update of its own shoots back when fired on", ( enterpriseDamageBefore == 0 ) && enterpriseFiredOn && ( enterpriseDamageAfter > 0 ), "damage before " + enterpriseDamageBefore + ", fired on " + enterpriseFiredOn + ", stance " + Stance() + ", damage after " + enterpriseDamageAfter );

		Finish( "scenario=m12 spemin=" + speminStanceAtStart + "/" + speminDamageBefore + " afterLaser=" + speminStanceAfterLaser + "/" + speminDamageAfter + " pinned=" + stanceAfterMakingFriends + " afterReturn=" + stanceAfterReturn + "/" + damageAfterReturn + " afterMissile=" + stanceAfterMissile + " uhlek=" + uhlekStance + "/" + uhlekDamage + " enterprise=" + enterpriseStance + "/" + enterpriseDamageBefore + "/" + enterpriseDamageAfter + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M20: repairs and treatment take time, treatment is for the crew on board

	static PD_Personnel.PD_PersonnelFile AddPerson( string name, float vitality, int skill )
	{
		var playerData = DataController.m_instance.m_playerData;
		var file = playerData.m_personnel.CreateNewPersonnel();

		file.m_name = name;
		file.m_crewRaceId = 0;
		file.m_vitality = vitality;
		file.m_science = file.m_navigation = file.m_engineering = file.m_communications = file.m_medicine = skill;

		playerData.m_personnel.m_personnelList.Add( file );

		return file;
	}

	// the lines in the message box, straight from the player data (the text object is only redrawn in its late update)
	static string MessageList()
	{
		return string.Join( " / ", DataController.m_instance.m_playerData.m_general.m_messageList ).Replace( '\n', '/' );
	}

	// answers the engineer's question about a repair mineral (No or Yes on the console), if the code asks one - returns whether it was asked
	static bool AnswerRepairQuestion( bool yes )
	{
		int repairSet;

		try
		{
			repairSet = (int) Enum.Parse( typeof( ButtonController.ButtonSet ), "RepairMineral" );
		}
		catch ( Exception )
		{
			return false;
		}

		if ( (int) SpaceflightController.m_instance.m_buttonController.GetCurrentButtonSet() != repairSet )
		{
			return false;
		}

		PressButton( (ButtonController.ButtonSet) repairSet, yes ? 1 : 0 );

		return true;
	}

	IEnumerator ScenarioM20()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var crew = playerData.m_crewAssignment;

		var updateRepairs = typeof( PD_PlayerShip ).GetMethod( "UpdateRepairs" );
		var repairsField = typeof( PD_PlayerShip ).GetField( "m_repairsAreUnderWay" );
		var progressField = typeof( PD_PlayerShip ).GetField( "m_repairProgress" );
		var updateTreatment = typeof( PD_CrewAssignment ).GetMethod( "UpdateTreatment" );
		var treatmentField = typeof( PD_CrewAssignment ).GetField( "m_treatmentIsUnderWay" );
		var patientField = typeof( PD_CrewAssignment ).GetField( "m_patientFileId" );

		// a crew of separate people: an engineer and a doctor of skill 100, two hurt crew members, and someone hurt who was left at starport
		playerData.m_personnel.m_personnelList.Clear();

		var captain = AddPerson( "Captain", 100.0f, 100 );
		var engineer = AddPerson( "Engineer", 100.0f, 100 );
		var doctor = AddPerson( "Doctor", 100.0f, 100 );
		var hurt = AddPerson( "Hurt", 50.0f, 100 );
		var worse = AddPerson( "Worse", 20.0f, 100 );
		var ashore = AddPerson( "Ashore", 40.0f, 100 );

		crew.Assign( PD_CrewAssignment.Role.Captain, captain.m_fileId );
		crew.Assign( PD_CrewAssignment.Role.ScienceOfficer, worse.m_fileId );
		crew.Assign( PD_CrewAssignment.Role.Navigator, hurt.m_fileId );
		crew.Assign( PD_CrewAssignment.Role.Engineer, engineer.m_fileId );
		crew.Assign( PD_CrewAssignment.Role.CommunicationsOfficer, captain.m_fileId );
		crew.Assign( PD_CrewAssignment.Role.Doctor, doctor.m_fileId );

		// ---- repair: class 2 armor (750 points) down to 300
		ship.m_armorClass = 2;
		ship.m_armorPoints = 300;

		var maxArmor = ship.GetArmor().m_points;

		// the engineer asks for the hull's repair mineral (since roadmap 1.2): aluminum in the hold, and yes
		ship.AddElement( 0, 200 );

		new RepairButton().Execute();

		AnswerRepairQuestion( true );

		var armorRightAfter = ship.m_armorPoints;
		var repairMessage = MessageList();

		yield return new WaitForSecondsRealtime( 5.0f );

		var armorAfter5s = ship.m_armorPoints;

		// pressing the button again must not speed anything up
		new RepairButton().Execute();

		var armorAfterSecondPress = ship.m_armorPoints;
		var secondPressMessage = MessageList();

		Log( "M20 repair, engineer skill 100, armor 300/" + maxArmor + ": right after the button " + armorRightAfter + ", 5 s later " + armorAfter5s + ", after pressing it again " + armorAfterSecondPress );
		Log( "M20 repair message: " + repairMessage );
		Log( "M20 repair message on the second press: " + secondPressMessage );

		Check( "M20 the repair button does not repair anything on the spot", armorRightAfter == 300, "armor right after the button " + armorRightAfter + " (was 300)" );
		Check( "M20 the engineer repairs about 2 points a second at skill 100", ( armorAfter5s > 302 ) && ( armorAfter5s <= 313 ) && ( armorAfterSecondPress <= armorAfter5s + 1 ), "armor after 5 s " + armorAfter5s + ", after a second press " + armorAfterSecondPress );

		var repair = "n/a";

		if ( ( updateRepairs != null ) && ( repairsField != null ) )
		{
			// a minute at skill 100, then a minute at skill 250
			ship.m_armorPoints = 300;
			progressField.SetValue( ship, 0.0f );

			updateRepairs.Invoke( ship, new object[] { 60.0f } );

			var gainedAtSkill100 = ship.m_armorPoints - 300;

			engineer.m_engineering = 250;
			ship.m_armorPoints = 300;

			updateRepairs.Invoke( ship, new object[] { 60.0f } );

			var gainedAtSkill250 = ship.m_armorPoints - 300;

			// and to the end
			updateRepairs.Invoke( ship, new object[] { 10000.0f } );

			var armorWhenDone = ship.m_armorPoints;
			var stillUnderWayWhenDone = (bool) repairsField.GetValue( ship );
			var doneMessage = MessageList();

			// a destroyed ship is not repaired back to life
			ship.m_armorPoints = 0;
			repairsField.SetValue( ship, true );

			updateRepairs.Invoke( ship, new object[] { 60.0f } );

			var destroyedArmor = ship.m_armorPoints;

			// armor sold while the repairs were under way (the ship is back on its bare hull, and 250 is all a bare hull can have)
			ship.m_armorClass = 0;
			ship.m_armorPoints = 250;
			repairsField.SetValue( ship, true );

			updateRepairs.Invoke( ship, new object[] { 60.0f } );

			var bareHullArmor = ship.m_armorPoints;

			// an engineer who is out of action
			ship.m_armorClass = 2;
			ship.m_armorPoints = 300;
			engineer.m_vitality = 0.0f;
			repairsField.SetValue( ship, true );

			updateRepairs.Invoke( ship, new object[] { 60.0f } );

			var armorWithoutEngineer = ship.m_armorPoints;

			engineer.m_vitality = 100.0f;

			repair = "60s@100=+" + gainedAtSkill100 + " 60s@250=+" + gainedAtSkill250 + " done=" + armorWhenDone + " destroyed=" + destroyedArmor + " bareHull=" + bareHullArmor + " noEngineer=" + armorWithoutEngineer;

			Log( "M20 repair: " + repair + " | message when done: " + doneMessage );

			Check( "M20 a better engineer repairs faster", ( gainedAtSkill100 == 120 ) && ( gainedAtSkill250 == 300 ), "60 s at skill 100: +" + gainedAtSkill100 + ", at skill 250: +" + gainedAtSkill250 );
			Check( "M20 repairs stop when the armor is whole", ( armorWhenDone == maxArmor ) && !stillUnderWayWhenDone && doneMessage.Contains( "completed" ), "armor " + armorWhenDone + "/" + maxArmor + ", still under way " + stillUnderWayWhenDone );
			Check( "M20 repairs never touch a destroyed ship, a ship with no armor, or run without an engineer", ( destroyedArmor == 0 ) && ( bareHullArmor == 250 ) && ( armorWithoutEngineer == 300 ), "destroyed ship " + destroyedArmor + ", bare hull " + bareHullArmor + ", no engineer " + armorWithoutEngineer );
		}

		// ---- treat
		SpaceflightController.m_instance.m_messages.Clear();

		new TreatButton().Execute();

		var treatMessage = MessageList();
		var rightAfterTreat = "Worse " + worse.m_vitality.ToString( "F0" ) + " Hurt " + hurt.m_vitality.ToString( "F0" ) + " Ashore " + ashore.m_vitality.ToString( "F0" );

		yield return new WaitForSecondsRealtime( 5.0f );

		var worseAfter5s = worse.m_vitality;
		var hurtAfter5s = hurt.m_vitality;
		var ashoreAfter5s = ashore.m_vitality;

		Log( "M20 treat, doctor skill 100 (Worse 20, Hurt 50 on board, Ashore 40 left at starport): right after the button " + rightAfterTreat + ", 5 s later Worse " + worseAfter5s.ToString( "F1" ) + " Hurt " + hurtAfter5s.ToString( "F1" ) + " Ashore " + ashoreAfter5s.ToString( "F1" ) );
		Log( "M20 treat message: " + treatMessage );

		Check( "M20 the treat button does not heal anyone on the spot", rightAfterTreat == "Worse 20 Hurt 50 Ashore 40", rightAfterTreat );
		Check( "M20 the doctor treats the crew member who is hurt the most, about 2 points a second at skill 100", ( worseAfter5s >= 29.0f ) && ( worseAfter5s <= 32.0f ) && ( hurtAfter5s == 50.0f ), "after 5 s Worse " + worseAfter5s.ToString( "F1" ) + ", Hurt " + hurtAfter5s.ToString( "F1" ) );
		Check( "M20 someone who is not on board is not treated", ashoreAfter5s == 40.0f, "Ashore " + ashoreAfter5s.ToString( "F1" ) + " (was 40)" );

		var treat = "n/a";

		if ( ( updateTreatment != null ) && ( treatmentField != null ) && ( patientField != null ) )
		{
			// to the end of the first treatment
			updateTreatment.Invoke( crew, new object[] { 1000.0f } );

			var worseWhenDone = worse.m_vitality;
			var hurtWhenWorseIsDone = hurt.m_vitality;
			var stillUnderWayWhenDone = (bool) treatmentField.GetValue( crew );
			var doneMessage = MessageList();

			// the next press picks the next patient
			new TreatButton().Execute();

			var nextPatient = (int) patientField.GetValue( crew );

			// a better doctor works faster: 10 seconds at skill 250 is 50 points
			doctor.m_medicine = 250;
			hurt.m_vitality = 10.0f;

			updateTreatment.Invoke( crew, new object[] { 10.0f } );

			var hurtAfter10sAtSkill250 = hurt.m_vitality;

			// the dead are not treated
			updateTreatment.Invoke( crew, new object[] { 1000.0f } );

			worse.m_vitality = 0.0f;

			new TreatButton().Execute();

			var treatingTheDead = (bool) treatmentField.GetValue( crew );
			var nobodyMessage = MessageList();

			treat = "done=" + worseWhenDone.ToString( "F0" ) + "/" + hurtWhenWorseIsDone.ToString( "F0" ) + " next=" + ( ( nextPatient == hurt.m_fileId ) ? "Hurt" : nextPatient.ToString() ) + " 10s@250=" + hurtAfter10sAtSkill250.ToString( "F0" ) + " dead=" + treatingTheDead;

			Log( "M20 treat: " + treat + " | message when done: " + doneMessage + " | message with only a dead crew member left: " + nobodyMessage );

			Check( "M20 one patient per command, and the treatment stops at full health", ( worseWhenDone == 100.0f ) && ( hurtWhenWorseIsDone == 50.0f ) && !stillUnderWayWhenDone && doneMessage.Contains( "recovered" ), "Worse " + worseWhenDone.ToString( "F0" ) + ", Hurt " + hurtWhenWorseIsDone.ToString( "F0" ) + ", still under way " + stillUnderWayWhenDone );
			Check( "M20 the next command treats the next patient, and a better doctor is faster", ( nextPatient == hurt.m_fileId ) && ( hurtAfter10sAtSkill250 == 60.0f ), "next patient file " + nextPatient + " (Hurt is " + hurt.m_fileId + "), 10 s at skill 250 took Hurt from 10 to " + hurtAfter10sAtSkill250.ToString( "F0" ) );
			Check( "M20 a dead crew member is not treated", !treatingTheDead && nobodyMessage.Contains( "healthy" ), "treatment under way " + treatingTheDead + ", message: " + nobodyMessage );
		}

		Finish( "scenario=m20 repairOnThePress=" + ( armorRightAfter - 300 ) + " repairIn5s=" + ( armorAfter5s - 300 ) + " repair=[" + repair + "] treatOnThePress=[" + rightAfterTreat + "] treatIn5s=Worse" + worseAfter5s.ToString( "F1" ) + "/Hurt" + hurtAfter5s.ToString( "F1" ) + "/Ashore" + ashoreAfter5s.ToString( "F1" ) + " treat=[" + treat + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- H8: missiles are not counted, every shot uses a little endurium

	// the endurium on board, in the units the cargo hold counts (tenths of a cubic meter)
	static int Endurium()
	{
		var elementReference = DataController.m_instance.m_playerData.m_playerShip.m_elementStorage.Find( 5 );

		return ( elementReference == null ) ? 0 : elementReference.m_volume;
	}

	// fire a weapon the given number of times with no waiting for the cooldown, at a target that can take it
	static int FireRepeatedly( bool missile, int shots )
	{
		var combat = CombatController.m_instance;
		var fired = 0;

		for ( var i = 0; i < shots; i++ )
		{
			var target = FirstLivingAlien();

			if ( target < 0 )
			{
				break;
			}

			SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList()[ target ].m_armorPoints = 10000000;

			combat.SetTarget( target );

			SetField( combat, missile ? "m_playerMissileCooldown" : "m_playerLaserCooldown", 0.0f );

			// only eight missiles can be in the air at once, and a launch with none free does nothing (it used to cost fuel all the same) -
			// so the last missile is taken out of the air before the next one is launched
			if ( missile )
			{
				combat.ClearMissiles();
			}

			if ( missile ? combat.FirePlayerMissile() : combat.FirePlayerLaser() )
			{
				fired++;
			}
		}

		return fired;
	}

	IEnumerator ScenarioH8()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;

		EnsureCrew();

		// a new ship with a missile launcher and a laser cannon bought for it, and 10.0 cubic meters of endurium
		ship.m_laserCannonClass = 1;
		ship.m_missileLauncherClass = 1;
		ship.m_armorPoints = 100000;
		ship.m_shieldsAreUp = false;
		ship.m_fuelUsed = 0.0f;

		if ( Endurium() > 0 )
		{
			ship.RemoveElement( 5, Endurium() );
		}

		ship.AddElement( 5, 100 );

		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		BringAliensClose();

		// ---- the missile button on a ship that has never been given any missiles
		var label = new FireMissileButton().GetLabel();

		combat.SetTarget( FirstLivingAlien() );

		SpaceflightController.m_instance.m_messages.Clear();

		var buttonLaunched = new FireMissileButton().Execute();
		var buttonMessage = MessageList();

		Log( "H8 missile button: label \"" + label + "\", launched=" + buttonLaunched + ", message: " + buttonMessage );

		Check( "H8 a ship with a missile launcher can launch missiles", buttonLaunched && buttonMessage.Contains( "Missile launched" ) && !label.Contains( "(" ), "label \"" + label + "\", launched " + buttonLaunched + ", message: " + buttonMessage );

		// ---- fuel: 50 missiles and 100 laser shots
		ship.m_fuelUsed = 0.0f;

		BringAliensClose();

		var enduriumBeforeMissiles = Endurium();
		var missilesFired = FireRepeatedly( true, 50 );
		var usedByMissiles = enduriumBeforeMissiles - Endurium();

		BringAliensClose();

		var enduriumBeforeLasers = Endurium();
		var lasersFired = FireRepeatedly( false, 100 );
		var usedByLasers = enduriumBeforeLasers - Endurium();

		Log( "H8 fuel: " + missilesFired + " missiles used " + usedByMissiles + " tenths of a cubic meter, " + lasersFired + " laser shots used " + usedByLasers + " (endurium left " + Endurium() + ")" );

		Check( "H8 50 missiles use about 1.0 cubic meter of endurium", ( missilesFired == 50 ) && ( usedByMissiles >= 9 ) && ( usedByMissiles <= 10 ), missilesFired + " fired, " + usedByMissiles + " tenths used" );
		Check( "H8 100 laser shots use about 1.0 cubic meter of endurium", ( lasersFired == 100 ) && ( usedByLasers >= 9 ) && ( usedByLasers <= 10 ), lasersFired + " fired, " + usedByLasers + " tenths used" );

		// ---- no endurium, no weapons
		ship.RemoveElement( 5, Endurium() );

		BringAliensClose();

		combat.SetTarget( FirstLivingAlien() );

		SetField( combat, "m_playerLaserCooldown", 0.0f );
		SetField( combat, "m_playerMissileCooldown", 0.0f );

		SpaceflightController.m_instance.m_messages.Clear();

		var laserWithNoFuel = new FireLaserButton().Execute();
		var laserMessage = MessageList();

		SpaceflightController.m_instance.m_messages.Clear();

		var missileWithNoFuel = new FireMissileButton().Execute();
		var missileMessage = MessageList();

		Log( "H8 no endurium: laser fired=" + laserWithNoFuel + " (" + laserMessage + "), missile launched=" + missileWithNoFuel + " (" + missileMessage + ")" );

		Check( "H8 the weapons do not fire without endurium, and say why", !laserWithNoFuel && !missileWithNoFuel && laserMessage.Contains( "Insufficient fuel" ) && missileMessage.Contains( "Insufficient fuel" ), "laser " + laserWithNoFuel + " (" + laserMessage + "), missile " + missileWithNoFuel + " (" + missileMessage + ")" );

		Finish( "scenario=h8 label=[" + label + "] buttonLaunched=" + buttonLaunched + " missiles=" + missilesFired + "/" + usedByMissiles + " lasers=" + lasersFired + "/" + usedByLasers + " noFuel=laser:" + laserWithNoFuel + ",missile:" + missileWithNoFuel + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- planets: getting into orbit, onto the surface and into the terrain vehicle

	// wait until the planets of the current star system have been generated, then go into orbit around one of them (the maneuver button does the same when the player stops next to a planet)
	static IEnumerator EnterOrbit( int planetId )
	{
		var end = Time.realtimeSinceStartup + 60.0f;

		while ( ( Time.realtimeSinceStartup < end ) && SpaceflightController.m_instance.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 5 );

		DataController.m_instance.m_playerData.m_general.m_currentPlanetId = planetId;

		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.InOrbit );

		yield return Frames( 10 );
	}

	// wait until the player is in the given location (or the time is up)
	static IEnumerator WaitForLocation( PD_General.Location location, float seconds )
	{
		var end = Time.realtimeSinceStartup + seconds;

		while ( ( Time.realtimeSinceStartup < end ) && ( DataController.m_instance.m_playerData.m_general.m_location != location ) )
		{
			yield return null;
		}
	}

	// press one of the six console buttons: select it and activate it, as the button controller does 0.35 s after the fire button
	static void PressButton( ButtonController.ButtonSet buttonSet, int buttonIndex )
	{
		var buttonController = SpaceflightController.m_instance.m_buttonController;

		if ( buttonController.GetCurrentButtonSet() != buttonSet )
		{
			buttonController.ChangeButtonSet( buttonSet );
		}

		buttonController.SetSelectedButton( buttonIndex );
		buttonController.ActivateButton();
	}

	// where a scenario puts a deposit beside the terrain vehicle: in reach of the Cargo button (a pickup reaches 10, TerrainElement) and clear of the vehicle,
	// since every deposit pushes a vehicle it touches (TerrainVehicleCollider): the vehicle jumps back and forth every other frame, and now and then it keeps the
	// push and moves away, which took a deposit 2 in front of it out of reach in some runs of the cargo scenario. The vehicle is a capsule that reaches 4.9 ahead
	// of its middle and 2.2 to its sides, and a deposit is a sphere of up to 3.8 (Lead on planet 90) whose middle is up to 0.5 from where it stands, so nothing
	// in front of the vehicle is both in reach and clear of it: the deposit goes 8 to its right
	static Vector3 BesideTheVehicle( TerrainVehicle terrainVehicle )
	{
		var toTheRight = Vector3.Cross( Vector3.up, DataController.m_instance.m_playerData.m_general.m_currentDirection );

		if ( toTheRight.sqrMagnitude < 0.0001f )
		{
			toTheRight = Vector3.right;
		}

		return terrainVehicle.transform.position + toTheRight.normalized * 8.0f;
	}

	// how much of an element the terrain vehicle is carrying
	static int TerrainVehicleCargo( int elementId )
	{
		var elementStorage = DataController.m_instance.m_playerData.m_terrainVehicle.m_elementStorage;
		var elementReference = ( elementStorage == null ) ? null : elementStorage.Find( elementId );

		return ( elementReference == null ) ? 0 : elementReference.m_volume;
	}

	// ---------------------------------------------------------------- M10: a deposit can only be picked up once

	IEnumerator ScenarioM10()
	{
		var playerData = DataController.m_instance.m_playerData;

		EnsureCrew();

		// planet 90 is in the arth system and has a mineral density of 43%
		yield return EnterOrbit( 90 );

		var locationInOrbit = playerData.m_general.m_location;

		// land without the 35 second landing animation: bake the terrain and switch, which is what the descend button and the animation do between them
		SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		var locationPlanetside = playerData.m_general.m_location;

		// disembark with the real button (the second button of the command set on a planet)
		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		var locationDisembarked = playerData.m_general.m_location;

		Log( "M10 locations: " + locationInOrbit + " -> " + locationPlanetside + " -> " + locationDisembarked );

		if ( locationDisembarked != PD_General.Location.Disembarked )
		{
			Finish( "scenario=m10 abort: never got into the terrain vehicle (" + locationInOrbit + "/" + locationPlanetside + "/" + locationDisembarked + ")", 2 );
			yield break;
		}

		// find the deposits the planet was populated with
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;
		var container = SpaceflightController.m_instance.m_disembarked.m_terrainGrid.m_terrainElements.transform;
		var deposits = container.GetComponentsInChildren<TerrainElement>( true );

		if ( deposits.Length == 0 )
		{
			Finish( "scenario=m10 abort: the planet has no deposits", 2 );
			yield break;
		}

		// put one of them next to the terrain vehicle and everything else out of reach
		var deposit = deposits[ 0 ];

		foreach ( var other in deposits )
		{
			if ( ( other != deposit ) && ( Vector3.Distance( other.transform.position, terrainVehicle.transform.position ) < 50.0f ) )
			{
				other.transform.position += Vector3.right * 1000.0f;
			}
		}

		deposit.transform.position = BesideTheVehicle( terrainVehicle );

		var elementId = deposit.m_elementId;
		var volume = deposit.m_volume;
		var cargoBefore = TerrainVehicleCargo( elementId );

		// first press: the deposit goes into the cargo hold
		new TVCargoButton().Execute();

		var cargoAfterFirstPress = TerrainVehicleCargo( elementId );
		var firstMessage = MessageList();

		// second press half a second later, while the transporter effect is still playing (it takes 1.5 s)
		yield return new WaitForSecondsRealtime( 0.5f );

		var depositStillThere = ( deposit != null );

		new TVCargoButton().Execute();

		var cargoAfterSecondPress = TerrainVehicleCargo( elementId );
		var secondMessage = MessageList();

		// third press after the effect has finished and the deposit is gone
		yield return new WaitForSecondsRealtime( 2.5f );

		var depositGone = ( deposit == null );

		new TVCargoButton().Execute();

		var cargoAfterThirdPress = TerrainVehicleCargo( elementId );

		Log( "M10 " + deposits.Length + " deposits on the planet; the one next to the vehicle is " + volume + " cubic meters of element " + elementId + ". Cargo of that element: before " + cargoBefore + ", after the first press " + cargoAfterFirstPress + ", after a second press 0.5 s later " + cargoAfterSecondPress + " (deposit object still there: " + depositStillThere + "), after a third press 3 s later " + cargoAfterThirdPress + " (deposit object gone: " + depositGone + ")" );
		Log( "M10 message after the first press: " + firstMessage );
		Log( "M10 message after the second press: " + secondMessage );

		Check( "M10 the first press picks the deposit up", cargoAfterFirstPress == cargoBefore + volume, "cargo " + cargoBefore + " -> " + cargoAfterFirstPress + " for a deposit of " + volume );
		Check( "M10 a second press during the transporter effect does not pick it up again", depositStillThere && ( cargoAfterSecondPress == cargoAfterFirstPress ), "deposit object still there " + depositStillThere + ", cargo " + cargoAfterFirstPress + " -> " + cargoAfterSecondPress );
		Check( "M10 the deposit is gone once the effect has finished", depositGone && ( cargoAfterThirdPress == cargoAfterSecondPress ), "deposit object gone " + depositGone + ", cargo " + cargoAfterSecondPress + " -> " + cargoAfterThirdPress );

		Finish( "scenario=m10 deposit=" + volume + " cargo=" + cargoBefore + "/" + cargoAfterFirstPress + "/" + cargoAfterSecondPress + "/" + cargoAfterThirdPress + " depositStillThereAtSecondPress=" + depositStillThere + " depositGoneAtThirdPress=" + depositGone + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M23: the console is locked while the ship is landing

	// hold one of the input controller's buttons or stick directions down (or let it go) for the code that reads it next
	static void SetInput( string name, bool value )
	{
		typeof( InputController ).GetProperty( name ).SetValue( InputController.m_instance, value );
	}

	// the state of the button console: the button set, the selected button and the button whose function is running
	static string Console()
	{
		var buttonController = SpaceflightController.m_instance.m_buttonController;
		var currentButton = GetField( buttonController, "m_currentButton" );

		return buttonController.GetCurrentButtonSet() + " selected " + GetField( buttonController, "m_selectedButtonIndex" ) + " running " + ( ( currentButton == null ) ? "nothing" : currentButton.GetType().Name );
	}

	// one frame of the button controller with the stick held down, or with the fire button held down
	static void ConsoleFrameWith( string input )
	{
		SetInput( input, true );

		Call( SpaceflightController.m_instance.m_buttonController, "Update" );

		SetInput( input, false );
	}

	IEnumerator ScenarioM23()
	{
		var playerData = DataController.m_instance.m_playerData;
		var buttonController = SpaceflightController.m_instance.m_buttonController;

		EnsureCrew();

		// planet 94 is a small rock planet in the arth system
		yield return EnterOrbit( 94 );

		// Land (the first button of the command set in orbit), then Descend (the second button of the land set)
		PressButton( ButtonController.ButtonSet.CommandB, 0 );

		yield return Frames( 5 );

		PressButton( ButtonController.ButtonSet.Land, 1 );

		var start = Time.realtimeSinceStartup;

		yield return Frames( 5 );

		var consoleAfterDescend = Console();

		// two seconds into the landing the player pushes the stick down...
		yield return new WaitForSecondsRealtime( 2.0f );

		ConsoleFrameWith( "m_south" );

		var consoleAfterStick = Console();

		// ...and presses the fire button (a press takes 0.35 s to activate the selected button)
		ConsoleFrameWith( "m_submit" );

		yield return new WaitForSecondsRealtime( 1.0f );

		var consoleAfterFire = Console();
		var locationAfterFire = playerData.m_general.m_location;

		Log( "M23 after Descend: " + consoleAfterDescend + " | stick down 2 s into the landing: " + consoleAfterStick + " | fire button, 1 s later: " + consoleAfterFire + " (" + locationAfterFire + ")" );

		Check( "M23 the stick does not move the selection during the landing", consoleAfterStick == consoleAfterDescend, "after Descend: " + consoleAfterDescend + ", after the stick: " + consoleAfterStick );
		Check( "M23 the fire button does not activate anything during the landing", consoleAfterFire == consoleAfterDescend, "after Descend: " + consoleAfterDescend + ", after the fire button: " + consoleAfterFire );

		// wait for the ship to be down (the landing animation calls PlayerHasLanded, which brings up the command buttons)
		var end = Time.realtimeSinceStartup + 50.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !( ( playerData.m_general.m_location == PD_General.Location.Planetside ) && ( buttonController.GetCurrentButtonSet() == ButtonController.ButtonSet.CommandA ) ) )
		{
			yield return null;
		}

		var landingTime = Time.realtimeSinceStartup - start;
		var consoleAfterLanding = Console();
		var locationAfterLanding = playerData.m_general.m_location;
		var messagesAfterLanding = MessageList();

		// the controls have to work again once the ship is down
		yield return Frames( 5 );

		ConsoleFrameWith( "m_south" );

		var consoleAfterLandingStick = Console();

		Log( "M23 " + landingTime.ToString( "F1" ) + " s after Descend: " + locationAfterLanding + ", " + consoleAfterLanding + " | stick down: " + consoleAfterLandingStick );
		Log( "M23 messages: " + messagesAfterLanding );

		Check( "M23 the landing finishes and gives the controls back", ( locationAfterLanding == PD_General.Location.Planetside ) && ( consoleAfterLanding == "CommandA selected 0 running nothing" ) && ( consoleAfterLandingStick == "CommandA selected 1 running nothing" ), locationAfterLanding + " after " + landingTime.ToString( "F1" ) + " s, " + consoleAfterLanding + ", after the stick: " + consoleAfterLandingStick );

		// the message box during the landing: every step once, in the order it happens (the original's lines, ORBIT-OV DESCEND)
		var plainMessages = System.Text.RegularExpressions.Regex.Replace( messagesAfterLanding, "<[^>]+>", "" );

		Check( "M23 the landing reports each step once and in order", plainMessages == "Computing descent profile... / Autopilot engaged. Descending... / Topography net locked on. / Safe landing, captain.", plainMessages );

		Finish( "scenario=m23 afterDescend=[" + consoleAfterDescend + "] afterStick=[" + consoleAfterStick + "] afterFire=[" + consoleAfterFire + "] landed=" + locationAfterLanding + "@" + landingTime.ToString( "F1" ) + "s afterLanding=[" + consoleAfterLanding + "] afterLandingStick=[" + consoleAfterLandingStick + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- missiles do not outlive the encounter they were fired in

	// how many missiles are in the air
	static int MissilesInFlight()
	{
		var missilePool = GetField( CombatController.m_instance, "m_missilePool" ) as List<MissileProjectile>;
		var count = 0;

		if ( missilePool != null )
		{
			foreach ( var missile in missilePool )
			{
				if ( missile.IsActive() )
				{
					count++;
				}
			}
		}

		return count;
	}

	// the armor and shield points an alien ship of the current encounter has left
	static int AlienPoints( int alienIndex )
	{
		var alienShip = SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList()[ alienIndex ];

		return alienShip.m_armorPoints + alienShip.m_shieldPoints;
	}

	IEnumerator ScenarioMissiles()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;

		EnsureCrew();

		// a ship with a missile launcher that survives whatever hits it
		ship.m_missileLauncherClass = 1;
		ship.m_armorPoints = 100000;
		ship.m_shieldsAreUp = false;

		if ( ship.m_elementStorage.Find( 5 ) == null )
		{
			ship.AddElement( 5, 50 );
		}

		var uhlekId = FindEncounter( 0, 1, 1, 0, GameData.Race.Uhlek );
		var speminIdA = FindEncounter( 1, 6, 3, 0 );
		var speminIdB = FindEncounter( 1, 6, 3, 1 );

		// all spemin scouts: 250 armor points, no shields, lasers only (so the only missiles in the air are the ones this scenario is about)
		ForceVessel( speminIdA, 2 );
		ForceVessel( speminIdB, 2 );

		// ---- inside an encounter an alien missile still arrives: the uhlek attack on sight, wait for their first missile to hit
		EnterEncounter( uhlekId );
		yield return Frames( 5 );

		var armorAtStart = ship.m_armorPoints;
		var end = Time.realtimeSinceStartup + 20.0f;

		while ( ( Time.realtimeSinceStartup < end ) && ( ship.m_armorPoints == armorAtStart ) )
		{
			BringAliensClose();

			yield return null;
		}

		var damageInsideTheEncounter = armorAtStart - ship.m_armorPoints;

		// ---- leave at the moment the next uhlek missile is in the air, and go straight into another encounter
		end = Time.realtimeSinceStartup + 20.0f;

		while ( ( Time.realtimeSinceStartup < end ) && ( MissilesInFlight() == 0 ) )
		{
			BringAliensClose();

			yield return null;
		}

		var inFlightWhenLeaving = MissilesInFlight();
		var armorWhenLeaving = ship.m_armorPoints;

		LeaveEncounter();

		var inFlightAfterLeaving = MissilesInFlight();

		EnterEncounter( speminIdA );

		yield return new WaitForSecondsRealtime( 4.0f );

		var damageInTheNextEncounter = armorWhenLeaving - ship.m_armorPoints;
		var stanceInTheNextEncounter = Stance();

		Log( "missiles: the first uhlek missile did " + damageInsideTheEncounter + " inside the encounter. Left with " + inFlightWhenLeaving + " in the air, " + inFlightAfterLeaving + " right after leaving; damage taken in the next encounter (spemin, " + stanceInTheNextEncounter + ") in 4 s: " + damageInTheNextEncounter );

		Check( "missiles: an alien missile still hits inside its own encounter", damageInsideTheEncounter > 0, "damage " + damageInsideTheEncounter );
		Check( "missiles: an alien missile in the air when the player leaves does not follow into the next encounter", ( inFlightWhenLeaving > 0 ) && ( inFlightAfterLeaving == 0 ) && ( damageInTheNextEncounter == 0 ), "in the air when leaving " + inFlightWhenLeaving + ", after leaving " + inFlightAfterLeaving + ", damage in the next encounter " + damageInTheNextEncounter );

		// ---- inside an encounter a player missile still arrives: fire at a ship 300 away and wait for it to land
		var target = FirstLivingAlien();

		BringAliensClose();

		combat.SetTarget( target );

		var pointsBeforeCloseShot = AlienPoints( target );
		var closeShotFired = combat.FirePlayerMissile();

		end = Time.realtimeSinceStartup + 6.0f;

		while ( ( Time.realtimeSinceStartup < end ) && ( AlienPoints( target ) == pointsBeforeCloseShot ) )
		{
			BringAliensClose();

			yield return null;
		}

		var closeShotDamage = pointsBeforeCloseShot - AlienPoints( target );

		// ---- fire at a ship 1200 away, leave at once, and go straight into another encounter
		yield return new WaitForSecondsRealtime( 0.5f );

		var alienShip = EncounterShip( target );

		alienShip.m_coordinates = playerData.m_general.m_coordinates + Vector3.forward * 1200.0f;
		alienShip.m_targetCoordinates = alienShip.m_coordinates;

		yield return Frames( 3 );

		combat.SetTarget( target );

		SetField( combat, "m_playerMissileCooldown", 0.0f );

		var farShotFired = combat.FirePlayerMissile();
		var playerMissilesWhenLeaving = MissilesInFlight();

		LeaveEncounter();

		var playerMissilesAfterLeaving = MissilesInFlight();

		EnterEncounter( speminIdB );
		yield return Frames( 3 );

		var pointsInTheNextEncounter = AlienPoints( target );

		// a missile lives for 5 seconds at most
		end = Time.realtimeSinceStartup + 5.5f;

		while ( Time.realtimeSinceStartup < end )
		{
			BringAliensClose();

			yield return null;
		}

		var damageToTheNextEncounter = pointsInTheNextEncounter - AlienPoints( target );

		Log( "missiles: a player missile at a ship 300 away did " + closeShotDamage + " (fired=" + closeShotFired + "). Fired at a ship 1200 away (fired=" + farShotFired + ") and left with " + playerMissilesWhenLeaving + " in the air, " + playerMissilesAfterLeaving + " right after leaving; ship " + target + " of the next encounter lost " + damageToTheNextEncounter + " points in 5.5 s" );

		Check( "missiles: a player missile still hits inside its own encounter", closeShotFired && ( closeShotDamage > 0 ), "fired " + closeShotFired + ", damage " + closeShotDamage );
		Check( "missiles: a player missile in the air when the player leaves does not hit a ship of the next encounter", farShotFired && ( playerMissilesWhenLeaving > 0 ) && ( playerMissilesAfterLeaving == 0 ) && ( damageToTheNextEncounter == 0 ), "fired " + farShotFired + ", in the air when leaving " + playerMissilesWhenLeaving + ", after leaving " + playerMissilesAfterLeaving + ", damage to ship " + target + " of the next encounter " + damageToTheNextEncounter );

		Finish( "scenario=missiles alienMissileInside=" + damageInsideTheEncounter + " alienMissileCarriedOver=" + inFlightWhenLeaving + "/" + inFlightAfterLeaving + "/" + damageInTheNextEncounter + " playerMissileInside=" + closeShotDamage + " playerMissileCarriedOver=" + playerMissilesWhenLeaving + "/" + playerMissilesAfterLeaving + "/" + damageToTheNextEncounter + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	static PD_AlienShip EncounterShip( int alienIndex )
	{
		return SpaceflightController.m_instance.m_encounter.m_pdEncounter.GetAlienShipList()[ alienIndex ];
	}

	// ---------------------------------------------------------------- M11: one source of truth for the maximum armor and shield points

	// what the status display shows for the ship as it is right now: the damage line of its text and how full its two gauges are
	static string StatusDamageLine( out float armorGauge, out float shieldGauge )
	{
		var display = SpaceflightController.m_instance.m_displayController.m_statusDisplay;

		display.Update();

		armorGauge = display.m_armorGauge.anchorMax.y;
		shieldGauge = display.m_shieldGauge.anchorMax.y;

		// the lines are: date, damage, cargo, energy, shields, weapons
		var lines = display.m_values.text.Split( '\n' );

		return ( lines.Length > 1 ) ? lines[ 1 ] : "";
	}

	// hit the hull (shields down) and tell whether the hull breach warning came with it
	static bool HitWarns( int armorBefore, int damage )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		ship.m_shieldsAreUp = false;
		ship.m_armorPoints = armorBefore;

		SpaceflightController.m_instance.m_messages.Clear();

		CombatController.m_instance.ApplyDamageToPlayer( damage, Vector3.forward );

		return MessageList().Contains( "Hull breach imminent" );
	}

	IEnumerator ScenarioM11()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;

		EnsureCrew();

		yield return Frames( 5 );

		// ---- 1. the ship of a new game: no armor plating (the bare hull has 250 points), no shields, nothing damaged
		var newShip = "class " + ship.m_armorClass + " armor with " + ship.m_armorPoints + " points, class " + ship.m_shieldingClass + " shields with " + ship.m_shieldPoints;

		var newDamageLine = StatusDamageLine( out var newArmorGauge, out var newShieldGauge );

		messages.Clear();
		new DamageButton().Execute();

		var newReport = MessageList();

		Log( "M11 new ship (" + newShip + "): status damage line '" + newDamageLine + "', armor gauge " + newArmorGauge.ToString( "F3" ) + ", shield gauge " + newShieldGauge.ToString( "F3" ) );
		Log( "M11 new ship damage report: " + newReport );

		Check( "M11 an undamaged new ship shows no damage", newDamageLine == "None", "status damage line '" + newDamageLine + "'" );
		Check( "M11 an undamaged new ship has a full armor gauge and an empty shield gauge", Mathf.Approximately( newArmorGauge, 1.0f ) && ( newShieldGauge == 0.0f ), "armor gauge " + newArmorGauge.ToString( "F3" ) + ", shield gauge " + newShieldGauge.ToString( "F3" ) );
		Check( "M11 the damage report gives the bare hull its 250 points", newReport.Contains( "(250/250)" ), newReport );

		// ---- 2. the bare hull damaged: 200 of 250, and the engineer (skill 100, so 2 points a second) repairs it
		ship.m_armorPoints = 200;

		var hurtDamageLine = StatusDamageLine( out var hurtArmorGauge, out _ );

		// the engineer asks for the hull's repair mineral (since roadmap 1.2): aluminum in the hold, and yes
		ship.AddElement( 0, 200 );

		messages.Clear();
		new RepairButton().Execute();

		AnswerRepairQuestion( true );

		var repairMessage = MessageList();
		var repairsStarted = ship.m_repairsAreUnderWay;

		ship.UpdateRepairs( 10.0f );

		var armorAfter10s = ship.m_armorPoints;

		ship.UpdateRepairs( 10000.0f );

		var armorWhenDone = ship.m_armorPoints;
		var stillUnderWay = ship.m_repairsAreUnderWay;
		var doneMessage = MessageList();

		messages.Clear();
		new RepairButton().Execute();

		var nothingToRepairMessage = MessageList();
		var startedWithNothingToRepair = ship.m_repairsAreUnderWay;

		ship.m_repairsAreUnderWay = false;

		Log( "M11 bare hull at 200/250: status damage line '" + hurtDamageLine + "', armor gauge " + hurtArmorGauge.ToString( "F3" ) + " | repair started " + repairsStarted + ", after 10 s " + armorAfter10s + ", when done " + armorWhenDone + " (still under way " + stillUnderWay + ")" );
		Log( "M11 bare hull repair message: " + repairMessage );
		Log( "M11 bare hull message when done: " + doneMessage );
		Log( "M11 bare hull repair message with nothing to repair: " + nothingToRepairMessage );

		Check( "M11 a bare hull at 200 of 250 shows slight damage (the original's word for four fifths of the hull) and a gauge at four fifths", hurtDamageLine.Contains( ">Slight<" ) && Mathf.Approximately( hurtArmorGauge, 0.8f ), "status damage line '" + hurtDamageLine + "', armor gauge " + hurtArmorGauge.ToString( "F3" ) );
		Check( "M11 the engineer repairs a bare hull", repairsStarted && ( armorAfter10s == 220 ), "repair started " + repairsStarted + ", armor after 10 s " + armorAfter10s + " (was 200) | " + repairMessage );
		Check( "M11 the repair of a bare hull stops at its 250 points", ( armorWhenDone == 250 ) && !stillUnderWay && doneMessage.Contains( "completed" ), "armor when done " + armorWhenDone + ", still under way " + stillUnderWay + " | " + doneMessage );
		Check( "M11 an undamaged bare hull needs no repairs", !startedWithNothingToRepair && nothingToRepairMessage.Contains( "No repairs" ), "started " + startedWithNothingToRepair + " | " + nothingToRepairMessage );

		// ---- 3. class 2 armor (750 points): undamaged, then down to 300
		ship.m_armorClass = 2;
		ship.m_armorPoints = 750;

		var class2FullDamageLine = StatusDamageLine( out var class2FullArmorGauge, out _ );

		ship.m_armorPoints = 300;

		var class2HurtDamageLine = StatusDamageLine( out var class2HurtArmorGauge, out _ );

		messages.Clear();
		new DamageButton().Execute();

		var class2Report = MessageList();

		Log( "M11 class 2 armor: at 750/750 status damage line '" + class2FullDamageLine + "', armor gauge " + class2FullArmorGauge.ToString( "F3" ) + " | at 300/750 '" + class2HurtDamageLine + "', armor gauge " + class2HurtArmorGauge.ToString( "F3" ) );
		Log( "M11 class 2 armor damage report at 300/750: " + class2Report );

		Check( "M11 undamaged class 2 armor shows no damage and a full gauge", ( class2FullDamageLine == "None" ) && Mathf.Approximately( class2FullArmorGauge, 1.0f ), "status damage line '" + class2FullDamageLine + "', armor gauge " + class2FullArmorGauge.ToString( "F3" ) );
		Check( "M11 class 2 armor at 300 of 750 shows heavy damage (the original's word for two fifths of the hull) and a gauge at two fifths", class2HurtDamageLine.Contains( ">Heavy<" ) && Mathf.Approximately( class2HurtArmorGauge, 0.4f ), "status damage line '" + class2HurtDamageLine + "', armor gauge " + class2HurtArmorGauge.ToString( "F3" ) );
		Check( "M11 the damage report still gives installed armor its own points", class2Report.Contains( "40% (300/750)" ), class2Report );

		// ---- 4. shields: class 1 (500 points) half charged, class 5 (2500 points) fully charged, and none
		ship.m_shieldingClass = 1;
		ship.m_shieldPoints = 250;

		StatusDamageLine( out _, out var halfShieldGauge );

		ship.m_shieldingClass = 5;
		ship.m_shieldPoints = 2500;

		StatusDamageLine( out _, out var fullShieldGauge );

		messages.Clear();
		new DamageButton().Execute();

		var shieldReport = MessageList();

		ship.m_shieldingClass = 0;
		ship.m_shieldPoints = 0;

		StatusDamageLine( out _, out var noShieldGauge );

		Log( "M11 shield gauge: class 1 at 250/500 " + halfShieldGauge.ToString( "F3" ) + ", class 5 at 2500/2500 " + fullShieldGauge.ToString( "F3" ) + ", no shields " + noShieldGauge.ToString( "F3" ) );
		Log( "M11 damage report with class 5 shields: " + shieldReport );

		Check( "M11 the shield gauge shows the charge of the installed shielding", Mathf.Approximately( halfShieldGauge, 0.5f ) && Mathf.Approximately( fullShieldGauge, 1.0f ) && ( noShieldGauge == 0.0f ), "class 1 at 250/500 " + halfShieldGauge.ToString( "F3" ) + ", class 5 at 2500/2500 " + fullShieldGauge.ToString( "F3" ) + ", none " + noShieldGauge.ToString( "F3" ) );
		Check( "M11 the damage report still gives installed shielding its own points", shieldReport.Contains( "100% (2500/2500)" ), shieldReport );

		// ---- 5. the hull breach warning comes when less than a quarter of the armor points are left
		ship.m_armorClass = 0;

		var bareScratchWarned = HitWarns( 250, 10 );
		var bareLowWarned = HitWarns( 70, 10 );

		ship.m_armorClass = 5;

		var class5AboveWarned = HitWarns( 400, 10 );
		var class5BelowWarned = HitWarns( 310, 10 );

		ship.m_armorClass = 0;
		ship.m_armorPoints = 250;

		Log( "M11 hull breach warning: bare hull 250 to 240 " + bareScratchWarned + ", bare hull 70 to 60 " + bareLowWarned + ", class 5 armor (1500) 400 to 390 " + class5AboveWarned + ", 310 to 300 " + class5BelowWarned );

		Check( "M11 a scratch on a bare hull does not set off the hull breach warning", !bareScratchWarned && bareLowWarned, "250 to 240 warned " + bareScratchWarned + ", 70 to 60 warned " + bareLowWarned );
		Check( "M11 class 5 armor warns below a quarter of its 1500 points", !class5AboveWarned && class5BelowWarned, "400 to 390 warned " + class5AboveWarned + ", 310 to 300 warned " + class5BelowWarned );

		Finish( "scenario=m11 newShip=[" + newDamageLine + " gauge " + newArmorGauge.ToString( "F3" ) + "] bareHullRepair=" + repairsStarted + "/" + armorAfter10s + "/" + armorWhenDone + " class2=[" + class2FullDamageLine + " " + class2FullArmorGauge.ToString( "F3" ) + " | " + class2HurtArmorGauge.ToString( "F3" ) + "] shieldGauge=" + halfShieldGauge.ToString( "F3" ) + "/" + fullShieldGauge.ToString( "F3" ) + "/" + noShieldGauge.ToString( "F3" ) + " warned=" + bareScratchWarned + "/" + bareLowWarned + "/" + class5AboveWarned + "/" + class5BelowWarned + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M18: generating the planet maps does not hold up the main thread

	// make the star system generate the maps of its planets again, the way entering the system does
	static void RegeneratePlanets()
	{
		var starSystem = SpaceflightController.m_instance.m_starSystem;

		// the star system only generates maps when the star has changed, so make it forget which star it has
		SetField( starSystem, "m_currentStar", null );

		starSystem.Initialize();
	}

	// the background task of the planet that is being processed right now (null if no planet is)
	static System.Threading.Tasks.Task RunningPlanetTask()
	{
		foreach ( var planetController in SpaceflightController.m_instance.m_starSystem.m_planetController )
		{
			var generator = planetController.GetPlanetGenerator();

			if ( generator == null )
			{
				continue;
			}

			var task = GetField( generator, "m_asyncTask" ) as System.Threading.Tasks.Task;

			if ( ( task != null ) && !task.IsCompleted )
			{
				return task;
			}
		}

		return null;
	}

	IEnumerator ScenarioM18()
	{
		var starSystem = SpaceflightController.m_instance.m_starSystem;
		var popup = PopupController.m_instance;

		EnsureCrew();

		// let the generation that began with the scene run to its end
		var deadline = Time.realtimeSinceStartup + 45.0f;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup < deadline ) )
		{
			yield return null;
		}

		if ( starSystem.GeneratingPlanets() )
		{
			Finish( "scenario=m18 abort: the first generation never finished", 2 );
			yield break;
		}

		// generate the maps of the same system again and watch the main thread while it happens
		RegeneratePlanets();

		var start = Time.realtimeSinceStartup;
		var lastFrameTime = start;

		var frames = 0;
		var longestFrame = 0.0f;

		// frames that began while a planet was being processed in the background
		var framesWithProcessing = 0;
		var longestFrameWithProcessing = 0.0f;
		var secondsWithProcessing = 0.0f;

		// the progress bar of the popup (in thousandths) at the end of each of those frames
		var fillValues = new HashSet<int>();

		var planetsProcessed = 0;

		System.Threading.Tasks.Task runningTask = null;
		System.Threading.Tasks.Task lastTask = null;

		// how long the planet that is being processed has taken so far, and the longest any planet took
		var planetSeconds = 0.0f;
		var longestPlanetSeconds = 0.0f;

		var taskStart = start;
		var lastCollections = GC.CollectionCount( 0 );
		var lastHeap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
		var lastUsed = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();

		Log( "M18 incremental garbage collector " + UnityEngine.Scripting.GarbageCollector.isIncremental + ", collections so far " + lastCollections + ", managed heap " + ( lastHeap >> 20 ) + " MB, used " + ( lastUsed >> 20 ) + " MB" );

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 60.0f ) )
		{
			yield return null;

			var now = Time.realtimeSinceStartup;
			var frameTime = now - lastFrameTime;

			lastFrameTime = now;

			frames++;
			longestFrame = Mathf.Max( longestFrame, frameTime );

			var collections = GC.CollectionCount( 0 );

			// was a planet being processed when this frame began?
			if ( runningTask != null )
			{
				framesWithProcessing++;
				secondsWithProcessing += frameTime;
				longestFrameWithProcessing = Mathf.Max( longestFrameWithProcessing, frameTime );

				planetSeconds += frameTime;
				longestPlanetSeconds = Mathf.Max( longestPlanetSeconds, planetSeconds );

				fillValues.Add( Mathf.RoundToInt( popup.m_popupFill.anchorMax.x * 1000.0f ) );

				// say what the long frames are (the garbage collector stops every thread, and the processing allocates a lot)
				if ( frameTime > 0.05f )
				{
					Log( "M18 long frame " + frames + " while planet " + planetsProcessed + " was being processed: " + frameTime.ToString( "F3" ) + " s, " + ( now - taskStart ).ToString( "F3" ) + " s after its task was seen, garbage collections during the frame " + ( collections - lastCollections ) + " (total " + collections + "), managed heap " + ( lastHeap >> 20 ) + " to " + ( UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20 ) + " MB, used " + ( lastUsed >> 20 ) + " to " + ( UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20 ) + " MB, progress bar " + popup.m_popupFill.anchorMax.x.ToString( "F3" ) + ", task done by the end of the frame " + runningTask.IsCompleted );
				}
			}

			// say in which frame the collector counted a collection (to tell whether the long frames are its pauses)
			if ( collections != lastCollections )
			{
				Log( "M18 garbage collection " + collections + " was counted in frame " + frames + " (" + frameTime.ToString( "F3" ) + " s, planet " + planetsProcessed + ( ( runningTask != null ) ? " being processed" : " not being processed" ) + "), used " + ( lastUsed >> 20 ) + " to " + ( UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20 ) + " MB" );
			}

			lastCollections = collections;
			lastHeap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
			lastUsed = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();

			runningTask = RunningPlanetTask();

			if ( ( runningTask != null ) && !ReferenceEquals( runningTask, lastTask ) )
			{
				lastTask = runningTask;
				taskStart = now;
				planetSeconds = 0.0f;
				planetsProcessed++;
			}
		}

		var seconds = Time.realtimeSinceStartup - start;
		var finished = !starSystem.GeneratingPlanets();

		// did every planet of the system get its maps?
		var planetsWithMaps = 0;
		var planetsInSystem = 0;

		foreach ( var planetController in starSystem.m_planetController )
		{
			if ( planetController.m_planet == null )
			{
				continue;
			}

			planetsInSystem++;

			var generator = planetController.GetPlanetGenerator();

			if ( ( generator != null ) && generator.m_mapsGenerated && ( generator.m_albedoTexture != null ) && ( generator.m_normalTexture != null ) )
			{
				planetsWithMaps++;
			}
		}

		var averageSecondsPerPlanet = ( planetsProcessed > 0 ) ? ( secondsWithProcessing / planetsProcessed ) : 0.0f;

		Log( "M18 generating the maps of " + planetsInSystem + " planets took " + seconds.ToString( "F2" ) + " s and " + frames + " frames (longest frame " + longestFrame.ToString( "F3" ) + " s), finished " + finished + ", planets with maps " + planetsWithMaps + ", paused afterwards " + SpaceflightController.m_instance.m_gameIsPaused );
		Log( "M18 while a planet was being processed in the background (" + planetsProcessed + " planets, " + secondsWithProcessing.ToString( "F2" ) + " s, " + averageSecondsPerPlanet.ToString( "F2" ) + " s a planet): " + framesWithProcessing + " frames, longest " + longestFrameWithProcessing.ToString( "F3" ) + " s, " + fillValues.Count + " different positions of the progress bar" );

		Check( "M18 the generation finishes and every planet gets its maps", finished && ( planetsInSystem > 0 ) && ( planetsWithMaps == planetsInSystem ) && !SpaceflightController.m_instance.m_gameIsPaused, "finished " + finished + ", planets with maps " + planetsWithMaps + " of " + planetsInSystem + ", paused " + SpaceflightController.m_instance.m_gameIsPaused );
		Check( "M18 the main thread keeps running frames while a planet is processed", ( planetsProcessed > 0 ) && ( framesWithProcessing >= planetsProcessed * 10 ), framesWithProcessing + " frames for " + planetsProcessed + " planets" );
		// waiting for the task made one frame last as long as the whole planet, so the longest frame was the slowest planet (what is left with the fix are pauses of the garbage collector)
		Check( "M18 no frame lasts as long as the processing of a planet", ( planetsProcessed > 0 ) && ( longestFrameWithProcessing < longestPlanetSeconds * 0.75f ), "longest frame " + longestFrameWithProcessing.ToString( "F3" ) + " s, the slowest planet took " + longestPlanetSeconds.ToString( "F3" ) + " s (" + averageSecondsPerPlanet.ToString( "F2" ) + " s on average)" );
		Check( "M18 the progress bar moves while a planet is processed", fillValues.Count >= planetsProcessed * 3, fillValues.Count + " different positions for " + planetsProcessed + " planets" );

		Finish( "scenario=m18 finished=" + finished + " planets=" + planetsWithMaps + "/" + planetsInSystem + " seconds=" + seconds.ToString( "F2" ) + " frames=" + frames + " processing=[planets " + planetsProcessed + " frames " + framesWithProcessing + " longestFrame " + longestFrameWithProcessing.ToString( "F3" ) + " perPlanet " + averageSecondsPerPlanet.ToString( "F2" ) + " barPositions " + fillValues.Count + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M17: a planet file that cannot be read aborts that planet and nothing else

	static byte[] Decompress( byte[] bytes )
	{
		using ( var input = new System.IO.MemoryStream( bytes ) )
		using ( var gzip = new System.IO.Compression.GZipStream( input, System.IO.Compression.CompressionMode.Decompress ) )
		using ( var output = new System.IO.MemoryStream() )
		{
			gzip.CopyTo( output );

			return output.ToArray();
		}
	}

	static byte[] Compress( byte[] bytes )
	{
		using ( var output = new System.IO.MemoryStream() )
		{
			using ( var gzip = new System.IO.Compression.GZipStream( output, System.IO.Compression.CompressionMode.Compress ) )
			{
				gzip.Write( bytes, 0, bytes.Length );
			}

			return output.ToArray();
		}
	}

	static byte[] FirstBytes( byte[] bytes, int count )
	{
		var result = new byte[ count ];

		Array.Copy( bytes, result, count );

		return result;
	}

	// what became of one planet generator that was given some bytes in place of its planet file
	class PlanetBytesResult
	{
		public int m_calls;
		public int m_throws;
		public string m_firstException = "none";
		public bool m_abort;
		public bool m_mapsGenerated;
		public bool m_hasTextures;
		public float m_seconds;

		public override string ToString()
		{
			return "threw " + m_throws + " times in " + m_calls + " calls (" + m_firstException + ") abort " + m_abort + " mapsGenerated " + m_mapsGenerated + " textures " + m_hasTextures + " in " + m_seconds.ToString( "F2" ) + " s";
		}
	}

	// start the processing of a planet generator the way its step 1 does, but with the given bytes in place of the planet file
	static void StartProcessing( PlanetGenerator generator, byte[] bytes )
	{
		SetField( generator, "m_step", 2 );
		SetField( generator, "m_asyncTask", System.Threading.Tasks.Task.Run( () => generator.AsyncProcess( bytes ) ) );
	}

	// give a new planet generator these bytes and call it once a frame, the way the game does while the popup is up
	IEnumerator ProcessBytes( GD_Planet planet, byte[] bytes, PlanetBytesResult result )
	{
		var generator = new PlanetGenerator();

		generator.Start( planet );

		StartProcessing( generator, bytes );

		var task = GetField( generator, "m_asyncTask" ) as System.Threading.Tasks.Task;
		var start = Time.realtimeSinceStartup;
		var callsAfterTheTask = 0;

		// give up 100 calls after the task has ended (a generator that has not made up its mind by then never will), or after 15 seconds
		while ( ( callsAfterTheTask < 100 ) && ( Time.realtimeSinceStartup - start < 15.0f ) )
		{
			result.m_calls++;

			if ( task.IsCompleted )
			{
				callsAfterTheTask++;
			}

			var threw = false;

			try
			{
				generator.Process();
			}
			catch ( Exception exception )
			{
				threw = true;

				result.m_throws++;

				if ( result.m_throws == 1 )
				{
					var inner = exception.GetBaseException();

					result.m_firstException = exception.GetType().Name + " / " + inner.GetType().Name + ": " + inner.Message;
				}
			}

			// the game only looks at the generator when the call came back (an exception ends the frame of the spaceflight controller)
			if ( !threw && ( generator.m_abort || generator.m_mapsGenerated ) )
			{
				break;
			}

			yield return null;
		}

		result.m_abort = generator.m_abort;
		result.m_mapsGenerated = generator.m_mapsGenerated;
		result.m_hasTextures = ( generator.m_albedoTexture != null ) && ( generator.m_normalTexture != null );
		result.m_seconds = Time.realtimeSinceStartup - start;

		// this generator was only for the test
		foreach ( var texture in new Texture2D[] { generator.m_albedoTexture, generator.m_specularTexture, generator.m_normalTexture, generator.m_waterMaskTexture } )
		{
			if ( texture != null )
			{
				Destroy( texture );
			}
		}
	}

	static int s_planetErrors;

	static void CountPlanetErrors( string condition, string stackTrace, LogType type )
	{
		if ( ( type == LogType.Error ) && condition.Contains( "planet" ) )
		{
			s_planetErrors++;

			if ( s_planetErrors <= 8 )
			{
				var newline = condition.IndexOf( '\n' );

				Log( "M17 error logged by the game: " + ( ( newline > 0 ) ? condition.Substring( 0, newline ) : condition ) );
			}
		}
	}

	IEnumerator ScenarioM17()
	{
		var starSystem = SpaceflightController.m_instance.m_starSystem;
		var gameData = DataController.m_instance.m_gameData;

		EnsureCrew();

		// let the generation that began with the scene run to its end
		var deadline = Time.realtimeSinceStartup + 45.0f;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup < deadline ) )
		{
			yield return null;
		}

		if ( starSystem.GeneratingPlanets() )
		{
			Finish( "scenario=m17 abort: the first generation never finished", 2 );
			yield break;
		}

		Application.logMessageReceived += CountPlanetErrors;

		// planet 90 is a frozen planet of the Arth system: it has a difference buffer after its two prepared maps
		const int c_planetId = 90;

		GD_Planet planet = null;

		foreach ( var candidate in gameData.m_planetList )
		{
			if ( candidate.m_id == c_planetId )
			{
				planet = candidate;
			}
		}

		var textAsset = Resources.Load<TextAsset>( "Planets/" + c_planetId );
		var good = textAsset.bytes;
		var plain = Decompress( good );

		// not a planet file at all
		var garbage = new byte[ 4096 ];

		new System.Random( 12345 ).NextBytes( garbage );

		// a file made by another version of the planet generator (the version is the first number in the file)
		var otherVersion = (byte[]) plain.Clone();

		otherVersion[ 0 ] = 3;

		// a file that was cut off: half of the compressed bytes, and a complete archive that is missing the last million bytes of its difference buffer
		var cutInHalf = FirstBytes( good, good.Length / 2 );
		var shortBuffer = Compress( FirstBytes( plain, plain.Length - 1000000 ) );

		// a file whose data is intact but does not match its checksum (a gzip file ends with the checksum and the length of its data, four bytes each)
		var wrongChecksum = (byte[]) good.Clone();

		wrongChecksum[ good.Length - 8 ] ^= 0x01;

		// a file with one bit of its compressed data flipped
		var bitFlipped = (byte[]) good.Clone();

		bitFlipped[ good.Length / 2 ] ^= 0x10;

		// a file with more data than the planet should have
		var longer = new byte[ plain.Length + 100 ];

		Array.Copy( plain, longer, plain.Length );

		Log( "M17 planet " + c_planetId + " (" + planet.m_id + ", gas giant " + planet.IsGasGiant() + "): file " + good.Length + " bytes, " + plain.Length + " uncompressed, version " + BitConverter.ToInt32( plain, 0 ) );

		// the real file comes last
		var names = new string[] { "garbage", "from another version", "cut in half", "missing the end of its difference buffer", "empty", "carrying a wrong checksum", "damaged by one flipped bit", "longer than it should be", "the real file" };
		var files = new byte[][] { garbage, Compress( otherVersion ), cutInHalf, shortBuffer, new byte[ 0 ], wrongChecksum, bitFlipped, Compress( longer ), good };
		var results = new PlanetBytesResult[ files.Length ];
		var real = files.Length - 1;

		for ( var i = 0; i < files.Length; i++ )
		{
			var errorsBefore = s_planetErrors;

			results[ i ] = new PlanetBytesResult();

			yield return ProcessBytes( planet, files[ i ], results[ i ] );

			Log( "M17 " + names[ i ] + " (" + files[ i ].Length + " bytes): " + results[ i ] + ", errors logged " + ( s_planetErrors - errorsBefore ) );
		}

		for ( var i = 0; i < real; i++ )
		{
			Check( "M17 a planet file that is " + names[ i ] + " aborts the planet and never throws", ( results[ i ].m_throws == 0 ) && results[ i ].m_abort && !results[ i ].m_mapsGenerated, results[ i ].ToString() );
		}

		Check( "M17 the real planet file still generates its maps", ( results[ real ].m_throws == 0 ) && !results[ real ].m_abort && results[ real ].m_mapsGenerated && results[ real ].m_hasTextures, results[ real ].ToString() );

		// ---- every planet file in the project has to get through the reader, which is stricter than it was (code from before the fix has no reader of its own to call)
		var readMethod = typeof( PlanetGenerator ).GetMethod( "ReadPlanetData", c_any );
		var filesRead = 0;
		var filesRefused = 0;
		var firstRefusal = "none";

		if ( readMethod != null )
		{
			foreach ( var candidate in gameData.m_planetList )
			{
				var asset = Resources.Load<TextAsset>( "Planets/" + candidate.m_id );

				if ( asset == null )
				{
					filesRefused++;

					if ( filesRefused == 1 )
					{
						firstRefusal = "planet " + candidate.m_id + " has no file";
					}

					continue;
				}

				var reader = new PlanetGenerator();

				reader.Start( candidate );

				try
				{
					readMethod.Invoke( reader, new object[] { asset.bytes } );

					filesRead++;
				}
				catch ( TargetInvocationException exception )
				{
					filesRefused++;

					if ( filesRefused == 1 )
					{
						firstRefusal = "planet " + candidate.m_id + ": " + exception.InnerException.GetType().Name + ": " + exception.InnerException.Message;
					}
				}

				Resources.UnloadAsset( asset );

				if ( ( ( filesRead + filesRefused ) % 50 ) == 0 )
				{
					yield return null;
				}
			}

			Log( "M17 the reader was given all " + gameData.m_planetList.Length + " planet files of the project: read " + filesRead + ", refused " + filesRefused + " (first: " + firstRefusal + ")" );

			Check( "M17 the reader accepts every planet file in the project", ( filesRead == gameData.m_planetList.Length ) && ( filesRefused == 0 ), "read " + filesRead + " of " + gameData.m_planetList.Length + ", refused " + filesRefused + " (first: " + firstRefusal + ")" );
		}
		else
		{
			Log( "M17 this code has no reader of its own for the planet data, so the " + gameData.m_planetList.Length + " planet files were not read one by one" );
		}

		// ---- the real thing: the first planet of the star system gets a file that cannot be read
		var errorsBeforeSystem = s_planetErrors;
		var exceptionsBeforeSystem = s_exceptionCount;

		RegeneratePlanets();

		Planet victim = null;

		foreach ( var planetController in starSystem.m_planetController )
		{
			if ( planetController.m_planet != null )
			{
				victim = planetController;

				break;
			}
		}

		StartProcessing( victim.GetPlanetGenerator(), garbage );

		var start = Time.realtimeSinceStartup;
		var frames = 0;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 12.0f ) )
		{
			frames++;

			yield return null;
		}

		var seconds = Time.realtimeSinceStartup - start;
		var finished = !starSystem.GeneratingPlanets();

		// the spaceflight controller takes the popup away in the frame after the last planet
		yield return null;

		var planetsInSystem = 0;
		var planetsWithMaps = 0;

		foreach ( var planetController in starSystem.m_planetController )
		{
			if ( planetController.m_planet == null )
			{
				continue;
			}

			planetsInSystem++;

			var generator = planetController.GetPlanetGenerator();

			if ( ( generator != null ) && generator.m_mapsGenerated && ( generator.m_albedoTexture != null ) )
			{
				planetsWithMaps++;
			}
		}

		var victimAborted = victim.GetPlanetGenerator().m_abort;
		var exceptions = s_exceptionCount - exceptionsBeforeSystem;
		var errors = s_planetErrors - errorsBeforeSystem;
		var paused = SpaceflightController.m_instance.m_gameIsPaused;

		Application.logMessageReceived -= CountPlanetErrors;

		Log( "M17 star system with an unreadable file for planet " + victim.m_planet.m_id + ": finished " + finished + " after " + seconds.ToString( "F2" ) + " s and " + frames + " frames, exceptions " + exceptions + ", errors logged " + errors + ", that planet aborted " + victimAborted + ", planets with maps " + planetsWithMaps + " of " + planetsInSystem + ", popup still up " + PopupController.m_instance.IsActive() + ", game paused " + paused );

		Check( "M17 a star system with one unreadable planet file still finishes generating", finished && !paused && ( exceptions == 0 ), "finished " + finished + " after " + seconds.ToString( "F2" ) + " s, exceptions " + exceptions + ", paused " + paused );
		Check( "M17 only the planet with the unreadable file goes without maps, and the game says so once", victimAborted && ( planetsWithMaps == planetsInSystem - 1 ) && ( errors == 1 ), "that planet aborted " + victimAborted + ", planets with maps " + planetsWithMaps + " of " + planetsInSystem + ", errors logged " + errors );

		var aborted = 0;
		var threw = 0;

		for ( var i = 0; i < real; i++ )
		{
			aborted += ( results[ i ].m_abort && !results[ i ].m_mapsGenerated && ( results[ i ].m_throws == 0 ) ) ? 1 : 0;
			threw += ( results[ i ].m_throws > 0 ) ? 1 : 0;
		}

		Finish( "scenario=m17 badFiles=" + real + " abortedCleanly=" + aborted + " threw=" + threw + " realFileGenerated=" + results[ real ].m_mapsGenerated + " allFiles=[read " + filesRead + " refused " + filesRefused + "] system=[finished " + finished + " exceptions " + exceptions + " errors " + errors + " maps " + planetsWithMaps + "/" + planetsInSystem + " paused " + paused + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M16: the maps of a star system's planets are destroyed when the system is left

	// the textures that were made while the game ran and are still alive: how many, how many of them have the size of a planet's albedo map, and the memory they hold
	static void RuntimeTextures( out int count, out int planetSized, out long bytes )
	{
		count = 0;
		planetSized = 0;
		bytes = 0;

		foreach ( var texture in Resources.FindObjectsOfTypeAll<Texture2D>() )
		{
			// an asset of the project is not made at runtime
			if ( EditorUtility.IsPersistent( texture ) )
			{
				continue;
			}

			count++;
			bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong( texture );

			if ( ( texture.width == 2048 ) && ( texture.height == 1024 ) )
			{
				planetSized++;
			}
		}
	}

	// the planet files that are loaded right now (they are text assets named after the id of their planet)
	static int LoadedPlanetFiles()
	{
		var count = 0;

		foreach ( var textAsset in Resources.FindObjectsOfTypeAll<TextAsset>() )
		{
			if ( int.TryParse( textAsset.name, out _ ) )
			{
				count++;
			}
		}

		return count;
	}

	// the planets of a star that are not gas giants (their albedo map is 2048 by 1024, a gas giant's is 256 by 128)
	static int RockyPlanets( GD_Star star )
	{
		var count = 0;

		foreach ( var planet in star.GetPlanetList() )
		{
			if ( ( planet != null ) && ( planet.m_id != -1 ) && !planet.IsGasGiant() )
			{
				count++;
			}
		}

		return count;
	}

	// put the ship into another star system, the way flying into it from hyperspace does
	static void EnterStarSystem( int starId )
	{
		var playerData = DataController.m_instance.m_playerData;

		playerData.m_general.m_currentStarId = starId;

		// just inside the edge of the system, away from its planets
		playerData.m_general.m_coordinates = new Vector3( 7900.0f, 0.0f, 0.0f );
		playerData.m_general.m_lastStarSystemCoordinates = playerData.m_general.m_coordinates;

		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.StarSystem );
	}

	static int s_errorsLogged;

	// counts the errors the game logs, and shows the first few
	static void CountErrors( string condition, string stackTrace, LogType type )
	{
		if ( type == LogType.Error )
		{
			s_errorsLogged++;

			if ( s_errorsLogged <= 5 )
			{
				var newline = condition.IndexOf( '\n' );

				Log( "error logged by the game: " + ( ( newline > 0 ) ? condition.Substring( 0, newline ) : condition ) );
			}
		}
	}

	IEnumerator ScenarioM16()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var starSystem = SpaceflightController.m_instance.m_starSystem;

		EnsureCrew();

		// let the generation that began with the scene run to its end
		var deadline = Time.realtimeSinceStartup + 45.0f;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup < deadline ) )
		{
			yield return null;
		}

		if ( starSystem.GeneratingPlanets() )
		{
			Finish( "scenario=m16 abort: the first generation never finished", 2 );
			yield break;
		}

		yield return Frames( 3 );

		var arth = gameData.m_starList[ playerData.m_general.m_currentStarId ];

		// three other stars with at least three planets that are not gas giants, and then back to arth
		var stars = new List<GD_Star>();

		foreach ( var star in gameData.m_starList )
		{
			if ( ( star.m_id != arth.m_id ) && ( RockyPlanets( star ) >= 3 ) )
			{
				stars.Add( star );

				if ( stars.Count == 3 )
				{
					break;
				}
			}
		}

		stars.Add( arth );

		RuntimeTextures( out var countAtStart, out var planetSizedAtStart, out var bytesAtStart );

		var filesAtStart = LoadedPlanetFiles();

		Log( "M16 at the start, star " + arth.m_id + " (" + RockyPlanets( arth ) + " planets that are not gas giants): " + countAtStart + " runtime textures holding " + ( bytesAtStart >> 20 ) + " MB, " + planetSizedAtStart + " of them the size of a planet map, " + filesAtStart + " planet files loaded" );

		// runtime textures of that size that are not planet maps (the planet maps have no name)
		var otherPlanetSized = 0;
		var otherNames = "";

		foreach ( var texture in Resources.FindObjectsOfTypeAll<Texture2D>() )
		{
			if ( !EditorUtility.IsPersistent( texture ) && ( texture.width == 2048 ) && ( texture.height == 1024 ) && ( texture.name != "" ) )
			{
				otherPlanetSized++;
				otherNames += " '" + texture.name + "' (" + texture.format + ")";
			}
		}

		Log( "M16 runtime textures of the size of a planet map that are something else: " + otherPlanetSized + otherNames );

		// the most textures of the size of a planet map there were beyond those of the planets of the current system
		var mostLeftOver = planetSizedAtStart - RockyPlanets( arth ) - otherPlanetSized;
		var mostFilesLoaded = filesAtStart;
		var everyPlanetHasItsMaps = true;
		var bytesAtEnd = bytesAtStart;
		var visits = "";

		// an elevation map as a landing makes it, to see that it goes too
		Texture2D elevationTexture = null;
		var elevationTextureWasMade = false;
		var elevationTextureOutlivedItsSystem = false;

		foreach ( var star in stars )
		{
			EnterStarSystem( star.m_id );

			var start = Time.realtimeSinceStartup;

			while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 30.0f ) )
			{
				yield return null;
			}

			if ( starSystem.GeneratingPlanets() )
			{
				Finish( "scenario=m16 abort: the planets of star " + star.m_id + " never finished generating", 2 );
				yield break;
			}

			// a destroyed object goes at the end of its frame
			yield return Frames( 3 );

			if ( playerData.m_general.m_location != PD_General.Location.StarSystem )
			{
				Finish( "scenario=m16 abort: something took the ship out of the star system of star " + star.m_id + " (" + playerData.m_general.m_location + ")", 2 );
				yield break;
			}

			// the elevation map of a planet of the system before this one
			if ( elevationTextureWasMade && ( elevationTexture != null ) )
			{
				elevationTextureOutlivedItsSystem = true;
			}

			RuntimeTextures( out var count, out var planetSized, out var bytes );

			var files = LoadedPlanetFiles();
			var rocky = RockyPlanets( star );

			// do the planets of this system have their maps, and are those on their materials?
			var planets = 0;
			var planetsWithMaps = 0;

			foreach ( var planetController in starSystem.m_planetController )
			{
				if ( planetController.m_planet == null )
				{
					continue;
				}

				planets++;

				var generator = planetController.GetPlanetGenerator();

				if ( ( generator != null ) && ( generator.m_albedoTexture != null ) && ( generator.m_normalTexture != null ) && ( planetController.GetMaterial().GetTexture( "_MainTex" ) == generator.m_albedoTexture ) )
				{
					planetsWithMaps++;
				}

				// make an elevation map for one planet of the first system (a landing does that)
				if ( !elevationTextureWasMade && ( generator != null ) && !planetController.m_planet.IsGasGiant() )
				{
					elevationTexture = generator.CreateElevationTexture();
					elevationTextureWasMade = true;
				}
			}

			Log( "M16 star " + star.m_id + " (" + planets + " planets, " + rocky + " not gas giants): " + count + " runtime textures holding " + ( bytes >> 20 ) + " MB, " + planetSized + " of them the size of a planet map, " + files + " planet files loaded, planets with their maps on their material " + planetsWithMaps );

			// (the textures were counted before the elevation map was made, so that one only counts if it outlives its system)
			mostLeftOver = Mathf.Max( mostLeftOver, planetSized - rocky - otherPlanetSized );
			mostFilesLoaded = Mathf.Max( mostFilesLoaded, files );
			everyPlanetHasItsMaps &= ( planetsWithMaps == planets );
			bytesAtEnd = bytes;
			visits += ( ( visits.Length > 0 ) ? " " : "" ) + star.m_id + ":" + planetSized + "/" + rocky + "/" + ( bytes >> 20 ) + "MB";
		}

		// back in the arth system: go down to a planet, which uses the maps and the elevation data
		var exceptionsBeforeLanding = s_exceptionCount;

		yield return EnterOrbit( 90 );

		SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		var landed = ( playerData.m_general.m_location == PD_General.Location.Planetside );
		var landingExceptions = s_exceptionCount - exceptionsBeforeLanding;

		var terrainGrid = GetField( SpaceflightController.m_instance.m_planetside, "m_terrainGrid" ) as TerrainGrid;
		var terrainMaterial = ( terrainGrid == null ) ? null : GetField( terrainGrid, "m_material" ) as Material;
		var terrainHasItsMap = ( terrainMaterial != null ) && ( terrainMaterial.GetTexture( "_MainTex" ) != null );

		Log( "M16 landing on planet 90 after four changes of star system: landed " + landed + ", exceptions " + landingExceptions + ", the terrain has its albedo map " + terrainHasItsMap );
		Log( "M16 the elevation map made for a planet of star " + stars[ 0 ].m_id + ": made " + elevationTextureWasMade + ", still alive after its system was left " + elevationTextureOutlivedItsSystem );

		Check( "M16 only the planets of the current star system have maps", mostLeftOver == 0, "the most textures of the size of a planet map beyond those of the current system: " + mostLeftOver + " (" + visits + ")" );
		Check( "M16 the planets of the current star system keep their maps", everyPlanetHasItsMaps, visits );
		Check( "M16 an elevation map goes with its star system", elevationTextureWasMade && !elevationTextureOutlivedItsSystem, "made " + elevationTextureWasMade + ", outlived its system " + elevationTextureOutlivedItsSystem );
		Check( "M16 back at the first star the textures hold no more memory than at the start", bytesAtEnd <= bytesAtStart + ( 4L << 20 ), ( bytesAtStart >> 20 ) + " MB at the start, " + ( bytesAtEnd >> 20 ) + " MB at the end" );
		Check( "M16 no planet file stays loaded", mostFilesLoaded == 0, "the most planet files loaded after a system had been generated: " + mostFilesLoaded );
		Check( "M16 a landing still works after the changes of star system", landed && ( landingExceptions == 0 ) && terrainHasItsMap, "landed " + landed + ", exceptions " + landingExceptions + ", terrain has its albedo map " + terrainHasItsMap );

		// ---- leave the spaceflight scene: the planets destroy their maps as they go, and that must not log an error
		// (unity clears out unused assets on a scene change, but a map is in use for as long as anything still refers to its generator - the scene objects stay
		// reachable through the static of the spaceflight controller, and through this scenario - so without the fix the maps of the last system stay)
		s_errorsLogged = 0;

		Application.logMessageReceived += CountErrors;

		var exceptionsBeforeLeaving = s_exceptionCount;

		SceneManager.LoadScene( "Intro" );

		yield return WaitForScene( "Intro" );
		yield return Frames( 5 );

		Application.logMessageReceived -= CountErrors;

		RuntimeTextures( out var countAfterLeaving, out var planetSizedAfterLeaving, out var bytesAfterLeaving );

		var leavingExceptions = s_exceptionCount - exceptionsBeforeLeaving;

		Log( "M16 after leaving the spaceflight scene (now in " + SceneManager.GetActiveScene().name + "): " + countAfterLeaving + " runtime textures holding " + ( bytesAfterLeaving >> 20 ) + " MB, " + ( planetSizedAfterLeaving - otherPlanetSized ) + " planet maps left, errors logged " + s_errorsLogged + ", exceptions " + leavingExceptions );

		Check( "M16 leaving the spaceflight scene leaves no planet map behind and logs no error", ( planetSizedAfterLeaving - otherPlanetSized == 0 ) && ( s_errorsLogged == 0 ) && ( leavingExceptions == 0 ), ( planetSizedAfterLeaving - otherPlanetSized ) + " planet maps left, errors " + s_errorsLogged + ", exceptions " + leavingExceptions );

		Finish( "scenario=m16 visits=[" + visits + "] mostLeftOver=" + mostLeftOver + " memory=" + ( bytesAtStart >> 20 ) + "MB->" + ( bytesAtEnd >> 20 ) + "MB filesLoaded=" + mostFilesLoaded + " elevationMapOutlived=" + elevationTextureOutlivedItsSystem + " landed=" + landed + " afterLeaving=" + ( planetSizedAfterLeaving - otherPlanetSized ) + "maps/" + s_errorsLogged + "errors checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- the animations of the system map

	// the animations of the system map (SystemDisplay): the wake a moving ship leaves on the map and that fades once it stops, the engine
	// glow at the back of the ship marker that pulses, the sensor ping that grows from the ship, all frozen while the game is paused and
	// hidden in orbit (it reaches the objects through the scene, so that it also runs on the map without them)
	IEnumerator ScenarioMapLife()
	{
		var controller = SpaceflightController.m_instance;
		var playerData = DataController.m_instance.m_playerData;
		var systemDisplay = controller.m_displayController.m_systemDisplay;
		var root = systemDisplay.transform;

		EnterStarSystem( 25 );

		yield return WaitForPlanetMaps( 60.0f );

		var wakeRoot = root.Find( "Wake" );
		var engineGlowTransform = root.Find( "Engine Glow" );
		var engineGlow = ( engineGlowTransform != null ) ? engineGlowTransform.GetComponent<UnityEngine.UI.Image>() : null;
		var pingTransform = root.Find( "Ping" );
		var ping = ( pingTransform != null ) ? pingTransform.GetComponent<UnityEngine.UI.Image>() : null;

		// ---- 1. the ship flies east across the map (60 world units a frame, about 1.9 map units) and leaves a wake behind it
		var start = new Vector3( -2400.0f, 0.0f, 1200.0f );

		playerData.m_general.m_currentDirection = Vector3.right;
		playerData.m_general.m_currentSpeed = playerData.m_general.m_currentMaximumSpeed;

		for ( var i = 0; i < 40; i++ )
		{
			MoveShipTo( start + Vector3.right * 60.0f * i );

			yield return null;
		}

		var shipOnMap = new Vector2( systemDisplay.m_player.transform.localPosition.x, systemDisplay.m_player.transform.localPosition.y );
		var segments = 0;
		var behindTheShip = 0;

		if ( wakeRoot != null )
		{
			foreach ( var segment in wakeRoot.GetComponentsInChildren<UnityEngine.UI.Image>( true ) )
			{
				if ( !segment.enabled || !segment.gameObject.activeInHierarchy )
				{
					continue;
				}

				segments++;

				// on the ship's path (the line y = the ship's), and west of the ship
				var position = segment.rectTransform.localPosition;

				if ( ( Mathf.Abs( position.y - shipOnMap.y ) < 0.5f ) && ( position.x < shipOnMap.x + 0.5f ) && ( position.x > shipOnMap.x - 30.0f ) )
				{
					behindTheShip++;
				}
			}
		}

		Check( "maplife: a moving ship leaves a wake on the map, along its path behind it", ( segments >= 5 ) && ( behindTheShip == segments ), segments + " segments, " + behindTheShip + " behind the ship" );

		// ---- 2. the engine glow is at the back of the chevron (west of the ship, which heads east) and pulses
		var glowBehind = false;
		var lowest = 1.0f;
		var highest = 0.0f;

		if ( engineGlow != null )
		{
			var glowPosition = engineGlow.rectTransform.localPosition;

			glowBehind = engineGlow.enabled && ( Mathf.Abs( glowPosition.x - ( shipOnMap.x - 5.0f ) ) < 0.1f ) && ( Mathf.Abs( glowPosition.y - shipOnMap.y ) < 0.1f );

			// over 1.5 s of real time (headless frames are much shorter than the pulse)
			var pulseStart = Time.realtimeSinceStartup;

			while ( Time.realtimeSinceStartup - pulseStart < 1.5f )
			{
				MoveShipTo( start + Vector3.right * 60.0f * 39 );

				yield return null;

				lowest = Mathf.Min( lowest, engineGlow.canvasRenderer.GetAlpha() );
				highest = Mathf.Max( highest, engineGlow.canvasRenderer.GetAlpha() );
			}
		}

		Check( "maplife: the engine glow is at the back of the ship marker and pulses", glowBehind && ( highest - lowest > 0.05f ), "behind " + glowBehind + ", alpha " + lowest.ToString( "F2" ) + " to " + highest.ToString( "F2" ) );

		// ---- 3. the ship stops: the wake fades away
		var stopTime = Time.realtimeSinceStartup;

		while ( Time.realtimeSinceStartup - stopTime < 2.5f )
		{
			MoveShipTo( start + Vector3.right * 60.0f * 39 );

			yield return null;
		}

		var segmentsAfterStopping = 0;

		if ( wakeRoot != null )
		{
			foreach ( var segment in wakeRoot.GetComponentsInChildren<UnityEngine.UI.Image>( true ) )
			{
				if ( segment.enabled )
				{
					segmentsAfterStopping++;
				}
			}
		}

		Check( "maplife: the wake fades away once the ship has stopped", ( wakeRoot != null ) && ( segmentsAfterStopping == 0 ), segmentsAfterStopping + " segments left" );

		// ---- 4. a sensor ping goes out from the ship within 4.5 s and grows
		var pingSeen = false;
		var pingGrew = false;
		var pingCentred = false;
		var firstSize = 0.0f;
		var firstSeen = 0.0f;
		var pingStart = Time.realtimeSinceStartup;

		// (it is compared over 0.3 s of real time: headless frames are short)
		while ( ( ping != null ) && ( Time.realtimeSinceStartup - pingStart < 4.5f ) && !pingGrew )
		{
			yield return null;

			if ( ping.enabled )
			{
				var size = ping.rectTransform.rect.width;

				if ( !pingSeen )
				{
					var pingPosition = ping.rectTransform.localPosition;

					pingCentred = ( Mathf.Abs( pingPosition.x - shipOnMap.x ) < 0.5f ) && ( Mathf.Abs( pingPosition.y - shipOnMap.y ) < 0.5f );
					pingSeen = true;
					firstSize = size;
					firstSeen = Time.realtimeSinceStartup;
				}
				else if ( ( Time.realtimeSinceStartup - firstSeen > 0.3f ) && ( size > firstSize + 5.0f ) )
				{
					pingGrew = true;
				}
			}
		}

		Check( "maplife: a sensor ping goes out from the ship and grows", pingSeen && pingGrew && pingCentred, "seen " + pingSeen + ", grew " + pingGrew + ", from the ship " + pingCentred );

		// a ping that goes out at the edge of the system (where the ship comes in) is clipped to the display, not drawn over the console
		var clipped = root.GetComponent<UnityEngine.UI.RectMask2D>() != null;

		Check( "maplife: the map is clipped to its display", clipped, "RectMask2D " + clipped );

		// ---- 5. paused: the ping stops growing and the engine glow stops pulsing
		var pingSizeBefore = ( ping != null ) ? ping.rectTransform.rect.width : -1.0f;
		var glowAlphaBefore = ( engineGlow != null ) ? engineGlow.canvasRenderer.GetAlpha() : -1.0f;

		controller.m_gameIsPaused = true;

		yield return Frames( 20 );

		var pingSizePaused = ( ping != null ) ? ping.rectTransform.rect.width : -1.0f;
		var glowAlphaPaused = ( engineGlow != null ) ? engineGlow.canvasRenderer.GetAlpha() : -1.0f;

		controller.m_gameIsPaused = false;

		Check( "maplife: the animations stop while the game is paused", ( ping != null ) && ( engineGlow != null ) && ( Mathf.Abs( pingSizePaused - pingSizeBefore ) < 0.001f ) && ( Mathf.Abs( glowAlphaPaused - glowAlphaBefore ) < 0.001f ), "ping " + pingSizeBefore.ToString( "R" ) + " -> " + pingSizePaused.ToString( "R" ) + ", glow " + glowAlphaBefore.ToString( "R" ) + " -> " + glowAlphaPaused.ToString( "R" ) );

		// ---- 6. a frame of the map takes no memory from the heap
		var controlBytes = AllocatedByTheControl();
		var mapBytes = Allocated( systemDisplay.Update, c_meterCalls ) / c_meterCalls;

		Check( "maplife: a frame of the animated system map takes no memory", ( controlBytes >= 256 ) && ( mapBytes == 0 ), mapBytes + " bytes per frame, the control measures " + controlBytes + " bytes per call" );

		// ---- 7. in orbit there is no ship marker, so none of its animations either (the location is set for a few frames only)
		var locationBefore = playerData.m_general.m_location;

		playerData.m_general.m_location = PD_General.Location.InOrbit;

		yield return Frames( 3 );

		var glowInOrbit = ( engineGlow != null ) && engineGlow.enabled;
		var pingInOrbit = ( ping != null ) && ping.enabled;
		var wakeInOrbit = 0;

		if ( wakeRoot != null )
		{
			foreach ( var segment in wakeRoot.GetComponentsInChildren<UnityEngine.UI.Image>( true ) )
			{
				if ( segment.enabled )
				{
					wakeInOrbit++;
				}
			}
		}

		playerData.m_general.m_location = locationBefore;

		Check( "maplife: in orbit there is no engine glow, wake or ping", ( engineGlow != null ) && ( ping != null ) && !glowInOrbit && !pingInOrbit && ( wakeInOrbit == 0 ), "glow " + glowInOrbit + ", ping " + pingInOrbit + ", wake " + wakeInOrbit );

		Finish( "scenario=maplife wake=" + segments + "/" + behindTheShip + " glow=" + lowest.ToString( "F2" ) + "-" + highest.ToString( "F2" ) + " ping=" + pingSeen + "/" + pingGrew + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// puts the ship at a position of the star system, as flying there does
	static void MoveShipTo( Vector3 coordinates )
	{
		var playerData = DataController.m_instance.m_playerData;

		playerData.m_general.m_coordinates = coordinates;
		playerData.m_general.m_lastStarSystemCoordinates = coordinates;
		SpaceflightController.m_instance.m_playerShip.transform.position = coordinates;
	}

	// ---------------------------------------------------------------- the system map

	// the system map (SystemDisplay): each planet drawn from its own albedo map, lit from the star and sized by its mass, the star in the colour of
	// real stars of its class, the ecosphere of the class, a ring image on each orbit, the star field, the ship marker turned to the heading,
	// and the maps of the next system once it has been entered (it reaches the map's objects through the scene and the material properties,
	// so that it also runs on the old map)
	IEnumerator ScenarioSystemMap()
	{
		var controller = SpaceflightController.m_instance;
		var playerData = DataController.m_instance.m_playerData;
		var systemDisplay = controller.m_displayController.m_systemDisplay;

		// star 156 (class A, ecosphere 5 to 7): a planet in every orbit, a gas giant of mass 400000 in orbit 4 and a rocky planet of mass 3000 in orbit 3
		EnterStarSystem( 156 );

		yield return WaitForPlanetMaps( 60.0f );

		// the ship heads east, then south
		playerData.m_general.m_currentDirection = Vector3.right;

		yield return Frames( 5 );

		var eastAngle = systemDisplay.m_player.rectTransform.localEulerAngles.z;

		playerData.m_general.m_currentDirection = Vector3.back;

		yield return Frames( 5 );

		var southAngle = systemDisplay.m_player.rectTransform.localEulerAngles.z;

		// ---- 1. the planets of star 156
		var mapped = 0;
		var planets = 0;
		var sizedByMass = 0;
		var litFromTheStar = 0;
		var upright = 0;
		var detail = "";

		for ( var i = 0; i < 8; i++ )
		{
			var planetController = controller.m_starSystem.m_planetController[ i ];

			if ( ( planetController.m_planet == null ) || ( planetController.m_planet.m_id == -1 ) )
			{
				continue;
			}

			planets++;

			var image = systemDisplay.m_planetList[ i ];
			var material = image.material;

			// its own albedo map
			var texture = material.HasProperty( "_PlanetTex" ) ? material.GetTexture( "_PlanetTex" ) : null;
			var generator = planetController.GetPlanetGenerator();

			if ( ( texture != null ) && ( generator != null ) && ( texture == generator.m_albedoTexture ) )
			{
				mapped++;
			}

			// its size from its mass (18 to 34 map units)
			var expectedSize = Mathf.Lerp( 18.0f, 34.0f, Mathf.Sqrt( Mathf.Max( 0.0f, planetController.m_planet.m_mass - 6.0f ) / 500000.0f ) );
			var size = image.rectTransform.rect.width;

			if ( Mathf.Abs( size - expectedSize ) < 0.01f )
			{
				sizedByMass++;
			}

			// the light comes from the star in the middle of the map
			var position = systemDisplay.transform.InverseTransformPoint( image.transform.position );
			var towardsTheStar = -new Vector2( position.x, position.y ).normalized;
			var light = material.HasProperty( "_LightDirection" ) ? (Vector2) material.GetVector( "_LightDirection" ) : Vector2.zero;

			if ( Vector2.Dot( light.normalized, towardsTheStar ) > 0.999f )
			{
				litFromTheStar++;
			}

			// the planet image is upright, whatever the angle of its orbit
			var tilt = Quaternion.Angle( systemDisplay.transform.rotation, image.transform.rotation );

			if ( tilt < 0.5f )
			{
				upright++;
			}

			detail += " o" + ( i + 1 ) + ":size " + size.ToString( "F1" ) + "/" + expectedSize.ToString( "F1" ) + " tilt " + tilt.ToString( "F1" ) + " map " + ( texture != null );
		}

		Log( "systemmap: star 156" + detail );

		Check( "systemmap: every planet on the map shows its own albedo map", ( planets == 8 ) && ( mapped == planets ), mapped + " of " + planets );
		Check( "systemmap: the planets are sized by their mass (the gas giant of orbit 4 is the largest)", ( planets == 8 ) && ( sizedByMass == planets ) && ( systemDisplay.m_planetList[ 3 ].rectTransform.rect.width > systemDisplay.m_planetList[ 2 ].rectTransform.rect.width ), sizedByMass + " of " + planets + detail );
		Check( "systemmap: each planet is lit from the star and stays upright as it goes round", ( planets == 8 ) && ( litFromTheStar == planets ) && ( upright == planets ), litFromTheStar + " lit, " + upright + " upright of " + planets );

		// ---- 2. the star: the colour of a real class A star (blue-white)
		var starMaterial = systemDisplay.m_star.material;
		var starColorA = starMaterial.HasProperty( "_StarColor" ) ? starMaterial.GetColor( "_StarColor" ) : Color.clear;

		Check( "systemmap: a class A star is blue-white", ( starColorA.b > 0.95f ) && ( starColorA.r < starColorA.b ) && ( starColorA.g > 0.7f ), starColorA.ToString() );

		// the sun of the star system (its shine) has the colour of the map's star
		var sunColorA = SunColor();

		Check( "systemmap: the sun of a class A star system has the colour of the map's star", ( starColorA.a > 0.0f ) && ( Vector4.Distance( sunColorA, starColorA ) < 0.001f ), "sun " + sunColorA + ", map " + starColorA );

		// ---- 3. the ecosphere of class A: orbits 5 to 7, half way to the orbits on either side (137.5 to 212.5 map units)
		var ecosphereTransform = systemDisplay.transform.Find( "Ecosphere" );
		var ecosphere = ( ecosphereTransform != null ) ? ecosphereTransform.GetComponent<UnityEngine.UI.Image>() : null;
		var innerRadius = -1.0f;
		var outerRadius = -1.0f;

		if ( ( ecosphere != null ) && ecosphere.material.HasProperty( "_InnerRadius" ) )
		{
			var halfSize = ecosphere.rectTransform.rect.width * 0.5f;

			innerRadius = ecosphere.material.GetFloat( "_InnerRadius" ) * halfSize;
			outerRadius = ecosphere.material.GetFloat( "_OuterRadius" ) * halfSize;
		}

		Check( "systemmap: the ecosphere band covers the ecosphere of the star's class", ( ecosphere != null ) && ecosphere.enabled && ( Mathf.Abs( innerRadius - 137.5f ) < 0.05f ) && ( Mathf.Abs( outerRadius - 212.5f ) < 0.05f ), "inner " + innerRadius.ToString( "F2" ) + ", outer " + outerRadius.ToString( "F2" ) );

		// ---- 4. a ring image on each orbit, at the radius of the orbit
		var rings = 0;

		for ( var i = 0; i < systemDisplay.m_orbitList.Length; i++ )
		{
			var ringTransform = systemDisplay.m_orbitList[ i ].transform.Find( "Ring" );
			var ring = ( ringTransform != null ) ? ringTransform.GetComponent<UnityEngine.UI.Image>() : null;

			if ( ( ring != null ) && ring.material.HasProperty( "_Radius" ) )
			{
				var radius = ring.material.GetFloat( "_Radius" ) * ring.rectTransform.rect.width * 0.5f;

				if ( Mathf.Abs( radius - Mathf.Lerp( 50.0f, 225.0f, i / 7.0f ) ) < 0.05f )
				{
					rings++;
				}
			}
		}

		Check( "systemmap: each orbit has a ring image at its radius", rings == 8, rings + " of 8" );

		// ---- 5. the ship marker points where the ship is heading
		Check( "systemmap: the ship marker turns to the ship's heading (east 270 degrees, south 180)", ( Mathf.Abs( Mathf.DeltaAngle( eastAngle, 270.0f ) ) < 0.5f ) && ( Mathf.Abs( Mathf.DeltaAngle( southAngle, 180.0f ) ) < 0.5f ), "east " + eastAngle.ToString( "F1" ) + ", south " + southAngle.ToString( "F1" ) );

		// ---- 6. a frame of the map takes no memory from the heap (it sets the light and the spin of eight materials every frame)
		var controlBytes = AllocatedByTheControl();
		var mapBytes = Allocated( systemDisplay.Update, c_meterCalls ) / c_meterCalls;

		Check( "systemmap: a frame of the system map takes no memory (control: the old map took none either)", ( controlBytes >= 256 ) && ( mapBytes == 0 ), mapBytes + " bytes per frame, the control measures " + controlBytes + " bytes per call" );

		// ---- 7. the star field
		var starFieldTransform = systemDisplay.transform.Find( "Star Field" );
		var starField = ( starFieldTransform != null ) ? starFieldTransform.GetComponent<UnityEngine.UI.RawImage>() : null;
		var starFieldRect156 = ( starField != null ) ? starField.uvRect : Rect.zero;

		// ---- 8. star 25 (class G, Arth's sun): orbits 2, 4 and 8 are empty, and the map takes the maps of the new planets
		var texturesOf156 = new System.Collections.Generic.List<Texture>();

		foreach ( var image in systemDisplay.m_planetList )
		{
			if ( image.material.HasProperty( "_PlanetTex" ) )
			{
				texturesOf156.Add( image.material.GetTexture( "_PlanetTex" ) );
			}
		}

		EnterStarSystem( 25 );

		yield return WaitForPlanetMaps( 60.0f );

		yield return Frames( 5 );

		var mapped25 = 0;
		var planets25 = 0;
		var stale = 0;
		var emptyOrbitsHidden = 0;

		for ( var i = 0; i < 8; i++ )
		{
			var planetController = controller.m_starSystem.m_planetController[ i ];
			var hasPlanet = ( planetController.m_planet != null ) && ( planetController.m_planet.m_id != -1 );

			if ( !hasPlanet )
			{
				if ( !systemDisplay.m_orbitList[ i ].activeSelf )
				{
					emptyOrbitsHidden++;
				}

				continue;
			}

			planets25++;

			var material = systemDisplay.m_planetList[ i ].material;
			var texture = material.HasProperty( "_PlanetTex" ) ? material.GetTexture( "_PlanetTex" ) : null;
			var generator = planetController.GetPlanetGenerator();

			if ( ( texture != null ) && ( generator != null ) && ( texture == generator.m_albedoTexture ) )
			{
				mapped25++;
			}

			if ( ( texture != null ) && texturesOf156.Contains( texture ) )
			{
				stale++;
			}
		}

		var starColorG = starMaterial.HasProperty( "_StarColor" ) ? starMaterial.GetColor( "_StarColor" ) : Color.clear;
		var starFieldRect25 = ( starField != null ) ? starField.uvRect : Rect.zero;

		Check( "systemmap: in the next system every planet shows its own new map, none of the last system's", ( planets25 == 5 ) && ( mapped25 == planets25 ) && ( stale == 0 ), mapped25 + " of " + planets25 + ", " + stale + " from star 156" );
		Check( "systemmap: the empty orbits of Arth's system are hidden (control)", emptyOrbitsHidden == 3, emptyOrbitsHidden + " of 3" );
		Check( "systemmap: a class G star is yellow-white", ( starColorG.r > 0.95f ) && ( starColorG.b < starColorG.r ) && ( starColorG.g > 0.8f ), starColorG.ToString() );

		var sunColorG = SunColor();

		Check( "systemmap: the sun of a class G star system has the colour of the map's star", ( starColorG.a > 0.0f ) && ( Vector4.Distance( sunColorG, starColorG ) < 0.001f ), "sun " + sunColorG + ", map " + starColorG );
		Check( "systemmap: the star field shows a part of its own for each star", ( starField != null ) && ( starField.texture != null ) && ( starFieldRect156 != starFieldRect25 ) && ( starFieldRect25.width > 0.0f ), starFieldRect156 + " / " + starFieldRect25 );

		Finish( "scenario=systemmap planets156=" + mapped + "/" + planets + " planets25=" + mapped25 + "/" + planets25 + " heading=" + eastAngle.ToString( "F0" ) + "," + southAngle.ToString( "F0" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// the colour of the shine of the star system's sun (what StarSystem.Show set last)
	static Color SunColor()
	{
		var shine = SpaceflightController.m_instance.m_starSystem.m_shine;
		var renderer = ( shine != null ) ? shine.GetComponent<MeshRenderer>() : null;

		return ( renderer != null ) ? renderer.material.GetColor( "_Color" ) : Color.clear;
	}

	// waits until the planets of the star system have their maps
	static IEnumerator WaitForPlanetMaps( float seconds )
	{
		var starSystem = SpaceflightController.m_instance.m_starSystem;
		var deadline = Time.realtimeSinceStartup + seconds;

		yield return null;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup < deadline ) )
		{
			yield return null;
		}
	}

	// ---------------------------------------------------------------- a planet whose maps could not be generated cannot be landed on

	// press a console button and say what it threw (nothing, if it worked)
	static string PressAndCatch( ButtonController.ButtonSet buttonSet, int buttonIndex )
	{
		try
		{
			PressButton( buttonSet, buttonIndex );
		}
		catch ( Exception exception )
		{
			s_exceptionCount++;

			return exception.GetType().Name;
		}

		return "nothing";
	}

	IEnumerator ScenarioNoMaps()
	{
		var playerData = DataController.m_instance.m_playerData;
		var starSystem = SpaceflightController.m_instance.m_starSystem;
		var buttonController = SpaceflightController.m_instance.m_buttonController;
		var messages = SpaceflightController.m_instance.m_messages;

		EnsureCrew();

		// generate the star system again, and give planet 90 a file that cannot be read: its maps are not generated
		var garbage = new byte[ 4096 ];

		new System.Random( 12345 ).NextBytes( garbage );

		RegeneratePlanets();

		var badPlanet = starSystem.GetPlanetController( 90 );

		StartProcessing( badPlanet.GetPlanetGenerator(), garbage );

		var start = Time.realtimeSinceStartup;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 30.0f ) )
		{
			yield return null;
		}

		yield return Frames( 3 );

		var generator = badPlanet.GetPlanetGenerator();

		Log( "no maps: planet 90 after the generation: aborted " + generator.m_abort + ", maps generated " + generator.m_mapsGenerated + ", still generating " + starSystem.GeneratingPlanets() );

		if ( starSystem.GeneratingPlanets() || !generator.m_abort )
		{
			Finish( "scenario=nomaps abort: planet 90 was not left without maps (this needs the fix for M17)", 2 );
			yield break;
		}

		// ---- into orbit around it, then the land button of the command officer, then descend
		var exceptionsBefore = s_exceptionCount;

		yield return EnterOrbit( 90 );

		var locationInOrbit = playerData.m_general.m_location;
		var orbitExceptions = s_exceptionCount - exceptionsBefore;

		messages.Clear();

		var landThrew = PressAndCatch( ButtonController.ButtonSet.CommandB, 0 );

		yield return Frames( 3 );

		var consoleAfterLand = Console();
		var landingMenuIsUp = ( buttonController.GetCurrentButtonSet() == ButtonController.ButtonSet.Land );
		var messageAfterLand = MessageList();

		var descendThrew = "not pressed";
		var messageAfterDescend = "";

		if ( landingMenuIsUp )
		{
			descendThrew = PressAndCatch( ButtonController.ButtonSet.Land, 1 );

			yield return Frames( 3 );

			messageAfterDescend = MessageList();

			// back out of the landing menu
			PressAndCatch( ButtonController.ButtonSet.Land, 2 );

			yield return Frames( 3 );
		}

		var locationAfter = playerData.m_general.m_location;
		var exceptions = s_exceptionCount - exceptionsBefore;

		Log( "no maps: in orbit around planet 90 (" + locationInOrbit + ", exceptions getting there " + orbitExceptions + "): Land threw " + landThrew + ", console " + consoleAfterLand + ", message: " + messageAfterLand );
		Log( "no maps: Descend threw " + descendThrew + ", message: " + messageAfterDescend + " | location afterwards " + locationAfter + ", exceptions in all " + exceptions );

		Check( "no maps: the land button refuses a planet whose maps could not be generated and says why", !landingMenuIsUp && ( landThrew == "nothing" ) && messageAfterLand.Contains( "can't land" ), "landing menu up " + landingMenuIsUp + ", Land threw " + landThrew + ", message: " + messageAfterLand );
		Check( "no maps: nothing throws on the way, and the ship stays in orbit", ( exceptions == 0 ) && ( locationAfter == PD_General.Location.InOrbit ), "exceptions " + exceptions + " (Descend threw " + descendThrew + "), location " + locationAfter );

		// ---- a planet of the same system that has its maps can still be landed on
		yield return EnterOrbit( 94 );

		var goodLandThrew = PressAndCatch( ButtonController.ButtonSet.CommandB, 0 );

		yield return Frames( 3 );

		var goodLandingMenuIsUp = ( buttonController.GetCurrentButtonSet() == ButtonController.ButtonSet.Land );
		var goodConsole = Console();

		// descend without the 35 second landing: bake the terrain and switch, which is what the descend button and its animation do between them
		var bakeThrew = "nothing";

		try
		{
			SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();
		}
		catch ( Exception exception )
		{
			bakeThrew = exception.GetType().Name;
		}

		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		var goodLocation = playerData.m_general.m_location;

		Log( "no maps: planet 94 has its maps: Land threw " + goodLandThrew + ", console " + goodConsole + ", baking the terrain threw " + bakeThrew + ", location " + goodLocation );

		Check( "no maps: a planet with maps can still be landed on", goodLandingMenuIsUp && ( goodLandThrew == "nothing" ) && ( bakeThrew == "nothing" ) && ( goodLocation == PD_General.Location.Planetside ), "landing menu up " + goodLandingMenuIsUp + ", Land threw " + goodLandThrew + ", baking threw " + bakeThrew + ", location " + goodLocation );

		// ---- on the surface of a planet that has no maps (a saved game can be there if its planet file was damaged later): the disembark button refuses.
		// The ship is on planet 94 now, so say that its maps were not generated for as long as the button is pressed
		var surfaceGenerator = starSystem.GetPlanetController( 94 ).GetPlanetGenerator();

		surfaceGenerator.m_mapsGenerated = false;

		messages.Clear();

		var disembarkThrew = PressAndCatch( ButtonController.ButtonSet.CommandA, 1 );

		yield return Frames( 3 );

		var consoleAfterDisembark = Console();
		var messageAfterDisembark = MessageList();
		var disembarkStarted = consoleAfterDisembark.Contains( "DisembarkButton" );

		surfaceGenerator.m_mapsGenerated = true;

		Log( "no maps: Disembark on a planet without maps threw " + disembarkThrew + ", console " + consoleAfterDisembark + ", message: " + messageAfterDisembark );

		Check( "no maps: the disembark button refuses a planet without maps and says why", !disembarkStarted && ( disembarkThrew == "nothing" ) && messageAfterDisembark.Contains( "can't disembark" ), "started " + disembarkStarted + ", threw " + disembarkThrew + ", message: " + messageAfterDisembark );

		Finish( "scenario=nomaps landRefused=" + !landingMenuIsUp + " landThrew=" + landThrew + " descendThrew=" + descendThrew + " exceptions=" + exceptions + " location=" + locationAfter + " goodPlanet=" + goodLandingMenuIsUp + "/" + goodLocation + " disembarkRefused=" + !disembarkStarted + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M25: the blur of the albedo map takes as much from the left as from the right

	// run the albedo map filter on flat ground above the water with nothing scattered, on a black map with single white pixels
	static Color[,] AlbedoOfWhitePixels( int width, int height, int[] xs, int[] ys )
	{
		var elevation = new float[ height, width ];
		var color = new Color[ height, width ];

		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				elevation[ y, x ] = 0.5f;
				color[ y, x ] = Color.black;
			}
		}

		for ( var i = 0; i < xs.Length; i++ )
		{
			color[ ys[ i ], xs[ i ] ] = Color.white;
		}

		// a water colour that is not in the map, and water far below the ground
		return new PG_AlbedoMap().Process( elevation, color, 0.0f, new Color( 1.0f, 0.0f, 1.0f ), Color.black );
	}

	IEnumerator ScenarioM25()
	{
		var gameData = DataController.m_instance.m_gameData;

		var randomPointsField = typeof( PG_AlbedoMap ).GetField( "m_randomPoints", c_any );

		yield return Frames( 2 );

		// ---- 1. the blur by itself: the filter scatters the colours with a table of random points, so give it a table of zeros
		var savedPoints = randomPointsField.GetValue( null );

		randomPointsField.SetValue( null, new Vector2[ ( (Vector2[]) savedPoints ).Length ] );

		const int c_width = 2048;
		const int c_height = 1024;

		// one white pixel in the middle of the map and one in the first column (its left neighbour is the last column, the map wraps around)
		var albedo = AlbedoOfWhitePixels( c_width, c_height, new int[] { 1000, 0 }, new int[] { 500, 300 } );

		randomPointsField.SetValue( null, savedPoints );

		var left = albedo[ 500, 999 ].r;
		var center = albedo[ 500, 1000 ].r;
		var right = albedo[ 500, 1001 ].r;
		var above = albedo[ 499, 1000 ].r;
		var below = albedo[ 501, 1000 ].r;

		var wrapLeft = albedo[ 300, c_width - 1 ].r;
		var wrapCenter = albedo[ 300, 0 ].r;
		var wrapRight = albedo[ 300, 1 ].r;

		Log( "M25 a white pixel at column 1000: left " + left.ToString( "F3" ) + ", itself " + center.ToString( "F3" ) + ", right " + right.ToString( "F3" ) + ", the rows above and below " + above.ToString( "F3" ) + " and " + below.ToString( "F3" ) );
		Log( "M25 a white pixel at column 0: the last column " + wrapLeft.ToString( "F3" ) + ", itself " + wrapCenter.ToString( "F3" ) + ", column 1 " + wrapRight.ToString( "F3" ) );

		Check( "M25 the blur takes a quarter from each side and half from the pixel itself", Mathf.Approximately( left, 0.25f ) && Mathf.Approximately( center, 0.5f ) && Mathf.Approximately( right, 0.25f ), "left " + left.ToString( "F3" ) + ", itself " + center.ToString( "F3" ) + ", right " + right.ToString( "F3" ) );
		Check( "M25 the blur wraps around the edge of the map on both sides", Mathf.Approximately( wrapLeft, 0.25f ) && Mathf.Approximately( wrapCenter, 0.5f ) && Mathf.Approximately( wrapRight, 0.25f ), "last column " + wrapLeft.ToString( "F3" ) + ", column 0 " + wrapCenter.ToString( "F3" ) + ", column 1 " + wrapRight.ToString( "F3" ) );
		Check( "M25 there is no blur from row to row", ( above == 0.0f ) && ( below == 0.0f ), "above " + above.ToString( "F3" ) + ", below " + below.ToString( "F3" ) );

		// ---- 2. a real planet, always scattered the same way, so that two runs can be compared pixel by pixel
		UnityEngine.Random.InitState( 20261004 );

		PG_AlbedoMap.Initialize();

		const int c_planetId = 90;

		GD_Planet planet = null;

		foreach ( var candidate in gameData.m_planetList )
		{
			if ( candidate.m_id == c_planetId )
			{
				planet = candidate;
			}
		}

		var generator = new PlanetGenerator();

		generator.Start( planet );

		var bytes = Resources.Load<TextAsset>( "Planets/" + c_planetId ).bytes;

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		generator.AsyncProcess( bytes );

		var seconds = stopwatch.ElapsedMilliseconds / 1000.0f;

		var planetAlbedo = GetField( generator, "m_albedoMap" ) as Color[,];

		var planetWidth = planetAlbedo.GetLength( 1 );
		var planetHeight = planetAlbedo.GetLength( 0 );

		// write it out as three bytes a pixel
		var dump = new byte[ planetWidth * planetHeight * 3 ];
		var sum = 0.0;
		var index = 0;

		for ( var y = 0; y < planetHeight; y++ )
		{
			for ( var x = 0; x < planetWidth; x++ )
			{
				var pixel = planetAlbedo[ y, x ];

				dump[ index++ ] = (byte) Mathf.RoundToInt( Mathf.Clamp01( pixel.r ) * 255.0f );
				dump[ index++ ] = (byte) Mathf.RoundToInt( Mathf.Clamp01( pixel.g ) * 255.0f );
				dump[ index++ ] = (byte) Mathf.RoundToInt( Mathf.Clamp01( pixel.b ) * 255.0f );

				sum += pixel.r + pixel.g + pixel.b;
			}
		}

		var dumpPath = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "starflight-probe", "m25-albedo-planet-" + c_planetId + ".bin" );

		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( dumpPath ) );
		System.IO.File.WriteAllBytes( dumpPath, dump );

		Log( "M25 albedo map of planet " + c_planetId + " (" + planetWidth + " by " + planetHeight + ", processed in " + seconds.ToString( "F2" ) + " s): average channel value " + ( sum / ( planetWidth * planetHeight * 3 ) * 255.0 ).ToString( "F3" ) + " of 255, written to " + dumpPath );

		Finish( "scenario=m25 pixel=" + left.ToString( "F3" ) + "/" + center.ToString( "F3" ) + "/" + right.ToString( "F3" ) + " wrap=" + wrapLeft.ToString( "F3" ) + "/" + wrapCenter.ToString( "F3" ) + "/" + wrapRight.ToString( "F3" ) + " rows=" + above.ToString( "F3" ) + "/" + below.ToString( "F3" ) + " planetSeconds=" + seconds.ToString( "F2" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M26: the south pole padding of the prepared height map (planet generator tool)

	IEnumerator ScenarioM26()
	{
		var gameData = DataController.m_instance.m_gameData;

		// the planet generator tool lives in the editor assembly, which this file cannot name at compile time
		var windowType = Type.GetType( "PG_EditorWindow, Assembly-CSharp-Editor" );
		var planetType = Type.GetType( "PG_Planet, Assembly-CSharp-Editor" );

		if ( ( windowType == null ) || ( planetType == null ) )
		{
			Finish( "scenario=m26 abort: the planet generator tool was not found in the editor assembly", 2 );
			yield break;
		}

		var prepareColorMap = windowType.GetMethod( "PrepareColorMap", c_any );
		var prepareHeightMap = windowType.GetMethod( "PrepareHeightMap", c_any );
		var planetConstructor = planetType.GetConstructor( new Type[] { typeof( GameData ), typeof( string ), typeof( int ) } );
		var mapIsValidProperty = planetType.GetProperty( "m_mapIsValid" );
		var heightProperty = planetType.GetProperty( "m_height" );
		var colorProperty = planetType.GetProperty( "m_color" );
		var waterColorField = planetType.GetField( "m_waterColor" );

		// the tool as the planet files of the project were made with it: four padding rows at each pole
		const int c_paddingRows = 4;
		const int c_rows = 24;
		const int c_columns = 48;

		// an instance of the tool's window that unity knows nothing about: creating it the normal way would run OnEnable and OnDisable, and those read and write the
		// settings the tool keeps in the editor preferences of this computer (the two methods used here only need the three fields they are given)
		var window = System.Runtime.Serialization.FormatterServices.GetUninitializedObject( windowType );

		SetField( window, "m_numPolePaddingRows", c_paddingRows );

		var planets = 0;
		var invalid = 0;
		var sameAsFile = 0;
		var onlySouthPaddingDiffers = 0;
		var somethingElseDiffers = 0;
		var southPoleIsBottomRowMaximum = 0;
		var largestChange = 0.0f;
		var largestChangePlanet = -1;
		var firstOther = "none";

		foreach ( var gdPlanet in gameData.m_planetList )
		{
			object pgPlanet = null;

			try
			{
				pgPlanet = planetConstructor.Invoke( new object[] { gameData, "Assets/Planet Generator/Data", gdPlanet.m_id } );
			}
			catch ( TargetInvocationException exception )
			{
				Log( "M26 planet " + gdPlanet.m_id + ": " + exception.InnerException.GetType().Name + ": " + exception.InnerException.Message );
			}

			if ( ( pgPlanet == null ) || !(bool) mapIsValidProperty.GetValue( pgPlanet ) )
			{
				invalid++;

				continue;
			}

			planets++;

			// the tool prepares the colour map first (that is where it picks the padding colours) and then the height map
			prepareColorMap.Invoke( window, new object[] { pgPlanet } );

			var prepared = prepareHeightMap.Invoke( window, new object[] { pgPlanet } ) as float[,];

			var heights = heightProperty.GetValue( pgPlanet ) as float[,];
			var colors = colorProperty.GetValue( pgPlanet ) as Color[,];
			var waterColor = (Color) waterColorField.GetValue( pgPlanet );
			var bottomPaddingColor = (Color) GetField( window, "m_bottomPaddingColor" );

			// what the south pole should rise to: the highest point of the bottom row among the columns that have the padding colour (sea level if that colour is water)
			var bottomRowMaximum = 0.0f;

			for ( var x = 0; x < c_columns; x++ )
			{
				if ( colors[ c_rows - 1, x ] == bottomPaddingColor )
				{
					bottomRowMaximum = Mathf.Max( bottomRowMaximum, heights[ c_rows - 1, x ] );
				}
			}

			if ( bottomPaddingColor == waterColor )
			{
				bottomRowMaximum = 0.0f;
			}

			var lastRow = c_rows + c_paddingRows * 2 - 1;
			var isBottomRowMaximum = true;

			for ( var x = 0; x < c_columns; x++ )
			{
				if ( Mathf.Abs( prepared[ lastRow, x ] - bottomRowMaximum ) > 0.000001f )
				{
					isBottomRowMaximum = false;
				}
			}

			if ( isBottomRowMaximum )
			{
				southPoleIsBottomRowMaximum++;
			}

			// the prepared height map that is in the planet file of the project
			var asset = Resources.Load<TextAsset>( "Planets/" + gdPlanet.m_id );
			var plain = Decompress( asset.bytes );

			Resources.UnloadAsset( asset );

			var offset = 4 + 4 * 3 + 4 * 9;
			var fileWidth = BitConverter.ToInt32( plain, offset );
			var fileHeight = BitConverter.ToInt32( plain, offset + 4 );

			offset += 8;

			var rowsThatDiffer = 0;
			var southPaddingRowsThatDiffer = 0;

			if ( ( fileWidth == c_columns ) && ( fileHeight == lastRow + 1 ) )
			{
				for ( var y = 0; y <= lastRow; y++ )
				{
					var rowDiffers = false;

					for ( var x = 0; x < c_columns; x++ )
					{
						var inFile = BitConverter.ToSingle( plain, offset + ( y * c_columns + x ) * 4 );
						var difference = Mathf.Abs( inFile - prepared[ y, x ] );

						if ( difference > 0.000001f )
						{
							rowDiffers = true;

							if ( ( y == lastRow ) && ( difference > largestChange ) )
							{
								largestChange = difference;
								largestChangePlanet = gdPlanet.m_id;
							}
						}
					}

					if ( rowDiffers )
					{
						rowsThatDiffer++;

						if ( y >= c_rows + c_paddingRows )
						{
							southPaddingRowsThatDiffer++;
						}
					}
				}
			}
			else
			{
				rowsThatDiffer = -1;
			}

			if ( rowsThatDiffer == 0 )
			{
				sameAsFile++;
			}
			else if ( ( rowsThatDiffer > 0 ) && ( rowsThatDiffer == southPaddingRowsThatDiffer ) )
			{
				onlySouthPaddingDiffers++;
			}
			else
			{
				somethingElseDiffers++;

				if ( somethingElseDiffers == 1 )
				{
					firstOther = "planet " + gdPlanet.m_id + " (" + rowsThatDiffer + " rows differ, " + southPaddingRowsThatDiffer + " of them south padding, file map " + fileWidth + " by " + fileHeight + ")";
				}
			}

			if ( ( planets % 50 ) == 0 )
			{
				yield return null;
			}
		}

		Log( "M26 the tool prepared the height maps of " + planets + " planets (" + invalid + " without a source image): the same as in the planet file " + sameAsFile + ", only the south pole padding differs " + onlySouthPaddingDiffers + ", something else differs " + somethingElseDiffers + " (first: " + firstOther + ")" );
		Log( "M26 the south pole rises to the highest point of the bottom row for " + southPoleIsBottomRowMaximum + " of " + planets + " planets; the largest difference to a planet file at the south pole is " + largestChange.ToString( "F3" ) + " (planet " + largestChangePlanet + ")" );

		Check( "M26 the south pole padding rises to the highest point of the bottom row", ( planets > 0 ) && ( southPoleIsBottomRowMaximum == planets ), southPoleIsBottomRowMaximum + " of " + planets + " planets" );
		Check( "M26 nothing but the south pole padding differs from the planet files of the project", somethingElseDiffers == 0, "the same " + sameAsFile + ", only the south pole padding " + onlySouthPaddingDiffers + ", something else " + somethingElseDiffers + " (first: " + firstOther + ")" );

		Finish( "scenario=m26 planets=" + planets + " sameAsFile=" + sameAsFile + " onlySouthPaddingDiffers=" + onlySouthPaddingDiffers + " somethingElseDiffers=" + somethingElseDiffers + " southPoleIsBottomRowMaximum=" + southPoleIsBottomRowMaximum + " largestChange=" + largestChange.ToString( "F3" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- M27: the planet generator tool (editor assembly): settings, rain drops, the blur

	// the settings the planet generator tool starts out with
	static void SetToolDefaults( object window )
	{
		SetField( window, "m_gameDataFileName", "Starflight Game Data" );
		SetField( window, "m_planetImagesPath", "Assets/Planet Generator/Data" );
		SetField( window, "m_resourcesPath", "Resources" );
		SetField( window, "m_debugMode", false );
		SetField( window, "m_textureMapHeight", 1024 );
		SetField( window, "m_textureMapWidth", 2048 );
		SetField( window, "m_numPolePaddingRows", 4 );
		SetField( window, "m_octaves", 10 );
		SetField( window, "m_mountainScale", 0.1f );
		SetField( window, "m_mountainLacunarity", 2.0f );
		SetField( window, "m_mountainPersistence", 0.5f );
		SetField( window, "m_mountainGain", 0.075f );
		SetField( window, "m_craterGain", 0.25f );
		SetField( window, "m_doHydraulicErosionPass", true );
		SetField( window, "m_xyScaleToMeters", 10.0f );
		SetField( window, "m_zScaleToMeters", 400.0f );
		SetField( window, "m_rainWaterAmount", 1.0f );
		SetField( window, "m_sedimentCapacity", 100.0f );
		SetField( window, "m_gravityConstant", -9.8f );
		SetField( window, "m_frictionConstant", 0.5f );
		SetField( window, "m_evaporationConstant", 1.0f );
		SetField( window, "m_depositionConstant", 5.0f );
		SetField( window, "m_dissolvingConstant", 4.0f );
		SetField( window, "m_stepDeltaTime", 0.005f );
		SetField( window, "m_finalBlurRadius", 3 );
	}

	// what the tool says is wrong with its settings after one of them was changed ("no check" if this code has none, "fine" if it finds nothing)
	static string SettingsProblem( object window, MethodInfo check, string field, object value )
	{
		if ( check == null )
		{
			return "no check";
		}

		SetToolDefaults( window );

		if ( field != null )
		{
			SetField( window, field, value );
		}

		var problem = check.Invoke( window, null ) as string;

		return ( problem == null ) ? "fine" : problem;
	}

	// a bowl: the ground falls from the rim to the middle of the map (heights from 0 to 1)
	static float[,] Bowl( int width, int height )
	{
		var map = new float[ height, width ];

		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				var dx = ( x - width * 0.5f ) / ( width * 0.5f );
				var dy = ( y - height * 0.5f ) / ( height * 0.5f );

				map[ y, x ] = 0.25f + 0.5f * Mathf.Clamp01( Mathf.Sqrt( dx * dx + dy * dy ) );
			}
		}

		return map;
	}

	// let one rain drop run on the erosion's current map and count its steps (the map and the constants are statics of the erosion class)
	static int DropSteps( Type erosionType, float evaporationFactor, float frictionFactor, float[,] mapInMeters, int limit )
	{
		var dropType = erosionType.GetNestedType( "Drop", c_any );

		erosionType.GetField( "m_evaporationConstant", c_any ).SetValue( null, evaporationFactor );
		erosionType.GetField( "m_frictionConstant", c_any ).SetValue( null, frictionFactor );
		erosionType.GetField( "m_outputElevation", c_any ).SetValue( null, mapInMeters.Clone() );

		var drop = Activator.CreateInstance( dropType, c_any, null, new object[] { 20.0f, 12.0f, 1.0f, 0.0f, Vector3.zero }, null );
		var update = (Func<bool>) Delegate.CreateDelegate( typeof( Func<bool> ), drop, dropType.GetMethod( "Update", c_any ) );

		var steps = 0;

		while ( ( steps < limit ) && update() )
		{
			steps++;
		}

		return steps;
	}

	IEnumerator ScenarioM27()
	{
		var windowType = Type.GetType( "PG_EditorWindow, Assembly-CSharp-Editor" );
		var erosionType = Type.GetType( "PG_HydraulicErosion, Assembly-CSharp-Editor" );

		if ( ( windowType == null ) || ( erosionType == null ) )
		{
			Finish( "scenario=m27 abort: the planet generator tool was not found in the editor assembly", 2 );
			yield break;
		}

		yield return Frames( 2 );

		// ---- 1. a blur with a radius of zero is no blur (the tool's Final Blur Radius slider starts at zero)
		var bumps = new float[ 64, 128 ];

		for ( var y = 0; y < 64; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				bumps[ y, x ] = ( ( x * 7 + y * 13 ) % 32 ) / 32.0f;
			}
		}

		var unblurred = new PG_GaussianBlurElevation().Process( bumps, 0, 0 );

		var notANumber = 0;
		var changed = 0;

		for ( var y = 0; y < 64; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				if ( float.IsNaN( unblurred[ y, x ] ) )
				{
					notANumber++;
				}
				else if ( Mathf.Abs( unblurred[ y, x ] - bumps[ y, x ] ) > 0.000001f )
				{
					changed++;
				}
			}
		}

		// a blur with a radius of three still spreads a single point the way it did (the weights, worked out here the way the filter does it)
		var point = new float[ 64, 128 ];

		point[ 32, 64 ] = 1.0f;

		var blurred = new PG_GaussianBlurElevation().Process( point, 3, 3 );

		var weights = new float[ 7 ];
		var weightSum = 0.0f;

		for ( var i = 0; i < 7; i++ )
		{
			weightSum += weights[ i ] = Mathf.Exp( -Mathf.Pow( i - 3, 2.0f ) / ( 2.0f * Mathf.Pow( 3 / 3.2f, 2.0f ) ) );
		}

		var expectedCenter = ( weights[ 3 ] / weightSum ) * ( weights[ 3 ] / weightSum );
		var expectedSide = ( weights[ 2 ] / weightSum ) * ( weights[ 3 ] / weightSum );

		Log( "M27 blur with radius 0 of a 128 by 64 map: " + notANumber + " values are not a number, " + changed + " changed | blur with radius 3 of one point: centre " + blurred[ 32, 64 ].ToString( "F5" ) + " (expected " + expectedCenter.ToString( "F5" ) + "), next to it " + blurred[ 32, 65 ].ToString( "F5" ) + " (expected " + expectedSide.ToString( "F5" ) + ")" );

		Check( "M27 a blur with a radius of zero leaves the map as it is", ( notANumber == 0 ) && ( changed == 0 ), notANumber + " values are not a number, " + changed + " changed" );
		Check( "M27 a blur with a radius of three still gives the same weights", ( Mathf.Abs( blurred[ 32, 64 ] - expectedCenter ) < 0.00001f ) && ( Mathf.Abs( blurred[ 32, 65 ] - expectedSide ) < 0.00001f ), "centre " + blurred[ 32, 64 ].ToString( "F5" ) + " / " + expectedCenter.ToString( "F5" ) + ", side " + blurred[ 32, 65 ].ToString( "F5" ) + " / " + expectedSide.ToString( "F5" ) );

		// ---- 2. the tool checks its settings before it starts
		// (an instance of the window that unity knows nothing about, so that the tool's settings in the editor preferences of this computer are neither read nor written)
		var window = System.Runtime.Serialization.FormatterServices.GetUninitializedObject( windowType );
		var check = windowType.GetMethod( "GetSettingsProblem", c_any );

		var defaults = SettingsProblem( window, check, null, null );
		var noEvaporation = SettingsProblem( window, check, "m_evaporationConstant", 0.0f );
		var noMountainScale = SettingsProblem( window, check, "m_mountainScale", 0.0f );
		var noLacunarity = SettingsProblem( window, check, "m_mountainLacunarity", 0.0f );
		var oddHeight = SettingsProblem( window, check, "m_textureMapHeight", 1000 );
		var otherHeight = SettingsProblem( window, check, "m_textureMapHeight", 512 );

		Log( "M27 settings: defaults -> " + defaults );
		Log( "M27 settings: evaporation 0 -> " + noEvaporation );
		Log( "M27 settings: mountain scale 0 -> " + noMountainScale );
		Log( "M27 settings: mountain lacunarity 0 -> " + noLacunarity );
		Log( "M27 settings: texture map height 1000 -> " + oddHeight );
		Log( "M27 settings: texture map height 512 -> " + otherHeight );

		Check( "M27 the tool accepts its default settings", defaults == "fine", defaults );
		Check( "M27 the tool refuses settings that end in a division by zero or an endless drop", noEvaporation.Contains( "Evaporation" ) && noMountainScale.Contains( "Mountain Scale" ) && noLacunarity.Contains( "Lacunarity" ) && oddHeight.Contains( "Texture Map Height" ) && otherHeight.Contains( "Texture Map Height" ), "evaporation 0: " + noEvaporation + " | scale 0: " + noMountainScale + " | lacunarity 0: " + noLacunarity + " | height 1000: " + oddHeight + " | height 512: " + otherHeight );

		// ---- 3. a game data file that is not there: the tool must say so and stop, not throw (nothing is generated either way: it never gets to the planets)
		SetToolDefaults( window );
		SetField( window, "m_gameDataFileName", "No Such Game Data File" );

		var magicThrew = "nothing";

		s_errorsLogged = 0;

		Application.logMessageReceived += CountErrors;

		try
		{
			windowType.GetMethod( "MakeSomeMagic", c_any ).Invoke( window, null );
		}
		catch ( TargetInvocationException exception )
		{
			magicThrew = exception.InnerException.GetType().Name;
		}

		yield return null;

		Application.logMessageReceived -= CountErrors;

		Log( "M27 the tool with a game data file that is not there: threw " + magicThrew + ", errors logged " + s_errorsLogged );

		Check( "M27 a missing game data file is reported and does not throw", ( magicThrew == "nothing" ) && ( s_errorsLogged == 1 ), "threw " + magicThrew + ", errors logged " + s_errorsLogged );

		// ---- 4. rain drops. Run the erosion once on a map too small to get any drops (128 by 64), which sets up its statics, then let single drops run on a bowl
		var erosion = Activator.CreateInstance( erosionType );
		var process = erosionType.GetMethod( "Process", c_any );

		const float c_zScale = 400.0f;
		const float c_stepDeltaTime = 0.005f;

		object[] Arguments( float[,] map, float evaporation, float friction )
		{
			// source, minimum elevation, xy scale, z scale, rain, sediment capacity, gravity, friction, evaporation, deposition, dissolving, step delta time, final blur radius
			return new object[] { map, 0.0f, 10.0f, c_zScale, 1.0f, 100.0f, -9.8f, friction, evaporation, 5.0f, 4.0f, c_stepDeltaTime, 3 };
		}

		process.Invoke( erosion, Arguments( Bowl( 128, 64 ), 1.0f, 0.5f ) );

		var bowlInMeters = Bowl( 128, 64 );

		for ( var y = 0; y < 64; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				bowlInMeters[ y, x ] *= c_zScale;
			}
		}

		const int c_limit = 2000000;

		var stepsWithEvaporation = DropSteps( erosionType, 1.0f - 1.0f * c_stepDeltaTime, 1.0f - 0.5f * c_stepDeltaTime, bowlInMeters, c_limit );
		var stepsWithoutEvaporation = DropSteps( erosionType, 1.0f, 1.0f - 0.5f * c_stepDeltaTime, bowlInMeters, c_limit );
		var stepsWithoutEvaporationOrFriction = DropSteps( erosionType, 1.0f, 1.0f, bowlInMeters, c_limit );

		Log( "M27 one drop on a bowl (stopped after " + c_limit + " steps): with the default evaporation " + stepsWithEvaporation + " steps, with no evaporation " + stepsWithoutEvaporation + ", with no evaporation and no friction " + stepsWithoutEvaporationOrFriction );

		// the whole erosion pass with no evaporation, on a map large enough to get drops (256 by 128: 32768 of them) - only on code that limits the steps of a drop
		var limitField = erosionType.GetField( "c_maximumDropStepsWithoutEvaporation", c_any );
		var passSeconds = -1.0f;
		var passReturned = false;

		if ( limitField != null )
		{
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			var result = process.Invoke( erosion, Arguments( Bowl( 256, 128 ), 0.0f, 0.0f ) ) as float[,];

			passSeconds = stopwatch.ElapsedMilliseconds / 1000.0f;
			passReturned = ( result != null ) && !float.IsNaN( result[ 64, 128 ] );

			Log( "M27 the erosion pass with no evaporation and no friction on a 256 by 128 bowl came back after " + passSeconds.ToString( "F1" ) + " s (a drop is given " + limitField.GetValue( null ) + " steps at the most)" );
		}
		else
		{
			Log( "M27 this code does not limit the steps of a drop, so the erosion pass with no evaporation was not run (it would never come back if a drop does not end)" );
		}

		Check( "M27 with evaporation a drop ends by itself", ( stepsWithEvaporation > 0 ) && ( stepsWithEvaporation < 3000 ), stepsWithEvaporation + " steps" );
		Check( "M27 the erosion pass comes back even when no drop ends by itself", passReturned, ( limitField == null ) ? "not run: no limit in this code" : ( "came back after " + passSeconds.ToString( "F1" ) + " s" ) );

		Finish( "scenario=m27 blur0=" + notANumber + "NaN/" + changed + "changed settings=[" + defaults + "] refused=" + ( ( check == null ) ? "no check" : "yes" ) + " missingGameData=" + magicThrew + " dropSteps=" + stepsWithEvaporation + "/" + stepsWithoutEvaporation + "/" + stepsWithoutEvaporationOrFriction + " pass=" + ( passReturned ? passSeconds.ToString( "F1" ) + "s" : "not run" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: the bank ledger and the most the player can buy (starport)

	// open a panel, change the bank balance as if something had been bought or sold in it, close it, and return what the ledger says about it
	IEnumerator LedgerEntry( Panel panel, string label, int balanceChange, string[] result )
	{
		var playerData = DataController.m_instance.m_playerData;

		yield return OpenPanel( panel, label );

		playerData.m_bank.m_currentBalance += balanceChange;

		var before = playerData.m_bank.m_transactionList.Count;

		yield return ClosePanel( panel, label );

		var transactions = playerData.m_bank.m_transactionList;

		result[ 0 ] = ( transactions.Count > before ) ? transactions[ transactions.Count - 1 ].m_amount : "nothing logged";
	}

	IEnumerator ScenarioStarportLedger()
	{
		var playerData = DataController.m_instance.m_playerData;
		var depot = FindPanel<TradeDepotPanel>();
		var shipConfiguration = FindPanel<ShipConfigurationPanel>();

		var result = new string[ 1 ];

		// money that came in and money that went out, in both panels that write to the ledger this way
		yield return LedgerEntry( depot, "trade depot", 1400, result );

		var depotIncome = result[ 0 ];

		yield return LedgerEntry( depot, "trade depot", -500, result );

		var depotSpending = result[ 0 ];

		yield return LedgerEntry( shipConfiguration, "ship configuration", 1400, result );

		var shipIncome = result[ 0 ];

		yield return LedgerEntry( shipConfiguration, "ship configuration", -500, result );

		var shipSpending = result[ 0 ];

		Log( "ledger: trade depot +1400 reads '" + depotIncome + "', -500 reads '" + depotSpending + "' | ship configuration +1400 reads '" + shipIncome + "', -500 reads '" + shipSpending + "'" );

		Check( "ledger: money that came in reads 1400+", ( depotIncome == "1400+" ) && ( shipIncome == "1400+" ), "trade depot '" + depotIncome + "', ship configuration '" + shipIncome + "'" );
		Check( "ledger: money that went out reads 500-", ( depotSpending == "500-" ) && ( shipSpending == "500-" ), "trade depot '" + depotSpending + "', ship configuration '" + shipSpending + "'" );

		// ---- the most of an element the player can afford, in tenths of a cubic meter: lead costs 80 a cubic meter
		var gameData = DataController.m_instance.m_gameData;
		var lead = -1;

		for ( var i = 0; i < gameData.m_elementList.Length; i++ )
		{
			if ( gameData.m_elementList[ i ].m_name == "Lead" )
			{
				lead = i;
			}
		}

		var price = gameData.m_elementList[ lead ].m_starportPrice;
		var balanceBefore = playerData.m_bank.m_currentBalance;

		playerData.m_bank.m_currentBalance = 1000000;

		var affordableWithAMillion = (int) Call( depot, "GetMaximumBuyAmountDueToCurrentBalance", lead );

		playerData.m_bank.m_currentBalance = 300000000;

		var affordableWith300Million = (int) Call( depot, "GetMaximumBuyAmountDueToCurrentBalance", lead );

		playerData.m_bank.m_currentBalance = int.MaxValue;

		var affordableWithTheMost = (int) Call( depot, "GetMaximumBuyAmountDueToCurrentBalance", lead );

		playerData.m_bank.m_currentBalance = balanceBefore;

		var expectedWith300Million = (int) ( 300000000L * 10L / price );
		var expectedWithTheMost = (int) ( (long) int.MaxValue * 10L / price );

		Log( "buy maximum of lead at " + price + " MU: with 1,000,000 MU " + affordableWithAMillion + " tenths, with 300,000,000 MU " + affordableWith300Million + " (should be " + expectedWith300Million + "), with " + int.MaxValue + " MU " + affordableWithTheMost + " (should be " + expectedWithTheMost + ")" );

		Check( "buy maximum: a million MU buys what it did", affordableWithAMillion == 1000000 * 10 / price, affordableWithAMillion + " tenths of a cubic meter" );
		Check( "buy maximum: it does not overflow with a large balance", ( affordableWith300Million == expectedWith300Million ) && ( affordableWithTheMost == expectedWithTheMost ), "300,000,000 MU: " + affordableWith300Million + " (should be " + expectedWith300Million + "), " + int.MaxValue + " MU: " + affordableWithTheMost + " (should be " + expectedWithTheMost + ")" );

		Finish( "scenario=starport-ledger depot=" + depotIncome + "/" + depotSpending + " ship=" + shipIncome + "/" + shipSpending + " buyMaximum=" + affordableWithAMillion + "/" + affordableWith300Million + "/" + affordableWithTheMost + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: cargo volumes in cubic meters, a deposit that does not fit, the size of a scanned vessel

	// ---------------------------------------------------------------- the push-back of what the terrain vehicle touches

	// what the physics steps leave in the terrain vehicle's push-back vector (TerrainVehicle.AddPushBack, called by TerrainVehicleCollider from OnCollisionStay)
	static bool s_samplingPushBack;
	static float s_largestPushBack;
	static int s_stepsWithPushBack;
	static float s_largestSpeedAfterPush;

	// reads the push-back vector after every physics step, before the next Update resets it
	IEnumerator SamplePushBack( TerrainVehicle terrainVehicle )
	{
		var general = DataController.m_instance.m_playerData.m_general;

		while ( s_samplingPushBack )
		{
			yield return new WaitForFixedUpdate();

			// (read by name: a fix may do without the vector)
			var pushField = FieldOrNull( terrainVehicle, "m_pushBackVector" );
			var push = ( pushField is Vector3 pushVector ) ? pushVector : Vector3.zero;

			if ( push.magnitude > 0.0001f )
			{
				s_stepsWithPushBack++;
			}

			s_largestPushBack = Mathf.Max( s_largestPushBack, push.magnitude );
			s_largestSpeedAfterPush = Mathf.Max( s_largestSpeedAfterPush, general.m_currentSpeed );
		}
	}

	// the terrain vehicle stands still for this long, and the drawn vehicle (its transform after Update) is compared with where it is saved: [0] the largest gap
	// between them, [1] the frames with a gap, [2] how far the saved place moved, [3] the largest move of the drawn vehicle from one frame to the next, [4] the
	// frames, [5] how far the saved place moved in the second half, [6] the largest push-back after a physics step, [7] the physics steps with a push-back,
	// [8] the largest speed after a physics step, [9] the farthest the drawn vehicle got from where it stood
	IEnumerator WatchStandingVehicle( TerrainVehicle terrainVehicle, float seconds, float[] result )
	{
		var general = DataController.m_instance.m_playerData.m_general;
		var start = general.m_coordinates;
		var lastDrawn = terrainVehicle.transform.localPosition;
		var halfway = start;
		var time = 0.0f;

		for ( var i = 0; i < result.Length; i++ )
		{
			result[ i ] = 0.0f;
		}

		s_largestPushBack = 0.0f;
		s_stepsWithPushBack = 0;
		s_largestSpeedAfterPush = 0.0f;
		s_samplingPushBack = true;

		StartCoroutine( SamplePushBack( terrainVehicle ) );

		while ( time < seconds )
		{
			yield return null;

			time += Time.deltaTime;

			var drawn = terrainVehicle.transform.localPosition;
			var saved = general.m_coordinates;
			var gap = Vector2.Distance( new Vector2( drawn.x, drawn.z ), new Vector2( saved.x, saved.z ) );

			result[ 0 ] = Mathf.Max( result[ 0 ], gap );
			result[ 1 ] += ( gap > 0.01f ) ? 1.0f : 0.0f;
			result[ 3 ] = Mathf.Max( result[ 3 ], Vector2.Distance( new Vector2( drawn.x, drawn.z ), new Vector2( lastDrawn.x, lastDrawn.z ) ) );
			result[ 9 ] = Mathf.Max( result[ 9 ], Vector2.Distance( new Vector2( drawn.x, drawn.z ), new Vector2( start.x, start.z ) ) );
			result[ 4 ] += 1.0f;

			if ( ( time >= seconds * 0.5f ) && ( halfway == start ) )
			{
				halfway = saved;
			}

			lastDrawn = drawn;
		}

		s_samplingPushBack = false;

		var end = general.m_coordinates;

		result[ 2 ] = Vector2.Distance( new Vector2( start.x, start.z ), new Vector2( end.x, end.z ) );
		result[ 5 ] = Vector2.Distance( new Vector2( halfway.x, halfway.z ), new Vector2( end.x, end.z ) );
		result[ 6 ] = s_largestPushBack;
		result[ 7 ] = s_stepsWithPushBack;
		result[ 8 ] = s_largestSpeedAfterPush;

		yield return new WaitForFixedUpdate();
	}

	static string DescribeStanding( float[] result )
	{
		return "gap between drawn and saved up to " + result[ 0 ].ToString( "F3" ) + " in " + result[ 1 ] + " of " + result[ 4 ] + " frames, saved place moved " + result[ 2 ].ToString( "F3" ) + " (" + result[ 5 ].ToString( "F3" ) + " in the second half), largest drawn move in a frame " + result[ 3 ].ToString( "F3" ) + ", push-back up to " + result[ 6 ].ToString( "F3" ) + " in " + result[ 7 ] + " physics steps, speed up to " + result[ 8 ].ToString( "F3" ) + ", drawn up to " + result[ 9 ].ToString( "F3" ) + " from where it stood";
	}

	// the terrain vehicle back where it stood, stopped, and drawn there
	static IEnumerator StandTheVehicleAt( TerrainVehicle terrainVehicle, Vector3 coordinates )
	{
		var general = DataController.m_instance.m_playerData.m_general;

		terrainVehicle.TurnOffEngines();

		general.m_coordinates = coordinates;
		general.m_currentSpeed = 0.0f;

		yield return new WaitForFixedUpdate();
		yield return Frames( 3 );

		general.m_coordinates = coordinates;
		general.m_currentSpeed = 0.0f;

		yield return Frames( 1 );
	}

	// the distance ahead of the vehicle's middle at which a deposit's sphere stops overlapping the vehicle's capsule (Physics.ComputePenetration, no move needed)
	// the elevation of the ground at this place of the planet, the way the terrain vehicle reads it (TerrainVehicle.ApplyElevation, before the scale)
	static float GroundElevation( PlanetGenerator planetGenerator, Vector3 worldCoordinates )
	{
		var x = worldCoordinates.x * 0.25f + planetGenerator.m_textureMapWidth * 0.5f - 0.5f;
		var y = worldCoordinates.z * 0.25f + planetGenerator.m_textureMapHeight * 0.5f - 0.5f;

		return planetGenerator.GetBicubicSmoothedElevation( x, y );
	}

	static float s_highestGroundFound;

	// the nearest place, in rings of 16 units, where the terrain vehicle and everything 16 around it stand above the water (the vehicle floats and bobs below it)
	static bool FindDryGround( TerrainVehicle terrainVehicle, Vector3 near, out Vector3 dry )
	{
		dry = near;

		var planetGenerator = FieldOrNull( terrainVehicle, "m_planetGenerator" ) as PlanetGenerator;

		if ( planetGenerator == null )
		{
			return false;
		}

		var water = planetGenerator.m_waterElevation + 0.002f;

		s_highestGroundFound = -1.0f;

		for ( var ring = 0; ring < 60; ring++ )
		{
			for ( var i = 0; i < 16; i++ )
			{
				var angle = i * 22.5f * Mathf.Deg2Rad;
				var place = near + new Vector3( Mathf.Sin( angle ), 0.0f, Mathf.Cos( angle ) ) * ring * 16.0f;

				s_highestGroundFound = Mathf.Max( s_highestGroundFound, GroundElevation( planetGenerator, place ) );

				if ( ( GroundElevation( planetGenerator, place ) >= water ) && ( GroundElevation( planetGenerator, place + Vector3.forward * 16.0f ) >= water ) && ( GroundElevation( planetGenerator, place + Vector3.back * 16.0f ) >= water ) && ( GroundElevation( planetGenerator, place + Vector3.left * 16.0f ) >= water ) && ( GroundElevation( planetGenerator, place + Vector3.right * 16.0f ) >= water ) )
				{
					dry = place;

					return true;
				}
			}
		}

		return false;
	}

	static float DistanceWhereItTouches( Collider vehicleCollider, TerrainElement deposit, Vector3 from, Vector3 forward )
	{
		var sphere = deposit.GetComponentInChildren<SphereCollider>();
		var offset = sphere.transform.position - deposit.transform.position;
		var near = 0.0f;
		var far = 40.0f;

		for ( var i = 0; i < 40; i++ )
		{
			var middle = ( near + far ) * 0.5f;
			var overlaps = Physics.ComputePenetration( vehicleCollider, vehicleCollider.transform.position, vehicleCollider.transform.rotation, sphere, from + forward * middle + offset, sphere.transform.rotation, out _, out _ );

			if ( overlaps )
			{
				near = middle;
			}
			else
			{
				far = middle;
			}
		}

		return far;
	}

	// every object that pushes the terrain vehicle, moved 1000 to the east if it is within 80 of this place (on the ground, any height)
	static TerrainVehicleCollider[] ClearAroundTheVehicle( Vector3 place )
	{
		var pushers = FindObjectsByType<TerrainVehicleCollider>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );

		foreach ( var pusher in pushers )
		{
			var flat = new Vector3( pusher.transform.position.x - place.x, 0.0f, pusher.transform.position.z - place.z );

			if ( flat.magnitude < 80.0f )
			{
				pusher.transform.position += Vector3.right * 1000.0f;
			}
		}

		return pushers;
	}

	// the deposit whose sphere is the largest
	static TerrainElement LargestDeposit( TerrainElement[] deposits, out float largestRadius )
	{
		var largest = deposits[ 0 ];

		largestRadius = 0.0f;

		foreach ( var deposit in deposits )
		{
			var sphere = deposit.GetComponentInChildren<SphereCollider>();

			if ( ( sphere != null ) && ( sphere.radius * sphere.transform.lossyScale.x > largestRadius ) )
			{
				largest = deposit;
				largestRadius = sphere.radius * sphere.transform.lossyScale.x;
			}
		}

		return largest;
	}

	IEnumerator ScenarioPushBack()
	{
		var playerData = DataController.m_instance.m_playerData;
		var general = playerData.m_general;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 94 of the Arth system, a small rock planet (no life, no trees: only deposits touch the vehicle here); planet 90, where the scenarios usually
		// land, is sea around the landing place, where the vehicle floats and its model bobs with the time, which case 9 looks at
		yield return LandInTheArthSystemAt( 94, Vector3.zero, done );

		if ( general.m_location != PD_General.Location.Disembarked )
		{
			Finish( "scenario=pushback abort: never got into the terrain vehicle (" + general.m_location + ")", 2 );
			yield break;
		}

		var terrainVehicle = controller.m_terrainVehicle;
		var vehicleCollider = terrainVehicle.GetComponentInChildren<CapsuleCollider>();
		var deposits = controller.m_disembarked.m_terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true );

		if ( ( vehicleCollider == null ) || ( deposits.Length < 2 ) )
		{
			Finish( "scenario=pushback abort: no vehicle collider (" + ( vehicleCollider != null ) + ") or fewer than two deposits (" + deposits.Length + ")", 2 );
			yield break;
		}

		// a game of 60 frames a second, the same in every run: the physics runs at 50 steps a second, so most frames have one and some have none
		Time.captureDeltaTime = 1.0f / 60.0f;

		// dry ground, where the vehicle holds still and a deposit put down just clear of it stays clear
		var landedAt = general.m_coordinates;

		if ( !FindDryGround( terrainVehicle, landedAt, out var start ) )
		{
			Time.captureDeltaTime = 0.0f;

			var planetGeneratorForLog = FieldOrNull( terrainVehicle, "m_planetGenerator" ) as PlanetGenerator;

			Finish( "scenario=pushback abort: no dry ground near " + landedAt + " (water " + ( ( planetGeneratorForLog != null ) ? planetGeneratorForLog.m_waterElevation : -1.0f ) + ", highest ground found " + s_highestGroundFound + ")", 2 );
			yield break;
		}

		var forward = new Vector3( general.m_currentDirection.x, 0.0f, general.m_currentDirection.z );

		forward = ( forward.sqrMagnitude < 0.0001f ) ? Vector3.forward : forward.normalized;

		var right = Vector3.Cross( Vector3.up, forward );

		// everything that pushes out of the way
		var pushers = ClearAroundTheVehicle( start );

		yield return StandTheVehicleAt( terrainVehicle, start );

		Log( "pushback: dry ground at " + start.ToString( "F1" ) + " (landed at " + landedAt.ToString( "F1" ) + "), the vehicle bobs there by " + FieldOrNull( terrainVehicle, "m_waterEffectAmount" ) );

		// the largest deposit, and another one
		var largest = LargestDeposit( deposits, out var largestRadius );

		var other = ( largest == deposits[ 0 ] ) ? deposits[ 1 ] : deposits[ 0 ];
		var body = largest.GetComponent<Rigidbody>();
		var largestSphere = largest.GetComponentInChildren<SphereCollider>();

		Log( "pushback: vehicle at " + start.ToString( "F2" ) + " facing " + forward.ToString( "F2" ) + ", its capsule on " + vehicleCollider.name + " (radius " + vehicleCollider.radius + ", height " + vehicleCollider.height + ", contact offset " + vehicleCollider.contactOffset + ", bounds " + vehicleCollider.bounds.extents.ToString( "F2" ) + "), rigidbody on the vehicle: " + ( ( terrainVehicle.GetComponentInChildren<Rigidbody>() != null ) ? "kinematic " + terrainVehicle.GetComponentInChildren<Rigidbody>().isKinematic + " on " + terrainVehicle.GetComponentInChildren<Rigidbody>().name : "none" ) + ", " + pushers.Length + " objects push" );
		Log( "pushback: the largest deposit is " + largest.GetElementName() + " (world radius " + largestRadius.ToString( "F2" ) + ", centre " + largestSphere.center + ", contact offset " + largestSphere.contactOffset + "), rigidbody " + ( body != null ) + ( ( body != null ) ? " kinematic " + body.isKinematic + " gravity " + body.useGravity + " constraints " + body.constraints : "" ) + "; the other is " + other.GetElementName() );

		// ---- 1. the control: nothing near the vehicle
		var nothing = new float[ 10 ];

		yield return StandTheVehicleAt( terrainVehicle, start );
		yield return WatchStandingVehicle( terrainVehicle, 3.0f, nothing );

		Log( "pushback: nothing near: " + DescribeStanding( nothing ) );

		// where the largest deposit stops overlapping the vehicle straight ahead
		yield return StandTheVehicleAt( terrainVehicle, start );

		var from = terrainVehicle.transform.position;
		var edge = DistanceWhereItTouches( vehicleCollider, largest, from, forward );
		var touchGap = ( vehicleCollider.contactOffset + largestSphere.contactOffset ) * 0.5f;

		Log( "pushback: the " + largest.GetElementName() + " deposit stops overlapping the vehicle " + edge.ToString( "F3" ) + " ahead of it (the pickup reaches 10, TerrainElement.c_pickupDistance)" );

		// ---- 2. the control: the deposit a unit clear of the vehicle
		var clear = new float[ 10 ];

		largest.transform.position = from + forward * ( edge + 1.0f );

		yield return StandTheVehicleAt( terrainVehicle, start );
		yield return WatchStandingVehicle( terrainVehicle, 3.0f, clear );

		Log( "pushback: " + largest.GetElementName() + " a unit clear: " + DescribeStanding( clear ) );

		// ---- 3. the deposit touching the vehicle, inside the contact offset but not overlapping
		var touching = new float[ 10 ];

		largest.transform.position = from + forward * ( edge + touchGap );

		var depositBefore = largest.transform.position;

		yield return StandTheVehicleAt( terrainVehicle, start );
		yield return WatchStandingVehicle( terrainVehicle, 3.0f, touching );

		Log( "pushback: " + largest.GetElementName() + " touching (" + touchGap.ToString( "F3" ) + " clear): " + DescribeStanding( touching ) + "; the deposit moved " + Vector3.Distance( depositBefore, largest.transform.position ).ToString( "F3" ) );

		// ---- 4. the deposit a unit into the vehicle
		var overlapping = new float[ 10 ];

		largest.transform.position = from + forward * ( edge - 1.0f );

		yield return StandTheVehicleAt( terrainVehicle, start );
		yield return WatchStandingVehicle( terrainVehicle, 3.0f, overlapping );

		Log( "pushback: " + largest.GetElementName() + " a unit into the vehicle: " + DescribeStanding( overlapping ) );

		// ---- 5. two deposits 2 in front of the vehicle's middle, where the scenarios used to put a deposit to pick up
		var two = new float[ 10 ];

		largest.transform.position = from + forward * 2.0f;
		other.transform.position = from + forward * 2.0f + right * 1.0f;

		yield return StandTheVehicleAt( terrainVehicle, start );
		yield return WatchStandingVehicle( terrainVehicle, 3.0f, two );

		Log( "pushback: " + largest.GetElementName() + " and " + other.GetElementName() + " 2 in front: " + DescribeStanding( two ) );

		// ---- 6. the same at 10 frames a second, as on a slow computer: five physics steps a frame, each adding to the push before Update uses it
		var twoSlow = new float[ 10 ];

		Time.captureDeltaTime = 0.1f;

		yield return StandTheVehicleAt( terrainVehicle, start );
		yield return WatchStandingVehicle( terrainVehicle, 3.0f, twoSlow );

		Log( "pushback: " + largest.GetElementName() + " and " + other.GetElementName() + " 2 in front at 10 frames a second: " + DescribeStanding( twoSlow ) );

		// ---- 7. the same with one long frame in the middle (Time.maximumDeltaTime, a third of a second by default): sixteen physics steps in one frame
		var twoHitch = new float[ 10 ];

		Time.captureDeltaTime = 1.0f / 60.0f;

		yield return StandTheVehicleAt( terrainVehicle, start );

		var hitchFrom = terrainVehicle.transform.localPosition;
		var hitchStart = general.m_coordinates;

		s_largestPushBack = 0.0f;
		s_stepsWithPushBack = 0;
		s_samplingPushBack = true;

		StartCoroutine( SamplePushBack( terrainVehicle ) );

		yield return Frames( 10 );

		Time.captureDeltaTime = Time.maximumDeltaTime;

		yield return null;

		var hitchDrawn = terrainVehicle.transform.localPosition;
		var hitchJump = Vector2.Distance( new Vector2( hitchDrawn.x, hitchDrawn.z ), new Vector2( hitchFrom.x, hitchFrom.z ) );
		var hitchSaved = Vector2.Distance( new Vector2( general.m_coordinates.x, general.m_coordinates.z ), new Vector2( hitchStart.x, hitchStart.z ) );

		Time.captureDeltaTime = 1.0f / 60.0f;

		yield return Frames( 10 );

		s_samplingPushBack = false;

		twoHitch[ 3 ] = hitchJump;
		twoHitch[ 2 ] = hitchSaved;

		Log( "pushback: " + largest.GetElementName() + " and " + other.GetElementName() + " 2 in front, one frame of " + Time.maximumDeltaTime.ToString( "F3" ) + " s: the drawn vehicle moved " + hitchJump.ToString( "F3" ) + " in that frame from where it stood, the saved place " + hitchSaved.ToString( "F3" ) + " (largest push-back after a physics step " + s_largestPushBack.ToString( "F3" ) + ")" );

		// ---- 8. driving into the Lead deposit from 12 units clear of it, at 60 frames a second: the port's vehicle stops at a deposit (keep that, or not: the owner's call)
		other.transform.position += Vector3.right * 1000.0f;
		largest.transform.position = from + forward * ( edge + 12.0f );

		yield return StandTheVehicleAt( terrainVehicle, start );

		var deepest = -100.0f;
		var driveTime = 0.0f;

		// the real Move button, and the up arrow of a keyboard held down (the stick to the north, which is where the vehicle faces)
		PressButton( ButtonController.ButtonSet.TerrainVehicle, 1 );

		yield return Frames( 30 );

		HoldKeys( Key.UpArrow );

		while ( driveTime < 6.0f )
		{
			yield return null;

			driveTime += Time.deltaTime;

			if ( driveTime >= 4.0f )
			{
				HoldKeys();
			}

			var ahead = Vector3.Dot( largest.transform.position - terrainVehicle.transform.position, forward );

			deepest = Mathf.Max( deepest, edge - ahead );
		}

		HoldKeys();

		terrainVehicle.TurnOffEngines();

		var drivenTo = Vector2.Distance( new Vector2( general.m_coordinates.x, general.m_coordinates.z ), new Vector2( start.x, start.z ) );

		var driven = new float[ 10 ];

		yield return WatchStandingVehicle( terrainVehicle, 2.0f, driven );

		var restAhead = Vector3.Dot( largest.transform.position - general.m_coordinates, forward );

		Log( "pushback: driving 4 s into " + largest.GetElementName() + " from 12 clear: the vehicle went " + drivenTo.ToString( "F3" ) + ", at most " + deepest.ToString( "F3" ) + " into it, resting with its middle " + restAhead.ToString( "F3" ) + " ahead (it touches at " + edge.ToString( "F3" ) + "), then standing: " + DescribeStanding( driven ) );

		// ---- 9. floating at the landing place, Lead put down just clear of the vehicle's nose: the model bobs (up to 5 degrees of pitch and 15 of yaw,
		// m_waterBobAmount) and can tilt into the deposit, which pushes the vehicle away and never pulls it back, so it may drift a little, but no farther
		// than the bob reaches
		var floating = new float[ 10 ];

		largest.transform.position += Vector3.right * 1000.0f;
		other.transform.position += Vector3.right * 1000.0f;

		yield return StandTheVehicleAt( terrainVehicle, start );

		Time.captureDeltaTime = 0.0f;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 90, Vector3.zero, done );

		Time.captureDeltaTime = 1.0f / 60.0f;

		var waterSpot = general.m_coordinates;
		var deposits90 = controller.m_disembarked.m_terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true );
		var floatingLog = "never got into the terrain vehicle on planet 90";
		var waterEdge = 0.0f;

		if ( ( general.m_location == PD_General.Location.Disembarked ) && ( deposits90.Length > 0 ) )
		{
			ClearAroundTheVehicle( waterSpot );

			var deposit90 = LargestDeposit( deposits90, out _ );
			var sphere90 = deposit90.GetComponentInChildren<SphereCollider>();

			yield return StandTheVehicleAt( terrainVehicle, waterSpot );

			var waterFrom = terrainVehicle.transform.position;

			waterEdge = DistanceWhereItTouches( vehicleCollider, deposit90, waterFrom, forward );

			deposit90.transform.position = waterFrom + forward * ( waterEdge + ( vehicleCollider.contactOffset + sphere90.contactOffset ) * 0.5f );

			yield return StandTheVehicleAt( terrainVehicle, waterSpot );
			yield return WatchStandingVehicle( terrainVehicle, 6.0f, floating );

			floatingLog = "floating at " + waterSpot.ToString( "F1" ) + " on planet 90 (bob " + FieldOrNull( terrainVehicle, "m_waterEffectAmount" ) + ") with " + deposit90.GetElementName() + " just clear of its nose (" + waterEdge.ToString( "F2" ) + " ahead) for 6 s: " + DescribeStanding( floating );

			deposit90.transform.position += Vector3.right * 1000.0f;

			yield return StandTheVehicleAt( terrainVehicle, waterSpot );
		}
		else
		{
			floating[ 0 ] = 100.0f;
			floating[ 2 ] = 100.0f;
		}

		Log( "pushback: " + floatingLog );

		Time.captureDeltaTime = 0.0f;

		Check( "pushback: nothing near, the vehicle stands still and is drawn where it is (control)", ( nothing[ 2 ] < 0.01f ) && ( nothing[ 0 ] < 0.01f ), DescribeStanding( nothing ) );
		Check( "pushback: a deposit a unit clear does not touch the vehicle (control)", ( clear[ 2 ] < 0.01f ) && ( clear[ 0 ] < 0.01f ) && ( clear[ 7 ] == 0.0f ), DescribeStanding( clear ) );
		Check( "pushback: a deposit that touches a standing vehicle does not move it", touching[ 2 ] < 0.01f, DescribeStanding( touching ) );
		Check( "pushback: a deposit that touches a standing vehicle does not shake it (drawn where it is saved)", touching[ 0 ] < 0.01f, DescribeStanding( touching ) );
		Check( "pushback: with a deposit in it, the vehicle is drawn where it is saved", overlapping[ 0 ] < 0.01f, DescribeStanding( overlapping ) );
		Check( "pushback: with a deposit in it, the vehicle comes to rest (still in the second half)", overlapping[ 5 ] < 0.01f, DescribeStanding( overlapping ) );
		Check( "pushback: with a deposit a unit into it, the drawn vehicle goes no farther than it takes to clear it, and a half more", overlapping[ 9 ] < 1.5f, DescribeStanding( overlapping ) );
		// how far the vehicle has to go back to clear the Lead deposit 2 ahead of its middle, and a unit more for the Gold one beside it and the frame timing
		var clearOfTwo = edge - 2.0f + 1.0f;

		Check( "pushback: with two deposits in it, the vehicle is drawn where it is saved", two[ 0 ] < 0.01f, DescribeStanding( two ) );
		Check( "pushback: with two deposits in it, the drawn vehicle goes no farther than it takes to clear them (" + clearOfTwo.ToString( "F2" ) + ")", two[ 9 ] < clearOfTwo, DescribeStanding( two ) );
		Check( "pushback: at 10 frames a second, the vehicle is drawn where it is saved", twoSlow[ 0 ] < 0.01f, DescribeStanding( twoSlow ) );
		Check( "pushback: at 10 frames a second, the drawn vehicle goes no farther than it takes to clear the deposits", twoSlow[ 9 ] < clearOfTwo, DescribeStanding( twoSlow ) );
		Check( "pushback: a long frame does not throw the drawn vehicle farther than it takes to clear the deposits", twoHitch[ 3 ] < clearOfTwo, "moved " + twoHitch[ 3 ].ToString( "F3" ) + ", saved place " + twoHitch[ 2 ].ToString( "F3" ) );
		Check( "pushback: driving into a deposit stops the vehicle at it (control: it does not drive through)", ( drivenTo > 5.0f ) && ( deepest < 2.0f ) && ( restAhead > 0.0f ), "went " + drivenTo.ToString( "F3" ) + ", at most " + deepest.ToString( "F3" ) + " into it, resting " + restAhead.ToString( "F3" ) + " ahead of it" );
		Check( "pushback: after driving into a deposit, the stopped vehicle is drawn where it is saved", driven[ 0 ] < 0.01f, DescribeStanding( driven ) );
		Check( "pushback: floating beside a deposit, the bobbing vehicle is drawn where it is saved", floating[ 0 ] < 0.01f, DescribeStanding( floating ) );
		Check( "pushback: floating beside a deposit, the bob drifts the vehicle less than half a unit in 6 s", floating[ 2 ] < 0.5f, DescribeStanding( floating ) );

		Finish( "scenario=pushback edge=" + edge.ToString( "F2" ) + " touching=" + touching[ 2 ].ToString( "F2" ) + "/" + touching[ 0 ].ToString( "F2" ) + " overlapping=" + overlapping[ 2 ].ToString( "F2" ) + "/" + overlapping[ 0 ].ToString( "F2" ) + " two=" + two[ 2 ].ToString( "F2" ) + "/" + two[ 3 ].ToString( "F2" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	IEnumerator ScenarioCargo()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var messages = SpaceflightController.m_instance.m_messages;
		var sensors = SpaceflightController.m_instance.m_displayController.m_sensorsDisplay;

		EnsureCrew();

		// ---- 1. the cargo list of the ship: a new game has 200 tenths of a cubic meter of endurium, which is 20.0 cubic meters
		var columns = Call( new ShipCargoButton(), "GetCargoDataTable" ) as string[];
		var volumeColumn = columns[ 1 ].Replace( '\n', '/' );

		Log( "cargo: the volume column of the ship's cargo list: " + volumeColumn + " (endurium in the hold: " + Endurium() + " tenths)" );

		Check( "cargo: the ship's cargo list shows cubic meters", volumeColumn.Contains( "/" + ( Endurium() / 10 ) + "." + ( Endurium() % 10 ) + "/" ), volumeColumn );

		// ---- 2. the size of a scanned vessel next to our ship: vessel 6 has a mass of 50, vessel 1 a mass of 800
		var shipMass = playerData.m_playerShip.m_mass;

		var hadSensorData = sensors.m_hasSensorData;
		var scanType = sensors.m_scanType;

		sensors.m_hasSensorData = true;

		// (a science officer of full skill, who gives every line of the analysis: roadmap 1.7)
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.ScienceOfficer ).m_science = 250;

		sensors.m_scanType = (SensorsDisplay.ScanType) 6;

		new AnalysisButton().Execute();

		var smallVessel = MessageList();

		sensors.m_scanType = (SensorsDisplay.ScanType) 1;

		new AnalysisButton().Execute();

		var largeVessel = MessageList();

		sensors.m_hasSensorData = hadSensorData;
		sensors.m_scanType = scanType;

		// (the original's size: the whole number, or "0." and the decimals to the first that is not zero - worked out here with decimal, apart from the button's code)
		var smallRatio = (decimal) gameData.m_vesselList[ 6 ].m_mass / shipMass;
		var largeRatio = (decimal) gameData.m_vesselList[ 1 ].m_mass / shipMass;
		var smallDecimals = 1;

		while ( ( smallRatio < 1.0m ) && ( decimal.Truncate( smallRatio * (decimal) Math.Pow( 10, smallDecimals ) ) == 0 ) && ( smallDecimals < 10 ) )
		{
			smallDecimals++;
		}

		var expectedSmall = ( smallRatio >= 1.0m ) ? decimal.Truncate( smallRatio ).ToString( System.Globalization.CultureInfo.InvariantCulture ) : ( decimal.Truncate( smallRatio * (decimal) Math.Pow( 10, smallDecimals ) ) / (decimal) Math.Pow( 10, smallDecimals ) ).ToString( "0." + new string( '0', smallDecimals ), System.Globalization.CultureInfo.InvariantCulture );
		var expectedLarge = decimal.Truncate( largeRatio ).ToString( System.Globalization.CultureInfo.InvariantCulture );

		Log( "cargo: our ship has a mass of " + shipMass + "; analysis of a vessel of mass " + gameData.m_vesselList[ 6 ].m_mass + ": " + smallVessel );
		Log( "cargo: analysis of a vessel of mass " + gameData.m_vesselList[ 1 ].m_mass + ": " + largeVessel );

		Check( "cargo: the analysis gives the size of a vessel as the original does", smallVessel.Contains( ">" + expectedSmall + " times the size of our ship." ) && largeVessel.Contains( ">" + expectedLarge + " times the size of our ship." ), "expected " + expectedSmall + " and " + expectedLarge + " | " + smallVessel + " | " + largeVessel );

		// ---- 3. down to planet 90 and into the terrain vehicle
		yield return EnterOrbit( 90 );

		SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		if ( playerData.m_general.m_location != PD_General.Location.Disembarked )
		{
			Finish( "scenario=cargo abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;
		var container = SpaceflightController.m_instance.m_disembarked.m_terrainGrid.m_terrainElements.transform;
		var deposits = container.GetComponentsInChildren<TerrainElement>( true );

		if ( deposits.Length < 2 )
		{
			Finish( "scenario=cargo abort: the planet has fewer than two deposits", 2 );
			yield break;
		}

		// where the terrain vehicle stands: nothing in the scenario moves it
		var vehicleStart = playerData.m_general.m_coordinates;

		// everything out of reach, then one deposit of 3 tenths next to the vehicle
		foreach ( var other in deposits )
		{
			if ( Vector3.Distance( other.transform.position, terrainVehicle.transform.position ) < 50.0f )
			{
				other.transform.position += Vector3.right * 1000.0f;
			}
		}

		var first = deposits[ 0 ];

		first.m_volume = 3;
		first.transform.position = BesideTheVehicle( terrainVehicle );

		new TVCargoButton().Execute();

		var pickupMessage = MessageList();
		var cargoAfterFirst = TerrainVehicleCargo( first.m_elementId );

		// the transporter effect of the first deposit takes 1.5 seconds
		yield return new WaitForSecondsRealtime( 2.5f );

		messages.Clear();

		Call( new TVCargoButton(), "ShowCargoContents" );

		var contents = MessageList();

		Log( "cargo: picking up a deposit of 3 tenths of a cubic meter: " + pickupMessage + " (in the hold now: " + cargoAfterFirst + " tenths)" );
		Log( "cargo: the cargo list of the terrain vehicle: " + contents );

		Check( "cargo: the pickup message gives the volume in cubic meters", ( cargoAfterFirst == 3 ) && pickupMessage.Contains( "Picked up 0.3 cubic meters" ), "in the hold " + cargoAfterFirst + " tenths | " + pickupMessage );
		Check( "cargo: the terrain vehicle's cargo list shows cubic meters", contents.Contains( ": 0.3 m" ) && contents.Contains( "Capacity: 0.3/" + ( gameData.m_misc.m_terrainVehicleVolume / 10 ) + "." + ( gameData.m_misc.m_terrainVehicleVolume % 10 ) + " m" ), contents );

		// ---- 4. a deposit that does not fit: fill the hold up to its last tenth of a cubic meter, then try a deposit of 4 tenths
		var second = deposits[ 1 ];

		playerData.m_terrainVehicle.AddElement( second.m_elementId, playerData.m_terrainVehicle.GetRemainingVolume() - 1 );

		second.m_volume = 4;
		second.transform.position = BesideTheVehicle( terrainVehicle );

		var cargoBefore = TerrainVehicleCargo( second.m_elementId );

		new TVCargoButton().Execute();

		var partialMessage = MessageList();
		var cargoAfterPartial = TerrainVehicleCargo( second.m_elementId );

		yield return new WaitForSecondsRealtime( 2.5f );

		var secondIsStillThere = ( second != null );
		var secondVolume = secondIsStillThere ? second.m_volume : -1;

		// and once more with the hold full
		new TVCargoButton().Execute();

		var fullMessage = MessageList();
		var cargoAfterFull = TerrainVehicleCargo( second.m_elementId );

		Log( "cargo: a deposit of 4 tenths with room for 1: took " + ( cargoAfterPartial - cargoBefore ) + ", free afterwards " + playerData.m_terrainVehicle.GetRemainingVolume() + ", deposit still there " + secondIsStillThere + " with " + secondVolume + " tenths | " + partialMessage );
		Log( "cargo: the same deposit with a full hold: took " + ( cargoAfterFull - cargoAfterPartial ) + " | " + fullMessage );

		Check( "cargo: a deposit that does not fit is left with what the hold could not take", ( cargoAfterPartial - cargoBefore == 1 ) && secondIsStillThere && ( secondVolume == 3 ) && partialMessage.Contains( "0.3 cubic meters are left" ), "took " + ( cargoAfterPartial - cargoBefore ) + ", still there " + secondIsStillThere + " with " + secondVolume + " tenths | " + partialMessage );
		Check( "cargo: nothing is taken from it with a full hold", ( cargoAfterFull == cargoAfterPartial ) && fullMessage.Contains( "full" ), "took " + ( cargoAfterFull - cargoAfterPartial ) + " | " + fullMessage );

		// the checks above need the deposits in reach, and the vehicle stands still: if it has moved, something pushed it (the deposits it touches did, see BesideTheVehicle)
		var vehicleMoved = Vector2.Distance( new Vector2( vehicleStart.x, vehicleStart.z ), new Vector2( playerData.m_general.m_coordinates.x, playerData.m_general.m_coordinates.z ) );

		Check( "cargo: the terrain vehicle stands where it was put down", vehicleMoved < 0.01f, "moved " + vehicleMoved.ToString( "F3" ) + " from " + vehicleStart.ToString( "F2" ) + " to " + playerData.m_general.m_coordinates.ToString( "F2" ) );

		Finish( "scenario=cargo shipList=[" + volumeColumn + "] size=" + expectedSmall + "/" + expectedLarge + " pickup=" + cargoAfterFirst + " partial=" + ( cargoAfterPartial - cargoBefore ) + "/" + secondIsStillThere + "/" + secondVolume + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: a game saved in the terrain vehicle, the terrain vehicle of a new game, the stardate on a computer with another calendar

	// the stardate a scratch copy of the general player data works out while the computer is set to the given culture ("threw ..." if it cannot)
	static string StardateIn( string cultureName )
	{
		var before = System.Globalization.CultureInfo.CurrentCulture;

		try
		{
			System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo( cultureName );

			var general = new PD_General();

			general.Reset();

			// ten days and five hours into the game
			general.m_day = 10;
			general.m_hour = 5;
			general.m_lastHour = 5;

			general.UpdateGameTime( 0.0f );

			return general.m_currentStardateYMD + " / " + general.m_currentStardateDHMY;
		}
		catch ( Exception exception )
		{
			return "threw " + exception.GetType().Name;
		}
		finally
		{
			System.Globalization.CultureInfo.CurrentCulture = before;
		}
	}

	IEnumerator ScenarioSaveData()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;

		EnsureCrew();

		yield return Frames( 2 );

		// ---- 1. the scene a saved game is loaded into: every location but starport is in the spaceflight scene
		var locationBefore = playerData.m_general.m_location;
		var wrongScenes = "";
		var disembarkedScene = "";

		foreach ( PD_General.Location location in Enum.GetValues( typeof( PD_General.Location ) ) )
		{
			playerData.m_general.m_location = location;

			var scene = dataController.GetCurrentSceneName();
			var expected = ( location == PD_General.Location.Starport ) ? "Starport" : "Spaceflight";

			if ( location == PD_General.Location.Disembarked )
			{
				disembarkedScene = scene;
			}

			if ( scene != expected )
			{
				wrongScenes += location + "->" + scene + " ";
			}
		}

		playerData.m_general.m_location = locationBefore;

		Log( "save and data: a game saved in the terrain vehicle is loaded into the " + disembarkedScene + " scene; locations with the wrong scene: " + ( ( wrongScenes == "" ) ? "none" : wrongScenes.Trim() ) );

		Check( "save and data: a game saved in the terrain vehicle loads into the spaceflight scene", ( disembarkedScene == "Spaceflight" ) && ( wrongScenes == "" ), "terrain vehicle -> " + disembarkedScene + ", wrong: " + ( ( wrongScenes == "" ) ? "none" : wrongScenes.Trim() ) );

		// ---- 2. the description of such a game in the save game panel (slot 1 is not the active slot, and the saves are in memory)
		var saveGamePanel = PanelController.m_instance.m_saveGamePanel;
		var otherSlot = dataController.m_playerDataList[ 1 ];
		var otherLocationBefore = otherSlot.m_general.m_location;
		var otherStardateBefore = otherSlot.m_general.m_currentStardateYMD;

		// a stardate whose day and month differ, so the order of the parts shows
		otherSlot.m_general.m_location = PD_General.Location.Disembarked;
		otherSlot.m_general.m_currentStardateYMD = "4620-03-15";

		Call( saveGamePanel, "UpdateDescriptions" );

		var descriptionTexts = GetField( saveGamePanel, "m_slotDescriptionText" ) as TMPro.TextMeshProUGUI[];
		var description = descriptionTexts[ 1 ].text;

		otherSlot.m_general.m_location = otherLocationBefore;
		otherSlot.m_general.m_currentStardateYMD = otherStardateBefore;

		Call( saveGamePanel, "UpdateDescriptions" );

		var opened = description.Split( new string[] { "<color=" }, StringSplitOptions.None ).Length - 1;
		var closed = description.Split( new string[] { "</color>" }, StringSplitOptions.None ).Length - 1;
		var locationLine = description.Split( '\n' )[ 1 ];

		Log( "save and data: the save panel's line for a game in the terrain vehicle: " + locationLine + " (" + opened + " colour tags opened, " + closed + " closed)" );

		Check( "save and data: the save panel names the location of a game in the terrain vehicle", ( opened == closed ) && locationLine.Contains( "Terrain Vehicle</color>" ), locationLine + " | opened " + opened + ", closed " + closed );

		// the date line shows the stardate as the game shows it, day-month-year, not in the stored year-month-day form
		// (the colour tag around it is the UI kit's, so any colour is accepted)
		var dateLine = description.Split( '\n' )[ 0 ];
		var dateShown = System.Text.RegularExpressions.Regex.IsMatch( dateLine, "^Date: <color=[^>]+>15-03-4620</color>" );

		Log( "save and data: the save panel's date line for a game saved on 4620-03-15: " + dateLine );

		Check( "save and data: the save panel shows the stardate day-month-year", dateShown, dateLine );

		// ---- 3. the terrain vehicle of a new game
		var fresh = new PlayerData();

		fresh.Reset();

		var freshVehicle = fresh.m_terrainVehicle;
		var hasStorage = ( freshVehicle.m_elementStorage != null ) && ( freshVehicle.m_artifactStorage != null );

		Log( "save and data: the terrain vehicle of a new game: fuel " + freshVehicle.m_fuelRemaining.ToString( "F1" ) + ", has its cargo holds " + hasStorage );

		Check( "save and data: a new game has a fuelled terrain vehicle with its cargo holds", ( freshVehicle.m_fuelRemaining == 1.0f ) && hasStorage, "fuel " + freshVehicle.m_fuelRemaining.ToString( "F1" ) + ", cargo holds " + hasStorage );

		// ---- 4. the stardate on computers with other calendars: thai (buddhist years), saudi arabian (a calendar that ends long before 4620), persian
		var invariant = StardateIn( "" );
		var thai = StardateIn( "th-TH" );
		var saudi = StardateIn( "ar-SA" );
		var persian = StardateIn( "fa-IR" );
		var german = StardateIn( "de-DE" );

		Log( "save and data: stardate after 10 days and 5 hours: invariant " + invariant + " | th-TH " + thai + " | ar-SA " + saudi + " | fa-IR " + persian + " | de-DE " + german );

		const string c_expected = "4620-01-11 / 11.05-01-4620";

		Check( "save and data: the stardate is the same whatever calendar the computer uses", ( invariant == c_expected ) && ( thai == c_expected ) && ( saudi == c_expected ) && ( persian == c_expected ) && ( german == c_expected ), "invariant " + invariant + " | th-TH " + thai + " | ar-SA " + saudi + " | fa-IR " + persian + " | de-DE " + german );

		Finish( "scenario=savedata terrainVehicleScene=" + disembarkedScene + " panelTags=" + opened + "/" + closed + " newVehicleFuel=" + freshVehicle.m_fuelRemaining.ToString( "F1" ) + " stardate=[" + thai + " | " + saudi + " | " + persian + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: combat (a paused missile, the size of a pooled explosion, a launch with no missile free, a ship destroyed twice)

	static int s_gameOverCalls;

	static void CountGameOverCalls( string condition, string stackTrace, LogType type )
	{
		if ( condition.StartsWith( "ShowGameOver called" ) )
		{
			s_gameOverCalls++;
		}
	}

	IEnumerator ScenarioCombat()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// a ship with both weapons that survives whatever hits it
		ship.m_laserCannonClass = 1;
		ship.m_missileLauncherClass = 1;
		ship.m_armorPoints = 100000;
		ship.m_shieldsAreUp = false;

		if ( Endurium() < 50 )
		{
			ship.AddElement( 5, 50 );
		}

		var missiles = GetField( combat, "m_missilePool" ) as List<MissileProjectile>;

		// ---- 1. a missile in the air while the game is paused (the save panel, the starmap and the ship's log all pause it with this flag):
		//         it is on its way to a point 3000 units off, at 500 units a second
		var missile = missiles[ 0 ];
		var launchPoint = playerData.m_general.m_coordinates + Vector3.up * 500.0f;
		var arrived = false;

		missile.Fire( launchPoint, launchPoint + Vector3.forward * 3000.0f, ( position, didHit ) => { arrived = true; } );

		yield return new WaitForSecondsRealtime( 0.5f );

		var travelledBeforePause = Vector3.Distance( missile.transform.position, launchPoint );

		controller.m_gameIsPaused = true;

		var positionAtPause = missile.transform.position;

		yield return new WaitForSecondsRealtime( 1.0f );

		var travelledWhilePaused = Vector3.Distance( missile.transform.position, positionAtPause );
		var positionAfterPause = missile.transform.position;

		controller.m_gameIsPaused = false;

		yield return new WaitForSecondsRealtime( 0.5f );

		var travelledAfterPause = Vector3.Distance( missile.transform.position, positionAfterPause );
		var inTheAirAfterPause = missile.IsActive();

		missile.Cancel();

		Log( "combat: a missile flew " + travelledBeforePause.ToString( "F0" ) + " units in half a second, " + travelledWhilePaused.ToString( "F0" ) + " in one second of pause, " + travelledAfterPause.ToString( "F0" ) + " in half a second after it (still in the air " + inTheAirAfterPause + ", arrived " + arrived + ")" );

		Check( "combat: a missile stands still while the game is paused and flies on afterwards", ( travelledBeforePause > 100.0f ) && ( travelledWhilePaused < 1.0f ) && ( travelledAfterPause > 100.0f ) && inTheAirAfterPause, "before " + travelledBeforePause.ToString( "F0" ) + ", paused " + travelledWhilePaused.ToString( "F0" ) + ", after " + travelledAfterPause.ToString( "F0" ) );

		// ---- 2. a pooled explosion that a missile hit played at half size is at full size for whoever gets it next
		var small = Call( combat, "GetAvailableExplosion" ) as ExplosionEffect;

		small.SetScale( 0.5f );
		small.Play( launchPoint );

		var waited = 0.0f;

		while ( small.IsPlaying() && ( waited < 10.0f ) )
		{
			waited += Time.unscaledDeltaTime;

			yield return null;
		}

		var next = Call( combat, "GetAvailableExplosion" ) as ExplosionEffect;
		var sameObject = ReferenceEquals( next, small );
		var nextScale = next.transform.localScale.x;

		Log( "combat: the explosion played at half size took " + waited.ToString( "F1" ) + " s; the next one handed out is " + ( sameObject ? "the same object" : "another object" ) + " at scale " + nextScale.ToString( "F2" ) );

		Check( "combat: an explosion is handed out at full size", sameObject && Mathf.Approximately( nextScale, 1.0f ), ( sameObject ? "same object" : "another object" ) + ", scale " + nextScale.ToString( "F2" ) );

		// ---- 3. a launch when every missile of the pool is in the air: nothing is launched, so no fuel is used and the aliens stay as they are
		var speminId = FindEncounter( 1, 6, 3, 0 );

		// all spemin scouts: lasers only, so the only missiles in the air are the ones this scenario puts there
		ForceVessel( speminId, 2 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		BringAliensClose();

		var pdEncounter = controller.m_encounter.m_pdEncounter;
		var from = playerData.m_general.m_coordinates;

		// every missile of the pool goes off to a far point (they are in the air for five seconds)
		foreach ( var pooled in missiles )
		{
			pooled.Fire( from, from + Vector3.up * 100000.0f, ( position, didHit ) => { } );
		}

		var inTheAirBeforeLaunch = MissilesInFlight();

		combat.SetTarget( FirstLivingAlien() );

		SetField( combat, "m_playerMissileCooldown", 0.0f );

		var fuelUsedBefore = ship.m_fuelUsed;
		var enduriumBefore = Endurium();
		var attackedBefore = pdEncounter.m_attackedByPlayer;

		controller.m_messages.Clear();

		var launchedWithNoMissileFree = combat.FirePlayerMissile();

		var fuelUsed = ( ship.m_fuelUsed - fuelUsedBefore ) + ( enduriumBefore - Endurium() ) * 0.1f;
		var attacked = pdEncounter.m_attackedByPlayer;
		var messageWithNoMissileFree = MessageList();

		// with missiles free again the same launch works
		foreach ( var pooled in missiles )
		{
			pooled.Cancel();
		}

		SetField( combat, "m_playerMissileCooldown", 0.0f );

		BringAliensClose();

		combat.SetTarget( FirstLivingAlien() );

		var fuelUsedBeforeSecond = ship.m_fuelUsed;
		var enduriumBeforeSecond = Endurium();

		var launchedWithMissilesFree = combat.FirePlayerMissile();

		var fuelUsedBySecond = ( ship.m_fuelUsed - fuelUsedBeforeSecond ) + ( enduriumBeforeSecond - Endurium() ) * 0.1f;
		var inTheAirAfterLaunch = MissilesInFlight();

		Log( "combat: launch with " + inTheAirBeforeLaunch + " of " + missiles.Count + " missiles in the air: launched " + launchedWithNoMissileFree + ", fuel used " + fuelUsed.ToString( "F3" ) + ", aliens attacked " + attackedBefore + " -> " + attacked + ", message: " + messageWithNoMissileFree + " | with missiles free: launched " + launchedWithMissilesFree + ", fuel used " + fuelUsedBySecond.ToString( "F3" ) + ", in the air " + inTheAirAfterLaunch + ", aliens attacked " + pdEncounter.m_attackedByPlayer );

		Check( "combat: a launch with no missile free does nothing and costs nothing", ( inTheAirBeforeLaunch == missiles.Count ) && !attackedBefore && !launchedWithNoMissileFree && ( Mathf.Abs( fuelUsed ) < 0.0001f ) && !attacked && !messageWithNoMissileFree.Contains( "Missile launched" ), "launched " + launchedWithNoMissileFree + ", fuel used " + fuelUsed.ToString( "F3" ) + ", aliens attacked " + attacked + ", message: " + messageWithNoMissileFree );
		Check( "combat: a launch with a missile free still works", launchedWithMissilesFree && ( Mathf.Abs( fuelUsedBySecond - 0.02f ) < 0.0001f ) && ( inTheAirAfterLaunch == 1 ) && pdEncounter.m_attackedByPlayer, "launched " + launchedWithMissilesFree + ", fuel used " + fuelUsedBySecond.ToString( "F3" ) + ", in the air " + inTheAirAfterLaunch + ", aliens attacked " + pdEncounter.m_attackedByPlayer );

		// ---- 4. the ship is destroyed, and hit again while it explodes: it is only destroyed once, and nothing fires at it or from it afterwards
		foreach ( var pooled in missiles )
		{
			pooled.Cancel();
		}

		var target = FirstLivingAlien();
		var alienShip = EncounterShip( target );
		var alienVessel = gameData.m_vesselList[ alienShip.m_vesselId ];

		BringAliensClose();

		combat.SetTarget( target );

		SetField( combat, "m_playerLaserCooldown", 0.0f );
		SetField( combat, "m_playerMissileCooldown", 0.0f );

		var couldFireBefore = combat.CanFireLaser() && combat.CanFireMissile();

		// one missile is on its way when the ship goes
		missiles[ 0 ].Fire( from, from + Vector3.up * 100000.0f, ( position, didHit ) => { } );

		ship.m_armorPoints = 100;

		s_gameOverCalls = 0;

		Application.logMessageReceived += CountGameOverCalls;

		var wrecksBefore = FindObjectsByType<DebrisTumble>( FindObjectsInactive.Include, FindObjectsSortMode.None ).Length;

		// the hit that destroys the ship
		combat.ApplyDamageToPlayer( 5000, Vector3.forward );

		var inTheAirAfterDestruction = MissilesInFlight();
		var canFireLaserAfter = combat.CanFireLaser();
		var canFireMissileAfter = combat.CanFireMissile();

		// an alien fires at the wreck (its laser used to destroy the ship a second time)
		controller.m_messages.Clear();

		combat.AlienFiresAtPlayer( alienShip, alienVessel );

		var alienFireMessages = MessageList();

		// and one more hit that does not come from an alien weapon (a missile that was already on its way, a flare)
		combat.ApplyDamageToPlayer( 5000, Vector3.forward );

		// the explosion of the ship is what calls the game over screen
		yield return new WaitForSecondsRealtime( 4.0f );

		Application.logMessageReceived -= CountGameOverCalls;

		var wrecks = FindObjectsByType<DebrisTumble>( FindObjectsInactive.Include, FindObjectsSortMode.None ).Length - wrecksBefore;

		Log( "combat: the ship was destroyed, fired at by a " + alienVessel.m_name + " (laser class " + alienVessel.m_laserClass + ") and hit for 5000 again: wrecks spawned " + wrecks + ", game over called " + s_gameOverCalls + " times, armor " + ship.m_armorPoints + ", game over flag " + controller.m_gameOver + " | missiles in the air right after the destruction " + inTheAirAfterDestruction + " | could fire before " + couldFireBefore + ", laser after " + canFireLaserAfter + ", missile after " + canFireMissileAfter + " | messages from the alien's shot: [" + alienFireMessages + "]" );

		Check( "combat: a destroyed ship is only destroyed once", ( wrecks == 1 ) && ( s_gameOverCalls == 1 ) && ( ship.m_armorPoints == 0 ) && controller.m_gameOver, "wrecks " + wrecks + ", game over called " + s_gameOverCalls + " times, armor " + ship.m_armorPoints );
		Check( "combat: no missile stays in the air once the ship is destroyed", inTheAirAfterDestruction == 0, inTheAirAfterDestruction + " in the air right after the ship was destroyed" );
		Check( "combat: a destroyed ship does not fire and is not fired at", couldFireBefore && !canFireLaserAfter && !canFireMissileAfter && ( alienFireMessages.Length == 0 ), "could fire before " + couldFireBefore + ", laser after " + canFireLaserAfter + ", missile after " + canFireMissileAfter + ", messages from the alien's shot: [" + alienFireMessages + "]" );

		Finish( "scenario=combat paused=" + travelledWhilePaused.ToString( "F0" ) + " explosionScale=" + nextScale.ToString( "F2" ) + " noMissileFree=" + launchedWithNoMissileFree + "/" + fuelUsed.ToString( "F3" ) + "/" + attacked + " destroyed=" + wrecks + "/" + s_gameOverCalls + "/" + inTheAirAfterDestruction + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: how encounters start (a ship that has just launched, the direction the aliens come from, two encounters in one frame, the radar dead astern)

	// how many ships of an encounter have been added to it
	static int ShipsAdded( PD_Encounter pdEncounter )
	{
		var count = 0;

		if ( pdEncounter.GetAlienShipList() != null )
		{
			foreach ( var alienShip in pdEncounter.GetAlienShipList() )
			{
				count += alienShip.m_addedToEncounter ? 1 : 0;
			}
		}

		return count;
	}

	// take every ship of an encounter out of it again
	static void ForgetShipsAdded( PD_Encounter pdEncounter )
	{
		if ( pdEncounter.GetAlienShipList() != null )
		{
			foreach ( var alienShip in pdEncounter.GetAlienShipList() )
			{
				alienShip.m_addedToEncounter = false;
			}
		}
	}

	// how often a text contains another
	static int CountOf( string text, string part )
	{
		var count = 0;

		for ( var index = text.IndexOf( part, StringComparison.Ordinal ); index >= 0; index = text.IndexOf( part, index + part.Length, StringComparison.Ordinal ) )
		{
			count++;
		}

		return count;
	}

	// true if the radar has a blip for this encounter
	static bool RadarShows( PD_Encounter pdEncounter )
	{
		var detections = GetField( SpaceflightController.m_instance.m_radar, "m_detectionList" ) as Array;

		if ( detections != null )
		{
			foreach ( var detection in detections )
			{
				if ( ReferenceEquals( GetField( detection, "m_encounter" ), pdEncounter ) )
				{
					return true;
				}
			}
		}

		return false;
	}

	// two encounters come within range of the player in the same frame: which one begins, and what happens to the other
	IEnumerator TwoEncountersInOneFrame( PD_Encounter near, PD_Encounter far, string[] result )
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var nearStar = near.m_starId;
		var farStar = far.m_starId;

		ForgetShipsAdded( near );
		ForgetShipsAdded( far );

		controller.m_messages.Clear();

		// both are in the player's star system, 60 and 90 away (the encounter range is 128)
		near.m_starId = far.m_starId = playerData.m_general.m_currentStarId;

		near.SetCoordinates( playerData.m_general.m_coordinates + Vector3.right * 60.0f );
		far.SetCoordinates( playerData.m_general.m_coordinates + Vector3.back * 90.0f );

		yield return WaitForLocation( PD_General.Location.Encounter, 3.0f );

		var began = playerData.m_general.m_location == PD_General.Location.Encounter;
		var entered = playerData.m_general.m_currentEncounterId;
		var announcements = CountOf( MessageList(), "Scanners indicate unidentified object!" );
		var nearAdded = ShipsAdded( near );
		var farAdded = ShipsAdded( far );

		// back to the star system, with both encounters out of it again
		if ( began )
		{
			LeaveEncounter();
		}

		near.m_starId = nearStar;
		far.m_starId = farStar;

		near.SetCoordinates( near.m_homeCoordinates );
		far.SetCoordinates( far.m_homeCoordinates );

		yield return Frames( 5 );

		result[ 0 ] = "began " + began + ", entered " + entered + " (the nearer one is " + near.m_encounterId + ", the other " + far.m_encounterId + "), announced " + announcements + " times, ships added to the nearer one " + nearAdded + ", to the other " + farAdded;
		result[ 1 ] = ( began && ( entered == near.m_encounterId ) && ( announcements == 1 ) && ( nearAdded > 0 ) && ( farAdded == 0 ) ) ? "ok" : "wrong";
	}

	IEnumerator ScenarioEncounters()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		playerData.m_playerShip.m_armorPoints = 100000;

		// ---- 1. a ship that has just launched from starport: the hyperspace encounters have nothing to do with it
		var launchPoint = gameData.m_planetList[ gameData.m_misc.m_arthPlanetId ].GetPosition();

		launchPoint.y = 0.0f;
		launchPoint.z += 128.0f;

		// the same thing the end of the launch animation does
		playerData.m_general.m_currentStarId = gameData.m_misc.m_arthStarId;
		playerData.m_general.m_lastStarSystemCoordinates = launchPoint;

		controller.SwitchLocation( PD_General.Location.JustLaunched );

		yield return Frames( 5 );

		var before = new Dictionary<int, Vector3>();
		var seenFromTheLaunchPoint = 0;
		PD_Encounter nearestHyperspaceEncounter = null;

		foreach ( var pdEncounter in playerData.m_encounterList )
		{
			if ( pdEncounter.GetLocation() == PD_General.Location.Hyperspace )
			{
				before[ pdEncounter.m_encounterId ] = pdEncounter.m_currentCoordinates;

				var distance = Vector3.Distance( pdEncounter.m_currentCoordinates, playerData.m_general.m_coordinates );

				if ( distance < controller.m_alienStarSystemRadarDistance )
				{
					seenFromTheLaunchPoint++;
				}

				if ( ( nearestHyperspaceEncounter == null ) || ( distance < Vector3.Distance( nearestHyperspaceEncounter.m_currentCoordinates, playerData.m_general.m_coordinates ) ) )
				{
					nearestHyperspaceEncounter = pdEncounter;
				}
			}
		}

		var nearestDistance = Vector3.Distance( nearestHyperspaceEncounter.m_currentCoordinates, playerData.m_general.m_coordinates );

		yield return new WaitForSecondsRealtime( 2.0f );

		var moved = 0;
		var furthestMove = 0.0f;

		foreach ( var pdEncounter in playerData.m_encounterList )
		{
			if ( before.TryGetValue( pdEncounter.m_encounterId, out var coordinates ) )
			{
				var distance = Vector3.Distance( coordinates, pdEncounter.m_currentCoordinates );

				moved += ( distance > 0.01f ) ? 1 : 0;
				furthestMove = Mathf.Max( furthestMove, distance );
			}
		}

		var locationAfterWaiting = playerData.m_general.m_location;

		Log( "encounters: just launched at " + playerData.m_general.m_coordinates + " (game time " + playerData.m_general.m_gameTime.ToString( "F2" ) + "): " + seenFromTheLaunchPoint + " hyperspace encounters have coordinates within " + controller.m_alienStarSystemRadarDistance + " of that point, the nearest (" + nearestHyperspaceEncounter.m_encounterId + ") at " + nearestDistance.ToString( "F0" ) + ". In 2 s " + moved + " of them moved, by up to " + furthestMove.ToString( "F1" ) + " (location " + locationAfterWaiting + ")" );

		Check( "encounters: no hyperspace encounter moves while the ship has just launched", ( seenFromTheLaunchPoint > 0 ) && ( moved == 0 ) && ( locationAfterWaiting == PD_General.Location.JustLaunched ), seenFromTheLaunchPoint + " within the radar distance, " + moved + " moved, by up to " + furthestMove.ToString( "F1" ) );

		// where the nearest one is after it has come all the way (at 32 units a second that takes it the distance above divided by 32 in seconds)
		nearestHyperspaceEncounter.SetCoordinates( playerData.m_general.m_coordinates + Vector3.right * 100.0f );

		yield return WaitForLocation( PD_General.Location.Encounter, 1.0f );

		var locationWithOneNextToTheShip = playerData.m_general.m_location;

		if ( locationWithOneNextToTheShip == PD_General.Location.Encounter )
		{
			LeaveEncounter();
		}

		// put everything back where it belongs
		foreach ( var pdEncounter in playerData.m_encounterList )
		{
			if ( pdEncounter.GetLocation() == PD_General.Location.Hyperspace )
			{
				pdEncounter.SetCoordinates( pdEncounter.m_homeCoordinates );
			}
		}

		yield return Frames( 5 );

		Log( "encounters: with hyperspace encounter " + nearestHyperspaceEncounter.m_encounterId + " 100 away from the ship that has just launched the location became " + locationWithOneNextToTheShip );

		Check( "encounters: a hyperspace encounter does not begin above starport", locationWithOneNextToTheShip == PD_General.Location.JustLaunched, "location " + locationWithOneNextToTheShip );

		// ---- 2. the direction the aliens come from in a star system: the maneuver button takes the ship into the star system
		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );

		var first = playerData.FindEncounter( FindEncounter( 1, 6, 3, 0 ) );
		var second = playerData.FindEncounter( FindEncounter( 1, 6, 3, 1 ) );
		var firstStar = first.m_starId;

		// the old code took the direction from the ship's last place in hyperspace to the aliens' place in the star system, two unrelated spaces.
		// the aliens come from the opposite side here, so the two cannot be mistaken for one another
		var unrelatedDirection = Vector3.Normalize( playerData.m_general.m_coordinates - playerData.m_general.m_lastHyperspaceCoordinates );
		var approachDirection = -unrelatedDirection;

		ForgetShipsAdded( first );

		first.m_starId = playerData.m_general.m_currentStarId;

		first.SetCoordinates( playerData.m_general.m_coordinates + approachDirection * 100.0f );

		yield return WaitForLocation( PD_General.Location.Encounter, 3.0f );

		var encounterBegan = playerData.m_general.m_location == PD_General.Location.Encounter;
		var centroid = Vector3.zero;
		var shipsInTheEncounter = 0;

		foreach ( var alienShip in first.GetAlienShipList() )
		{
			if ( alienShip.m_addedToEncounter )
			{
				centroid += alienShip.m_coordinates;

				shipsInTheEncounter++;
			}
		}

		centroid /= Mathf.Max( 1, shipsInTheEncounter );

		var alignment = Vector3.Dot( Vector3.Normalize( centroid ), approachDirection );

		if ( encounterBegan )
		{
			LeaveEncounter();
		}

		first.m_starId = firstStar;

		first.SetCoordinates( first.m_homeCoordinates );

		yield return Frames( 5 );

		Log( "encounters: the aliens came from direction " + approachDirection + " in the star system: " + shipsInTheEncounter + " ships appeared around " + centroid + ", " + centroid.magnitude.ToString( "F0" ) + " away, alignment with the direction they came from " + alignment.ToString( "F2" ) + " (began " + encounterBegan + ")" );

		Check( "encounters: in a star system the aliens appear on the side they came from", encounterBegan && ( shipsInTheEncounter > 0 ) && ( alignment > 0.9f ), shipsInTheEncounter + " ships, alignment " + alignment.ToString( "F2" ) + " (1 is the side they came from, -1 the opposite side)" );

		// ---- 3. two encounters reach the ship in the same frame: one of them begins, the nearer one
		var firstNearer = new string[ 2 ];
		var secondNearer = new string[ 2 ];

		yield return TwoEncountersInOneFrame( first, second, firstNearer );
		yield return TwoEncountersInOneFrame( second, first, secondNearer );

		Log( "encounters: two in range in one frame, " + first.m_encounterId + " nearer: " + firstNearer[ 0 ] );
		Log( "encounters: two in range in one frame, " + second.m_encounterId + " nearer: " + secondNearer[ 0 ] );

		Check( "encounters: of two encounters that reach the ship in one frame only the nearer one begins", ( firstNearer[ 1 ] == "ok" ) && ( secondNearer[ 1 ] == "ok" ), firstNearer[ 0 ] + " | " + secondNearer[ 0 ] );

		// ---- 4. the radar: one encounter dead astern (180 degrees) and one abeam (90 degrees), for a little more than one sweep of six seconds
		var secondStar = second.m_starId;

		first.m_starId = second.m_starId = playerData.m_general.m_currentStarId;

		var shipAtStart = playerData.m_general.m_coordinates;

		first.SetCoordinates( shipAtStart + Vector3.back * 3000.0f );
		second.SetCoordinates( shipAtStart + Vector3.right * 3000.0f );

		var asternSeen = false;
		var abeamSeen = false;
		var end = Time.realtimeSinceStartup + 7.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			asternSeen |= RadarShows( first );
			abeamSeen |= RadarShows( second );

			yield return null;
		}

		var asternAngle = Vector3.SignedAngle( Vector3.forward, first.m_currentCoordinates - playerData.m_general.m_coordinates, Vector3.up );
		var abeamAngle = Vector3.SignedAngle( Vector3.forward, second.m_currentCoordinates - playerData.m_general.m_coordinates, Vector3.up );
		var shipMoved = Vector3.Distance( shipAtStart, playerData.m_general.m_coordinates );

		Log( "encounters: radar in 7 s: astern (angle " + asternAngle.ToString( "F3" ) + ", now " + first.GetDistance().ToString( "F0" ) + " away) seen " + asternSeen + ", abeam (angle " + abeamAngle.ToString( "F3" ) + ", now " + second.GetDistance().ToString( "F0" ) + " away) seen " + abeamSeen + ", the ship moved " + shipMoved.ToString( "F1" ) + ", location " + playerData.m_general.m_location );

		Check( "encounters: the radar sees an encounter that is dead astern", asternSeen && abeamSeen && ( Mathf.Abs( asternAngle ) > 179.99f ), "astern seen " + asternSeen + " (angle " + asternAngle.ToString( "F3" ) + "), abeam seen " + abeamSeen );

		first.m_starId = firstStar;
		second.m_starId = secondStar;

		Finish( "scenario=encounters justLaunched=" + moved + "/" + locationWithOneNextToTheShip + " approach=" + alignment.ToString( "F2" ) + " twoInOneFrame=" + firstNearer[ 1 ] + "/" + secondNearer[ 1 ] + " radarAstern=" + asternSeen + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: comms and the console (the captain's name in alien messages, mechan 9 after firing on the mechans, a press that lands on other buttons)

	// the first encounter with ships of the given race (-1 if there is none)
	static int FindEncounterOfRace( GameData.Race race )
	{
		var gameData = DataController.m_instance.m_gameData;

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			if ( gameData.m_encounterList[ i ].m_race == race )
			{
				return i;
			}
		}

		return -1;
	}

	// make the aliens of the current encounter speak about a subject the given number of times, and collect what the message box shows each time
	static List<string> AlienComms( GD_Comm.Subject subject, int count )
	{
		var lines = new List<string>();

		for ( var i = 0; i < count; i++ )
		{
			SpaceflightController.m_instance.m_messages.Clear();

			try
			{
				SpaceflightController.m_instance.m_encounter.AddComm( subject, false );

				lines.Add( MessageList() );
			}
			catch ( Exception exception )
			{
				lines.Add( "threw " + exception.GetType().Name );
			}
		}

		return lines;
	}

	// the mechans ask a question and the player gives the right answer, the given number of times
	IEnumerator AnswerMechanQuestions( int questionId, int count )
	{
		var pdEncounter = SpaceflightController.m_instance.m_encounter.m_pdEncounter;

		for ( var i = 0; i < count; i++ )
		{
			pdEncounter.m_lastQuestionFromAliens = questionId;
			pdEncounter.m_lastSubjectFromPlayer = GD_Comm.Subject.Yes;

			yield return Frames( 3 );
		}
	}

	IEnumerator ScenarioComms()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var buttonController = controller.m_buttonController;

		EnsureCrew();

		playerData.m_playerShip.m_armorPoints = 100000;
		playerData.m_playerShip.m_shieldsAreUp = false;

		// a communications officer who understands every word, so that nothing in the messages is garbled
		var captain = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Captain );
		var commOfficer = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer );

		commOfficer.m_communications = 250;

		// ---- 1. the spemin use the captain's name in one of their questions and in one of their answers about other races
		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var questions = AlienComms( GD_Comm.Subject.Question, 40 );
		var answers = AlienComms( GD_Comm.Subject.OtherRaces, 40 );

		// the same question with a captain whose name has two spaces in a row (names are used as they were typed): the message is
		// split into words at the spaces, which leaves an empty word, and the code looks at the last character of every word
		var nameBefore = captain.m_name;

		captain.m_name = "Jean  Luc";

		var withTwoSpaces = AlienComms( GD_Comm.Subject.Question, 40 );

		captain.m_name = nameBefore;

		// and with a communications officer who understands nothing: every word is garbled (the question with the captain's name in it is comm 203)
		commOfficer.m_communications = 0;

		var garbledQuestion = "(not shown)";

		for ( var i = 0; ( i < 40 ) && ( garbledQuestion == "(not shown)" ); i++ )
		{
			var lines = AlienComms( GD_Comm.Subject.Question, 1 );

			if ( controller.m_encounter.m_pdEncounter.m_lastQuestionFromAliens == 203 )
			{
				garbledQuestion = lines[ 0 ];
			}
		}

		commOfficer.m_communications = 250;

		controller.m_encounter.m_pdEncounter.m_lastQuestionFromAliens = 0;

		buttonController.SetBridgeButtons();

		var question = questions.Find( line => line.Contains( "self defense captain" ) ) ?? "(not shown)";
		var answer = answers.Find( line => line.Contains( "pretending to worship you" ) ) ?? "(not shown)";
		var withToken = 0;

		foreach ( var line in questions )
		{
			withToken += line.Contains( "*" ) ? 1 : 0;
		}

		foreach ( var line in answers )
		{
			withToken += line.Contains( "*" ) ? 1 : 0;
		}

		Log( "comms: captain \"" + captain.m_name + "\". The spemin question: " + question );
		Log( "comms: the spemin answer about other races: " + answer );

		Check( "comms: alien messages name the captain", question.Contains( "self defense captain " + captain.m_name + "," ) && answer.Contains( "worship you, " + captain.m_name + "." ) && ( withToken == 0 ), withToken + " of " + ( questions.Count + answers.Count ) + " messages still have the * in them. " + question );

		var threw = withTwoSpaces.FindAll( line => line.StartsWith( "threw" ) ).Count;
		var questionWithTwoSpaces = withTwoSpaces.Find( line => line.Contains( "self defense captain" ) ) ?? "(not shown)";

		Log( "comms: with the captain called \"Jean  Luc\" (two spaces) " + threw + " of " + withTwoSpaces.Count + " alien messages threw. The question: " + questionWithTwoSpaces );

		Check( "comms: a captain's name with two spaces in it does not break an alien message", ( threw == 0 ) && questionWithTwoSpaces.Contains( "self defense captain Jean Luc," ), threw + " of " + withTwoSpaces.Count + " threw. " + questionWithTwoSpaces );

		var separators = new char[] { ' ', '/' };
		var plainWords = System.Text.RegularExpressions.Regex.Replace( question, "<[^>]+>", " " ).Split( separators, StringSplitOptions.RemoveEmptyEntries ).Length;
		var garbledWords = System.Text.RegularExpressions.Regex.Replace( garbledQuestion, "<[^>]+>", " " ).Split( separators, StringSplitOptions.RemoveEmptyEntries ).Length;

		Log( "comms: the same question with nothing understood (" + garbledWords + " words, " + plainWords + " when understood): " + garbledQuestion );

		Check( "comms: a garbled alien message has as many words as the message", ( garbledWords == plainWords ) && ( plainWords > 20 ) && !garbledQuestion.Contains( "captain" ) && !garbledQuestion.Contains( captain.m_name ), garbledWords + " words, " + plainWords + " when understood. " + garbledQuestion );

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. the mechans: five right answers unlock mechan 9, but not once the player has fired on them
		var mechanId = FindEncounterOfRace( GameData.Race.Mechan );
		var questionId = 0;

		// a mechan question that is answered with yes (101, 102, 103 and 105 are answered with no)
		foreach ( var comm in gameData.m_commList )
		{
			if ( ( comm.m_race == GameData.Race.Mechan ) && ( comm.m_subject == GD_Comm.Subject.Question ) && ( comm.m_id != 101 ) && ( comm.m_id != 102 ) && ( comm.m_id != 103 ) && ( comm.m_id != 105 ) )
			{
				questionId = comm.m_id;
				break;
			}
		}

		playerData.m_general.m_mechan9Unlocked = false;

		EnterEncounter( mechanId );
		yield return Frames( 10 );

		var stanceAtStart = Stance();

		yield return AnswerMechanQuestions( questionId, 5 );

		var unlockedInPeace = playerData.m_general.m_mechan9Unlocked;
		var stanceInPeace = Stance();

		// the same again, in a new visit, after firing on them
		playerData.m_general.m_mechan9Unlocked = false;

		LeaveEncounter();
		yield return Frames( 10 );

		EnterEncounter( mechanId );
		yield return Frames( 10 );

		controller.m_encounter.PlayerAttacked();

		yield return AnswerMechanQuestions( questionId, 5 );

		var unlockedAfterFiring = playerData.m_general.m_mechan9Unlocked;
		var stanceAfterFiring = Stance();
		var answersCounted = controller.m_encounter.m_pdEncounter.m_numCorrectAnswers;

		playerData.m_general.m_mechan9Unlocked = false;

		Log( "comms: mechan encounter " + mechanId + ", question " + questionId + ", stance at the start " + stanceAtStart + ". Five right answers: unlocked " + unlockedInPeace + ", stance " + stanceInPeace + ". Five right answers after firing on them: unlocked " + unlockedAfterFiring + ", stance " + stanceAfterFiring + ", answers counted " + answersCounted );

		Check( "comms: five right answers unlock mechan 9", ( questionId != 0 ) && ( stanceAtStart == "Neutral" ) && unlockedInPeace && ( stanceInPeace == "Friendly" ), "stance at the start " + stanceAtStart + ", unlocked " + unlockedInPeace + ", stance " + stanceInPeace );
		Check( "comms: mechans that have been fired on do not unlock mechan 9", !unlockedAfterFiring && ( stanceAfterFiring == "Hostile" ), "unlocked " + unlockedAfterFiring + ", stance " + stanceAfterFiring );

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. the fire button is pressed on Statement, and before the press is carried out (0.35 s) the aliens ask a question, which puts Yes and No on the console
		EnterEncounter( speminId );
		yield return Frames( 10 );

		// the comm buttons are only on the console after a hail, and a hail gives the player a posture (there is nothing to say without one)
		controller.m_encounter.m_pdEncounter.m_playerStance = GD_Comm.Stance.Friendly;

		buttonController.ChangeButtonSet( ButtonController.ButtonSet.Comm );

		ConsoleFrameWith( "m_submit" );

		controller.m_encounter.AddComm( GD_Comm.Subject.Question, false );

		controller.m_messages.Clear();

		var consoleAtTheQuestion = Console();

		yield return new WaitForSecondsRealtime( 1.0f );

		var consoleAfterThePress = Console();
		var sentAfterThePress = MessageList();

		// a press with nothing in its way is carried out
		controller.m_encounter.m_pdEncounter.m_lastQuestionFromAliens = 0;

		buttonController.ChangeButtonSet( ButtonController.ButtonSet.Comm );

		controller.m_messages.Clear();

		ConsoleFrameWith( "m_submit" );

		yield return new WaitForSecondsRealtime( 1.0f );

		var sentByAPlainPress = MessageList();

		Log( "comms: fire button on Statement, then a question from the aliens: console " + consoleAtTheQuestion + ", one second later " + consoleAfterThePress + ", sent: [" + sentAfterThePress + "] | a press with nothing in its way sent: [" + sentByAPlainPress + "]" );

		Check( "comms: a press on Statement does not answer a question that came after it", ( consoleAtTheQuestion == "AnswerQuestion selected 0 running nothing" ) && ( consoleAfterThePress == "AnswerQuestion selected 0 running nothing" ) && !sentAfterThePress.Contains( "Transmitting" ), "console " + consoleAfterThePress + ", sent: [" + sentAfterThePress + "]" );
		Check( "comms: a press with nothing in its way is carried out", sentByAPlainPress.Contains( "Transmitting" ) && !sentByAPlainPress.Contains( "ERROR" ), "sent: [" + sentByAPlainPress + "]" );

		Finish( "scenario=comms tokens=" + withToken + " twoSpacesThrew=" + threw + " garbledWords=" + garbledWords + "/" + plainWords + " mechan9=" + unlockedInPeace + "/" + unlockedAfterFiring + " press=[" + consoleAfterThePress + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: terrain (the crater maps, the random numbers after a landing, an object at the edge of the map, a scan of a deposit that is being picked up, labels after leaving)

	// how many objects a populator has put on the planet, and a number that changes when any of them is somewhere else
	static string Placed( Component populator )
	{
		var count = 0;
		var sum = 0.0;

		if ( populator != null )
		{
			foreach ( Transform child in populator.transform )
			{
				count++;

				sum += child.localPosition.x + child.localPosition.y * 3.0 + child.localPosition.z * 7.0;
			}
		}

		return count + "@" + sum.ToString( "F1" );
	}

	// how many scan labels are showing
	static int LabelsShowing()
	{
		var count = 0;

		foreach ( var textMesh in FindObjectsByType<TextMesh>( FindObjectsSortMode.None ) )
		{
			count += ( textMesh.gameObject.name == "Label" ) ? 1 : 0;
		}

		return count;
	}

	IEnumerator ScenarioTerrain()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// ---- 1. the crater maps are read from three 2048 by 1024 textures when the spaceflight scene starts. They are kept in a static, so a second start has nothing to read
		var mapsField = typeof( PG_Craters ).GetField( "m_craterTextureMaps", c_any );
		var mapsAtStart = mapsField.GetValue( null ) as float[][,];

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		PG_Craters.Initialize();

		var secondStart = stopwatch.Elapsed.TotalMilliseconds;
		var keptTheMaps = ReferenceEquals( mapsAtStart, mapsField.GetValue( null ) );

		// the first start of a session: nothing has been read yet
		mapsField.SetValue( null, null );

		stopwatch.Restart();

		PG_Craters.Initialize();

		var firstStart = stopwatch.Elapsed.TotalMilliseconds;
		var maps = mapsField.GetValue( null ) as float[][,];

		// the maps against the textures, read one pixel at a time (every fifth column of every seventh row)
		var compared = 0;
		var different = 0;
		var largestDifference = 0.0f;

		for ( var i = 0; i < maps.Length; i++ )
		{
			var texture = Resources.Load<Texture2D>( "Craters " + ( i + 1 ) );

			for ( var y = 0; y < texture.height; y += 7 )
			{
				for ( var x = 0; x < texture.width; x += 5 )
				{
					var fromTheTexture = texture.GetPixel( x, y ).r;

					compared++;

					different += ( maps[ i ][ y, x ] != fromTheTexture ) ? 1 : 0;

					largestDifference = Mathf.Max( largestDifference, Mathf.Abs( maps[ i ][ y, x ] - fromTheTexture ) );
				}
			}
		}

		Log( "terrain: crater maps: a first start reads them in " + firstStart.ToString( "F0" ) + " ms, a second start takes " + secondStart.ToString( "F0" ) + " ms (kept the maps it had: " + keptTheMaps + "). " + different + " of " + compared + " values differ from the textures, by " + largestDifference.ToString( "E2" ) + " at the most" );

		Check( "terrain: the crater maps are read once", keptTheMaps && ( secondStart < 50.0 ), "second start " + secondStart.ToString( "F0" ) + " ms, kept the maps: " + keptTheMaps );
		Check( "terrain: the crater maps hold what the textures hold", ( different == 0 ) && ( compared > 100000 ), different + " of " + compared + " values differ" );

		// ---- into the terrain vehicle on planet 90 (arth system, mineral density 43%), the way the m10 scenario does it
		yield return EnterOrbit( 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		if ( playerData.m_general.m_location != PD_General.Location.Disembarked )
		{
			Finish( "scenario=terrain abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainGrid = controller.m_disembarked.m_terrainGrid;
		var terrainVehicle = controller.m_terrainVehicle;
		var planetGenerator = controller.m_starSystem.GetPlanetController( 90 ).GetPlanetGenerator();

		var placedByTheGame = "rocks " + Placed( terrainGrid.m_terrainRocks ) + " deposits " + Placed( terrainGrid.m_terrainElements ) + " trees " + Placed( terrainGrid.m_terrainTrees );

		// ---- 2. putting the rocks on a planet must not disturb the random numbers of the game (it used to leave them at the same place after every landing on the same planet)
		UnityEngine.Random.InitState( 4242 );

		var nextNumber = UnityEngine.Random.value;

		UnityEngine.Random.InitState( 4242 );

		TerrainGridPopulator.ResetSpawnLists( planetGenerator );

		terrainGrid.m_terrainRocks.Initialize( planetGenerator, terrainGrid.m_elevationScale, 90 + 1 );

		var nextNumberAfterTheRocks = UnityEngine.Random.value;

		// the same again from another place in the game's random numbers
		UnityEngine.Random.InitState( 777 );

		var otherNextNumber = UnityEngine.Random.value;

		UnityEngine.Random.InitState( 777 );

		TerrainGridPopulator.ResetSpawnLists( planetGenerator );

		terrainGrid.m_terrainRocks.Initialize( planetGenerator, terrainGrid.m_elevationScale, 90 + 1 );

		var otherNextNumberAfterTheRocks = UnityEngine.Random.value;

		// the children that were replaced are destroyed at the end of the frame
		yield return Frames( 2 );

		var rocksAgain = Placed( terrainGrid.m_terrainRocks );

		Log( "terrain: placed by the game: " + placedByTheGame + " | rocks placed again: " + rocksAgain );
		Log( "terrain: the game's next random number was going to be " + nextNumber.ToString( "F6" ) + " and after the rocks it is " + nextNumberAfterTheRocks.ToString( "F6" ) + "; from another seed " + otherNextNumber.ToString( "F6" ) + " and " + otherNextNumberAfterTheRocks.ToString( "F6" ) );

		Check( "terrain: placing objects leaves the game's random numbers alone", ( nextNumberAfterTheRocks == nextNumber ) && ( otherNextNumberAfterTheRocks == otherNextNumber ) && ( nextNumber != otherNextNumber ), "expected " + nextNumber.ToString( "F6" ) + " and " + otherNextNumber.ToString( "F6" ) + ", got " + nextNumberAfterTheRocks.ToString( "F6" ) + " and " + otherNextNumberAfterTheRocks.ToString( "F6" ) );
		Check( "terrain: the rocks are in the same places every time", placedByTheGame.Contains( "rocks " + rocksAgain + " " ), "by the game: " + placedByTheGame + ", again: " + rocksAgain );

		// ---- 3. an object at the very right edge of the map (Random.Range can return its upper limit, which is the width of the map)
		var edge = Tools.MapToWorldCoordinates( planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight * 0.5f, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );
		var addToSpawnList = typeof( TerrainGridPopulator ).GetMethod( "AddToSpawnList", c_any );
		var overlapsSomething = typeof( TerrainGridPopulator ).GetMethod( "OverlapsSomething", c_any );
		var edgeResult = "added";

		try
		{
			overlapsSomething.Invoke( null, new object[] { edge } );
			addToSpawnList.Invoke( null, new object[] { edge } );

			// it has to be found again by the next object that wants the same spot
			edgeResult = (bool) overlapsSomething.Invoke( null, new object[] { edge } ) ? "added and found again" : "added but not found again";
		}
		catch ( TargetInvocationException exception )
		{
			edgeResult = "threw " + exception.InnerException.GetType().Name;
		}

		Log( "terrain: an object at map x " + planetGenerator.m_textureMapWidth + " of " + planetGenerator.m_textureMapWidth + ": " + edgeResult );

		Check( "terrain: an object at the right edge of the map can be placed", edgeResult == "added and found again", edgeResult );

		// ---- 4. a scan while a deposit is being picked up, and the labels of a scan after leaving the terrain vehicle
		var deposits = terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true );

		if ( deposits.Length < 2 )
		{
			Finish( "scenario=terrain abort: the planet has fewer than two deposits", 2 );
			yield break;
		}

		// one deposit next to the terrain vehicle, one 20 away (a scan reaches 50, a pickup 10), every other deposit out of reach
		var near = deposits[ 0 ];
		var far = deposits[ 1 ];

		foreach ( var other in deposits )
		{
			if ( ( other != near ) && ( other != far ) && ( Vector3.Distance( other.transform.position, terrainVehicle.transform.position ) < 60.0f ) )
			{
				other.transform.position += Vector3.right * 1000.0f;
			}
		}

		near.transform.position = BesideTheVehicle( terrainVehicle );
		far.transform.position = terrainVehicle.transform.position + Vector3.forward * 20.0f;

		var nearName = near.GetElementName();
		var farName = far.GetElementName();
		var expectedBefore = ( nearName == farName ) ? ( nearName + " deposits: 2" ) : ( nearName + " deposit: 1" );

		new ScanButton().Execute();

		var scanBefore = MessageList();

		// pick the near one up (its transporter effect takes a second and a half) and scan again at once
		new TVCargoButton().Execute();

		var pickupMessage = MessageList();

		new ScanButton().Execute();

		var scanDuring = MessageList();

		yield return Frames( 3 );

		var labelsInTheVehicle = LabelsShowing();

		Log( "terrain: scan with " + nearName + " 8 away and " + farName + " 20 away: " + scanBefore );
		Log( "terrain: pickup: " + pickupMessage + " | scan right after it: " + scanDuring );

		var countedBefore = scanBefore.Contains( expectedBefore );
		var countedDuring = ( nearName == farName ) ? !scanDuring.Contains( nearName + " deposit: 1" ) : scanDuring.Contains( nearName + " deposit" );

		Check( "terrain: a scan does not count a deposit that has just been picked up", countedBefore && pickupMessage.Contains( "Picked up" ) && !countedDuring, "before: counted " + countedBefore + "; right after the pickup: still counted " + countedDuring );

		// back into the ship
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		var labelsAfterLeaving = LabelsShowing();

		Log( "terrain: scan labels showing in the terrain vehicle " + labelsInTheVehicle + ", after going back into the ship " + labelsAfterLeaving + " (" + playerData.m_general.m_location + ")" );

		Check( "terrain: no scan label is left showing after leaving the terrain vehicle", ( labelsInTheVehicle > 0 ) && ( labelsAfterLeaving == 0 ), "in the vehicle " + labelsInTheVehicle + ", after leaving " + labelsAfterLeaving );

		Finish( "scenario=terrain craters=" + secondStart.ToString( "F0" ) + "ms/" + firstStart.ToString( "F0" ) + "ms/" + different + " random=" + ( nextNumberAfterTheRocks == nextNumber ) + " placed=[" + placedByTheGame + "] edge=[" + edgeResult + "] scan=" + countedDuring + " labels=" + labelsInTheVehicle + "/" + labelsAfterLeaving + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: the Escape key (the save panel) during the landing and while the ship explodes

	// one frame of the spaceflight controller with the Escape key held down
	static void PressEscape()
	{
		SetInput( "m_cancel", true );

		Call( SpaceflightController.m_instance, "Update" );

		SetInput( "m_cancel", false );
	}

	// close the save panel if it is open, and wait for it to slide away
	IEnumerator CloseTheSavePanel()
	{
		if ( PanelController.m_instance.HasActivePanel() )
		{
			PanelController.m_instance.Close();

			var end = Time.realtimeSinceStartup + 5.0f;

			while ( ( Time.realtimeSinceStartup < end ) && PanelController.m_instance.HasActivePanel() )
			{
				yield return null;
			}
		}
	}

	IEnumerator ScenarioSavePanel()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var buttonController = controller.m_buttonController;

		EnsureCrew();

		// ---- 1. Escape in space opens the save panel and pauses the game (so that the checks below are not passed by a panel that never opens)
		PressEscape();

		yield return new WaitForSecondsRealtime( 1.5f );

		var opensInSpace = PanelController.m_instance.HasActivePanel() && controller.m_gameIsPaused;

		yield return CloseTheSavePanel();

		var pausedAfterClosing = controller.m_gameIsPaused;

		Check( "savepanel: Escape opens the save panel in space, and closing it carries on with the game", opensInSpace && !pausedAfterClosing, "opened and paused " + opensInSpace + ", paused after closing " + pausedAfterClosing );

		// ---- 2. Escape two seconds into a landing (planet 94 is a small rock planet in the arth system; Land, then Descend)
		yield return EnterOrbit( 94 );

		PressButton( ButtonController.ButtonSet.CommandB, 0 );

		yield return Frames( 5 );

		PressButton( ButtonController.ButtonSet.Land, 1 );

		yield return new WaitForSecondsRealtime( 2.0f );

		var cameraBefore = controller.m_playerCamera.m_camera.transform.position;

		PressEscape();

		yield return new WaitForSecondsRealtime( 1.5f );

		var openedDuringLanding = PanelController.m_instance.HasActivePanel();
		var pausedDuringLanding = controller.m_gameIsPaused;

		yield return new WaitForSecondsRealtime( 1.5f );

		var cameraMoved = Vector3.Distance( cameraBefore, controller.m_playerCamera.m_camera.transform.position );

		Log( "savepanel: Escape 2 s into the landing: panel open " + openedDuringLanding + ", game paused " + pausedDuringLanding + ", the camera moved " + cameraMoved.ToString( "F0" ) + " in the 3 s after it (" + Console() + ")" );

		Check( "savepanel: Escape does not open the save panel during the landing", !openedDuringLanding && !pausedDuringLanding && ( cameraMoved > 1.0f ), "panel open " + openedDuringLanding + ", paused " + pausedDuringLanding + ", camera moved " + cameraMoved.ToString( "F0" ) );

		yield return CloseTheSavePanel();

		// wait for the ship to be down
		var end = Time.realtimeSinceStartup + 50.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !( ( playerData.m_general.m_location == PD_General.Location.Planetside ) && ( buttonController.GetCurrentButtonSet() == ButtonController.ButtonSet.CommandA ) ) )
		{
			yield return null;
		}

		yield return new WaitForSecondsRealtime( 1.0f );

		var locationAfterLanding = playerData.m_general.m_location;

		// on the ground Escape works again
		PressEscape();

		yield return new WaitForSecondsRealtime( 1.5f );

		var opensOnTheGround = PanelController.m_instance.HasActivePanel() && controller.m_gameIsPaused;

		yield return CloseTheSavePanel();

		Log( "savepanel: after the landing (" + locationAfterLanding + ") Escape opens the panel: " + opensOnTheGround + ", paused after closing it: " + controller.m_gameIsPaused );

		Check( "savepanel: Escape opens the save panel again once the ship is down", ( locationAfterLanding == PD_General.Location.Planetside ) && opensOnTheGround && !controller.m_gameIsPaused, locationAfterLanding + ", opened " + opensOnTheGround + ", paused after closing " + controller.m_gameIsPaused );

		// ---- 3. Escape while the ship explodes (the explosion takes a second and a half, then the game over screen pauses the game for good)
		playerData.m_playerShip.m_shieldsAreUp = false;

		EnterEncounter( FindEncounter( 1, 6, 3, 0 ) );

		yield return Frames( 10 );

		playerData.m_playerShip.m_armorPoints = 100;

		CombatController.m_instance.ApplyDamageToPlayer( 5000, Vector3.forward );

		yield return Frames( 5 );

		PressEscape();

		yield return new WaitForSecondsRealtime( 3.0f );

		var openedDuringExplosion = PanelController.m_instance.HasActivePanel();
		var gameOver = controller.m_gameOver;

		yield return CloseTheSavePanel();

		var pausedAfterGameOver = controller.m_gameIsPaused;

		// the game over screen has to keep the game paused whoever tells the controller that a panel was closed
		controller.PanelWasClosed();

		var pausedAfterAPanelClosed = controller.m_gameIsPaused;

		Log( "savepanel: Escape while the ship explodes: panel open " + openedDuringExplosion + ", game over " + gameOver + ", paused after closing the panel " + pausedAfterGameOver + ", paused after a panel reports that it closed " + pausedAfterAPanelClosed );

		Check( "savepanel: Escape does not open the save panel while the ship explodes", gameOver && !openedDuringExplosion, "game over " + gameOver + ", panel open " + openedDuringExplosion );
		Check( "savepanel: a game that is over stays paused", gameOver && pausedAfterGameOver && pausedAfterAPanelClosed, "game over " + gameOver + ", paused " + pausedAfterGameOver + ", paused after a panel closed " + pausedAfterAPanelClosed );

		Finish( "scenario=savepanel space=" + opensInSpace + " landing=" + openedDuringLanding + "/" + pausedDuringLanding + " ground=" + opensOnTheGround + " explosion=" + openedDuringExplosion + "/" + pausedAfterGameOver + "/" + pausedAfterAPanelClosed + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- input: the keys of the old input manager, read through the Input System

	static Keyboard s_probeKeyboard;

	// a keyboard of the probe's own (a headless run has none), whose keys reach the game although no game view has the focus
	static Keyboard ProbeKeyboard()
	{
		if ( s_probeKeyboard == null )
		{
			// these settings live in memory only (the project has no Input System settings asset), so they end with the run
			InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
			InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

			s_probeKeyboard = InputSystem.AddDevice<Keyboard>( "ProbeKeyboard" );
		}

		return s_probeKeyboard;
	}

	// hold these keys of the probe keyboard down, and every other key up, from the next input update on
	static void HoldKeys( params Key[] keys )
	{
		InputSystem.QueueStateEvent( ProbeKeyboard(), new KeyboardState( keys ) );
	}

	// press the keys for three frames and let them go for three
	static IEnumerator Tap( params Key[] keys )
	{
		HoldKeys( keys );

		yield return Frames( 3 );

		HoldKeys();

		yield return Frames( 3 );
	}

	// what the input controller reports: the buttons it has down and the stick position
	static string InputReport()
	{
		var input = InputController.m_instance;
		var held = new List<string>();

		if ( input.m_northWest ) held.Add( "NW" );
		if ( input.m_north ) held.Add( "N" );
		if ( input.m_northEast ) held.Add( "NE" );
		if ( input.m_east ) held.Add( "E" );
		if ( input.m_southEast ) held.Add( "SE" );
		if ( input.m_south ) held.Add( "S" );
		if ( input.m_southWest ) held.Add( "SW" );
		if ( input.m_west ) held.Add( "W" );
		if ( input.m_submit ) held.Add( "Submit" );
		if ( input.m_cancel ) held.Add( "Cancel" );

		return ( ( held.Count == 0 ) ? "nothing" : string.Join( "+", held ) ) + " x=" + input.m_x.ToString( "0" ) + " y=" + input.m_y.ToString( "0" );
	}

	// the name of the UI object the event system has selected
	static string Selected()
	{
		var selected = ( EventSystem.current != null ) ? EventSystem.current.currentSelectedGameObject : null;

		return ( selected != null ) ? selected.name : "none";
	}

	// the type of the event system's input module
	static string InputModule()
	{
		var module = ( EventSystem.current != null ) ? EventSystem.current.currentInputModule : null;

		return ( module != null ) ? module.GetType().Name : "none";
	}

	IEnumerator ScenarioInput()
	{
		var controller = SpaceflightController.m_instance;
		var playerData = DataController.m_instance.m_playerData;

		EnsureCrew();

		ProbeKeyboard();

		Log( "input: probe keyboard added, Keyboard.current is it " + ( Keyboard.current == s_probeKeyboard ) + ", input module " + InputModule() );

		// ---- 1. every key of ProjectSettings/InputManager.asset (the positive and the alternate positive button of each axis), and keys that had no axis.
		// The spaceflight controller and the console would act on the keys and debounce them, so they sit this part out
		controller.enabled = false;
		controller.m_buttonController.enabled = false;

		HoldKeys();

		yield return Frames( 3 );

		var keys = new[]
		{
			new KeyValuePair<Key, string>( Key.Numpad7, "NW x=-1 y=1" ), new KeyValuePair<Key, string>( Key.Home, "NW x=-1 y=1" ),
			new KeyValuePair<Key, string>( Key.Numpad8, "N x=0 y=1" ), new KeyValuePair<Key, string>( Key.UpArrow, "N x=0 y=1" ),
			new KeyValuePair<Key, string>( Key.Numpad9, "NE x=1 y=1" ), new KeyValuePair<Key, string>( Key.PageUp, "NE x=1 y=1" ),
			new KeyValuePair<Key, string>( Key.Numpad6, "E x=1 y=0" ), new KeyValuePair<Key, string>( Key.RightArrow, "E x=1 y=0" ),
			new KeyValuePair<Key, string>( Key.Numpad3, "SE x=1 y=-1" ), new KeyValuePair<Key, string>( Key.PageDown, "SE x=1 y=-1" ),
			new KeyValuePair<Key, string>( Key.Numpad2, "S x=0 y=-1" ), new KeyValuePair<Key, string>( Key.DownArrow, "S x=0 y=-1" ),
			new KeyValuePair<Key, string>( Key.Numpad1, "SW x=-1 y=-1" ), new KeyValuePair<Key, string>( Key.End, "SW x=-1 y=-1" ),
			new KeyValuePair<Key, string>( Key.Numpad4, "W x=-1 y=0" ), new KeyValuePair<Key, string>( Key.LeftArrow, "W x=-1 y=0" ),
			new KeyValuePair<Key, string>( Key.Enter, "Submit x=0 y=0" ), new KeyValuePair<Key, string>( Key.NumpadEnter, "Submit x=0 y=0" ),
			new KeyValuePair<Key, string>( Key.Escape, "Cancel x=0 y=0" ),
		};

		var others = new[] { Key.W, Key.A, Key.S, Key.D, Key.Space, Key.Numpad5, Key.Numpad0, Key.Tab };

		var wrongKeys = new List<string>();
		var wrongOthers = new List<string>();

		foreach ( var pair in keys )
		{
			HoldKeys( pair.Key );

			yield return Frames( 2 );

			var held = InputReport();

			HoldKeys();

			yield return Frames( 2 );

			var released = InputReport();

			if ( ( held != pair.Value ) || ( released != "nothing x=0 y=0" ) )
			{
				wrongKeys.Add( pair.Key + ": " + held + " / released " + released );
			}
		}

		foreach ( var key in others )
		{
			HoldKeys( key );

			yield return Frames( 2 );

			var held = InputReport();

			HoldKeys();

			yield return Frames( 2 );

			if ( held != "nothing x=0 y=0" )
			{
				wrongOthers.Add( key + ": " + held );
			}
		}

		Log( "input: " + ( keys.Length - wrongKeys.Count ) + " of " + keys.Length + " keys gave their input; wrong: " + string.Join( " | ", wrongKeys ) );
		Log( "input: keys with no input that gave one: " + string.Join( " | ", wrongOthers ) );

		Check( "input: each key of the old input manager gives its input, and only while it is held", wrongKeys.Count == 0, string.Join( " | ", wrongKeys ) );
		Check( "input: keys the old input manager did not have give no input", wrongOthers.Count == 0, string.Join( " | ", wrongOthers ) );

		// ---- 2. two keys of one input, and the debounce: an input that has been debounced stays off until every key has been let go
		HoldKeys( Key.Numpad8, Key.UpArrow );

		yield return Frames( 2 );

		HoldKeys( Key.UpArrow );

		yield return Frames( 2 );

		var oneOfTwoLetGo = InputReport();

		InputController.m_instance.Debounce();

		yield return Frames( 2 );

		var debounced = InputReport();

		HoldKeys();

		yield return Frames( 2 );

		HoldKeys( Key.Numpad8 );

		yield return Frames( 2 );

		var pressedAgain = InputReport();

		HoldKeys();

		yield return Frames( 2 );

		Log( "input: up arrow held after numpad 8 was let go: " + oneOfTwoLetGo + "; debounced while held: " + debounced + "; pressed again after letting go: " + pressedAgain );

		Check( "input: one key of an input still holds it when the other is let go", oneOfTwoLetGo == "N x=0 y=1", oneOfTwoLetGo );
		Check( "input: a debounced input stays off while held and comes back once it has been let go", ( debounced == "nothing x=0 y=0" ) && ( pressedAgain == "N x=0 y=1" ), debounced + " / " + pressedAgain );

		controller.enabled = true;
		controller.m_buttonController.enabled = true;

		yield return Frames( 3 );

		// ---- 3. the UI: Escape opens the save panel, the arrows and the numpad move the selection (A and D do not), Return and the numpad Enter press Resume.
		// The buttons of the save panel are in a row with explicit left and right neighbours, so up and down leave the selection where it is
		yield return Tap( Key.Escape );

		yield return new WaitForSecondsRealtime( 1.5f );

		var panelOpened = PanelController.m_instance.HasActivePanel() && controller.m_gameIsPaused;
		var atOpen = Selected();

		yield return Tap( Key.RightArrow );

		var afterRight = Selected();

		yield return Tap( Key.LeftArrow );

		var afterLeft = Selected();

		yield return Tap( Key.Numpad6 );

		var afterNumpad6 = Selected();

		yield return Tap( Key.Numpad4 );

		var afterNumpad4 = Selected();

		yield return Tap( Key.D );

		var afterD = Selected();

		yield return Tap( Key.A );

		var afterA = Selected();

		yield return Tap( Key.DownArrow );

		var afterDown = Selected();

		yield return Tap( Key.Enter );

		yield return new WaitForSecondsRealtime( 1.5f );

		var closedByReturn = !PanelController.m_instance.HasActivePanel() && !controller.m_gameIsPaused;

		yield return Tap( Key.Escape );

		yield return new WaitForSecondsRealtime( 1.5f );

		var panelOpenedAgain = PanelController.m_instance.HasActivePanel();

		yield return Tap( Key.NumpadEnter );

		yield return new WaitForSecondsRealtime( 1.5f );

		var closedByNumpadEnter = !PanelController.m_instance.HasActivePanel() && !controller.m_gameIsPaused;

		if ( PanelController.m_instance.HasActivePanel() )
		{
			yield return CloseTheSavePanel();
		}

		Log( "input: module " + InputModule() + "; Escape opened the save panel " + panelOpened + ", selected " + atOpen + ", right arrow " + afterRight + ", left arrow " + afterLeft + ", numpad 6 " + afterNumpad6 + ", numpad 4 " + afterNumpad4 + ", D " + afterD + ", A " + afterA + ", down arrow " + afterDown + "; Return closed it " + closedByReturn + "; opened again " + panelOpenedAgain + ", numpad Enter closed it " + closedByNumpadEnter );

		Check( "input: Escape opens the save panel", panelOpened && panelOpenedAgain, "opened " + panelOpened + ", again " + panelOpenedAgain );
		Check( "input: the arrow keys and the numpad move the UI selection, A and D do not", ( atOpen != "none" ) && ( afterRight != atOpen ) && ( afterRight != "none" ) && ( afterLeft == atOpen ) && ( afterNumpad6 == afterRight ) && ( afterNumpad4 == atOpen ) && ( afterD == atOpen ) && ( afterA == atOpen ) && ( afterDown == atOpen ), atOpen + " > " + afterRight + " > " + afterLeft + " > " + afterNumpad6 + " > " + afterNumpad4 + " > " + afterD + " > " + afterA + " > " + afterDown );
		Check( "input: Return and the numpad Enter press the selected button", panelOpened && closedByReturn && panelOpenedAgain && closedByNumpadEnter, "opened " + panelOpened + ", Return closed it " + closedByReturn + ", opened again " + panelOpenedAgain + ", numpad Enter closed it " + closedByNumpadEnter );

		// ---- 4. the keys the spaceflight controller reads itself: F9 and F10 (Editor only) and Escape once the game is over
		var messagesBefore = MessageList();

		yield return Tap( Key.F9 );

		var f9Message = MessageList().Length > messagesBefore.Length && MessageList().Substring( messagesBefore.Length ).Contains( "DEBUG:" );
		var locationAfterF9 = playerData.m_general.m_location;

		yield return Tap( Key.F10 );

		var destroyed = CombatController.m_instance.PlayerIsDestroyed();

		for ( var i = 0; ( i < 100 ) && !controller.m_gameOver; i++ )
		{
			yield return new WaitForSecondsRealtime( 0.1f );
		}

		var gameOver = controller.m_gameOver;

		yield return Tap( Key.Escape );

		var gameOverAfterEscape = controller.m_gameOver;

		yield return WaitForScene( "Intro" );

		var sceneAfterEscape = SceneManager.GetActiveScene().name;

		Log( "input: F9 left a DEBUG message " + f9Message + " (" + locationAfterF9 + "), F10 destroyed the ship " + destroyed + ", game over " + gameOver + ", game over after Escape " + gameOverAfterEscape + ", scene " + sceneAfterEscape );

		Check( "input: F9 runs the debug encounter spawn", f9Message, MessageList() );
		Check( "input: F10 destroys the ship", destroyed, "destroyed " + destroyed );
		Check( "input: Escape at game over restarts the game", gameOver && !gameOverAfterEscape && ( sceneAfterEscape == "Intro" ), "game over " + gameOver + ", after Escape " + gameOverAfterEscape + ", scene " + sceneAfterEscape );

		HoldKeys();

		Finish( "scenario=input keys=" + ( keys.Length - wrongKeys.Count ) + "/" + keys.Length + " others=" + wrongOthers.Count + " ui=" + atOpen + ">" + afterRight + "/" + closedByReturn + "/" + closedByNumpadEnter + " f9=" + f9Message + " f10=" + destroyed + " restart=" + sceneAfterEscape + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	IEnumerator ScenarioStarportInput()
	{
		ProbeKeyboard();

		var module = InputModule();

		// the Operations panel selects its Notices button when it opens
		var operationsPanel = FindPanel<OperationsPanel>();

		yield return OpenPanel( operationsPanel, "starport-input" );

		var atOpen = Selected();

		yield return Tap( Key.DownArrow );

		var afterDown = Selected();

		yield return Tap( Key.UpArrow );

		var afterUp = Selected();

		yield return Tap( Key.Numpad2 );

		var afterNumpad2 = Selected();

		yield return Tap( Key.Numpad8 );

		var afterNumpad8 = Selected();

		yield return Tap( Key.S );

		var afterS = Selected();

		yield return ClosePanel( operationsPanel, "starport-input" );

		// the crew's name field: a legacy InputField reads Input.compositionString (the IME) in every frame it has the focus, the one use of the old Input class left in UGUI
		var personnelPanel = FindPanel<PersonnelPanel>();

		yield return OpenPanel( personnelPanel, "starport-input" );

		var exceptionsBefore = s_exceptionCount;

		Call( personnelPanel, "SwitchToGiveNameState" );

		yield return Frames( 30 );

		var nameFieldFocused = personnelPanel.m_nameInputField.isFocused;
		var exceptionsWhileFocused = s_exceptionCount - exceptionsBefore;

		yield return ClosePanel( personnelPanel, "starport-input" );

		Log( "starport-input: module " + module + "; Operations panel selected " + atOpen + ", down arrow " + afterDown + ", up arrow " + afterUp + ", numpad 2 " + afterNumpad2 + ", numpad 8 " + afterNumpad8 + ", S " + afterS + "; crew name field focused " + nameFieldFocused + ", exceptions while it had the focus " + exceptionsWhileFocused );

		Check( "starport-input: the event system reads the Input System", module == "InputSystemUIInputModule", module );
		Check( "starport-input: the arrow keys and the numpad move the UI selection, S does not", ( atOpen != "none" ) && ( afterDown != atOpen ) && ( afterDown != "none" ) && ( afterUp == atOpen ) && ( afterNumpad2 == afterDown ) && ( afterNumpad8 == atOpen ) && ( afterS == atOpen ), atOpen + " > " + afterDown + " > " + afterUp + " > " + afterNumpad2 + " > " + afterNumpad8 + " > " + afterS );
		Check( "starport-input: the crew name field takes the focus without an exception", nameFieldFocused && ( exceptionsWhileFocused == 0 ), "focused " + nameFieldFocused + ", exceptions " + exceptionsWhileFocused );

		HoldKeys();

		Finish( "scenario=starport-input module=" + module + " ui=" + atOpen + ">" + afterDown + ">" + afterUp + " nameField=" + nameFieldFocused + "/" + exceptionsWhileFocused + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: visual (a floating object when its timer wraps, the end of an explosion, the speed of the landing site crosshair)

	// set the stick position the input controller reports (it is a number from -1 to 1 for each axis)
	static void SetAxis( string name, float value )
	{
		typeof( InputController ).GetProperty( name ).SetValue( InputController.m_instance, value );
	}

	IEnumerator ScenarioVisual()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var combat = CombatController.m_instance;

		EnsureCrew();

		// ---- 1. a floating object: its height is the sine of a timer that counts up, and the timer starts again at some point
		var floatObject = new GameObject( "Probe Float" );
		var floating = floatObject.AddComponent<Float>();

		floating.m_speed = 1.0f;
		floating.m_range = 45.0f;

		yield return Frames( 2 );

		// just before the place where the old code started the timer again (360), and then enough frames to get past it
		SetField( floating, "m_timer", 359.99f );

		var largestStep = 0.0f;
		var heightAtTheLargestStep = 0.0f;

		for ( var i = 0; i < 500; i++ )
		{
			var last = floatObject.transform.localPosition.y;

			Call( floating, "Update" );

			// the first call only puts the object where its timer says it is
			if ( ( i > 0 ) && ( Mathf.Abs( floatObject.transform.localPosition.y - last ) > largestStep ) )
			{
				largestStep = Mathf.Abs( floatObject.transform.localPosition.y - last );
				heightAtTheLargestStep = last;
			}
		}

		var timerAfter = (float) GetField( floating, "m_timer" );

		Destroy( floatObject );

		Log( "visual: a floating object with a range of 45, its timer taken from 359.99 past 360 in 500 steps of " + Time.deltaTime.ToString( "F4" ) + ": the largest step in height was " + largestStep.ToString( "F2" ) + " (from " + heightAtTheLargestStep.ToString( "F2" ) + "), timer now " + timerAfter.ToString( "F3" ) );

		Check( "visual: a floating object does not jump when its timer starts again", largestStep < 5.0f, "largest step " + largestStep.ToString( "F2" ) + " of a range of 45" );

		// ---- 2. an explosion: whoever waits for it is told after a second and a half; its debris and smoke live for two seconds
		var explosion = Call( combat, "GetAvailableExplosion" ) as ExplosionEffect;
		var toldAfter = -1.0f;
		var start = Time.realtimeSinceStartup;

		explosion.Play( playerData.m_general.m_coordinates + Vector3.up * 500.0f, () => { toldAfter = Time.realtimeSinceStartup - start; } );

		yield return new WaitForSecondsRealtime( 1.8f );

		var onAt18 = explosion.gameObject.activeSelf;
		var busyAt18 = explosion.IsPlaying();

		yield return new WaitForSecondsRealtime( 0.8f );

		var onAt26 = explosion.gameObject.activeSelf;
		var busyAt26 = explosion.IsPlaying();

		Log( "visual: explosion: the caller was told after " + toldAfter.ToString( "F2" ) + " s; at 1.8 s it is " + ( onAt18 ? "on" : "off" ) + " (busy " + busyAt18 + "), at 2.6 s it is " + ( onAt26 ? "on" : "off" ) + " (busy " + busyAt26 + ")" );

		Check( "visual: an explosion is not switched off before its smoke and debris are gone", ( toldAfter > 1.4f ) && ( toldAfter < 1.7f ) && onAt18 && busyAt18 && !onAt26 && !busyAt26, "told after " + toldAfter.ToString( "F2" ) + " s, on at 1.8 s: " + onAt18 + ", on at 2.6 s: " + onAt26 );

		// ---- 3. the crosshair of the landing site: the stick held to the right for one second
		var terrainMapDisplay = controller.m_displayController.m_terrainMapDisplay;

		playerData.m_general.m_selectedLatitude = 0.0f;
		playerData.m_general.m_selectedLongitude = 0.0f;

		// one call with the stick in the middle, as there is in the game before the stick is moved
		terrainMapDisplay.MoveCrosshairs();

		var frames = 0;
		var end = Time.realtimeSinceStartup + 1.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			SetAxis( "m_x", 1.0f );

			terrainMapDisplay.MoveCrosshairs();

			SetAxis( "m_x", 0.0f );

			frames++;

			yield return null;
		}

		var movedInOneSecond = playerData.m_general.m_selectedLatitude;

		// and for two more seconds, by which time it has reached its top speed
		end = Time.realtimeSinceStartup + 2.0f;

		playerData.m_general.m_selectedLatitude = -180.0f;

		var atTwoSeconds = float.NaN;
		var held = 1.0f;
		var lastTime = Time.realtimeSinceStartup;

		while ( Time.realtimeSinceStartup < end )
		{
			SetAxis( "m_x", 1.0f );

			terrainMapDisplay.MoveCrosshairs();

			SetAxis( "m_x", 0.0f );

			held += Time.realtimeSinceStartup - lastTime;
			lastTime = Time.realtimeSinceStartup;

			if ( float.IsNaN( atTwoSeconds ) && ( held >= 2.0f ) )
			{
				atTwoSeconds = playerData.m_general.m_selectedLatitude;
			}

			yield return null;
		}

		var speedAtTheEnd = playerData.m_general.m_selectedLatitude - atTwoSeconds;

		Log( "visual: crosshair: stick held right for one second (" + frames + " frames): moved " + movedInOneSecond.ToString( "F1" ) + " degrees. In the third second it moved " + speedAtTheEnd.ToString( "F1" ) + " degrees" );

		Check( "visual: the crosshair moves the same distance at any frame rate", ( movedInOneSecond > 10.0f ) && ( movedInOneSecond < 20.0f ), "moved " + movedInOneSecond.ToString( "F1" ) + " degrees in one second over " + frames + " frames (15 at 60 frames a second)" );
		Check( "visual: the crosshair has a top speed", ( speedAtTheEnd > 50.0f ) && ( speedAtTheEnd < 70.0f ), "moved " + speedAtTheEnd.ToString( "F1" ) + " degrees in the third second (the top speed is 60 a second)" );

		playerData.m_general.m_selectedLatitude = 0.0f;

		Finish( "scenario=visual floatStep=" + largestStep.ToString( "F2" ) + " explosion=" + toldAfter.ToString( "F2" ) + "/" + onAt18 + "/" + onAt26 + " crosshair=" + movedInOneSecond.ToString( "F1" ) + "/" + speedAtTheEnd.ToString( "F1" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: explosion pictures (what the explosion is drawn with, and pictures of it when the run has a graphics device: probe.ps1 -Graphics)

	// render the main camera into a PNG file (the space view only: the console is drawn by other cameras and the canvas)
	static void RenderCameraPicture( Camera camera, string path )
	{
		var renderTexture = new RenderTexture( 1920, 1080, 24, RenderTextureFormat.ARGB32 );
		var picture = new Texture2D( 1920, 1080, TextureFormat.RGB24, false );

		camera.targetTexture = renderTexture;
		camera.Render();
		camera.targetTexture = null;

		var active = RenderTexture.active;

		RenderTexture.active = renderTexture;
		picture.ReadPixels( new Rect( 0, 0, 1920, 1080 ), 0, 0 );
		picture.Apply();
		RenderTexture.active = active;

		System.IO.File.WriteAllBytes( path, picture.EncodeToPNG() );

		UnityEngine.Object.Destroy( picture );
		renderTexture.Release();
		UnityEngine.Object.Destroy( renderTexture );
	}

	// how many explosion pictures were drawn
	static int s_explosionPictures;

	// draw a picture at each of the given frames after this one (game time is a thirtieth of a second a frame), then wait for the explosion, if one is given, to end
	static IEnumerator ExplosionPictureSeries( Camera camera, string folder, string prefix, int[] frameTimes, ExplosionEffect explosion )
	{
		var start = Time.frameCount;

		foreach ( var frameTime in frameTimes )
		{
			while ( Time.frameCount - start < frameTime )
			{
				yield return null;
			}

			RenderCameraPicture( camera, System.IO.Path.Combine( folder, prefix + "t" + ( frameTime * 1000 / 30 ).ToString( "D4" ) + "ms.png" ) );
			s_explosionPictures++;
		}

		while ( ( explosion != null ) && explosion.IsPlaying() && ( Time.frameCount - start < 200 ) )
		{
			yield return null;
		}
	}

	IEnumerator ScenarioExplosionPictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var combat = CombatController.m_instance;

		// ---- 1. what an explosion is drawn with: every part textured and transparent, no cubes, and it follows the scale the combat controller gives it
		var explosion = Call( combat, "GetAvailableExplosion" ) as ExplosionEffect;
		var renderers = explosion.GetComponentsInChildren<ParticleSystemRenderer>( true );
		var opaqueOrUntextured = new List<string>();
		var cubes = 0;

		foreach ( var particleRenderer in renderers )
		{
			var material = particleRenderer.sharedMaterial;

			if ( ( material == null ) || ( material.mainTexture == null ) || ( material.renderQueue < 3000 ) )
			{
				opaqueOrUntextured.Add( particleRenderer.name + " (" + ( ( material == null ) ? "no material" : material.shader.name + ", queue " + material.renderQueue + ", texture " + ( material.mainTexture != null ) ) + ")" );
			}

			if ( ( particleRenderer.renderMode == ParticleSystemRenderMode.Mesh ) && ( particleRenderer.mesh != null ) && particleRenderer.mesh.name.StartsWith( "Cube" ) )
			{
				cubes++;
			}
		}

		var scaledSystems = 0;
		var particleSystems = explosion.GetComponentsInChildren<ParticleSystem>( true );

		foreach ( var particleSystem in particleSystems )
		{
			if ( particleSystem.main.scalingMode == ParticleSystemScalingMode.Hierarchy )
			{
				scaledSystems++;
			}
		}

		Log( "explosionpictures: " + renderers.Length + " parts, opaque or untextured: " + ( ( opaqueOrUntextured.Count == 0 ) ? "none" : string.Join( ", ", opaqueOrUntextured ) ) + "; cube debris parts " + cubes + "; parts that follow the explosion's scale " + scaledSystems + " of " + particleSystems.Length );

		Check( "explosionpictures: every part of an explosion is textured and transparent", ( renderers.Length > 0 ) && ( opaqueOrUntextured.Count == 0 ), ( opaqueOrUntextured.Count == 0 ) ? "all " + renderers.Length + " parts" : string.Join( ", ", opaqueOrUntextured ) );
		Check( "explosionpictures: the debris is not grey cubes", cubes == 0, cubes + " parts draw the cube mesh" );
		Check( "explosionpictures: a half size explosion (a missile hit) is drawn at half size", ( particleSystems.Length > 0 ) && ( scaledSystems == particleSystems.Length ), scaledSystems + " of " + particleSystems.Length + " parts follow the scale" );

		// ---- 2. control: making the explosion's textures does not draw from the game's random numbers (the planets depend on them)
		ForgetEffectMaterials();

		var stateBefore = JsonUtility.ToJson( UnityEngine.Random.state );
		var madeObject = new GameObject( "Probe Explosion" );

		madeObject.AddComponent<ExplosionEffect>();

		var stateAfter = JsonUtility.ToJson( UnityEngine.Random.state );

		Destroy( madeObject );

		Check( "explosionpictures: making an explosion leaves the game's random numbers alone", stateBefore == stateAfter, ( stateBefore == stateAfter ) ? "Random.state unchanged" : "Random.state changed" );

		// ---- 3. pictures, when there is a graphics device to draw them with
		if ( SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null )
		{
			Log( "explosionpictures: no graphics device (-nographics), so no pictures; run probe.ps1 with -Graphics for them" );

			Finish( "scenario=explosionpictures parts=" + renderers.Length + " opaqueOrUntextured=" + opaqueOrUntextured.Count + " cubes=" + cubes + " scaled=" + scaledSystems + "/" + particleSystems.Length + " pictures=0 checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
			yield break;
		}

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "explosion-pictures" ) );

		System.IO.Directory.CreateDirectory( folder );

		var camera = Camera.main;
		var controller = SpaceflightController.m_instance;
		var frameTimes = new int[] { 2, 5, 9, 15, 24, 33, 45, 60 };

		s_explosionPictures = 0;

		// ---- 3a. out in dark hyperspace (a new game starts next to the sun, whose glare hides everything), with no encounter able to begin
		controller.m_encounterRange = 0.0f;
		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );
		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 30 );

		// game time goes up by a thirtieth of a second a frame, however long a frame takes, so the pictures are at the same moments in every run
		Time.captureDeltaTime = 1.0f / 30.0f;

		// a missile hit, at half size, beside the ship
		var hit = Call( combat, "GetAvailableExplosion" ) as ExplosionEffect;

		hit.SetScale( 0.5f );
		hit.Play( playerData.m_general.m_coordinates + new Vector3( 120.0f, 0.0f, 0.0f ) );

		yield return ExplosionPictureSeries( camera, folder, "space_hit_", new int[] { 9 }, hit );

		// an explosion the size of the ship blowing up, on the ship
		var full = Call( combat, "GetAvailableExplosion" ) as ExplosionEffect;

		full.Play( playerData.m_general.m_coordinates );

		yield return ExplosionPictureSeries( camera, folder, "space_", frameTimes, full );

		Time.captureDeltaTime = 0.0f;

		// ---- 3b. in orbit around a planet, the ship destroyed the way F10 destroys it (its wreck is left where it was)
		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );

		yield return EnterOrbit( 90 );

		yield return Frames( 30 );

		Time.captureDeltaTime = 1.0f / 30.0f;

		playerData.m_playerShip.m_shieldPoints = 0;
		playerData.m_playerShip.m_armorPoints = 0;
		combat.ApplyDamageToPlayer( 1, Vector3.forward );

		yield return ExplosionPictureSeries( camera, folder, "orbit_", frameTimes, null );

		Time.captureDeltaTime = 0.0f;

		var pictures = s_explosionPictures;

		Log( "explosionpictures: " + pictures + " pictures in " + folder );

		Finish( "scenario=explosionpictures parts=" + renderers.Length + " opaqueOrUntextured=" + opaqueOrUntextured.Count + " cubes=" + cubes + " scaled=" + scaledSystems + "/" + particleSystems.Length + " pictures=" + pictures + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: hit pictures (what the hull and shield hit effects are drawn with, where they land, and pictures of them with -Graphics)

	// forget the shared effect materials, wherever they are kept (the explosion kept them itself before they moved to EffectMaterials), so the next effect makes them again
	static void ForgetEffectMaterials()
	{
		var holders = new Type[] { typeof( ExplosionEffect ), Type.GetType( "EffectMaterials, Assembly-CSharp" ) };

		foreach ( var holder in holders )
		{
			if ( holder == null )
			{
				continue;
			}

			foreach ( var name in new string[] { "m_glowMaterial", "m_fireMaterial", "m_smokeMaterial", "m_ringMaterial", "m_debrisMaterial", "m_debrisMeshes" } )
			{
				var field = holder.GetField( name, c_any );

				if ( ( field != null ) && field.IsStatic )
				{
					field.SetValue( null, null );
				}
			}
		}
	}

	// play a hit effect with the direction of the shot if it takes one (the old shield hit had only a position)
	static void PlayHitEffect( object effect, Vector3 position, Vector3 direction )
	{
		var withDirection = effect.GetType().GetMethod( "Play", new Type[] { typeof( Vector3 ), typeof( Vector3 ) } );

		if ( withDirection != null )
		{
			withDirection.Invoke( effect, new object[] { position, direction } );
		}
		else
		{
			effect.GetType().GetMethod( "Play", new Type[] { typeof( Vector3 ) } ).Invoke( effect, new object[] { position } );
		}
	}

	// render the main camera zoomed in (a narrower field of view around the same middle)
	static void RenderZoomedPicture( Camera camera, string path, float fieldOfView )
	{
		var normalFieldOfView = camera.fieldOfView;

		camera.fieldOfView = fieldOfView;

		RenderCameraPicture( camera, path );

		camera.fieldOfView = normalFieldOfView;
	}

	// draw a zoomed picture at each of the given frames after this one, then let the effect end
	static IEnumerator HitPictureSeries( Camera camera, string folder, string prefix, int[] frameTimes )
	{
		var start = Time.frameCount;

		foreach ( var frameTime in frameTimes )
		{
			while ( Time.frameCount - start < frameTime )
			{
				yield return null;
			}

			RenderZoomedPicture( camera, System.IO.Path.Combine( folder, prefix + "t" + ( frameTime * 1000 / 30 ).ToString( "D4" ) + "ms.png" ), 9.0f );
			s_explosionPictures++;
		}

		yield return Frames( 30 );
	}

	IEnumerator ScenarioHitPictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var combat = CombatController.m_instance;
		var controller = SpaceflightController.m_instance;

		var hullHit = Call( combat, "GetAvailableHullHit" ) as HullHitEffect;
		var shieldHit = Call( combat, "GetAvailableShieldHit" ) as ShieldHitEffect;

		// ---- 1. what the hits are drawn with: nothing with the lit Standard shader or the untextured opaque particle shader, everything transparent
		var drawnWrong = new List<string>();
		var parts = 0;

		foreach ( var effect in new Component[] { hullHit, shieldHit } )
		{
			foreach ( var renderer in effect.GetComponentsInChildren<Renderer>( true ) )
			{
				var material = renderer.sharedMaterial;

				parts++;

				if ( ( material == null ) || ( material.shader.name == "Standard" ) || ( material.shader.name == "Particles/Standard Unlit" ) || ( material.renderQueue < 3000 ) )
				{
					drawnWrong.Add( effect.GetType().Name + "/" + renderer.name + " (" + ( ( material == null ) ? "no material" : material.shader.name + ", queue " + material.renderQueue ) + ")" );
				}
			}
		}

		Log( "hitpictures: " + parts + " parts, drawn with the lit or opaque shaders: " + ( ( drawnWrong.Count == 0 ) ? "none" : string.Join( ", ", drawnWrong ) ) );

		Check( "hitpictures: the hull and shield hits are drawn transparent, not with the Standard or untextured particle shaders", ( parts > 0 ) && ( drawnWrong.Count == 0 ), ( drawnWrong.Count == 0 ) ? "all " + parts + " parts" : string.Join( ", ", drawnWrong ) );

		// ---- 2. every particle of a hull hit is gone before the effect is switched off (it cut its debris off in mid air)
		var hullDuration = (float) GetField( hullHit, "m_duration" );
		var longestLife = 0.0f;
		var longestName = "";

		foreach ( var particleSystem in hullHit.GetComponentsInChildren<ParticleSystem>( true ) )
		{
			var life = particleSystem.main.startLifetime.constantMax;

			if ( life > longestLife )
			{
				longestLife = life;
				longestName = particleSystem.name;
			}
		}

		Log( "hitpictures: a hull hit is switched off after " + hullDuration.ToString( "F2" ) + " s; its longest lived particles (" + longestName + ") live " + longestLife.ToString( "F2" ) + " s" );

		Check( "hitpictures: a hull hit's particles are gone before it is switched off", longestLife <= hullDuration, longestName + " live " + longestLife.ToString( "F2" ) + " s, switched off after " + hullDuration.ToString( "F2" ) + " s" );

		// ---- 3. the hits land on the side the shot came from (a shot from the upper left, travelling down and to the right)
		var shipPosition = playerData.m_general.m_coordinates;
		var shotDirection = new Vector3( 1.0f, 0.0f, -1.0f ).normalized;

		hullHit.Play( shipPosition, shotDirection );

		var hullSide = Vector3.Dot( hullHit.transform.position - shipPosition, -shotDirection );

		PlayHitEffect( shieldHit, shipPosition, shotDirection );

		var impact = shieldHit.transform.Find( "Impact" );
		var shieldSide = ( impact != null ) ? Vector3.Dot( impact.position - shipPosition, -shotDirection ) : 0.0f;

		Log( "hitpictures: a shot from the upper left strikes the hull " + hullSide.ToString( "F1" ) + " and the shield bubble " + shieldSide.ToString( "F1" ) + " towards the shooter from the middle of the ship" );

		Check( "hitpictures: a hull hit is on the side of the hull the shot came from", hullSide > 1.0f, "offset towards the shot " + hullSide.ToString( "F1" ) );
		Check( "hitpictures: a shield hit lights the side of the bubble the shot came from", shieldSide > 1.0f, ( impact != null ) ? "offset towards the shot " + shieldSide.ToString( "F1" ) : "no impact point" );

		yield return Frames( 30 );

		// ---- 4. control: making the hit effects does not draw from the game's random numbers
		ForgetEffectMaterials();

		var stateBefore = JsonUtility.ToJson( UnityEngine.Random.state );
		var madeObject = new GameObject( "Probe Hits" );

		madeObject.AddComponent<HullHitEffect>();
		madeObject.AddComponent<ShieldHitEffect>();

		var stateAfter = JsonUtility.ToJson( UnityEngine.Random.state );

		Destroy( madeObject );

		Check( "hitpictures: making the hit effects leaves the game's random numbers alone", stateBefore == stateAfter, ( stateBefore == stateAfter ) ? "Random.state unchanged" : "Random.state changed" );

		// ---- 5. pictures, when there is a graphics device to draw them with
		if ( SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null )
		{
			Log( "hitpictures: no graphics device (-nographics), so no pictures; run probe.ps1 with -Graphics for them" );

			Finish( "scenario=hitpictures parts=" + parts + " drawnWrong=" + drawnWrong.Count + " longestLife=" + longestLife.ToString( "F2" ) + "/" + hullDuration.ToString( "F2" ) + " side=" + hullSide.ToString( "F1" ) + "/" + shieldSide.ToString( "F1" ) + " pictures=0 checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
			yield break;
		}

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "hit-pictures" ) );

		System.IO.Directory.CreateDirectory( folder );

		var camera = Camera.main;
		var frameTimes = new int[] { 1, 3, 6, 9, 13 };

		s_explosionPictures = 0;

		// out in dark hyperspace, with no encounter able to begin
		controller.m_encounterRange = 0.0f;
		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );
		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 30 );

		// game time goes up by a thirtieth of a second a frame, so the pictures are at the same moments in every run
		Time.captureDeltaTime = 1.0f / 30.0f;

		// the shield hit, then the hull hit, each on its own, from a shot from the upper left
		PlayHitEffect( Call( combat, "GetAvailableShieldHit" ), playerData.m_general.m_coordinates, shotDirection );

		yield return HitPictureSeries( camera, folder, "shield_", frameTimes );

		( Call( combat, "GetAvailableHullHit" ) as HullHitEffect ).Play( playerData.m_general.m_coordinates, shotDirection );

		yield return HitPictureSeries( camera, folder, "hull_", frameTimes );

		// and through the game's own way: a hit on raised shields, then one on the hull with the shields down
		playerData.m_playerShip.m_shieldsAreUp = true;
		playerData.m_playerShip.m_shieldPoints = 500;

		combat.ApplyDamageToPlayer( 10, shotDirection );

		yield return HitPictureSeries( camera, folder, "game_shield_", new int[] { 3 } );

		playerData.m_playerShip.m_shieldsAreUp = false;

		combat.ApplyDamageToPlayer( 10, shotDirection );

		yield return HitPictureSeries( camera, folder, "game_hull_", new int[] { 3 } );

		// one at the size the game shows it
		combat.ApplyDamageToPlayer( 10, shotDirection );

		var start = Time.frameCount;

		while ( Time.frameCount - start < 3 )
		{
			yield return null;
		}

		RenderCameraPicture( camera, System.IO.Path.Combine( folder, "game_hull_unzoomed_t0100ms.png" ) );
		s_explosionPictures++;

		Time.captureDeltaTime = 0.0f;

		Log( "hitpictures: " + s_explosionPictures + " pictures in " + folder );

		Finish( "scenario=hitpictures parts=" + parts + " drawnWrong=" + drawnWrong.Count + " longestLife=" + longestLife.ToString( "F2" ) + "/" + hullDuration.ToString( "F2" ) + " side=" + hullSide.ToString( "F1" ) + "/" + shieldSide.ToString( "F1" ) + " pictures=" + s_explosionPictures + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: weapon pictures (what the laser and the missile are drawn with, a paused missile's exhaust, and pictures of them with -Graphics)

	// how many particles a missile's exhaust and smoke have
	static int ExhaustParticles( MissileProjectile missile )
	{
		var count = 0;

		foreach ( var particleSystem in missile.GetComponentsInChildren<ParticleSystem>( true ) )
		{
			count += particleSystem.particleCount;
		}

		return count;
	}

	// draw a picture, zoomed in by the given field of view, at each of the given frames after this one
	static IEnumerator WeaponPictureSeries( Camera camera, string folder, string prefix, int[] frameTimes, float fieldOfView )
	{
		var start = Time.frameCount;

		foreach ( var frameTime in frameTimes )
		{
			while ( Time.frameCount - start < frameTime )
			{
				yield return null;
			}

			RenderZoomedPicture( camera, System.IO.Path.Combine( folder, prefix + "t" + ( frameTime * 1000 / 30 ).ToString( "D4" ) + "ms.png" ), fieldOfView );
			s_explosionPictures++;
		}
	}

	IEnumerator ScenarioWeaponPictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var combat = CombatController.m_instance;
		var controller = SpaceflightController.m_instance;

		var laser = Call( combat, "GetAvailableLaser" ) as LaserBeam;
		var missile = Call( combat, "GetAvailableMissile" ) as MissileProjectile;

		// ---- 1. what the laser and the missile are drawn with: nothing with the lit Standard shader or the untextured opaque particle shader, everything transparent
		var drawnWrong = new List<string>();
		var parts = 0;

		foreach ( var effect in new Component[] { laser, missile } )
		{
			foreach ( var renderer in effect.GetComponentsInChildren<Renderer>( true ) )
			{
				var material = renderer.sharedMaterial;

				parts++;

				if ( ( material == null ) || ( material.shader.name == "Standard" ) || ( material.shader.name == "Particles/Standard Unlit" ) || ( material.renderQueue < 3000 ) )
				{
					drawnWrong.Add( effect.GetType().Name + "/" + renderer.name + " (" + ( ( material == null ) ? "no material" : material.shader.name + ", queue " + material.renderQueue ) + ")" );
				}
			}
		}

		Log( "weaponpictures: " + parts + " parts, drawn with the lit or opaque shaders: " + ( ( drawnWrong.Count == 0 ) ? "none" : string.Join( ", ", drawnWrong ) ) );

		Check( "weaponpictures: the laser and the missile are drawn transparent, not with the Standard or untextured particle shaders", ( parts > 0 ) && ( drawnWrong.Count == 0 ), ( drawnWrong.Count == 0 ) ? "all " + parts + " parts" : string.Join( ", ", drawnWrong ) );

		// ---- 2. a missile stands still while the game is paused, and so does its exhaust (it would pile up where the missile stands)
		var shipTransform = controller.m_playerShip.transform;

		missile.Fire( shipTransform.position + new Vector3( -2000.0f, 0.0f, 2000.0f ), shipTransform, ( position, hit ) => { } );

		yield return Frames( 6 );

		controller.m_gameIsPaused = true;

		yield return Frames( 2 );

		var exhaustAtPause = ExhaustParticles( missile );

		yield return new WaitForSecondsRealtime( 1.0f );

		var exhaustAfterASecond = ExhaustParticles( missile );

		controller.m_gameIsPaused = false;

		yield return Frames( 2 );

		missile.Cancel();

		Log( "weaponpictures: a missile's exhaust had " + exhaustAtPause + " particles when the game was paused and " + exhaustAfterASecond + " a second later" );

		Check( "weaponpictures: a paused missile's exhaust stands still", ( exhaustAtPause > 0 ) && ( exhaustAfterASecond == exhaustAtPause ), exhaustAtPause + " particles at the pause, " + exhaustAfterASecond + " a second later" );

		// ---- 3. control: making the laser and the missile does not draw from the game's random numbers
		ForgetEffectMaterials();

		var stateBefore = JsonUtility.ToJson( UnityEngine.Random.state );
		var madeObject = new GameObject( "Probe Weapons" );

		madeObject.AddComponent<LaserBeam>();
		madeObject.AddComponent<MissileProjectile>();

		var stateAfter = JsonUtility.ToJson( UnityEngine.Random.state );

		Destroy( madeObject );

		Check( "weaponpictures: making the laser and the missile leaves the game's random numbers alone", stateBefore == stateAfter, ( stateBefore == stateAfter ) ? "Random.state unchanged" : "Random.state changed" );

		// ---- 4. pictures, when there is a graphics device to draw them with
		if ( SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null )
		{
			Log( "weaponpictures: no graphics device (-nographics), so no pictures; run probe.ps1 with -Graphics for them" );

			Finish( "scenario=weaponpictures parts=" + parts + " drawnWrong=" + drawnWrong.Count + " exhaust=" + exhaustAtPause + "/" + exhaustAfterASecond + " pictures=0 checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
			yield break;
		}

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "weapon-pictures" ) );

		System.IO.Directory.CreateDirectory( folder );

		var camera = Camera.main;

		s_explosionPictures = 0;

		// out in dark hyperspace, with no encounter able to begin
		controller.m_encounterRange = 0.0f;
		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );
		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 30 );

		// game time goes up by a thirtieth of a second a frame, so the pictures are at the same moments in every run
		Time.captureDeltaTime = 1.0f / 30.0f;

		// an alien laser from the upper left, the colour AlienFiresAtPlayer gives it
		var shooter = shipTransform.position + new Vector3( -150.0f, 0.0f, 110.0f );
		var redLaser = Call( combat, "GetAvailableLaser" ) as LaserBeam;

		redLaser.SetColor( new Color( 1.0f, 0.5f, 0.5f ) );
		redLaser.Fire( shooter, shipTransform.position );

		yield return WeaponPictureSeries( camera, folder, "laser_", new int[] { 1, 3, 6, 9 }, 14.0f );

		yield return Frames( 15 );

		// a plasma bolt, the same way in green
		var plasma = Call( combat, "GetAvailableLaser" ) as LaserBeam;

		plasma.SetColor( new Color( 0.5f, 1.0f, 0.5f ) );
		plasma.Fire( shooter, shipTransform.position );

		yield return WeaponPictureSeries( camera, folder, "plasma_", new int[] { 2 }, 14.0f );

		yield return Frames( 15 );

		// a missile crossing the view from left to right, just above the ship (it flies 500 a second, so it is across in a little over a second)
		var crossing = Call( combat, "GetAvailableMissile" ) as MissileProjectile;

		crossing.Fire( shipTransform.position + new Vector3( -320.0f, 0.0f, 60.0f ), shipTransform.position + new Vector3( 400.0f, 0.0f, 60.0f ), ( position, hit ) => { } );

		yield return WeaponPictureSeries( camera, folder, "missile_", new int[] { 6, 12, 18, 24 }, 22.0f );

		crossing.Cancel();

		yield return Frames( 5 );

		// one at the size the game shows it
		var unzoomed = Call( combat, "GetAvailableMissile" ) as MissileProjectile;

		unzoomed.Fire( shipTransform.position + new Vector3( -320.0f, 0.0f, 60.0f ), shipTransform.position + new Vector3( 400.0f, 0.0f, 60.0f ), ( position, hit ) => { } );

		var start = Time.frameCount;

		while ( Time.frameCount - start < 15 )
		{
			yield return null;
		}

		var laserBeside = Call( combat, "GetAvailableLaser" ) as LaserBeam;

		laserBeside.SetColor( new Color( 1.0f, 0.5f, 0.5f ) );
		laserBeside.Fire( shooter, shipTransform.position );

		yield return null;

		RenderCameraPicture( camera, System.IO.Path.Combine( folder, "unzoomed_t0500ms.png" ) );
		s_explosionPictures++;

		unzoomed.Cancel();

		Time.captureDeltaTime = 0.0f;

		Log( "weaponpictures: " + s_explosionPictures + " pictures in " + folder );

		Finish( "scenario=weaponpictures parts=" + parts + " drawnWrong=" + drawnWrong.Count + " exhaust=" + exhaustAtPause + "/" + exhaustAfterASecond + " pictures=" + s_explosionPictures + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: transporter pictures (what a pickup's transporter effect is drawn with, what it does to the object, and pictures of it with -Graphics)

	// render the main camera into a PNG with the camera covering the whole picture (with its own rect, an image effect between the opaque and the transparent objects puts the transparent ones in the wrong place in the picture)
	static void RenderWholeFramePicture( Camera camera, string path )
	{
		var rect = camera.rect;

		camera.rect = new Rect( 0.0f, 0.0f, 1.0f, 1.0f );

		RenderCameraPicture( camera, path );

		camera.rect = rect;
	}

	// the renderers of a transporter effect: those under the object that are not its own, and those of a shimmer object of its own in the world
	static List<Renderer> TransporterRenderers( GameObject pickedUp, List<Renderer> objectRenderers )
	{
		var renderers = new List<Renderer>();

		if ( pickedUp != null )
		{
			foreach ( var renderer in pickedUp.GetComponentsInChildren<Renderer>( true ) )
			{
				if ( !objectRenderers.Contains( renderer ) )
				{
					renderers.Add( renderer );
				}
			}
		}

		var shimmer = GameObject.Find( "Transporter Shimmer" );

		if ( shimmer != null )
		{
			renderers.AddRange( shimmer.GetComponentsInChildren<Renderer>( true ) );
		}

		return renderers;
	}

	IEnumerator ScenarioTransporterPictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

		EnsureCrew();

		// ---- into the terrain vehicle on planet 90, the way the terrain scenario does it
		yield return EnterOrbit( 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		if ( playerData.m_general.m_location != PD_General.Location.Disembarked )
		{
			Finish( "scenario=transporterpictures abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainGrid = controller.m_disembarked.m_terrainGrid;
		var terrainVehicle = controller.m_terrainVehicle;
		var deposits = terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true );

		if ( deposits.Length < 1 )
		{
			Finish( "scenario=transporterpictures abort: the planet has no deposits", 2 );
			yield break;
		}

		// one deposit in front of the terrain vehicle, every other deposit out of reach
		var near = deposits[ 0 ];

		foreach ( var other in deposits )
		{
			if ( ( other != near ) && ( Vector3.Distance( other.transform.position, terrainVehicle.transform.position ) < 60.0f ) )
			{
				other.transform.position += Vector3.right * 1000.0f;
			}
		}

		// in front of the terrain vehicle as the camera sees it (up the screen), at the same height
		var screenUp = Camera.main.transform.up;
		var upTheScreen = new Vector3( screenUp.x, 0.0f, screenUp.z ).normalized;

		near.transform.position = terrainVehicle.transform.position + upTheScreen * 7.0f;

		var pickedUp = near.gameObject;
		var objectRenderers = new List<Renderer>( pickedUp.GetComponentsInChildren<Renderer>( true ) );
		var size = 0.0f;
		var rendererNames = new List<string>();

		foreach ( var renderer in objectRenderers )
		{
			size = Mathf.Max( size, renderer.bounds.size.magnitude );
			rendererNames.Add( renderer.name + " at " + renderer.bounds.center.ToString( "F1" ) + " size " + renderer.bounds.size.ToString( "F1" ) );
		}

		Log( "transporterpictures: terrain vehicle at " + terrainVehicle.transform.position.ToString( "F1" ) + ", deposit at " + near.transform.position.ToString( "F1" ) + ", its renderers: " + string.Join( "; ", rendererNames ) );

		yield return Frames( 5 );

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "transporter-pictures" ) );
		var camera = Camera.main;

		if ( hasGraphics )
		{
			System.IO.Directory.CreateDirectory( folder );

			// game time goes up by a thirtieth of a second a frame, so the pictures are at the same moments in every run
			Time.captureDeltaTime = 1.0f / 30.0f;

			RenderWholeFramePicture( camera, System.IO.Path.Combine( folder, "t0000ms.png" ) );
		}

		// ---- pick it up: the transporter effect takes a second and a half, then the object is gone
		var errorsBefore = s_errorCount;

		new TVCargoButton().Execute();

		var start = Time.frameCount;
		var pictures = 0;
		var drawnWrong = new List<string>();
		var parts = 0;
		var objectQueue = -1;

		foreach ( var frameTime in new int[] { 3, 9, 18, 27, 36, 43 } )
		{
			while ( Time.frameCount - start < frameTime )
			{
				yield return null;
			}

			// ---- 1. what the effect is drawn with, and what it did to the object's own materials (looked at once, while it plays)
			if ( frameTime == 9 )
			{
				foreach ( var renderer in TransporterRenderers( pickedUp, objectRenderers ) )
				{
					var material = renderer.sharedMaterial;

					parts++;

					if ( ( material == null ) || ( material.shader.name == "Standard" ) || ( material.shader.name == "Particles/Standard Unlit" ) || ( material.renderQueue < 3000 ) )
					{
						drawnWrong.Add( renderer.name + " (" + ( ( material == null ) ? "no material" : material.shader.name + ", queue " + material.renderQueue ) + ")" );
					}
				}

				foreach ( var renderer in objectRenderers )
				{
					if ( ( renderer != null ) && ( renderer.sharedMaterial != null ) )
					{
						objectQueue = Mathf.Max( objectQueue, renderer.sharedMaterial.renderQueue );
					}
				}
			}

			if ( hasGraphics )
			{
				RenderWholeFramePicture( camera, System.IO.Path.Combine( folder, "t" + ( frameTime * 1000 / 30 ).ToString( "D4" ) + "ms.png" ) );
				pictures++;
			}
		}

		Time.captureDeltaTime = 0.0f;

		// the effect runs on game time, which headless runs can make run slower than real time: wait for 3 s of it (and 20 s of real time at the most)
		var gameWaitEnd = Time.time + 3.0f;
		var realWaitEnd = Time.realtimeSinceStartup + 20.0f;

		while ( ( pickedUp != null ) && ( Time.time < gameWaitEnd ) && ( Time.realtimeSinceStartup < realWaitEnd ) )
		{
			yield return null;
		}

		var goneAfter = Time.frameCount - start;
		var errorsDuring = s_errorCount - errorsBefore;


		Log( "transporterpictures: a deposit " + size.ToString( "F2" ) + " across picked up: " + parts + " effect parts, drawn with the lit or opaque shaders: " + ( ( drawnWrong.Count == 0 ) ? "none" : string.Join( ", ", drawnWrong ) ) + "; the object's materials in queue " + objectQueue + " while it plays; the object gone " + ( pickedUp == null ) + " after " + goneAfter + " frames" );

		Check( "transporterpictures: the transporter effect is drawn transparent, not with the Standard or untextured particle shaders", ( parts > 0 ) && ( drawnWrong.Count == 0 ), ( drawnWrong.Count == 0 ) ? "all " + parts + " parts" : string.Join( ", ", drawnWrong ) );
		Check( "transporterpictures: the object keeps its own rendering while it is beamed up (no depth writes switched off)", ( objectQueue > 0 ) && ( objectQueue < 3000 ), "queue " + objectQueue );
		Check( "transporterpictures: the object is gone once the effect is over (control)", pickedUp == null, "gone " + ( pickedUp == null ) );
		Check( "transporterpictures: no error is logged while the effect plays", errorsDuring == 0, errorsDuring + " errors" + ( ( errorsDuring > 0 ) ? ", the last: " + s_lastError : "" ) );

		// ---- 2. control: the effect does not draw from the game's random numbers
		var probeObject = GameObject.CreatePrimitive( PrimitiveType.Cube );

		probeObject.transform.position = terrainVehicle.transform.position + Vector3.right * 30.0f;

		var stateBefore = JsonUtility.ToJson( UnityEngine.Random.state );

		probeObject.AddComponent<TransporterEffect>().Play();

		var stateAfter = JsonUtility.ToJson( UnityEngine.Random.state );

		Check( "transporterpictures: the transporter effect leaves the game's random numbers alone", stateBefore == stateAfter, ( stateBefore == stateAfter ) ? "Random.state unchanged" : "Random.state changed" );

		yield return new WaitForSecondsRealtime( 2.0f );

		Log( "transporterpictures: " + pictures + " pictures" + ( hasGraphics ? " in " + folder : " (no graphics device; run probe.ps1 with -Graphics for them)" ) );

		Finish( "scenario=transporterpictures parts=" + parts + " drawnWrong=" + drawnWrong.Count + " objectQueue=" + objectQueue + " gone=" + ( pickedUp == null ) + " errors=" + errorsDuring + " pictures=" + pictures + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: debris pictures (what the wreck of an alien ship and of the player ship are drawn with, and pictures of them with -Graphics)

	// what the renderers under an object are drawn with: name, shader, texture, colour
	static string DescribeRenderers( GameObject root )
	{
		var parts = new List<string>();

		if ( root == null )
		{
			return "nothing";
		}

		foreach ( var renderer in root.GetComponentsInChildren<Renderer>( true ) )
		{
			var material = renderer.sharedMaterial;

			parts.Add( renderer.name + ( renderer.enabled && renderer.gameObject.activeInHierarchy ? "" : " (off)" ) + ": " + ( ( material == null ) ? "no material" : material.shader.name + ( ( material.mainTexture != null ) ? " textured " + material.mainTexture.name : " untextured" ) + ( material.HasProperty( "_Color" ) ? " colour " + material.color.ToString( "F2" ) : "" ) + ( material.HasProperty( "_EmissionColor" ) ? " emission " + material.GetColor( "_EmissionColor" ).ToString( "F2" ) : "" ) ) + " bounds " + renderer.bounds.size.ToString( "F1" ) );
		}

		return parts.Count + " renderers: " + string.Join( "; ", parts );
	}

	// render a picture of a point from a camera of its own, at the game camera's angle and the given distance (the whole frame)
	static void RenderFromAbove( Vector3 target, float distance, string path )
	{
		var main = Camera.main;
		var cameraObject = new GameObject( "Probe Camera" );
		var camera = cameraObject.AddComponent<Camera>();

		camera.CopyFrom( main );
		camera.rect = new Rect( 0.0f, 0.0f, 1.0f, 1.0f );
		camera.enabled = false;
		camera.transform.rotation = main.transform.rotation;
		camera.transform.position = target - main.transform.forward * distance;

		RenderCameraPicture( camera, path );

		UnityEngine.Object.Destroy( cameraObject );
	}

	// the renderers of a wreck that are still drawn with the blank white material the debris models were imported with
	static List<string> BlankRenderers( GameObject root )
	{
		var blank = new List<string>();

		if ( root == null )
		{
			return blank;
		}

		foreach ( var renderer in root.GetComponentsInChildren<Renderer>( true ) )
		{
			foreach ( var material in renderer.sharedMaterials )
			{
				if ( ( material == null ) || ( ( material.shader.name == "Standard" ) && ( material.mainTexture == null ) ) )
				{
					blank.Add( renderer.name );
					break;
				}
			}
		}

		return blank;
	}

	IEnumerator ScenarioDebrisPictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var combat = CombatController.m_instance;
		var controller = SpaceflightController.m_instance;
		var hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "debris-pictures" ) );
		var camera = Camera.main;

		if ( hasGraphics )
		{
			System.IO.Directory.CreateDirectory( folder );
		}

		playerData.m_playerShip.m_armorPoints = 100000;

		// ---- 1. an alien ship destroyed in an encounter: its wreck takes its place
		var speminId = FindEncounter( 1, 6, 3, 0 );

		// spemin scouts
		ForceVessel( speminId, 2 );

		EnterEncounter( speminId );

		yield return Frames( 10 );

		var target = FirstLivingAlien();

		if ( target < 0 )
		{
			Finish( "scenario=debrispictures abort: no alien ship in the encounter", 2 );
			yield break;
		}

		var alienShips = controller.m_encounter.m_pdEncounter.GetAlienShipList();
		var targetShip = alienShips[ target ];

		// every other alien ship far away, the target still in front of the player ship (up the screen)
		var screenUp = camera.transform.up;
		var upTheScreen = new Vector3( screenUp.x, 0.0f, screenUp.z ).normalized;
		var wreckPosition = playerData.m_general.m_coordinates + upTheScreen * 70.0f;

		for ( var i = 0; i < 30; i++ )
		{
			foreach ( var alienShip in alienShips )
			{
				if ( alienShip.m_addedToEncounter && !alienShip.m_isDead )
				{
					alienShip.m_coordinates = ( alienShip == targetShip ) ? wreckPosition : playerData.m_general.m_coordinates + Vector3.right * 3000.0f;
					alienShip.m_targetCoordinates = alienShip.m_coordinates;
				}
			}

			yield return null;
		}

		var shipModel = Call( controller.m_encounter, "GetAlienShipModel", target ) as GameObject;

		Log( "debrispictures: the spemin scout before: " + DescribeRenderers( shipModel ) );

		if ( hasGraphics )
		{
			RenderFromAbove( wreckPosition, 260.0f, System.IO.Path.Combine( folder, "alien_ship.png" ) );
		}

		// destroyed the way the player's weapons destroy it
		var errorsBefore = s_errorCount;

		Call( combat, "ApplyDamageToAlien", target, 100000 );

		// the explosion is over after 2.2 s
		yield return new WaitForSecondsRealtime( 2.6f );

		var wreck = Call( controller.m_encounter, "GetAlienShipModel", target ) as GameObject;
		var alienBlank = BlankRenderers( wreck );

		Log( "debrispictures: the spemin scout's wreck: " + DescribeRenderers( wreck ) );

		if ( hasGraphics )
		{
			RenderFromAbove( wreckPosition, 260.0f, System.IO.Path.Combine( folder, "alien_wreck_hot.png" ) );
		}

		yield return new WaitForSecondsRealtime( 4.5f );

		if ( hasGraphics )
		{
			RenderFromAbove( wreckPosition, 260.0f, System.IO.Path.Combine( folder, "alien_wreck_cold.png" ) );
		}

		Check( "debrispictures: an alien ship's wreck is drawn with the ship's own materials, not blank white ones", ( wreck != null ) && ( alienBlank.Count == 0 ), ( wreck == null ) ? "no wreck" : ( alienBlank.Count == 0 ) ? "all its renderers" : alienBlank.Count + " blank: " + string.Join( ", ", alienBlank ) );

		// ---- 2. the player ship destroyed in hyperspace, the way F10 does it: its wreck is left where it was
		LeaveEncounter();

		yield return Frames( 5 );

		controller.m_encounterRange = 0.0f;
		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );
		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 30 );

		var shipPosition = playerData.m_general.m_coordinates;

		if ( hasGraphics )
		{
			RenderFromAbove( shipPosition, 220.0f, System.IO.Path.Combine( folder, "player_ship.png" ) );
		}

		playerData.m_playerShip.m_shieldPoints = 0;
		playerData.m_playerShip.m_armorPoints = 0;
		combat.ApplyDamageToPlayer( 1, Vector3.forward );

		yield return new WaitForSecondsRealtime( 2.6f );

		GameObject playerWreck = null;

		foreach ( var tumble in UnityEngine.Object.FindObjectsByType<DebrisTumble>( FindObjectsSortMode.None ) )
		{
			if ( ( tumble.transform.parent == null ) && ( Vector3.Distance( tumble.transform.position, playerData.m_general.m_coordinates ) < 200.0f ) )
			{
				playerWreck = tumble.gameObject;
			}
		}

		var playerBlank = BlankRenderers( playerWreck );

		Log( "debrispictures: the player ship's wreck: " + DescribeRenderers( playerWreck ) + ( ( playerWreck != null ) ? " at scale " + playerWreck.transform.localScale.ToString( "F3" ) : "" ) );

		if ( hasGraphics )
		{
			RenderFromAbove( shipPosition, 220.0f, System.IO.Path.Combine( folder, "player_wreck_hot.png" ) );
			RenderCameraPicture( camera, System.IO.Path.Combine( folder, "player_wreck_game_view.png" ) );
		}

		yield return new WaitForSecondsRealtime( 4.5f );

		if ( hasGraphics )
		{
			RenderFromAbove( shipPosition, 220.0f, System.IO.Path.Combine( folder, "player_wreck_cold.png" ) );
		}

		var errorsDuring = s_errorCount - errorsBefore;

		Check( "debrispictures: the player ship's wreck is drawn with the ship's own materials, not blank white ones", ( playerWreck != null ) && ( playerBlank.Count == 0 ), ( playerWreck == null ) ? "no wreck" : ( playerBlank.Count == 0 ) ? "all its renderers" : playerBlank.Count + " blank: " + string.Join( ", ", playerBlank ) );
		Check( "debrispictures: no error is logged while the wrecks are made and cool", errorsDuring == 0, errorsDuring + " errors" + ( ( errorsDuring > 0 ) ? ", the last: " + s_lastError : "" ) );

		Finish( "scenario=debrispictures alienBlank=" + alienBlank.Count + " playerBlank=" + playerBlank.Count + " errors=" + errorsDuring + " pictures=" + ( hasGraphics ? 7 : 0 ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: atmosphere pictures (what a planet's atmosphere is drawn with, in the star system and in orbit, and pictures of it with -Graphics)

	// what an atmosphere object is drawn with
	static string DescribeAtmosphere( GameObject atmosphere )
	{
		if ( atmosphere == null )
		{
			return "no atmosphere object";
		}

		var renderer = atmosphere.GetComponent<MeshRenderer>();
		var material = ( renderer != null ) ? renderer.sharedMaterial : null;

		if ( material == null )
		{
			return "no material";
		}

		var text = "active " + atmosphere.activeInHierarchy + ", " + material.name + " (" + material.shader.name + "), queue " + material.renderQueue + ", scale " + atmosphere.transform.lossyScale.x.ToString( "F1" );

		foreach ( var name in new string[] { "SF_AlbedoColor", "SF_EmissiveColor" } )
		{
			if ( material.HasProperty( name ) )
			{
				text += ", " + name + " " + material.GetColor( name ).ToString( "F2" );
			}
		}

		foreach ( var name in new string[] { "SF_BlendSrc", "SF_BlendDst", "SF_CullMode", "SF_ZWriteOn", "SF_Density", "SF_NormalMapStrength" } )
		{
			if ( material.HasProperty( name ) )
			{
				text += ", " + name + " " + material.GetFloat( name ).ToString( "F2" );
			}
		}

		if ( material.HasProperty( "SF_AlbedoMap" ) && ( material.GetTexture( "SF_AlbedoMap" ) != null ) )
		{
			text += ", albedo map " + material.GetTexture( "SF_AlbedoMap" ).name;
		}

		return text;
	}

	// how an atmosphere sits on its planet: the halo room (the quad's half size over the planet's radius), the quad's height over the planet's top (in planet radii), and the brightest channel of its colour
	static void MeasureAtmosphere( GameObject atmosphere, GameObject planetModel, out float room, out float lift, out float brightest )
	{
		room = 0.0f;
		lift = -1.0f;
		brightest = 0.0f;

		if ( ( atmosphere == null ) || ( planetModel == null ) )
		{
			return;
		}

		var planetMesh = planetModel.GetComponentInChildren<MeshFilter>();
		var planetRenderer = planetModel.GetComponentInChildren<Renderer>();
		var quadMesh = atmosphere.GetComponent<MeshFilter>();
		var quadRenderer = atmosphere.GetComponent<MeshRenderer>();

		if ( ( planetMesh == null ) || ( planetRenderer == null ) || ( quadMesh == null ) || ( quadRenderer == null ) )
		{
			return;
		}

		var planetRadius = planetMesh.sharedMesh.bounds.extents.x * planetMesh.transform.lossyScale.x;
		var quadExtents = quadMesh.sharedMesh.bounds.extents;
		var quadHalfSize = Mathf.Max( quadExtents.x, Mathf.Max( quadExtents.y, quadExtents.z ) ) * atmosphere.transform.lossyScale.x;

		room = quadHalfSize / planetRadius;
		lift = ( atmosphere.transform.position.y - ( planetRenderer.bounds.center.y + planetRadius ) ) / planetRadius;

		var material = quadRenderer.sharedMaterial;

		if ( ( material != null ) && material.HasProperty( "SF_AlbedoColor" ) )
		{
			var color = material.GetColor( "SF_AlbedoColor" );

			brightest = Mathf.Max( color.r, Mathf.Max( color.g, color.b ) );
		}
	}

	IEnumerator ScenarioAtmospherePictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "atmosphere-pictures" ) );
		var camera = Camera.main;

		if ( hasGraphics )
		{
			System.IO.Directory.CreateDirectory( folder );
		}

		// the planets of the star system with an atmosphere, the first of each colour
		var star = gameData.m_starList[ playerData.m_general.m_currentStarId ];
		var chosen = new List<GD_Planet>();
		var colours = new List<Color>();

		foreach ( var planet in star.GetPlanetList() )
		{
			if ( ( planet != null ) && planet.HasAtmosphere() && !colours.Contains( planet.GetAtmosphereColor() ) )
			{
				chosen.Add( planet );
				colours.Add( planet.GetAtmosphereColor() );
			}
		}

		Log( "atmospherepictures: " + chosen.Count + " planets with an atmosphere of their own colour in star " + star.m_id );

		var errorsBefore = s_errorCount;
		var noRoom = new List<string>();
		var hidden = new List<string>();
		var dim = new List<string>();

		System.Action<string, GameObject, GameObject> measure = ( where, atmosphere, planetModel ) =>
		{
			float room, lift, brightest;

			MeasureAtmosphere( atmosphere, planetModel, out room, out lift, out brightest );

			Log( "atmospherepictures: " + where + ": the quad is " + room.ToString( "F3" ) + " planet radii across from the middle, " + lift.ToString( "F3" ) + " planet radii above the planet's top, colour at most " + brightest.ToString( "F2" ) );

			// the halo reaches 12% of the radius beyond the planet's edge: the quad has to be bigger than that
			if ( room < 1.12f )
			{
				noRoom.Add( where + " " + room.ToString( "F2" ) );
			}

			// below the planet's top, the planet hides the haze over it
			if ( lift < 0.0f )
			{
				hidden.Add( where + " " + lift.ToString( "F2" ) );
			}

			// a dark colour adds next to nothing to what is behind it
			if ( brightest < 0.5f )
			{
				dim.Add( where + " " + brightest.ToString( "F2" ) );
			}
		};

		// ---- 1. in the star system: each planet from a camera of its own over it
		foreach ( var planet in chosen )
		{
			var planetController = controller.m_starSystem.GetPlanetController( planet.m_id );

			Log( "atmospherepictures: planet " + planet.m_id + " (atmosphere colour " + planet.GetAtmosphereColor().ToString( "F2" ) + ", density " + planet.GetAtmosphereDensity().ToString( "F2" ) + ") in the star system: " + DescribeAtmosphere( ( planetController != null ) ? planetController.m_planetAtmosphere : null ) );

			if ( planetController != null )
			{
				measure( "planet " + planet.m_id + " in the star system", planetController.m_planetAtmosphere, planetController.m_planetModel );
			}

			if ( hasGraphics && ( planetController != null ) )
			{
				var size = planetController.m_planetModel.transform.lossyScale.x;

				RenderFromAbove( planetController.transform.position, size * 4.0f, System.IO.Path.Combine( folder, "system_planet" + planet.m_id + ".png" ) );
			}
		}

		// ---- 2. in orbit around each of them
		foreach ( var planet in chosen )
		{
			yield return EnterOrbit( planet.m_id );

			yield return Frames( 30 );

			Log( "atmospherepictures: planet " + planet.m_id + " in orbit: " + DescribeAtmosphere( controller.m_inOrbit.m_planetAtmosphere ) );

			measure( "planet " + planet.m_id + " in orbit", controller.m_inOrbit.m_planetAtmosphere, controller.m_inOrbit.m_planetModel.gameObject );

			if ( hasGraphics )
			{
				RenderCameraPicture( camera, System.IO.Path.Combine( folder, "orbit_planet" + planet.m_id + ".png" ) );
			}

			controller.SwitchLocation( PD_General.Location.StarSystem );

			yield return Frames( 10 );
		}

		var errorsDuring = s_errorCount - errorsBefore;

		Check( "atmospherepictures: an atmosphere leaves room for a halo beyond the planet's edge", ( chosen.Count > 0 ) && ( noRoom.Count == 0 ), ( noRoom.Count == 0 ) ? "all " + ( chosen.Count * 2 ) : string.Join( ", ", noRoom ) );
		Check( "atmospherepictures: an atmosphere is in front of its planet, so the haze over the planet is not hidden in it", ( chosen.Count > 0 ) && ( hidden.Count == 0 ), ( hidden.Count == 0 ) ? "all " + ( chosen.Count * 2 ) : string.Join( ", ", hidden ) );
		Check( "atmospherepictures: an atmosphere of a dark colour is drawn bright enough to show", ( chosen.Count > 0 ) && ( dim.Count == 0 ), ( dim.Count == 0 ) ? "all " + ( chosen.Count * 2 ) : string.Join( ", ", dim ) );
		Check( "atmospherepictures: no error is logged while the atmospheres are set up", errorsDuring == 0, errorsDuring + " errors" + ( ( errorsDuring > 0 ) ? ", the last: " + s_lastError : "" ) );

		Finish( "scenario=atmospherepictures planets=" + chosen.Count + " noRoom=" + noRoom.Count + " hidden=" + hidden.Count + " dim=" + dim.Count + " errors=" + errorsDuring + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: starfield pictures (the stars' sizes, colours, glints and twinkle, and the game's random numbers they take)

	// render the main camera into a texture and read back the brightness of each pixel of its space view (for a comparison of two moments)
	static float[] RenderBrightness( Camera camera )
	{
		var renderTexture = new RenderTexture( 960, 540, 24, RenderTextureFormat.ARGB32 );
		var picture = new Texture2D( 960, 540, TextureFormat.RGB24, false );

		camera.targetTexture = renderTexture;
		camera.Render();
		camera.targetTexture = null;

		var active = RenderTexture.active;

		RenderTexture.active = renderTexture;
		picture.ReadPixels( new Rect( 0, 0, 960, 540 ), 0, 0 );
		picture.Apply();
		RenderTexture.active = active;

		var pixels = picture.GetPixels();
		var brightness = new float[ pixels.Length ];

		for ( var i = 0; i < pixels.Length; i++ )
		{
			brightness[ i ] = Mathf.Max( pixels[ i ].r, Mathf.Max( pixels[ i ].g, pixels[ i ].b ) );
		}

		UnityEngine.Object.Destroy( picture );
		renderTexture.Release();
		UnityEngine.Object.Destroy( renderTexture );

		return brightness;
	}

	IEnumerator ScenarioStarfieldPictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
		var camera = Camera.main;

		// out in dark hyperspace, where there is nothing but the stars, with no encounter able to begin
		controller.m_encounterRange = 0.0f;
		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );
		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 30 );

		var starfield = UnityEngine.Object.FindFirstObjectByType<InfiniteStarfield>();

		if ( starfield == null )
		{
			Finish( "scenario=starfieldpictures abort: no starfield", 2 );
			yield break;
		}

		var errorsBefore = s_errorCount;

		// ---- 1. control: the starfield takes four of the game's random numbers for each star, as it always has (the planets and the encounters draw from the same numbers)
		UnityEngine.Random.InitState( 4321 );

		starfield.InitStarField();

		var stateAfterTheStarfield = JsonUtility.ToJson( UnityEngine.Random.state );

		yield return Frames( 2 );

		var particleSystems = GetField( starfield, "m_particleSystems" ) as ParticleSystem[];
		var starCount = 0;

		foreach ( var layer in GetField( starfield, "m_particles" ) as ParticleSystem.Particle[][] )
		{
			starCount += layer.Length;
		}

		UnityEngine.Random.InitState( 4321 );

		for ( var i = 0; i < starCount * 4; i++ )
		{
			var draw = UnityEngine.Random.value;
		}

		var stateAfterFourEach = JsonUtility.ToJson( UnityEngine.Random.state );

		Check( "starfieldpictures: the starfield takes four of the game's random numbers for each star (control)", stateAfterTheStarfield == stateAfterFourEach, starCount + " stars, " + ( ( stateAfterTheStarfield == stateAfterFourEach ) ? "the same state as after " + ( starCount * 4 ) + " draws" : "a different state" ) );

		// ---- 2. the stars of a layer differ in size and colour, a few have a glint, and the shader gets their random numbers for the twinkle
		var flatLayers = new List<string>();
		var glinting = 0;
		var twinkleStreams = 0;

		for ( var l = 0; l < particleSystems.Length; l++ )
		{
			var particles = new ParticleSystem.Particle[ particleSystems[ l ].particleCount ];

			particleSystems[ l ].GetParticles( particles );

			var smallest = float.MaxValue;
			var largest = 0.0f;
			var colours = new HashSet<Color32>();

			foreach ( var particle in particles )
			{
				smallest = Mathf.Min( smallest, particle.startSize );
				largest = Mathf.Max( largest, particle.startSize );
				colours.Add( particle.startColor );

				if ( particle.startColor.a > 0 && particle.startColor.a < 255 )
				{
					glinting++;
				}
			}

			if ( ( particles.Length == 0 ) || ( largest < smallest * 2.0f ) || ( colours.Count < 4 ) )
			{
				flatLayers.Add( "layer " + l + " (" + particles.Length + " stars, sizes " + smallest.ToString( "F0" ) + " to " + largest.ToString( "F0" ) + ", " + colours.Count + " colours)" );
			}

			var streams = new List<ParticleSystemVertexStream>();

			particleSystems[ l ].GetComponent<ParticleSystemRenderer>().GetActiveVertexStreams( streams );

			if ( streams.Contains( ParticleSystemVertexStream.StableRandomXY ) )
			{
				twinkleStreams++;
			}
		}

		var glintShare = ( starCount > 0 ) ? glinting / ( float ) starCount : 0.0f;

		Log( "starfieldpictures: " + particleSystems.Length + " layers, " + starCount + " stars; layers whose stars are all alike: " + ( ( flatLayers.Count == 0 ) ? "none" : string.Join( ", ", flatLayers ) ) + "; glinting " + glinting + " (" + ( glintShare * 100.0f ).ToString( "F1" ) + "%); layers with the twinkle's random numbers " + twinkleStreams );

		Check( "starfieldpictures: the stars of each layer differ in size and colour", ( particleSystems.Length > 0 ) && ( flatLayers.Count == 0 ), ( flatLayers.Count == 0 ) ? "all " + particleSystems.Length + " layers" : string.Join( ", ", flatLayers ) );
		Check( "starfieldpictures: a few bright stars have a glint", ( glintShare > 0.01f ) && ( glintShare < 0.1f ), ( glintShare * 100.0f ).ToString( "F1" ) + "% of the stars" );
		Check( "starfieldpictures: the star shader gets each star's random numbers for its twinkle", ( particleSystems.Length > 0 ) && ( twinkleStreams == particleSystems.Length ), twinkleStreams + " of " + particleSystems.Length + " layers" );

		// ---- 3. pictures, and the twinkle seen: the same view a third of a second apart
		var twinkled = -1;
		var starPixels = 0;

		if ( hasGraphics )
		{
			var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "starfield-pictures" ) );

			System.IO.Directory.CreateDirectory( folder );

			Time.captureDeltaTime = 1.0f / 30.0f;

			RenderCameraPicture( camera, System.IO.Path.Combine( folder, "hyperspace.png" ) );
			RenderZoomedPicture( camera, System.IO.Path.Combine( folder, "hyperspace_zoomed.png" ), 10.0f );

			var first = RenderBrightness( camera );

			var start = Time.frameCount;

			while ( Time.frameCount - start < 10 )
			{
				yield return null;
			}

			var second = RenderBrightness( camera );

			Time.captureDeltaTime = 0.0f;

			twinkled = 0;

			for ( var i = 0; i < first.Length; i++ )
			{
				if ( Mathf.Max( first[ i ], second[ i ] ) > 0.15f )
				{
					starPixels++;

					if ( Mathf.Abs( first[ i ] - second[ i ] ) > 0.06f )
					{
						twinkled++;
					}
				}
			}

			Log( "starfieldpictures: of " + starPixels + " star pixels, " + twinkled + " changed brightness in a third of a second (the camera did not move); pictures in " + folder );

			Check( "starfieldpictures: stars twinkle", ( starPixels > 0 ) && ( twinkled > starPixels / 20 ), twinkled + " of " + starPixels + " star pixels changed" );
		}

		var errorsDuring = s_errorCount - errorsBefore;

		Check( "starfieldpictures: no error is logged while the starfield is made and drawn", errorsDuring == 0, errorsDuring + " errors" + ( ( errorsDuring > 0 ) ? ", the last: " + s_lastError : "" ) );

		Finish( "scenario=starfieldpictures stars=" + starCount + " flatLayers=" + flatLayers.Count + " glint=" + ( glintShare * 100.0f ).ToString( "F1" ) + "% twinkleStreams=" + twinkleStreams + "/" + particleSystems.Length + " twinkled=" + twinkled + "/" + starPixels + " errors=" + errorsDuring + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: what the displays cost every frame (the status display, the terrain vehicle display)

	// something for the control measurement to hold on to
	static byte[] s_garbage;

	// how many calls a measurement is made of. The heap in use is only known to the nearest block of a few kilobytes,
	// so it takes many calls for the number to mean something: one byte per call is 20 kilobytes
	const int c_meterCalls = 20000;

	// how many bytes of memory a number of calls take from the heap (0 if they take none, -1 if it could not be measured). The heap in use
	// is read before and after. If the garbage collector ran in between, the number says nothing, and it is measured again
	static long Allocated( Action call, int count )
	{
		// once before measuring, so that whatever is only done the first time has been done
		call();

		for ( var attempt = 0; attempt < 5; attempt++ )
		{
			var collections = GC.CollectionCount( 0 );
			var before = GC.GetTotalMemory( false );

			for ( var i = 0; i < count; i++ )
			{
				call();
			}

			var bytes = GC.GetTotalMemory( false ) - before;

			if ( ( GC.CollectionCount( 0 ) == collections ) && ( bytes >= 0 ) )
			{
				return bytes;
			}
		}

		return -1;
	}

	// the measurement itself has to be shown to work: an array of 256 bytes per call must come out as at least 256 bytes per call
	static long AllocatedByTheControl()
	{
		return Allocated( () => { s_garbage = new byte[ 256 ]; }, c_meterCalls ) / c_meterCalls;
	}

	IEnumerator ScenarioPerFrame()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;

		EnsureCrew();

		// ---- 1. the status display, 100 frames in which nothing changes (a laser cannon on board, so that the weapons line has something to say)
		var statusDisplay = controller.m_displayController.m_statusDisplay;

		ship.m_laserCannonClass = 1;

		controller.m_displayController.ChangeDisplay( statusDisplay );

		yield return Frames( 5 );

		var controlBytes = AllocatedByTheControl();

		Log( "perframe: control: an array of 256 bytes per call measures as " + controlBytes + " bytes per call" );

		Check( "perframe: the measurement sees memory being taken", controlBytes >= 256, controlBytes + " bytes per call for an array of 256 bytes" );

		var statusText = statusDisplay.m_values.text;
		var statusBytes = Allocated( statusDisplay.Update, c_meterCalls ) / c_meterCalls;
		var statusTextAfter = statusDisplay.m_values.text;

		// it still has to follow the ship: half the armor gone, the shields up, and back
		var armorBefore = ship.m_armorPoints;

		ship.m_armorPoints = ship.GetMaximumArmorPoints() / 2;

		statusDisplay.Update();

		var damagedText = statusDisplay.m_values.text;

		ship.m_armorPoints = armorBefore;
		ship.m_weaponsAreArmed = !ship.m_weaponsAreArmed;

		statusDisplay.Update();

		var armedText = statusDisplay.m_values.text;

		ship.m_weaponsAreArmed = !ship.m_weaponsAreArmed;

		statusDisplay.Update();

		var statusTextAtTheEnd = statusDisplay.m_values.text;

		Log( "perframe: status display: a frame in which nothing changes takes " + statusBytes + " bytes. Text: " + statusText.Replace( '\n', '/' ) );
		Log( "perframe: status display with half the armor gone: " + damagedText.Replace( '\n', '/' ) );

		Check( "perframe: the status display takes no memory while nothing changes", ( statusBytes == 0 ) && ( statusText == statusTextAfter ) && ( statusText.Length > 20 ), statusBytes + " bytes per frame" );
		Check( "perframe: the status display still shows what changes", damagedText.Contains( "Moderate" ) && ( armedText != statusText ) && ( statusTextAtTheEnd == statusText ), "damaged: " + damagedText.Replace( '\n', '/' ) + " | back to: " + statusTextAtTheEnd.Replace( '\n', '/' ) );

		// ---- 2. the terrain vehicle display (planet 90 in the arth system, the way the m10 scenario gets there)
		yield return EnterOrbit( 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		if ( playerData.m_general.m_location != PD_General.Location.Disembarked )
		{
			Finish( "scenario=perframe abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var vehicleDisplay = controller.m_displayController.m_terrainVehicleDisplay;

		var vehicleText = vehicleDisplay.m_statusValues.text;
		var vehicleBytes = Allocated( vehicleDisplay.Update, c_meterCalls ) / c_meterCalls;
		var vehicleTextAfter = vehicleDisplay.m_statusValues.text;

		// it still has to follow the vehicle: three kilometers further from the ship (2048 units are 225 km)
		var coordinates = playerData.m_general.m_lastDisembarkedCoordinates;

		playerData.m_general.m_lastDisembarkedCoordinates = coordinates + Vector3.right * ( 3.0f * 2048.0f / 225.0f );

		vehicleDisplay.Update();

		var vehicleTextFurtherAway = vehicleDisplay.m_statusValues.text;

		playerData.m_general.m_lastDisembarkedCoordinates = coordinates;

		vehicleDisplay.Update();

		var vehicleTextAtTheEnd = vehicleDisplay.m_statusValues.text;

		Log( "perframe: terrain vehicle display: a frame in which nothing changes takes " + vehicleBytes + " bytes. Text: " + vehicleText.Replace( '\n', '/' ) + " | three kilometers further away: " + vehicleTextFurtherAway.Replace( '\n', '/' ) );

		Check( "perframe: the terrain vehicle display takes no memory while nothing changes", ( vehicleBytes == 0 ) && ( vehicleText == vehicleTextAfter ) && ( vehicleText.Length > 20 ), vehicleBytes + " bytes per frame" );
		Check( "perframe: the terrain vehicle display still shows what changes", ( vehicleTextFurtherAway != vehicleText ) && ( vehicleTextAtTheEnd == vehicleText ), "further away: " + vehicleTextFurtherAway.Replace( '\n', '/' ) + " | back to: " + vehicleTextAtTheEnd.Replace( '\n', '/' ) );

		Finish( "scenario=perframe status=" + statusBytes + " vehicle=" + vehicleBytes + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: what the docking bay transporter costs every frame (the astronaut fading out)

	IEnumerator ScenarioStarportTransport()
	{
		var playerData = DataController.m_instance.m_playerData;

		// everything the docking bay asks for before it lets the crew aboard: a crew, a name for the ship, engines, fuel
		EnsureCrew();

		playerData.m_playerShip.m_name = "Probe";
		playerData.m_playerShip.m_enginesClass = 1;

		if ( playerData.m_playerShip.m_elementStorage.Find( 5 ) == null )
		{
			playerData.m_playerShip.AddElement( 5, 50 );
		}

		var panel = FindPanel<DockingBayPanel>();

		if ( panel == null )
		{
			Finish( "scenario=starport-transport abort: no docking bay panel", 2 );
			yield break;
		}

		// the materials the astronaut fades out with are assets - what the fade does to them must stay in the scene
		var assetAlphaBefore = panel.m_fadeAstronautMaterials[ 0 ].GetColor( "SF_AlbedoColor" ).a;

		PanelController.m_instance.Open( panel );

		var transporting = panel.IsTransporting();

		// half way through the fade
		yield return new WaitForSecondsRealtime( panel.m_fadeStartTime + panel.m_fadeDuration * 0.5f );

		var updateOpacity = (Action<float>) Delegate.CreateDelegate( typeof( Action<float> ), panel, typeof( DockingBayPanel ).GetMethod( "UpdateOpacity", c_any ) );

		var controlBytes = AllocatedByTheControl();
		var bytes = Allocated( () => updateOpacity( 0.25f ), c_meterCalls ) / c_meterCalls;

		var shownMaterials = panel.m_astronautRenderer.sharedMaterials;
		var alpha = shownMaterials[ 0 ].GetColor( "SF_AlbedoColor" ).a;
		var expectedAlpha = Mathf.GammaToLinearSpace( 0.25f );
		var assetAlphaAfter = panel.m_fadeAstronautMaterials[ 0 ].GetColor( "SF_AlbedoColor" ).a;

		Log( "starport-transport: transporting " + transporting + ". An opacity update takes " + bytes + " bytes (the control: " + controlBytes + " bytes per call). " + shownMaterials.Length + " materials on the astronaut, the first now has alpha " + alpha.ToString( "F4" ) + " (wanted " + expectedAlpha.ToString( "F4" ) + "); the material asset had " + assetAlphaBefore.ToString( "F4" ) + " and has " + assetAlphaAfter.ToString( "F4" ) );

		Check( "starport-transport: the measurement sees memory being taken", controlBytes >= 256, controlBytes + " bytes per call for an array of 256 bytes" );
		Check( "starport-transport: fading the astronaut takes no memory", transporting && ( bytes == 0 ), bytes + " bytes per update" );
		Check( "starport-transport: the fade reaches the astronaut and leaves the material assets alone", Mathf.Approximately( alpha, expectedAlpha ) && Mathf.Approximately( assetAlphaAfter, assetAlphaBefore ), "alpha " + alpha.ToString( "F4" ) + " (wanted " + expectedAlpha.ToString( "F4" ) + "), asset " + assetAlphaBefore.ToString( "F4" ) + " -> " + assetAlphaAfter.ToString( "F4" ) );

		Finish( "scenario=starport-transport bytes=" + bytes + " alpha=" + alpha.ToString( "F4" ) + " asset=" + assetAlphaAfter.ToString( "F4" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: the ship's log after an empty log (where the list is scrolled to, and the two loops that scroll it)

	IEnumerator ScenarioShipsLog()
	{
		var shipsLog = SpaceflightController.m_instance.m_shipsLog;

		EnsureCrew();

		// a log with forty entries and a log with none
		var longLog = new List<PD_ShipsLog.Entry>();

		for ( var i = 0; i < 40; i++ )
		{
			longLog.Add( new PD_ShipsLog.Entry( i, "01.00-01-4620", "Entry " + i, "Message " + i ) );
		}

		var emptyLog = new List<PD_ShipsLog.Entry>();

		// ---- 1. open the long log and go down to its last entry with the stick
		shipsLog.Show( longLog );

		yield return Frames( 5 );

		var entries = GetField( shipsLog, "m_entries" ) as TMPro.TextMeshProUGUI;

		for ( var i = 0; i < 39; i++ )
		{
			SetField( shipsLog, "m_ignoreControllerTimer", 0.0f );

			SetAxis( "m_y", -1.0f );

			Call( shipsLog, "Update" );

			SetAxis( "m_y", 0.0f );
		}

		yield return Frames( 3 );

		var selected = (int) GetField( shipsLog, "m_currentIndex" );
		var scrolled = (float) GetField( shipsLog, "m_currentEntriesOffset" );
		var heightOfTheLongLog = entries.renderedHeight;

		shipsLog.Hide();

		yield return Frames( 3 );

		// ---- 2. open the empty log and close it again
		shipsLog.Show( emptyLog );

		yield return Frames( 5 );

		var heightOfTheEmptyLog = entries.renderedHeight;

		shipsLog.Hide();

		yield return Frames( 3 );

		var scrolledBefore = (float) GetField( shipsLog, "m_currentEntriesOffset" );

		Log( "shipslog: long log: selected entry " + selected + " of 40, list scrolled by " + scrolled.ToString( "F1" ) + ", text height " + heightOfTheLongLog.ToString( "F1" ) + ". Empty log: text height " + heightOfTheEmptyLog.ToString( "F1" ) + ". The scroll position is still " + scrolledBefore.ToString( "F1" ) );
		Log( "shipslog: opening the long log again (the review thought the two scroll loops could run for ever here - they would with a text height of 0)" );

		// ---- 3. the long log again
		var start = Time.realtimeSinceStartup;

		shipsLog.Show( longLog );

		var seconds = Time.realtimeSinceStartup - start;

		yield return Frames( 5 );

		var selectedAfter = (int) GetField( shipsLog, "m_currentIndex" );
		var scrolledAfter = (float) GetField( shipsLog, "m_currentEntriesOffset" );

		// and it still scrolls: down to the last entry again
		for ( var i = 0; i < 39; i++ )
		{
			SetField( shipsLog, "m_ignoreControllerTimer", 0.0f );

			SetAxis( "m_y", -1.0f );

			Call( shipsLog, "Update" );

			SetAxis( "m_y", 0.0f );
		}

		yield return Frames( 3 );

		var selectedAtTheEnd = (int) GetField( shipsLog, "m_currentIndex" );
		var scrolledAtTheEnd = (float) GetField( shipsLog, "m_currentEntriesOffset" );

		shipsLog.Hide();

		Log( "shipslog: opened in " + seconds.ToString( "F3" ) + " s: selected entry " + selectedAfter + ", list scrolled by " + scrolledAfter.ToString( "F1" ) + ". After going down again: entry " + selectedAtTheEnd + ", scrolled by " + scrolledAtTheEnd.ToString( "F1" ) );

		Check( "shipslog: the test reached the state that matters", ( selected == 39 ) && ( scrolled > 0.0f ) && ( heightOfTheEmptyLog <= 0.0f ) && ( scrolledBefore > 0.0f ), "selected " + selected + ", scrolled " + scrolled.ToString( "F1" ) + ", height of the empty log " + heightOfTheEmptyLog.ToString( "F1" ) );
		Check( "shipslog: a log opens without hanging after an empty log", seconds < 1.0f, "opened in " + seconds.ToString( "F3" ) + " s" );
		Check( "shipslog: a log opens at its first entry after an empty log", ( selectedAfter == 0 ) && ( scrolledAfter == 0.0f ), "selected " + selectedAfter + ", scrolled " + scrolledAfter.ToString( "F1" ) );
		Check( "shipslog: the list still scrolls to the last entry", ( selectedAtTheEnd == 39 ) && Mathf.Approximately( scrolledAtTheEnd, scrolled ), "selected " + selectedAtTheEnd + ", scrolled " + scrolledAtTheEnd.ToString( "F1" ) + " (the first time " + scrolled.ToString( "F1" ) + ")" );

		Finish( "scenario=shipslog scrolled=" + scrolled.ToString( "F1" ) + " emptyHeight=" + heightOfTheEmptyLog.ToString( "F1" ) + " reopened=" + seconds.ToString( "F3" ) + "s/" + selectedAfter + "/" + scrolledAfter.ToString( "F1" ) + " again=" + selectedAtTheEnd + "/" + scrolledAtTheEnd.ToString( "F1" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: what stays in memory (the materials of a deposit that was picked up, the spaceflight scene after it has been left)

	// how many materials there are in memory, the ones in the project included
	static int MaterialCount()
	{
		return Resources.FindObjectsOfTypeAll<Material>().Length;
	}

	// put a deposit next to the terrain vehicle and everything else out of reach (the scenario itself must not keep hold of anything in the scene)
	static bool PutADepositNextToTheVehicle()
	{
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;
		var deposits = SpaceflightController.m_instance.m_disembarked.m_terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true );

		if ( deposits.Length == 0 )
		{
			return false;
		}

		foreach ( var other in deposits )
		{
			if ( ( other != deposits[ 0 ] ) && ( Vector3.Distance( other.transform.position, terrainVehicle.transform.position ) < 50.0f ) )
			{
				other.transform.position += Vector3.right * 1000.0f;
			}
		}

		deposits[ 0 ].transform.position = BesideTheVehicle( terrainVehicle );

		return true;
	}

	// weak references to three things in the spaceflight scene: the controller, which leads to everything else, the maps of the planet
	// the terrain vehicle is on, and the maps of another planet of the system
	static WeakReference[] WatchTheSpaceflightScene()
	{
		var controller = SpaceflightController.m_instance;

		return new WeakReference[]
		{
			new WeakReference( controller ),
			new WeakReference( controller.m_starSystem.GetPlanetController( 90 ).GetPlanetGenerator() ),
			new WeakReference( controller.m_starSystem.GetPlanetController( 94 ).GetPlanetGenerator() ),
		};
	}

	IEnumerator ScenarioLeaks()
	{
		var playerData = DataController.m_instance.m_playerData;

		EnsureCrew();

		// into the terrain vehicle on planet 90 (arth system), the way the m10 scenario does it
		yield return EnterOrbit( 90 );

		SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		if ( ( playerData.m_general.m_location != PD_General.Location.Disembarked ) || !PutADepositNextToTheVehicle() )
		{
			Finish( "scenario=leaks abort: no terrain vehicle or no deposit (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		// ---- 1. the materials of a deposit that is picked up: its transporter effect used to make its own copies of them (and had to destroy them); it uses the shared effect materials now and copies none
		yield return Frames( 5 );

		var materialsBefore = MaterialCount();

		new TVCargoButton().Execute();

		yield return Frames( 5 );

		var materialsDuring = MaterialCount();

		// the effect takes a second and a half, then the deposit is destroyed
		yield return new WaitForSecondsRealtime( 2.5f );

		var materialsAfter = MaterialCount();

		Log( "leaks: materials in memory: " + materialsBefore + " before the pickup, " + materialsDuring + " during the transporter effect, " + materialsAfter + " after the deposit is gone" );

		Check( "leaks: a transporter effect leaves no materials behind", materialsAfter <= materialsBefore, materialsBefore + " before, " + materialsDuring + " during, " + materialsAfter + " after" );

		// ---- 2. leaving the spaceflight scene. The save panel is opened and closed first, as a player who saves does (the panel lives in the persistent scene)
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		PressEscape();

		yield return new WaitForSecondsRealtime( 1.5f );

		var panelWasOpen = PanelController.m_instance.HasActivePanel();

		yield return CloseTheSavePanel();

		var watched = WatchTheSpaceflightScene();

		SceneManager.LoadScene( "Intro" );

		yield return WaitForScene( "Intro" );

		// a few frames and a few collections (the collector also looks at what is left on the stack)
		for ( var i = 0; i < 5; i++ )
		{
			yield return Frames( 3 );

			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		var controllerInMemory = watched[ 0 ].IsAlive;
		var vehiclePlanetInMemory = watched[ 1 ].IsAlive;
		var otherPlanetInMemory = watched[ 2 ].IsAlive;
		var staticStillSet = !ReferenceEquals( SpaceflightController.m_instance, null );
		var heapInMegabytes = GC.GetTotalMemory( false ) >> 20;

		Log( "leaks: after leaving the spaceflight scene (now in " + SceneManager.GetActiveScene().name + ", save panel had been open: " + panelWasOpen + "): controller still in memory " + controllerInMemory + ", the maps of the planet the vehicle was on " + vehiclePlanetInMemory + ", the maps of another planet " + otherPlanetInMemory + ", the static still points at the old controller " + staticStillSet + ", managed heap in use " + heapInMegabytes + " MB" );

		Check( "leaks: nothing of the spaceflight scene stays in memory after it is left", panelWasOpen && !controllerInMemory && !vehiclePlanetInMemory && !otherPlanetInMemory && !staticStillSet, "controller " + controllerInMemory + ", planet of the vehicle " + vehiclePlanetInMemory + ", other planet " + otherPlanetInMemory + ", static " + staticStillSet );

		Finish( "scenario=leaks materials=" + materialsBefore + "/" + materialsDuring + "/" + materialsAfter + " afterLeaving=" + controllerInMemory + "/" + vehiclePlanetInMemory + "/" + otherPlanetInMemory + "/" + staticStillSet + " heap=" + heapInMegabytes + "MB checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: latent and Test.unity only (the message box slide with another duration, the experimental planet mesh)

	// slide the message box out with the given duration and say where it is at half that time and at the end, then slide it back in
	IEnumerator SlideTheMessageBox( float duration, float[] result )
	{
		var messages = SpaceflightController.m_instance.m_messages;
		var sceneDuration = messages.m_slideDuration;

		messages.m_slideDuration = duration;

		messages.SlideOut();

		yield return new WaitForSecondsRealtime( duration * 0.5f );

		result[ 0 ] = messages.m_frame.offsetMin.x;

		yield return new WaitForSecondsRealtime( duration * 0.5f + 0.3f );

		result[ 1 ] = messages.m_frame.offsetMin.x;

		messages.SlideIn();

		yield return new WaitForSecondsRealtime( duration + 0.3f );

		result[ 2 ] = messages.m_frame.offsetMin.x;

		messages.m_slideDuration = sceneDuration;
	}

	IEnumerator ScenarioLatent()
	{
		EnsureCrew();

		// ---- 1. the message box slides between -12 (in) and -524 (out). With the one second that is set in the scene it is half way after half a second
		var oneSecond = new float[ 3 ];
		var twoSeconds = new float[ 3 ];

		yield return SlideTheMessageBox( 1.0f, oneSecond );
		yield return SlideTheMessageBox( 2.0f, twoSeconds );

		Log( "latent: message box slide of 1 s: at half time " + oneSecond[ 0 ].ToString( "F0" ) + ", at the end " + oneSecond[ 1 ].ToString( "F0" ) + ", back in " + oneSecond[ 2 ].ToString( "F0" ) + " | slide of 2 s: at half time " + twoSeconds[ 0 ].ToString( "F0" ) + ", at the end " + twoSeconds[ 1 ].ToString( "F0" ) + ", back in " + twoSeconds[ 2 ].ToString( "F0" ) );

		Check( "latent: the message box slide of one second is as it was", ( Mathf.Abs( oneSecond[ 0 ] + 268.0f ) < 60.0f ) && ( Mathf.Abs( oneSecond[ 1 ] + 524.0f ) < 1.0f ) && ( Mathf.Abs( oneSecond[ 2 ] + 12.0f ) < 1.0f ), "half time " + oneSecond[ 0 ].ToString( "F0" ) + ", end " + oneSecond[ 1 ].ToString( "F0" ) + ", back in " + oneSecond[ 2 ].ToString( "F0" ) );
		Check( "latent: a message box slide of two seconds is half way after one second", ( Mathf.Abs( twoSeconds[ 0 ] + 268.0f ) < 60.0f ) && ( Mathf.Abs( twoSeconds[ 1 ] + 524.0f ) < 1.0f ) && ( Mathf.Abs( twoSeconds[ 2 ] + 12.0f ) < 1.0f ), "half time " + twoSeconds[ 0 ].ToString( "F0" ) + ", end " + twoSeconds[ 1 ].ToString( "F0" ) + ", back in " + twoSeconds[ 2 ].ToString( "F0" ) );

		// ---- 2. the experimental planet mesh (it is only used in Test.unity): a planet object with no parent, at a resolution of 110
		//         (6 faces of 110 by 110 are 72600 vertices, more than the 65535 a mesh with 16 bit indices can have)
		var planetData = ScriptableObject.CreateInstance<PlanetData>();

		typeof( PlanetData ).GetField( "_resolution", c_any ).SetValue( planetData, 110 );

		var meshesBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;

		var planetObject = new GameObject( "Probe Planet" );
		var planetManager = planetObject.AddComponent<PlanetManager>();

		typeof( PlanetManager ).GetField( "_planetData", c_any ).SetValue( planetManager, planetData );

		var generateThrew = "nothing";

		Application.logMessageReceived += CountErrors;

		s_errorsLogged = 0;

		try
		{
			planetManager.GeneratePlanet();
		}
		catch ( Exception exception )
		{
			generateThrew = exception.GetType().Name;
		}

		Application.logMessageReceived -= CountErrors;

		var mesh = planetObject.GetComponent<MeshFilter>().sharedMesh;
		var vertices = ( mesh != null ) ? mesh.vertexCount : -1;
		var triangles = ( mesh != null ) ? (int) ( mesh.GetIndexCount( 0 ) / 3 ) : -1;
		var indexFormat = ( mesh != null ) ? mesh.indexFormat.ToString() : "no mesh";

		// the largest vertex number any triangle uses, read back from the mesh (the last vertex is number 72599)
		var largestIndex = -1;

		if ( mesh != null )
		{
			foreach ( var index in mesh.triangles )
			{
				largestIndex = Mathf.Max( largestIndex, index );
			}
		}
		var meshesWithThePlanet = Resources.FindObjectsOfTypeAll<Mesh>().Length;

		Destroy( planetObject );

		yield return Frames( 3 );

		var meshesAfter = Resources.FindObjectsOfTypeAll<Mesh>().Length;

		Destroy( planetData );

		Log( "latent: planet mesh at resolution 110 with no parent: threw " + generateThrew + ", errors logged " + s_errorsLogged + ", " + vertices + " vertices (72600 wanted), " + triangles + " triangles (142572 wanted), index format " + indexFormat + ", largest vertex number in a triangle " + largestIndex + ". Meshes in memory: " + meshesBefore + " before, " + meshesWithThePlanet + " with the planet, " + meshesAfter + " after it was destroyed" );

		Check( "latent: the triangles of a planet mesh with more than 65535 vertices reach all of them", ( s_errorsLogged == 0 ) && ( vertices == 72600 ) && ( triangles == 142572 ) && ( largestIndex == 72599 ), "errors " + s_errorsLogged + ", " + vertices + " vertices, " + triangles + " triangles, index format " + indexFormat + ", largest vertex number in a triangle " + largestIndex );
		Check( "latent: a planet with no parent can be generated", generateThrew == "nothing", "threw " + generateThrew );
		Check( "latent: the planet mesh goes with its planet", ( meshesWithThePlanet > meshesBefore ) && ( meshesAfter == meshesBefore ), meshesBefore + " before, " + meshesWithThePlanet + " with the planet, " + meshesAfter + " after" );

		// ---- 3. the adapter with the procedural planets switched off and no planet to go back to
		var adapterObject = new GameObject( "Probe Adapter" );
		var adapter = adapterObject.AddComponent<ProceduralAdapter>();
		var switchBefore = ProceduralAdapter.EnableProceduralGeneration;
		var adapterThrew = "nothing";

		ProceduralAdapter.EnableProceduralGeneration = false;

		try
		{
			adapter.Initialize( null, DataController.m_instance.m_gameData.m_planetList[ 90 ] );
		}
		catch ( Exception exception )
		{
			adapterThrew = exception.GetType().Name;
		}

		ProceduralAdapter.EnableProceduralGeneration = switchBefore;

		Destroy( adapterObject );

		Log( "latent: the adapter, switched off, with no planet controller: threw " + adapterThrew );

		Check( "latent: the adapter does nothing without a planet controller", adapterThrew == "nothing", "threw " + adapterThrew );

		Finish( "scenario=latent oneSecond=" + oneSecond[ 0 ].ToString( "F0" ) + "/" + oneSecond[ 1 ].ToString( "F0" ) + " twoSeconds=" + twoSeconds[ 0 ].ToString( "F0" ) + "/" + twoSeconds[ 1 ].ToString( "F0" ) + " planet=" + generateThrew + "/" + vertices + "/" + triangles + "/" + ( meshesAfter - meshesBefore ) + " adapter=" + adapterThrew + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- Low: editor tools (the textures the planet generator's save functions make, the shader inspector with a texture that is not 2D, an empty path)

	// call a method of the editor assembly and say what it threw (nothing, if it worked)
	static string InvokeAndCatch( MethodInfo method, object target, object[] arguments, out object result )
	{
		result = null;

		try
		{
			result = method.Invoke( target, arguments );

			return "nothing";
		}
		catch ( TargetInvocationException exception )
		{
			return exception.InnerException.GetType().Name;
		}
	}

	IEnumerator ScenarioEditorTools()
	{
		// ---- 1. the four save functions of the planet generator tool each make a texture for the picture they save
		var tools = Type.GetType( "PG_Tools, Assembly-CSharp-Editor" );
		var folder = System.IO.Path.Combine( Application.temporaryCachePath, "ClaudeProbeEditorTools" );
		var floats = new float[ 16, 16 ];
		var colors = new Color[ 16, 16 ];

		var saves = new MethodInfo[]
		{
			tools.GetMethod( "SaveAsPNG", new Type[] { typeof( float[,] ), typeof( string ) } ),
			tools.GetMethod( "SaveAsPNG", new Type[] { typeof( Color[,] ), typeof( string ), typeof( bool ) } ),
			tools.GetMethod( "SaveAsEXR", new Type[] { typeof( float[,] ), typeof( string ) } ),
			tools.GetMethod( "SaveAsEXR", new Type[] { typeof( Color[,] ), typeof( string ) } ),
		};

		var arguments = new object[][]
		{
			new object[] { floats, System.IO.Path.Combine( folder, "floats.png" ) },
			new object[] { colors, System.IO.Path.Combine( folder, "colors.png" ), false },
			new object[] { floats, System.IO.Path.Combine( folder, "floats.exr" ) },
			new object[] { colors, System.IO.Path.Combine( folder, "colors.exr" ) },
		};

		var texturesBefore = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
		var saveThrew = "";
		var filesWritten = 0;

		for ( var i = 0; i < saves.Length; i++ )
		{
			saveThrew += InvokeAndCatch( saves[ i ], null, arguments[ i ], out _ ) + " ";

			filesWritten += System.IO.File.Exists( (string) arguments[ i ][ 1 ] ) ? 1 : 0;
		}

		var texturesAfter = Resources.FindObjectsOfTypeAll<Texture2D>().Length;

		if ( System.IO.Directory.Exists( folder ) )
		{
			System.IO.Directory.Delete( folder, true );
		}

		Log( "editortools: four saves: threw [" + saveThrew.Trim() + "], files written " + filesWritten + ", textures in memory " + texturesBefore + " before and " + texturesAfter + " after" );

		Check( "editortools: the save functions of the planet generator leave no texture behind", ( filesWritten == 4 ) && ( texturesAfter == texturesBefore ), filesWritten + " files, textures " + texturesBefore + " -> " + texturesAfter );

		// ---- 2. the shader inspector asks whether a normal map is DXT5 compressed. A texture that is not a 2D texture (a render texture here) has no format to ask for
		var shaderGUIType = Type.GetType( "SFShaderGUI, Assembly-CSharp-Editor" );
		var shaderGUI = Activator.CreateInstance( shaderGUIType, true );
		var textureIsCompressed = shaderGUIType.GetMethod( "TextureIsCompressed", c_any );

		Material template = null;

		foreach ( var candidate in Resources.FindObjectsOfTypeAll<Material>() )
		{
			if ( candidate.HasProperty( "SF_NormalMap" ) )
			{
				template = candidate;
				break;
			}
		}

		var material = new Material( template );
		var renderTexture = new RenderTexture( 16, 16, 0 );
		var compressedTexture = new Texture2D( 16, 16, TextureFormat.DXT5, false );
		var plainTexture = new Texture2D( 16, 16, TextureFormat.RGBA32, false );

		material.SetTexture( "SF_NormalMap", renderTexture );

		var withRenderTexture = InvokeAndCatch( textureIsCompressed, shaderGUI, new object[] { material, "SF_NormalMap" }, out var renderTextureAnswer );

		material.SetTexture( "SF_NormalMap", compressedTexture );

		InvokeAndCatch( textureIsCompressed, shaderGUI, new object[] { material, "SF_NormalMap" }, out var compressedAnswer );

		material.SetTexture( "SF_NormalMap", plainTexture );

		InvokeAndCatch( textureIsCompressed, shaderGUI, new object[] { material, "SF_NormalMap" }, out var plainAnswer );

		Destroy( material );
		Destroy( renderTexture );
		Destroy( compressedTexture );
		Destroy( plainTexture );

		Log( "editortools: shader inspector (shader " + template.shader.name + "): a render texture as normal map threw " + withRenderTexture + " (answer " + renderTextureAnswer + "), a DXT5 texture is compressed: " + compressedAnswer + ", an RGBA32 texture is compressed: " + plainAnswer );

		Check( "editortools: the shader inspector takes a normal map that is not a 2D texture", ( withRenderTexture == "nothing" ) && Equals( renderTextureAnswer, false ) && Equals( compressedAnswer, true ) && Equals( plainAnswer, false ), "render texture threw " + withRenderTexture + ", DXT5 " + compressedAnswer + ", RGBA32 " + plainAnswer );

		// ---- 3. what a cancelled save dialog leaves behind: an empty file name. The two tool windows give its directory to the next dialog
		var emptyPath = "nothing";

		try
		{
			var directory = System.IO.Path.GetDirectoryName( "" );

			emptyPath = "returned " + ( ( directory == null ) ? "null" : ( "\"" + directory + "\"" ) );
		}
		catch ( Exception exception )
		{
			emptyPath = "threw " + exception.GetType().Name;
		}

		Log( "editortools: Path.GetDirectoryName of an empty file name " + emptyPath );

		yield return null;

		Finish( "scenario=editortools textures=" + texturesBefore + "/" + texturesAfter + " shaderGUI=" + withRenderTexture + " emptyPath=[" + emptyPath + "] checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- rulings of 2026-10-05: armor points of an old save, stardates in the bank, the notices and the ships log, the starting balance (starport)

	// a year-month-day stardate the way the game shows it, worked out without any date class (so it does not depend on the code under test)
	static string DayMonthYear( string stardateYMD )
	{
		var parts = stardateYMD.Split( '-' );

		return ( parts.Length == 3 ) ? ( parts[ 2 ] + "-" + parts[ 1 ] + "-" + parts[ 0 ] ) : stardateYMD;
	}

	// put a game with these armor points into save slot 1 (in memory), load it the way the data controller does, and return the armor points it has then
	static int ArmorPointsAfterLoad( int armorClass, int armorPoints )
	{
		var dataController = DataController.m_instance;

		var saved = new PlayerData();

		saved.Reset();

		saved.m_playerShip.m_armorClass = armorClass;
		saved.m_playerShip.m_armorPoints = armorPoints;

		MemorySaveSystem.s_slots[ 1 ] = JsonUtility.ToJson( saved, true );

		var loaded = Call( dataController, "LoadPlayerData", 1 ) as PlayerData;

		MemorySaveSystem.s_slots.Remove( 1 );

		return ( loaded == null ) ? int.MinValue : loaded.m_playerShip.m_armorPoints;
	}

	IEnumerator ScenarioStarportSaveData()
	{
		var dataController = DataController.m_instance;
		var gameData = dataController.m_gameData;
		var playerData = dataController.m_playerData;

		// ---- 1. a save with more armor points than its armor allows is cut back when it is loaded
		var bareHull = PD_PlayerShip.c_bareHullArmorPoints;
		var class2Maximum = gameData.m_armorList[ 2 ].m_points;

		var soldArmor = ArmorPointsAfterLoad( 0, 1500 );
		var tooManyForClass2 = ArmorPointsAfterLoad( 2, class2Maximum + 700 );
		var damagedClass2 = ArmorPointsAfterLoad( 2, class2Maximum - 10 );
		var wholeBareHull = ArmorPointsAfterLoad( 0, bareHull );
		var destroyed = ArmorPointsAfterLoad( 2, 0 );
		var belowZero = ArmorPointsAfterLoad( 0, -50 );

		Log( "old saves: armor points after loading: no armor with 1500 points -> " + soldArmor + " (bare hull " + bareHull + "), class 2 with " + ( class2Maximum + 700 ) + " -> " + tooManyForClass2 + " (its armor has " + class2Maximum + "), class 2 with " + ( class2Maximum - 10 ) + " -> " + damagedClass2 + ", a whole bare hull -> " + wholeBareHull + ", destroyed with 0 -> " + destroyed + ", with -50 -> " + belowZero );

		Check( "old saves: armor points above the maximum are cut back to it on load", ( soldArmor == bareHull ) && ( tooManyForClass2 == class2Maximum ), "no armor: 1500 -> " + soldArmor + " (maximum " + bareHull + "), class 2: " + ( class2Maximum + 700 ) + " -> " + tooManyForClass2 + " (maximum " + class2Maximum + ")" );
		Check( "old saves: armor points at or below the maximum are left alone", ( damagedClass2 == class2Maximum - 10 ) && ( wholeBareHull == bareHull ), "class 2 with " + ( class2Maximum - 10 ) + " -> " + damagedClass2 + ", bare hull with " + bareHull + " -> " + wholeBareHull );
		Check( "old saves: a save written with a destroyed ship still loads with 1 armor point", ( destroyed == 1 ) && ( belowZero == 1 ), "0 -> " + destroyed + ", -50 -> " + belowZero );

		// ---- 2. the bank shows its dates as stardates, whatever calendar the computer uses (thai: buddhist years)
		var cultureBefore = System.Globalization.CultureInfo.CurrentCulture;

		var bankPanel = FindPanel<BankPanel>();
		var operationsPanel = FindPanel<OperationsPanel>();

		playerData.m_bank.m_transactionList.Add( new PD_Bank.Transaction( "4620-03-26", "Trade depot", "1400+" ) );

		var bankDates = "panel did not open";

		System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo( "th-TH" );

		try
		{
			PanelController.m_instance.Open( bankPanel );

			bankDates = bankPanel.m_dateListText.text.Replace( "\r", "" ).Replace( "\n", " | " );
		}
		catch ( Exception exception )
		{
			bankDates = "threw " + exception.GetType().Name;
		}
		finally
		{
			System.Globalization.CultureInfo.CurrentCulture = cultureBefore;
		}

		yield return new WaitForSecondsRealtime( 1.5f );
		yield return ClosePanel( bankPanel, "bank" );

		Log( "dates: the bank's date column with the thai calendar: " + bankDates );

		Check( "dates: the bank shows stardates (day-month-year) with the thai calendar", bankDates == "01-01-4620 | 26-03-4620", bankDates );

		// ---- 3. the notices in operations: today's date, the date of the notice, and the entry it leaves in the ships log
		playerData.m_general.m_currentStardateYMD = "4620-03-26";

		var today = "panel did not open";
		var noticeDate = "";
		var expectedNoticeDate = "";
		var logStardate = "";
		var logHeader = "";

		System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo( "th-TH" );

		try
		{
			PanelController.m_instance.Open( operationsPanel );

			operationsPanel.ShowNotices();

			today = operationsPanel.m_stardateText.text;
			noticeDate = operationsPanel.m_messageText.text.Replace( "\r", "" ).Split( '\n' )[ 0 ];

			var currentNoticeId = (int) GetField( operationsPanel, "m_currentNoticeId" );

			expectedNoticeDate = DayMonthYear( gameData.m_noticeList[ currentNoticeId ].m_stardate );

			foreach ( var entry in playerData.m_shipsLog.m_starportNotices )
			{
				if ( entry.m_id == currentNoticeId )
				{
					logStardate = entry.m_stardate;
					logHeader = entry.m_header;
				}
			}
		}
		catch ( Exception exception )
		{
			today = "threw " + exception.GetType().Name + ": " + exception.Message;
		}
		finally
		{
			System.Globalization.CultureInfo.CurrentCulture = cultureBefore;
		}

		yield return new WaitForSecondsRealtime( 1.5f );
		yield return ClosePanel( operationsPanel, "operations" );

		playerData.m_general.m_currentStardateYMD = "4620-01-01";

		Log( "dates: operations with the thai calendar: '" + today + "', the notice is dated '" + noticeDate + "' (its stardate is " + expectedNoticeDate + "), its ships log entry '" + logStardate + "' with the heading '" + logHeader + "'" );

		Check( "dates: operations shows today's stardate with the thai calendar", today == "Today is 26-03-4620", today );
		Check( "dates: a notice is dated with its stardate with the thai calendar", ( expectedNoticeDate != "" ) && ( noticeDate == expectedNoticeDate ), "'" + noticeDate + "' (should be " + expectedNoticeDate + ")" );
		Check( "dates: the ships log entry of a notice has its stardate with the thai calendar", ( expectedNoticeDate != "" ) && ( logStardate == expectedNoticeDate ) && ( logHeader == expectedNoticeDate ), "'" + logStardate + "' / '" + logHeader + "' (should be " + expectedNoticeDate + ")" );

		// ---- 4. a ships log entry that an older build saved with the computer's date format is dated again when the save is loaded
		var oldSave = new PlayerData();

		oldSave.Reset();

		oldSave.m_shipsLog.m_starportNotices.Add( new PD_ShipsLog.Entry( 0, "1/1/5163", "1 January 5163", gameData.m_noticeList[ 0 ].m_message ) );
		oldSave.m_shipsLog.m_starportNotices.Add( new PD_ShipsLog.Entry( 99999, "kept", "kept", "an entry whose notice the game data does not have" ) );

		MemorySaveSystem.s_slots[ 1 ] = JsonUtility.ToJson( oldSave, true );

		var loadedOldSave = Call( dataController, "LoadPlayerData", 1 ) as PlayerData;

		MemorySaveSystem.s_slots.Remove( 1 );

		var repairedStardate = "not loaded";
		var repairedHeader = "";
		var unknownNotice = "";

		if ( ( loadedOldSave != null ) && ( loadedOldSave.m_shipsLog.m_starportNotices.Count == 2 ) )
		{
			repairedStardate = loadedOldSave.m_shipsLog.m_starportNotices[ 0 ].m_stardate;
			repairedHeader = loadedOldSave.m_shipsLog.m_starportNotices[ 0 ].m_header;
			unknownNotice = loadedOldSave.m_shipsLog.m_starportNotices[ 1 ].m_stardate;
		}

		var firstNoticeDate = DayMonthYear( gameData.m_noticeList[ 0 ].m_stardate );

		Log( "old saves: a ships log entry saved as '1/1/5163' / '1 January 5163' loads as '" + repairedStardate + "' / '" + repairedHeader + "' (the notice's stardate is " + firstNoticeDate + "); an entry for a notice that does not exist loads as '" + unknownNotice + "'" );

		Check( "old saves: a notice in the ships log is dated again with its stardate on load", ( repairedStardate == firstNoticeDate ) && ( repairedHeader == firstNoticeDate ), "'" + repairedStardate + "' / '" + repairedHeader + "' (should be " + firstNoticeDate + ")" );
		Check( "old saves: a ships log entry whose notice does not exist is left alone", unknownNotice == "kept", "'" + unknownNotice + "'" );

		// ---- 5. the starting balance: a build starts with the original 12,000 MU, the editor stays rich (the probe always runs in the editor, so the build value is asked for directly)
		var getStartingBalance = typeof( PD_Bank ).GetMethod( "GetStartingBalance", c_any );

		var buildBalance = ( getStartingBalance == null ) ? -1 : (int) getStartingBalance.Invoke( null, new object[] { false } );
		var editorBalance = ( getStartingBalance == null ) ? -1 : (int) getStartingBalance.Invoke( null, new object[] { true } );

		var fresh = new PlayerData();

		fresh.Reset();

		Log( "starting balance: a build " + ( ( getStartingBalance == null ) ? "has no balance of its own (no PD_Bank.GetStartingBalance)" : buildBalance.ToString() ) + ", the editor " + editorBalance + ", a new game in this editor run " + fresh.m_bank.m_currentBalance + " (Application.isEditor " + Application.isEditor + ")" );

		Check( "starting balance: a build starts with the original 12,000 MU", buildBalance == 12000, ( getStartingBalance == null ) ? "this code has one balance for the editor and for builds: " + fresh.m_bank.m_currentBalance : buildBalance.ToString() );
		Check( "starting balance: a new game in the editor still has 1,000,000 MU", fresh.m_bank.m_currentBalance == 1000000, fresh.m_bank.m_currentBalance.ToString() );

		Finish( "scenario=starport-savedata armor=" + soldArmor + "/" + tooManyForClass2 + "/" + damagedClass2 + "/" + destroyed + " bank=[" + bankDates + "] operations=[" + today + " | " + noticeDate + "] log=[" + logStardate + " | " + repairedStardate + "] balance=" + buildBalance + "/" + fresh.m_bank.m_currentBalance + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- rulings of 2026-10-05: a comm link stops alien fire, a missile that times out reports a miss, the dead method of the encounter is gone

	// how many explosions of the combat controller's pool are playing
	static int ExplosionsPlaying()
	{
		var explosionPool = GetField( CombatController.m_instance, "m_explosionPool" ) as System.Collections.IEnumerable;
		var count = 0;

		if ( explosionPool != null )
		{
			foreach ( var explosion in explosionPool )
			{
				if ( (bool) Call( explosion, "IsPlaying" ) )
				{
					count++;
				}
			}
		}

		return count;
	}

	// how many missiles are in the air
	static int MissilesInTheAir()
	{
		var missilePool = GetField( CombatController.m_instance, "m_missilePool" ) as System.Collections.IEnumerable;
		var count = 0;

		if ( missilePool != null )
		{
			foreach ( var missile in missilePool )
			{
				if ( (bool) Call( missile, "IsActive" ) )
				{
					count++;
				}
			}
		}

		return count;
	}

	// let every missile in the air run out of time (the next update of each one is its time out)
	static void RunMissilesOutOfTime()
	{
		var missilePool = GetField( CombatController.m_instance, "m_missilePool" ) as System.Collections.IEnumerable;

		if ( missilePool == null )
		{
			return;
		}

		foreach ( var missile in missilePool )
		{
			if ( (bool) Call( missile, "IsActive" ) )
			{
				SetField( missile, "m_lifetime", 1000.0f );
			}
		}
	}

	IEnumerator ScenarioCommLink()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;
		var encounter = SpaceflightController.m_instance.m_encounter;

		EnsureCrew();

		// a ship that can launch missiles and that survives being shot at for the whole scenario
		ship.m_laserCannonClass = 1;
		ship.m_missileLauncherClass = 1;
		ship.m_armorPoints = 100000;
		ship.m_shieldsAreUp = false;

		if ( ship.m_elementStorage.Find( 5 ) == null )
		{
			ship.AddElement( 5, 50 );
		}

		var damage = 0;

		// ---- 1. spemin scouts (lasers) that have been fired on: they shoot, they hold their fire while the comm link is up, they shoot again after it
		var speminId = FindEncounter( 1, 6, 3, 0 );

		ForceVessel( speminId, 2 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		encounter.PlayerAttacked();

		yield return DamageTaken( 8.0f, value => damage = value );

		var damageBeforeLink = damage;

		ClearMissiles();

		var connectThrew = "";

		try
		{
			encounter.Connect();
		}
		catch ( Exception exception )
		{
			connectThrew = exception.GetType().Name;
		}

		yield return Frames( 2 );

		var connectedAtStart = encounter.IsConnected();

		yield return DamageTaken( 11.0f, value => damage = value );

		var damageDuringLink = damage;
		var connectedAtEnd = encounter.IsConnected();
		var stanceDuringLink = Stance();

		try
		{
			encounter.Disconnect();
		}
		catch ( Exception exception )
		{
			connectThrew += " disconnect " + exception.GetType().Name;
		}

		yield return DamageTaken( 8.0f, value => damage = value );

		var damageAfterLink = damage;

		Log( "comm link: hostile spemin scouts did " + damageBeforeLink + " damage in 8 s before the link, " + damageDuringLink + " in 11 s with the link up (connected at its start " + connectedAtStart + ", at its end " + connectedAtEnd + ", stance " + stanceDuringLink + "), " + damageAfterLink + " in 8 s after it" + ( ( connectThrew == "" ) ? "" : " | threw: " + connectThrew ) );

		Check( "comm link: hostile aliens fire before the link (control)", damageBeforeLink > 0, damageBeforeLink + " damage in 8 s" );
		Check( "comm link: the aliens hold their fire while the comm link is up", connectedAtStart && connectedAtEnd && ( stanceDuringLink == "Hostile" ) && ( damageDuringLink == 0 ), damageDuringLink + " damage in 11 s, connected " + connectedAtStart + "/" + connectedAtEnd + ", stance " + stanceDuringLink );
		Check( "comm link: the aliens fire again once the link is down", damageAfterLink > 0, damageAfterLink + " damage in 8 s" );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. the uhlek never talk, so they never have a comm link in play - the flag is set by hand here, to see that the rule holds for the races that reach alien fire by another way
		var uhlekId = FindEncounter( 0, 1, 1, 0, GameData.Race.Uhlek );

		EnterEncounter( uhlekId );
		yield return Frames( 10 );

		encounter.m_pdEncounter.m_connected = true;

		ClearMissiles();

		yield return DamageTaken( 7.0f, value => damage = value );

		var uhlekDamageWithLink = damage;

		encounter.m_pdEncounter.m_connected = false;

		yield return DamageTaken( 6.0f, value => damage = value );

		var uhlekDamageWithoutLink = damage;

		Log( "comm link: the uhlek did " + uhlekDamageWithLink + " damage in 7 s with the comm flag set by hand, " + uhlekDamageWithoutLink + " in 6 s without it" );

		Check( "comm link: the rule holds for a race that does not use the default update (uhlek, flag set by hand)", ( uhlekDamageWithLink == 0 ) && ( uhlekDamageWithoutLink > 0 ), uhlekDamageWithLink + " with the flag, " + uhlekDamageWithoutLink + " without" );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. a player missile that runs out of time: an explosion where it was, and no damage (first a missile that arrives, to see that a hit is measured)
		ship.m_armorPoints = 100000;

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var targetIndex = FirstLivingAlien();
		var target = encounter.m_pdEncounter.GetAlienShipList()[ targetIndex ];

		target.m_armorPoints = 5000;
		target.m_shieldPoints = 0;

		combat.SetTarget( targetIndex );

		BringAliensClose();

		var firstLaunched = combat.FirePlayerMissile();

		var waitEnd = Time.realtimeSinceStartup + 3.0f;

		while ( ( Time.realtimeSinceStartup < waitEnd ) && ( MissilesInTheAir() > 0 ) )
		{
			BringAliensClose();

			yield return null;
		}

		var damageOfAHit = 5000 - target.m_armorPoints;

		// wait for its explosion to end and for the launcher to be ready again
		waitEnd = Time.realtimeSinceStartup + 3.0f;

		while ( Time.realtimeSinceStartup < waitEnd )
		{
			BringAliensClose();

			yield return null;
		}

		var explosionsBefore = ExplosionsPlaying();
		var armorBefore = target.m_armorPoints;

		combat.SetTarget( targetIndex );

		BringAliensClose();

		var secondLaunched = combat.FirePlayerMissile();
		var inTheAir = MissilesInTheAir();

		RunMissilesOutOfTime();

		yield return Frames( 3 );

		var explosionsAfterTimeOut = ExplosionsPlaying();
		var damageOfATimeOut = armorBefore - target.m_armorPoints;
		var inTheAirAfter = MissilesInTheAir();

		Log( "missile time out: a player missile that arrives does " + damageOfAHit + " damage (launched " + firstLaunched + "); one that runs out of time (launched " + secondLaunched + ", " + inTheAir + " in the air, " + inTheAirAfter + " after) does " + damageOfATimeOut + " damage, explosions playing before " + explosionsBefore + " and after " + explosionsAfterTimeOut );

		Check( "missile time out: a player missile that arrives still does its damage (control)", firstLaunched && ( damageOfAHit > 0 ), damageOfAHit + " damage" );
		Check( "missile time out: a player missile that runs out of time explodes where it was and does no damage", secondLaunched && ( inTheAir > 0 ) && ( inTheAirAfter == 0 ) && ( explosionsBefore == 0 ) && ( explosionsAfterTimeOut == 1 ) && ( damageOfATimeOut == 0 ), "explosions " + explosionsBefore + " -> " + explosionsAfterTimeOut + ", damage " + damageOfATimeOut + ", in the air " + inTheAir + " -> " + inTheAirAfter );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 4. an alien missile that runs out of time: the same, and the player ship is not damaged (elowan scouts only have missiles)
		var secondId = FindEncounter( 1, 6, 3, 1 );

		ForceVessel( secondId, 6 );

		EnterEncounter( secondId );
		yield return Frames( 10 );

		// wait for the explosions of the encounter before to end
		waitEnd = Time.realtimeSinceStartup + 3.0f;

		while ( ( Time.realtimeSinceStartup < waitEnd ) && ( ExplosionsPlaying() > 0 ) )
		{
			yield return null;
		}

		ship.m_armorPoints = 100000;

		encounter.PlayerAttacked();

		// wait for their first missile
		waitEnd = Time.realtimeSinceStartup + 8.0f;

		while ( ( Time.realtimeSinceStartup < waitEnd ) && ( MissilesInTheAir() == 0 ) )
		{
			BringAliensClose();

			yield return null;
		}

		var alienMissiles = MissilesInTheAir();
		var alienExplosionsBefore = ExplosionsPlaying();
		var playerArmorBefore = ship.m_armorPoints;

		RunMissilesOutOfTime();

		yield return Frames( 3 );

		var alienExplosionsAfter = ExplosionsPlaying();
		var playerDamage = playerArmorBefore - ship.m_armorPoints;

		Log( "missile time out: an alien missile that runs out of time (" + alienMissiles + " in the air): explosions playing before " + alienExplosionsBefore + " and after " + alienExplosionsAfter + ", damage to the player " + playerDamage );

		Check( "missile time out: an alien missile that runs out of time explodes where it was and does no damage", ( alienMissiles > 0 ) && ( alienExplosionsAfter > alienExplosionsBefore ) && ( playerDamage == 0 ), alienMissiles + " in the air, explosions " + alienExplosionsBefore + " -> " + alienExplosionsAfter + ", damage " + playerDamage );

		ClearMissiles();

		// ---- 5. the method of the encounter that nothing called is gone
		var deadMethod = typeof( Encounter ).GetMethod( "LeaveEncounterAfterVictory", c_any );

		Check( "dead code: Encounter.LeaveEncounterAfterVictory is gone", deadMethod == null, ( deadMethod == null ) ? "not there" : "still there" );

		Finish( "scenario=commlink spemin=" + damageBeforeLink + "/" + damageDuringLink + "/" + damageAfterLink + " uhlek=" + uhlekDamageWithLink + "/" + uhlekDamageWithoutLink + " playerMissile=hit" + damageOfAHit + "/timeout" + damageOfATimeOut + "/explosions" + explosionsAfterTimeOut + " alienMissile=explosions" + alienExplosionsAfter + "/damage" + playerDamage + " deadMethod=" + ( deadMethod != null ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- ruling of 2026-10-05: a mineral deposit holds 1 to 5 cubic meters

	IEnumerator ScenarioDeposits()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// down to planet 90 (43% mineral density) and into the terrain vehicle
		yield return EnterOrbit( 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		if ( playerData.m_general.m_location != PD_General.Location.Disembarked )
		{
			Finish( "scenario=deposits abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainVehicle = controller.m_terrainVehicle;
		var container = controller.m_disembarked.m_terrainGrid.m_terrainElements.transform;
		var deposits = container.GetComponentsInChildren<TerrainElement>( true );

		if ( deposits.Length < 60 )
		{
			Finish( "scenario=deposits abort: the planet has only " + deposits.Length + " deposits", 2 );
			yield break;
		}

		// ---- 1. the size of every deposit of the planet, in tenths of a cubic meter (and where the deposits are, to compare two runs: the size must not move them)
		var smallest = int.MaxValue;
		var largest = int.MinValue;
		var total = 0;
		var notWholeCubicMeters = 0;
		var sizes = new int[ 6 ];
		long placement = 0;

		for ( var i = 0; i < deposits.Length; i++ )
		{
			var volume = deposits[ i ].m_volume;

			smallest = Mathf.Min( smallest, volume );
			largest = Mathf.Max( largest, volume );
			total += volume;

			if ( ( volume % 10 != 0 ) || ( volume < 10 ) || ( volume > 50 ) )
			{
				notWholeCubicMeters++;
			}
			else
			{
				sizes[ volume / 10 ]++;
			}

			var position = deposits[ i ].transform.position;

			placement = ( placement * 31 + Mathf.RoundToInt( position.x ) * 7 + Mathf.RoundToInt( position.z ) * 3 + deposits[ i ].m_elementId ) % 1000000007L;
		}

		Log( "deposits: planet 90 has " + deposits.Length + " deposits, from " + Tools.VolumeToText( smallest ) + " to " + Tools.VolumeToText( largest ) + " cubic meters, " + Tools.VolumeToText( total ) + " in all; " + notWholeCubicMeters + " are not 1, 2, 3, 4 or 5 cubic meters; of each size: " + sizes[ 1 ] + "/" + sizes[ 2 ] + "/" + sizes[ 3 ] + "/" + sizes[ 4 ] + "/" + sizes[ 5 ] + "; placement checksum " + placement );

		Check( "deposits: every deposit holds 1 to 5 cubic meters", ( notWholeCubicMeters == 0 ) && ( smallest == 10 ) && ( largest == 50 ), "from " + Tools.VolumeToText( smallest ) + " to " + Tools.VolumeToText( largest ) + " cubic meters, " + notWholeCubicMeters + " of " + deposits.Length + " outside 1 to 5" );

		// ---- 2. pick one up with the real button: what the message says and what arrives in the hold
		foreach ( var other in deposits )
		{
			if ( Vector3.Distance( other.transform.position, terrainVehicle.transform.position ) < 50.0f )
			{
				other.transform.position += Vector3.right * 1000.0f;
			}
		}

		var first = deposits[ 0 ];
		var firstVolume = first.m_volume;

		first.transform.position = BesideTheVehicle( terrainVehicle );

		new TVCargoButton().Execute();

		var pickupMessage = MessageList();
		var cargoAfterFirst = TerrainVehicleCargo( first.m_elementId );

		Log( "deposits: picking up a deposit of " + firstVolume + " tenths: " + pickupMessage + " (in the hold now: " + cargoAfterFirst + " tenths)" );

		Check( "deposits: the pickup message and the hold agree on a whole number of cubic meters", ( firstVolume >= 10 ) && ( cargoAfterFirst == firstVolume ) && pickupMessage.Contains( "Picked up " + ( firstVolume / 10 ) + ".0 cubic meters of " ), "deposit " + firstVolume + " tenths, hold " + cargoAfterFirst + " tenths | " + pickupMessage );

		// ---- 3. how many deposits it takes to fill the terrain vehicle's hold (one deposit after the other next to the vehicle, real button)
		var holdSize = gameData.m_misc.m_terrainVehicleVolume;
		var pickups = 1;
		var lastMessage = "";

		for ( var i = 1; ( i < deposits.Length ) && ( i < 700 ) && ( playerData.m_terrainVehicle.GetRemainingVolume() > 0 ); i++ )
		{
			deposits[ i ].transform.position = BesideTheVehicle( terrainVehicle );

			new TVCargoButton().Execute();

			lastMessage = MessageList();

			pickups++;

			// out of the way again (a deposit that did not fit stays on the surface with what is left of it)
			deposits[ i ].transform.position += Vector3.right * 1000.0f;
		}

		var holdUsed = holdSize - playerData.m_terrainVehicle.GetRemainingVolume();

		// one more press with the hold full and a fresh deposit in reach
		deposits[ deposits.Length - 1 ].transform.position = BesideTheVehicle( terrainVehicle );

		new TVCargoButton().Execute();

		var fullMessage = MessageList();
		var holdUsedAfterFull = holdSize - playerData.m_terrainVehicle.GetRemainingVolume();

		deposits[ deposits.Length - 1 ].transform.position += Vector3.right * 1000.0f;

		Log( "deposits: the hold of " + Tools.VolumeToText( holdSize ) + " cubic meters was full after " + pickups + " deposits (in it: " + Tools.VolumeToText( holdUsed ) + ") | last pickup: " + lastMessage + " | one more press: " + fullMessage );

		Check( "deposits: the terrain vehicle's hold is full after 10 to 50 deposits, and never over full", ( holdUsed == holdSize ) && ( holdUsedAfterFull == holdSize ) && ( pickups >= 10 ) && ( pickups <= 50 ) && fullMessage.Contains( "full" ), pickups + " deposits, " + Tools.VolumeToText( holdUsed ) + " of " + Tools.VolumeToText( holdSize ) + " cubic meters, after one more press " + Tools.VolumeToText( holdUsedAfterFull ) );

		// ---- 4. the cargo display that is not in any scene yet shows the hold in cubic meters too (made here from its class)
		var displayObject = new GameObject( "probe cargo display" );
		var labelsObject = new GameObject( "labels" );
		var valuesObject = new GameObject( "values" );

		labelsObject.transform.SetParent( displayObject.transform );
		valuesObject.transform.SetParent( displayObject.transform );

		var cargoDisplay = displayObject.AddComponent<TerrainVehicleCargoDisplay>();

		cargoDisplay.m_labelsText = labelsObject.AddComponent<TMPro.TextMeshProUGUI>();
		cargoDisplay.m_valuesText = valuesObject.AddComponent<TMPro.TextMeshProUGUI>();

		Call( cargoDisplay, "UpdateCargoDisplay" );

		var displayValues = cargoDisplay.m_valuesText.text.Replace( "\n", "/" );

		Destroy( displayObject );

		Log( "deposits: the values column of TerrainVehicleCargoDisplay with a full hold: " + displayValues );

		Check( "deposits: the unused cargo display shows cubic meters", displayValues.Contains( Tools.VolumeToText( holdSize ) + "/" + Tools.VolumeToText( holdSize ) + " m" ), displayValues );

		// ---- 5. back into the ship: the ship's hold takes what fits, the rest stays in the terrain vehicle
		var shipFreeBefore = playerData.m_playerShip.GetRemainingVolume();

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		var shipFreeAfter = playerData.m_playerShip.GetRemainingVolume();
		var leftInVehicle = holdSize - playerData.m_terrainVehicle.GetRemainingVolume();
		var expectedMoved = Mathf.Min( shipFreeBefore, holdSize );

		Log( "deposits: back in the ship (" + playerData.m_general.m_location + "): the ship had room for " + Tools.VolumeToText( shipFreeBefore ) + " cubic meters and has room for " + Tools.VolumeToText( shipFreeAfter ) + " now; " + Tools.VolumeToText( leftInVehicle ) + " are still in the terrain vehicle" );

		Check( "deposits: the ship takes what fits and the rest stays in the terrain vehicle (control)", ( shipFreeAfter == shipFreeBefore - expectedMoved ) && ( shipFreeAfter >= 0 ) && ( leftInVehicle == holdSize - expectedMoved ), "moved " + ( shipFreeBefore - shipFreeAfter ) + " tenths (should be " + expectedMoved + "), left in the vehicle " + leftInVehicle );

		Finish( "scenario=deposits count=" + deposits.Length + " sizes=" + smallest + ".." + largest + " total=" + total + " placement=" + placement + " first=" + firstVolume + "/" + cargoAfterFirst + " toFill=" + pickups + " hold=" + holdUsed + "/" + holdSize + " ship=" + shipFreeBefore + "->" + shipFreeAfter + " leftInVehicle=" + leftInVehicle + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- rulings of 2026-10-05: a planet whose maps could not be generated looks plain and says so, and a game loaded in the terrain vehicle on such a planet goes back into orbit

	// "2048x1024" for a texture, "none" for no texture
	static string SizeOf( Texture texture )
	{
		return ( texture == null ) ? "none" : ( texture.width + "x" + texture.height );
	}

	// what Planet.CouldNotBeMapped says about a planet ("no such method" on code that does not have it)
	static string CouldNotBeMapped( Planet planetController )
	{
		var method = typeof( Planet ).GetMethod( "CouldNotBeMapped", c_any );

		return ( method == null ) ? "no such method" : method.Invoke( planetController, null ).ToString();
	}

	IEnumerator ScenarioUnmapped()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var starSystem = controller.m_starSystem;
		var messages = controller.m_messages;

		EnsureCrew();

		// the maps planet 90 has on its material now (the real ones, from when the star system was entered)
		var realAlbedo = starSystem.GetPlanetController( 90 ).GetMaterial().GetTexture( "_MainTex" );
		var realAlbedoSize = SizeOf( realAlbedo );

		// ---- 1. the star system is generated again, and this time the file of planet 90 cannot be read
		var garbage = new byte[ 4096 ];

		new System.Random( 12345 ).NextBytes( garbage );

		RegeneratePlanets();

		var badPlanet = starSystem.GetPlanetController( 90 );

		StartProcessing( badPlanet.GetPlanetGenerator(), garbage );

		var start = Time.realtimeSinceStartup;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 30.0f ) )
		{
			yield return null;
		}

		yield return Frames( 5 );

		if ( starSystem.GeneratingPlanets() || !badPlanet.GetPlanetGenerator().m_abort )
		{
			Finish( "scenario=unmapped abort: planet 90 was not left without maps", 2 );
			yield break;
		}

		var material = badPlanet.GetMaterial();
		var albedo = material.GetTexture( "_MainTex" );
		var specular = material.GetTexture( "SF_SpecularMap" );
		var normal = material.GetTexture( "SF_NormalMap" );
		var waterMask = material.GetTexture( "SF_WaterMaskMap" );

		var albedoSize = SizeOf( albedo );
		var keepsTheOldMaps = ( albedo != null ) && ReferenceEquals( albedo, realAlbedo );
		var oldMapsDestroyed = ( realAlbedo == null );
		var albedoColor = "";

		if ( ( albedo is Texture2D albedoTexture ) && albedoTexture.isReadable && ( albedoTexture.width <= 16 ) )
		{
			var pixel = albedoTexture.GetPixel( 1, 1 );

			albedoColor = pixel.r.ToString( "F2" ) + "/" + pixel.g.ToString( "F2" ) + "/" + pixel.b.ToString( "F2" );
		}

		var goodAlbedoSize = SizeOf( starSystem.GetPlanetController( 94 ).GetMaterial().GetTexture( "_MainTex" ) );
		var hasMaps = badPlanet.HasMaps();
		var couldNotBeMapped = CouldNotBeMapped( badPlanet );
		var goodCouldNotBeMapped = CouldNotBeMapped( starSystem.GetPlanetController( 94 ) );

		Log( "unmapped: planet 90 had an albedo map of " + realAlbedoSize + "; with a file that cannot be read its material has: albedo " + albedoSize + ( ( albedoColor == "" ) ? "" : " (" + albedoColor + ")" ) + ", specular " + SizeOf( specular ) + ", normal " + SizeOf( normal ) + ", water mask " + SizeOf( waterMask ) + " | still the maps from before " + keepsTheOldMaps + ", those destroyed " + oldMapsDestroyed + " | HasMaps " + hasMaps + ", CouldNotBeMapped " + couldNotBeMapped + " | planet 94: albedo " + goodAlbedoSize + ", CouldNotBeMapped " + goodCouldNotBeMapped );

		Check( "unmapped: a planet whose maps could not be generated gets plain maps, not the ones that were on its material", !keepsTheOldMaps && oldMapsDestroyed && ( albedoSize == "4x4" ) && ( SizeOf( specular ) == "4x4" ) && ( SizeOf( normal ) == "4x4" ) && ( SizeOf( waterMask ) == "4x4" ), "albedo " + albedoSize + " " + albedoColor + ", specular " + SizeOf( specular ) + ", normal " + SizeOf( normal ) + ", water mask " + SizeOf( waterMask ) + ", old maps kept " + keepsTheOldMaps + ", destroyed " + oldMapsDestroyed );
		Check( "unmapped: it still has no maps to land on, and the other planets have theirs (control)", !hasMaps && ( goodAlbedoSize == "2048x1024" ), "HasMaps " + hasMaps + ", planet 94 albedo " + goodAlbedoSize );
		Check( "unmapped: the planet says that it could not be mapped, the others do not", ( couldNotBeMapped == "True" ) && ( goodCouldNotBeMapped == "False" ), "planet 90 " + couldNotBeMapped + ", planet 94 " + goodCouldNotBeMapped );

		// ---- 2. flying up to it in the star system
		playerData.m_general.m_lastStarSystemCoordinates = badPlanet.transform.localPosition;

		SetField( starSystem, "m_planetToOrbitId", -1 );

		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );

		var rangeMessage = MessageList();
		var locationAtThePlanet = playerData.m_general.m_location;

		// ---- 3. into orbit around it
		yield return EnterOrbit( 90 );

		var orbitMessage = MessageList();
		var orbitAlbedoSize = SizeOf( controller.m_inOrbit.m_planetModel.material.GetTexture( "_MainTex" ) );

		Log( "unmapped: next to planet 90 in the star system (" + locationAtThePlanet + "): " + rangeMessage );
		Log( "unmapped: in orbit around planet 90 (" + playerData.m_general.m_location + "): " + orbitMessage + " | the albedo map of the planet model in orbit is " + orbitAlbedoSize );

		Check( "unmapped: within orbital range the player is told that the planet could not be mapped", rangeMessage.Contains( "Standing by to orbit" ) && rangeMessage.Contains( "could not be mapped" ), rangeMessage );
		Check( "unmapped: in orbit the player is told so too, and the planet model has the plain maps", orbitMessage.Contains( "Orbit established" ) && orbitMessage.Contains( "could not be mapped" ) && ( orbitAlbedoSize == "4x4" ), orbitMessage + " | albedo " + orbitAlbedoSize );

		// a planet with maps gets no such message (control)
		yield return EnterOrbit( 94 );

		var goodOrbitMessage = MessageList();

		Check( "unmapped: in orbit around a planet with maps there is no such message (control)", goodOrbitMessage.Contains( "Orbit established" ) && !goodOrbitMessage.Contains( "could not be mapped" ), goodOrbitMessage );

		// ---- 4. a game that was saved in the terrain vehicle on planet 90 is loaded after the planet's file has been damaged:
		// the spaceflight scene switches to the saved location, which generates the planets and shows the disembarked location (done here by hand, with the file of planet 90 unreadable)
		playerData.m_general.m_currentPlanetId = 90;
		playerData.m_general.m_currentSpeed = 12.0f;

		playerData.m_terrainVehicle.AddElement( 6, 30 );

		var shipFreeBefore = playerData.m_playerShip.GetRemainingVolume();
		var vehicleCargoBefore = TerrainVehicleCargo( 6 );
		var exceptionsBefore = s_exceptionCount;

		SetField( starSystem, "m_currentStar", null );

		controller.SwitchLocation( PD_General.Location.Disembarked );

		StartProcessing( starSystem.GetPlanetController( 90 ).GetPlanetGenerator(), garbage );

		var locationWhileGenerating = playerData.m_general.m_location;

		start = Time.realtimeSinceStartup;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 30.0f ) )
		{
			yield return null;
		}

		// a second of play after the load
		yield return Frames( 60 );

		var locationAfterLoad = playerData.m_general.m_location;
		var exceptionsAfterLoad = s_exceptionCount - exceptionsBefore;
		var messageAfterLoad = MessageList();
		var vehicleCargoAfter = TerrainVehicleCargo( 6 );
		var shipFreeAfter = playerData.m_playerShip.GetRemainingVolume();
		var saved = Stored( dataController.m_activeSaveGameSlotNumber );
		var savedLocation = ( saved == null ) ? "nothing saved" : saved.m_general.m_location.ToString();
		var console = Console();
		var followed = controller.m_playerCamera.GetCameraFollowGameObject();
		var speedAfterLoad = playerData.m_general.m_currentSpeed;

		// the plain maps of the first part were replaced by those of this generation (plain ones again) - they must not stay in memory
		var firstPlainMapsDestroyed = ( albedo == null ) && ( specular == null ) && ( normal == null ) && ( waterMask == null );
		var albedoAfterLoad = SizeOf( starSystem.GetPlanetController( 90 ).GetMaterial().GetTexture( "_MainTex" ) );

		Log( "unmapped: the four maps planet 90 had on its material after the first part are destroyed now: " + firstPlainMapsDestroyed + " (its albedo map now: " + albedoAfterLoad + ")" );

		Check( "unmapped: the plain maps are destroyed when the planet gets new ones", firstPlainMapsDestroyed && ( albedoAfterLoad == "4x4" ), "destroyed " + firstPlainMapsDestroyed + ", albedo now " + albedoAfterLoad );

		Log( "unmapped: loaded in the terrain vehicle on planet 90 (" + locationWhileGenerating + " while the planets were generated): a second later the location is " + locationAfterLoad + ", exceptions " + exceptionsAfterLoad + ", the save says " + savedLocation + ", console " + console + ", the camera follows " + ( ( followed == null ) ? "nothing" : followed.name ) + ", speed " + speedAfterLoad.ToString( "F1" ) );
		Log( "unmapped: messages after the load: " + messageAfterLoad );
		Log( "unmapped: the terrain vehicle carried " + vehicleCargoBefore + " tenths and carries " + vehicleCargoAfter + " now; the ship had room for " + shipFreeBefore + " and has room for " + shipFreeAfter );

		Check( "unmapped: a game loaded in the terrain vehicle on a planet without maps is put back in orbit, and nothing throws", ( locationWhileGenerating == PD_General.Location.Disembarked ) && ( locationAfterLoad == PD_General.Location.InOrbit ) && ( exceptionsAfterLoad == 0 ) && ( savedLocation == "InOrbit" ) && ( followed == null ), "location " + locationAfterLoad + ", exceptions " + exceptionsAfterLoad + ", saved as " + savedLocation + ", camera follows " + ( ( followed == null ) ? "nothing" : followed.name ) );
		Check( "unmapped: the player is told why", messageAfterLoad.Contains( "could not be mapped" ) && messageAfterLoad.Contains( "returned to orbit" ), messageAfterLoad );
		Check( "unmapped: the cargo of the terrain vehicle comes on board with it, and it stands still", ( vehicleCargoBefore == 30 ) && ( vehicleCargoAfter == 0 ) && ( shipFreeAfter == shipFreeBefore - 30 ) && ( speedAfterLoad == 0.0f ), "vehicle " + vehicleCargoBefore + " -> " + vehicleCargoAfter + ", ship room " + shipFreeBefore + " -> " + shipFreeAfter + ", speed " + speedAfterLoad.ToString( "F1" ) );

		// ---- 5. a game loaded in the terrain vehicle on a planet that has its maps stays there (control) - planet 94, the star system generated again from its real files
		playerData.m_general.m_currentPlanetId = 94;

		exceptionsBefore = s_exceptionCount;

		SetField( starSystem, "m_currentStar", null );

		controller.SwitchLocation( PD_General.Location.Disembarked );

		start = Time.realtimeSinceStartup;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 30.0f ) )
		{
			yield return null;
		}

		yield return Frames( 30 );

		var goodLocationAfterLoad = playerData.m_general.m_location;
		var goodExceptions = s_exceptionCount - exceptionsBefore;

		Log( "unmapped: loaded in the terrain vehicle on planet 94, which has its maps: location " + goodLocationAfterLoad + ", exceptions " + goodExceptions );

		Check( "unmapped: a game loaded in the terrain vehicle on a planet with maps stays in the terrain vehicle (control)", ( goodLocationAfterLoad == PD_General.Location.Disembarked ) && ( goodExceptions == 0 ), "location " + goodLocationAfterLoad + ", exceptions " + goodExceptions );

		Finish( "scenario=unmapped albedo=" + realAlbedoSize + "->" + albedoSize + " oldMapsKept=" + keepsTheOldMaps + " couldNotBeMapped=" + couldNotBeMapped + " rangeMessage=" + rangeMessage.Contains( "could not be mapped" ) + " orbitMessage=" + orbitMessage.Contains( "could not be mapped" ) + " loaded=" + locationAfterLoad + "/exceptions" + exceptionsAfterLoad + "/saved" + savedLocation + " cargo=" + vehicleCargoBefore + "->" + vehicleCargoAfter + " control=" + goodLocationAfterLoad + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- ruling of 2026-10-05: an encounter in orbit begins when the ship goes into orbit around its planet, and ends at the level of the star system

	// fly out of the encounter: the encounter location looks at how far the player is from its middle, so put the player beyond its edge and let it run
	static IEnumerator FlyOutOfEncounter()
	{
		DataController.m_instance.m_playerData.m_general.m_coordinates = new Vector3( 5000.0f, 0.0f, 0.0f );

		yield return Frames( 5 );
	}

	IEnumerator ScenarioOrbit()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var starSystem = controller.m_starSystem;

		EnsureCrew();

		// a ship that survives whatever it meets
		playerData.m_playerShip.m_armorPoints = 100000;

		// ---- the game data: the encounters in orbit, and whether their star has a planet in the orbit they name
		var inOrbitEncounters = 0;
		var withAPlanet = 0;
		var withoutAPlanet = "";

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			var gdEncounter = gameData.m_encounterList[ i ];

			if ( gdEncounter.m_location != 2 )
			{
				continue;
			}

			inOrbitEncounters++;

			var pdEncounter = playerData.FindEncounter( i );
			var found = false;

			foreach ( var planet in gameData.m_planetList )
			{
				if ( ( planet.m_starId == pdEncounter.GetStarId() ) && ( planet.m_orbitPosition == gdEncounter.m_orbitPosition ) )
				{
					found = true;
				}
			}

			if ( found )
			{
				withAPlanet++;
			}
			else
			{
				withoutAPlanet += i + " (" + gdEncounter.m_race + ", star " + pdEncounter.GetStarId() + ", orbit " + gdEncounter.m_orbitPosition + ") ";
			}
		}

		Log( "orbit: the game data has " + inOrbitEncounters + " encounters in orbit; " + withAPlanet + " of them name an orbit that has a planet; the others: " + ( ( withoutAPlanet == "" ) ? "none" : withoutAPlanet.Trim() ) );

		// ---- 0. the way out of the other encounters is unchanged (control): one that began in the star system, and one that began in hyperspace
		var range = controller.m_encounterRange * 1.25f;
		var speminId = FindEncounter( 1, 6, 3, 0 );

		var systemBefore = playerData.m_general.m_lastStarSystemCoordinates;
		var locationBefore = playerData.m_general.m_location;

		EnterEncounter( speminId );

		yield return Frames( 5 );
		yield return FlyOutOfEncounter();

		var locationAfterSystemEncounter = playerData.m_general.m_location;
		var systemMove = playerData.m_general.m_lastStarSystemCoordinates - systemBefore;

		// the same encounter again, this time as if the ship had met it in hyperspace
		EnterEncounter( speminId );

		yield return Frames( 5 );

		playerData.m_general.m_lastLocation = PD_General.Location.Hyperspace;

		var hyperspaceBefore = playerData.m_general.m_lastHyperspaceCoordinates;

		playerData.m_general.m_coordinates = new Vector3( 5000.0f, 0.0f, 0.0f );

		yield return null;
		yield return null;

		var locationAfterHyperspaceEncounter = playerData.m_general.m_location;
		var hyperspaceMove = playerData.m_general.m_lastHyperspaceCoordinates - hyperspaceBefore;

		Log( "orbit: out of an encounter that began in the star system (" + locationBefore + "): " + locationAfterSystemEncounter + ", the ship's place in the system moved by " + systemMove + " (encounter range times 1.25 is " + range.ToString( "F1" ) + ") | out of one that began in hyperspace: " + locationAfterHyperspaceEncounter + ", the ship's place in hyperspace moved by " + hyperspaceMove );

		Check( "orbit: the way out of an encounter that began in a star system or in hyperspace is unchanged (control)", ( locationBefore == PD_General.Location.StarSystem ) && ( locationAfterSystemEncounter == PD_General.Location.StarSystem ) && ( Vector3.Distance( systemMove, Vector3.right * range ) < 0.01f ) && ( locationAfterHyperspaceEncounter != PD_General.Location.Encounter ) && ( Vector3.Distance( hyperspaceMove, Vector3.right * range ) < 0.01f ), "star system: " + locationAfterSystemEncounter + " moved " + systemMove + " | hyperspace: " + locationAfterHyperspaceEncounter + " moved " + hyperspaceMove + " | expected (" + range.ToString( "F1" ) + ", 0, 0)" );

		// ---- the star of encounter 316 (a derelict in orbit 3 of star 30, and no other encounter in that star system)
		const int c_encounterId = 316;
		const int c_starId = 30;

		var theEncounter = gameData.m_encounterList[ c_encounterId ];

		var guardedPlanetId = -1;
		var otherPlanetId = -1;

		foreach ( var planet in gameData.m_planetList )
		{
			if ( planet.m_starId == c_starId )
			{
				if ( planet.m_orbitPosition == theEncounter.m_orbitPosition )
				{
					guardedPlanetId = planet.m_id;
				}
				else
				{
					otherPlanetId = planet.m_id;
				}
			}
		}

		if ( ( theEncounter.m_location != 2 ) || ( guardedPlanetId < 0 ) || ( otherPlanetId < 0 ) )
		{
			Finish( "scenario=orbit abort: encounter " + c_encounterId + " is not what this scenario expects (location " + theEncounter.m_location + ", planets " + guardedPlanetId + " and " + otherPlanetId + ")", 2 );
			yield break;
		}

		EnterStarSystem( c_starId );

		var start = Time.realtimeSinceStartup;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 40.0f ) )
		{
			yield return null;
		}

		yield return Frames( 5 );

		var systemCoordinates = playerData.m_general.m_lastStarSystemCoordinates;

		// ---- 1. into orbit around a planet nobody waits at (control)
		yield return EnterOrbit( otherPlanetId );

		var locationAtOtherPlanet = playerData.m_general.m_location;

		// ---- 2. into orbit around the planet the derelict is at
		var exceptionsBefore = s_exceptionCount;

		yield return EnterOrbit( guardedPlanetId );

		var locationAtGuardedPlanet = playerData.m_general.m_location;
		var encounterBegun = ( locationAtGuardedPlanet == PD_General.Location.Encounter );
		var currentEncounter = playerData.m_general.m_currentEncounterId;
		var cameFrom = playerData.m_general.m_lastLocation;
		var saved = Stored( dataController.m_activeSaveGameSlotNumber );
		var savedAs = ( saved == null ) ? "nothing saved" : ( saved.m_general.m_location + " from " + saved.m_general.m_lastLocation );

		// where the aliens are: on the side of the planet, seen from where the ship was in the star system
		var sideOfThePlanet = -2.0f;
		var shipsInTheEncounter = 0;

		if ( encounterBegun )
		{
			var planetPosition = starSystem.GetPlanetController( guardedPlanetId ).transform.localPosition;
			var towardsThePlanet = Vector3.Normalize( planetPosition - systemCoordinates );

			foreach ( var alienShip in controller.m_encounter.m_pdEncounter.GetAlienShipList() )
			{
				if ( alienShip.m_addedToEncounter && !alienShip.m_isDead )
				{
					shipsInTheEncounter++;

					sideOfThePlanet = Vector3.Dot( Vector3.Normalize( alienShip.m_coordinates ), towardsThePlanet );
				}
			}
		}

		Log( "orbit: in orbit around planet " + otherPlanetId + " (nobody waits there): " + locationAtOtherPlanet + " | around planet " + guardedPlanetId + " (encounter " + c_encounterId + " is in its orbit): " + locationAtGuardedPlanet + ", current encounter " + currentEncounter + ", came from " + cameFrom + ", saved as " + savedAs + ", ships " + shipsInTheEncounter + ", on the side of the planet " + sideOfThePlanet.ToString( "F3" ) );

		Check( "orbit: nothing begins in orbit around a planet nobody waits at (control)", locationAtOtherPlanet == PD_General.Location.InOrbit, locationAtOtherPlanet.ToString() );
		Check( "orbit: the encounter begins when the ship goes into orbit around its planet", encounterBegun && ( currentEncounter == c_encounterId ) && ( cameFrom == PD_General.Location.InOrbit ) && ( shipsInTheEncounter > 0 ), locationAtGuardedPlanet + ", encounter " + currentEncounter + ", from " + cameFrom + ", ships " + shipsInTheEncounter );
		Check( "orbit: its ships come from the side of the planet", sideOfThePlanet > 0.95f, sideOfThePlanet.ToString( "F3" ) );

		// ---- 3. flying out of it: back at the level of the star system, where the ship was, and nothing begins by itself there
		var locationAfterLeaving = "not left";
		var movedBy = -1.0f;
		var locationLater = "";

		if ( encounterBegun )
		{
			yield return FlyOutOfEncounter();

			locationAfterLeaving = playerData.m_general.m_location.ToString();
			movedBy = Vector3.Distance( playerData.m_general.m_lastStarSystemCoordinates, systemCoordinates );

			yield return Frames( 30 );

			locationLater = playerData.m_general.m_location.ToString();
		}

		Log( "orbit: after flying out of the encounter: " + locationAfterLeaving + ", " + movedBy.ToString( "F1" ) + " from where the ship was in the star system; 30 frames later: " + locationLater );

		Check( "orbit: an encounter from orbit ends at the level of the star system, where the ship was", ( locationAfterLeaving == "StarSystem" ) && ( movedBy == 0.0f ) && ( locationLater == "StarSystem" ), locationAfterLeaving + ", moved " + movedBy.ToString( "F1" ) + ", later " + locationLater );

		// ---- 4. back into orbit: the derelict is still there, so the encounter begins again
		var locationSecondTime = "not tried";

		if ( encounterBegun )
		{
			yield return EnterOrbit( guardedPlanetId );

			locationSecondTime = playerData.m_general.m_location.ToString();

			if ( playerData.m_general.m_location == PD_General.Location.Encounter )
			{
				yield return FlyOutOfEncounter();
			}
		}

		Check( "orbit: going back into orbit begins it again while it has living ships", locationSecondTime == "Encounter", locationSecondTime );

		// ---- 5. a launch from the surface of that planet: the ship is in orbit 17 seconds into the camera animation, which runs for 30 - the encounter waits for its end
		playerData.m_general.m_currentPlanetId = guardedPlanetId;

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		var locationOnTheSurface = playerData.m_general.m_location;

		PressButton( ButtonController.ButtonSet.CommandA, 0 );

		yield return Frames( 3 );

		PressButton( ButtonController.ButtonSet.Launch, 0 );

		// wait for the ship to be in orbit (or for the encounter, if it does not wait)
		yield return WaitForLocation( PD_General.Location.InOrbit, 25.0f );

		var inOrbitAt = Time.realtimeSinceStartup;
		var launchingWhenInOrbit = controller.m_playerCamera.IsLaunchingOrLanding();
		var locationWhenInOrbit = playerData.m_general.m_location;

		// two seconds later the animation is still running
		yield return new WaitForSecondsRealtime( 2.0f );

		var launchingTwoSecondsLater = controller.m_playerCamera.IsLaunchingOrLanding();
		var locationTwoSecondsLater = playerData.m_general.m_location;

		// now wait for the encounter
		yield return WaitForLocation( PD_General.Location.Encounter, 20.0f );

		var encounterAfter = Time.realtimeSinceStartup - inOrbitAt;
		var locationAfterLaunch = playerData.m_general.m_location;
		var launchingWhenItBegan = controller.m_playerCamera.IsLaunchingOrLanding();
		var consoleAfterLaunch = Console();

		Log( "orbit: launch from planet " + guardedPlanetId + " (" + locationOnTheSurface + "): in orbit with the launch animation running " + launchingWhenInOrbit + " (" + locationWhenInOrbit + "), two seconds later " + launchingTwoSecondsLater + " (" + locationTwoSecondsLater + "); " + encounterAfter.ToString( "F1" ) + " s after reaching orbit the location is " + locationAfterLaunch + " with the animation running " + launchingWhenItBegan + ", console " + consoleAfterLaunch );

		Check( "orbit: after a launch the encounter waits for the end of the launch animation", ( locationOnTheSurface == PD_General.Location.Planetside ) && launchingWhenInOrbit && launchingTwoSecondsLater && ( locationTwoSecondsLater == PD_General.Location.InOrbit ) && ( locationAfterLaunch == PD_General.Location.Encounter ) && !launchingWhenItBegan, "two seconds into orbit: " + locationTwoSecondsLater + " (animation " + launchingTwoSecondsLater + "), then " + locationAfterLaunch + " after " + encounterAfter.ToString( "F1" ) + " s (animation " + launchingWhenItBegan + ")" );

		// ---- 6. with its ships destroyed the encounter is over for good: the ship stays in orbit
		var locationWithNoShipsLeft = "not tried";

		if ( playerData.m_general.m_location == PD_General.Location.Encounter )
		{
			var alienShipList = controller.m_encounter.m_pdEncounter.GetAlienShipList();

			for ( var i = 0; i < alienShipList.Length; i++ )
			{
				Kill( i );
			}

			yield return Frames( 5 );
			yield return FlyOutOfEncounter();
			yield return EnterOrbit( guardedPlanetId );
			yield return Frames( 20 );

			locationWithNoShipsLeft = playerData.m_general.m_location.ToString();
		}

		var exceptions = s_exceptionCount - exceptionsBefore;

		Log( "orbit: with every ship of the encounter destroyed, in orbit around planet " + guardedPlanetId + ": " + locationWithNoShipsLeft + " | exceptions since the first orbit: " + exceptions );

		Check( "orbit: an encounter with no living ships does not begin, and nothing threw", ( locationWithNoShipsLeft == "InOrbit" ) && ( exceptions == 0 ), locationWithNoShipsLeft + ", exceptions " + exceptions );

		Finish( "scenario=orbit inOrbitEncounters=" + inOrbitEncounters + " withAPlanet=" + withAPlanet + " control=" + locationAtOtherPlanet + " begun=" + locationAtGuardedPlanet + "/" + currentEncounter + "/from" + cameFrom + " side=" + sideOfThePlanet.ToString( "F2" ) + " left=" + locationAfterLeaving + "/" + movedBy.ToString( "F0" ) + " again=" + locationSecondTime + " launch=" + locationTwoSecondsLater + "->" + locationAfterLaunch + "@" + encounterAfter.ToString( "F0" ) + "s noShips=" + locationWithNoShipsLeft + " exceptions=" + exceptions + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- ruling of 2026-10-05: the erosion pass of the planet generator tool gives the same result every time

	// a number made from every bit of a height map (two maps with the same number are taken to be the same)
	static long MapChecksum( float[,] map )
	{
		if ( map == null )
		{
			return -1;
		}

		long checksum = 17;

		foreach ( var value in map )
		{
			checksum = ( checksum * 31 + BitConverter.ToInt32( BitConverter.GetBytes( value ), 0 ) ) % 1000000007L;

			if ( checksum < 0 )
			{
				checksum += 1000000007L;
			}
		}

		return checksum;
	}

	IEnumerator ScenarioErosion()
	{
		var erosionType = Type.GetType( "PG_HydraulicErosion, Assembly-CSharp-Editor" );

		if ( erosionType == null )
		{
			Finish( "scenario=erosion abort: the planet generator tool was not found in the editor assembly", 2 );
			yield break;
		}

		yield return Frames( 2 );

		var erosion = Activator.CreateInstance( erosionType );
		var process = erosionType.GetMethod( "Process", c_any );

		// the tool's default settings: source, minimum elevation, xy scale, z scale, rain, sediment capacity, gravity, friction, evaporation, deposition, dissolving, step delta time, final blur radius
		object[] Arguments( float[,] map )
		{
			return new object[] { map, 0.0f, 10.0f, 400.0f, 1.0f, 100.0f, -9.8f, 0.5f, 1.0f, 5.0f, 4.0f, 0.005f, 3 };
		}

		// a bowl sends every drop towards the middle of the map. The tool rains on eight parts of the map, and when it does that at the same time
		// all eight write to the same cells in the middle - that is where an update gets lost
		const int c_runs = 6;

		var checksums = new long[ c_runs ];
		var seconds = new float[ c_runs ];
		var distinct = new HashSet<long>();
		var failed = 0;

		for ( var run = 0; run < c_runs; run++ )
		{
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			float[,] result = null;

			try
			{
				result = process.Invoke( erosion, Arguments( Bowl( 256, 128 ) ) ) as float[,];
			}
			catch ( Exception exception )
			{
				Log( "erosion: run " + run + " threw " + ( ( exception.InnerException != null ) ? exception.InnerException.GetType().Name : exception.GetType().Name ) );
			}

			seconds[ run ] = stopwatch.ElapsedMilliseconds / 1000.0f;
			checksums[ run ] = MapChecksum( result );

			if ( result == null )
			{
				failed++;
			}

			distinct.Add( checksums[ run ] );

			yield return null;
		}

		var checksumText = string.Join( " ", checksums );
		var secondsText = "";
		var total = 0.0f;

		foreach ( var value in seconds )
		{
			secondsText += value.ToString( "F2" ) + " ";

			total += value;
		}

		// the same once more on rough ground, where the drops go their own ways (a second kind of map, same question)
		var rough = new float[ 128, 256 ];

		var roughRandom = new System.Random( 4242 );

		for ( var y = 0; y < 128; y++ )
		{
			for ( var x = 0; x < 256; x++ )
			{
				rough[ y, x ] = 0.3f + 0.4f * (float) roughRandom.NextDouble();
			}
		}

		var roughDistinct = new HashSet<long>();

		for ( var run = 0; run < 3; run++ )
		{
			float[,] result = null;

			try
			{
				result = process.Invoke( erosion, Arguments( (float[,]) rough.Clone() ) ) as float[,];
			}
			catch ( Exception exception )
			{
				Log( "erosion: rough run " + run + " threw " + ( ( exception.InnerException != null ) ? exception.InnerException.GetType().Name : exception.GetType().Name ) );
			}

			if ( result == null )
			{
				failed++;
			}

			roughDistinct.Add( MapChecksum( result ) );

			yield return null;
		}

		Log( "erosion: " + c_runs + " runs of the erosion pass on the same 256 by 128 bowl with the default settings: " + distinct.Count + " different results (checksums " + checksumText + "), seconds per run " + secondsText.Trim() + " (" + ( total / c_runs ).ToString( "F2" ) + " on average), runs that gave no result " + failed + " | 3 runs on rough ground: " + roughDistinct.Count + " different results | processors " + Environment.ProcessorCount );

		Check( "erosion: the same map and the same settings give the same result every time", ( failed == 0 ) && ( distinct.Count == 1 ) && ( roughDistinct.Count == 1 ), distinct.Count + " different results in " + c_runs + " runs on a bowl, " + roughDistinct.Count + " in 3 on rough ground, " + failed + " runs with no result" );

		Finish( "scenario=erosion runs=" + c_runs + " distinct=" + distinct.Count + " roughDistinct=" + roughDistinct.Count + " checksum=" + checksums[ 0 ] + " secondsPerRun=" + ( total / c_runs ).ToString( "F2" ) + " processors=" + Environment.ProcessorCount + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- small entries of the review, approved on 2026-10-05: the stardate texts per frame, a statement in a neutral posture,
	// the contract resolver of the planet generator tool, the system display with a planet in an orbit it has no place for, the legend texture, and the limits on the ship's log loops

	IEnumerator ScenarioSmallFixes()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;

		EnsureCrew();

		yield return Frames( 2 );

		// ---- 1. the stardate texts: what a frame costs in which neither the day nor the hour changes, and that the texts still follow the time
		var general = new PD_General();

		general.Reset();

		general.m_day = 10;
		general.m_hour = 5;
		general.m_lastHour = 5;

		general.UpdateGameTime( 0.0f );

		var controlBytes = AllocatedByTheControl();
		var gameTimeBytes = Allocated( () => general.UpdateGameTime( 0.0f ), c_meterCalls );
		var gameTimeBytesPerCall = ( gameTimeBytes < 0 ) ? -1 : gameTimeBytes / c_meterCalls;

		var stardateAtFive = general.m_currentStardateYMD + " / " + general.m_currentStardateDHMY;

		general.m_hour = 6;
		general.m_lastHour = 6;

		general.UpdateGameTime( 0.0f );

		var stardateAtSix = general.m_currentStardateYMD + " / " + general.m_currentStardateDHMY;

		general.m_day = 40;

		general.UpdateGameTime( 0.0f );

		var stardateOnDayForty = general.m_currentStardateYMD + " / " + general.m_currentStardateDHMY;

		// a game that was loaded has its texts from the save and makes them again in its first update
		var loaded = JsonUtility.FromJson<PD_General>( JsonUtility.ToJson( general ) );

		loaded.m_currentStardateYMD = "stale";
		loaded.m_currentStardateDHMY = "stale";
		loaded.m_lastHour = loaded.m_hour;

		loaded.UpdateGameTime( 0.0f );

		var stardateAfterLoad = loaded.m_currentStardateYMD + " / " + loaded.m_currentStardateDHMY;

		Log( "game time: an update in which the hour does not change takes " + gameTimeBytesPerCall + " bytes (the control, an array of 256 bytes, measures " + controlBytes + ") | the texts: " + stardateAtFive + " -> an hour later " + stardateAtSix + " -> on day 40 " + stardateOnDayForty + " | after a load " + stardateAfterLoad );

		Check( "game time: the measurement works (control)", controlBytes >= 256, controlBytes + " bytes per call for an array of 256 bytes" );
		Check( "game time: an update in which the hour does not change takes no memory", gameTimeBytesPerCall == 0, gameTimeBytesPerCall + " bytes per call" );
		Check( "game time: the stardate texts follow the hour and the day, also after a load (control)", ( stardateAtFive == "4620-01-11 / 11.05-01-4620" ) && ( stardateAtSix == "4620-01-11 / 11.06-01-4620" ) && ( stardateOnDayForty == "4620-02-11 / 11.06-02-4620" ) && ( stardateAfterLoad == stardateOnDayForty ), stardateAtFive + " | " + stardateAtSix + " | " + stardateOnDayForty + " | loaded " + stardateAfterLoad );

		// ---- 2. the statement button with a neutral posture (set by hand - the console only gets to the comm buttons after a hail, which sets a posture): the game data has no neutral statement
		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );

		yield return Frames( 5 );

		encounter.Connect();

		yield return Frames( 2 );

		encounter.m_pdEncounter.m_playerStance = GD_Comm.Stance.Neutral;
		encounter.m_pdEncounter.m_lastSubjectFromPlayer = GD_Comm.Subject.None;

		controller.m_messages.Clear();

		var statementThrew = "nothing";

		try
		{
			new StatementButton().Execute();
		}
		catch ( Exception exception )
		{
			statementThrew = exception.GetType().Name;
		}

		var neutralStatement = MessageList();
		var subjectAfterNeutral = encounter.m_pdEncounter.m_lastSubjectFromPlayer;

		encounter.m_pdEncounter.m_playerStance = GD_Comm.Stance.Friendly;

		controller.m_messages.Clear();

		new StatementButton().Execute();

		var friendlyStatement = MessageList();
		var subjectAfterFriendly = encounter.m_pdEncounter.m_lastSubjectFromPlayer;

		Log( "statement: in a neutral posture the button threw " + statementThrew + " and the message box says: [" + neutralStatement + "] (last subject from the player: " + subjectAfterNeutral + ") | in a friendly posture: [" + friendlyStatement + "] (" + subjectAfterFriendly + ")" );

		Check( "statement: a neutral posture transmits nothing, and not the word ERROR", ( statementThrew == "nothing" ) && !neutralStatement.Contains( "ERROR" ) && !neutralStatement.Contains( "Transmitting" ) && ( subjectAfterNeutral == GD_Comm.Subject.None ), "[" + neutralStatement + "], last subject " + subjectAfterNeutral );
		Check( "statement: a friendly posture still transmits a statement (control)", friendlyStatement.Contains( "Transmitting" ) && !friendlyStatement.Contains( "ERROR" ) && ( subjectAfterFriendly == GD_Comm.Subject.Statement ), "[" + friendlyStatement + "], last subject " + subjectAfterFriendly );

		encounter.Disconnect();

		ClearMissiles();
		LeaveEncounter();

		yield return Frames( 10 );

		// ---- 3. the contract resolver of the planet generator tool (editor assembly, used by nothing): two includes for one type, and an include for a type that has ignores
		var resolverType = Type.GetType( "PG_ContractResolver, Assembly-CSharp-Editor" );

		var includesKept = -1;
		var includeAfterIgnore = "not run";

		if ( resolverType != null )
		{
			var resolver = Activator.CreateInstance( resolverType );
			var include = resolverType.GetMethod( "IncludeProperty", c_any );
			var ignore = resolverType.GetMethod( "IgnoreProperty", c_any );

			include.Invoke( resolver, new object[] { typeof( string ), new string[] { "first" } } );
			include.Invoke( resolver, new object[] { typeof( string ), new string[] { "second" } } );

			var includes = GetField( resolver, "m_includes" ) as Dictionary<Type, HashSet<string>>;

			includesKept = ( ( includes != null ) && includes.ContainsKey( typeof( string ) ) ) ? includes[ typeof( string ) ].Count : -1;

			ignore.Invoke( resolver, new object[] { typeof( int ), new string[] { "ignored" } } );

			try
			{
				include.Invoke( resolver, new object[] { typeof( int ), new string[] { "included" } } );

				includeAfterIgnore = "worked";
			}
			catch ( TargetInvocationException exception )
			{
				includeAfterIgnore = "threw " + exception.InnerException.GetType().Name;
			}
		}

		Log( "contract resolver: found " + ( resolverType != null ) + "; two includes for one type leave " + includesKept + " names; an include for a type that has ignores " + includeAfterIgnore );

		Check( "contract resolver: a second include for a type keeps the first, and a type with ignores can have includes", ( includesKept == 2 ) && ( includeAfterIgnore == "worked" ), includesKept + " names kept, include after ignore " + includeAfterIgnore );

		// ---- 4. the system display with a planet in an orbit it has no place for (orbit 9, set by hand - every planet of the game data is in orbit 1 to 8)
		var systemDisplay = controller.m_displayController.m_systemDisplay;

		GD_Planet somePlanet = null;

		foreach ( var planetController in controller.m_starSystem.m_planetController )
		{
			if ( ( planetController.m_planet != null ) && ( planetController.m_planet.m_id != -1 ) )
			{
				somePlanet = planetController.m_planet;
			}
		}

		var changeSystemThrew = "no planet found";

		if ( somePlanet != null )
		{
			var orbitBefore = somePlanet.m_orbitPosition;

			somePlanet.m_orbitPosition = 9;

			try
			{
				systemDisplay.ChangeSystem();

				changeSystemThrew = "nothing";
			}
			catch ( Exception exception )
			{
				changeSystemThrew = exception.GetType().Name;
			}

			somePlanet.m_orbitPosition = orbitBefore;

			systemDisplay.ChangeSystem();
		}

		Log( "system display: with a planet in orbit 9, ChangeSystem threw " + changeSystemThrew );

		Check( "system display: a planet in an orbit the display has no place for does not throw", changeSystemThrew == "nothing", changeSystemThrew );

		// ---- 5. the legend texture of the planet generator, which nothing ever made
		var legendField = typeof( PlanetGenerator ).GetField( "m_legendTexture", c_any );

		Check( "planet generator: the legend texture that nothing made is gone", legendField == null, ( legendField == null ) ? "not there" : "still there" );

		// ---- 6. the two scroll loops of the ship's log with a row height that scrolls nowhere. This comes last: the code without a limit never comes back from it.
		// The state is set by hand: three entries with nothing to show for them (their text has no height), and a list that is already scrolled down.
		// The game does not get there: since PR 55 a log is opened at its first entry, with the list at the top
		var shipsLog = controller.m_shipsLog;

		var blankLog = new List<PD_ShipsLog.Entry>();

		for ( var i = 0; i < 3; i++ )
		{
			blankLog.Add( new PD_ShipsLog.Entry( i, "", "Entry " + i, "Message " + i ) );
		}

		shipsLog.Show( blankLog );

		yield return Frames( 5 );

		var entriesText = GetField( shipsLog, "m_entries" ) as TMPro.TextMeshProUGUI;
		var rowHeight = entriesText.renderedHeight / blankLog.Count;

		SetField( shipsLog, "m_currentIndex", 1 );
		SetField( shipsLog, "m_currentEntriesOffset", 100.0f );

		Log( "ships log: three entries with no text have a row height of " + rowHeight.ToString( "G4" ) + "; calling UpdateDisplay with the list scrolled down by 100 (code without a limit on its loops does not come back from this call)" );

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		Call( shipsLog, "UpdateDisplay" );

		var cameBackAfter = stopwatch.ElapsedMilliseconds;

		shipsLog.Hide();

		yield return Frames( 3 );

		Log( "ships log: UpdateDisplay came back after " + cameBackAfter + " ms" );

		Check( "ships log: the scroll loops end with a row height that scrolls nowhere", cameBackAfter < 1000, "came back after " + cameBackAfter + " ms (row height " + rowHeight.ToString( "G4" ) + ")" );

		Finish( "scenario=smallfixes gameTimeBytes=" + gameTimeBytesPerCall + " statement=" + ( neutralStatement.Contains( "ERROR" ) ? "ERROR" : "nothing" ) + " resolver=" + includesKept + "/" + includeAfterIgnore + " systemDisplay=" + changeSystemThrew + " legendTexture=" + ( legendField != null ) + " shipsLog=" + cameBackAfter + "ms checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// ---------------------------------------------------------------- roadmap 0.1: the locations and orbits of encounters, corrected to the original game data

	// the planet of a star in the given orbit (-1 if the star has none there)
	static int PlanetInOrbit( int starId, int orbitPosition )
	{
		foreach ( var planet in DataController.m_instance.m_gameData.m_planetList )
		{
			if ( ( planet.m_starId == starId ) && ( planet.m_orbitPosition == orbitPosition ) )
			{
				return planet.m_id;
			}
		}

		return -1;
	}

	// goes into orbit around a planet of a star: returns through the out parameter the id of the encounter that began there (-1 for none), and flies out of it
	static IEnumerator OrbitAndSee( int starId, int planetId, int[] encounterBegun )
	{
		var playerData = DataController.m_instance.m_playerData;
		var starSystem = SpaceflightController.m_instance.m_starSystem;

		encounterBegun[ 0 ] = -1;

		if ( playerData.m_general.m_currentStarId != starId )
		{
			// the star system encounters of that star wait on the far side of the system, so that none of them reaches the ship before it is in orbit (star 32 has five Spemin groups)
			foreach ( var pdEncounter in playerData.m_encounterList )
			{
				if ( ( pdEncounter.GetLocation() == PD_General.Location.StarSystem ) && ( pdEncounter.GetStarId() == starId ) )
				{
					pdEncounter.SetCoordinates( new Vector3( -7500.0f, 0.0f, 0.0f ) );
				}
			}

			EnterStarSystem( starId );

			var start = Time.realtimeSinceStartup;

			while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 40.0f ) )
			{
				yield return null;
			}

			yield return Frames( 5 );
		}
		else if ( playerData.m_general.m_location != PD_General.Location.StarSystem )
		{
			SpaceflightController.m_instance.SwitchLocation( PD_General.Location.StarSystem );

			yield return Frames( 5 );
		}

		yield return EnterOrbit( planetId );

		if ( playerData.m_general.m_location == PD_General.Location.Encounter )
		{
			encounterBegun[ 0 ] = playerData.m_general.m_currentEncounterId;

			yield return FlyOutOfEncounter();
		}
		else if ( playerData.m_general.m_location == PD_General.Location.InOrbit )
		{
			// back to the level of the star system without going through an encounter
			SpaceflightController.m_instance.SwitchLocation( PD_General.Location.StarSystem );

			yield return Frames( 5 );
		}
	}

	IEnumerator ScenarioEncounterData()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;

		EnsureCrew();

		// a ship that survives whatever it meets (the Spemin home fleet has 255 ships)
		playerData.m_playerShip.m_armorPoints = 100000;

		// ---- 1. the game data: the three Thrynn scouts are hyperspace encounters, and the five encounters in orbit name the orbit the original gives them
		int[] scoutIds = { 144, 145, 146 };

		var scouts = "";
		var scoutsInHyperspace = true;

		foreach ( var id in scoutIds )
		{
			var location = gameData.m_encounterList[ id ].m_location;

			scouts += id + ":" + location + " ";

			scoutsInHyperspace = scoutsInHyperspace && ( location == 0 );
		}

		int[] orbitIds = { 139, 165, 301, 302, 305 };
		int[] originalOrbits = { 6, 4, 1, 5, 1 };

		var orbits = "";
		var orbitsAsInTheOriginal = true;

		for ( var i = 0; i < orbitIds.Length; i++ )
		{
			var orbit = gameData.m_encounterList[ orbitIds[ i ] ].m_orbitPosition;

			orbits += orbitIds[ i ] + ":" + orbit + " ";

			orbitsAsInTheOriginal = orbitsAsInTheOriginal && ( orbit == originalOrbits[ i ] );
		}

		Log( "encounterdata: game data location of the scouts (0 is hyperspace): " + scouts.Trim() + " | orbit of the five: " + orbits.Trim() + " (the original: 139:6 165:4 301:1 302:5 305:1)" );

		Check( "encounterdata: encounters 144 to 146 are hyperspace encounters in the game data", scoutsInHyperspace, scouts.Trim() );
		Check( "encounterdata: encounters 139, 165, 301, 302 and 305 name the orbit of the original", orbitsAsInTheOriginal, orbits.Trim() );

		// ---- 2. every encounter in orbit names an orbit its star has a planet in
		var inOrbit = 0;
		var withAPlanet = 0;
		var withoutAPlanet = "";

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			var gdEncounter = gameData.m_encounterList[ i ];

			if ( gdEncounter.m_location != 2 )
			{
				continue;
			}

			inOrbit++;

			if ( PlanetInOrbit( playerData.FindEncounter( i ).GetStarId(), gdEncounter.m_orbitPosition ) >= 0 )
			{
				withAPlanet++;
			}
			else
			{
				withoutAPlanet += i + " ";
			}
		}

		Log( "encounterdata: " + withAPlanet + " of " + inOrbit + " encounters in orbit name an orbit that has a planet; the others: " + ( ( withoutAPlanet == "" ) ? "none" : withoutAPlanet.Trim() ) );

		Check( "encounterdata: every encounter in orbit names an orbit that has a planet", ( inOrbit == 12 ) && ( withAPlanet == 12 ), withAPlanet + " of " + inOrbit + ", without: " + withoutAPlanet.Trim() );

		// ---- 3. a new game: the scouts are in hyperspace, at the place the game data gives them
		var newGame = new PlayerData();

		newGame.Reset();

		var newGameScouts = "";
		var newGameScoutsInHyperspace = true;

		foreach ( var id in scoutIds )
		{
			var pdEncounter = newGame.FindEncounter( id );
			var gdEncounter = gameData.m_encounterList[ id ];
			var expected = Tools.GameToWorldCoordinates( new Vector3( gdEncounter.m_xCoordinate, 0.0f, gdEncounter.m_yCoordinate ) );

			newGameScouts += id + ":" + pdEncounter.GetLocation() + " " + pdEncounter.m_homeCoordinates + " ";

			newGameScoutsInHyperspace = newGameScoutsInHyperspace && ( pdEncounter.GetLocation() == PD_General.Location.Hyperspace ) && ( Vector3.Distance( pdEncounter.m_homeCoordinates, expected ) < 0.01f ) && ( pdEncounter.m_currentCoordinates == pdEncounter.m_homeCoordinates );
		}

		Log( "encounterdata: the scouts in a new game: " + newGameScouts.Trim() );

		Check( "encounterdata: a new game has the scouts in hyperspace at their coordinates", newGameScoutsInHyperspace, newGameScouts.Trim() );

		// ---- 4. a save written before the correction: scout 144 in star 0, one of its ships destroyed; another encounter of that save as the control
		var oldSave = new PlayerData();

		oldSave.Reset();

		var oldScout = oldSave.FindEncounter( 144 );

		oldScout.m_location = PD_General.Location.StarSystem;
		oldScout.m_starId = 0;
		oldScout.m_homeCoordinates = new Vector3( 1234.0f, 0.0f, -567.0f );
		oldScout.m_currentCoordinates = new Vector3( 1300.0f, 0.0f, -500.0f );
		oldScout.m_alienShipList[ 0 ].m_isDead = true;
		oldScout.m_shownCommList.Add( 42 );

		const int c_controlId = 7;

		var control = oldSave.FindEncounter( c_controlId );
		var controlLocation = control.GetLocation();
		var controlStar = control.GetStarId();
		var controlHome = new Vector3( 2222.0f, 0.0f, 3333.0f );

		control.m_homeCoordinates = controlHome;
		control.m_currentCoordinates = controlHome;

		MemorySaveSystem.s_slots[ 1 ] = JsonUtility.ToJson( oldSave, true );

		PlayerData loaded = null;

		try
		{
			loaded = Call( dataController, "LoadPlayerData", 1 ) as PlayerData;
		}
		catch ( Exception exception )
		{
			Log( "encounterdata: loading threw " + exception.GetType().Name + ": " + exception.Message );
		}

		MemorySaveSystem.s_slots.Remove( 1 );

		var repairedText = "nothing loaded";
		var repaired = false;
		var kept = false;
		var controlText = "nothing loaded";
		var controlUnchanged = false;

		if ( loaded != null )
		{
			var scout = loaded.FindEncounter( 144 );
			var expected = Tools.GameToWorldCoordinates( new Vector3( gameData.m_encounterList[ 144 ].m_xCoordinate, 0.0f, gameData.m_encounterList[ 144 ].m_yCoordinate ) );

			repairedText = scout.GetLocation() + ", star " + scout.GetStarId() + ", home " + scout.m_homeCoordinates + ", now " + scout.m_currentCoordinates + " (expected " + expected + ")";
			repaired = ( scout.GetLocation() == PD_General.Location.Hyperspace ) && ( Vector3.Distance( scout.m_homeCoordinates, expected ) < 0.01f ) && ( scout.m_currentCoordinates == scout.m_homeCoordinates );
			kept = scout.m_alienShipList[ 0 ].m_isDead && scout.m_shownCommList.Contains( 42 );

			var loadedControl = loaded.FindEncounter( c_controlId );

			controlText = loadedControl.GetLocation() + ", star " + loadedControl.GetStarId() + ", home " + loadedControl.m_homeCoordinates;
			controlUnchanged = ( loadedControl.GetLocation() == controlLocation ) && ( loadedControl.GetStarId() == controlStar ) && ( loadedControl.m_homeCoordinates == controlHome ) && ( loadedControl.m_currentCoordinates == controlHome );
		}

		Log( "encounterdata: scout 144 of a save that has it in star 0, after loading: " + repairedText + " | its destroyed ship and its comm kept: " + kept + " | encounter " + c_controlId + " (control, " + controlLocation + " at star " + controlStar + "): " + controlText );

		Check( "encounterdata: a save that has scout 144 in a star system loads with it in hyperspace at its coordinates", repaired, repairedText );
		Check( "encounterdata: the repaired scout keeps its destroyed ship and what was said", kept, "kept " + kept );
		Check( "encounterdata: an encounter that is where the game data has it is left alone (control)", controlUnchanged, controlText );

		// ---- 5. in the game: the drone at the planet in orbit 5 of 143,115 (star 29, the planet with a Black Egg), and the Spemin home fleet at the one planet of 82,148 (star 32)
		var begun = new int[ 1 ];

		var blackEggPlanet = PlanetInOrbit( 29, 5 );
		var otherPlanet = PlanetInOrbit( 29, 7 );

		yield return OrbitAndSee( 29, otherPlanet, begun );

		var atOtherPlanet = begun[ 0 ];

		yield return OrbitAndSee( 29, blackEggPlanet, begun );

		var atBlackEggPlanet = begun[ 0 ];

		var speminPlanet = PlanetInOrbit( 32, 6 );

		yield return OrbitAndSee( 32, speminPlanet, begun );

		var atSpeminPlanet = begun[ 0 ];

		Log( "encounterdata: into orbit around planet " + otherPlanet + " of star 29: encounter " + atOtherPlanet + " | around planet " + blackEggPlanet + " of star 29: encounter " + atBlackEggPlanet + " | around planet " + speminPlanet + " of star 32: encounter " + atSpeminPlanet + " (-1 is none)" );

		Check( "encounterdata: nothing begins at the planet of star 29 the drone does not guard (control)", ( otherPlanet >= 0 ) && ( atOtherPlanet == -1 ), "planet " + otherPlanet + ", encounter " + atOtherPlanet );
		Check( "encounterdata: the Veloxi drone 302 begins at the planet in orbit 5 of star 29", ( blackEggPlanet >= 0 ) && ( atBlackEggPlanet == 302 ), "planet " + blackEggPlanet + ", encounter " + atBlackEggPlanet );
		Check( "encounterdata: the Spemin home fleet 139 begins at the planet in orbit 6 of star 32", ( speminPlanet >= 0 ) && ( atSpeminPlanet == 139 ), "planet " + speminPlanet + ", encounter " + atSpeminPlanet );

		Finish( "scenario=encounterdata scouts=" + scouts.Trim().Replace( ' ', ',' ) + " orbits=" + orbits.Trim().Replace( ' ', ',' ) + " inOrbitWithAPlanet=" + withAPlanet + "/" + inOrbit + " repaired=" + repaired + " star29=" + atOtherPlanet + "," + atBlackEggPlanet + " star32=" + atSpeminPlanet, 0 );
	}

	// ---------------------------------------------------------------- roadmap 0.2: a Veloxi drone lets the ship into orbit after the right answers to its numbers (STRINFO 2.3)

	// goes into orbit around a planet of a star (into the star system first if the ship is elsewhere) and stays there, or in whatever encounter begins
	static IEnumerator GoIntoOrbit( int starId, int planetId )
	{
		var playerData = DataController.m_instance.m_playerData;
		var starSystem = SpaceflightController.m_instance.m_starSystem;

		if ( ( playerData.m_general.m_currentStarId != starId ) || ( playerData.m_general.m_location == PD_General.Location.Hyperspace ) )
		{
			EnterStarSystem( starId );

			var start = Time.realtimeSinceStartup;

			while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 40.0f ) )
			{
				yield return null;
			}

			yield return Frames( 5 );
		}
		else if ( playerData.m_general.m_location != PD_General.Location.StarSystem )
		{
			SpaceflightController.m_instance.SwitchLocation( PD_General.Location.StarSystem );

			yield return Frames( 5 );
		}

		yield return EnterOrbit( planetId );
	}

	// waits until the aliens of the encounter ask a question (a drone: a number), or the time is up - returns it through the array (0 for none)
	static IEnumerator WaitForAlienQuestion( float seconds, int[] question )
	{
		var encounter = SpaceflightController.m_instance.m_encounter;
		var end = Time.realtimeSinceStartup + seconds;

		question[ 0 ] = 0;

		while ( Time.realtimeSinceStartup < end )
		{
			if ( ( encounter.m_pdEncounter != null ) && ( encounter.m_pdEncounter.m_lastQuestionFromAliens != 0 ) )
			{
				question[ 0 ] = encounter.m_pdEncounter.m_lastQuestionFromAliens;

				yield break;
			}

			yield return null;
		}
	}

	// how many times the message list has this text in it
	static int CountMessages( string text )
	{
		var count = 0;

		foreach ( var message in DataController.m_instance.m_playerData.m_general.m_messageList )
		{
			if ( message.Contains( text ) )
			{
				count++;
			}
		}

		return count;
	}

	IEnumerator ScenarioDrones()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;

		EnsureCrew();

		// a ship that survives a drone attack
		playerData.m_playerShip.m_armorPoints = 100000;

		const string c_grantedText = "Permission to orbit granted.";
		const string c_deniedText = "Permission to orbit denied.";

		// ---- 1. the game data: the two lines of STRINFO 2.3, from the file of recovered data (subjects 17 and 18 are new, so they are cast from numbers here)
		var grantedLine = "missing";
		var deniedLine = "missing";

		foreach ( var comm in gameData.m_commList )
		{
			if ( comm.m_race != GameData.Race.VeloxProbe )
			{
				continue;
			}

			if ( (int) comm.m_subject == 17 )
			{
				grantedLine = comm.m_id + ":" + comm.m_text;
			}
			else if ( (int) comm.m_subject == 18 )
			{
				deniedLine = comm.m_id + ":" + comm.m_text;
			}
		}

		Log( "drones: game data comms " + gameData.m_commList.Length + " | the drone's lines: granted " + grantedLine + " | denied " + deniedLine );

		Check( "drones: the game data has the drone's two lines from STRINFO, after the 556 of the original data", ( grantedLine == "1000:" + c_grantedText ) && ( deniedLine == "1001:" + c_deniedText ) && ( gameData.m_commList.Length == 558 ), "granted " + grantedLine + " | denied " + deniedLine + " | " + gameData.m_commList.Length + " comms" );

		// ---- 2. the drone at the planet in orbit 5 of star 29 (the planet with a Black Egg): three numbers, each answered right
		const int c_droneId = 302;
		const int c_starId = 29;

		var planetId = PlanetInOrbit( c_starId, 5 );
		var question = new int[ 1 ];

		yield return GoIntoOrbit( c_starId, planetId );

		var begun = ( playerData.m_general.m_location == PD_General.Location.Encounter ) && ( playerData.m_general.m_currentEncounterId == c_droneId );
		var numbers = "";
		var numbersAsked = 0;
		var numbersInRange = true;

		if ( begun )
		{
			encounter.Connect();

			yield return Frames( 2 );

			for ( var i = 0; i < 3; i++ )
			{
				yield return WaitForAlienQuestion( 8.0f, question );

				numbers += question[ 0 ] + " ";

				if ( question[ 0 ] == 0 )
				{
					break;
				}

				numbersAsked++;
				numbersInRange = numbersInRange && ( question[ 0 ] >= 1 ) && ( question[ 0 ] <= 99 );

				// the right answer: yes to a multiple of 6, no to any other number
				if ( ( question[ 0 ] % 6 ) == 0 )
				{
					new AnswerYesButton().Execute();
				}
				else
				{
					new AnswerNoButton().Execute();
				}

				yield return Frames( 3 );
			}
		}

		var grantedShown = CountMessages( c_grantedText );

		// the ship goes back into orbit by itself a few seconds later
		yield return WaitForLocation( PD_General.Location.InOrbit, 8.0f );

		var locationAfterGrant = playerData.m_general.m_location;
		var planetAfterGrant = playerData.m_general.m_currentPlanetId;

		yield return Frames( 30 );

		var locationLater = playerData.m_general.m_location;

		Log( "drones: into orbit around planet " + planetId + ": encounter begun " + begun + " | numbers asked: " + numbers.Trim() + " | granted shown " + grantedShown + " | then " + locationAfterGrant + " at planet " + planetAfterGrant + ", 30 frames later " + locationLater );

		Check( "drones: the drone asks three numbers from 1 to 99", begun && ( numbersAsked == 3 ) && numbersInRange, "begun " + begun + ", numbers " + numbers.Trim() );
		Check( "drones: three right answers get permission to orbit", grantedShown == 1, "granted shown " + grantedShown );
		Check( "drones: after permission the ship is back in orbit around the planet, and the drone does not begin again", ( locationAfterGrant == PD_General.Location.InOrbit ) && ( planetAfterGrant == planetId ) && ( locationLater == PD_General.Location.InOrbit ), locationAfterGrant + " at " + planetAfterGrant + ", later " + locationLater );

		// ---- 3. out to the level of the star system and into orbit again: the permission holds while the ship stays in the star system
		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );
		yield return EnterOrbit( planetId );

		var locationSecondOrbit = playerData.m_general.m_location;

		Check( "drones: going into orbit again in the same visit to the star system begins nothing", locationSecondOrbit == PD_General.Location.InOrbit, locationSecondOrbit.ToString() );

		// ---- 4. a new visit (through hyperspace): the drone asks again, and a wrong answer gets permission denied and an attack
		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 5 );
		yield return GoIntoOrbit( c_starId, planetId );

		var begunAgain = ( playerData.m_general.m_location == PD_General.Location.Encounter ) && ( playerData.m_general.m_currentEncounterId == c_droneId );
		var wrongNumber = 0;

		if ( begunAgain )
		{
			encounter.Connect();

			yield return Frames( 2 );
			yield return WaitForAlienQuestion( 8.0f, question );

			wrongNumber = question[ 0 ];

			// the message list keeps the last ten messages only - start counting from here
			controller.m_messages.Clear();

			// the wrong answer
			if ( ( wrongNumber % 6 ) == 0 )
			{
				new AnswerNoButton().Execute();
			}
			else
			{
				new AnswerYesButton().Execute();
			}

			yield return Frames( 3 );
		}

		var deniedShown = CountMessages( c_deniedText );
		var stanceAfterWrong = ( encounter.m_pdEncounter == null ) ? "none" : encounter.m_pdEncounter.m_alienStance.ToString();
		var connectedAfterWrong = ( encounter.m_pdEncounter != null ) && encounter.m_pdEncounter.m_connected;

		yield return FlyOutOfEncounter();

		var locationAfterDenial = playerData.m_general.m_location;

		Log( "drones: a new visit: encounter begun " + begunAgain + " | number " + wrongNumber + " answered wrong | denied shown " + deniedShown + ", stance " + stanceAfterWrong + ", comm link up " + connectedAfterWrong + " | after flying out: " + locationAfterDenial );

		Check( "drones: on a new visit to the star system the drone asks again", begunAgain && ( wrongNumber != 0 ), "begun " + begunAgain + ", number " + wrongNumber );
		Check( "drones: a wrong answer gets permission denied, and the drone ends the comm link and attacks", ( deniedShown == 1 ) && ( stanceAfterWrong == "Hostile" ) && !connectedAfterWrong, "denied shown " + deniedShown + ", stance " + stanceAfterWrong + ", link up " + connectedAfterWrong );
		Check( "drones: flying out after a refusal ends at the level of the star system", locationAfterDenial == PD_General.Location.StarSystem, locationAfterDenial.ToString() );

		// ---- 5. ending the comm link before the numbers are answered fails the test as well
		yield return EnterOrbit( planetId );

		var begunThird = ( playerData.m_general.m_location == PD_General.Location.Encounter ) && ( playerData.m_general.m_currentEncounterId == c_droneId );
		var stanceAfterTerminate = "not begun";

		if ( begunThird )
		{
			encounter.Connect();

			yield return Frames( 2 );
			yield return WaitForAlienQuestion( 8.0f, question );

			// the message list keeps the last ten messages only - start counting from here
			controller.m_messages.Clear();

			encounter.AddComm( GD_Comm.Subject.Terminate, true );

			yield return Frames( 3 );

			stanceAfterTerminate = encounter.m_pdEncounter.m_alienStance.ToString();

			yield return FlyOutOfEncounter();
		}

		var deniedAfterTerminate = CountMessages( c_deniedText );

		Log( "drones: comm link ended before answering: begun " + begunThird + ", stance " + stanceAfterTerminate + ", denied shown " + deniedAfterTerminate );

		Check( "drones: ending the comm link before answering gets permission denied and an attack", begunThird && ( stanceAfterTerminate == "Hostile" ) && ( deniedAfterTerminate == 1 ), "begun " + begunThird + ", stance " + stanceAfterTerminate + ", denied " + deniedAfterTerminate );

		Finish( "scenario=drones lines=" + ( grantedLine != "missing" ) + "/" + ( deniedLine != "missing" ) + " numbers=" + numbers.Trim().Replace( ' ', ',' ) + " granted=" + grantedShown + " afterGrant=" + locationAfterGrant + "/" + locationLater + " secondOrbit=" + locationSecondOrbit + " wrong=" + wrongNumber + "/" + stanceAfterWrong + " terminated=" + stanceAfterTerminate, 0 );
	}

	// ---------------------------------------------------------------- roadmap 0.3: the game clock runs wherever the ship is out in space

	// the game time in whole game seconds
	static long GameSeconds()
	{
		var general = DataController.m_instance.m_playerData.m_general;

		return ( ( (long) general.m_day * 24 + general.m_hour ) * 60 + general.m_minute ) * 60 + general.m_second;
	}

	// how many game seconds pass in the current location in the given real seconds (the location is read again at the end, so a location that the game leaves by itself shows up)
	static IEnumerator ClockIn( float realSeconds, long[] passed, string[] where )
	{
		var start = GameSeconds();
		var end = Time.realtimeSinceStartup + realSeconds;

		while ( Time.realtimeSinceStartup < end )
		{
			yield return null;
		}

		passed[ 0 ] = GameSeconds() - start;
		where[ 0 ] = DataController.m_instance.m_playerData.m_general.m_location.ToString();
	}

	IEnumerator ScenarioGameClock()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// a ship that survives whatever it meets
		playerData.m_playerShip.m_armorPoints = 100000;

		var passed = new long[ 1 ];
		var where = new string[ 1 ];
		var results = "";

		// ---- 1. the star system (the clock ran here before - control)
		yield return ClockIn( 1.5f, passed, where );

		var inStarSystem = passed[ 0 ];
		var starSystemWhere = where[ 0 ];

		results += where[ 0 ] + "=" + passed[ 0 ] + " ";

		// ---- 2. in orbit around planet 90 (in the Arth system, which the scenario starts in; nobody waits in its orbit; the m10 scenario lands on it too)
		yield return EnterOrbit( 90 );
		yield return ClockIn( 1.5f, passed, where );

		var inOrbit = passed[ 0 ];
		var orbitWhere = where[ 0 ];

		results += where[ 0 ] + "=" + passed[ 0 ] + " ";

		// ---- 3. on its surface (the way the orbit scenario puts the ship down, without the 35 s landing)
		var planetsideWhere = "not reached";
		long onSurface = -1;

		try
		{
			controller.m_planetside.UpdateTerrainGridNow();
			controller.SwitchLocation( PD_General.Location.Planetside );
		}
		catch ( Exception exception )
		{
			Log( "gameclock: putting the ship down threw " + exception.GetType().Name + ": " + exception.Message );
		}

		yield return Frames( 10 );
		yield return ClockIn( 1.5f, passed, where );

		onSurface = passed[ 0 ];
		planetsideWhere = where[ 0 ];

		results += where[ 0 ] + "=" + passed[ 0 ] + " ";

		// ---- 4. in the terrain vehicle, through the real Disembark button
		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );
		yield return ClockIn( 1.5f, passed, where );

		var inVehicle = passed[ 0 ];
		var vehicleWhere = where[ 0 ];

		results += where[ 0 ] + "=" + passed[ 0 ] + " ";

		// ---- 5. in an encounter, with the shields up across a star hour: the hour's fuel for the shields
		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );

		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );

		yield return Frames( 5 );
		yield return ClockIn( 1.5f, passed, where );

		var inEncounter = passed[ 0 ];
		var encounterWhere = where[ 0 ];

		results += where[ 0 ] + "=" + passed[ 0 ] + " ";

		// the shields up and the clock just before the next hour (a star hour is about 20 real seconds)
		playerData.m_playerShip.RaiseShields();

		yield return Frames( 2 );

		var general = playerData.m_general;

		general.m_minute = 59;
		general.m_second = 0;

		var hourBefore = general.m_hour;
		var fuelBefore = Endurium();

		yield return new WaitForSecondsRealtime( 1.5f );

		var hourAfter = general.m_hour;
		var fuelAfter = Endurium();
		var stillInEncounter = playerData.m_general.m_location == PD_General.Location.Encounter;

		playerData.m_playerShip.DropShields();

		Log( "gameclock: in the encounter with the shields up from minute 59: hour " + hourBefore + " -> " + hourAfter + ", Endurium " + fuelBefore + " -> " + fuelAfter + " tenths, still in the encounter " + stillInEncounter );

		yield return FlyOutOfEncounter();

		// ---- 6. the docking bay is part of the starport: no clock there (control)
		controller.SwitchLocation( PD_General.Location.DockingBay );

		yield return Frames( 10 );
		yield return ClockIn( 1.5f, passed, where );

		var inDockingBay = passed[ 0 ];
		var dockingBayWhere = where[ 0 ];

		results += where[ 0 ] + "=" + passed[ 0 ] + " ";

		Log( "gameclock: game seconds passed in 1.5 real seconds: " + results.Trim() + " (at one game year for 50 hours of play, 1.5 real seconds are about 263 game seconds)" );

		// about 175 game seconds pass in a real second; allow for the frame time cap of slow frames
		Check( "gameclock: the clock runs in the star system (control)", ( starSystemWhere == "StarSystem" ) && ( inStarSystem > 100 ), starSystemWhere + " " + inStarSystem );
		Check( "gameclock: the clock runs in orbit", ( orbitWhere == "InOrbit" ) && ( inOrbit > 100 ), orbitWhere + " " + inOrbit );
		Check( "gameclock: the clock runs on a planet's surface", ( planetsideWhere == "Planetside" ) && ( onSurface > 100 ), planetsideWhere + " " + onSurface );
		Check( "gameclock: the clock runs in the terrain vehicle", ( vehicleWhere == "Disembarked" ) && ( inVehicle > 100 ), vehicleWhere + " " + inVehicle );
		Check( "gameclock: the clock runs in an encounter", ( encounterWhere == "Encounter" ) && ( inEncounter > 100 ), encounterWhere + " " + inEncounter );
		Check( "gameclock: raised shields use their fuel at the star hour in an encounter", stillInEncounter && ( hourAfter != hourBefore ) && ( fuelAfter < fuelBefore ), "hour " + hourBefore + " -> " + hourAfter + ", Endurium " + fuelBefore + " -> " + fuelAfter );
		Check( "gameclock: the clock stands still in the docking bay (control)", ( dockingBayWhere == "DockingBay" ) && ( inDockingBay == 0 ), dockingBayWhere + " " + inDockingBay );

		Finish( "scenario=gameclock " + results.Trim().Replace( ' ', ',' ) + " shieldHour=" + hourBefore + "->" + hourAfter + " fuel=" + fuelBefore + "->" + fuelAfter, 0 );
	}

	// ---------------------------------------------------------------- alien ship models

	// the lengths the procedural stand-ins are built to (DevTools/ShipModels, rulings of 2026-10-09): vessel id, length in units
	static readonly Dictionary<int, float> c_shipModelLengths = new Dictionary<int, float>()
	{
		// veloxi transport, scout, warship and drone (STRINFO 1, 0.6, 1 and 0.3 times the player ship)
		{ 11, 51.06f }, { 12, 30.636f }, { 13, 51.06f }, { 18, 15.318f },
		// thrynn transport, scout and warship (1.2, 0.4 and 0.8)
		{ 8, 61.272f }, { 9, 20.424f }, { 10, 40.848f },
		// elowan transport, scout and warship (0.7, 0.1 shown at 0.3, and 0.3)
		{ 5, 35.742f }, { 6, 15.318f }, { 7, 15.318f },
		// uhlek scout and warship (2 and 10, shown at 5.0)
		{ 16, 102.12f }, { 17, 256.213f },
		// gazurtoid scout and warship (60 and 90, shown at 7.0 and 7.5)
		{ 14, 357.844f }, { 15, 380.843f },
		// the noah 9 derelict (4) and the enterprise (38, shown at 6.5)
		{ 23, 204.24f }, { 21, 331.936f },
	};

	// the size of the meshes under model, measured along the axes of container (the ship's own frame: +Z is its nose)
	static Vector3 SizeInFrameOf( Transform container, GameObject model )
	{
		return SizeInFrameOf( container.worldToLocalMatrix, model );
	}

	// the size of a ship model template at rest, posed as its clone is in its container (Encounter.Start puts the container at the identity before it clones
	// the template with the template's own local rotation and scale): what an animated model is made of, wherever its parts have moved to in the encounter
	static Vector3 RestSizeOf( GameObject template )
	{
		var pose = Matrix4x4.TRS( Vector3.zero, template.transform.localRotation, template.transform.localScale );

		return SizeInFrameOf( pose * template.transform.worldToLocalMatrix, template );
	}

	// the size of the meshes under model, measured along the axes of the frame that toContainer takes world space to
	static Vector3 SizeInFrameOf( Matrix4x4 toContainer, GameObject model )
	{
		var bounds = new Bounds();
		var first = true;

		var meshes = new List<KeyValuePair<Mesh, Transform>>();

		foreach ( var filter in model.GetComponentsInChildren<MeshFilter>( true ) )
		{
			meshes.Add( new KeyValuePair<Mesh, Transform>( filter.sharedMesh, filter.transform ) );
		}

		foreach ( var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>( true ) )
		{
			meshes.Add( new KeyValuePair<Mesh, Transform>( skinned.sharedMesh, skinned.transform ) );
		}

		foreach ( var pair in meshes )
		{
			if ( pair.Key == null )
			{
				continue;
			}

			var matrix = toContainer * pair.Value.localToWorldMatrix;
			var meshBounds = pair.Key.bounds;

			for ( var corner = 0; corner < 8; corner++ )
			{
				var sign = new Vector3( ( corner & 1 ) == 0 ? -1.0f : 1.0f, ( corner & 2 ) == 0 ? -1.0f : 1.0f, ( corner & 4 ) == 0 ? -1.0f : 1.0f );
				var point = matrix.MultiplyPoint3x4( meshBounds.center + Vector3.Scale( meshBounds.extents, sign ) );

				if ( first )
				{
					bounds = new Bounds( point, Vector3.zero );
					first = false;
				}
				else
				{
					bounds.Encapsulate( point );
				}
			}
		}

		return bounds.size;
	}

	// the middle of the meshes under model along the nose axis (+Z) of container
	static float SizeCenterAlongNose( Transform container, GameObject model )
	{
		var low = float.MaxValue;
		var high = float.MinValue;

		foreach ( var filter in model.GetComponentsInChildren<MeshFilter>( true ) )
		{
			if ( filter.sharedMesh == null )
			{
				continue;
			}

			var bounds = filter.sharedMesh.bounds;

			for ( var corner = 0; corner < 8; corner++ )
			{
				var sign = new Vector3( ( corner & 1 ) == 0 ? -1.0f : 1.0f, ( corner & 2 ) == 0 ? -1.0f : 1.0f, ( corner & 4 ) == 0 ? -1.0f : 1.0f );
				var z = container.InverseTransformPoint( filter.transform.TransformPoint( bounds.center + Vector3.Scale( bounds.extents, sign ) ) ).z;

				low = Mathf.Min( low, z );
				high = Mathf.Max( high, z );
			}
		}

		return ( low <= high ) ? ( ( low + high ) / 2.0f ) : 0.0f;
	}

	// where the parts with an engine material are, along the ship's nose axis (+Z), in the frame of container; NaN if there are none
	static float EngineCenterAlongNose( Transform container, GameObject model )
	{
		var total = 0.0f;
		var count = 0;

		foreach ( var renderer in model.GetComponentsInChildren<MeshRenderer>( true ) )
		{
			var filter = renderer.GetComponent<MeshFilter>();

			if ( ( filter == null ) || ( filter.sharedMesh == null ) )
			{
				continue;
			}

			var materials = renderer.sharedMaterials;

			for ( var i = 0; ( i < materials.Length ) && ( i < filter.sharedMesh.subMeshCount ); i++ )
			{
				if ( ( materials[ i ] != null ) && materials[ i ].name.Contains( "Engine" ) )
				{
					var center = filter.sharedMesh.GetSubMesh( i ).bounds.center;

					total += container.InverseTransformPoint( filter.transform.TransformPoint( center ) ).z;
					count++;
				}
			}
		}

		return ( count > 0 ) ? ( total / count ) : float.NaN;
	}

	static bool IsPlaceholderModel( GameObject template )
	{
		return ( template == null ) || ( template.name == "Not Modeled Yet" );
	}

	// alien ship models: an empty template slot in Encounter.Start, which vessels still use the placeholder, and for every vessel
	// in a real hyperspace encounter the model it gets, its size in the ship's frame, and the debris it leaves
	IEnumerator ScenarioShipModels()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var encounter = SpaceflightController.m_instance.m_encounter;
		var templates = encounter.m_alienShipModelTemplate;
		var debrisTemplates = encounter.m_alienShipDebrisTemplate;

		Check( "a model slot for every vessel", templates.Length == gameData.m_vesselList.Length, "slots=" + templates.Length + " vessels=" + gameData.m_vesselList.Length );
		Check( "a debris slot for every vessel", ( debrisTemplates != null ) && ( debrisTemplates.Length == gameData.m_vesselList.Length ), "slots=" + ( ( debrisTemplates == null ) ? -1 : debrisTemplates.Length ) );

		// 1. Encounter.Start turns every model template off; an empty slot must not stop it
		var exceptionsBeforeStart = s_exceptionCount;
		var keptTemplate = templates[ 0 ];

		templates[ 0 ] = null;
		Call( encounter, "Start" );
		templates[ 0 ] = keptTemplate;

		Check( "Encounter.Start steps over an empty model slot", s_exceptionCount == exceptionsBeforeStart, "exceptions=" + ( s_exceptionCount - exceptionsBeforeStart ) );

		// 2. which vessels still clone the placeholder (a stretched sphere with the UV grid material)
		var placeholders = "";

		for ( var vesselId = 1; vesselId < templates.Length; vesselId++ )
		{
			if ( IsPlaceholderModel( templates[ vesselId ] ) )
			{
				placeholders += ( ( placeholders.Length > 0 ) ? "," : "" ) + vesselId;
			}
		}

		Log( "vessels with the placeholder model: " + placeholders );

		Check( "no vessel clones the placeholder model", placeholders.Length == 0, "vessels=" + placeholders );

		foreach ( var pair in c_shipModelLengths )
		{
			var template = ( pair.Key < templates.Length ) ? templates[ pair.Key ] : null;

			Check( "vessel " + pair.Key + " has its own model", !IsPlaceholderModel( template ), "template=" + ( ( template == null ) ? "none" : template.name ) );
		}

		// 3. every vessel in a hyperspace encounter (all its ships are shown at once)
		var encounterId = -1;

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			if ( ( gameData.m_encounterList[ i ].m_location == 0 ) && ( gameData.m_encounterList[ i ].m_maxNumShips >= 1 ) )
			{
				encounterId = i;
				break;
			}
		}

		if ( encounterId < 0 )
		{
			Finish( "abort: no hyperspace encounter", 2 );
			yield break;
		}

		var sizes = "";
		var exceptionsBeforeEncounters = s_exceptionCount;

		for ( var vesselId = 1; vesselId < templates.Length; vesselId++ )
		{
			// a fresh encounter with every ship of this vessel type, and a ship that survives what they fire
			playerData.FindEncounter( encounterId ).Reset( encounterId );
			ForceVessel( encounterId, vesselId );
			playerData.m_playerShip.m_armorPoints = playerData.m_playerShip.GetMaximumArmorPoints();

			EnterEncounter( encounterId );
			yield return Frames( 10 );

			var vesselName = gameData.m_vesselList[ vesselId ].m_name;
			var model = encounter.GetAlienShipModel( 0 );
			var hasModel = ( model != null ) && model.activeSelf && ( model.transform.childCount > 0 );
			var size = hasModel ? SizeInFrameOf( model.transform, model ) : Vector3.zero;
			var longest = Mathf.Max( size.x, size.y, size.z );

			sizes += " " + vesselId + ":" + size.x.ToString( "F1" ) + "x" + size.y.ToString( "F1" ) + "x" + size.z.ToString( "F1" );

			Check( "vessel " + vesselId + " (" + vesselName + ") gets a model in an encounter", hasModel, "model=" + ( ( model == null ) ? "none" : model.name ) );

			// the smallest vessels are shown at 0.3 times the player ship (3.404 units long in its FBX, scale 15)
			Check( "vessel " + vesselId + " is big enough to be seen (a quarter of the player ship or more)", longest >= 3.404f * 15.0f * 0.25f, "longest side=" + longest.ToString( "F1" ) );

			float expectedLength;

			if ( c_shipModelLengths.TryGetValue( vesselId, out expectedLength ) )
			{
				Check( "vessel " + vesselId + " is " + expectedLength.ToString( "F1" ) + " units long", Mathf.Abs( longest - expectedLength ) <= expectedLength * 0.05f, "size=" + size.ToString( "F1" ) );
				Check( "vessel " + vesselId + " is longest along its nose", size.z >= Mathf.Max( size.x, size.y ) * 0.999f, "size=" + size.ToString( "F1" ) );

				// the ship flies along +Z of its model container, so its engines have to be behind the middle of the bounds
				var engines = EngineCenterAlongNose( model.transform, model );

				if ( !float.IsNaN( engines ) )
				{
					var middle = SizeCenterAlongNose( model.transform, model );

					Check( "vessel " + vesselId + " has its engines at the back", engines < middle, "engines z=" + engines.ToString( "F1" ) + " middle z=" + middle.ToString( "F1" ) );
				}
			}

			// the debris: destroy the first ship (the debris replaces it at once, in CombatController.ApplyDamageToAlien, before the explosion has played)
			if ( hasModel && ( debrisTemplates != null ) && ( vesselId < debrisTemplates.Length ) && ( debrisTemplates[ vesselId ] != null ) )
			{
				Kill( 0 );

				// measure the debris in the ship's frame before a frame has passed: its DebrisTumble turns it by up to 17 degrees a second from its first update, and one
				// slow headless frame (up to Time.maximumDeltaTime, a third of a second) turned it far enough to fail the shape check below
				var tumble = model.GetComponentInChildren<DebrisTumble>( false );

				var debrisSize = ( tumble != null ) ? SizeInFrameOf( model.transform, tumble.gameObject ) : Vector3.zero;
				var debrisLongest = Mathf.Max( debrisSize.x, debrisSize.y, debrisSize.z );
				var ratio = ( longest > 0.0f ) ? ( debrisLongest / longest ) : 0.0f;

				// the debris is turned like the ship if its proportions along the ship's axes are those of the ship, each within a tenth of the longest side, measured
				// against the ship at rest: the Mysterion (vessel 20) is a cloud of orbs that Mysterion.Update moves with the game time, so its shape when it is
				// destroyed depends on how long the encounter's first frames took (90.6 x 80.0 x 90.6 in one run, 87.1 x 80.0 x 87.1 in another)
				var restSize = RestSizeOf( templates[ vesselId ] );
				var restLongest = Mathf.Max( restSize.x, restSize.y, restSize.z );
				var sameShape = ( debrisLongest > 0.0f ) && ( restLongest > 0.0f );

				for ( var axis = 0; sameShape && ( axis < 3 ); axis++ )
				{
					sameShape &= Mathf.Abs( debrisSize[ axis ] / debrisLongest - restSize[ axis ] / restLongest ) <= 0.1f;
				}

				sizes += "(debris " + ratio.ToString( "F2" ) + ( sameShape ? "" : " turned" ) + ")";

				Check( "vessel " + vesselId + " leaves debris of its own size", ( tumble != null ) && ( Mathf.Abs( ratio - 1.0f ) <= 0.1f ), "debris=" + debrisSize.ToString( "F1" ) + " ratio=" + ratio.ToString( "F2" ) );
				Check( "vessel " + vesselId + " leaves debris turned like the ship", ( tumble != null ) && sameShape, "debris=" + debrisSize.ToString( "F1" ) + " ship at rest=" + restSize.ToString( "F1" ) + " ship=" + size.ToString( "F1" ) );
			}

			ClearMissiles();
			LeaveEncounter();
			yield return Frames( 5 );
		}

		Check( "no exceptions in the encounters", s_exceptionCount == exceptionsBeforeEncounters, "exceptions=" + ( s_exceptionCount - exceptionsBeforeEncounters ) );

		Finish( "scenario=shipmodels placeholders=" + placeholders + " encounter=" + encounterId + " sizes=" + sizes.Trim() + " playerDestroyed=" + CombatController.m_instance.PlayerIsDestroyed(), 0 );
	}

	// ---------------------------------------------------------------- roadmap 0.4: what the original had and the game data file lacks, recovered from STRINFO

	// a field of an object by reflection, or null if the object or the field is not there (the recovered data lists do not exist in the code before them)
	static object FieldOrNull( object target, string name )
	{
		if ( target == null )
		{
			return null;
		}

		var field = target.GetType().GetField( name, c_any );

		return ( field == null ) ? null : field.GetValue( target );
	}

	static int IntFieldOrMin( object target, string name )
	{
		var value = FieldOrNull( target, name );

		return ( value is int ) ? (int) value : int.MinValue;
	}

	// the records of one of the recovered data lists of the game data (empty if the list is not there)
	static List<object> RecoveredList( string name )
	{
		var list = new List<object>();

		if ( FieldOrNull( DataController.m_instance.m_gameData, name ) is Array array )
		{
			foreach ( var item in array )
			{
				list.Add( item );
			}
		}

		return list;
	}

	// the planet the first record with this value in this field was found at (int.MinValue if there is no such record)
	static int PlanetOfRecord( List<object> list, string field, object value, string field2 = null, object value2 = null )
	{
		foreach ( var record in list )
		{
			if ( !Equals( FieldOrNull( record, field ), value ) )
			{
				continue;
			}

			if ( ( field2 != null ) && !Equals( FieldOrNull( record, field2 ), value2 ) )
			{
				continue;
			}

			return IntFieldOrMin( record, "m_planetId" );
		}

		return int.MinValue;
	}

	// the planet an encounter in orbit waits at (from the game data's orbit, not from the recovered data)
	static int PlanetOfOrbitEncounter( int encounterId )
	{
		var playerData = DataController.m_instance.m_playerData;
		var gdEncounter = DataController.m_instance.m_gameData.m_encounterList[ encounterId ];

		return PlanetInOrbit( playerData.FindEncounter( encounterId ).GetStarId(), gdEncounter.m_orbitPosition );
	}

	IEnumerator ScenarioRecoveredData()
	{
		var gameData = DataController.m_instance.m_gameData;

		yield return null;

		var messages = RecoveredList( "m_planetMessageList" );
		var sites = RecoveredList( "m_artifactSiteList" );
		var evaluations = RecoveredList( "m_colonyEvaluationList" );
		var storyTexts = RecoveredList( "m_storyTextList" );

		var counts = messages.Count + "/" + sites.Count + "/" + evaluations.Count + "/" + storyTexts.Count;

		Log( "recovereddata: planet messages / artifact sites / colony evaluations / story texts: " + counts );

		Check( "recovereddata: the game data has the 38 planet messages, 14 artifact sites, 51 colony evaluations and 13 story texts of STRINFO and the original's screens", counts == "38/14/51/13", counts );

		// ---- every record names a planet (and an artifact) the game data has
		var unresolved = "";

		foreach ( var record in messages )
		{
			if ( IntFieldOrMin( record, "m_planetId" ) < 0 )
			{
				unresolved += "message " + IntFieldOrMin( record, "m_id" ) + " ";
			}
		}

		foreach ( var record in sites )
		{
			if ( ( IntFieldOrMin( record, "m_planetId" ) < 0 ) || ( IntFieldOrMin( record, "m_artifactId" ) < 0 ) )
			{
				unresolved += "site " + IntFieldOrMin( record, "m_id" ) + " ";
			}
		}

		foreach ( var record in evaluations )
		{
			if ( IntFieldOrMin( record, "m_planetId" ) < 0 )
			{
				unresolved += "evaluation " + IntFieldOrMin( record, "m_id" ) + " ";
			}
		}

		Log( "recovereddata: records that name something the game data does not have: " + ( ( unresolved == "" ) ? "none" : unresolved.Trim() ) );

		Check( "recovereddata: every record names a planet and an artifact the game data has", ( messages.Count > 0 ) && ( sites.Count > 0 ) && ( evaluations.Count > 0 ) && ( unresolved == "" ), ( unresolved == "" ) ? counts : unresolved.Trim() );

		// ---- planets worked out by hand from the game data (the planets of a star by orbit, counted from the sun)
		var orb = PlanetOfRecord( sites, "m_artifactName", "Crystal Orb" );
		var eggAt143 = PlanetOfRecord( sites, "m_artifactName", "Black Egg", "m_starX", 143 );
		var pearl = PlanetOfRecord( sites, "m_artifactName", "Crystal Pearl" );
		var ring = PlanetOfRecord( sites, "m_artifactName", "Ring Device" );
		var hypercube = PlanetOfRecord( sites, "m_artifactName", "Hypercube" );
		var heaven = PlanetOfRecord( evaluations, "m_placeName", "HEAVEN" );
		var crystalPlanet = PlanetOfRecord( messages, "m_placeName", "THE CRYSTAL PLANET" );
		var noahWreck = PlanetOfRecord( messages, "m_placeName", "NOAH 9 WRECK SITE" );

		var planets = "orb " + orb + " egg " + eggAt143 + " pearl " + pearl + " ring " + ring + " hypercube " + hypercube + " heaven " + heaven + " crystal " + crystalPlanet + " noah " + noahWreck;

		Log( "recovereddata: planets: " + planets + " (by hand: 130, 112, 123, 4, 5, 107, 33, 115)" );

		Check( "recovereddata: the planets match the ones worked out by hand (Sphexi 130, the Black Egg of 143,115 at 112, the City of the Ancients 123, Mars 4, Earth 5, Heaven 107, the Crystal Planet 33, the Noah 9 wreck 115)", ( orb == 130 ) && ( eggAt143 == 112 ) && ( pearl == 123 ) && ( ring == 4 ) && ( hypercube == 5 ) && ( heaven == 107 ) && ( crystalPlanet == 33 ) && ( noahWreck == 115 ), planets );

		// ---- the two numberings meet: the guards in orbit (game data orbits) wait at the planets STRINFO names (counted from the sun)
		var guards = "302 at " + PlanetOfOrbitEncounter( 302 ) + " / Black Egg " + eggAt143 + ", 305 at " + PlanetOfOrbitEncounter( 305 ) + " / Crystal Orb " + orb + ", 77 at " + PlanetOfOrbitEncounter( 77 ) + " / Heaven " + heaven + ", 33 at " + PlanetOfOrbitEncounter( 33 ) + " / Shimmering Ball " + PlanetOfRecord( sites, "m_artifactName", "Shimmering Ball" );

		Log( "recovereddata: guards in orbit and the planets STRINFO names: " + guards );

		Check( "recovereddata: the Veloxi drones, the Mechans and the Gazurtoids wait in orbit around the planets STRINFO puts the Black Egg, the Crystal Orb, Heaven and the Shimmering Ball on", ( PlanetOfOrbitEncounter( 302 ) == eggAt143 ) && ( PlanetOfOrbitEncounter( 305 ) == orb ) && ( PlanetOfOrbitEncounter( 77 ) == heaven ) && ( PlanetOfOrbitEncounter( 33 ) == PlanetOfRecord( sites, "m_artifactName", "Shimmering Ball" ) ), guards );

		// ---- the story texts
		string[] keys = { "EvaluationOptimal", "EvaluationSuitable", "EvaluationUnsuitable", "MissionComplete", "TerrainVehicleLost", "TowingCharges", "CrystalPlanetDestroyed", "CrystalPlanetDamaged", "FlareDeath", "DistressNoResponse" };

		var missingKeys = "";
		var flareText = "";

		foreach ( var key in keys )
		{
			string text = null;

			foreach ( var record in storyTexts )
			{
				if ( Equals( FieldOrNull( record, "m_key" ), key ) )
				{
					text = FieldOrNull( record, "m_text" ) as string;
				}
			}

			if ( string.IsNullOrEmpty( text ) || text.Contains( "EMBED" ) )
			{
				missingKeys += key + " ";
			}

			if ( key == "FlareDeath" )
			{
				flareText = text ?? "";
			}
		}

		Log( "recovereddata: story texts missing or broken: " + ( ( missingKeys == "" ) ? "none" : missingKeys.Trim() ) + " | the flare text: " + flareText.Replace( '\n', '/' ) );

		Check( "recovereddata: the ten story texts are there, with their line breaks", ( missingKeys == "" ) && flareText.Contains( "INCINERATED" ) && flareText.Contains( "\n" ), ( missingKeys == "" ) ? flareText.Replace( '\n', '/' ) : missingKeys.Trim() );

		// ---- the game data file itself is unchanged (control): 811 planets, 51 artifacts, its 556 comms and the 2 recovered drone lines
		var sizes = gameData.m_planetList.Length + "/" + gameData.m_artifactList.Length + "/" + gameData.m_commList.Length;

		Check( "recovereddata: the original game data is unchanged (control)", sizes == "811/51/558", sizes );

		Finish( "scenario=recovereddata counts=" + counts + " unresolved=" + ( ( unresolved == "" ) ? "none" : unresolved.Trim().Replace( ' ', ',' ) ) + " planets=" + planets.Replace( ' ', ',' ) + " gameData=" + sizes, 0 );
	}

	// ---------------------------------------------------------------- roadmap 0.5: the flare days of the stars that flared before the game began

	// the inner size of the shine of the star system's sun (what StarSystem.Show set last)
	static float ShineInnerSize()
	{
		var shine = SpaceflightController.m_instance.m_starSystem.m_shine;
		var material = FieldOrNull( shine, "m_material" ) as Material;

		return ( material == null ) ? float.NaN : material.GetVector( "_Size" ).x;
	}

	// the living surface of the star system's sun (Shine): a small flare rises from the rim within a few seconds and falls back, the size
	// of the shine (the game's flares) stays what StarSystem set, the sun takes no numbers from the game's Random, and a frame of it takes
	// no memory
	IEnumerator ScenarioSunLife()
	{
		var controller = SpaceflightController.m_instance;

		EnterStarSystem( 25 );

		yield return WaitForPlanetMaps( 60.0f );

		var shine = controller.m_starSystem.m_shine;
		var material = shine.GetComponent<MeshRenderer>().material;
		var sizeBefore = material.GetVector( "_Size" );

		// ---- 1. within 6 s of real time a flare rises (its strength goes above 0.3) and falls back to nothing
		var hasFlare = material.HasProperty( "_Flare" );
		var strongest = 0.0f;
		var fellBack = false;
		var highestRise = 0.0f;
		var start = Time.realtimeSinceStartup;

		while ( hasFlare && ( Time.realtimeSinceStartup - start < 6.0f ) && !fellBack )
		{
			yield return null;

			var flare = material.GetVector( "_Flare" );

			strongest = Mathf.Max( strongest, flare.w );
			highestRise = Mathf.Max( highestRise, flare.y );

			if ( ( strongest > 0.3f ) && ( flare.w < 0.01f ) )
			{
				fellBack = true;
			}
		}

		Check( "sunlife: a small flare rises from the sun's rim and falls back", hasFlare && ( strongest > 0.3f ) && fellBack && ( highestRise > 0.0f ), "property " + hasFlare + ", strongest " + strongest.ToString( "F2" ) + ", rose " + highestRise.ToString( "F1" ) + " units, fell back " + fellBack );

		// ---- 2. the size of the shine is still what StarSystem set (the flares of the game are not touched)
		var sizeAfter = material.GetVector( "_Size" );

		Check( "sunlife: the size of the sun's shine is untouched by its small flares (control)", sizeAfter == sizeBefore, sizeBefore + " -> " + sizeAfter );

		// ---- 3. the sun takes no numbers from the game's Random, and a frame of it takes no memory
		var update = (System.Action) System.Delegate.CreateDelegate( typeof( System.Action ), shine, "Update" );
		var randomBefore = UnityEngine.Random.state;

		for ( var i = 0; i < 20000; i++ )
		{
			update();
		}

		var randomUntouched = UnityEngine.Random.state.Equals( randomBefore );
		var controlBytes = AllocatedByTheControl();
		var sunBytes = Allocated( update, c_meterCalls ) / c_meterCalls;

		Check( "sunlife: the sun takes nothing from the game's Random and no memory in a frame (control)", randomUntouched && ( controlBytes >= 256 ) && ( sunBytes == 0 ), "Random untouched " + randomUntouched + ", " + sunBytes + " bytes per frame, the control measures " + controlBytes );

		Finish( "scenario=sunlife strongest=" + strongest.ToString( "F2" ) + " rise=" + highestRise.ToString( "F1" ) + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	IEnumerator ScenarioFlareData()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		yield return null;

		// ---- 1. the stars whose flare day the game data holds unsigned: 36, all with a negative m_daysSincePreviousFlare
		var flaredBefore = 0;
		var signed = 0;
		var earthSun = "";
		var future = 0;
		var futureRange = "";
		var futureMin = int.MaxValue;
		var futureMax = int.MinValue;
		var calendarMatches = 0;

		foreach ( var star in gameData.m_starList )
		{
			if ( star.m_daysSincePreviousFlare < 0 )
			{
				flaredBefore++;

				if ( star.m_daysToNextFlare == star.m_daysSincePreviousFlare )
				{
					signed++;
				}
			}
			else
			{
				future++;

				futureMin = Mathf.Min( futureMin, star.m_daysToNextFlare );
				futureMax = Mathf.Max( futureMax, star.m_daysToNextFlare );

				// the date fields of the game data, worked out with a calendar of 10 months of 30 days from day 0 = 01-01-4620
				var day = star.m_daysToNextFlare;

				if ( ( star.m_yearOfNextFlare == 4620 + day / 300 ) && ( star.m_monthOfNextFlare == ( day % 300 ) / 30 + 1 ) && ( star.m_dayOfNextFlare == day % 30 + 1 ) )
				{
					calendarMatches++;
				}
			}

			if ( star.m_id == 0 )
			{
				earthSun = star.m_xCoordinate + "," + star.m_yCoordinate + " day " + star.m_daysToNextFlare;
			}
		}

		futureRange = futureMin + " to " + futureMax;

		var arth = gameData.m_starList[ 25 ];

		Log( "flaredata: stars that flared before the game began: " + flaredBefore + ", flare day signed: " + signed + " | Earth's sun: " + earthSun + " | stars still to flare: " + future + " (days " + futureRange + "), Arth (star 25 at " + arth.m_xCoordinate + "," + arth.m_yCoordinate + ") on day " + arth.m_daysToNextFlare + " | their date fields in a calendar of 10 months of 30 days: " + calendarMatches + " of " + future );

		Check( "flaredata: the 36 stars that flared before the game began have a negative flare day", ( flaredBefore == 36 ) && ( signed == 36 ), signed + " of " + flaredBefore + " signed, Earth's sun " + earthSun );
		Check( "flaredata: the 234 stars still to flare keep their flare day, Arth's on day 300 (control)", ( future == 234 ) && ( futureMin == 4 ) && ( futureMax == 792 ) && ( arth.m_daysToNextFlare == 300 ), future + " stars, days " + futureRange + ", Arth " + arth.m_daysToNextFlare );
		Check( "flaredata: the date fields of the stars still to flare are their flare day in a calendar of 10 months of 30 days (control: the game data itself)", calendarMatches == 234, calendarMatches + " of " + future );

		// ---- 2. in the game: the sun of a star that flared before the game began shines as a stable sun, and Arth's (300 days away) is larger (control)
		EnterStarSystem( 0 );

		var start = Time.realtimeSinceStartup;

		while ( controller.m_starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 40.0f ) )
		{
			yield return null;
		}

		yield return Frames( 5 );

		var earthShine = ShineInnerSize();

		EnterStarSystem( 25 );

		start = Time.realtimeSinceStartup;

		while ( controller.m_starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 40.0f ) )
		{
			yield return null;
		}

		yield return Frames( 5 );

		var arthShine = ShineInnerSize();

		Log( "flaredata: inner size of the sun's shine: Earth's sun " + earthShine.ToString( "F5" ) + ", Arth's sun " + arthShine.ToString( "F5" ) + " (a stable sun is 128)" );

		Check( "flaredata: the sun of a star that flared before the game began shines as a stable sun", earthShine == 128.0f, earthShine.ToString( "F5" ) );
		Check( "flaredata: Arth's sun, 300 days from its flare, shines larger (control)", arthShine > 128.0f, arthShine.ToString( "F5" ) );

		Finish( "scenario=flaredata flaredBefore=" + flaredBefore + " signed=" + signed + " future=" + future + " calendar=" + calendarMatches + " shine=" + earthShine.ToString( "F5" ) + "/" + arthShine.ToString( "F5" ), 0 );
	}

	// ---------------------------------------------------------------- roadmap 0.7: the original's calendar of 10 months of 30 days

	// sets a field by reflection if the object has it (the calendar flag does not exist in the code before it) - returns false if it does not
	static bool SetFieldIfThere( object target, string name, object value )
	{
		var field = target.GetType().GetField( name, c_any );

		if ( field == null )
		{
			return false;
		}

		field.SetValue( target, value );

		return true;
	}

	// the two stardate texts of a day and an hour, made by the game's own clock
	static string StardateTexts( int day, int hour )
	{
		var general = new PD_General();

		general.Reset();

		general.m_day = day;
		general.m_hour = hour;
		general.m_lastHour = hour;

		general.UpdateGameTime( 0.0f );

		return general.m_currentStardateYMD + " / " + general.m_currentStardateDHMY;
	}

	IEnumerator ScenarioCalendar()
	{
		var dataController = DataController.m_instance;
		var gameData = dataController.m_gameData;

		yield return null;

		// ---- 1. the stardate texts of the clock: the first day (control), the turn of a month, day 40, the last day of the year, day 300 and day 365
		var day0 = StardateTexts( 0, 6 );
		var day29 = StardateTexts( 29, 6 );
		var day30 = StardateTexts( 30, 6 );
		var day40 = StardateTexts( 40, 6 );
		var day299 = StardateTexts( 299, 6 );
		var day300 = StardateTexts( 300, 6 );
		var day365 = StardateTexts( 365, 6 );

		Log( "calendar: day 0 " + day0 + " | day 29 " + day29 + " | day 30 " + day30 + " | day 40 " + day40 + " | day 299 " + day299 + " | day 300 " + day300 + " | day 365 " + day365 );

		Check( "calendar: the first day of the game is 01-01-4620 (control)", day0 == "4620-01-01 / 01.06-01-4620", day0 );
		Check( "calendar: a month has 30 days (day 29 is the 30th of month 1, day 30 the 1st of month 2, day 40 the 11th)", ( day29 == "4620-01-30 / 30.06-01-4620" ) && ( day30 == "4620-02-01 / 01.06-02-4620" ) && ( day40 == "4620-02-11 / 11.06-02-4620" ), day29 + " | " + day30 + " | " + day40 );
		Check( "calendar: a year has 10 months (day 299 is 30-10-4620, day 300 is 01-01-4621, day 365 is 06-03-4621)", ( day299 == "4620-10-30 / 30.06-10-4620" ) && ( day300 == "4621-01-01 / 01.06-01-4621" ) && ( day365 == "4621-03-06 / 06.06-03-4621" ), day299 + " | " + day300 + " | " + day365 );

		// ---- 2. the clock and the star data agree: Arth's sun flares on day 300, and the date fields of its star say the same day
		var arth = gameData.m_starList[ 25 ];
		var arthFields = arth.m_yearOfNextFlare.ToString( "D4" ) + "-" + arth.m_monthOfNextFlare.ToString( "D2" ) + "-" + arth.m_dayOfNextFlare.ToString( "D2" );
		var arthClock = StardateTexts( arth.m_daysToNextFlare, 0 ).Split( ' ' )[ 0 ];

		Log( "calendar: Arth's sun flares on day " + arth.m_daysToNextFlare + ": the clock shows " + arthClock + ", the date fields of the star say " + arthFields );

		Check( "calendar: the clock shows the day of Arth's flare as the date the star data gives it", arthClock == arthFields, arthClock + " / " + arthFields );

		// ---- 3. a save made in the real-world calendar: on day 365, hour 15 (31-12-4620 there, 4620 is a leap year), with a bank entry, a logged planet and something the aliens said
		var oldSave = new PlayerData();

		oldSave.Reset();

		var hasCalendarFlag = SetFieldIfThere( oldSave.m_general, "m_stardateCalendar", 0 );

		oldSave.m_general.m_day = 365;
		oldSave.m_general.m_hour = 15;
		oldSave.m_general.m_currentStardateYMD = "4620-12-31";
		oldSave.m_general.m_currentStardateDHMY = "31.15-12-4620";
		oldSave.m_bank.m_transactionList.Add( new PD_Bank.Transaction( "4620-12-31", "Trade depot", "100+" ) );
		oldSave.m_shipsLog.AddPlanetLog( 90, "10.06-02-4620", "Planet 90", "a logged planet" );
		oldSave.m_shipsLog.GetAlienComms( PD_ShipsLog.AlienComm.Themselves ).Add( new PD_ShipsLog.Entry( 5, "31.15-12-4620", "Alien Species #6", "something the aliens said" ) );

		// and one made in the original's calendar (control: nothing in it may move)
		var newSave = new PlayerData();

		newSave.Reset();

		newSave.m_general.m_day = 65;
		newSave.m_general.m_hour = 0;
		newSave.m_bank.m_transactionList.Add( new PD_Bank.Transaction( "4620-03-06", "Trade depot", "100+" ) );

		PlayerData loadedOld = null;
		PlayerData loadedNew = null;

		try
		{
			MemorySaveSystem.s_slots[ 1 ] = JsonUtility.ToJson( oldSave, true );
			loadedOld = Call( dataController, "LoadPlayerData", 1 ) as PlayerData;

			MemorySaveSystem.s_slots[ 1 ] = JsonUtility.ToJson( newSave, true );
			loadedNew = Call( dataController, "LoadPlayerData", 1 ) as PlayerData;
		}
		catch ( Exception exception )
		{
			Log( "calendar: loading threw " + exception.GetType().Name + ": " + exception.Message );
		}

		MemorySaveSystem.s_slots.Remove( 1 );

		var oldDates = "nothing loaded";
		var oldRepaired = false;

		if ( loadedOld != null )
		{
			var bank = loadedOld.m_bank.m_transactionList;
			var planetLog = loadedOld.m_shipsLog.m_planetLogs[ 0 ].m_stardate;
			var alienComm = loadedOld.m_shipsLog.GetAlienComms( PD_ShipsLog.AlienComm.Themselves )[ 0 ].m_stardate;
			var today = loadedOld.m_general.m_currentStardateYMD + " / " + loadedOld.m_general.m_currentStardateDHMY;

			oldDates = "bank " + bank[ 0 ].m_stardate + ", " + bank[ bank.Count - 1 ].m_stardate + " | planet log " + planetLog + " | aliens " + alienComm + " | today " + today + " | calendar flag " + IntFieldOrMin( loadedOld.m_general, "m_stardateCalendar" );
			oldRepaired = ( bank[ 0 ].m_stardate == "4620-01-01" ) && ( bank[ bank.Count - 1 ].m_stardate == "4621-03-06" ) && ( planetLog == "11.06-02-4620" ) && ( alienComm == "06.15-03-4621" ) && ( today == "4621-03-06 / 06.15-03-4621" );
		}

		var newDates = "nothing loaded";
		var newKept = false;

		if ( loadedNew != null )
		{
			var bank = loadedNew.m_bank.m_transactionList;

			newDates = "bank " + bank[ bank.Count - 1 ].m_stardate + " | today " + loadedNew.m_general.m_currentStardateYMD;
			newKept = ( bank[ bank.Count - 1 ].m_stardate == "4620-03-06" );
		}

		Log( "calendar: a save in the real-world calendar (the save has the calendar flag: " + hasCalendarFlag + "), after loading: " + oldDates + " | a save in the original's calendar: " + newDates );

		Check( "calendar: a save made in the real-world calendar has its bank, ship's log and current dates moved to the original's calendar when it is loaded", oldRepaired, oldDates );
		Check( "calendar: a save made in the original's calendar keeps its dates (control)", newKept, newDates );

		Finish( "scenario=calendar day40=" + day40.Replace( ' ', '_' ) + " day300=" + day300.Replace( ' ', '_' ) + " arth=" + arthClock + "/" + arthFields + " repaired=" + oldRepaired + " kept=" + newKept, 0 );
	}

	// ---------------------------------------------------------------- phase 3: what has been taken from a planet stays taken

	// into the terrain vehicle through the real Disembark button, from the planet's surface (returns false if the vehicle did not go out)
	static IEnumerator DisembarkNow( bool[] done )
	{
		var playerData = DataController.m_instance.m_playerData;

		done[ 0 ] = false;

		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		yield return WaitForLocation( PD_General.Location.Disembarked, 15.0f );
		yield return Frames( 10 );

		done[ 0 ] = ( playerData.m_general.m_location == PD_General.Location.Disembarked );
	}

	// the deposit of the terrain vehicle's grid that was placed at this spot (x and z), or null - deposits that are gone (inactive) are not counted
	static TerrainElement DepositAt( Vector3 position )
	{
		var container = SpaceflightController.m_instance.m_disembarked.m_terrainGrid.m_terrainElements.transform;

		foreach ( var deposit in container.GetComponentsInChildren<TerrainElement>( false ) )
		{
			var delta = deposit.transform.position - position;

			if ( ( Mathf.Abs( delta.x ) < 0.01f ) && ( Mathf.Abs( delta.z ) < 0.01f ) )
			{
				return deposit;
			}
		}

		return null;
	}

	IEnumerator ScenarioPickups()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// down to planet 90 (43% mineral density) and into the terrain vehicle
		yield return EnterOrbit( 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( !done[ 0 ] )
		{
			Finish( "scenario=pickups abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainVehicle = controller.m_terrainVehicle;
		var container = controller.m_disembarked.m_terrainGrid.m_terrainElements.transform;
		var deposits = container.GetComponentsInChildren<TerrainElement>( true );

		if ( deposits.Length < 10 )
		{
			Finish( "scenario=pickups abort: the planet has only " + deposits.Length + " deposits", 2 );
			yield break;
		}

		// three deposits, known by where they were placed: one to take all of, one to take part of, and one to leave alone (the control)
		var whole = deposits[ 0 ];
		var part = deposits[ 1 ];
		var untouched = deposits[ 2 ];

		var wholePosition = whole.transform.position;
		var partPosition = part.transform.position;
		var untouchedPosition = untouched.transform.position;
		var partVolumeBefore = part.m_volume;
		var untouchedVolumeBefore = untouched.m_volume;

		// nothing else within reach of the terrain vehicle
		foreach ( var other in deposits )
		{
			if ( Vector3.Distance( other.transform.position, terrainVehicle.transform.position ) < 50.0f )
			{
				other.transform.position += Vector3.right * 1000.0f;
			}
		}

		// ---- 1. all of the first deposit, through the real Cargo button
		whole.transform.position = BesideTheVehicle( terrainVehicle );

		new TVCargoButton().Execute();

		yield return Frames( 5 );

		var wholeTaken = whole.HasBeenPickedUp();

		// ---- 2. part of the second: the hold has room for 1 cubic meter only
		var room = playerData.m_terrainVehicle.GetRemainingVolume();

		playerData.m_terrainVehicle.AddElement( part.m_elementId, room - 10 );

		part.transform.position = BesideTheVehicle( terrainVehicle );

		new TVCargoButton().Execute();

		yield return Frames( 5 );

		var partLeft = part.m_volume;

		part.transform.position = partPosition;

		Log( "pickups: on planet 90, deposit 0 taken whole: " + wholeTaken + " | deposit 1 had " + partVolumeBefore + " tenths, " + partLeft + " left after 10 fitted | deposit 2 (control) has " + untouchedVolumeBefore );

		// ---- 3. back into the ship, and out again: the planet is placed again from its seed
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		var wholeAgain = DepositAt( wholePosition );
		var partAgain = DepositAt( partPosition );
		var untouchedAgain = DepositAt( untouchedPosition );

		var afterReturn = "whole " + ( ( wholeAgain == null ) ? "gone" : ( wholeAgain.m_volume + " tenths" ) ) + ", part " + ( ( partAgain == null ) ? "gone" : ( partAgain.m_volume + " tenths" ) ) + ", control " + ( ( untouchedAgain == null ) ? "gone" : ( untouchedAgain.m_volume + " tenths" ) );

		Log( "pickups: out again: " + afterReturn );

		Check( "pickups: a deposit that was taken whole is gone the next time the terrain vehicle goes out", wholeTaken && done[ 0 ] && ( wholeAgain == null ), afterReturn );
		Check( "pickups: a deposit that was taken in part has only what was left", done[ 0 ] && ( partLeft == partVolumeBefore - 10 ) && ( partAgain != null ) && ( partAgain.m_volume == partLeft ), afterReturn + " (left " + partLeft + ")" );
		Check( "pickups: a deposit nothing was taken from is as it was (control)", done[ 0 ] && ( untouchedAgain != null ) && ( untouchedAgain.m_volume == untouchedVolumeBefore ), afterReturn );

		// ---- 4. through a save and a load: what was taken is in the save
		var loaded = JsonUtility.FromJson<PlayerData>( JsonUtility.ToJson( playerData ) );
		var surfaces = FieldOrNull( loaded, "m_planetSurfaces" );
		var savedDeposits = FieldOrNull( surfaces, "m_depositList" ) as System.Collections.IList;
		var savedText = ( savedDeposits == null ) ? "nothing saved" : savedDeposits.Count + " deposits saved";

		Log( "pickups: after a save and a load: " + savedText );

		Check( "pickups: the deposits that were taken from are saved", ( savedDeposits != null ) && ( savedDeposits.Count == 2 ), savedText );

		Finish( "scenario=pickups whole=" + wholeTaken + " partLeft=" + partLeft + " afterReturn=" + afterReturn.Replace( ' ', '_' ) + " saved=" + savedText.Replace( ' ', '_' ), 0 );
	}

	// ---------------------------------------------------------------- phase 3: ruins at the places of the original's messages, and the messages recorded with the cargo button

	// the ruins of the terrain vehicle's grid (found by the component's name, so that this compiles on the code before ruins existed)
	static List<Component> RuinsOnTheGround()
	{
		var list = new List<Component>();
		var grid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var container = ( grid == null ) ? null : FieldOrNull( grid, "m_terrainRuins" ) as Component;

		if ( container != null )
		{
			foreach ( Transform child in container.transform )
			{
				var ruin = child.GetComponent( "TerrainRuin" );

				if ( ruin != null )
				{
					list.Add( ruin );
				}
			}
		}

		return list;
	}

	// the message ids a ruin holds
	static List<int> MessagesInRuin( Component ruin )
	{
		return ( FieldOrNull( ruin, "m_messageIds" ) as List<int> ) ?? new List<int>();
	}

	IEnumerator ScenarioRuins()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- what the recovered data puts on Earth (planet 5, the third planet of 215, 86): the messages at sites (a site can hold several) and the ones at random places
		const int c_earth = 5;

		var siteKeys = new HashSet<string>();
		var randomMessages = 0;
		var siteMessageIds = new List<int>();

		foreach ( var planetMessage in gameData.m_planetMessageList )
		{
			if ( planetMessage.m_planetId != c_earth )
			{
				continue;
			}

			if ( planetMessage.m_placement == "Site" )
			{
				siteKeys.Add( planetMessage.m_latitude + "/" + planetMessage.m_longitude );

				if ( ( planetMessage.m_latitude == 11 ) && ( planetMessage.m_longitude == -104 ) )
				{
					siteMessageIds.Add( planetMessage.m_id );
				}
			}
			else
			{
				randomMessages++;
			}
		}

		var expectedRuins = siteKeys.Count + randomMessages;

		// ---- land at 11N x 104W (the crosshair's "latitude" is east-west in the port, its "longitude" north-south) and go out in the terrain vehicle
		playerData.m_general.m_selectedLatitude = -104.0f;
		playerData.m_general.m_selectedLongitude = 11.0f;

		yield return GoIntoOrbit( 0, c_earth );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( !done[ 0 ] )
		{
			Finish( "scenario=ruins abort: never got into the terrain vehicle on Earth (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var ruins = RuinsOnTheGround();

		// the ruin of the site, where the latitude and the longitude put it
		var sitePosition = Tools.LatLongToWorldCoordinates( -104.0f, 11.0f );
		Component siteRuin = null;

		foreach ( var ruin in ruins )
		{
			var delta = ruin.transform.position - sitePosition;

			if ( ( Mathf.Abs( delta.x ) < 0.5f ) && ( Mathf.Abs( delta.z ) < 0.5f ) )
			{
				siteRuin = ruin;
			}
		}

		var siteHolds = ( siteRuin == null ) ? "no ruin" : string.Join( ",", MessagesInRuin( siteRuin ) );

		Log( "ruins: Earth has " + siteMessageIds.Count + " messages at 11N x 104W (" + string.Join( ",", siteMessageIds ) + "), " + siteKeys.Count + " sites and " + randomMessages + " messages at random places: " + expectedRuins + " ruins expected, " + ruins.Count + " on the ground | the ruin at the site holds " + siteHolds );

		Check( "ruins: Earth has a ruin for each site and for each message at a random place", ( expectedRuins == 12 ) && ( ruins.Count == expectedRuins ), ruins.Count + " of " + expectedRuins );
		Check( "ruins: the ruin of 11N x 104W is at its latitude and longitude and holds both of its messages", ( siteRuin != null ) && ( siteHolds == string.Join( ",", siteMessageIds ) ) && ( siteMessageIds.Count == 2 ), siteHolds );

		// ---- no rock or tree right next to a ruin
		var crowded = 0;
		var grid = controller.m_disembarked.m_terrainGrid;

		foreach ( var ruin in ruins )
		{
			foreach ( var container in new Component[] { grid.m_terrainRocks, grid.m_terrainTrees } )
			{
				foreach ( Transform child in container.transform )
				{
					var delta = child.position - ruin.transform.position;

					delta.y = 0.0f;

					if ( child.gameObject.activeSelf && ( delta.magnitude < 15.0f ) )
					{
						crowded++;
					}
				}
			}
		}

		Check( "ruins: no rock or tree stands right next to a ruin", ( ruins.Count > 0 ) && ( crowded == 0 ), crowded + " rocks or trees within 15 units of a ruin" );

		// ---- the cargo button beside the ruin of the site records its messages in the ship's log, dated today, once
		if ( siteRuin != null )
		{
			controller.m_terrainVehicle.transform.position = siteRuin.transform.position + Vector3.forward * 5.0f;
		}

		yield return Frames( 2 );

		var foundBefore = playerData.m_shipsLog.m_foundMessages.Count;

		controller.m_messages.Clear();

		new TVCargoButton().Execute();

		yield return Frames( 3 );

		var foundAfter = playerData.m_shipsLog.m_foundMessages.Count;
		var recordedText = MessageList();
		var today = playerData.m_general.m_currentStardateDHMY;
		var datedToday = 0;
		var recordedIds = "";

		foreach ( var entry in playerData.m_shipsLog.m_foundMessages )
		{
			recordedIds += entry.m_id + " ";

			if ( entry.m_stardate == today )
			{
				datedToday++;
			}
		}

		new TVCargoButton().Execute();

		yield return Frames( 3 );

		var foundAfterSecondPress = playerData.m_shipsLog.m_foundMessages.Count;

		Log( "ruins: found messages " + foundBefore + " -> " + foundAfter + " (" + recordedIds.Trim() + "), " + datedToday + " dated today (" + today + "), after a second press " + foundAfterSecondPress + " | " + recordedText.Substring( 0, Mathf.Min( 160, recordedText.Length ) ) );

		Check( "ruins: the cargo button beside the ruin records its two messages in the ship's log, dated the day they were found", ( foundBefore == 0 ) && ( foundAfter == 2 ) && ( datedToday == 2 ) && recordedText.Contains( "INVOICE" ), foundBefore + " -> " + foundAfter + ", dated today " + datedToday );
		Check( "ruins: pressing it again records nothing twice", foundAfterSecondPress == foundAfter, foundAfter + " -> " + foundAfterSecondPress );

		// ---- the scan names the ruins
		controller.m_messages.Clear();

		new ScanButton().Execute();

		yield return Frames( 3 );

		var scanText = MessageList();

		Check( "ruins: the scan reports the ruin", scanText.Contains( "Ruin" ), scanText.Substring( 0, Mathf.Min( 160, scanText.Length ) ) );

		// ---- back into the ship and out again: the same ruins at the same places
		var placesBefore = "";

		foreach ( var ruin in ruins )
		{
			placesBefore += Mathf.RoundToInt( ruin.transform.position.x ) + "/" + Mathf.RoundToInt( ruin.transform.position.z ) + " ";
		}

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		var placesAfter = "";

		foreach ( var ruin in RuinsOnTheGround() )
		{
			placesAfter += Mathf.RoundToInt( ruin.transform.position.x ) + "/" + Mathf.RoundToInt( ruin.transform.position.z ) + " ";
		}

		Check( "ruins: the ruins are at the same places every time", ( placesBefore != "" ) && ( placesBefore == placesAfter ), placesBefore.Trim() + " | " + placesAfter.Trim() );

		// ---- a planet with no messages has no ruins (control): planet 90 of the Arth system
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return GoIntoOrbit( 25, 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		var ruinsOn90 = RuinsOnTheGround().Count;

		Check( "ruins: a planet the original has no messages on has no ruins (control)", done[ 0 ] && ( ruinsOn90 == 0 ), ruinsOn90 + " ruins on planet 90" );

		Finish( "scenario=ruins earth=" + ruins.Count + "/" + expectedRuins + " site=" + siteHolds + " found=" + foundAfter + "/" + foundAfterSecondPress + " planet90=" + ruinsOn90, 0 );
	}

	// ---------------------------------------------------------------- phase 3: the special artifacts at their sites, taken with the cargo button and kept as taken

	// how many of this artifact a storage holds
	static int ArtifactsHeld( PD_ArtifactStorage storage, int artifactId )
	{
		var count = 0;

		if ( ( storage != null ) && ( storage.m_artifactList != null ) )
		{
			foreach ( var artifactReference in storage.m_artifactList )
			{
				if ( artifactReference.m_artifactId == artifactId )
				{
					count++;
				}
			}
		}

		return count;
	}

	IEnumerator ScenarioArtifactSites()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- 1. the owner's rulings of 2026-10-09 on two of STRINFO's conflicts: the Rod Device at 54N x 13E, and Koann 3 the third planet of 112, 200 (orbit 5, planet 58)
		var rodDevice = "missing";
		var redCylinderPlanet = -1;

		foreach ( var artifactSite in gameData.m_artifactSiteList )
		{
			if ( artifactSite.m_artifactName == "Rod Device" )
			{
				rodDevice = artifactSite.m_latitude + "/" + artifactSite.m_longitude;
			}

			if ( artifactSite.m_artifactName == "Red Cylinder" )
			{
				redCylinderPlanet = artifactSite.m_planetId;
			}
		}

		var koannPlanet = -1;

		foreach ( var colonyEvaluation in gameData.m_colonyEvaluationList )
		{
			if ( ( colonyEvaluation.m_starX == 112 ) && ( colonyEvaluation.m_starY == 200 ) )
			{
				koannPlanet = colonyEvaluation.m_planetId;
			}
		}

		Log( "artifactsites: the Rod Device at " + rodDevice + ", the Red Cylinder on planet " + redCylinderPlanet + ", Koann 3's evaluation for planet " + koannPlanet + " (planet 58 is in orbit 5 of star 16)" );

		Check( "artifactsites: the Rod Device lies at 54N x 13E, and the Red Cylinder and Koann 3's evaluation are on the third planet of 112, 200", ( rodDevice == "54/13" ) && ( redCylinderPlanet == 58 ) && ( koannPlanet == 58 ), rodDevice + ", " + redCylinderPlanet + ", " + koannPlanet );

		// ---- 2. Earth, at 11N x 104W: the Hypercube lies in the ruin of the invoice
		const int c_earth = 5;
		var hypercubeId = gameData.FindArtifactId( "Hypercube" );

		playerData.m_general.m_selectedLatitude = -104.0f;
		playerData.m_general.m_selectedLongitude = 11.0f;

		yield return GoIntoOrbit( 0, c_earth );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( !done[ 0 ] )
		{
			Finish( "scenario=artifactsites abort: never got into the terrain vehicle on Earth (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var ruins = RuinsOnTheGround();
		var sitePosition = Tools.LatLongToWorldCoordinates( -104.0f, 11.0f );
		Component siteRuin = null;

		foreach ( var ruin in ruins )
		{
			var delta = ruin.transform.position - sitePosition;

			if ( ( Mathf.Abs( delta.x ) < 0.5f ) && ( Mathf.Abs( delta.z ) < 0.5f ) )
			{
				siteRuin = ruin;
			}
		}

		var siteArtifacts = ( siteRuin == null ) ? null : FieldOrNull( siteRuin, "m_artifactSiteIds" ) as List<int>;
		var siteHolds = ( siteArtifacts == null ) ? "nothing" : string.Join( ",", siteArtifacts );

		Log( "artifactsites: Earth has " + ruins.Count + " ruins; the ruin of 11N x 104W holds the artifact sites " + siteHolds + " and the messages " + ( ( siteRuin == null ) ? "-" : string.Join( ",", MessagesInRuin( siteRuin ) ) ) );

		Check( "artifactsites: the Hypercube's site shares the ruin of the invoice, and Earth still has 12 ruins", ( ruins.Count == 12 ) && ( siteArtifacts != null ) && ( siteArtifacts.Count == 1 ) && ( gameData.m_artifactSiteList[ siteArtifacts[ 0 ] ].m_artifactId == hypercubeId ), ruins.Count + " ruins, sites " + siteHolds );

		// ---- 3. the scan sees the artifact in the ruin
		if ( siteRuin != null )
		{
			controller.m_terrainVehicle.transform.position = siteRuin.transform.position + Vector3.forward * 5.0f;
		}

		yield return Frames( 2 );

		controller.m_messages.Clear();

		new ScanButton().Execute();

		yield return Frames( 3 );

		var scanText = MessageList();

		Check( "artifactsites: the scan reports the artifact in the ruin", scanText.Contains( "Artifact in the ruins: 1" ), scanText.Substring( 0, Mathf.Min( 200, scanText.Length ) ) );

		// ---- 4. the cargo button takes it, once, and the taking is saved
		new TVCargoButton().Execute();

		yield return Frames( 3 );

		var inVehicle = ArtifactsHeld( playerData.m_terrainVehicle.m_artifactStorage, hypercubeId );
		var takenText = MessageList();

		new TVCargoButton().Execute();

		yield return Frames( 3 );

		var inVehicleAfterSecondPress = ArtifactsHeld( playerData.m_terrainVehicle.m_artifactStorage, hypercubeId );
		var takenList = FieldOrNull( FieldOrNull( playerData, "m_planetSurfaces" ), "m_takenArtifactSiteList" ) as List<int>;
		var takenCount = ( takenList == null ) ? -1 : takenList.Count;

		Log( "artifactsites: Hypercubes in the terrain vehicle " + inVehicle + ", after a second press " + inVehicleAfterSecondPress + ", artifact sites taken " + takenCount + " | " + takenText.Substring( 0, Mathf.Min( 200, takenText.Length ) ) );

		Check( "artifactsites: the cargo button beside the ruin takes the Hypercube into the terrain vehicle, once", ( inVehicle == 1 ) && ( inVehicleAfterSecondPress == 1 ) && takenText.Contains( "Picked up the Hypercube" ), inVehicle + " then " + inVehicleAfterSecondPress );
		Check( "artifactsites: the taking is in the save", takenCount == 1, takenCount + " sites taken" );

		// ---- 5. back into the ship: the Hypercube goes into the ship's hold; out again: there is nothing more to take
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		var inShip = ArtifactsHeld( playerData.m_playerShip.m_artifactStorage, hypercubeId );
		var leftInVehicle = ArtifactsHeld( playerData.m_terrainVehicle.m_artifactStorage, hypercubeId );

		yield return DisembarkNow( done );

		controller.m_terrainVehicle.transform.position = sitePosition + Vector3.forward * 5.0f;

		yield return Frames( 2 );

		new TVCargoButton().Execute();

		yield return Frames( 3 );

		var inVehicleLater = ArtifactsHeld( playerData.m_terrainVehicle.m_artifactStorage, hypercubeId );

		Log( "artifactsites: back in the ship: " + inShip + " in the ship's hold, " + leftInVehicle + " left in the terrain vehicle | out again and the cargo button pressed at the ruin: " + inVehicleLater + " in the terrain vehicle" );

		Check( "artifactsites: back in the ship the Hypercube is in the ship's hold", ( inShip == 1 ) && ( leftInVehicle == 0 ), inShip + " in the ship, " + leftInVehicle + " in the vehicle" );
		Check( "artifactsites: going out again, the ruin has nothing more to take", done[ 0 ] && ( inVehicleLater == 0 ), inVehicleLater + " Hypercubes taken the second time" );

		// ---- 6. the messages of the same ruin were recorded as before (control)
		var recorded = playerData.m_shipsLog.m_foundMessages.Count;

		Check( "artifactsites: the two messages of the ruin are recorded as before (control)", recorded == 2, recorded + " messages" );

		Finish( "scenario=artifactsites rod=" + rodDevice + " redCylinder=" + redCylinderPlanet + " earthRuins=" + ruins.Count + " site=" + siteHolds + " taken=" + inVehicle + "/" + inVehicleAfterSecondPress + " ship=" + inShip + " later=" + inVehicleLater, 0 );
	}

	// ---------------------------------------------------------------- phase 3: the Most Magnificent Hexagon and the City of the Ancients

	// lands on a planet at a latitude and a longitude (north and east positive) and goes out in the terrain vehicle - returns the ruins on the ground, or null if the vehicle did not go out
	static IEnumerator LandAndGoOut( int starId, int planetId, float northLatitude, float eastLongitude, List<Component>[] ruins )
	{
		var controller = SpaceflightController.m_instance;
		var playerData = DataController.m_instance.m_playerData;
		var done = new bool[ 1 ];

		ruins[ 0 ] = null;

		// the crosshair's "latitude" is east-west in the port, its "longitude" north-south
		playerData.m_general.m_selectedLatitude = eastLongitude;
		playerData.m_general.m_selectedLongitude = northLatitude;

		yield return GoIntoOrbit( starId, planetId );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( done[ 0 ] )
		{
			ruins[ 0 ] = RuinsOnTheGround();
		}
	}

	IEnumerator ScenarioFormations()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var ruins = new List<Component>[ 1 ];

		EnsureCrew();

		// ---- 1. Sphexi (planet 130, the first of 132, 165): the Crystal Orb at 46N x 14E in the middle of six ruins - the drones in its orbit let the ship in first
		controller.GrantOrbitPermission( 305 );

		yield return LandAndGoOut( 35, 130, 46.0f, 14.0f, ruins );

		var sphexiRuins = ruins[ 0 ];
		var orbCenter = Tools.LatLongToWorldCoordinates( 14.0f, 46.0f );
		var orbRuins = 0;
		var ringRuins = 0;
		var ringDistances = "";
		var ringAngles = new List<float>();

		if ( sphexiRuins != null )
		{
			foreach ( var ruin in sphexiRuins )
			{
				var delta = ruin.transform.position - orbCenter;

				delta.y = 0.0f;

				if ( delta.magnitude < 0.5f )
				{
					orbRuins++;
				}
				else
				{
					ringRuins++;
					ringDistances += delta.magnitude.ToString( "F1" ) + " ";
					ringAngles.Add( Mathf.Atan2( delta.x, delta.z ) * Mathf.Rad2Deg );
				}
			}
		}

		ringAngles.Sort();

		var evenlySpread = ringAngles.Count == 6;

		for ( var i = 1; i < ringAngles.Count; i++ )
		{
			evenlySpread = evenlySpread && ( Mathf.Abs( ringAngles[ i ] - ringAngles[ i - 1 ] - 60.0f ) < 0.5f );
		}

		var allAtOneDistance = ringRuins > 0;

		foreach ( var text in ringDistances.Trim().Split( ' ' ) )
		{
			allAtOneDistance = allAtOneDistance && ( text == "48.0" );
		}

		Log( "formations: Sphexi has " + ( ( sphexiRuins == null ) ? "-" : sphexiRuins.Count.ToString() ) + " ruins: " + orbRuins + " at the Orb's site, " + ringRuins + " around it at " + ringDistances.Trim() + ", at angles " + string.Join( " ", ringAngles.ConvertAll( a => a.ToString( "F0" ) ) ) );

		Check( "formations: the Crystal Orb's ruin on Sphexi stands in the middle of six ruins in a circle (the Most Magnificent Hexagon)", ( sphexiRuins != null ) && ( orbRuins == 1 ) && ( ringRuins == 6 ) && allAtOneDistance && evenlySpread, orbRuins + " + " + ringRuins + ", distances " + ringDistances.Trim() );

		// ---- 2. the City of the Ancients (planet 123, the first of 56, 144): fifteen ruins, the Crystal Pearl's at 28N x 13W in their southwest
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandAndGoOut( 33, 123, 28.0f, -13.0f, ruins );

		var cityRuins = ruins[ 0 ];
		var pearlCenter = Tools.LatLongToWorldCoordinates( -13.0f, 28.0f );
		var pearlRuins = 0;
		var blockRuins = 0;
		var southOrWest = 0;
		var farthestEast = 0.0f;
		var farthestNorth = 0.0f;

		if ( cityRuins != null )
		{
			foreach ( var ruin in cityRuins )
			{
				var delta = ruin.transform.position - pearlCenter;

				if ( ( Mathf.Abs( delta.x ) < 0.5f ) && ( Mathf.Abs( delta.z ) < 0.5f ) )
				{
					pearlRuins++;
					continue;
				}

				blockRuins++;

				if ( ( delta.x < -0.5f ) || ( delta.z < -0.5f ) )
				{
					southOrWest++;
				}

				farthestEast = Mathf.Max( farthestEast, delta.x );
				farthestNorth = Mathf.Max( farthestNorth, delta.z );
			}
		}

		Log( "formations: the City of the Ancients has " + ( ( cityRuins == null ) ? "-" : cityRuins.Count.ToString() ) + " ruins: " + pearlRuins + " at the Pearl's site, " + blockRuins + " others, " + southOrWest + " of them south or west of it, reaching " + farthestEast.ToString( "F0" ) + " east and " + farthestNorth.ToString( "F0" ) + " north" );

		Check( "formations: the Crystal Pearl's ruin is in the southwest of a cluster of fifteen ruins (the City of the Ancients)", ( cityRuins != null ) && ( pearlRuins == 1 ) && ( blockRuins == 15 ) && ( southOrWest == 0 ) && ( farthestEast > 0.0f ) && ( farthestNorth > 0.0f ), pearlRuins + " + " + blockRuins + ", " + southOrWest + " south or west" );

		// ---- 3. a planet with no formation keeps its ruins as they were (control): Earth has 12
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandAndGoOut( 0, 5, 11.0f, -104.0f, ruins );

		var earthRuins = ( ruins[ 0 ] == null ) ? -1 : ruins[ 0 ].Count;

		Check( "formations: Earth keeps its 12 ruins (control)", earthRuins == 12, earthRuins + " ruins" );

		Finish( "scenario=formations sphexi=" + orbRuins + "+" + ringRuins + " city=" + pearlRuins + "+" + blockRuins + " earth=" + earthRuins, 0 );
	}

	// ---------------------------------------------------------------- phase 3: dropping cargo from the terrain vehicle, and picking it up again

	// the things the terrain vehicle has dropped that lie on the ground (found by the component's name, so that this compiles on the code before dropping existed)
	static List<Component> DroppedOnTheGround()
	{
		var list = new List<Component>();
		var grid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var container = ( grid == null ) ? null : FieldOrNull( grid, "m_terrainRuins" ) as Component;

		if ( container != null )
		{
			foreach ( Transform child in container.transform )
			{
				var dropped = child.GetComponent( "TerrainDroppedCargo" );

				if ( ( dropped != null ) && child.gameObject.activeSelf )
				{
					list.Add( dropped );
				}
			}
		}

		return list;
	}

	// the button set of the cargo list by its name (-1 if this code does not have it)
	static int CargoListButtonSet()
	{
		try
		{
			return (int) Enum.Parse( typeof( ButtonController.ButtonSet ), "TerrainVehicleCargo" );
		}
		catch ( Exception )
		{
			return -1;
		}
	}

	IEnumerator ScenarioDropCargo()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var buttonController = controller.m_buttonController;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- down to planet 90 and out in the terrain vehicle, with 3 cubic meters of an element and a Hypercube in its hold, and nothing within reach
		yield return EnterOrbit( 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( !done[ 0 ] )
		{
			Finish( "scenario=dropcargo abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainVehicle = controller.m_terrainVehicle;
		var deposits = controller.m_disembarked.m_terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true );
		var elementId = gameData.m_planetList[ 90 ].m_elementIdA;
		var hypercubeId = gameData.FindArtifactId( "Hypercube" );

		foreach ( var deposit in deposits )
		{
			if ( Vector3.Distance( deposit.transform.position, terrainVehicle.transform.position ) < 50.0f )
			{
				deposit.transform.position += Vector3.right * 1000.0f;
			}
		}

		playerData.m_terrainVehicle.AddElement( elementId, 30 );
		playerData.m_terrainVehicle.AddArtifact( hypercubeId );

		var dropPosition = terrainVehicle.transform.position;

		// ---- 1. the cargo button with nothing within reach: the cargo list, with the buttons for dropping
		controller.m_messages.Clear();

		new TVCargoButton().Execute();

		yield return Frames( 2 );

		var setAfterCargo = buttonController.GetCurrentButtonSet().ToString();
		var listText = MessageList();
		var cargoListSet = CargoListButtonSet();

		Log( "dropcargo: after the cargo button: button set " + setAfterCargo + " | " + listText.Substring( 0, Mathf.Min( 200, listText.Length ) ) );

		Check( "dropcargo: the cargo button lists the cargo with the first item marked, and puts the buttons for dropping on the console", ( setAfterCargo == "TerrainVehicleCargo" ) && listText.Contains( "> " ), setAfterCargo );

		// ---- 2. next, then drop: the Hypercube is dropped; drop again: the element is dropped, and the console goes back to the terrain vehicle's buttons
		var hypercubeAfterDrop = -1;
		var droppedAfterFirst = -1;
		var elementAfterDrops = -1;
		var setAfterLastDrop = "";

		if ( cargoListSet >= 0 )
		{
			PressButton( (ButtonController.ButtonSet) cargoListSet, 0 );

			yield return Frames( 2 );

			PressButton( (ButtonController.ButtonSet) cargoListSet, 1 );

			yield return Frames( 2 );

			hypercubeAfterDrop = ArtifactsHeld( playerData.m_terrainVehicle.m_artifactStorage, hypercubeId );
			droppedAfterFirst = DroppedOnTheGround().Count;

			PressButton( (ButtonController.ButtonSet) cargoListSet, 1 );

			yield return Frames( 2 );

			elementAfterDrops = TerrainVehicleCargo( elementId );
			setAfterLastDrop = buttonController.GetCurrentButtonSet().ToString();
		}

		var droppedSaved = FieldOrNull( FieldOrNull( playerData, "m_planetSurfaces" ), "m_droppedCargoList" ) as System.Collections.IList;
		var droppedOnGround = DroppedOnTheGround().Count;

		Log( "dropcargo: after next and drop: Hypercubes in the vehicle " + hypercubeAfterDrop + ", dropped on the ground " + droppedAfterFirst + " | after the second drop: the element in the vehicle " + elementAfterDrops + ", button set " + setAfterLastDrop + ", on the ground " + droppedOnGround + ", saved " + ( ( droppedSaved == null ) ? "nothing" : droppedSaved.Count.ToString() ) );

		Check( "dropcargo: next marks the Hypercube and drop puts it on the ground beside the terrain vehicle", ( hypercubeAfterDrop == 0 ) && ( droppedAfterFirst == 1 ), hypercubeAfterDrop + " in the vehicle, " + droppedAfterFirst + " on the ground" );
		Check( "dropcargo: dropping the last item empties the hold and brings back the terrain vehicle's buttons", ( elementAfterDrops == 0 ) && ( setAfterLastDrop == "TerrainVehicle" ) && ( droppedOnGround == 2 ), elementAfterDrops + ", " + setAfterLastDrop + ", " + droppedOnGround );
		Check( "dropcargo: what was dropped is in the save", ( droppedSaved != null ) && ( droppedSaved.Count == 2 ), ( droppedSaved == null ) ? "nothing" : droppedSaved.Count.ToString() );

		// ---- 3. the scan sees it
		foreach ( var dropped in DroppedOnTheGround() )
		{
			var delta = dropped.transform.position - terrainVehicle.transform.position;

			Log( "dropcargo: a dropped thing is " + delta.magnitude.ToString( "F1" ) + " from the terrain vehicle (" + new Vector2( delta.x, delta.z ).magnitude.ToString( "F1" ) + " across, " + delta.y.ToString( "F1" ) + " up)" );
		}

		controller.m_messages.Clear();

		new ScanButton().Execute();

		yield return Frames( 3 );

		var scanText = MessageList();

		Check( "dropcargo: the scan reports the dropped cargo", scanText.Contains( "Dropped cargo: 2" ), scanText.Substring( 0, Mathf.Min( 200, scanText.Length ) ) );

		// ---- 4. back into the ship and out again: it still lies where it was dropped, and the cargo button picks it all up again
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		var droppedAgain = DroppedOnTheGround();
		var atTheSpot = 0;

		foreach ( var dropped in droppedAgain )
		{
			var delta = dropped.transform.position - dropPosition;

			if ( ( Mathf.Abs( delta.x ) < 0.5f ) && ( Mathf.Abs( delta.z ) < 0.5f ) )
			{
				atTheSpot++;
			}
		}

		terrainVehicle.transform.position = dropPosition;

		yield return Frames( 2 );

		new TVCargoButton().Execute();

		yield return Frames( 3 );

		var elementBack = TerrainVehicleCargo( elementId );
		var hypercubeBack = ArtifactsHeld( playerData.m_terrainVehicle.m_artifactStorage, hypercubeId );
		var leftOnGround = DroppedOnTheGround().Count;
		var leftSaved = ( droppedSaved == null ) ? -1 : ( FieldOrNull( FieldOrNull( playerData, "m_planetSurfaces" ), "m_droppedCargoList" ) as System.Collections.IList ).Count;

		Log( "dropcargo: out again: " + droppedAgain.Count + " dropped things on the ground, " + atTheSpot + " where they were dropped | after the cargo button: the element " + elementBack + ", Hypercubes " + hypercubeBack + ", left on the ground " + leftOnGround + ", left in the save " + leftSaved );

		Check( "dropcargo: going out again, what was dropped lies where it was dropped", ( droppedAgain.Count == 2 ) && ( atTheSpot == 2 ), droppedAgain.Count + " on the ground, " + atTheSpot + " at the spot" );
		Check( "dropcargo: the cargo button picks it all up again, and nothing is left on the ground or in the save", ( elementBack == 30 ) && ( hypercubeBack == 1 ) && ( leftOnGround == 0 ) && ( leftSaved == 0 ), elementBack + ", " + hypercubeBack + ", " + leftOnGround + ", " + leftSaved );

		Finish( "scenario=dropcargo set=" + setAfterCargo + " dropped=" + droppedOnGround + " saved=" + ( ( droppedSaved == null ) ? -1 : droppedSaved.Count ) + " again=" + droppedAgain.Count + " back=" + elementBack + "/" + hypercubeBack, 0 );
	}

	// ---------------------------------------------------------------- phase 3: the terrain vehicle's cargo display

	IEnumerator ScenarioCargoDisplay()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var displayController = controller.m_displayController;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- out on planet 90 with 3 cubic meters of an element and a Hypercube, nothing within reach
		yield return EnterOrbit( 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( !done[ 0 ] )
		{
			Finish( "scenario=cargodisplay abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainVehicle = controller.m_terrainVehicle;

		foreach ( var deposit in controller.m_disembarked.m_terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true ) )
		{
			if ( Vector3.Distance( deposit.transform.position, terrainVehicle.transform.position ) < 50.0f )
			{
				deposit.transform.position += Vector3.right * 1000.0f;
			}
		}

		var elementId = gameData.m_planetList[ 90 ].m_elementIdA;
		var elementName = gameData.m_elementList[ elementId ].m_name;
		var hypercubeId = gameData.FindArtifactId( "Hypercube" );

		playerData.m_terrainVehicle.AddElement( elementId, 30 );
		playerData.m_terrainVehicle.AddArtifact( hypercubeId );

		// the display controller's field is new, the display class is not (it was there, wired into nothing)
		var cargoDisplay = FieldOrNull( displayController, "m_terrainVehicleCargoDisplay" ) as TerrainVehicleCargoDisplay;
		var labels = ( cargoDisplay == null ) ? null : cargoDisplay.m_labelsText;
		var values = ( cargoDisplay == null ) ? null : cargoDisplay.m_valuesText;

		// ---- 1. the cargo button opens the cargo list, and the display shows the hold
		new TVCargoButton().Execute();

		yield return Frames( 3 );

		var shownOnOpen = ( cargoDisplay != null ) && cargoDisplay.gameObject.activeInHierarchy;
		var vehicleShownOnOpen = displayController.m_terrainVehicleDisplay.gameObject.activeInHierarchy;
		var labelsOnOpen = ( labels == null ) ? "" : labels.text;
		var valuesOnOpen = ( values == null ) ? "" : values.text;

		Log( "cargodisplay: cargo display shown " + shownOnOpen + ", terrain vehicle display shown " + vehicleShownOnOpen + " | labels: " + labelsOnOpen.Replace( '\n', '/' ) + " | values: " + valuesOnOpen.Replace( '\n', '/' ) );

		Check( "cargodisplay: the cargo button shows the cargo display, with the element and the Hypercube in it", shownOnOpen && !vehicleShownOnOpen && labelsOnOpen.Contains( elementName ) && labelsOnOpen.Contains( "Hypercube" ) && valuesOnOpen.Contains( "3.0" ), shownOnOpen + "/" + vehicleShownOnOpen + " " + labelsOnOpen.Replace( '\n', '/' ) );

		// ---- 2. an update with nothing changed takes no memory (the text is made again only when the hold changes), with the control that shows the measurement works
		var controlBytes = AllocatedByTheControl();
		var updateBytes = ( cargoDisplay == null ) ? -1 : Allocated( () => cargoDisplay.Update(), c_meterCalls );
		var updateBytesPerCall = ( updateBytes < 0 ) ? -1 : updateBytes / c_meterCalls;

		Log( "cargodisplay: an update of the cargo display with nothing changed takes " + updateBytesPerCall + " bytes (the control, an array of 256 bytes, measures " + controlBytes + ")" );

		Check( "cargodisplay: the measurement works (control)", controlBytes >= 256, controlBytes + " bytes per call for an array of 256 bytes" );
		Check( "cargodisplay: an update of the cargo display with nothing changed takes no memory", updateBytesPerCall == 0, updateBytesPerCall + " bytes per call" );

		// ---- 3. a drop: the display follows the hold
		var cargoListSet = CargoListButtonSet();

		if ( cargoListSet >= 0 )
		{
			PressButton( (ButtonController.ButtonSet) cargoListSet, 0 );

			yield return Frames( 2 );

			PressButton( (ButtonController.ButtonSet) cargoListSet, 1 );

			yield return Frames( 3 );
		}

		var labelsAfterDrop = ( labels == null ) ? "" : labels.text;

		Check( "cargodisplay: after the Hypercube is dropped the display no longer shows it", ( labels != null ) && !labelsAfterDrop.Contains( "Hypercube" ) && labelsAfterDrop.Contains( elementName ), labelsAfterDrop.Replace( '\n', '/' ) );

		// ---- 4. back: the terrain vehicle's display again
		if ( cargoListSet >= 0 )
		{
			PressButton( (ButtonController.ButtonSet) cargoListSet, 2 );

			yield return Frames( 3 );
		}

		var shownAfterBack = ( cargoDisplay != null ) && cargoDisplay.gameObject.activeInHierarchy;
		var vehicleShownAfterBack = displayController.m_terrainVehicleDisplay.gameObject.activeInHierarchy;

		Check( "cargodisplay: back brings the terrain vehicle's display back", ( cargoDisplay != null ) && !shownAfterBack && vehicleShownAfterBack, shownAfterBack + "/" + vehicleShownAfterBack );

		Finish( "scenario=cargodisplay open=" + shownOnOpen + " bytes=" + updateBytesPerCall + " back=" + vehicleShownAfterBack, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the field of the Crystal Planet, and the Crystal Orb

	// the armor points the player ship loses in orbit in the given number of real seconds, with its shields down and its armor full
	static IEnumerator ArmorLostInOrbit( float seconds, int[] lost )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		ship.m_shieldsAreUp = false;
		ship.m_armorPoints = ship.GetMaximumArmorPoints();

		var before = ship.m_armorPoints;
		var end = Time.realtimeSinceStartup + seconds;

		while ( Time.realtimeSinceStartup < end )
		{
			yield return null;
		}

		lost[ 0 ] = before - ship.m_armorPoints;
	}

	IEnumerator ScenarioCrystalField()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var ship = playerData.m_playerShip;
		var orbId = gameData.FindArtifactId( "Crystal Orb" );
		var lost = new int[ 1 ];

		EnsureCrew();

		// no Crystal Orb aboard to begin with
		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( orbId ) != null ); guard++ )
		{
			ship.RemoveArtifact( orbId );
		}

		// ---- 1. the control: in orbit around an ordinary planet nothing damages the ship
		yield return GoIntoOrbit( gameData.m_planetList[ 90 ].m_starId, 90 );
		yield return ArmorLostInOrbit( 3.0f, lost );

		var lostAroundPlanet90 = lost[ 0 ];

		// ---- 2. in orbit around the Crystal Planet (planet 1 of 192,152) without the Crystal Orb the ship takes damage
		yield return GoIntoOrbit( gameData.m_planetList[ 33 ].m_starId, 33 );

		var location = playerData.m_general.m_location;

		yield return ArmorLostInOrbit( 3.0f, lost );

		var lostWithoutTheOrb = lost[ 0 ];

		// ---- 3. with the Crystal Orb in the hold the field does nothing
		ship.AddArtifact( orbId );

		yield return ArmorLostInOrbit( 3.0f, lost );

		var lostWithTheOrb = lost[ 0 ];

		Log( "crystalfield: armor lost in 3 s around planet 90 " + lostAroundPlanet90 + ", around the Crystal Planet without the orb " + lostWithoutTheOrb + " (" + location + "), with the orb " + lostWithTheOrb );

		Check( "crystalfield: in orbit around an ordinary planet the ship takes no damage (control)", lostAroundPlanet90 == 0, lostAroundPlanet90.ToString() );
		Check( "crystalfield: in orbit around the Crystal Planet without the Crystal Orb the ship takes damage rapidly", ( location == PD_General.Location.InOrbit ) && ( lostWithoutTheOrb >= 10 ), lostWithoutTheOrb + " in " + location );
		Check( "crystalfield: with the Crystal Orb in the hold the field does no damage", lostWithTheOrb == 0, lostWithTheOrb.ToString() );

		Finish( "scenario=crystalfield control=" + lostAroundPlanet90 + " withoutOrb=" + lostWithoutTheOrb + " withOrb=" + lostWithTheOrb, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Black Egg

	// the planets the save says a Black Egg has destroyed (found by the field's name, so that this compiles on the code before the Black Egg; null if the code has no such list)
	static System.Collections.IList DestroyedPlanets()
	{
		return FieldOrNull( DataController.m_instance.m_playerData.m_planetSurfaces, "m_destroyedPlanetList" ) as System.Collections.IList;
	}

	// waits in orbit until the ship is no longer in orbit or the time is up
	static IEnumerator WaitWhileInOrbit( float seconds )
	{
		var end = Time.realtimeSinceStartup + seconds;

		while ( ( Time.realtimeSinceStartup < end ) && ( DataController.m_instance.m_playerData.m_general.m_location == PD_General.Location.InOrbit ) )
		{
			yield return null;
		}
	}

	IEnumerator ScenarioBlackEgg()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var blackEggId = gameData.FindArtifactId( "Black Egg" );
		var planet90StarId = gameData.m_planetList[ 90 ].m_starId;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- 1. out on planet 90 with a Black Egg in the terrain vehicle, nothing within reach: the cargo list drops it, and it is armed
		yield return GoIntoOrbit( planet90StarId, 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( !done[ 0 ] )
		{
			Finish( "scenario=blackegg abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var terrainVehicle = controller.m_terrainVehicle;

		foreach ( var deposit in controller.m_disembarked.m_terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true ) )
		{
			if ( Vector3.Distance( deposit.transform.position, terrainVehicle.transform.position ) < 50.0f )
			{
				deposit.transform.position += Vector3.right * 1000.0f;
			}
		}

		playerData.m_terrainVehicle.AddArtifact( blackEggId );

		controller.m_messages.Clear();

		new TVCargoButton().Execute();

		yield return Frames( 2 );

		var cargoListSet = CargoListButtonSet();

		if ( cargoListSet >= 0 )
		{
			PressButton( (ButtonController.ButtonSet) cargoListSet, 1 );

			yield return Frames( 2 );
		}

		var dropText = MessageList();
		var eggsOnPlanet90 = 0;

		foreach ( var droppedCargo in playerData.m_planetSurfaces.m_droppedCargoList )
		{
			if ( ( droppedCargo.m_planetId == 90 ) && ( droppedCargo.m_artifactId == blackEggId ) )
			{
				eggsOnPlanet90++;
			}
		}

		Log( "blackegg: after the drop: " + eggsOnPlanet90 + " Black Egg on planet 90 | " + dropText.Substring( 0, Mathf.Min( 200, dropText.Length ) ) );

		Check( "blackegg: the terrain vehicle drops the Black Egg, and the messages say it is armed", ( eggsOnPlanet90 == 1 ) && dropText.Contains( "armed" ), eggsOnPlanet90 + " | " + dropText );

		// ---- 2. back up in orbit: the countdown, BOOM, and planet 90 is gone, with the ship in the star system
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		controller.m_messages.Clear();
		ship.m_armorPoints = ship.GetMaximumArmorPoints();

		controller.SwitchLocation( PD_General.Location.InOrbit );

		yield return WaitWhileInOrbit( 12.0f );
		yield return Frames( 3 );

		var countdownText = MessageList();
		var destroyed = DestroyedPlanets();
		var planet90Destroyed = ( destroyed != null ) && destroyed.Contains( 90 );
		var locationAfter = playerData.m_general.m_location;
		var controllerOf90 = controller.m_starSystem.GetPlanetController( 90 );

		Log( "blackegg: back in orbit above planet 90: location " + locationAfter + ", destroyed in the save " + planet90Destroyed + ", its planet controller " + ( ( controllerOf90 == null ) ? "gone" : "still there" ) + " | " + countdownText.Substring( 0, Mathf.Min( 300, countdownText.Length ) ) );

		Check( "blackegg: back in orbit the egg counts down from 5 and goes BOOM", countdownText.Contains( "COUNTDOWN TRANSMISSION FROM THE BLACK EGG" ) && countdownText.Contains( "5" ) && countdownText.Contains( "1" ) && countdownText.Contains( "BOOM!" ), countdownText );
		Check( "blackegg: planet 90 is destroyed, saved, gone from its star system, and the ship is in the star system", planet90Destroyed && ( controllerOf90 == null ) && ( locationAfter == PD_General.Location.StarSystem ), planet90Destroyed + ", " + ( controllerOf90 == null ) + ", " + locationAfter );

		// ---- 3. an egg on the Crystal Planet away from its control nexus: damaged but not destroyed
		var crystalStarId = gameData.m_planetList[ 33 ].m_starId;
		var awayFromTheNexus = Tools.LatLongToWorldCoordinates( 0.0f, 0.0f );

		playerData.m_planetSurfaces.AddDroppedCargo( 33, awayFromTheNexus.x, 0.0f, awayFromTheNexus.z, -1, 0, blackEggId );

		ship.AddArtifact( gameData.FindArtifactId( "Crystal Orb" ) );

		yield return GoIntoOrbit( crystalStarId, 33 );

		controller.m_messages.Clear();

		var end = Time.realtimeSinceStartup + 10.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !MessageList().Contains( "BOOM!" ) )
		{
			yield return null;
		}

		yield return Frames( 3 );

		var damagedText = MessageList();
		var crystalAfterAway = ( destroyed != null ) && destroyed.Contains( 33 );
		var eggsLeftOn33 = 0;

		foreach ( var droppedCargo in playerData.m_planetSurfaces.m_droppedCargoList )
		{
			if ( droppedCargo.m_planetId == 33 )
			{
				eggsLeftOn33++;
			}
		}

		Log( "blackegg: an egg on the Crystal Planet at 0 x 0: destroyed " + crystalAfterAway + ", location " + playerData.m_general.m_location + ", eggs left " + eggsLeftOn33 + " | " + damagedText );

		Check( "blackegg: away from the control nexus the Crystal Planet is damaged but not destroyed", damagedText.Contains( "CRYSTAL PLANET DAMAGED BUT NOT DESTROYED" ) && !crystalAfterAway && ( eggsLeftOn33 == 0 ) && ( playerData.m_general.m_location == PD_General.Location.InOrbit ), crystalAfterAway + ", " + eggsLeftOn33 + ", " + playerData.m_general.m_location );

		// ---- 4. an egg at the control nexus (47N x 45E): the Crystal Planet is destroyed
		var atTheNexus = Tools.LatLongToWorldCoordinates( 45.0f, 47.0f );

		playerData.m_planetSurfaces.AddDroppedCargo( 33, atTheNexus.x, 0.0f, atTheNexus.z, -1, 0, blackEggId );

		controller.m_messages.Clear();

		yield return WaitWhileInOrbit( 12.0f );
		yield return Frames( 3 );

		var nexusText = MessageList();
		var crystalDestroyed = ( destroyed != null ) && destroyed.Contains( 33 );

		Log( "blackegg: an egg at the nexus: destroyed " + crystalDestroyed + ", location " + playerData.m_general.m_location + " | " + nexusText.Substring( 0, Mathf.Min( 300, nexusText.Length ) ) );

		Check( "blackegg: at the control nexus the Crystal Planet is destroyed, with the message from Interstel", crystalDestroyed && nexusText.Contains( "INTERSTEL MEDAL OF SUBLIME ACHIEVEMENT" ) && ( playerData.m_general.m_location == PD_General.Location.StarSystem ), crystalDestroyed + ", " + playerData.m_general.m_location );

		// ---- 5. back in planet 90's star system: the planet is still gone
		EnterStarSystem( planet90StarId );

		end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 5 );

		var stillGone = controller.m_starSystem.GetPlanetController( 90 ) == null;
		var otherPlanets = 0;

		foreach ( var planet in gameData.m_starList[ planet90StarId ].GetPlanetList() )
		{
			if ( ( planet != null ) && ( planet.m_id != -1 ) && ( planet.m_id != 90 ) && ( controller.m_starSystem.GetPlanetController( planet.m_id ) != null ) )
			{
				otherPlanets++;
			}
		}

		Log( "blackegg: back in the star system of planet 90: planet 90 " + ( stillGone ? "gone" : "back" ) + ", the other planets there " + otherPlanets );

		Check( "blackegg: coming back to its star system, the destroyed planet is still gone and the others are there", stillGone && ( otherPlanets > 0 ), stillGone + ", " + otherPlanets );

		Finish( "scenario=blackegg dropped=" + eggsOnPlanet90 + " planet90=" + planet90Destroyed + " crystalAway=" + crystalAfterAway + " crystalNexus=" + crystalDestroyed + " stillGone=" + stillGone, 0 );
	}

	// the terrain vehicle takes the Black Egg and the Flat Device out of the ship's hold every time it goes out (the original's disembark, TV-OV WEE39), so that the egg
	// can be dropped and armed from the terrain vehicle's cargo; another artifact stays in the ship, and what is left in the terrain vehicle comes back on board
	IEnumerator ScenarioTvTakesEgg()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var blackEggId = gameData.FindArtifactId( "Black Egg" );
		var flatDeviceId = gameData.FindArtifactId( "Flat Device" );
		var whiningOrbId = gameData.FindArtifactId( "Whining Orb" );
		var planet90StarId = gameData.m_planetList[ 90 ].m_starId;
		var done = new bool[ 1 ];

		EnsureCrew();

		if ( ( blackEggId < 0 ) || ( flatDeviceId < 0 ) || ( whiningOrbId < 0 ) )
		{
			Finish( "scenario=tvtakesegg abort: an artifact is missing from the game data (" + blackEggId + ", " + flatDeviceId + ", " + whiningOrbId + ")", 2 );
			yield break;
		}

		// ---- 1. the three artifacts in the ship's hold, down on planet 90 and out through the real Disembark button
		ship.AddArtifact( blackEggId );
		ship.AddArtifact( flatDeviceId );
		ship.AddArtifact( whiningOrbId );

		yield return GoIntoOrbit( planet90StarId, 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		if ( !done[ 0 ] )
		{
			Finish( "scenario=tvtakesegg abort: never got into the terrain vehicle (" + playerData.m_general.m_location + ")", 2 );
			yield break;
		}

		var vehicle = playerData.m_terrainVehicle;
		var eggInVehicle = vehicle.HasArtifact( "Black Egg" );
		var flatInVehicle = vehicle.HasArtifact( "Flat Device" );
		var eggInShip = ship.HasArtifact( "Black Egg" );
		var flatInShip = ship.HasArtifact( "Flat Device" );

		Log( "tvtakesegg: after the disembark: vehicle egg " + eggInVehicle + " flat " + flatInVehicle + ", ship egg " + eggInShip + " flat " + flatInShip + " orb " + ship.HasArtifact( "Whining Orb" ) );

		Check( "tvtakesegg: the terrain vehicle has taken the Black Egg and the Flat Device out of the ship's hold", eggInVehicle && flatInVehicle && !eggInShip && !flatInShip, "vehicle egg " + eggInVehicle + " flat " + flatInVehicle + ", ship egg " + eggInShip + " flat " + flatInShip );

		Check( "tvtakesegg: the Whining Orb stays in the ship's hold (control)", ship.HasArtifact( "Whining Orb" ) && !vehicle.HasArtifact( "Whining Orb" ), "ship " + ship.HasArtifact( "Whining Orb" ) + ", vehicle " + vehicle.HasArtifact( "Whining Orb" ) );

		// ---- 2. the terrain vehicle's cargo list has the egg, and dropping it arms it
		var terrainVehicle = controller.m_terrainVehicle;

		foreach ( var deposit in controller.m_disembarked.m_terrainGrid.m_terrainElements.transform.GetComponentsInChildren<TerrainElement>( true ) )
		{
			if ( Vector3.Distance( deposit.transform.position, terrainVehicle.transform.position ) < 50.0f )
			{
				deposit.transform.position += Vector3.right * 1000.0f;
			}
		}

		var items = TVCargoButton.BuildCargoItems();
		var eggIndex = items.FindIndex( item => item.m_artifactId == blackEggId );
		var dropText = "";
		var eggsOnPlanet90 = 0;

		if ( eggIndex >= 0 )
		{
			controller.m_messages.Clear();

			TVCargoButton.OpenCargoList();

			for ( var i = 0; i < eggIndex; i++ )
			{
				TVCargoButton.MarkNextItem();
			}

			TVCargoButton.DropMarkedItem();

			yield return Frames( 2 );

			dropText = MessageList();

			foreach ( var droppedCargo in playerData.m_planetSurfaces.m_droppedCargoList )
			{
				if ( ( droppedCargo.m_planetId == 90 ) && ( droppedCargo.m_artifactId == blackEggId ) )
				{
					eggsOnPlanet90++;
				}
			}
		}

		Log( "tvtakesegg: the egg is item " + eggIndex + " of " + items.Count + " in the cargo list; " + eggsOnPlanet90 + " Black Egg on planet 90 | " + dropText.Substring( 0, Mathf.Min( 200, dropText.Length ) ) );

		Check( "tvtakesegg: the terrain vehicle's cargo list drops the Black Egg it took from the ship, and it is armed", ( eggIndex >= 0 ) && ( eggsOnPlanet90 == 1 ) && dropText.Contains( "armed" ), "item " + eggIndex + ", " + eggsOnPlanet90 + " | " + dropText );

		// ---- 3. back in the ship: the Flat Device comes back on board, the terrain vehicle's hold is empty
		TVCargoButton.BackToTerrainVehicle();

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		var vehicleArtifacts = ( vehicle.m_artifactStorage != null ) && ( vehicle.m_artifactStorage.m_artifactList != null ) ? vehicle.m_artifactStorage.m_artifactList.Count : 0;

		Check( "tvtakesegg: back in the ship, the Flat Device is in the ship's hold again and nothing is left in the terrain vehicle", ship.HasArtifact( "Flat Device" ) && !ship.HasArtifact( "Black Egg" ) && ( vehicleArtifacts == 0 ), "ship flat " + ship.HasArtifact( "Flat Device" ) + " egg " + ship.HasArtifact( "Black Egg" ) + ", vehicle artifacts " + vehicleArtifacts );

		Finish( "scenario=tvtakesegg eggIndex=" + eggIndex + " dropped=" + eggsOnPlanet90 + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}
	// ---------------------------------------------------------------- phase 4: the Crystal Cone

	// goes into orbit around a planet from its star system, clears the messages and waits a while in orbit - returns the messages
	static IEnumerator OrbitAndListen( int starId, int planetId, string[] messages )
	{
		var controller = SpaceflightController.m_instance;

		yield return GoIntoOrbit( starId, planetId );

		// once more from the star system, so that the messages are those of this orbit only
		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );

		controller.m_messages.Clear();

		yield return EnterOrbit( planetId );

		var end = Time.realtimeSinceStartup + 2.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			yield return null;
		}

		messages[ 0 ] = MessageList();
	}

	IEnumerator ScenarioCrystalCone()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var coneId = gameData.FindArtifactId( "Crystal Cone" );
		var crystalStarId = gameData.m_planetList[ 33 ].m_starId;
		var messages = new string[ 1 ];

		EnsureCrew();

		// the Crystal Orb keeps the field of the Crystal Planet away, and no Crystal Cone to begin with
		ship.AddArtifact( gameData.FindArtifactId( "Crystal Orb" ) );

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( coneId ) != null ); guard++ )
		{
			ship.RemoveArtifact( coneId );
		}

		// ---- 1. the control: in orbit around the Crystal Planet without the Cone, nothing about the nexus
		yield return OrbitAndListen( crystalStarId, 33, messages );

		var withoutTheCone = messages[ 0 ];

		// ---- 2. with the Cone in the hold, going into orbit reports the nexus
		ship.AddArtifact( coneId );

		yield return OrbitAndListen( crystalStarId, 33, messages );

		var withTheCone = messages[ 0 ];

		// ---- 3. and Land, which opens the map to pick the landing site, reports it again
		controller.m_messages.Clear();

		new LandButton().Execute();

		yield return Frames( 3 );

		var onLand = MessageList();

		// back to the bridge buttons for what follows
		controller.m_buttonController.SetBridgeButtons();

		// ---- 4. the control: with the Cone, in orbit around an ordinary planet, nothing about a nexus
		yield return OrbitAndListen( gameData.m_planetList[ 90 ].m_starId, 90, messages );

		var aroundPlanet90 = messages[ 0 ];

		Log( "crystalcone: without the Cone: " + withoutTheCone + " | with the Cone: " + withTheCone + " | on Land: " + onLand + " | around planet 90: " + aroundPlanet90 );

		Check( "crystalcone: in orbit around the Crystal Planet without the Crystal Cone nothing is said about the nexus (control)", !withoutTheCone.Contains( "nexus" ) && ( withoutTheCone.Length > 0 ), withoutTheCone );
		Check( "crystalcone: with the Crystal Cone, going into orbit around the Crystal Planet reports the control nexus at 47N x 45E", withTheCone.Contains( "control nexus" ) && withTheCone.Contains( "47N x 45E" ), withTheCone );
		Check( "crystalcone: with the Crystal Cone, Land over the Crystal Planet reports the nexus again", onLand.Contains( "47N x 45E" ), onLand );
		Check( "crystalcone: with the Crystal Cone, in orbit around an ordinary planet nothing is said about a nexus (control)", !aroundPlanet90.Contains( "nexus" ), aroundPlanet90 );

		Finish( "scenario=crystalcone without=" + withoutTheCone.Contains( "nexus" ) + " with=" + withTheCone.Contains( "47N x 45E" ) + " land=" + onLand.Contains( "47N x 45E" ) + " planet90=" + aroundPlanet90.Contains( "nexus" ), 0 );
	}

	// ---------------------------------------------------------------- phase 5: the win

	// sets a field if the code has it (so that this compiles on the code before it) - returns true if it did
	static bool SetFieldIfPresent( object target, string fieldName, object value )
	{
		var field = ( target == null ) ? null : target.GetType().GetField( fieldName );

		if ( field == null )
		{
			return false;
		}

		field.SetValue( target, value );

		return true;
	}

	// the star that flares soonest after the start, other than Arth's
	static int StarThatFlaresSoonest()
	{
		var gameData = DataController.m_instance.m_gameData;
		var starId = -1;

		for ( var id = 0; id < gameData.m_starList.Length; id++ )
		{
			var star = gameData.m_starList[ id ];

			if ( ( id == gameData.m_misc.m_arthStarId ) || ( star.m_daysToNextFlare <= 1 ) )
			{
				continue;
			}

			if ( ( starId < 0 ) || ( star.m_daysToNextFlare < gameData.m_starList[ starId ].m_daysToNextFlare ) )
			{
				starId = id;
			}
		}

		return starId;
	}

	// puts the game on a day (the clock works the game time out from it in the next update)
	static void SetGameDay( int day )
	{
		var general = DataController.m_instance.m_playerData.m_general;

		general.m_day = day;
		general.m_hour = 0;
		general.m_minute = 0;
		general.m_second = 0;
		general.m_gameTime = day;
	}

	// a star that flared before the game began, other than Arth's (its star system is safe whatever day it is)
	static int StarThatNeverFlares()
	{
		var gameData = DataController.m_instance.m_gameData;

		for ( var id = 0; id < gameData.m_starList.Length; id++ )
		{
			if ( ( id != gameData.m_misc.m_arthStarId ) && ( gameData.m_starList[ id ].m_daysToNextFlare <= 0 ) )
			{
				return id;
			}
		}

		return 0;
	}

	// enters the star system of a star the day before it flares and stays there into the day of its flare - returns whether the ship was destroyed
	static IEnumerator ThroughTheFlareDay( int starId, bool[] destroyed )
	{
		var star = DataController.m_instance.m_gameData.m_starList[ starId ];

		SetGameDay( star.m_daysToNextFlare - 1 );

		EnterStarSystem( starId );

		// wait for the planets, so that the star system is ready
		var end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && SpaceflightController.m_instance.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 10 );

		// the day of the flare comes
		SetGameDay( star.m_daysToNextFlare );

		yield return Frames( 15 );

		destroyed[ 0 ] = SpaceflightController.m_instance.m_combatController.PlayerIsDestroyed();
	}

	IEnumerator ScenarioWin()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var destroyed = new bool[ 1 ];

		EnsureCrew();

		var flareStarId = StarThatFlaresSoonest();

		// ---- 1. the win: a Black Egg at the control nexus of the Crystal Planet
		var atTheNexus = Tools.LatLongToWorldCoordinates( 45.0f, 47.0f );

		playerData.m_planetSurfaces.AddDroppedCargo( 33, atTheNexus.x, 0.0f, atTheNexus.z, -1, 0, gameData.FindArtifactId( "Black Egg" ) );

		ship.AddArtifact( gameData.FindArtifactId( "Crystal Orb" ) );

		yield return GoIntoOrbit( gameData.m_planetList[ 33 ].m_starId, 33 );

		var end = Time.realtimeSinceStartup + 12.0f;

		while ( ( Time.realtimeSinceStartup < end ) && ( playerData.m_general.m_location == PD_General.Location.InOrbit ) )
		{
			yield return null;
		}

		yield return Frames( 3 );

		var won = Equals( FieldOrNull( playerData.m_general, "m_gameWon" ), true );
		var bonusPending = Equals( FieldOrNull( playerData.m_general, "m_winBonusPending" ), true );

		Log( "win: after the egg at the nexus: won " + won + ", bonus pending " + bonusPending + ", location " + playerData.m_general.m_location );

		Check( "win: destroying the Crystal Planet at its control nexus wins the game, with Interstel's bonus to be paid", won && bonusPending, won + "/" + bonusPending );

		// ---- 2. after the win no star flares: in the star system of the star that flares soonest through the day of its flare (the gameover scenario has the control)
		yield return ThroughTheFlareDay( flareStarId, destroyed );

		var destroyedAfterTheWin = destroyed[ 0 ];

		Log( "win: in the system of star " + flareStarId + " through the day of its flare (day " + gameData.m_starList[ flareStarId ].m_daysToNextFlare + ") after the win: the ship destroyed " + destroyedAfterTheWin );

		Check( "win: after the win no star flares", !destroyedAfterTheWin, destroyedAfterTheWin.ToString() );

		// ---- 3. back at the Starport: the bonus, once
		playerData.m_general.m_currentStarId = gameData.m_misc.m_arthStarId;
		playerData.m_general.m_currentPlanetId = gameData.m_misc.m_arthPlanetId;

		var balanceBefore = playerData.m_bank.m_currentBalance;
		var ledgerBefore = playerData.m_bank.m_transactionList.Count;

		controller.m_messages.Clear();
		controller.SwitchLocation( PD_General.Location.DockingBay );

		yield return Frames( 10 );

		var paid = playerData.m_bank.m_currentBalance - balanceBefore;
		var ledgerEntry = ( playerData.m_bank.m_transactionList.Count > ledgerBefore ) ? playerData.m_bank.m_transactionList[ playerData.m_bank.m_transactionList.Count - 1 ] : null;
		var dockText = MessageList();

		// out and back in again
		controller.SwitchLocation( PD_General.Location.JustLaunched );

		yield return Frames( 10 );

		controller.SwitchLocation( PD_General.Location.DockingBay );

		yield return Frames( 10 );

		var paidAgain = playerData.m_bank.m_currentBalance - balanceBefore - paid;

		Log( "win: docking at the Starport paid " + paid + " MU (ledger: " + ( ( ledgerEntry == null ) ? "nothing" : ledgerEntry.m_description + " " + ledgerEntry.m_amount ) + "), docking again paid " + paidAgain + " | " + dockText );

		Check( "win: back at the Starport Interstel pays the bonus of 500,000 MU, in the ledger and the messages", ( paid == 500000 ) && ( ledgerEntry != null ) && ( ledgerEntry.m_amount == "500000+" ) && dockText.Contains( "500,000" ), paid + ", " + ( ( ledgerEntry == null ) ? "no entry" : ledgerEntry.m_amount ) + ", " + dockText );
		Check( "win: the bonus is paid once", paidAgain == 0, paidAgain.ToString() );

		// a later scenario must not start from a won game
		SetFieldIfPresent( playerData.m_general, "m_gameWon", false );

		Finish( "scenario=win won=" + won + " flaredAfter=" + destroyedAfterTheWin + " paid=" + paid + " paidAgain=" + paidAgain, 0 );
	}

	IEnumerator ScenarioStarportWin()
	{
		var playerData = DataController.m_instance.m_playerData;
		var operationsPanel = FindPanel<OperationsPanel>();
		var texts = new string[ 2 ];

		for ( var pass = 0; pass < 2; pass++ )
		{
			// first the control (not won), then won
			SetFieldIfPresent( playerData.m_general, "m_gameWon", pass == 1 );

			PanelController.m_instance.Open( operationsPanel );

			yield return new WaitForSecondsRealtime( 1.0f );

			operationsPanel.ShowEvaluations();

			yield return Frames( 2 );

			var textTransform = operationsPanel.m_evaluationGameObject.transform.Find( "Display/Error Text Mask/Error Text" );
			var text = ( textTransform == null ) ? null : textTransform.GetComponent<TMPro.TextMeshProUGUI>();

			texts[ pass ] = ( text == null ) ? "no text" : text.text.Replace( "\r", "" ).Replace( '\n', '/' );

			yield return ClosePanel( operationsPanel, "operations" );
		}

		SetFieldIfPresent( playerData.m_general, "m_gameWon", false );

		Log( "starport-win: the evaluation before the win: " + texts[ 0 ] + " | after it: " + texts[ 1 ] );

		Check( "starport-win: before the win the evaluation is the scene's (control)", texts[ 0 ].Contains( "colony world recommendations" ), texts[ 0 ] );
		Check( "starport-win: after the win the evaluation is Interstel's supplemental evaluation on the completion of the mission", texts[ 1 ].Contains( "COMPLETION OF MISSION" ) && texts[ 1 ].Contains( "500,000 MU" ), texts[ 1 ] );

		Finish( "scenario=starport-win before=" + texts[ 0 ].Contains( "colony" ) + " after=" + texts[ 1 ].Contains( "COMPLETION OF MISSION" ), 0 );
	}

	// ---------------------------------------------------------------- phase 5: Arth's sun flares

	// true if the save says the Starport has been destroyed (false on the code before it has the flag)
	static bool StarportDestroyed()
	{
		return Equals( FieldOrNull( DataController.m_instance.m_playerData.m_general, "m_starportDestroyed" ), true );
	}

	IEnumerator ScenarioArthFlare()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var arthStarId = gameData.m_misc.m_arthStarId;
		var arthPlanetId = gameData.m_misc.m_arthPlanetId;
		var flareDay = gameData.m_starList[ arthStarId ].m_daysToNextFlare;

		EnsureCrew();

		// ---- 1. away from Arth, in the system of a star that flared before the game began (planet 90 is in Arth's, and a star that flares in the first 300 days would
		// flare at the ship as the days go by): the day before the flare nothing happens (control), on the day the Starport is gone and the ship is fine
		var awayStarId = StarThatNeverFlares();

		EnterStarSystem( awayStarId );

		var end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		SetGameDay( flareDay - 1 );

		yield return Frames( 10 );

		var destroyedTheDayBefore = StarportDestroyed();

		SetGameDay( flareDay );

		yield return Frames( 10 );

		var destroyedOnTheDay = StarportDestroyed();
		var shipFineAway = !controller.m_combatController.PlayerIsDestroyed() && ( playerData.m_playerShip.m_armorPoints > 0 );

		Log( "arthflare: away from Arth (star " + awayStarId + "): the Starport destroyed the day before the flare (day " + ( flareDay - 1 ) + ") " + destroyedTheDayBefore + ", on the day " + destroyedOnTheDay + ", the ship fine " + shipFineAway );

		Check( "arthflare: the day before Arth's flare the Starport is there (control)", !destroyedTheDayBefore, destroyedTheDayBefore.ToString() );
		Check( "arthflare: on the day of Arth's flare the Starport is destroyed, and a ship in another star system is fine", destroyedOnTheDay && shipFineAway, destroyedOnTheDay + ", " + shipFineAway );

		// ---- 2. a distress signal gets no response
		controller.m_messages.Clear();

		new DistressButton().Execute();

		// (the call is confirmed first, as the original asks: TRANSMIT EMERGENCY DISTRESS CALL / CONFIRM)
		PressDistressChoice( 1 );

		var distressText = MessageList();

		Check( "arthflare: after the flare a distress signal gets no response", distressText.Contains( "NO RESPONSE" ), distressText );

		// ---- 3. in Arth's star system: no Starport model, and in orbital range of Arth no docking
		EnterStarSystem( arthStarId );

		end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 5 );

		var arthController = controller.m_starSystem.GetPlanetController( arthPlanetId );
		var starportShown = ( arthController != null ) && ( arthController.m_starportModel != null ) && arthController.m_starportModel.activeInHierarchy;

		controller.m_messages.Clear();

		for ( var frame = 0; ( frame < 20 ) && ( arthController != null ); frame++ )
		{
			playerData.m_general.m_coordinates = arthController.transform.localPosition;
			controller.m_playerShip.transform.position = arthController.transform.localPosition;

			yield return null;
		}

		var rangeText = MessageList();
		var planetToOrbit = controller.m_starSystem.m_planetToOrbitId;

		Log( "arthflare: in Arth's system after the flare: the Starport model shown " + starportShown + ", at Arth the planet to orbit " + planetToOrbit + " | " + rangeText );

		Check( "arthflare: after the flare Arth has no Starport, does not answer, and cannot be docked at", !starportShown && ( planetToOrbit != arthPlanetId ) && rangeText.Contains( "no response from Starport" ), starportShown + ", " + planetToOrbit + ", " + rangeText );

		// ---- 4. once the game has been won Arth's sun does not flare (the control for 5)
		SetFieldIfPresent( playerData.m_general, "m_starportDestroyed", false );
		SetFieldIfPresent( playerData.m_general, "m_gameWon", true );

		playerData.m_general.m_coordinates = new Vector3( 7900.0f, 0.0f, 0.0f );
		controller.m_playerShip.transform.position = playerData.m_general.m_coordinates;

		yield return Frames( 10 );

		var destroyedAfterTheWin = StarportDestroyed();

		Check( "arthflare: after the win Arth's sun does not flare (control)", !destroyedAfterTheWin, destroyedAfterTheWin.ToString() );

		// ---- 5. not won, in Arth's star system on the day: the ship and its crew are incinerated, and the game is over
		SetFieldIfPresent( playerData.m_general, "m_gameWon", false );

		controller.m_messages.Clear();

		end = Time.realtimeSinceStartup + 4.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !controller.m_combatController.PlayerIsDestroyed() )
		{
			yield return null;
		}

		yield return Frames( 3 );

		var incinerated = controller.m_combatController.PlayerIsDestroyed();
		var deathText = MessageList();

		Log( "arthflare: in Arth's system on the day: the ship destroyed " + incinerated + " | " + deathText );

		Check( "arthflare: a ship in Arth's star system when its sun flares is incinerated, and the game is over", incinerated && deathText.Contains( "INCINERATED" ) && deathText.Contains( "125, 100" ) && deathText.Contains( "01-01-4621" ), incinerated + " | " + deathText );

		Finish( "scenario=arthflare dayBefore=" + destroyedTheDayBefore + " onTheDay=" + destroyedOnTheDay + " starportShown=" + starportShown + " afterWin=" + destroyedAfterTheWin + " incinerated=" + incinerated, 0 );
	}

	// ---------------------------------------------------------------- phase 5: every way to lose ends in the same game over

	IEnumerator ScenarioGameOver()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var flareStarId = StarThatFlaresSoonest();
		var flareStar = gameData.m_starList[ flareStarId ];
		var otherStarId = StarThatNeverFlares();

		EnsureCrew();

		ship.m_shieldsAreUp = false;
		ship.m_armorPoints = ship.GetMaximumArmorPoints();

		var armorBefore = ship.m_armorPoints;

		// ---- 1. the control: coming into a star system on the day of its star's flare, the ship is fine (the star flares as the day before ends)
		SetGameDay( flareStar.m_daysToNextFlare );

		EnterStarSystem( flareStarId );

		var end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 15 );

		var fineOnArrival = !controller.m_combatController.PlayerIsDestroyed() && ( ship.m_armorPoints == armorBefore );

		// ---- 2. the control: the day before the flare, in its star system, the ship is fine (the port damaged a ship then)
		EnterStarSystem( otherStarId );

		yield return Frames( 5 );

		SetGameDay( flareStar.m_daysToNextFlare - 1 );

		EnterStarSystem( flareStarId );

		end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 15 );

		var fineTheDayBefore = !controller.m_combatController.PlayerIsDestroyed() && ( ship.m_armorPoints == armorBefore );

		Log( "gameover: star " + flareStarId + " (" + flareStar.m_xCoordinate + ", " + flareStar.m_yCoordinate + ") flares on day " + flareStar.m_daysToNextFlare + ": coming in on that day the ship is fine " + fineOnArrival + ", in its system the day before fine " + fineTheDayBefore + " (armor " + ship.m_armorPoints + " of " + armorBefore + ")" );

		Check( "gameover: a ship that comes into a star system on its star's flare day is not caught in the flare (control)", fineOnArrival, fineOnArrival.ToString() );
		Check( "gameover: in a star system the day before its star flares the ship is fine", fineTheDayBefore, ship.m_armorPoints + " of " + armorBefore );

		// ---- 3. the day of the flare comes with the ship there: incinerated, and the game over screen says so (once the explosion is over, 1.5 s)
		SetGameDay( flareStar.m_daysToNextFlare );

		end = Time.realtimeSinceStartup + 6.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !controller.m_gameOver )
		{
			yield return null;
		}

		// the message box is redrawn in its late update
		yield return Frames( 3 );

		var destroyed = controller.m_combatController.PlayerIsDestroyed();
		var gameOverText = MessageList();
		var coordinates = flareStar.m_xCoordinate + ", " + flareStar.m_yCoordinate;

		Log( "gameover: on the flare day in its system: destroyed " + destroyed + ", game over " + controller.m_gameOver + ", paused " + controller.m_gameIsPaused + " | " + gameOverText );

		Check( "gameover: a ship in a star system when its star flares is incinerated, and the game is over", destroyed && controller.m_gameOver && controller.m_gameIsPaused, destroyed + ", " + controller.m_gameOver + ", " + controller.m_gameIsPaused );
		Check( "gameover: the game over screen says the ship was incinerated by that star's flare, not that it was destroyed", gameOverText.Contains( "INCINERATED" ) && gameOverText.Contains( coordinates ) && !gameOverText.Contains( "Ship destroyed!" ) && gameOverText.Contains( "ESC" ), gameOverText );

		Finish( "scenario=gameover onArrival=" + fineOnArrival + " dayBefore=" + fineTheDayBefore + " incinerated=" + destroyed + " gameOver=" + controller.m_gameOver, 0 );
	}

	// ---------------------------------------------------------------- phase 5: what a Black Egg on Elan or on the Uhlek mind-ganglion does to those races

	IEnumerator ScenarioRaceLosses()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var ship = playerData.m_playerShip;
		var encounter = SpaceflightController.m_instance.m_encounter;
		var damage = 0;

		EnsureCrew();

		// a ship that survives being shot at for the whole scenario, with a communications officer who understands every word
		ship.m_armorPoints = 100000;
		ship.m_shieldsAreUp = false;

		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;

		var uhlekPlanetId = gameData.FindPlanetFromSun( 55, 32, 2 );
		var elanPlanetId = gameData.FindPlanetFromSun( 148, 63, 2 );

		// ---- 1. the control: the Uhlek attack
		var uhlekId = FindEncounter( 0, 1, 1, 0, GameData.Race.Uhlek );

		EnterEncounter( uhlekId );
		yield return Frames( 10 );

		yield return DamageTaken( 10.0f, value => damage = value );

		var uhlekDamageBefore = damage;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. once their mind-ganglion's planet has been destroyed they are harmless
		playerData.m_planetSurfaces.DestroyPlanet( uhlekPlanetId );

		EnterEncounter( uhlekId );
		yield return Frames( 10 );

		yield return DamageTaken( 10.0f, value => damage = value );

		var uhlekDamageAfter = damage;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		Log( "racelosses: uhlek encounter " + uhlekId + ": damage taken in 10 s before their mind-ganglion's planet " + uhlekPlanetId + " was destroyed " + uhlekDamageBefore + ", after " + uhlekDamageAfter );

		Check( "racelosses: the Uhlek attack (control)", uhlekDamageBefore > 0, uhlekDamageBefore.ToString() );
		Check( "racelosses: once the planet of their mind-ganglion has been destroyed the Uhlek are harmless", uhlekDamageAfter == 0, uhlekDamageAfter.ToString() );

		// (the Uhlek's fire damaged the ship's systems as well as its hull - the comm among them would garble what the Elowan say)
		var restoreSystems = typeof( PD_PlayerShip ).GetMethod( "RestoreSystems" );

		if ( restoreSystems != null )
		{
			restoreSystems.Invoke( ship, null );
		}

		// ---- 3. the control: the Elowan meet a ship without hostility, and hail it
		var elowanId = FindEncounter( 0, 1, 1, 0, GameData.Race.Elowan );

		ForceVessel( elowanId, 7 );

		EnterEncounter( elowanId );
		yield return Frames( 10 );

		var elowanStanceBefore = Stance();

		encounter.m_pdEncounter.m_conversationTimer = 0.1f;

		yield return new WaitForSecondsRealtime( 1.0f );

		var elowanWordsBefore = MessageList();

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 4. after Elan: hostile, their one comm, no comm link for a hail, and they attack
		playerData.m_planetSurfaces.DestroyPlanet( elanPlanetId );

		SpaceflightController.m_instance.m_messages.Clear();

		EnterEncounter( elowanId );
		yield return Frames( 10 );

		var elowanStanceAfter = Stance();

		encounter.m_pdEncounter.m_conversationTimer = 0.1f;

		yield return new WaitForSecondsRealtime( 1.0f );

		var elowanWordsAfter = MessageList();

		// the player hails: no answer
		encounter.m_pdEncounter.m_playerWantsToConnect = true;
		encounter.m_pdEncounter.m_conversationTimer = 0.1f;

		yield return DamageTaken( 10.0f, value => damage = value );

		var elowanConnected = encounter.m_pdEncounter.m_connected;
		var elowanDamageAfter = damage;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		Log( "racelosses: elowan encounter " + elowanId + ": before Elan (planet " + elanPlanetId + ") was destroyed: stance " + elowanStanceBefore + " | " + elowanWordsBefore + " || after: stance " + elowanStanceAfter + ", connected after a hail " + elowanConnected + ", damage in 10 s " + elowanDamageAfter + " | " + elowanWordsAfter );

		Check( "racelosses: the Elowan meet the ship without hostility and say nothing of Elan (control)", ( elowanStanceBefore != "Hostile" ) && !elowanWordsBefore.Contains( "unthinkably foul" ), elowanStanceBefore + " | " + elowanWordsBefore );
		Check( "racelosses: after Elan has been destroyed the Elowan are hostile and say it was a deed most unthinkably foul", ( elowanStanceAfter == "Hostile" ) && elowanWordsAfter.Contains( "unthinkably foul" ), elowanStanceAfter + " | " + elowanWordsAfter );
		Check( "racelosses: after Elan the Elowan do not answer a hail, and they attack", !elowanConnected && ( elowanDamageAfter > 0 ), elowanConnected + ", " + elowanDamageAfter );

		Finish( "scenario=racelosses uhlekBefore=" + uhlekDamageBefore + " uhlekAfter=" + uhlekDamageAfter + " elowanBefore=" + elowanStanceBefore + " elowanAfter=" + elowanStanceAfter + " connected=" + elowanConnected + " elowanDamage=" + elowanDamageAfter, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Tesseract

	// flies in hyperspace with the engines on for some real seconds from a standstill - returns the fuel used (in cubic meters) and whether the ship stayed in hyperspace
	static IEnumerator HyperspaceFuelUsed( float seconds, float[] fuelUsed, bool[] stayed )
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		controller.m_playerShip.TurnOffEngines();
		playerData.m_general.m_currentSpeed = 0.0f;

		var enduriumBefore = Endurium();
		var fuelUsedBefore = playerData.m_playerShip.m_fuelUsed;

		controller.m_playerShip.TurnOnEngines();

		var end = Time.realtimeSinceStartup + seconds;

		stayed[ 0 ] = true;

		while ( Time.realtimeSinceStartup < end )
		{
			stayed[ 0 ] = stayed[ 0 ] && ( playerData.m_general.m_location == PD_General.Location.Hyperspace );

			yield return null;
		}

		controller.m_playerShip.TurnOffEngines();

		// what went from the hold (in tenths) and what is still counted towards the next tenth
		fuelUsed[ 0 ] = ( enduriumBefore - Endurium() ) * 0.1f + ( playerData.m_playerShip.m_fuelUsed - fuelUsedBefore );
	}

	IEnumerator ScenarioTesseract()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var tesseractId = gameData.FindArtifactId( "Tesseract" );
		var fuelUsed = new float[ 1 ];
		var stayed = new bool[ 1 ];

		EnsureCrew();

		// plenty of Endurium, no Tesseract to begin with
		ship.AddElement( 5, 500 );

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( tesseractId ) != null ); guard++ )
		{
			ship.RemoveArtifact( tesseractId );
		}

		// out in hyperspace, in a corner of it, with no encounter able to begin while the fuel is measured
		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 10 );

		// ---- 1. the control: the fuel the engines use in 3 s without the Tesseract
		yield return HyperspaceFuelUsed( 3.0f, fuelUsed, stayed );

		var withoutTheTesseract = fuelUsed[ 0 ];
		var stayedWithout = stayed[ 0 ];

		// ---- 2. the same with the Tesseract in the hold
		ship.AddArtifact( tesseractId );

		yield return HyperspaceFuelUsed( 3.0f, fuelUsed, stayed );

		var withTheTesseract = fuelUsed[ 0 ];
		var stayedWith = stayed[ 0 ];

		controller.m_encounterRange = encounterRange;

		var ratio = ( withoutTheTesseract > 0.0f ) ? ( withTheTesseract / withoutTheTesseract ) : -1.0f;

		Log( "tesseract: fuel the engines used in 3 s of hyperspace from a standstill: without the Tesseract " + withoutTheTesseract.ToString( "0.0000" ) + " m3, with it " + withTheTesseract.ToString( "0.0000" ) + " m3, ratio " + ratio.ToString( "0.00" ) + " (stayed in hyperspace " + stayedWithout + "/" + stayedWith + ")" );

		Check( "tesseract: the engines use fuel in hyperspace (control)", ( withoutTheTesseract > 0.0f ) && stayedWithout, withoutTheTesseract + ", " + stayedWithout );
		Check( "tesseract: with the Tesseract in the hold the engines use half the fuel", ( ratio >= 0.4f ) && ( ratio <= 0.6f ) && stayedWith, ratio + ", " + stayedWith );

		Finish( "scenario=tesseract without=" + withoutTheTesseract.ToString( "0.0000" ) + " with=" + withTheTesseract.ToString( "0.0000" ) + " ratio=" + ratio.ToString( "0.00" ), 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Whining Orb

	// what the messages show of this comm, said by the aliens of the current encounter
	static string AlienCommShown( GD_Comm comm )
	{
		SpaceflightController.m_instance.m_messages.Clear();

		SpaceflightController.m_instance.m_encounter.AddComm( GD_Comm.Subject.Statement, false, comm );

		return MessageList();
	}

	IEnumerator ScenarioWhiningOrb()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var ship = playerData.m_playerShip;
		var orbId = gameData.FindArtifactId( "Whining Orb" );

		EnsureCrew();

		// a communications officer who understands almost nothing, and no Whining Orb to begin with
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 0;

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( orbId ) != null ); guard++ )
		{
			ship.RemoveArtifact( orbId );
		}

		// a statement of the Spemin from the game data with nothing in it that is filled in
		GD_Comm speminComm = null;

		foreach ( var comm in gameData.m_commList )
		{
			if ( ( comm.m_race == GameData.Race.Spemin ) && ( comm.m_subject == GD_Comm.Subject.Statement ) && ( comm.m_text.IndexOfAny( new[] { '&', '*', '/', '(', ')' } ) < 0 ) )
			{
				speminComm = comm;

				break;
			}
		}

		// ---- 1. the control: the Spemin, without the Orb, are garbled
		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var withoutTheOrb = AlienCommShown( speminComm );

		// ---- 2. with the Whining Orb in the hold every word comes through
		ship.AddArtifact( orbId );

		var withTheOrb = AlienCommShown( speminComm );

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. the control: the Orb translates only the Spemin - an Elowan statement is still garbled
		GD_Comm elowanComm = null;

		foreach ( var comm in gameData.m_commList )
		{
			if ( ( comm.m_race == GameData.Race.Elowan ) && ( comm.m_subject == GD_Comm.Subject.Statement ) && ( comm.m_text.IndexOfAny( new[] { '&', '*', '/', '(', ')' } ) < 0 ) )
			{
				elowanComm = comm;

				break;
			}
		}

		EnterEncounter( FindEncounter( 0, 1, 1, 0, GameData.Race.Elowan ) );
		yield return Frames( 10 );

		var elowanWithTheOrb = AlienCommShown( elowanComm );

		LeaveEncounter();
		yield return Frames( 10 );

		Log( "whiningorb: spemin comm " + speminComm.m_id + " without the Orb: " + withoutTheOrb + " | with it: " + withTheOrb + " | elowan comm " + elowanComm.m_id + " with the Orb: " + elowanWithTheOrb );

		Check( "whiningorb: without the Whining Orb a crew with no skill in communications gets the Spemin garbled (control)", !withoutTheOrb.Contains( speminComm.m_text ), withoutTheOrb );
		Check( "whiningorb: with the Whining Orb in the hold every word of the Spemin comes through", withTheOrb.Contains( speminComm.m_text ), withTheOrb );
		Check( "whiningorb: the Whining Orb translates only the Spemin (an Elowan statement is still garbled)", !elowanWithTheOrb.Contains( elowanComm.m_text ), elowanWithTheOrb );

		Finish( "scenario=whiningorb without=" + withoutTheOrb.Contains( speminComm.m_text ) + " with=" + withTheOrb.Contains( speminComm.m_text ) + " elowan=" + elowanWithTheOrb.Contains( elowanComm.m_text ), 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Rod Device

	// the armor points one shot of this alien vessel takes off the player ship, with its shields down and armor to spare
	static int ArmorLostToOneShot( PD_AlienShip alienShip, GD_Vessel vessel )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		ship.m_shieldsAreUp = false;
		ship.m_armorPoints = 100000;

		CombatController.m_instance.AlienFiresAtPlayer( alienShip, vessel );

		return 100000 - ship.m_armorPoints;
	}

	IEnumerator ScenarioRodDevice()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var ship = playerData.m_playerShip;
		var rodId = gameData.FindArtifactId( "Rod Device" );

		EnsureCrew();

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( rodId ) != null ); guard++ )
		{
			ship.RemoveArtifact( rodId );
		}

		// a vessel with a laser and no plasma bolts, and one with plasma bolts
		GD_Vessel laserVessel = null;
		GD_Vessel plasmaVessel = null;

		foreach ( var vessel in gameData.m_vesselList )
		{
			if ( ( laserVessel == null ) && !vessel.m_hasPlasmaBolts && ( vessel.m_laserClass > 0 ) )
			{
				laserVessel = vessel;
			}

			if ( ( plasmaVessel == null ) && vessel.m_hasPlasmaBolts )
			{
				plasmaVessel = vessel;
			}
		}

		// in an encounter, for an alien ship to fire from
		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var alienShip = dataController.m_playerData.FindEncounter( speminId ).GetAlienShipList()[ 0 ];

		// ---- 1. the control: what a laser and a plasma bolt take off without the Rod Device
		var laserWithout = ArmorLostToOneShot( alienShip, laserVessel );
		var plasmaWithout = ( plasmaVessel == null ) ? -1 : ArmorLostToOneShot( alienShip, plasmaVessel );

		// ---- 2. with the Rod Device in the hold: half of the laser, the plasma bolt as before
		ship.AddArtifact( rodId );

		var laserWith = ArmorLostToOneShot( alienShip, laserVessel );
		var plasmaWith = ( plasmaVessel == null ) ? -1 : ArmorLostToOneShot( alienShip, plasmaVessel );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		Log( "roddevice: one laser shot of " + laserVessel.m_name + " (laser class " + laserVessel.m_laserClass + ") takes " + laserWithout + " armor points without the Rod Device, " + laserWith + " with it | a plasma bolt of " + ( ( plasmaVessel == null ) ? "none" : plasmaVessel.m_name ) + " " + plasmaWithout + " without, " + plasmaWith + " with" );

		Check( "roddevice: an alien laser hits the ship (control)", laserWithout > 0, laserWithout.ToString() );
		Check( "roddevice: with the Rod Device in the hold an alien laser does half the damage", ( laserWithout > 0 ) && ( laserWith == laserWithout / 2 ), laserWithout + " -> " + laserWith );
		Check( "roddevice: the Rod Device is a laser shield - a plasma bolt does the same damage (control)", plasmaWith == plasmaWithout, plasmaWithout + " -> " + plasmaWith );

		Finish( "scenario=roddevice laser=" + laserWithout + "/" + laserWith + " plasma=" + plasmaWithout + "/" + plasmaWith, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Crystal Pearl

	IEnumerator ScenarioCrystalPearl()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;
		var pearlId = gameData.FindArtifactId( "Crystal Pearl" );
		var maximum = ship.GetMaximumArmorPoints();
		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnsureCrew();

		ship.m_shieldsAreUp = false;

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( pearlId ) != null ); guard++ )
		{
			ship.RemoveArtifact( pearlId );
		}

		// ---- 1. the control: without the Crystal Pearl a ship hit down to a twentieth of its armor stays in the encounter
		ship.m_armorPoints = maximum;

		EnterEncounter( speminId );
		yield return Frames( 10 );

		combat.ApplyDamageToPlayer( maximum - maximum / 20, Vector3.forward );

		yield return Frames( 10 );

		var locationWithout = playerData.m_general.m_location;
		var armorWithout = ship.m_armorPoints;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. with the Crystal Pearl the same hit warps the ship out of the encounter, with the armor it has left
		ship.AddArtifact( pearlId );
		ship.m_armorPoints = maximum;

		EnterEncounter( speminId );
		yield return Frames( 10 );

		SpaceflightController.m_instance.m_messages.Clear();

		combat.ApplyDamageToPlayer( maximum - maximum / 20, Vector3.forward );

		yield return Frames( 10 );

		var locationWith = playerData.m_general.m_location;
		var armorWith = ship.m_armorPoints;
		var warpText = MessageList();

		// ---- 3. and a hit that would destroy the ship warps it out with 1 armor point
		ship.m_armorPoints = maximum;

		EnterEncounter( speminId );
		yield return Frames( 10 );

		combat.ApplyDamageToPlayer( maximum * 2, Vector3.forward );

		yield return Frames( 10 );

		var locationAfterLethal = playerData.m_general.m_location;
		var armorAfterLethal = ship.m_armorPoints;
		var destroyed = combat.PlayerIsDestroyed();

		Log( "crystalpearl: a hit down to " + ( maximum / 20 ) + " of " + maximum + " armor points without the Pearl: " + locationWithout + " with " + armorWithout + " | with the Pearl: " + locationWith + " with " + armorWith + " (" + warpText + ") | a hit of " + ( maximum * 2 ) + " with the Pearl: " + locationAfterLethal + " with " + armorAfterLethal + ", destroyed " + destroyed );

		Check( "crystalpearl: without the Crystal Pearl a critically wounded ship stays in the encounter (control)", ( locationWithout == PD_General.Location.Encounter ) && ( armorWithout == maximum / 20 ), locationWithout + ", " + armorWithout );
		Check( "crystalpearl: with the Crystal Pearl a critically wounded ship is warped out of the encounter", ( locationWith != PD_General.Location.Encounter ) && ( armorWith == maximum / 20 ) && warpText.Contains( "Crystal Pearl" ), locationWith + ", " + armorWith + " | " + warpText );
		Check( "crystalpearl: with the Crystal Pearl a hit that would destroy the ship warps it out with 1 armor point", ( locationAfterLethal != PD_General.Location.Encounter ) && ( armorAfterLethal == 1 ) && !destroyed, locationAfterLethal + ", " + armorAfterLethal + ", " + destroyed );

		Finish( "scenario=crystalpearl without=" + locationWithout + "/" + armorWithout + " with=" + locationWith + "/" + armorWith + " lethal=" + locationAfterLethal + "/" + armorAfterLethal + "/" + destroyed, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Dodecahedron

	// puts an encounter at its home, the ship (stopped) one and a half alien radar distances from it in hyperspace, and returns how much closer the encounter came in some real seconds
	static IEnumerator EncounterCameCloser( PD_Encounter pdEncounter, float seconds, float[] cameCloser )
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		pdEncounter.m_currentCoordinates = pdEncounter.m_homeCoordinates;

		var shipCoordinates = pdEncounter.m_homeCoordinates + Vector3.right * ( controller.m_alienHyperspaceRadarDistance * 1.5f );

		controller.m_playerShip.TurnOffEngines();
		playerData.m_general.m_currentSpeed = 0.0f;
		playerData.m_general.m_coordinates = shipCoordinates;
		playerData.m_general.m_lastHyperspaceCoordinates = shipCoordinates;
		controller.m_playerShip.transform.position = shipCoordinates;

		yield return Frames( 2 );

		var before = Vector3.Distance( pdEncounter.m_currentCoordinates, playerData.m_general.m_coordinates );
		var end = Time.realtimeSinceStartup + seconds;

		while ( Time.realtimeSinceStartup < end )
		{
			yield return null;
		}

		cameCloser[ 0 ] = before - Vector3.Distance( pdEncounter.m_currentCoordinates, playerData.m_general.m_coordinates );
	}

	IEnumerator ScenarioDodecahedron()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var dodecahedronId = gameData.FindArtifactId( "Dodecahedron" );
		var cameCloser = new float[ 1 ];

		EnsureCrew();

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( dodecahedronId ) != null ); guard++ )
		{
			ship.RemoveArtifact( dodecahedronId );
		}

		// in hyperspace, with no encounter able to begin while the aliens are watched
		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 10 );

		var encounterId = FindEncounter( 0, 1, 1, 0 );
		var pdEncounter = playerData.FindEncounter( encounterId );

		// ---- 1. the control: without the Dodecahedron aliens one and a half radar distances away do not come
		yield return EncounterCameCloser( pdEncounter, 2.0f, cameCloser );

		var closerWithout = cameCloser[ 0 ];

		// ---- 2. with the Dodecahedron in the hold they do
		ship.AddArtifact( dodecahedronId );

		yield return EncounterCameCloser( pdEncounter, 2.0f, cameCloser );

		var closerWith = cameCloser[ 0 ];

		controller.m_encounterRange = encounterRange;

		Log( "dodecahedron: encounter " + encounterId + " at its home, the ship " + ( controller.m_alienHyperspaceRadarDistance * 1.5f ) + " away (alien radar distance " + controller.m_alienHyperspaceRadarDistance + "): came closer in 2 s by " + closerWithout + " without the Dodecahedron, " + closerWith + " with it" );

		Check( "dodecahedron: without the Dodecahedron aliens beyond their radar distance do not come at the ship (control)", Mathf.Abs( closerWithout ) < 1.0f, closerWithout.ToString() );
		Check( "dodecahedron: with the Dodecahedron in the hold aliens from twice as far come at the ship", closerWith > 1.0f, closerWith.ToString() );

		Finish( "scenario=dodecahedron without=" + closerWithout + " with=" + closerWith, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Shimmering Ball

	IEnumerator ScenarioShimmeringBall()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;
		var ballId = gameData.FindArtifactId( "Shimmering Ball" );

		EnsureCrew();

		// a ship that can fire its laser, and no Shimmering Ball to begin with
		ship.m_laserCannonClass = 1;
		ship.AddElement( 5, 50 );

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( ballId ) != null ); guard++ )
		{
			ship.RemoveArtifact( ballId );
		}

		GD_Vessel laserVessel = null;

		foreach ( var vessel in gameData.m_vesselList )
		{
			if ( ( laserVessel == null ) && !vessel.m_hasPlasmaBolts && ( vessel.m_laserClass > 0 ) )
			{
				laserVessel = vessel;
			}
		}

		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var alienShip = playerData.FindEncounter( speminId ).GetAlienShipList()[ 0 ];

		// ---- 1. the control: without the Shimmering Ball an alien laser hits
		var hitWithout = ArmorLostToOneShot( alienShip, laserVessel );

		// ---- 2. with the Shimmering Ball in the hold the aliens cannot fire at the cloaked ship
		ship.AddArtifact( ballId );

		var hitCloaked = ArmorLostToOneShot( alienShip, laserVessel );

		// ---- 3. a shot of the player drops the cloak: the aliens can fire back
		combat.SetTarget( FirstLivingAlien() );

		BringAliensClose();

		var laserFired = combat.FirePlayerLaser();
		var hitAfterFiring = ArmorLostToOneShot( alienShip, laserVessel );

		// ---- 4. and a few seconds later the ship is cloaked again
		yield return new WaitForSecondsRealtime( 4.0f );

		var hitLater = ArmorLostToOneShot( alienShip, laserVessel );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		Log( "shimmeringball: one laser shot of a " + laserVessel.m_name + ": without the Shimmering Ball " + hitWithout + ", cloaked " + hitCloaked + ", right after the player fired (fired " + laserFired + ") " + hitAfterFiring + ", 4 s later " + hitLater );

		Check( "shimmeringball: without the Shimmering Ball an alien laser hits (control)", hitWithout > 0, hitWithout.ToString() );
		Check( "shimmeringball: with the Shimmering Ball the aliens cannot fire at the cloaked ship", hitCloaked == 0, hitCloaked.ToString() );
		Check( "shimmeringball: a shot of the player drops the cloak, and the aliens can fire back", laserFired && ( hitAfterFiring > 0 ), laserFired + ", " + hitAfterFiring );
		Check( "shimmeringball: a few seconds after the player's shot the ship is cloaked again", hitLater == 0, hitLater.ToString() );

		Finish( "scenario=shimmeringball without=" + hitWithout + " cloaked=" + hitCloaked + " afterFiring=" + hitAfterFiring + " later=" + hitLater, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Ring Device

	// how many of the fluxes the hyperspace location shows (its copies of the flux template that are active)
	static int FluxesShown()
	{
		var hyperspace = SpaceflightController.m_instance.m_hyperspace;
		var template = GetField( hyperspace, "m_fluxTemplate" ) as GameObject;
		var cloneName = template.name + "(Clone)";
		var shown = 0;

		foreach ( Transform child in hyperspace.transform )
		{
			if ( ( child.name == cloneName ) && child.gameObject.activeSelf )
			{
				shown++;
			}
		}

		return shown;
	}

	IEnumerator ScenarioRingDevice()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var ringId = gameData.FindArtifactId( "Ring Device" );
		var fluxCount = gameData.m_fluxList.Length;

		EnsureCrew();

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( ringId ) != null ); guard++ )
		{
			ship.RemoveArtifact( ringId );
		}

		// in hyperspace, with no encounter able to begin
		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		// out in a corner of hyperspace (the game starts at Arth, and coming out there would put the ship back into Arth's star system at once)
		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 10 );

		// ---- 1. without the Ring Device no flux is shown
		var shownWithout = FluxesShown();

		// ---- 2. a flux that is not shown is still there: the ship that flies into it goes through
		var flux = gameData.m_fluxList[ 0 ];

		controller.m_playerShip.TurnOffEngines();
		playerData.m_general.m_currentSpeed = 0.0f;
		playerData.m_general.m_coordinates = flux.GetFrom();
		controller.m_playerShip.transform.position = flux.GetFrom();

		yield return Frames( 5 );

		var throughHiddenFlux = Equals( GetField( controller.m_hyperspace, "m_travelingThroughFlux" ), true ) && ( playerData.m_general.m_location == PD_General.Location.Hyperspace );

		// wait until the ship is out of the flux again
		var end = Time.realtimeSinceStartup + 20.0f;

		while ( ( Time.realtimeSinceStartup < end ) && Equals( GetField( controller.m_hyperspace, "m_travelingThroughFlux" ), true ) )
		{
			yield return null;
		}

		yield return Frames( 5 );

		// ---- 3. with the Ring Device in the hold every flux is shown
		ship.AddArtifact( ringId );

		yield return Frames( 5 );

		var shownWith = FluxesShown();

		controller.m_encounterRange = encounterRange;

		Log( "ringdevice: fluxes shown in hyperspace without the Ring Device " + shownWithout + ", with it " + shownWith + " of " + fluxCount + " | without it the ship flew into the flux at " + flux.m_x1 + ", " + flux.m_y1 + ": going through " + throughHiddenFlux );

		Check( "ringdevice: without the Ring Device no continuum flux is shown", shownWithout == 0, shownWithout + " of " + fluxCount );
		Check( "ringdevice: a flux that is not shown is still there, and a ship that flies into it goes through (control)", throughHiddenFlux, throughHiddenFlux.ToString() );
		Check( "ringdevice: with the Ring Device in the hold every continuum flux is shown", shownWith == fluxCount, shownWith + " of " + fluxCount );

		Finish( "scenario=ringdevice without=" + shownWithout + " with=" + shownWith + " of=" + fluxCount + " through=" + throughHiddenFlux, 0 );
	}

	// ---------------------------------------------------------------- phase 1: the price of Endurium

	// what the Starport pays for a cubic meter of an element (through the code's own method where it has one, so that this compiles on the code before it)
	static int ElementSaleValue( int elementId )
	{
		var element = DataController.m_instance.m_gameData.m_elementList[ elementId ];
		var method = element.GetType().GetMethod( "GetActualValue" );

		return ( method != null ) ? (int) method.Invoke( element, null ) : element.m_actualValue;
	}

	// buys or sells one cubic meter of Endurium at the trade depot, the way the player would - returns the change of the bank balance
	static int TradeOneCubicMeterOfEndurium( TradeDepotPanel depot, bool buy )
	{
		var playerData = DataController.m_instance.m_playerData;
		var balanceBefore = playerData.m_bank.m_currentBalance;

		Call( depot, buy ? "SwitchToBuyItemState" : "SwitchToSellItemState", true );

		var items = GetField( depot, "m_itemList" ) as IList;
		var index = -1;

		for ( var i = 0; i < items.Count; i++ )
		{
			if ( ( (int) GetField( items[ i ], "m_type" ) == 0 ) && ( (int) GetField( items[ i ], "m_id" ) == 5 ) )
			{
				index = i;
			}
		}

		if ( index < 0 )
		{
			return 0;
		}

		SetField( depot, "m_currentItemIndex", index );

		Call( depot, buy ? "SwitchToBuyAmountState" : "SwitchToSellAmountState" );

		depot.m_amountInputField.text = "1";
		depot.OnEndEdit();

		return playerData.m_bank.m_currentBalance - balanceBefore;
	}

	IEnumerator ScenarioStarportEndurium()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var depot = FindPanel<TradeDepotPanel>();
		var dates = new[] { "4620-02-19", "4620-02-20", "4620-05-15" };
		var bought = new int[ dates.Length ];
		var sold = new int[ dates.Length ];
		var molybdenumBefore = 0;
		var molybdenumAfter = 0;

		playerData.m_bank.m_currentBalance = 1000000;
		playerData.m_playerShip.AddElement( 5, 100 );

		yield return OpenPanel( depot, "trade depot" );

		for ( var dateIndex = 0; dateIndex < dates.Length; dateIndex++ )
		{
			playerData.m_general.m_currentStardateYMD = dates[ dateIndex ];

			if ( dateIndex == 0 )
			{
				molybdenumBefore = ElementSaleValue( 11 );
			}

			bought[ dateIndex ] = -TradeOneCubicMeterOfEndurium( depot, true );
			sold[ dateIndex ] = TradeOneCubicMeterOfEndurium( depot, false );

			yield return Frames( 2 );
		}

		molybdenumAfter = ElementSaleValue( 11 );

		Call( depot, "SwitchToMenuBarState" );

		yield return ClosePanel( depot, "trade depot" );

		Log( "starport-endurium: a cubic meter of Endurium bought / sold on " + dates[ 0 ] + ": " + bought[ 0 ] + " / " + sold[ 0 ] + ", on " + dates[ 1 ] + ": " + bought[ 1 ] + " / " + sold[ 1 ] + ", on " + dates[ 2 ] + ": " + bought[ 2 ] + " / " + sold[ 2 ] + " | what the Starport pays for molybdenum " + molybdenumBefore + " -> " + molybdenumAfter );

		Check( "starport-endurium: before 20-02-4620 Endurium costs 1000 MU, and the Starport buys it back for the same", ( bought[ 0 ] == 1000 ) && ( sold[ 0 ] == 1000 ), bought[ 0 ] + " / " + sold[ 0 ] );
		Check( "starport-endurium: from 20-02-4620 Endurium is bought and sold for 1500 MU", ( bought[ 1 ] == 1500 ) && ( sold[ 1 ] == 1500 ), bought[ 1 ] + " / " + sold[ 1 ] );
		Check( "starport-endurium: from 15-05-4620 Endurium is bought and sold for 2000 MU", ( bought[ 2 ] == 2000 ) && ( sold[ 2 ] == 2000 ), bought[ 2 ] + " / " + sold[ 2 ] );
		Check( "starport-endurium: the other minerals keep their prices (control)", molybdenumBefore == molybdenumAfter, molybdenumBefore + " -> " + molybdenumAfter );

		Finish( "scenario=starport-endurium " + dates[ 0 ] + "=" + bought[ 0 ] + "/" + sold[ 0 ] + " " + dates[ 1 ] + "=" + bought[ 1 ] + "/" + sold[ 1 ] + " " + dates[ 2 ] + "=" + bought[ 2 ] + "/" + sold[ 2 ], 0 );
	}

	// ---------------------------------------------------------------- phase 1: nebulae keep the shields down

	// presses Raise Shields - returns whether the shields are up afterwards
	static bool PressRaiseShields()
	{
		new RaiseShieldsButton().Execute();

		return DataController.m_instance.m_playerData.m_playerShip.m_shieldsAreUp;
	}

	IEnumerator ScenarioNebula()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;

		EnsureCrew();

		// shields to raise, and the Endurium they burn
		ship.m_shieldingClass = 1;
		ship.AddElement( 5, 100 );

		// a star inside a nebula, and one that is not (that flared before the game began, so its system is safe whatever the day)
		var nebulaStarId = -1;

		for ( var starId = 0; ( starId < gameData.m_starList.Length ) && ( nebulaStarId < 0 ); starId++ )
		{
			if ( gameData.m_starList[ starId ].m_insideNebula && ( starId != gameData.m_misc.m_arthStarId ) )
			{
				nebulaStarId = starId;
			}
		}

		var clearStarId = StarThatNeverFlares();

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		// ---- 1. the control: in a star system outside the nebulae the shields come up
		EnterStarSystem( clearStarId );

		var generating = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < generating ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 10 );

		ship.DropShields();

		var upInClearSystem = PressRaiseShields();

		yield return Frames( 5 );

		var stillUpInClearSystem = ship.m_shieldsAreUp;

		// ---- 2. coming into a star system inside a nebula with the shields up, they collapse, and they cannot be raised there
		EnterStarSystem( nebulaStarId );

		var end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		// (the messages of every frame: a radar message can push the nebula's out of the list before the check reads it)
		var collapseText = MessageList();

		for ( var frame = 0; frame < 10; frame++ )
		{
			yield return null;

			collapseText += " / " + MessageList();
		}

		var upAfterComingIn = ship.m_shieldsAreUp;
		var upInNebulaSystem = PressRaiseShields();

		// ---- 3. in hyperspace inside a nebula the shields cannot be raised; outside them they can (the control)
		// (a nebula whose center is clear of every star, or the ship would come out of hyperspace into that star's system at once)
		GD_Nebula nebula = null;

		foreach ( var candidate in gameData.m_nebulaList )
		{
			var center = Tools.GameToWorldCoordinates( new Vector3( candidate.m_xCoordinate, 0.0f, candidate.m_yCoordinate ) );
			var clear = true;

			foreach ( var star in gameData.m_starList )
			{
				if ( Vector3.Distance( center, star.GetWorldCoordinates() ) < star.GetBreachDistance() * 4.0f )
				{
					clear = false;
				}
			}

			if ( clear && ( nebula == null ) )
			{
				nebula = candidate;
			}
		}

		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( nebula.m_xCoordinate, 0.0f, nebula.m_yCoordinate ) );

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 10 );

		var inHyperspaceNebula = playerData.m_general.m_location == PD_General.Location.Hyperspace;
		var upInHyperspaceNebula = PressRaiseShields();

		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );

		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 5 );

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 10 );

		var upInClearHyperspace = PressRaiseShields();

		ship.DropShields();

		controller.m_encounterRange = encounterRange;

		Log( "nebula: star " + clearStarId + " (no nebula): shields up " + upInClearSystem + ", still up " + stillUpInClearSystem + " | star " + nebulaStarId + " (inside a nebula): up after coming in " + upAfterComingIn + ", raised there " + upInNebulaSystem + " (" + collapseText + ") | hyperspace inside nebula " + nebula.m_id + " (in hyperspace " + inHyperspaceNebula + "): raised " + upInHyperspaceNebula + " | hyperspace outside: raised " + upInClearHyperspace );

		Check( "nebula: outside the nebulae the shields come up and stay up (control)", upInClearSystem && stillUpInClearSystem && upInClearHyperspace, upInClearSystem + ", " + stillUpInClearSystem + ", " + upInClearHyperspace );
		Check( "nebula: shields that are up collapse when the ship comes into a star system inside a nebula", !upAfterComingIn && collapseText.Contains( "nebula" ), upAfterComingIn + " | " + collapseText );
		Check( "nebula: inside a nebula the shields cannot be raised, in a star system or in hyperspace", !upInNebulaSystem && inHyperspaceNebula && !upInHyperspaceNebula, upInNebulaSystem + ", " + inHyperspaceNebula + ", " + upInHyperspaceNebula );

		Finish( "scenario=nebula clear=" + upInClearSystem + " afterComingIn=" + upAfterComingIn + " nebulaSystem=" + upInNebulaSystem + " nebulaHyperspace=" + upInHyperspaceNebula + " clearHyperspace=" + upInClearHyperspace, 0 );
	}

	// ---------------------------------------------------------------- phase 1: repair at the Starport

	// presses Repair in ship configuration - returns the balance it took and the message it shows
	static int PressRepair( ShipConfigurationPanel panel, string[] message )
	{
		var bank = DataController.m_instance.m_playerData.m_bank;
		var balanceBefore = bank.m_currentBalance;

		panel.RepairClicked();

		message[ 0 ] = panel.m_errorMessageText.text;

		// back to the menu, as the player's next key would
		if ( Equals( GetField( panel, "m_currentState" ).ToString(), "ErrorMessage" ) )
		{
			Call( panel, "SwitchToMenuBarState" );
		}

		return balanceBefore - bank.m_currentBalance;
	}

	IEnumerator ScenarioStarportRepair()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;
		var bank = playerData.m_bank;
		var panel = FindPanel<ShipConfigurationPanel>();
		var message = new string[ 1 ];

		bank.m_currentBalance = 100000;
		ship.m_shieldingClass = 1;

		yield return OpenPanel( panel, "ship configuration" );

		// ---- 1. the control: a whole ship needs no repairs and costs nothing
		ship.m_armorClass = 1;
		ship.m_armorPoints = ship.GetMaximumArmorPoints();
		ship.m_shieldPoints = ship.GetMaximumShieldPoints();

		var costWhole = PressRepair( panel, message );
		var messageWhole = message[ 0 ];

		// ---- 2. class 1 armor (500 points for 1500 M.U.) with 200 points left and the shields empty: 300 points at 3 M.U., the shields for nothing
		ship.m_armorPoints = 200;
		ship.m_shieldPoints = 0;

		var costClass1 = PressRepair( panel, message );
		var messageClass1 = message[ 0 ];
		var armorAfterClass1 = ship.m_armorPoints;
		var shieldsAfterClass1 = ship.m_shieldPoints;

		// ---- 3. a bare hull (250 points) with 100 left: 150 points at the price of class 1
		ship.m_armorClass = 0;
		ship.m_armorPoints = 100;

		var costBareHull = PressRepair( panel, message );
		var armorAfterBareHull = ship.m_armorPoints;

		// ---- 4. not enough money: refused, nothing changes
		ship.m_armorPoints = 100;
		bank.m_currentBalance = 10;

		var costTooPoor = PressRepair( panel, message );
		var messageTooPoor = message[ 0 ];
		var armorTooPoor = ship.m_armorPoints;

		bank.m_currentBalance = 100000;

		var ledgerBefore = bank.m_transactionList.Count;

		yield return ClosePanel( panel, "ship configuration" );

		var ledgerEntry = ( bank.m_transactionList.Count > ledgerBefore ) ? bank.m_transactionList[ bank.m_transactionList.Count - 1 ].m_amount : "nothing";

		Log( "starport-repair: a whole ship: " + costWhole + " (" + messageWhole + ") | class 1 armor at 200 of 500, shields empty: " + costClass1 + " M.U. (" + messageClass1 + "), armor " + armorAfterClass1 + ", shields " + shieldsAfterClass1 + " of " + ship.GetMaximumShieldPoints() + " | a bare hull at 100 of 250: " + costBareHull + " M.U., armor " + armorAfterBareHull + " | with 10 M.U.: " + costTooPoor + " (" + messageTooPoor + "), armor " + armorTooPoor + " | the ledger on closing: " + ledgerEntry );

		Check( "starport-repair: a whole ship needs no repairs and pays nothing", ( costWhole == 0 ) && messageWhole.Contains( "no repairs" ), costWhole + " | " + messageWhole );
		Check( "starport-repair: the missing armor is paid at the price of the ship's armor class, and the ship is whole again with full shields", ( costClass1 == 900 ) && ( armorAfterClass1 == 500 ) && ( shieldsAfterClass1 == ship.GetMaximumShieldPoints() ), costClass1 + ", " + armorAfterClass1 + ", " + shieldsAfterClass1 );
		Check( "starport-repair: a bare hull is repaired at the price of class 1 armor", ( costBareHull == 450 ) && ( armorAfterBareHull == 250 ), costBareHull + ", " + armorAfterBareHull );
		Check( "starport-repair: without the money the repair is refused and nothing changes", ( costTooPoor == 0 ) && ( armorTooPoor == 100 ) && messageTooPoor.Contains( "insufficient funds" ), costTooPoor + ", " + armorTooPoor + " | " + messageTooPoor );

		Finish( "scenario=starport-repair whole=" + costWhole + " class1=" + costClass1 + " bareHull=" + costBareHull + " tooPoor=" + costTooPoor, 0 );
	}

	// ---------------------------------------------------------------- phase 1: distress

	// enters a star system, sends a distress call - returns what it cost, where the ship is then, and the last supplemental evaluation (or "")
	static IEnumerator DistressFrom( int starId, int[] fine, string[] where, string[] evaluation )
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		EnterStarSystem( starId );

		var end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 5 );

		var balanceBefore = playerData.m_bank.m_currentBalance;

		controller.m_messages.Clear();

		new DistressButton().Execute();

		// (the call is confirmed first, as the original asks: TRANSMIT EMERGENCY DISTRESS CALL / CONFIRM)
		PressDistressChoice( 1 );

		yield return Frames( 5 );

		fine[ 0 ] = balanceBefore - playerData.m_bank.m_currentBalance;
		where[ 0 ] = playerData.m_general.m_location + " at star " + playerData.m_general.m_currentStarId;

		var evaluations = FieldOrNull( playerData.m_general, "m_supplementalEvaluationList" ) as IList;

		evaluation[ 0 ] = ( ( evaluations == null ) || ( evaluations.Count == 0 ) ) ? "" : ( evaluations[ evaluations.Count - 1 ] as string );
	}

	IEnumerator ScenarioDistress()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var arthStarId = gameData.m_misc.m_arthStarId;
		var arthStar = gameData.m_starList[ arthStarId ];
		var fine = new int[ 1 ];
		var where = new string[ 1 ];
		var evaluation = new string[ 1 ];

		EnsureCrew();

		playerData.m_bank.m_currentBalance = 1000000;

		var encounterRange = SpaceflightController.m_instance.m_encounterRange;

		SpaceflightController.m_instance.m_encounterRange = 0.0f;

		// the star farthest from Arth (one that flared before the game began would be safer, but the farthest is what sets the largest fine)
		var farthestStarId = arthStarId;
		var farthest = 0.0f;

		for ( var starId = 0; starId < gameData.m_starList.Length; starId++ )
		{
			var star = gameData.m_starList[ starId ];
			var distance = Vector2.Distance( new Vector2( star.m_xCoordinate, star.m_yCoordinate ), new Vector2( arthStar.m_xCoordinate, arthStar.m_yCoordinate ) );

			if ( distance > farthest )
			{
				farthest = distance;
				farthestStarId = starId;
			}
		}

		// ---- 1. a distress call from the star farthest from Arth: towed home, fined the most, with Interstel's evaluation
		var ledgerBefore = playerData.m_bank.m_transactionList.Count;

		yield return DistressFrom( farthestStarId, fine, where, evaluation );

		var fineFar = fine[ 0 ];
		var whereFar = where[ 0 ];
		var evaluationFar = evaluation[ 0 ];
		var ledgerFar = ( playerData.m_bank.m_transactionList.Count > ledgerBefore ) ? playerData.m_bank.m_transactionList[ playerData.m_bank.m_transactionList.Count - 1 ].m_description + " " + playerData.m_bank.m_transactionList[ playerData.m_bank.m_transactionList.Count - 1 ].m_amount : "nothing";

		// ---- 2. one from Arth's own star system: the smallest fine
		yield return DistressFrom( arthStarId, fine, where, evaluation );

		var fineNear = fine[ 0 ];

		// ---- 3. the control: after the Starport has been destroyed there is no response, and no tow
		SetFieldIfPresent( playerData.m_general, "m_starportDestroyed", true );

		yield return DistressFrom( StarThatNeverFlares(), fine, where, evaluation );

		var fineAfterStarport = fine[ 0 ];
		var whereAfterStarport = where[ 0 ];
		var textAfterStarport = MessageList();

		SetFieldIfPresent( playerData.m_general, "m_starportDestroyed", false );

		SpaceflightController.m_instance.m_encounterRange = encounterRange;

		Log( "distress: from star " + farthestStarId + " (" + farthest.ToString( "0" ) + " from Arth): fined " + fineFar + ", now " + whereFar + ", ledger '" + ledgerFar + "' | from Arth's system: fined " + fineNear + " | after the Starport: fined " + fineAfterStarport + ", now " + whereAfterStarport + " (" + textAfterStarport + ") | the evaluation: " + evaluationFar.Replace( '\n', '/' ) );

		Check( "distress: a distress call gets the ship towed back to the Starport's docking bay", whereFar.StartsWith( "DockingBay" ) && whereFar.EndsWith( "star " + arthStarId ), whereFar );
		Check( "distress: the fine runs from 15000 MU in Arth's system to 80000 MU from the farthest star, in the ledger and in Interstel's evaluation", ( fineNear == 15000 ) && ( fineFar == 80000 ) && ledgerFar.StartsWith( "Towing charges" ) && evaluationFar.Contains( "TOWING CHARGES" ) && evaluationFar.Contains( fineFar.ToString() ), fineNear + ", " + fineFar + ", '" + ledgerFar + "'" );
		Check( "distress: after the Starport has been destroyed there is no response, no fine and no tow (control)", ( fineAfterStarport == 0 ) && !whereAfterStarport.StartsWith( "DockingBay" ) && textAfterStarport.Contains( "NO RESPONSE" ), fineAfterStarport + ", " + whereAfterStarport );

		Finish( "scenario=distress far=" + fineFar + " near=" + fineNear + " afterStarport=" + fineAfterStarport, 0 );
	}

	// ---------------------------------------------------------------- phase 1: alien vessels with their own armor, shields and speed

	// enters an encounter of a race and returns how far its first ship travels per second of game time over some seconds of game time, the name and move delay of
	// that ship's vessel, and whether its ships have the armor and shield points of their vessels in the game data
	// each ship's vessel is picked at random from the encounter's vessels, and the vessels of one race do not all have the same move delay (the Elowan warship has
	// 2700, the transport and the scout 2200), so the speed has to be compared with the move delay of the ship that was watched
	// game time and not real time: the coroutine runs after the encounter's Update of the same frame, so the ship has moved by this frame's Time.deltaTime, which
	// Unity caps on a slow frame (on such a frame real time runs ahead of the ship)
	static IEnumerator AlienShipTravel( GameData.Race race, float seconds, float[] perSecond, int[] moveDelay, string[] vesselName, string[] points )
	{
		var gameData = DataController.m_instance.m_gameData;
		var encounter = SpaceflightController.m_instance.m_encounter;
		var encounterId = FindEncounter( 1, 6, 3, 0, race );

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		var ships = encounter.m_pdEncounter.GetAlienShipList();

		points[ 0 ] = "";

		foreach ( var ship in ships )
		{
			var vessel = gameData.m_vesselList[ ship.m_vesselId ];

			points[ 0 ] += vessel.m_name + " " + ship.m_armorPoints + "/" + vessel.m_armor + " " + ship.m_shieldPoints + "/" + vessel.m_shields + "; ";
		}

		var watched = ships[ 0 ];

		moveDelay[ 0 ] = gameData.m_vesselList[ watched.m_vesselId ].m_moveDelay;
		vesselName[ 0 ] = gameData.m_vesselList[ watched.m_vesselId ].m_name;

		var last = watched.m_coordinates;
		var travelled = 0.0f;
		var elapsed = 0.0f;

		// a limit in real time in case game time stands still
		var giveUp = Time.realtimeSinceStartup + seconds * 10.0f;

		while ( ( elapsed < seconds ) && ( Time.realtimeSinceStartup < giveUp ) )
		{
			yield return null;

			travelled += Vector3.Distance( last, watched.m_coordinates );
			elapsed += Time.deltaTime;
			last = watched.m_coordinates;
		}

		perSecond[ 0 ] = ( elapsed > 0.0f ) ? ( travelled / elapsed ) : 0.0f;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );
	}

	IEnumerator ScenarioVesselStats()
	{
		var perSecond = new float[ 1 ];
		var moveDelay = new int[ 1 ];
		var vesselName = new string[ 1 ];
		var points = new string[ 1 ];

		EnsureCrew();

		yield return AlienShipTravel( GameData.Race.Spemin, 3.0f, perSecond, moveDelay, vesselName, points );

		var speminSpeed = perSecond[ 0 ];
		var speminDelay = moveDelay[ 0 ];
		var speminVessel = vesselName[ 0 ];
		var speminPoints = points[ 0 ];

		yield return AlienShipTravel( GameData.Race.Elowan, 3.0f, perSecond, moveDelay, vesselName, points );

		var elowanSpeed = perSecond[ 0 ];
		var elowanDelay = moveDelay[ 0 ];
		var elowanVessel = vesselName[ 0 ];
		var elowanPoints = points[ 0 ];

		var ratio = ( elowanSpeed > 0.0f ) ? ( speminSpeed / elowanSpeed ) : -1.0f;

		// the shorter the move delay the faster: the speeds go as the inverse of the delays (2200 / 1500 = 1.47 for an Elowan transport or scout, 2700 / 1500 = 1.80
		// for an Elowan warship)
		var expected = ( speminDelay > 0 ) ? ( (float) elowanDelay / speminDelay ) : -1.0f;

		// every ship has the points of its vessel: "armor/vessel's armor shields/vessel's shields" with the two halves equal
		var allMatch = true;

		foreach ( var entry in ( speminPoints + elowanPoints ).Split( new[] { "; " }, StringSplitOptions.RemoveEmptyEntries ) )
		{
			var parts = entry.Split( ' ' );

			for ( var part = 0; part < parts.Length; part++ )
			{
				if ( parts[ part ].Contains( "/" ) )
				{
					var halves = parts[ part ].Split( '/' );

					allMatch = allMatch && ( halves[ 0 ] == halves[ 1 ] );
				}
			}
		}

		Log( "vesselstats: spemin ships " + speminPoints + "| elowan ships " + elowanPoints + "| travel per second of game time: " + speminVessel + " (move delay " + speminDelay + ") " + speminSpeed.ToString( "0.0" ) + ", " + elowanVessel + " (move delay " + elowanDelay + ") " + elowanSpeed.ToString( "0.0" ) + ", ratio " + ratio.ToString( "0.000" ) + " (expected " + expected.ToString( "0.000" ) + ")" );

		Check( "vesselstats: every alien ship has the armor and shield points of its vessel in the game data", allMatch, speminPoints + elowanPoints );
		Check( "vesselstats: an alien ship moves at the speed of its vessel's move delay (a Spemin ship is as many times as fast as an Elowan one as the Elowan vessel's move delay is longer)", ( expected > 1.0f ) && ( Mathf.Abs( ratio - expected ) < 0.02f * expected ), ratio.ToString( "0.000" ) + " (expected " + expected.ToString( "0.000" ) + ", " + speminVessel + " " + speminDelay + ", " + elowanVessel + " " + elowanDelay + ")" );

		Finish( "scenario=vesselstats ratio=" + ratio.ToString( "0.000" ) + " expected=" + expected.ToString( "0.000" ) + " elowan=" + elowanDelay + " points=" + allMatch, 0 );
	}

	// ---------------------------------------------------------------- phase 1: the starmap's fuel estimate

	// moves the starmap's cursor to these starmap coordinates, as scrolling does, and returns the viewport label
	static string StarmapLabelAt( float x, float y )
	{
		var starmap = SpaceflightController.m_instance.m_starmap;
		var scale = starmap.m_starmap.transform.localScale;

		SetField( starmap, "m_positionOffset", new Vector3( x * scale.x, y * scale.y, 0.0f ) );

		// the label is made again as the cursor moves (the code before the estimate has no such method: then the label is what it was)
		var method = typeof( Starmap ).GetMethod( "UpdateLabel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public );

		if ( method != null )
		{
			method.Invoke( starmap, null );
		}

		return SpaceflightController.m_instance.m_viewport.m_label.text;
	}

	IEnumerator ScenarioStarmapFuel()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var tesseractId = gameData.FindArtifactId( "Tesseract" );

		EnsureCrew();

		// class 1 engines (0.48 cubic meters of Endurium for every coordinate), no Tesseract
		ship.m_enginesClass = 1;

		for ( var guard = 0; ( guard < 10 ) && ( ship.m_artifactStorage.Find( tesseractId ) != null ); guard++ )
		{
			ship.RemoveArtifact( tesseractId );
		}

		// in hyperspace at 40, 40 (a corner with no star), with no encounter able to begin
		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 40.0f, 0.0f, 40.0f ) );

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 10 );

		// ---- 1. the starmap opens with the cursor on the ship: no fuel to get there
		controller.m_starmap.Show();

		yield return Frames( 2 );

		var labelOnShip = controller.m_viewport.m_label.text;

		// ---- 2. the cursor 20 coordinates away: 20 x 0.48 = 9.6 cubic meters
		var labelAt20 = StarmapLabelAt( 60.0f, 40.0f );

		// ---- 3. with the Tesseract, half: 4.8
		ship.AddArtifact( tesseractId );

		var labelWithTesseract = StarmapLabelAt( 60.0f, 40.0f );

		controller.m_starmap.Hide();

		controller.m_encounterRange = encounterRange;

		Log( "starmapfuel: the label on opening '" + labelOnShip + "', at 60, 40 (20 from the ship) '" + labelAt20 + "', with the Tesseract '" + labelWithTesseract + "'" );

		Check( "starmapfuel: the starmap opens with the cursor on the ship and no fuel needed to get there", labelOnShip.Contains( "40   40" ) && labelOnShip.Contains( "Fuel 0.0" ), labelOnShip );
		Check( "starmapfuel: the starmap shows the Endurium the engines need to fly to the cursor (20 coordinates at 0.48 = 9.6)", labelAt20.Contains( "60   40" ) && labelAt20.Contains( "Fuel 9.6" ), labelAt20 );
		Check( "starmapfuel: with the Tesseract the estimate is half (4.8)", labelWithTesseract.Contains( "Fuel 4.8" ), labelWithTesseract );

		Finish( "scenario=starmapfuel onShip='" + labelOnShip + "' at20='" + labelAt20 + "' tesseract='" + labelWithTesseract + "'", 0 );
	}

	// ---------------------------------------------------------------- phase 1: colony recommendations

	// goes into orbit around a planet, logs it with the real Log Planet button and answers the question - returns whether the question was asked
	static IEnumerator LogAndRecommend( int planetId, bool recommend, bool[] asked )
	{
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;

		yield return GoIntoOrbit( gameData.m_planetList[ planetId ].m_starId, planetId );

		new LogPlanetButton().Execute();

		yield return Frames( 2 );

		var recommendSet = -1;

		try
		{
			recommendSet = (int) Enum.Parse( typeof( ButtonController.ButtonSet ), "RecommendPlanet" );
		}
		catch ( Exception )
		{
		}

		asked[ 0 ] = ( recommendSet >= 0 ) && ( (int) controller.m_buttonController.GetCurrentButtonSet() == recommendSet ) && MessageList().Contains( "Recommend this planet" );

		if ( asked[ 0 ] )
		{
			// No is the first button, Yes the second (as the original's "<- N  Y ->")
			PressButton( (ButtonController.ButtonSet) recommendSet, recommend ? 1 : 0 );

			yield return Frames( 2 );
		}
	}

	IEnumerator ScenarioColonies()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var bank = playerData.m_bank;
		var asked = new bool[ 1 ];

		EnsureCrew();

		bank.m_currentBalance = 100000;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		// Mardan 4, an optimal planet of Interstel's list (50000 MU, and no encounter waits in orbit there - Heaven's Mechans would begin theirs), and two planets of
		// the Uhlek system that are not on it
		var heaven = gameData.FindPlanetFromSun( 217, 88, 4 );
		var unsuitableA = gameData.FindPlanetFromSun( 55, 32, 2 );
		var unsuitableB = gameData.FindPlanetFromSun( 55, 32, 1 );
		var notRecommended = gameData.FindPlanetFromSun( 55, 32, 3 );
		var onTheList = "";

		foreach ( var colonyEvaluation in RecoveredList( "m_colonyEvaluationList" ) )
		{
			var planetId = IntFieldOrMin( colonyEvaluation, "m_planetId" );

			if ( ( planetId == unsuitableA ) || ( planetId == unsuitableB ) || ( planetId == notRecommended ) )
			{
				onTheList += planetId + " ";
			}
		}

		// ---- 1. Log Planet asks whether to recommend the planet, and Yes recommends it
		yield return LogAndRecommend( heaven, true, asked );

		var askedForHeaven = asked[ 0 ];

		yield return LogAndRecommend( unsuitableA, true, asked );
		yield return LogAndRecommend( unsuitableB, true, asked );

		// ---- 2. the control: No does not recommend it
		yield return LogAndRecommend( notRecommended, false, asked );

		var pending = FieldOrNull( playerData.m_general, "m_colonyRecommendationList" ) as IList;
		var pendingCount = ( pending == null ) ? -1 : pending.Count;

		// ---- 3. back at the Starport, Interstel evaluates them
		var balanceBefore = bank.m_currentBalance;
		var ledgerBefore = bank.m_transactionList.Count;

		playerData.m_general.m_currentStarId = gameData.m_misc.m_arthStarId;
		playerData.m_general.m_currentPlanetId = gameData.m_misc.m_arthPlanetId;

		controller.SwitchLocation( PD_General.Location.DockingBay );

		yield return Frames( 10 );

		controller.m_encounterRange = encounterRange;

		var change = bank.m_currentBalance - balanceBefore;
		var ledger = "";

		for ( var index = ledgerBefore; index < bank.m_transactionList.Count; index++ )
		{
			ledger += bank.m_transactionList[ index ].m_description + " " + bank.m_transactionList[ index ].m_amount + "; ";
		}

		var evaluations = FieldOrNull( playerData.m_general, "m_supplementalEvaluationList" ) as IList;
		var evaluationText = "";

		if ( evaluations != null )
		{
			foreach ( var evaluation in evaluations )
			{
				evaluationText += evaluation + " || ";
			}
		}

		var pendingAfter = ( pending == null ) ? -1 : pending.Count;
		var reasonsA = string.Join( ", ", gameData.m_planetList[ unsuitableA ].GetType().GetMethod( "GetUnsuitableReasons" ) == null ? new List<string>() : (List<string>) gameData.m_planetList[ unsuitableA ].GetType().GetMethod( "GetUnsuitableReasons" ).Invoke( gameData.m_planetList[ unsuitableA ], null ) );
		var reasonsB = string.Join( ", ", gameData.m_planetList[ unsuitableB ].GetType().GetMethod( "GetUnsuitableReasons" ) == null ? new List<string>() : (List<string>) gameData.m_planetList[ unsuitableB ].GetType().GetMethod( "GetUnsuitableReasons" ).Invoke( gameData.m_planetList[ unsuitableB ], null ) );
		var reasonCountB = ( reasonsB.Length == 0 ) ? 1 : reasonsB.Split( ',' ).Length;

		Log( "colonies: planets " + heaven + " (Mardan 4), " + unsuitableA + " and " + unsuitableB + " recommended, " + notRecommended + " not (on the list among them: '" + onTheList + "') | asked for Mardan 4 " + askedForHeaven + ", pending before docking " + pendingCount + ", after " + pendingAfter + " | the bank " + change + ", ledger: " + ledger + " | reasons " + unsuitableA + ": " + reasonsA + "; " + unsuitableB + ": " + reasonsB + " | evaluations: " + evaluationText.Replace( '\n', '/' ) );

		Check( "colonies: Log Planet asks whether to recommend the planet for colonization, and Yes recommends it while No does not", askedForHeaven && ( pendingCount == 3 ), askedForHeaven + ", " + pendingCount );
		Check( "colonies: back at the Starport an optimal planet of Interstel's list earns its bonus", ledger.Contains( "Colony bonus 50000+" ) && evaluationText.Contains( "OPTIMAL PLANET" ) && evaluationText.Contains( "BONUS OF 50000" ), ledger );
		Check( "colonies: the first unsuitable planet is a warning of 100 MU, with its reasons", ledger.Contains( "Colony fine 100-" ) && evaluationText.Contains( "FINED 100 MU'S.  THIS IS ONLY A WARNING" ) && ( reasonsA.Length > 0 ) && evaluationText.Contains( reasonsA.Split( ',' )[ 0 ].Trim() ), ledger + " | " + reasonsA );
		Check( "colonies: a later unsuitable planet is fined heavily, 5000 MU for each reason, with no warning", ledger.Contains( "Colony fine " + ( 5000 * reasonCountB ) + "-" ) && ( change == 50000 - 100 - 5000 * reasonCountB ) && ( pendingAfter == 0 ), ledger + " | " + change + " | pending " + pendingAfter );

		Finish( "scenario=colonies asked=" + askedForHeaven + " pending=" + pendingCount + " change=" + change + " pendingAfter=" + pendingAfter, 0 );
	}

	// ---------------------------------------------------------------- phase 1: damage to the ship's systems

	// the health of each system the code knows (none on the code before the systems could be damaged) - "Engines 100, Sensors 100, ..."
	static int[] SystemHealth()
	{
		var field = typeof( PD_PlayerShip ).GetField( "m_systemHealth" );
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		// (a getter makes sure the list is there)
		var getter = typeof( PD_PlayerShip ).GetMethod( "AllSystemsAreWhole" );

		if ( getter != null )
		{
			getter.Invoke( ship, null );
		}

		return ( field == null ) ? new int[ 0 ] : ( (int[]) field.GetValue( ship ) ).Clone() as int[];
	}

	IEnumerator ScenarioSystemDamage()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;

		EnsureCrew();

		// a bare hull (250 points) with shields and lasers but no missile launcher, the shields down
		ship.m_armorClass = 0;
		ship.m_shieldingClass = 1;
		ship.m_laserCannonClass = 1;
		ship.m_missileLauncherClass = 0;
		ship.m_shieldsAreUp = false;
		ship.m_armorPoints = ship.GetMaximumArmorPoints();

		var whole = SystemHealth();

		// ---- 1. one hit of 50 (a fifth of the hull): the hull loses 50 and one system a fifth of itself
		combat.ApplyDamageToPlayer( 50, Vector3.forward );

		var afterOne = SystemHealth();
		var dropped = 0;
		var totalDrop = 0;

		for ( var system = 0; ( system < afterOne.Length ) && ( system < whole.Length ); system++ )
		{
			if ( afterOne[ system ] < whole[ system ] )
			{
				dropped++;
				totalDrop += whole[ system ] - afterOne[ system ];
			}
		}

		// ---- 2. many small hits: only the systems the ship has are damaged (the missiles, index 4, stay whole)
		for ( var hit = 0; hit < 40; hit++ )
		{
			ship.m_armorPoints = ship.GetMaximumArmorPoints();

			combat.ApplyDamageToPlayer( 5, Vector3.forward );
		}

		var afterMany = SystemHealth();
		var missilesWhole = ( afterMany.Length > 4 ) && ( afterMany[ 4 ] == 100 );
		var othersHit = 0;

		for ( var system = 0; system < afterMany.Length; system++ )
		{
			if ( ( system != 4 ) && ( afterMany[ system ] < 100 ) )
			{
				othersHit++;
			}
		}

		// ---- 3. the damage report lists the systems
		SpaceflightController.m_instance.m_messages.Clear();

		new DamageButton().Execute();

		var report = MessageList();

		// ---- 4. the systems' health is saved
		var roundTrip = JsonUtility.FromJson<PD_PlayerShip>( JsonUtility.ToJson( ship ) );
		var savedField = typeof( PD_PlayerShip ).GetField( "m_systemHealth" );
		var saved = ( savedField == null ) ? null : (int[]) savedField.GetValue( roundTrip );
		var survives = ( saved != null ) && ( saved.Length == afterMany.Length ) && ( string.Join( ",", saved ) == string.Join( ",", afterMany ) );

		ship.m_armorPoints = ship.GetMaximumArmorPoints();

		yield return Frames( 2 );

		Log( "systemdamage: whole " + string.Join( ",", whole ) + " | after a hit of 50 on a hull of 250: " + string.Join( ",", afterOne ) + " (" + dropped + " system lost " + totalDrop + ") | after 40 hits of 5: " + string.Join( ",", afterMany ) + " | saved and loaded " + survives + " | " + report.Replace( '\n', '/' ) );

		Check( "systemdamage: a new ship's systems are whole", ( whole.Length == 6 ) && ( string.Join( ",", whole ) == "100,100,100,100,100,100" ), string.Join( ",", whole ) );
		Check( "systemdamage: a hit that gets through the shields also damages one system, by the same share as the hull (50 of 250: 20)", ( dropped == 1 ) && ( totalDrop == 20 ), dropped + " systems, " + totalDrop );
		Check( "systemdamage: only the systems the ship has are damaged (no missile launcher: the missiles stay whole)", missilesWhole && ( othersHit >= 3 ), string.Join( ",", afterMany ) );
		Check( "systemdamage: the damage report lists the systems, and their health is saved", report.Contains( "Engines:" ) && report.Contains( "Laser cannon:" ) && !report.Contains( "Missile launcher:" ) && survives, survives + " | " + report.Replace( '\n', '/' ) );

		Finish( "scenario=systemdamage afterOne=" + string.Join( ",", afterOne ) + " afterMany=" + string.Join( ",", afterMany ), 0 );
	}

	// ---------------------------------------------------------------- phase 1: what a damaged system does

	// sets the health of a system (by its index in PD_PlayerShip.ShipSystem: 0 engines, 1 sensors, 2 comm, 3 shields, 4 missiles, 5 lasers) - nothing on the code
	// before the systems could be damaged
	static void SetSystemHealth( int system, int health )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var field = typeof( PD_PlayerShip ).GetField( "m_systemHealth" );

		if ( field == null )
		{
			return;
		}

		var values = field.GetValue( ship ) as int[];

		if ( ( values == null ) || ( values.Length < 6 ) )
		{
			values = new[] { 100, 100, 100, 100, 100, 100 };
		}

		values[ system ] = health;

		field.SetValue( ship, values );
	}

	IEnumerator ScenarioSystemEffects()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var gameData = dataController.m_gameData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;
		var combat = CombatController.m_instance;

		EnsureCrew();

		ship.m_shieldingClass = 1;
		ship.m_laserCannonClass = 1;
		ship.AddElement( 5, 200 );

		// ---- 1. the shield generator: its maximum charge in proportion to its health
		SetSystemHealth( 3, 100 );

		var shieldsWhole = ship.GetMaximumShieldPoints();

		SetSystemHealth( 3, 50 );

		var shieldsHalf = ship.GetMaximumShieldPoints();

		SetSystemHealth( 3, 100 );

		// ---- 2. the engines: the top speed in proportion to their health (in hyperspace, with no encounter able to begin)
		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 4.0f, 0.0f, 4.0f ) );

		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 5 );

		controller.m_playerShip.TurnOnEngines();

		SetSystemHealth( 0, 100 );

		yield return Frames( 3 );

		var speedWhole = playerData.m_general.m_currentMaximumSpeed;

		SetSystemHealth( 0, 50 );

		yield return Frames( 3 );

		var speedHalf = playerData.m_general.m_currentMaximumSpeed;

		controller.m_playerShip.TurnOffEngines();

		SetSystemHealth( 0, 100 );

		// ---- 3. the laser cannon: the damage of a shot in proportion to its health, and no shot once it is destroyed
		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var alien = playerData.FindEncounter( speminId ).GetAlienShipList()[ 0 ];
		var laserHits = new int[ 2 ];

		for ( var pass = 0; pass < 2; pass++ )
		{
			SetSystemHealth( 5, ( pass == 0 ) ? 100 : 50 );

			alien.m_armorPoints = 100000;
			alien.m_shieldPoints = 0;

			combat.SetTarget( FirstLivingAlien() );

			BringAliensClose();

			SetField( combat, "m_playerLaserCooldown", 0.0f );

			combat.FirePlayerLaser();

			laserHits[ pass ] = 100000 - alien.m_armorPoints;
		}

		SetSystemHealth( 5, 0 );

		SetField( combat, "m_playerLaserCooldown", 0.0f );

		var firesDestroyed = combat.CanFireLaser();

		SetSystemHealth( 5, 100 );

		// ---- 4. the comm: a statement of the Spemin with an officer of full skill, the comm whole and destroyed
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;

		GD_Comm speminComm = null;

		foreach ( var comm in gameData.m_commList )
		{
			if ( ( speminComm == null ) && ( comm.m_race == GameData.Race.Spemin ) && ( comm.m_subject == GD_Comm.Subject.Statement ) && ( comm.m_text.IndexOfAny( new[] { '&', '*', '/', '(', ')' } ) < 0 ) )
			{
				speminComm = comm;
			}
		}

		SetSystemHealth( 2, 100 );

		var commWhole = AlienCommShown( speminComm );

		SetSystemHealth( 2, 0 );

		var commDestroyed = AlienCommShown( speminComm );

		SetSystemHealth( 2, 100 );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 5. the sensors: no readings below a quarter of their health (in a star system)
		EnterStarSystem( StarThatNeverFlares() );

		var end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 5 );

		SetSystemHealth( 1, 100 );
		controller.m_messages.Clear();

		new SensorsButton().Execute();

		var sensorsWhole = MessageList();

		SetSystemHealth( 1, 20 );
		controller.m_messages.Clear();

		new SensorsButton().Execute();

		var sensorsBroken = MessageList();

		SetSystemHealth( 1, 100 );

		controller.m_encounterRange = encounterRange;

		Log( "systemeffects: maximum shields " + shieldsWhole + " whole, " + shieldsHalf + " at half | top speed " + speedWhole + " whole, " + speedHalf + " at half | laser hit " + laserHits[ 0 ] + " whole, " + laserHits[ 1 ] + " at half, can fire when destroyed " + firesDestroyed + " | comm whole: " + commWhole + " | destroyed: " + commDestroyed + " | sensors whole: " + sensorsWhole + " | at 20: " + sensorsBroken );

		Check( "systemeffects: a damaged shield generator holds less charge (half of it at half its health)", ( shieldsWhole > 0 ) && ( shieldsHalf == shieldsWhole / 2 ), shieldsWhole + " -> " + shieldsHalf );
		Check( "systemeffects: damaged engines are slower (half the top speed at half their health)", ( speedWhole > 0.0f ) && Mathf.Abs( speedHalf - speedWhole / 2.0f ) < 0.01f, speedWhole + " -> " + speedHalf );
		Check( "systemeffects: a damaged laser cannon does less damage, and a destroyed one does not fire", ( laserHits[ 0 ] > 0 ) && ( laserHits[ 1 ] == Mathf.RoundToInt( laserHits[ 0 ] / 2.0f ) ) && !firesDestroyed, laserHits[ 0 ] + " -> " + laserHits[ 1 ] + ", " + firesDestroyed );
		Check( "systemeffects: destroyed comm equipment garbles what a fully trained officer understands", commWhole.Contains( speminComm.m_text ) && !commDestroyed.Contains( speminComm.m_text ), commDestroyed );
		Check( "systemeffects: badly damaged sensors give no readings", sensorsWhole.Contains( "Star System Scan" ) && sensorsBroken.Contains( "too badly damaged" ), sensorsBroken );

		Finish( "scenario=systemeffects shields=" + shieldsWhole + "/" + shieldsHalf + " speed=" + speedWhole + "/" + speedHalf + " laser=" + laserHits[ 0 ] + "/" + laserHits[ 1 ], 0 );
	}

	// ---------------------------------------------------------------- phase 1: the engineer's repairs with minerals

	// how much of an element is in the ship's hold, in tenths of a cubic meter
	static int HeldElement( int elementId )
	{
		var reference = DataController.m_instance.m_playerData.m_playerShip.m_elementStorage.Find( elementId );

		return ( reference == null ) ? 0 : reference.m_volume;
	}

	IEnumerator ScenarioSystemRepairs()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;

		EnsureCrew();

		// class 2 armor at half (375 of 750), the engines at 30, the lasers at 70 - the engines are the worst
		ship.m_armorClass = 2;
		ship.m_armorPoints = 375;
		ship.m_laserCannonClass = 1;

		SetSystemHealth( 0, 30 );
		SetSystemHealth( 5, 70 );

		const int molybdenum = 11;
		const int aluminum = 0;

		// ---- 1. without the engines' mineral in the hold the engineer asks for it and stops
		controller.m_messages.Clear();

		new RepairButton().Execute();

		var withoutMineral = MessageList();
		var askedWithout = AnswerRepairQuestion( false );
		var underWayWithout = ship.m_repairsAreUnderWay;

		// ---- 2. with it in the hold he asks to take it, and no leaves it there
		ship.AddElement( molybdenum, 100 );

		controller.m_messages.Clear();

		new RepairButton().Execute();

		var question = MessageList();
		var askedNo = AnswerRepairQuestion( false );
		var afterNo = MessageList();
		var molybdenumAfterNo = HeldElement( molybdenum );

		// ---- 3. yes: 7 cubic meters for the 70 missing, and the repairs begin on the engines
		controller.m_messages.Clear();

		new RepairButton().Execute();

		var askedYes = AnswerRepairQuestion( true );
		var molybdenumAfterYes = HeldElement( molybdenum );
		var underWayAfterYes = ship.m_repairsAreUnderWay;
		var startText = MessageList();

		// ---- 4. once the engines are whole he goes on to the next worst (the hull at 50: 5 cubic meters of aluminum, which the hold lacks)
		controller.m_messages.Clear();

		ship.UpdateRepairs( 10000.0f );

		yield return Frames( 2 );

		var enginesAfter = SystemHealth().Length > 0 ? SystemHealth()[ 0 ] : -1;
		var nextText = MessageList();

		// ---- 5. the control: a whole ship needs no repairs
		ship.m_armorPoints = ship.GetMaximumArmorPoints();

		var restore = typeof( PD_PlayerShip ).GetMethod( "RestoreSystems" );

		if ( restore != null )
		{
			restore.Invoke( ship, null );
		}

		ship.m_repairsAreUnderWay = false;

		controller.m_messages.Clear();

		new RepairButton().Execute();

		var wholeText = MessageList();

		Log( "systemrepairs: without molybdenum: " + withoutMineral + " (asked " + askedWithout + ", under way " + underWayWithout + ") | with it: " + question + " -> no: " + afterNo + " (asked " + askedNo + ", molybdenum " + molybdenumAfterNo + ") | yes: molybdenum " + molybdenumAfterYes + ", under way " + underWayAfterYes + " (" + startText + ") | the engines after the repair " + enginesAfter + ": " + nextText + " | a whole ship: " + wholeText );

		Check( "systemrepairs: the engineer asks for the worst system's mineral and stops without it", withoutMineral.Contains( "7 cubic meters of Molybdenum" ) && withoutMineral.Contains( "Ceasing repair" ) && !askedWithout && !underWayWithout, withoutMineral );
		Check( "systemrepairs: with the mineral in the hold he asks to take it, and no leaves it there", askedNo && question.Contains( "Shall I take this from cargo?" ) && afterNo.Contains( "Ceasing repair" ) && ( molybdenumAfterNo == 100 ), askedNo + ", " + molybdenumAfterNo + " | " + afterNo );
		Check( "systemrepairs: yes takes the 7 cubic meters from the hold and the repairs begin on the engines", askedYes && ( molybdenumAfterYes == 30 ) && underWayAfterYes && startText.Contains( "engines" ), askedYes + ", " + molybdenumAfterYes + ", " + underWayAfterYes + " | " + startText );
		Check( "systemrepairs: once the engines are whole the engineer goes on to the next worst, the hull", ( enginesAfter == 100 ) && nextText.Contains( "completed" ) && nextText.Contains( "5 cubic meters of Aluminum" ), enginesAfter + " | " + nextText );
		Check( "systemrepairs: a whole ship needs no repairs", wholeText.Contains( "No repairs are required" ), wholeText );

		Finish( "scenario=systemrepairs molybdenum=" + molybdenumAfterNo + "/" + molybdenumAfterYes + " engines=" + enginesAfter, 0 );
	}

	// ---------------------------------------------------------------- phase 2: the home fleets' warnings

	// the id of the star at these coordinates (-1 if there is none)
	static int StarAt( int x, int y )
	{
		var gameData = DataController.m_instance.m_gameData;

		for ( var starId = 0; starId < gameData.m_starList.Length; starId++ )
		{
			if ( ( gameData.m_starList[ starId ].m_xCoordinate == x ) && ( gameData.m_starList[ starId ].m_yCoordinate == y ) )
			{
				return starId;
			}
		}

		return -1;
	}

	// the text of a comm of the game data ("missing" if there is none)
	static string CommText( int commId )
	{
		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( comm.m_id == commId )
			{
				return comm.m_text;
			}
		}

		return "missing";
	}

	// lets the aliens of the current encounter speak at once (their conversation timer runs out), a number of times, and adds what the messages show after each
	static IEnumerator LetAliensSpeak( int times, List<string> said )
	{
		var encounter = SpaceflightController.m_instance.m_encounter;

		for ( var i = 0; i < times; i++ )
		{
			SpaceflightController.m_instance.m_messages.Clear();

			if ( encounter.m_pdEncounter != null )
			{
				encounter.m_pdEncounter.m_conversationTimer = 0.0f;
			}

			yield return Frames( 3 );

			said.Add( MessageList() );
		}
	}

	IEnumerator ScenarioHomeworlds()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;

		EnsureCrew();

		// an officer who understands every word, and a ship that survives an attack
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;
		playerData.m_playerShip.m_armorPoints = 100000;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		// ---- 1. the Thrynn home fleet (encounter 165, orbit 4 of 129,33): the ship does not answer, and each hail is the next warning, then the attack
		var thrynnStar = StarAt( 129, 33 );

		yield return GoIntoOrbit( thrynnStar, PlanetInOrbit( thrynnStar, 4 ) );

		var thrynnBegun = ( playerData.m_general.m_location == PD_General.Location.Encounter ) && ( playerData.m_general.m_currentEncounterId == 165 );
		var thrynnSaid = new List<string>();

		yield return LetAliensSpeak( 3, thrynnSaid );

		var hostileBeforeLast = thrynnBegun && ( encounter.m_pdEncounter.m_alienStance == GD_Comm.Stance.Hostile );

		yield return LetAliensSpeak( 1, thrynnSaid );

		yield return Frames( 3 );

		var thrynnHostile = thrynnBegun && ( encounter.m_pdEncounter.m_alienStance == GD_Comm.Stance.Hostile ) && encounter.m_pdEncounter.m_disconnected;
		var thrynnWarnings = new[] { 386, 387, 388, 394 };
		var thrynnInOrder = thrynnSaid.Count == thrynnWarnings.Length;

		for ( var i = 0; thrynnInOrder && ( i < thrynnWarnings.Length ); i++ )
		{
			thrynnInOrder = thrynnSaid[ i ].Contains( CommText( thrynnWarnings[ i ] ) );
		}

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. the Veloxi home fleet (encounter 233, orbit 3 of 150,148): the ship hails first, and their one homeworld response is the last warning
		var veloxStar = StarAt( 150, 148 );

		yield return GoIntoOrbit( veloxStar, PlanetInOrbit( veloxStar, 3 ) );

		var veloxBegun = ( playerData.m_general.m_location == PD_General.Location.Encounter ) && ( playerData.m_general.m_currentEncounterId == 233 );
		var veloxSaid = new List<string>();

		if ( veloxBegun )
		{
			encounter.Hail( GD_Comm.Stance.Friendly, false );
		}

		yield return LetAliensSpeak( 1, veloxSaid );

		yield return Frames( 3 );

		var veloxResponse = ( veloxSaid.Count > 0 ) && veloxSaid[ 0 ].Contains( CommText( 483 ) );
		var veloxHostile = veloxBegun && ( encounter.m_pdEncounter.m_alienStance == GD_Comm.Stance.Hostile ) && !encounter.m_pdEncounter.m_connected;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. the control: a Thrynn patrol in their home system (encounter 157, met in flight) hails as any Thrynn ship does
		EnterEncounter( 157 );
		yield return Frames( 10 );

		var patrolSaid = new List<string>();

		yield return LetAliensSpeak( 1, patrolSaid );

		var patrolLine = ( patrolSaid.Count > 0 ) ? patrolSaid[ 0 ] : "";
		var patrolWarned = false;

		foreach ( var comm in dataController.m_gameData.m_commList )
		{
			patrolWarned = patrolWarned || ( comm.m_homeworld && patrolLine.Contains( comm.m_text ) );
		}

		var patrolHostile = encounter.m_pdEncounter.m_alienStance == GD_Comm.Stance.Hostile;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		controller.m_encounterRange = encounterRange;

		Log( "homeworlds: thrynn fleet begun " + thrynnBegun + ", said " + string.Join( " | ", thrynnSaid ) + " | hostile before the last " + hostileBeforeLast + ", after " + thrynnHostile + " || veloxi fleet begun " + veloxBegun + ", said " + string.Join( " | ", veloxSaid ) + " | hostile " + veloxHostile + " || thrynn patrol: " + patrolLine + " | hostile " + patrolHostile );

		Check( "homeworlds: the Thrynn home fleet gives its homeworld warnings one after the other while the ship does not answer", thrynnBegun && thrynnInOrder, string.Join( " | ", thrynnSaid ) );
		Check( "homeworlds: the Thrynn hold their fire until the last warning, then end the comm link and attack", thrynnBegun && !hostileBeforeLast && thrynnHostile, hostileBeforeLast + " -> " + thrynnHostile );
		Check( "homeworlds: the Veloxi home fleet answers a hail with its homeworld response and attacks", veloxBegun && veloxResponse && veloxHostile, veloxResponse + ", " + veloxHostile + " | " + string.Join( " | ", veloxSaid ) );
		Check( "homeworlds: a Thrynn patrol met in flight speaks no homeworld line and does not attack (control)", ( patrolLine.Contains( "Receiving" ) ) && !patrolWarned && !patrolHostile, patrolLine );

		Finish( "scenario=homeworlds thrynn=" + thrynnSaid.Count + " velox=" + veloxSaid.Count, 0 );
	}

	// ---------------------------------------------------------------- phase 2: the Spemin, the Thrynn and the Veloxi after their losses

	// the placeholders that Encounter.AddComm fills in before a line is said: the player's ship (&), the player's captain (*), the aliens' captain (%) and ship (+)
	static readonly char[] c_linePlaceholders = { '&', '*', '%', '+' };

	// true if the text holds this line of the game data as it is said - the pieces between its placeholders, in order, with anything in between
	static bool SaysLine( string text, string line )
	{
		var pieces = line.Split( c_linePlaceholders );
		var position = 0;

		foreach ( var piece in pieces )
		{
			// (two placeholders in a row, or one at an end, leave an empty piece)
			if ( piece.Length == 0 )
			{
				continue;
			}

			var found = text.IndexOf( piece, position, System.StringComparison.Ordinal );

			if ( found < 0 )
			{
				return false;
			}

			position = found + piece.Length;
		}

		return true;
	}

	// true if the text holds one of these comms of the game data
	static bool SaysOneOf( string text, params int[] commIds )
	{
		foreach ( var commId in commIds )
		{
			if ( SaysLine( text, CommText( commId ) ) )
			{
				return true;
			}
		}

		return false;
	}

	// meets three ships of a race in a star system, fires on them, destroys two and hails - adds the encounter, the aliens' stance and what they answer
	static IEnumerator BeatAndHail( GameData.Race race, List<string> results )
	{
		var playerData = DataController.m_instance.m_playerData;
		var encounter = SpaceflightController.m_instance.m_encounter;
		var encounterId = FindEncounter( 1, 6, 3, 0, race );

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		encounter.PlayerAttacked();

		Kill( FirstLivingAlien() );
		Kill( FirstLivingAlien() );

		yield return Frames( 3 );

		// (a hit in the fight may have damaged the comm, which garbles what they say)
		playerData.m_playerShip.RestoreSystems();

		encounter.Hail( GD_Comm.Stance.Friendly, false );

		var said = new List<string>();

		yield return LetAliensSpeak( 1, said );

		results.Add( encounterId + " " + encounter.m_pdEncounter.m_alienStance + " " + ( ( said.Count > 0 ) ? said[ 0 ] : "nothing said" ) );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );
	}

	IEnumerator ScenarioCowardice()
	{
		var playerData = DataController.m_instance.m_playerData;
		var encounter = SpaceflightController.m_instance.m_encounter;

		EnsureCrew();

		// an officer who understands every word, and a ship that survives the fight
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;
		playerData.m_playerShip.m_armorPoints = 100000;

		// three Spemin ships at once, in a star system
		var speminId = FindEncounter( 1, 6, 3, 0 );

		EnterEncounter( speminId );
		yield return Frames( 10 );

		var pdEncounter = encounter.m_pdEncounter;
		var joined = 0;

		foreach ( var alienShip in pdEncounter.GetAlienShipList() )
		{
			joined += alienShip.m_addedToEncounter ? 1 : 0;
		}

		// the player opens fire
		encounter.PlayerAttacked();

		// ---- 1. one ship of three lost: they fight on
		Kill( FirstLivingAlien() );
		yield return Frames( 3 );

		var stanceAfterOne = pdEncounter.m_alienStance;

		// ---- 2. a second: they act cowardly and stop fighting
		Kill( FirstLivingAlien() );
		yield return Frames( 3 );

		var stanceAfterTwo = pdEncounter.m_alienStance;
		var survivorLives = FirstLivingAlien() >= 0;

		// ---- 3. their hail is one of the cowardly ones (294, 295, and the surrender 296)
		pdEncounter.m_questionLikelihood = 0;

		var said = new List<string>();

		// (a hit in the fight may have damaged the comm, which garbles what they say)
		playerData.m_playerShip.RestoreSystems();

		yield return LetAliensSpeak( 1, said );

		var hailCowardly = ( said.Count > 0 ) && SaysOneOf( said[ 0 ], 294, 295, 296 );

		// ---- 4. the player answers, and their statement is one of the obsequious ones (218 to 220)
		encounter.Hail( GD_Comm.Stance.Friendly, true );
		yield return Frames( 3 );

		var connected = pdEncounter.m_connected;

		playerData.m_playerShip.RestoreSystems();

		yield return LetAliensSpeak( 1, said );

		var statementCowardly = ( said.Count > 1 ) && SaysOneOf( said[ 1 ], 218, 219, 220 );

		// ---- 5. the player fires again: they still do not fight
		encounter.PlayerAttacked();
		yield return Frames( 3 );

		var stanceAfterFiring = pdEncounter.m_alienStance;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 6. the Thrynn and the Veloxi the same way: two of three lost, and the player's hail is answered with their surrender
		var thrynn = new List<string>();
		var velox = new List<string>();

		yield return BeatAndHail( GameData.Race.Thrynn, thrynn );
		yield return BeatAndHail( GameData.Race.Velox, velox );

		var thrynnSurrender = ( thrynn.Count > 0 ) && thrynn[ 0 ].Contains( " Obsequious " ) && SaysOneOf( thrynn[ 0 ], 385 );
		var veloxSurrender = ( velox.Count > 0 ) && velox[ 0 ].Contains( " Obsequious " ) && SaysOneOf( velox[ 0 ], 485, 486 );

		Log( "cowardice: encounter " + speminId + " with " + joined + " ships | after one loss " + stanceAfterOne + ", after two " + stanceAfterTwo + " (a survivor " + survivorLives + ") | said " + string.Join( " | ", said ) + " | connected " + connected + " | after firing again " + stanceAfterFiring + " || thrynn: " + string.Join( " | ", thrynn ) + " || veloxi: " + string.Join( " | ", velox ) );

		Check( "cowardice: with one ship of three lost the Spemin fight on (control)", ( joined == 3 ) && ( stanceAfterOne == GD_Comm.Stance.Hostile ), joined + " ships, " + stanceAfterOne );
		Check( "cowardice: with two lost the survivor acts cowardly and stops fighting", survivorLives && ( stanceAfterTwo == GD_Comm.Stance.Obsequious ), stanceAfterTwo.ToString() );
		Check( "cowardice: the cowardly Spemin hail with their cowardly lines, the surrender among them", hailCowardly, ( said.Count > 0 ) ? said[ 0 ] : "nothing said" );
		Check( "cowardice: their statements are the obsequious ones", connected && statementCowardly, connected + " | " + ( ( said.Count > 1 ) ? said[ 1 ] : "nothing said" ) );
		Check( "cowardice: fired on again, they still do not fight", stanceAfterFiring == GD_Comm.Stance.Obsequious, stanceAfterFiring.ToString() );
		Check( "cowardice: the Thrynn, two of three lost, stop fighting and answer a hail with their surrender", thrynnSurrender, string.Join( " | ", thrynn ) );
		Check( "cowardice: the Veloxi, two of three lost, stop fighting and answer a hail with their surrender", veloxSurrender, string.Join( " | ", velox ) );

		Finish( "scenario=cowardice after1=" + stanceAfterOne + " after2=" + stanceAfterTwo + " firing=" + stanceAfterFiring, 0 );
	}

	// ---------------------------------------------------------------- phase 2: the Elowan's gift of energy

	// sets how much Endurium is in the hold, in tenths of a cubic meter
	static void SetEndurium( int volume )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var enduriumId = DataController.m_instance.m_gameData.m_misc.m_enduriumElementId;

		ship.RemoveElement( enduriumId, HeldElement( enduriumId ) );

		if ( volume > 0 )
		{
			ship.AddElement( enduriumId, volume );
		}
	}

	// meets the aliens of an encounter again, opens the comm link, and lets them ask their first question - adds what the messages show then
	static IEnumerator MeetAndLetThemAsk( int encounterId, List<string> said )
	{
		var encounter = SpaceflightController.m_instance.m_encounter;

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		encounter.Hail( GD_Comm.Stance.Friendly, false );

		yield return LetAliensSpeak( 1, said );

		encounter.m_pdEncounter.m_questionLikelihood = 100;

		yield return LetAliensSpeak( 1, said );
	}

	IEnumerator ScenarioElowanGift()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;
		var enduriumId = dataController.m_gameData.m_misc.m_enduriumElementId;

		EnsureCrew();

		// an officer who understands every word
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		var elowanId = FindEncounterOfRace( GameData.Race.Elowan );
		var question = CommText( 6 );

		// ---- 1. 5 cubic meters of Endurium in the hold: the Elowan ask, and a yes gets 15 cubic meters
		SetEndurium( 50 );

		var lowSaid = new List<string>();

		yield return MeetAndLetThemAsk( elowanId, lowSaid );

		var lowAsked = ( lowSaid.Count > 1 ) && lowSaid[ 1 ].Contains( question ) && ( encounter.m_pdEncounter.m_lastQuestionFromAliens == 6 );

		controller.m_messages.Clear();

		new AnswerYesButton().Execute();
		yield return Frames( 3 );

		var lowAnswer = MessageList();
		var lowAfter = HeldElement( enduriumId );

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. 30 cubic meters: a yes gets the Elowan's "thy need does not seem dire", and nothing
		SetEndurium( 300 );

		var highSaid = new List<string>();

		yield return MeetAndLetThemAsk( elowanId, highSaid );

		var highAsked = ( highSaid.Count > 1 ) && highSaid[ 1 ].Contains( question );

		controller.m_messages.Clear();

		new AnswerYesButton().Execute();
		yield return Frames( 3 );

		var highAnswer = MessageList();
		var highAfter = HeldElement( enduriumId );

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. the control: 5 cubic meters again, and a no gets nothing
		SetEndurium( 50 );

		var noSaid = new List<string>();

		yield return MeetAndLetThemAsk( elowanId, noSaid );

		controller.m_messages.Clear();

		new AnswerNoButton().Execute();
		yield return Frames( 3 );

		var noAnswer = MessageList();
		var noAfter = HeldElement( enduriumId );

		LeaveEncounter();
		yield return Frames( 10 );

		controller.m_encounterRange = encounterRange;

		Log( "elowangift: encounter " + elowanId + " | 5 m3: " + string.Join( " | ", lowSaid ) + " -> yes: " + lowAnswer + " (endurium " + lowAfter + ") || 30 m3: " + string.Join( " | ", highSaid ) + " -> yes: " + highAnswer + " (endurium " + highAfter + ") || 5 m3, no: " + noAnswer + " (endurium " + noAfter + ")" );

		Check( "elowangift: once the comm link is up the Elowan's first question is whether the ship's need for energy is dire", lowAsked && highAsked, ( ( lowSaid.Count > 1 ) ? lowSaid[ 1 ] : "nothing" ) );
		Check( "elowangift: a yes with less than 20 cubic meters of Endurium gets 15 cubic meters", lowAnswer.Contains( CommText( 5 ) ) && ( lowAfter == 200 ), lowAfter + " | " + lowAnswer );
		Check( "elowangift: a yes with 20 or more gets their reproach and nothing", highAnswer.Contains( CommText( 4 ) ) && ( highAfter == 300 ), highAfter + " | " + highAnswer );
		Check( "elowangift: a no gets nothing (control)", !noAnswer.Contains( CommText( 5 ) ) && ( noAfter == 50 ), noAfter + " | " + noAnswer );

		Finish( "scenario=elowangift low=" + lowAfter + " high=" + highAfter + " no=" + noAfter, 0 );
	}

	// ---------------------------------------------------------------- phase 2: the Veloxi's demands

	// meets the Veloxi with this much Endurium (in tenths) and these artifacts in the hold, lets them make their demand and answers it with this button - returns
	// what was said, and what the hold and the aliens' stance are afterwards, through the list
	static IEnumerator AnswerTheVeloxi( int encounterId, int endurium, string artifact, ShipButton answer, List<string> results )
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var encounter = SpaceflightController.m_instance.m_encounter;
		var ship = playerData.m_playerShip;
		var enduriumId = dataController.m_gameData.m_misc.m_enduriumElementId;

		SetEndurium( endurium );

		foreach ( var name in new[] { "Crystal Orb", "Ellipsoid" } )
		{
			for ( var i = 0; ( i < 10 ) && ship.HasArtifact( name ); i++ )
			{
				ship.m_artifactStorage.Remove( dataController.m_gameData.FindArtifactId( name ) );
			}
		}

		if ( artifact != null )
		{
			ship.AddArtifact( dataController.m_gameData.FindArtifactId( artifact ) );
		}

		ship.RestoreSystems();

		var said = new List<string>();

		yield return MeetAndLetThemAsk( encounterId, said );

		var demand = ( said.Count > 1 ) ? said[ 1 ] : "nothing said";

		SpaceflightController.m_instance.m_messages.Clear();

		answer.Execute();
		yield return Frames( 3 );

		var reply = MessageList();
		var stance = encounter.m_pdEncounter.m_alienStance;
		var connected = encounter.m_pdEncounter.m_connected;

		results.Add( demand );
		results.Add( reply );
		results.Add( HeldElement( enduriumId ).ToString() );
		results.Add( ( artifact != null ) && ship.HasArtifact( artifact ) ? "kept" : "gone" );
		results.Add( stance.ToString() );
		results.Add( connected.ToString() );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );
	}

	IEnumerator ScenarioVeloxDemands()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// an officer who understands every word, and a ship that survives an attack
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;
		playerData.m_playerShip.m_armorPoints = 100000;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		var veloxId = FindEncounter( 1, 6, 3, 0, GameData.Race.Velox );

		// ---- 1. the tribute of 3 cubic meters, paid from 5 (the control: they thank, and do not attack)
		var paid = new List<string>();

		yield return AnswerTheVeloxi( veloxId, 50, null, new AnswerYesButton(), paid );

		// ---- 2. the tribute refused
		var refused = new List<string>();

		yield return AnswerTheVeloxi( veloxId, 50, null, new AnswerNoButton(), refused );

		// ---- 3. a yes with 1 cubic meter in the hold
		var tooLittle = new List<string>();

		yield return AnswerTheVeloxi( veloxId, 10, null, new AnswerYesButton(), tooLittle );

		// ---- 4. the Crystal Orb in the hold: their demand for it, and a yes
		var orb = new List<string>();

		yield return AnswerTheVeloxi( veloxId, 50, "Crystal Orb", new AnswerYesButton(), orb );

		// ---- 5. the Ellipsoid in the hold: their demand for it, and the comm link ended with no answer
		var ellipsoid = new List<string>();

		yield return AnswerTheVeloxi( veloxId, 50, "Ellipsoid", new TerminateButton(), ellipsoid );

		controller.m_encounterRange = encounterRange;

		Log( "veloxdemands: encounter " + veloxId + " || paid: " + string.Join( " | ", paid ) + " || refused: " + string.Join( " | ", refused ) + " || too little: " + string.Join( " | ", tooLittle ) + " || orb: " + string.Join( " | ", orb ) + " || ellipsoid: " + string.Join( " | ", ellipsoid ) );

		var tribute = "pay tribute of 3 Arth energy crystals";

		Check( "veloxdemands: the Veloxi demand a tribute of 3, and a yes pays it and gets their thanks without an attack (control of the attacks)", paid[ 0 ].Contains( tribute ) && paid[ 1 ].Contains( CommText( 415 ) ) && ( paid[ 2 ] == "20" ) && ( paid[ 4 ] != "Hostile" ), string.Join( " | ", paid ) );
		Check( "veloxdemands: a refused tribute brings an attack and keeps the Endurium", refused[ 0 ].Contains( tribute ) && ( refused[ 2 ] == "50" ) && ( refused[ 4 ] == "Hostile" ) && ( refused[ 5 ] == "False" ), string.Join( " | ", refused ) );
		Check( "veloxdemands: a yes with less than 3 cubic meters is refused like a no", tooLittle[ 0 ].Contains( tribute ) && ( tooLittle[ 2 ] == "10" ) && ( tooLittle[ 4 ] == "Hostile" ), string.Join( " | ", tooLittle ) );
		Check( "veloxdemands: with the Crystal Orb aboard they demand it, and a yes hands it over without an attack", orb[ 0 ].Contains( CommText( 398 ) ) && ( orb[ 3 ] == "gone" ) && ( orb[ 4 ] != "Hostile" ), string.Join( " | ", orb ) );
		Check( "veloxdemands: with the Ellipsoid aboard they demand it, and ending the comm link with no answer brings an attack", ellipsoid[ 0 ].Contains( CommText( 397 ) ) && ( ellipsoid[ 3 ] == "kept" ) && ( ellipsoid[ 4 ] == "Hostile" ), string.Join( " | ", ellipsoid ) );

		Finish( "scenario=veloxdemands paid=" + paid[ 2 ] + "/" + paid[ 4 ] + " refused=" + refused[ 4 ] + " orb=" + orb[ 3 ] + " ellipsoid=" + ellipsoid[ 4 ], 0 );
	}

	// ---------------------------------------------------------------- phase 2: the Thrynn's trades

	// meets the Thrynn with this much Endurium and plutonium (in tenths) and these artifacts in the hold, lets them make their offer and answers it with this
	// button - adds the offer, the reply, the Endurium and plutonium afterwards, whether the first artifact is still aboard, whether a Black Box is, and the stance
	static IEnumerator TradeWithTheThrynn( int encounterId, int endurium, int plutonium, string[] artifacts, ShipButton answer, List<string> results )
	{
		var dataController = DataController.m_instance;
		var gameData = dataController.m_gameData;
		var encounter = SpaceflightController.m_instance.m_encounter;
		var ship = dataController.m_playerData.m_playerShip;
		var plutoniumId = gameData.FindElementId( "Plutonium" );

		SetEndurium( endurium );

		ship.RemoveElement( plutoniumId, HeldElement( plutoniumId ) );

		if ( plutonium > 0 )
		{
			ship.AddElement( plutoniumId, plutonium );
		}

		foreach ( var artifact in gameData.m_artifactList )
		{
			for ( var i = 0; ( i < 10 ) && ( ship.m_artifactStorage != null ) && ( ship.m_artifactStorage.Find( artifact.m_id ) != null ); i++ )
			{
				ship.m_artifactStorage.Remove( artifact.m_id );
			}
		}

		foreach ( var name in artifacts )
		{
			ship.AddArtifact( gameData.FindArtifactId( name ) );
		}

		ship.RecalculateVolumeUsed();
		ship.RestoreSystems();

		var said = new List<string>();

		yield return MeetAndLetThemAsk( encounterId, said );

		SpaceflightController.m_instance.m_messages.Clear();

		answer.Execute();
		yield return Frames( 3 );

		results.Add( ( said.Count > 1 ) ? said[ 1 ] : "nothing said" );
		results.Add( MessageList() );
		results.Add( HeldElement( gameData.m_misc.m_enduriumElementId ).ToString() );
		results.Add( HeldElement( plutoniumId ).ToString() );
		results.Add( ( artifacts.Length > 0 ) && ship.HasArtifact( artifacts[ 0 ] ) ? "kept" : "gone" );
		results.Add( ship.HasArtifact( "Black Box" ) ? "black box" : "no black box" );
		results.Add( encounter.m_pdEncounter.m_alienStance.ToString() );

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );
	}

	IEnumerator ScenarioThrynnTrades()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var ship = playerData.m_playerShip;

		EnsureCrew();

		// an officer who understands every word, and a hold with room for every payment
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;

		ship.m_numCargoPods = 16;
		ship.RecalculateVolume();

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		var thrynnId = FindEncounter( 1, 6, 3, 0, GameData.Race.Thrynn );

		// ---- 1. the Tesseract (29 at their prices) and the Whining Orb (18) aboard: they offer for the Tesseract, and a yes sells it for 29 cubic meters
		var sold = new List<string>();

		yield return TradeWithTheThrynn( thrynnId, 50, 0, new[] { "Tesseract", "Whining Orb" }, new AnswerYesButton(), sold );

		// ---- 2. the same, and a no (the control: "very well", nothing changes hands, no attack)
		var kept = new List<string>();

		yield return TradeWithTheThrynn( thrynnId, 50, 0, new[] { "Tesseract", "Whining Orb" }, new AnswerNoButton(), kept );

		// ---- 3. 12 cubic meters of plutonium and nothing they want else: 12 of Endurium for it
		var plutonium = new List<string>();

		yield return TradeWithTheThrynn( thrynnId, 50, 120, new string[ 0 ], new AnswerYesButton(), plutonium );

		// ---- 4. nothing to sell and 40 cubic meters of Endurium: they sell the Black Box for 30
		var bought = new List<string>();

		yield return TradeWithTheThrynn( thrynnId, 400, 0, new string[ 0 ], new AnswerYesButton(), bought );

		// ---- 5. nothing to sell and 10: "you do not have enough"
		var tooLittle = new List<string>();

		yield return TradeWithTheThrynn( thrynnId, 100, 0, new string[ 0 ], new AnswerYesButton(), tooLittle );

		controller.m_encounterRange = encounterRange;

		Log( "thrynntrades: encounter " + thrynnId + " || sold: " + string.Join( " | ", sold ) + " || kept: " + string.Join( " | ", kept ) + " || plutonium: " + string.Join( " | ", plutonium ) + " || black box: " + string.Join( " | ", bought ) + " || too little: " + string.Join( " | ", tooLittle ) );

		var veryWell = CommText( 303 );

		Check( "thrynntrades: they offer their price for the artifact aboard they pay the most for, and a yes sells it", sold[ 0 ].Contains( "a Tesseract" ) && sold[ 0 ].Contains( " 29 " ) && sold[ 1 ].Contains( veryWell ) && ( sold[ 2 ] == "340" ) && ( sold[ 4 ] == "gone" ), string.Join( " | ", sold ) );
		Check( "thrynntrades: a no gets \"very well\", and nothing changes hands (control)", kept[ 0 ].Contains( "a Tesseract" ) && kept[ 1 ].Contains( veryWell ) && ( kept[ 2 ] == "50" ) && ( kept[ 4 ] == "kept" ) && ( kept[ 6 ] != "Hostile" ), string.Join( " | ", kept ) );
		Check( "thrynntrades: they offer a cubic meter of Endurium for each of plutonium", plutonium[ 0 ].Contains( " 12 " ) && ( plutonium[ 2 ] == "170" ) && ( plutonium[ 3 ] == "0" ), string.Join( " | ", plutonium ) );
		Check( "thrynntrades: with nothing they want aboard they sell the Black Box for 30", bought[ 0 ].Contains( CommText( 301 ) ) && ( bought[ 2 ] == "100" ) && ( bought[ 5 ] == "black box" ), string.Join( " | ", bought ) );
		Check( "thrynntrades: a yes without 30 cubic meters gets \"you do not have enough\"", tooLittle[ 1 ].Contains( CommText( 302 ) ) && ( tooLittle[ 2 ] == "100" ) && ( tooLittle[ 5 ] == "no black box" ), string.Join( " | ", tooLittle ) );

		Finish( "scenario=thrynntrades sold=" + sold[ 2 ] + " plutonium=" + plutonium[ 2 ] + " blackbox=" + bought[ 5 ], 0 );
	}

	// ---------------------------------------------------------------- phase 2: the aliens' mood

	// a field of the current encounter's player data, by name (the fallback on the code before the field)
	static T EncounterField<T>( string name, T fallback )
	{
		var field = typeof( PD_Encounter ).GetField( name );
		var pdEncounter = SpaceflightController.m_instance.m_encounter.m_pdEncounter;

		return ( ( field == null ) || ( pdEncounter == null ) ) ? fallback : (T) field.GetValue( pdEncounter );
	}

	// sets a field of the current encounter's player data, by name (nothing on the code before the field)
	static void SetEncounterField( string name, object value )
	{
		var field = typeof( PD_Encounter ).GetField( name );
		var pdEncounter = SpaceflightController.m_instance.m_encounter.m_pdEncounter;

		if ( ( field != null ) && ( pdEncounter != null ) )
		{
			field.SetValue( pdEncounter, value );
		}
	}

	// true if the text holds one of the game data's lines of this race, subject and stance
	static bool SaysALineOf( string text, GameData.Race race, GD_Comm.Subject subject, GD_Comm.Stance stance )
	{
		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( ( comm.m_race == race ) && ( comm.m_subject == subject ) && ( comm.m_stance == stance ) && SaysLine( text, comm.m_text ) )
			{
				return true;
			}
		}

		return false;
	}

	// meets the aliens of an encounter, hails them in this posture and lets them answer (the comm link is up then) - adds what the messages show
	static IEnumerator MeetAndConnect( int encounterId, GD_Comm.Stance posture, List<string> said )
	{
		var encounter = SpaceflightController.m_instance.m_encounter;

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		DataController.m_instance.m_playerData.m_playerShip.RestoreSystems();

		encounter.Hail( posture, false );

		yield return LetAliensSpeak( 1, said );

		encounter.m_pdEncounter.m_questionLikelihood = 0;
	}

	IEnumerator ScenarioMood()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;
		var ship = playerData.m_playerShip;

		EnsureCrew();

		// an officer who understands every word
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		var speminId = FindEncounter( 1, 6, 3, 0 );
		var elowanId = FindEncounterOfRace( GameData.Race.Elowan );
		var thrynnId = FindEncounter( 1, 6, 3, 0, GameData.Race.Thrynn );

		// ---- 1. the Spemin met in peace by a ship weaker than them: they talk tough (a hostile statement, which they have and no neutral one), and hold their fire
		ship.m_armorPoints = 250;
		ship.m_shieldPoints = 0;

		var weakSaid = new List<string>();

		yield return MeetAndConnect( speminId, GD_Comm.Stance.Friendly, weakSaid );
		yield return LetAliensSpeak( 1, weakSaid );

		var weakStatement = ( weakSaid.Count > 1 ) ? weakSaid[ 1 ] : "nothing said";
		var weakStance = encounter.m_pdEncounter.m_alienStance;

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. the same Spemin met by a far stronger ship: they grovel
		ship.m_armorPoints = 100000;

		var strongSaid = new List<string>();

		yield return MeetAndConnect( speminId, GD_Comm.Stance.Friendly, strongSaid );
		yield return LetAliensSpeak( 1, strongSaid );

		var strongStatement = ( strongSaid.Count > 1 ) ? strongSaid[ 1 ] : "nothing said";

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. the Elowan: two friendly statements calm them, the second by half as much as the first, and a question by half again of the third
		var elowanSaid = new List<string>();

		yield return MeetAndConnect( elowanId, GD_Comm.Stance.Friendly, elowanSaid );

		var moodAtStart = EncounterField( "m_mood", -1.0f );

		new StatementButton().Execute();
		yield return Frames( 2 );

		var moodAfterOne = EncounterField( "m_mood", -1.0f );

		new StatementButton().Execute();
		yield return Frames( 2 );

		var moodAfterTwo = EncounterField( "m_mood", -1.0f );

		new QThemselvesButton().Execute();
		yield return Frames( 2 );

		var moodAfterQuestion = EncounterField( "m_mood", -1.0f );
		var elowanStance = encounter.m_pdEncounter.m_alienStance;

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 4. the Elowan too agitated to talk: a hostile statement at 94 - against a stronger ship they take flight (no attack), against a weaker one they fight
		var flight = new List<string>();

		yield return MeetAndConnect( elowanId, GD_Comm.Stance.Friendly, flight );

		SetEncounterField( "m_mood", 94.0f );

		encounter.Hail( GD_Comm.Stance.Hostile, false );

		new StatementButton().Execute();
		yield return Frames( 3 );

		var flightConnected = encounter.m_pdEncounter.m_connected;
		var flightStance = encounter.m_pdEncounter.m_alienStance;

		LeaveEncounter();
		yield return Frames( 10 );

		ship.m_armorPoints = 1000;

		var fight = new List<string>();

		yield return MeetAndConnect( elowanId, GD_Comm.Stance.Friendly, fight );

		foreach ( var alienShip in encounter.m_pdEncounter.GetAlienShipList() )
		{
			alienShip.m_armorPoints = 1000000;
		}

		SetEncounterField( "m_mood", 94.0f );

		encounter.Hail( GD_Comm.Stance.Hostile, false );

		new StatementButton().Execute();
		yield return Frames( 3 );

		var fightConnected = encounter.m_pdEncounter.m_connected;
		var fightStance = encounter.m_pdEncounter.m_alienStance;

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		ship.m_armorPoints = 100000;

		// ---- 5. the Thrynn tired of talking: with one phrase left, they say it, and then end the comm link
		var thrynnSaid = new List<string>();

		yield return MeetAndConnect( thrynnId, GD_Comm.Stance.Friendly, thrynnSaid );

		SetEncounterField( "m_alienPhrasesLeft", 1 );

		yield return LetAliensSpeak( 2, thrynnSaid );

		var thrynnConnectedAfter = encounter.m_pdEncounter.m_connected;
		var thrynnLast = ( thrynnSaid.Count > 2 ) ? thrynnSaid[ 2 ] : "nothing said";

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 6. the control: the Gazurtoid have no line to end a conversation with, and still end it when their time is up
		var gazurtoidSaid = new List<string>();

		yield return MeetAndConnect( FindEncounterOfRace( GameData.Race.Gazurtoid ), GD_Comm.Stance.Friendly, gazurtoidSaid );

		var gazurtoidConnected = encounter.m_pdEncounter.m_connected;

		encounter.m_pdEncounter.m_connectionTimer = 0.0f;

		yield return Frames( 3 );

		var gazurtoidConnectedAfter = encounter.m_pdEncounter.m_connected;

		LeaveEncounter();
		yield return Frames( 10 );

		controller.m_encounterRange = encounterRange;

		Log( "mood: spemin, weak ship: " + weakStatement + " (stance " + weakStance + ") | strong ship: " + strongStatement + " || elowan mood " + moodAtStart + " -> " + moodAfterOne + " -> " + moodAfterTwo + " -> question " + moodAfterQuestion + " (stance " + elowanStance + ") || too agitated, stronger ship: connected " + flightConnected + ", stance " + flightStance + " | weaker ship: connected " + fightConnected + ", stance " + fightStance + " || thrynn: " + string.Join( " | ", thrynnSaid ) + " (connected after " + thrynnConnectedAfter + ")" );

		Check( "mood: the Spemin met in peace by a weaker ship talk tough and hold their fire", SaysALineOf( weakStatement, GameData.Race.Spemin, GD_Comm.Subject.Statement, GD_Comm.Stance.Hostile ) && ( weakStance != GD_Comm.Stance.Hostile ), weakStance + " | " + weakStatement );
		Check( "mood: the Spemin met by a far stronger ship grovel", SaysALineOf( strongStatement, GameData.Race.Spemin, GD_Comm.Subject.Statement, GD_Comm.Stance.Obsequious ), strongStatement );
		Check( "mood: friendly statements calm the Elowan, each by less than the one before, and a question by half", ( moodAtStart == 45.0f ) && ( moodAfterOne == 35.0f ) && ( moodAfterTwo == 30.0f ) && Mathf.Abs( moodAfterQuestion - ( 30.0f - 10.0f / 3.0f / 2.0f ) ) < 0.01f && ( elowanStance == GD_Comm.Stance.Friendly ), moodAtStart + " -> " + moodAfterOne + " -> " + moodAfterTwo + " -> " + moodAfterQuestion + ", " + elowanStance );
		Check( "mood: too agitated to talk, the Elowan end the comm link and take flight from a stronger ship", !flightConnected && ( flightStance == GD_Comm.Stance.Obsequious ), flightConnected + ", " + flightStance );
		Check( "mood: too agitated to talk, the Elowan end the comm link and fight a weaker ship", !fightConnected && ( fightStance == GD_Comm.Stance.Hostile ), fightConnected + ", " + fightStance );
		Check( "mood: the Thrynn say their last phrase and then end the comm link, tired of talking", !thrynnConnectedAfter && SaysALineOf( thrynnLast, GameData.Race.Thrynn, GD_Comm.Subject.Terminate, GD_Comm.Stance.Neutral ), thrynnConnectedAfter + " | " + thrynnLast );

		Check( "mood: aliens with no line to end a conversation with still end it when their time is up (control)", gazurtoidConnected && !gazurtoidConnectedAfter, gazurtoidConnected + " -> " + gazurtoidConnectedAfter );

		Finish( "scenario=mood elowan=" + moodAtStart + "/" + moodAfterOne + "/" + moodAfterTwo + "/" + moodAfterQuestion + " flight=" + flightStance + " fight=" + fightStance, 0 );
	}

	// ---------------------------------------------------------------- phase 2: the Elowan and the Thrynn, and a crew that is friends with the other race

	// the first of the Thrynn and the Elowan the crew opened a comm link with ("missing" on the code before it was remembered)
	static string FirstContactRace()
	{
		var field = typeof( PD_General ).GetField( "m_firstContactRace" );

		return ( field == null ) ? "missing" : field.GetValue( DataController.m_instance.m_playerData.m_general ).ToString();
	}

	// true if the text holds one of the game data's lines of this race and subject, in any stance
	static bool SaysALineOfAnyStance( string text, GameData.Race race, GD_Comm.Subject subject )
	{
		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( ( comm.m_race == race ) && ( comm.m_subject == subject ) && SaysLine( text, comm.m_text ) )
			{
				return true;
			}
		}

		return false;
	}

	IEnumerator ScenarioRivals()
	{
		var dataController = DataController.m_instance;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;

		EnsureCrew();

		var officer = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer );

		officer.m_communications = 250;
		playerData.m_playerShip.m_armorPoints = 100000;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		var elowanId = FindEncounterOfRace( GameData.Race.Elowan );
		var thrynnId = FindEncounter( 1, 6, 3, 0, GameData.Race.Thrynn );

		// ---- 1. the first comm link of the game is with the Thrynn: it is remembered
		var firstBefore = FirstContactRace();
		var thrynnSaid = new List<string>();

		yield return MeetAndConnect( thrynnId, GD_Comm.Stance.Friendly, thrynnSaid );

		var firstAfter = FirstContactRace();

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. then the Elowan: their greeting says what they think of friends of the Thrynn, and they start in their hostile band
		var elowanSaid = new List<string>();

		yield return MeetAndConnect( elowanId, GD_Comm.Stance.Friendly, elowanSaid );

		var elowanGreeting = ( elowanSaid.Count > 0 ) ? elowanSaid[ 0 ] : "nothing said";
		var elowanMood = EncounterField( "m_mood", -1.0f );

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. a Thrynn in the crew: the Elowan have no dealings with the ship, end the comm link and do not attack
		var thrynnCrew = playerData.m_personnel.CreateNewPersonnel();

		thrynnCrew.m_name = "Thrynn";
		thrynnCrew.m_crewRaceId = 2;
		thrynnCrew.m_vitality = 100.0f;

		playerData.m_personnel.m_personnelList.Add( thrynnCrew );
		playerData.m_crewAssignment.Assign( PD_CrewAssignment.Role.Navigator, thrynnCrew.m_fileId );

		EnterEncounter( elowanId );
		yield return Frames( 10 );

		var noDealingsSaid = new List<string>();

		yield return LetAliensSpeak( 1, noDealingsSaid );
		yield return Frames( 3 );

		var noDealings = ( noDealingsSaid.Count > 0 ) ? noDealingsSaid[ 0 ] : "nothing said";
		var noDealingsDisconnected = encounter.m_pdEncounter.m_disconnected;
		var noDealingsStance = encounter.m_pdEncounter.m_alienStance;

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 4. translation: an officer with no skill, and the Thrynn's greeting comes through whole with a Thrynn aboard - garbled without (the control)
		officer.m_communications = 0;

		var withThrynn = new List<string>();

		yield return MeetAndConnect( thrynnId, GD_Comm.Stance.Friendly, withThrynn );

		LeaveEncounter();
		yield return Frames( 10 );

		thrynnCrew.m_crewRaceId = 3;

		var withoutThrynn = new List<string>();

		yield return MeetAndConnect( thrynnId, GD_Comm.Stance.Friendly, withoutThrynn );

		LeaveEncounter();
		yield return Frames( 10 );

		var wholeWithThrynn = ( withThrynn.Count > 0 ) && SaysALineOfAnyStance( withThrynn[ 0 ], GameData.Race.Thrynn, GD_Comm.Subject.GreetingResponse );
		var wholeWithout = ( withoutThrynn.Count > 0 ) && ( SaysALineOfAnyStance( withoutThrynn[ 0 ], GameData.Race.Thrynn, GD_Comm.Subject.GreetingResponse ) || SaysOneOf( withoutThrynn[ 0 ], 297 ) );

		// ---- 5. an Elowan in the crew (the same crew member, now an Elowan): the Thrynn greet with "most regrettable" and start in their hostile band
		officer.m_communications = 250;

		var elowanAboard = new List<string>();

		yield return MeetAndConnect( thrynnId, GD_Comm.Stance.Friendly, elowanAboard );

		var regrettable = ( elowanAboard.Count > 0 ) ? elowanAboard[ 0 ] : "nothing said";
		var thrynnMood = EncounterField( "m_mood", -1.0f );

		LeaveEncounter();
		yield return Frames( 10 );

		controller.m_encounterRange = encounterRange;

		Log( "rivals: first contact " + firstBefore + " -> " + firstAfter + " || elowan after the thrynn: " + elowanGreeting + " (mood " + elowanMood + ") || thrynn aboard: " + noDealings + " (disconnected " + noDealingsDisconnected + ", stance " + noDealingsStance + ") || skill 0, thrynn aboard: " + string.Join( " | ", withThrynn ) + " || skill 0, none aboard: " + string.Join( " | ", withoutThrynn ) + " || elowan aboard: " + regrettable + " (mood " + thrynnMood + ")" );

		Check( "rivals: the first comm link with the Thrynn is remembered", ( firstBefore == "None" ) && ( firstAfter == "Thrynn" ), firstBefore + " -> " + firstAfter );
		Check( "rivals: after the Thrynn the Elowan greet the crew as friends of the Thrynn, and start in their hostile band", elowanGreeting.Contains( CommText( 2 ) ) && ( elowanMood >= 75.0f ), elowanMood + " | " + elowanGreeting );
		Check( "rivals: with a Thrynn aboard the Elowan have no dealings with the ship, end the comm link, and do not attack", noDealings.Contains( CommText( 1 ) ) && noDealingsDisconnected && ( noDealingsStance != GD_Comm.Stance.Hostile ), noDealingsDisconnected + ", " + noDealingsStance + " | " + noDealings );
		Check( "rivals: with a Thrynn aboard an officer with no skill understands every word of the Thrynn, and without one not (control)", wholeWithThrynn && !wholeWithout, string.Join( " | ", withThrynn ) + " || " + string.Join( " | ", withoutThrynn ) );
		Check( "rivals: with an Elowan aboard the Thrynn greet with \"most regrettable\", and start in their hostile band", regrettable.Contains( CommText( 297 ) ) && ( thrynnMood >= 65.0f ), thrynnMood + " | " + regrettable );

		Finish( "scenario=rivals first=" + firstAfter + " elowanMood=" + elowanMood + " thrynnMood=" + thrynnMood, 0 );
	}

	// ---------------------------------------------------------------- phase 2: the races that transmit rather than talk

	// true if each of these texts holds the comm of the game data at the same place, in order
	static bool InOrder( List<string> said, int first, params int[] commIds )
	{
		if ( said.Count < first + commIds.Length )
		{
			return false;
		}

		for ( var i = 0; i < commIds.Length; i++ )
		{
			if ( !said[ first + i ].Contains( CommText( commIds[ i ] ) ) )
			{
				return false;
			}
		}

		return true;
	}

	IEnumerator ScenarioTransmissions()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;

		EnsureCrew();

		// an officer who understands every word
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;
		playerData.m_playerShip.m_armorPoints = 100000;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		// ---- 1. a Minstrel: once the comm link is up, its seven verses in order, and then the link ends
		var minstrel = new List<string>();

		yield return MeetAndConnect( FindEncounterOfRace( GameData.Race.Minstrel ), GD_Comm.Stance.Friendly, minstrel );
		yield return LetAliensSpeak( 8, minstrel );

		var minstrelConnected = encounter.m_pdEncounter.m_connected;

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 2. a Nomad probe: its six planetary reports in order, and then the link ends
		var nomad = new List<string>();

		yield return MeetAndConnect( FindEncounterOfRace( GameData.Race.NomadProbe ), GD_Comm.Stance.Friendly, nomad );
		yield return LetAliensSpeak( 7, nomad );

		var nomadConnected = encounter.m_pdEncounter.m_connected;

		LeaveEncounter();
		yield return Frames( 10 );

		// ---- 3. the Noah 9 derelict in orbit 3 of 175,94: its distress beacon, and once the ship answers it (the comm link is up at once), the two parts of its
		// recorded message
		var noahStar = StarAt( 175, 94 );

		yield return GoIntoOrbit( noahStar, PlanetInOrbit( noahStar, 3 ) );

		var noahBegun = ( playerData.m_general.m_location == PD_General.Location.Encounter ) && ( playerData.m_general.m_currentEncounterId == 316 );
		var noah = new List<string>();

		yield return LetAliensSpeak( 1, noah );

		encounter.Hail( GD_Comm.Stance.Friendly, true );

		yield return LetAliensSpeak( 3, noah );

		var noahConnected = encounter.m_pdEncounter.m_connected;

		LeaveEncounter();
		yield return Frames( 10 );

		controller.m_encounterRange = encounterRange;

		Log( "transmissions: minstrel " + string.Join( " | ", minstrel ) + " (connected after " + minstrelConnected + ") || nomad " + string.Join( " | ", nomad ) + " (connected after " + nomadConnected + ") || noah 9 begun " + noahBegun + ": " + string.Join( " | ", noah ) + " (connected after " + noahConnected + ")" );

		Check( "transmissions: a Minstrel sings its seven verses in order once the comm link is up, and then ends the link", InOrder( minstrel, 1, 504, 505, 506, 507, 508, 509, 510 ) && !minstrelConnected, string.Join( " | ", minstrel ) );
		Check( "transmissions: a Nomad probe sends its six planetary reports in order, and then ends the link", InOrder( nomad, 1, 188, 189, 190, 191, 192, 193 ) && !nomadConnected, string.Join( " | ", nomad ) );
		Check( "transmissions: the Noah 9 derelict sends its distress beacon, and once answered its recorded message, and then ends the link", noahBegun && ( noah[ 0 ].Contains( "distress beacon signal" ) || SaysOneOf( noah[ 0 ], 556 ) ) && InOrder( noah, 1, 551, 552 ) && !noahConnected, string.Join( " | ", noah ) );

		Finish( "scenario=transmissions minstrel=" + minstrel.Count + " nomad=" + nomad.Count + " noah=" + noah.Count, 0 );
	}

	// ---------------------------------------------------------------- phase 1: the stability of a star

	// the first star other than Arth's whose flare day is in this range (-1 if there is none)
	static int StarFlaringBetween( int firstDay, int lastDay )
	{
		var gameData = DataController.m_instance.m_gameData;

		for ( var id = 0; id < gameData.m_starList.Length; id++ )
		{
			var flareDay = gameData.m_starList[ id ].m_daysToNextFlare;

			if ( ( id != gameData.m_misc.m_arthStarId ) && ( flareDay >= firstDay ) && ( flareDay <= lastDay ) )
			{
				return id;
			}
		}

		return -1;
	}

	// enters a star system, waits for its planets and scans it - returns what the messages show
	static IEnumerator ScanStarSystem( int starId, List<string> scans )
	{
		var controller = SpaceflightController.m_instance;

		EnterStarSystem( starId );

		var end = Time.realtimeSinceStartup + 40.0f;

		while ( ( Time.realtimeSinceStartup < end ) && controller.m_starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 5 );

		controller.m_messages.Clear();

		new SensorsButton().Execute();

		scans.Add( MessageList() );
	}

	IEnumerator ScenarioStarStability()
	{
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		var flared = StarThatNeverFlares();
		var near = StarFlaringBetween( 150, 290 );
		var nearDay = ( near >= 0 ) ? gameData.m_starList[ near ].m_daysToNextFlare : 0;

		var scans = new List<string>();

		// ---- 1. a star that flared before the game began
		yield return ScanStarSystem( flared, scans );

		// (no star of the game data is more than 792 days from its flare, so none is ever "slightly unstable")

		// ---- 2. a star with less than 1000 days to its flare: the days to its flare, and 100 of them once the day is 100 before it
		yield return ScanStarSystem( near, scans );

		SetGameDay( nearDay - 100 );

		yield return Frames( 3 );

		controller.m_messages.Clear();

		new SensorsButton().Execute();

		scans.Add( MessageList() );

		controller.m_encounterRange = encounterRange;

		Log( "starstability: flared star " + flared + ", near star " + near + " (flare day " + nearDay + ") || " + string.Join( " || ", scans ) );

		Check( "starstability: a star that has flared is stable", scans[ 0 ].Contains( "Stability: <color=white>Stable (post-flare)" ), scans[ 0 ] );
		Check( "starstability: a star with less gives the days to its flare", ( near >= 0 ) && scans[ 1 ].Contains( "Unstable - estimated time to flare " + nearDay + " days" ) && scans[ 2 ].Contains( "Unstable - estimated time to flare 100 days" ), scans[ 1 ] + " || " + scans[ 2 ] );

		Finish( "scenario=starstability near=" + near + "/" + nearDay, 0 );
	}

	// ---------------------------------------------------------------- phase 1: the science officer's skill and the analysis

	// takes a new sensor reading of the planet the ship orbits (finished at once) and has the science officer analyze it - returns what the messages show
	static string AnalyzeNewReading( bool newReading )
	{
		var sensors = SpaceflightController.m_instance.m_displayController.m_sensorsDisplay;
		var planet = DataController.m_instance.m_gameData.m_planetList[ DataController.m_instance.m_playerData.m_general.m_currentPlanetId ];

		if ( newReading )
		{
			sensors.StartScanning( SensorsDisplay.ScanType.Planet, 18, planet.m_mass, planet.m_bioDensity, planet.m_mineralDensity );
		}

		sensors.m_hasSensorData = true;

		SpaceflightController.m_instance.m_messages.Clear();

		new AnalysisButton().Execute();

		return MessageList();
	}

	// how many lines of an analysis read "Unknown"
	static int UnknownLines( string analysis )
	{
		var count = 0;

		for ( var index = analysis.IndexOf( ">Unknown<" ); index >= 0; index = analysis.IndexOf( ">Unknown<", index + 1 ) )
		{
			count++;
		}

		return count;
	}

	IEnumerator ScenarioAnalysisSkill()
	{
		var playerData = DataController.m_instance.m_playerData;
		var sensors = SpaceflightController.m_instance.m_displayController.m_sensorsDisplay;

		EnsureCrew();

		var scienceOfficer = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.ScienceOfficer );

		yield return EnterOrbit( 90 );

		// the sensor window has to be up for a reading
		SpaceflightController.m_instance.m_displayController.ChangeDisplay( sensors );
		yield return Frames( 3 );

		// ---- 1. a science officer of full skill gives every line (the control)
		scienceOfficer.m_science = 250;

		var full = AnalyzeNewReading( true );

		// ---- 2. the same reading again: already attempted
		var again = AnalyzeNewReading( false );

		// ---- 3. no skill: the object's class, and every other line unknown
		scienceOfficer.m_science = 0;

		var none = AnalyzeNewReading( true );

		// ---- 4. a skill of 100: about half of 120 lines in 20 readings
		scienceOfficer.m_science = 100;

		var unknownOfHalf = 0;

		for ( var i = 0; i < 20; i++ )
		{
			unknownOfHalf += UnknownLines( AnalyzeNewReading( true ) );
		}

		// ---- 5. an alien ship's reading may be analyzed as often as wanted
		scienceOfficer.m_science = 250;

		sensors.StartScanning( SensorsDisplay.ScanType.ElowanScout, 1, 50, 0, 0 );
		sensors.m_hasSensorData = true;

		SpaceflightController.m_instance.m_messages.Clear();

		new AnalysisButton().Execute();
		new AnalysisButton().Execute();

		var shipTwice = MessageList();

		yield return Frames( 3 );

		Log( "analysisskill: skill 250: " + full + " || again: " + again + " || skill 0: " + none + " || skill 100: " + unknownOfHalf + " of 120 lines unknown || a ship twice: " + shipTwice );

		Check( "analysisskill: a science officer of full skill gives every line (control)", full.Contains( "Object: <color=white>Planet" ) && ( UnknownLines( full ) == 0 ), full );
		Check( "analysisskill: the same reading of a planet is not analyzed twice", again.Contains( "I have already attempted analysis of this data, Captain." ), again );
		Check( "analysisskill: with no skill only the object's class is given", none.Contains( "Object: <color=white>Planet" ) && ( UnknownLines( none ) == 6 ), none );
		Check( "analysisskill: with a skill of 100 about half the lines are given (skill / 2 percent each)", ( unknownOfHalf >= 36 ) && ( unknownOfHalf <= 84 ), unknownOfHalf + " of 120" );
		Check( "analysisskill: an alien ship's reading may be analyzed again", !shipTwice.Contains( "already attempted" ) && shipTwice.Contains( "Analysis of Last Sensor Reading" ), shipTwice );

		Finish( "scenario=analysisskill unknown100=" + unknownOfHalf, 0 );
	}

	// ---------------------------------------------------------------- phase 1: salvage from debris

	// a summary of the hold's elements: "id:volume" for each, in the order of the element list
	static string HoldSummary()
	{
		var gameData = DataController.m_instance.m_gameData;
		var text = "";

		for ( var elementId = 0; elementId < gameData.m_elementList.Length; elementId++ )
		{
			var volume = HeldElement( elementId );

			if ( volume > 0 )
			{
				text += elementId + ":" + volume + " ";
			}
		}

		return text.Trim();
	}

	IEnumerator ScenarioSalvage()
	{
		var dataController = DataController.m_instance;
		var gameData = dataController.m_gameData;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var encounter = controller.m_encounter;
		var ship = playerData.m_playerShip;
		var sensors = controller.m_displayController.m_sensorsDisplay;

		EnsureCrew();

		ship.m_armorPoints = 100000;
		ship.m_numCargoPods = 16;
		ship.RecalculateVolume();

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		EnterEncounter( FindEncounter( 1, 6, 3, 0 ) );
		yield return Frames( 10 );

		var alienShips = encounter.m_pdEncounter.GetAlienShipList();

		// ---- 1. a wreck far from the ship is not taken aboard
		var first = FirstLivingAlien();
		var vessel = gameData.m_vesselList[ alienShips[ first ].m_vesselId ];

		Kill( first );

		alienShips[ first ].m_coordinates = playerData.m_general.m_coordinates + Vector3.forward * 2000.0f;

		var holdBefore = HoldSummary();

		yield return Frames( 5 );

		var holdFar = HoldSummary();

		// ---- 2. flown over, its debris comes aboard, as the vessel's record has it
		controller.m_messages.Clear();

		alienShips[ first ].m_coordinates = playerData.m_general.m_coordinates;

		var enduriumBefore = HeldElement( gameData.m_misc.m_enduriumElementId );
		var elementBefore = HeldElement( vessel.m_elementIdA );

		yield return Frames( 5 );

		var enduriumAfter = HeldElement( gameData.m_misc.m_enduriumElementId );
		var elementAfter = HeldElement( vessel.m_elementIdA );
		var salvageMessage = MessageList();

		// ---- 3. the same wreck again: nothing more
		alienShips[ first ].m_coordinates = playerData.m_general.m_coordinates + Vector3.forward * 2000.0f;

		yield return Frames( 3 );

		alienShips[ first ].m_coordinates = playerData.m_general.m_coordinates;

		var holdOnce = HoldSummary();

		yield return Frames( 5 );

		var holdAgain = HoldSummary();

		// ---- 4. a full hold: no room for the debris
		var second = FirstLivingAlien();

		Kill( second );

		ship.AddElement( gameData.FindElementId( "Aluminum" ), Mathf.Max( 0, ship.GetRemainingVolume() ) );

		controller.m_messages.Clear();

		alienShips[ second ].m_coordinates = playerData.m_general.m_coordinates;

		yield return Frames( 5 );

		var fullMessage = MessageList();

		// ---- 5. the sensors read what a wreck is made of
		controller.m_displayController.ChangeDisplay( sensors );
		yield return Frames( 3 );

		sensors.StartScanning( SensorsDisplay.ScanType.Debris, 0, vessel.m_mass, 0, 0, alienShips[ first ].m_vesselId );

		yield return new WaitForSecondsRealtime( sensors.m_minDuration + 1.5f );

		var readout = MessageList();

		ClearMissiles();
		LeaveEncounter();
		yield return Frames( 10 );

		controller.m_encounterRange = encounterRange;

		Log( "salvage: " + vessel.m_name + " (endurium " + vessel.m_enduriumVolume + ", element " + vessel.m_elementIdA + " " + vessel.m_elementVolumeA + ") | hold " + holdBefore + " -> far " + holdFar + " | endurium " + enduriumBefore + " -> " + enduriumAfter + ", element " + elementBefore + " -> " + elementAfter + " | " + salvageMessage + " | again " + holdOnce + " -> " + holdAgain + " | full hold: " + fullMessage + " | wreck readout: " + readout );

		var elementName = gameData.m_elementList[ vessel.m_elementIdA ].m_name;

		Check( "salvage: a wreck far from the ship is not taken aboard (control)", holdFar == holdBefore, holdBefore + " -> " + holdFar );
		Check( "salvage: flown over, a wreck's debris comes aboard as its vessel's record has it", ( enduriumAfter - enduriumBefore == vessel.m_enduriumVolume ) && ( elementAfter - elementBefore == vessel.m_elementVolumeA ) && salvageMessage.Contains( "Salvaged from the wreck" ) && salvageMessage.Contains( elementName ), ( enduriumAfter - enduriumBefore ) + " and " + ( elementAfter - elementBefore ) + " | " + salvageMessage );
		Check( "salvage: a wreck is salvaged once", holdAgain == holdOnce, holdOnce + " -> " + holdAgain );
		Check( "salvage: with the hold full there is no room for the debris", fullMessage.Contains( "no room in the cargo hold" ), fullMessage );
		Check( "salvage: the sensors read what a wreck is made of", readout.Contains( "Object Constituents" ) && readout.Contains( elementName ), readout );

		Finish( "scenario=salvage endurium=" + ( enduriumAfter - enduriumBefore ) + " element=" + ( elementAfter - elementBefore ), 0 );
	}

	// ---------------------------------------------------------------- phase 3: random ruins

	// the ruins on the ground that hold random items (none on the code before random ruins)
	static List<Component> RandomRuinsOnTheGround()
	{
		var randomRuins = new List<Component>();

		foreach ( var ruin in RuinsOnTheGround() )
		{
			var items = FieldOrNull( ruin, "m_randomItems" ) as System.Collections.IList;

			if ( ( items != null ) && ( items.Count > 0 ) )
			{
				randomRuins.Add( ruin );
			}
		}

		return randomRuins;
	}

	// lands on planet 94 of the Arth system at this place (world x and z of the planet's surface), and gets into the terrain vehicle
	static IEnumerator LandOnPlanet94At( Vector3 place, bool[] done )
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		// (the port's "latitude" is the east-west coordinate - see Tools.LatLongToWorldCoordinates)
		Tools.WorldToLatLongCoordinates( place, out var x, out var z );

		playerData.m_general.m_selectedLatitude = x;
		playerData.m_general.m_selectedLongitude = z;

		yield return GoIntoOrbit( 25, 94 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );
	}

	// presses Cargo where the terrain vehicle is - adds what the messages show
	static IEnumerator PressCargo( List<string> said )
	{
		SpaceflightController.m_instance.m_messages.Clear();

		new TVCargoButton().Execute();

		yield return Frames( 2 );

		said.Add( MessageList() );
	}

	IEnumerator ScenarioRandomRuins()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- 1. planet 94, the small rock planet of the Arth system, has no message of the original's and one random ruin
		yield return LandOnPlanet94At( Vector3.zero, done );

		var randomRuins = RandomRuinsOnTheGround();
		var place = ( randomRuins.Count > 0 ) ? randomRuins[ 0 ].transform.position : Vector3.zero;
		var placeBefore = ( randomRuins.Count > 0 ) ? Mathf.RoundToInt( place.x ) + "/" + Mathf.RoundToInt( place.z ) : "none";
		var name = ( randomRuins.Count > 0 ) ? randomRuins[ 0 ].name : "none";

		// ---- 2. landed beside it, Cargo takes what it holds aboard, and a second press nothing more
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandOnPlanet94At( place + Vector3.forward * 5.0f, done );

		var said = new List<string>();
		var roomBefore = playerData.m_terrainVehicle.GetRemainingVolume();

		yield return PressCargo( said );

		var roomAfter = playerData.m_terrainVehicle.GetRemainingVolume();

		yield return PressCargo( said );

		var roomAgain = playerData.m_terrainVehicle.GetRemainingVolume();

		// ---- 3. back into the ship and out again at the same place: the ruin is where it was, and what was taken is gone
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		var randomRuinsAgain = RandomRuinsOnTheGround();
		var placeAfter = ( randomRuinsAgain.Count > 0 ) ? Mathf.RoundToInt( randomRuinsAgain[ 0 ].transform.position.x ) + "/" + Mathf.RoundToInt( randomRuinsAgain[ 0 ].transform.position.z ) : "none";

		yield return PressCargo( said );

		Log( "randomruins: planet 94 has " + randomRuins.Count + " random ruin (" + name + ") at " + placeBefore + " | room " + roomBefore + " -> " + roomAfter + " -> " + roomAgain + " | " + string.Join( " || ", said ) + " | after landing again at " + placeAfter );

		var first = said[ 0 ];
		var second = said[ 1 ];
		var third = said[ 2 ];

		Check( "randomruins: planet 94 has a random ruin", randomRuins.Count == 1, randomRuins.Count + " random ruins" );
		Check( "randomruins: Cargo beside it takes what it holds aboard", first.Contains( "Captain, we have discovered" ) && ( roomAfter < roomBefore ), roomBefore + " -> " + roomAfter + " | " + first );
		Check( "randomruins: what has been taken is not there again", !second.Contains( "Captain, we have discovered" ) && ( roomAgain == roomAfter ) && !third.Contains( "Captain, we have discovered" ), second + " || " + third );
		Check( "randomruins: the ruin is at the same place on every visit", ( placeBefore != "none" ) && ( placeBefore == placeAfter ), placeBefore + " | " + placeAfter );

		Finish( "scenario=randomruins ruin=" + name + " at=" + placeBefore, 0 );
	}

	// ---------------------------------------------------------------- phase 6: lava, and the loss of the terrain vehicle

	// a place of the terrain vehicle's planet whose ground lies under the liquid (world x and z) - Vector3.zero if there is none
	static Vector3 FindPlaceUnderTheLiquid()
	{
		return FindPlaceByTheLiquid( true );
	}

	// a place whose ground lies under the liquid, or well above it
	static Vector3 FindPlaceByTheLiquid( bool under )
	{
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;
		var planetGenerator = FieldOrNull( terrainVehicle, "m_planetGenerator" ) as PlanetGenerator;

		if ( planetGenerator == null )
		{
			return Vector3.zero;
		}

		var width = planetGenerator.m_textureMapWidth;
		var height = planetGenerator.m_textureMapHeight;

		for ( var y = height / 4; y < height * 3 / 4; y += 4 )
		{
			for ( var x = 0; x < width; x += 4 )
			{
				// well under it, so that the vehicle is in it and not at its edge (or well above it)
				var elevation = planetGenerator.GetBicubicSmoothedElevation( x, y );

				if ( under ? ( elevation < planetGenerator.m_waterElevation * 0.9f ) : ( elevation > planetGenerator.m_waterElevation * 1.1f + 0.01f ) )
				{
					return new Vector3( ( x + 0.5f - width * 0.5f ) * 4.0f, 0.0f, ( y + 0.5f - height * 0.5f ) * 4.0f );
				}
			}
		}

		return Vector3.zero;
	}

	// whether the ship has lost its terrain vehicle (false on the code before it could)
	static bool TerrainVehicleIsLost()
	{
		var value = FieldOrNull( DataController.m_instance.m_playerData.m_terrainVehicle, "m_isLost" );

		return ( value is bool ) && (bool) value;
	}

	IEnumerator ScenarioLava()
	{
		var dataController = DataController.m_instance;
		var gameData = dataController.m_gameData;
		var playerData = dataController.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		// ---- 1. the control: on Arth's world of water, the terrain vehicle driven into the sea floats
		yield return GoIntoOrbit( 25, 90 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		var water = FindPlaceUnderTheLiquid();

		playerData.m_general.m_coordinates = water;

		yield return Frames( 5 );

		var afterWater = playerData.m_general.m_location;
		var lostInWater = TerrainVehicleIsLost();

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		// ---- 2. planet 29, molten (around star 7, which flared before the game began): the terrain vehicle carrying iron, driven into the lava
		var molten = gameData.m_planetList[ 29 ].IsMolten();

		yield return GoIntoOrbit( 7, 29 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );

		var landed = done[ 0 ];

		playerData.m_terrainVehicle.AddElement( gameData.FindElementId( "Iron" ), 50 );

		var carriedBefore = playerData.m_terrainVehicle.m_volumeUsed;
		var lava = FindPlaceUnderTheLiquid();

		// from solid ground (the ship may have landed by the lava, and only entering it destroys the vehicle)
		playerData.m_general.m_coordinates = FindPlaceByTheLiquid( false );

		yield return Frames( 5 );

		var onSolidGround = playerData.m_general.m_location == PD_General.Location.Disembarked;

		controller.m_messages.Clear();

		// into the lava: the warning, and out of it again in time
		playerData.m_general.m_coordinates = lava;

		yield return Frames( 5 );

		var warning = MessageList();

		playerData.m_general.m_coordinates = FindPlaceByTheLiquid( false );

		yield return new WaitForSecondsRealtime( 4.0f );

		var savedByLeaving = ( playerData.m_general.m_location == PD_General.Location.Disembarked ) && !TerrainVehicleIsLost();

		// into it again, and staying there
		controller.m_messages.Clear();

		playerData.m_general.m_coordinates = lava;

		yield return new WaitForSecondsRealtime( 4.0f );

		var afterLava = playerData.m_general.m_location;
		var lostInLava = TerrainVehicleIsLost();
		var carriedAfter = playerData.m_terrainVehicle.m_volumeUsed;
		var lavaMessage = MessageList();

		// ---- 3. Disembark finds no terrain vehicle
		controller.m_messages.Clear();

		new DisembarkButton().Execute();

		yield return Frames( 3 );

		var disembarkMessage = MessageList();
		var afterDisembark = playerData.m_general.m_location;

		// ---- 4. at the Starport: a new terrain vehicle, the fine and Interstel's evaluation
		var balanceBefore = playerData.m_bank.m_currentBalance;

		controller.SwitchLocation( PD_General.Location.DockingBay );

		yield return Frames( 10 );

		var balanceAfter = playerData.m_bank.m_currentBalance;
		var lostAfterDocking = TerrainVehicleIsLost();
		var evaluations = FieldOrNull( playerData.m_general, "m_supplementalEvaluationList" ) as List<string>;
		var evaluated = ( evaluations != null ) && evaluations.Exists( evaluation => evaluation.Contains( "LOSS OF TERRAIN VEHICLE" ) );

		controller.m_encounterRange = encounterRange;

		Log( "lava: water at " + water + " -> " + afterWater + ", lost " + lostInWater + " || planet 29 molten " + molten + ", landed " + landed + ", on solid ground " + onSolidGround + ", warning " + warning + ", saved by leaving " + savedByLeaving + ", lava at " + lava + " -> " + afterLava + ", lost " + lostInLava + ", carried " + carriedBefore + " -> " + carriedAfter + " | " + lavaMessage + " || disembark: " + disembarkMessage + " (" + afterDisembark + ") || balance " + balanceBefore + " -> " + balanceAfter + ", lost after docking " + lostAfterDocking + ", evaluated " + evaluated );

		Check( "lava: the terrain vehicle driven into water floats (control)", ( water != Vector3.zero ) && ( afterWater == PD_General.Location.Disembarked ) && !lostInWater, water + " " + afterWater + " " + lostInWater );
		Check( "lava: the heat of the lava gives a warning first, and leaving it in time saves the vehicle", warning.Contains( "we're burning up" ) && savedByLeaving, savedByLeaving + " | " + warning );
		Check( "lava: staying in the lava of a molten planet it is destroyed with its cargo, and the crew is back in the ship", landed && onSolidGround && ( lava != Vector3.zero ) && lostInLava && ( carriedAfter == 0 ) && ( afterLava == PD_General.Location.Planetside ) && lavaMessage.Contains( "destroyed by lava" ), lava + " " + afterLava + " " + lostInLava + " " + carriedAfter + " | " + lavaMessage );
		Check( "lava: with the terrain vehicle lost there is no disembarking", disembarkMessage.Contains( "no terrain vehicle" ) && ( afterDisembark == PD_General.Location.Planetside ), afterDisembark + " | " + disembarkMessage );
		Check( "lava: at the Starport a new terrain vehicle is installed for a fine of 10,000 MU, with Interstel's evaluation", !lostAfterDocking && ( balanceBefore - balanceAfter == 10000 ) && evaluated, ( balanceBefore - balanceAfter ) + " " + lostAfterDocking + " " + evaluated );

		Finish( "scenario=lava lost=" + lostInLava + " fine=" + ( balanceBefore - balanceAfter ), 0 );
	}

	// ---------------------------------------------------------------- phase 7: the names of the aliens' captains and ships

	// meets the aliens of an encounter and has them say a line of the game data - returns what the messages show
	static IEnumerator SayLine( int encounterId, int commId, List<string> said )
	{
		var controller = SpaceflightController.m_instance;
		GD_Comm line = null;

		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( comm.m_id == commId )
			{
				line = comm;
			}
		}

		EnterEncounter( encounterId );
		yield return Frames( 10 );

		for ( var i = 0; i < 2; i++ )
		{
			controller.m_messages.Clear();

			controller.m_encounter.AddComm( GD_Comm.Subject.Custom, false, line );

			said.Add( MessageList() );
		}

		LeaveEncounter();
		yield return Frames( 10 );
	}

	// the word after this one in a text ("" if there is none)
	static string WordAfter( string text, string word )
	{
		var index = text.IndexOf( word );

		if ( index < 0 )
		{
			return "";
		}

		var rest = text.Substring( index + word.Length );
		var end = rest.IndexOfAny( new[] { ' ', '.', ',', '<' } );

		return ( end < 0 ) ? rest : rest.Substring( 0, end );
	}

	IEnumerator ScenarioAlienNames()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// an officer who understands every word
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer ).m_communications = 250;

		var encounterRange = controller.m_encounterRange;

		controller.m_encounterRange = 0.0f;

		// ---- 1. the Veloxi's "Me is naming captain % of most elegant and dangerous Veloxi ship naming +", twice
		var velox = new List<string>();

		yield return SayLine( FindEncounter( 1, 6, 3, 0, GameData.Race.Velox ), 496, velox );

		// ---- 2. the Thrynn's "This is captain % of the Thrynn ship +"
		var thrynn = new List<string>();

		yield return SayLine( FindEncounter( 1, 6, 3, 0, GameData.Race.Thrynn ), 391, thrynn );

		controller.m_encounterRange = encounterRange;

		var veloxCaptain = WordAfter( velox[ 0 ], "captain " );
		var veloxShip = WordAfter( velox[ 0 ], "ship naming " );
		var thrynnCaptain = WordAfter( thrynn[ 0 ], "captain " );

		Log( "aliennames: veloxi " + string.Join( " | ", velox ) + " (captain " + veloxCaptain + ", ship " + veloxShip + ") || thrynn " + thrynn[ 0 ] + " (captain " + thrynnCaptain + ")" );

		Check( "aliennames: the Veloxi name their captain and ship instead of % and +", !velox[ 0 ].Contains( "%" ) && !velox[ 0 ].Contains( "+" ) && ( veloxCaptain.Length >= 2 ) && char.IsUpper( veloxCaptain[ 0 ] ) && ( veloxShip.Length >= 2 ), velox[ 0 ] );
		Check( "aliennames: the names are the same every time", velox[ 0 ] == velox[ 1 ], velox[ 0 ] + " | " + velox[ 1 ] );
		Check( "aliennames: the Thrynn name theirs too", !thrynn[ 0 ].Contains( "%" ) && !thrynn[ 0 ].Contains( "+" ) && ( thrynnCaptain.Length >= 2 ), thrynn[ 0 ] );

		Finish( "scenario=aliennames veloxi=" + veloxCaptain + "/" + veloxShip + " thrynn=" + thrynnCaptain, 0 );
	}

	// ---------------------------------------------------------------- phase 6: the lifeforms of a planet

	// the lifeforms on the ground (none on the code before lifeforms)
	static List<Transform> LifeformsOnTheGround()
	{
		var lifeforms = new List<Transform>();
		var populator = FieldOrNull( SpaceflightController.m_instance.m_disembarked.m_terrainGrid, "m_terrainLifeforms" ) as Component;

		if ( populator != null )
		{
			foreach ( Transform child in populator.transform )
			{
				lifeforms.Add( child );
			}
		}

		return lifeforms;
	}

	// lands on a planet of the Arth system at this place (world x and z), and gets into the terrain vehicle
	static IEnumerator LandInTheArthSystemAt( int planetId, Vector3 place, bool[] done )
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		// (the port's "latitude" is the east-west coordinate - see Tools.LatLongToWorldCoordinates)
		Tools.WorldToLatLongCoordinates( place, out var x, out var z );

		playerData.m_general.m_selectedLatitude = x;
		playerData.m_general.m_selectedLongitude = z;

		yield return GoIntoOrbit( 25, planetId );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );
	}

	// the places of the lifeforms on the ground, rounded
	static string LifeformPlaces( List<Transform> lifeforms )
	{
		var places = "";

		foreach ( var lifeform in lifeforms )
		{
			places += Mathf.RoundToInt( lifeform.position.x ) + "/" + Mathf.RoundToInt( lifeform.position.z ) + " ";
		}

		return places.Trim();
	}

	IEnumerator ScenarioLifeforms()
	{
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- 1. the control: planet 90 of the Arth system has no life (bio density 0)
		yield return LandInTheArthSystemAt( 90, Vector3.zero, done );

		var on90 = LifeformsOnTheGround().Count;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		// ---- 2. planet 92 (bio density 7): its lifeforms, on dry ground near the equator
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var lifeforms = LifeformsOnTheGround();
		var placesFirst = LifeformPlaces( lifeforms );
		var allDryAndTemperate = lifeforms.Count > 0;
		var planetGenerator = FieldOrNull( controller.m_terrainVehicle, "m_planetGenerator" ) as PlanetGenerator;

		foreach ( var lifeform in lifeforms )
		{
			Tools.WorldToMapCoordinates( lifeform.position, out var mapX, out var mapY, planetGenerator.m_textureMapWidth, planetGenerator.m_textureMapHeight );

			var dry = planetGenerator.GetBilinearSmoothedElevation( mapX, mapY ) >= planetGenerator.m_waterElevation;
			var temperate = ( mapY >= planetGenerator.m_textureMapHeight * 0.35f - 1.0f ) && ( mapY <= planetGenerator.m_textureMapHeight * 0.65f + 1.0f );

			allDryAndTemperate = allDryAndTemperate && dry && temperate;
		}

		// ---- 3. landed beside the first one, Scan reads it
		var firstPlace = ( lifeforms.Count > 0 ) ? lifeforms[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		var placesAgain = LifeformPlaces( LifeformsOnTheGround() );

		controller.m_messages.Clear();

		new ScanButton().Execute();

		yield return Frames( 2 );

		var scan = MessageList();

		Log( "lifeforms: planet 90 has " + on90 + " | planet 92 has " + lifeforms.Count + " at " + placesFirst + " (all dry and temperate " + allDryAndTemperate + ") | again at " + placesAgain + " | scan: " + scan );

		Check( "lifeforms: a planet with no life has no lifeforms (control)", on90 == 0, on90 + " lifeforms" );
		Check( "lifeforms: a planet with life has its lifeforms, 3 to 8 of each species, on dry ground near the equator", ( lifeforms.Count >= 3 ) && ( lifeforms.Count <= 8 * 6 ) && allDryAndTemperate, lifeforms.Count + " lifeforms, all dry and temperate " + allDryAndTemperate );
		Check( "lifeforms: they are at the same places on every visit", ( placesFirst != "" ) && ( placesFirst == placesAgain ), placesFirst + " | " + placesAgain );
		Check( "lifeforms: Scan beside one reads it as the notes' scan has it", scan.Contains( "Lifeform" ) && scan.Contains( "Aggression analog" ) && scan.Contains( "Intelligence analog" ) && scan.Contains( "Niche" ) && scan.Contains( "Composition" ), scan );

		Finish( "scenario=lifeforms on92=" + lifeforms.Count, 0 );
	}

	// ---------------------------------------------------------------- phase 6: the terrain vehicle's laser and stunner

	// a ship button by its class name, or null on the code before it (so the scenario compiles there)
	static ShipButton MakeButton( string className )
	{
		var type = typeof( ShipButton ).Assembly.GetType( className );

		return ( type == null ) ? null : Activator.CreateInstance( type ) as ShipButton;
	}

	// presses a button made by its class name - returns what the messages show ("missing" if there is no such button)
	static string Press( string className )
	{
		var button = MakeButton( className );

		if ( button == null )
		{
			return "missing";
		}

		SpaceflightController.m_instance.m_messages.Clear();

		button.Execute();

		return MessageList();
	}

	// a field of a lifeform's component as text ("" if there is none)
	static string LifeformField( Transform lifeform, string name )
	{
		var value = FieldOrNull( lifeform.GetComponent( "TerrainLifeform" ), name );

		return ( value == null ) ? "" : value.ToString();
	}

	IEnumerator ScenarioTVWeapons()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system: landed beside its first lifeform
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var lifeforms = LifeformsOnTheGround();
		var firstPlace = ( lifeforms.Count > 0 ) ? lifeforms[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		lifeforms = LifeformsOnTheGround();

		Transform target = null;

		foreach ( var lifeform in lifeforms )
		{
			if ( ( target == null ) || ( Vector3.Distance( lifeform.position, controller.m_terrainVehicle.transform.position ) < Vector3.Distance( target.position, controller.m_terrainVehicle.transform.position ) ) )
			{
				target = lifeform;
			}
		}

		// ---- 1. Weapon: the weapon buttons
		new WeaponButton().Execute();

		yield return Frames( 2 );

		var weaponSet = controller.m_buttonController.GetCurrentButtonSet().ToString();

		// ---- 2. the stunner: what the species record says it does
		var switchText = Press( "TVSwitchWeaponButton" );
		var stunsBefore = ( target == null ) ? "" : LifeformField( target, "m_stuns" );
		var stunText = Press( "TVFireButton" );
		var stunsAfter = ( target == null ) ? "" : LifeformField( target, "m_stuns" );
		var statusAfterStun = ( target == null ) ? "" : LifeformField( target, "m_status" );
		var species = ( target == null ) ? null : FieldOrNull( target.GetComponent( "TerrainLifeform" ), "m_species" );
		var stunEffect = ( species == null ) ? "" : FieldOrNull( species, "m_stunEffect" ).ToString();

		// ---- 3. the laser: 8 units of energy a shot, 20 to 100 hit points, until it is dead
		Press( "TVSwitchWeaponButton" );

		var hitsBefore = ( target == null ) ? 0 : int.Parse( "0" + LifeformField( target, "m_hits" ) );
		var fuelBefore = playerData.m_terrainVehicle.m_fuelRemaining;
		var laserText = Press( "TVFireButton" );
		var fuelAfter = playerData.m_terrainVehicle.m_fuelRemaining;
		var hitsAfter = ( target == null ) ? 0 : int.Parse( "0" + LifeformField( target, "m_hits" ) );
		var lastText = laserText;

		for ( var shot = 0; ( shot < 20 ) && ( target != null ) && ( LifeformField( target, "m_status" ) != "Dead" ); shot++ )
		{
			lastText = Press( "TVFireButton" );
		}

		var statusAtTheEnd = ( target == null ) ? "" : LifeformField( target, "m_status" );

		// ---- 4. nothing left within range: the laser hits nothing
		var nothingText = Press( "TVFireButton" );

		Log( "tvweapons: weapon set " + weaponSet + " | switch: " + switchText + " | stunner (effect " + stunEffect + "): " + stunText + " (stuns " + stunsBefore + " -> " + stunsAfter + ", " + statusAfterStun + ") | laser: " + laserText + " (hits " + hitsBefore + " -> " + hitsAfter + ", energy " + fuelBefore + " -> " + fuelAfter + ") | last: " + lastText + " (" + statusAtTheEnd + ") | then: " + nothingText );

		var stunAsRecorded = ( stunEffect == "Unaffected" ) ? stunText.Contains( "no effect" ) : ( ( stunEffect == "Killed" ) ? stunText.Contains( "kills" ) : ( stunText.Contains( "stunned" ) || ( stunsAfter != stunsBefore ) || ( statusAfterStun == "Dead" ) ) );

		Check( "tvweapons: Weapon shows the weapon buttons, with the laser mounted", ( weaponSet == "TerrainVehicleWeapon" ) && switchText.Contains( "stunner is mounted" ), weaponSet + " | " + switchText );
		Check( "tvweapons: the stunner does what the species record says", ( target != null ) && ( stunEffect != "" ) && stunAsRecorded, stunEffect + " | " + stunText );
		Check( "tvweapons: a laser shot costs 8 units of energy and does 20 to 100 points of damage", ( Mathf.Abs( fuelBefore - fuelAfter - 8.0f / 999.0f ) < 0.0001f ) && ( ( statusAfterStun == "Dead" ) || ( ( hitsBefore - hitsAfter >= 20 ) && ( hitsBefore - hitsAfter <= 100 ) ) || ( hitsAfter == 0 ) ), ( fuelBefore - fuelAfter ) + ", " + hitsBefore + " -> " + hitsAfter );
		Check( "tvweapons: the laser kills it", statusAtTheEnd == "Dead", statusAtTheEnd + " | " + lastText );
		Check( "tvweapons: with no living lifeform within range the laser hits nothing (control)", nothingText.Contains( "hits nothing" ), nothingText );

		Finish( "scenario=tvweapons stun=" + stunEffect + " end=" + statusAtTheEnd, 0 );
	}

	// ---------------------------------------------------------------- phase 6: how the lifeforms respond to the terrain vehicle

	// sets a field of a lifeform's component or of its species (nothing on the code before them)
	static void SetLifeformField( Transform lifeform, string name, object value, bool onSpecies = false )
	{
		var component = lifeform.GetComponent( "TerrainLifeform" );
		var target = onSpecies ? FieldOrNull( component, "m_species" ) : component;

		if ( target == null )
		{
			return;
		}

		var field = target.GetType().GetField( name );

		if ( field != null )
		{
			field.SetValue( target, value );
		}
	}

	// the horizontal distance between a lifeform and the terrain vehicle
	static float DistanceToVehicle( Transform lifeform )
	{
		var delta = lifeform.position - SpaceflightController.m_instance.m_terrainVehicle.transform.position;

		delta.y = 0.0f;

		return delta.magnitude;
	}

	// puts a lifeform this far from the terrain vehicle, on the far side from where it is
	static void PutAt( Transform lifeform, float distance )
	{
		var vehicle = SpaceflightController.m_instance.m_terrainVehicle.transform.position;
		var direction = lifeform.position - vehicle;

		direction.y = 0.0f;

		if ( direction.magnitude < 0.01f )
		{
			direction = Vector3.forward;
		}

		lifeform.position = vehicle + direction.normalized * distance + Vector3.up * 2.0f;
	}

	IEnumerator ScenarioLifeBehaviour()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system: landed beside its first lifeform (a plant)
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		Transform lifeform = null;

		foreach ( var candidate in LifeformsOnTheGround() )
		{
			if ( ( lifeform == null ) || ( DistanceToVehicle( candidate ) < DistanceToVehicle( lifeform ) ) )
			{
				lifeform = candidate;
			}
		}

		if ( lifeform == null )
		{
			Finish( "scenario=lifebehaviour abort: no lifeform", 2 );
			yield break;
		}

		var crewMember = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Captain );

		// ---- 1. the control: a plant of middling aggression next to the vehicle neither moves nor attacks
		SetLifeformField( lifeform, "m_aggression", 50 );
		SetLifeformField( lifeform, "m_noticedPlayer", true );
		PutAt( lifeform, 8.0f );

		var placeBefore = lifeform.position;

		controller.m_messages.Clear();

		yield return new WaitForSecondsRealtime( 2.0f );

		var calmMoved = Vector3.Distance( placeBefore, lifeform.position );
		var calmAttacked = MessageList().Contains( "being attacked" );

		// ---- 2. made dangerous, it attacks the crew
		crewMember.m_vitality = 100.0f;

		SetLifeformField( lifeform, "m_aggression", 95 );

		controller.m_messages.Clear();

		yield return new WaitForSecondsRealtime( 4.0f );

		var attackMessages = MessageList();
		var vitalityAfter = crewMember.m_vitality;

		crewMember.m_vitality = 100.0f;

		// ---- 3. made to move, and dangerous, 40 units away: it comes at the vehicle
		SetLifeformField( lifeform, "m_movementRate", 3, true );
		PutAt( lifeform, 40.0f );

		var approachBefore = DistanceToVehicle( lifeform );

		yield return new WaitForSecondsRealtime( 2.0f );

		var approachAfter = DistanceToVehicle( lifeform );

		// ---- 4. made wimpy: it flees
		SetLifeformField( lifeform, "m_aggression", 10 );
		PutAt( lifeform, 30.0f );

		var fleeBefore = DistanceToVehicle( lifeform );

		yield return new WaitForSecondsRealtime( 2.0f );

		var fleeAfter = DistanceToVehicle( lifeform );

		Log( "lifebehaviour: calm moved " + calmMoved + ", attacked " + calmAttacked + " | dangerous: " + attackMessages + " (vitality " + vitalityAfter + ") | approach " + approachBefore + " -> " + approachAfter + " | flee " + fleeBefore + " -> " + fleeAfter );

		Check( "lifebehaviour: a plant of middling aggression neither moves nor attacks (control)", ( calmMoved < 0.5f ) && !calmAttacked, calmMoved + ", " + calmAttacked );
		Check( "lifebehaviour: a dangerous lifeform next to the vehicle attacks the crew", attackMessages.Contains( "being attacked" ) && attackMessages.Contains( "lifeform attacks" ), attackMessages );
		Check( "lifebehaviour: a dangerous lifeform that moves comes at the vehicle", approachAfter < approachBefore - 5.0f, approachBefore + " -> " + approachAfter );
		Check( "lifebehaviour: a wimpy one flees it", fleeAfter > fleeBefore + 5.0f, fleeBefore + " -> " + fleeAfter );

		Finish( "scenario=lifebehaviour approach=" + approachBefore + "/" + approachAfter + " flee=" + fleeBefore + "/" + fleeAfter, 0 );
	}

	// ---------------------------------------------------------------- phase 6: the crew's deaths

	// whether a crew member is dead ("missing" on the code before deaths)
	static string IsDead( PD_Personnel.PD_PersonnelFile file )
	{
		var value = FieldOrNull( file, "m_isDead" );

		return ( value == null ) ? "missing" : value.ToString();
	}

	IEnumerator ScenarioCrewDeath()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var crewAssignment = playerData.m_crewAssignment;
		var done = new bool[ 1 ];

		// a crew of two: Ada the captain, and Ben everything else
		var ada = AddPerson( "Ada", 100.0f, 100 );
		var ben = AddPerson( "Ben", 100.0f, 100 );

		crewAssignment.Assign( PD_CrewAssignment.Role.Captain, ada.m_fileId );

		for ( var role = PD_CrewAssignment.Role.ScienceOfficer; role < PD_CrewAssignment.Role.Count; role++ )
		{
			crewAssignment.Assign( role, ben.m_fileId );
		}

		// planet 92 of the Arth system, landed beside its first lifeform
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		// ---- 1. the captain dies of an injury: the next in the chain of command takes command
		var injure = typeof( PD_CrewAssignment ).GetMethod( "InjureCrewMember" );

		controller.m_messages.Clear();

		if ( injure != null )
		{
			injure.Invoke( crewAssignment, new object[] { ada, 200.0f } );
		}

		var deathMessages = MessageList();
		var adaDead = IsDead( ada );
		var captainAfter = crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Captain ).m_name;

		// ---- 2. the last of the crew, at 1 vitality, next to a dangerous lifeform: its attacks kill him, and the game is lost
		Transform lifeform = null;

		foreach ( var candidate in LifeformsOnTheGround() )
		{
			if ( ( lifeform == null ) || ( DistanceToVehicle( candidate ) < DistanceToVehicle( lifeform ) ) )
			{
				lifeform = candidate;
			}
		}

		ben.m_vitality = 1.0f;

		if ( lifeform != null )
		{
			SetLifeformField( lifeform, "m_aggression", 100 );
			SetLifeformField( lifeform, "m_noticedPlayer", true );
			PutAt( lifeform, 8.0f );
		}

		controller.m_messages.Clear();

		var end = Time.realtimeSinceStartup + 20.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !controller.m_gameOver )
		{
			yield return null;
		}

		var gameOverText = MessageList();

		Log( "crewdeath: injury: " + deathMessages + " (Ada dead " + adaDead + ", captain " + captainAfter + ") || the last: Ben dead " + IsDead( ben ) + ", vitality " + ben.m_vitality + ", game over " + controller.m_gameOver + ", armor " + playerData.m_playerShip.m_armorPoints + " | " + gameOverText );

		Check( "crewdeath: a captain who dies of an injury is dead, and the next in the chain of command takes command", ( adaDead == "True" ) && deathMessages.Contains( "Ada is dead" ) && ( captainAfter == "Ben" ) && deathMessages.Contains( "Ben takes command" ), adaDead + ", " + captainAfter + " | " + deathMessages );
		Check( "crewdeath: when the last of the crew dies the game is lost, and it cannot be saved", ( IsDead( ben ) == "True" ) && controller.m_gameOver && gameOverText.Contains( "The entire crew has died" ) && ( playerData.m_playerShip.m_armorPoints <= 0 ), IsDead( ben ) + ", " + controller.m_gameOver + ", " + playerData.m_playerShip.m_armorPoints + " | " + gameOverText );

		Finish( "scenario=crewdeath captain=" + captainAfter + " gameover=" + controller.m_gameOver, 0 );
	}

	// ---------------------------------------------------------------- phase 6: the lifeforms' mates

	// an animal species for a test lifeform (a flyer, so that the ground does not turn it)
	static GD_Species MakeTestAnimal( GD_Species.Niche niche, int size )
	{
		return new GD_Species { m_index = 7, m_bodyPlan = GD_Species.BodyPlan.BilateralFourLegs, m_niche = niche, m_size = size, m_volume = size * size, m_hitPoints = 100, m_aggression = 50, m_intelligence = 50, m_stunPoints = 50, m_movementRate = 3, m_canFly = true, m_red = 1.0f, m_green = 0.5f, m_blue = 0.2f };
	}

	// a lifeform's behaviour towards its mate ("missing" on the code before mates)
	static string MateBehaviour( TerrainLifeform lifeform )
	{
		var value = ( lifeform == null ) ? null : FieldOrNull( lifeform, "m_behaviour" );

		return ( value == null ) ? "missing" : value.ToString();
	}

	// the horizontal distance between two places
	static float Flat( Vector3 a, Vector3 b )
	{
		a.y = 0.0f;
		b.y = 0.0f;

		return Vector3.Distance( a, b );
	}

	IEnumerator ScenarioMates()
	{
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system, landed beside its first lifeform
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		var all = LifeformsOnTheGround();

		if ( all.Count < 3 )
		{
			Finish( "scenario=mates abort: " + all.Count + " lifeforms", 2 );
			yield break;
		}

		var vehicle = controller.m_terrainVehicle.transform.position;
		var plant = all[ 0 ].GetComponent<TerrainLifeform>();
		var grazer = all[ 1 ].GetComponent<TerrainLifeform>();
		var prey = all[ 2 ].GetComponent<TerrainLifeform>();

		// everything but the plant and the grazer far outside the encounter area
		for ( var i = 2; i < all.Count; i++ )
		{
			all[ i ].position = vehicle + Vector3.right * 600.0f + Vector3.forward * ( i * 10.0f );
		}

		// the plant, 30 units from the vehicle, quick to kill
		plant.transform.position = new Vector3( vehicle.x + 30.0f, plant.transform.position.y, vehicle.z );
		plant.m_hits = 40;

		// ---- 1. a grazer 30 units from the plant, on the side its own wandering would take it away from: it comes at the plant
		grazer.m_species = MakeTestAnimal( GD_Species.Niche.Herbivore, 4 );
		grazer.m_aggression = 50;
		grazer.m_hits = 100;

		var wander = FieldOrNull( grazer, "m_moveDirection" );
		var away = ( wander is Vector3 ) ? (Vector3) wander : Vector3.forward;

		grazer.transform.position = plant.transform.position + away * 30.0f;

		var startDistance = Flat( grazer.transform.position, plant.transform.position );
		var closest = startDistance;
		var behaviours = "";
		var end = Time.realtimeSinceStartup + 8.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			closest = Mathf.Min( closest, Flat( grazer.transform.position, plant.transform.position ) );

			var behaviour = MateBehaviour( grazer );

			if ( !behaviours.Contains( behaviour ) )
			{
				behaviours += behaviour + " ";
			}

			if ( closest <= 12.0f )
			{
				break;
			}

			yield return null;
		}

		// ---- 2. next to it, the grazer bites the plant and in time eats it (the plant is gone)
		var plantHitsLowest = plant.m_hits;
		var plantDied = false;

		end = Time.realtimeSinceStartup + 45.0f;

		while ( ( Time.realtimeSinceStartup < end ) && ( plant != null ) )
		{
			plantHitsLowest = Mathf.Min( plantHitsLowest, plant.m_hits );
			plantDied |= plant.m_status == TerrainLifeform.Status.Dead;

			var behaviour = MateBehaviour( grazer );

			if ( !behaviours.Contains( behaviour ) )
			{
				behaviours += behaviour + " ";
			}

			yield return null;
		}

		var plantEaten = plant == null;

		// ---- 3. the grazer made a large carnivore, and a small herbivore 25 units away, on the side its own wandering would take it towards the hunter: it flees
		grazer.m_species = MakeTestAnimal( GD_Species.Niche.Carnivore, 6 );
		prey.m_species = MakeTestAnimal( GD_Species.Niche.Herbivore, 2 );
		prey.m_aggression = 50;
		prey.m_hits = 100;

		var preyWander = FieldOrNull( prey, "m_moveDirection" );
		var towards = ( preyWander is Vector3 ) ? (Vector3) preyWander : Vector3.forward;
		var hunterStart = grazer.transform.position;

		prey.transform.position = hunterStart - towards * 25.0f;

		var preyStart = prey.transform.position;
		var preyBehaviours = "";
		var hunterBehaviours = "";

		// the prey decides only once the hunter is coming at it: every lifeform decides on the same frame, in an order Unity does not fix, and the hunter may
		// unpair at random as it pairs; a prey that decided first saw nothing coming, wandered into the air, and a flyer is never approached again (it never
		// lands), so about one run in ten failed
		SetField( prey, "m_flying", false );
		SetField( prey, "m_moving", false );

		end = Time.realtimeSinceStartup + 3.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !( ( MateBehaviour( grazer ) == "ApproachingMate" ) && ( ( FieldOrNull( grazer, "m_mate" ) as TerrainLifeform ) == prey ) ) )
		{
			SetField( prey, "m_behaviourTimer", 0.5f );

			yield return null;
		}

		hunterBehaviours = MateBehaviour( grazer ) + " ";

		SetField( prey, "m_behaviourTimer", 0.0f );

		end = Time.realtimeSinceStartup + 3.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			var behaviour = MateBehaviour( prey );

			if ( !preyBehaviours.Contains( behaviour ) )
			{
				preyBehaviours += behaviour + " ";
			}

			behaviour = MateBehaviour( grazer );

			if ( !hunterBehaviours.Contains( behaviour ) )
			{
				hunterBehaviours += behaviour + " ";
			}

			yield return null;
		}

		// how far the prey went away from where the hunter was
		var awayFromHunter = Vector3.Dot( prey.transform.position - preyStart, ( preyStart - hunterStart ).normalized );

		Log( "mates: grazer " + startDistance + " -> closest " + closest + ", behaviours [" + behaviours + "], plant hits lowest " + plantHitsLowest + ", died " + plantDied + ", eaten " + plantEaten + " || prey [" + preyBehaviours + "] hunter [" + hunterBehaviours + "], prey went " + awayFromHunter + " away from the hunter" );

		Check( "mates: a grazer comes at the plant it is paired with", ( closest <= 12.0f ) && behaviours.Contains( "ApproachingMate" ), startDistance + " -> " + closest + " | " + behaviours );
		Check( "mates: next to it, the grazer bites the plant", ( plantHitsLowest < 40 ) && behaviours.Contains( "AttackingMate" ), plantHitsLowest + " | " + behaviours );
		Check( "mates: in time the grazer eats the plant, which is gone", plantEaten, plantEaten + ", died " + plantDied );
		Check( "mates: a small herbivore flees the carnivore that comes at it", preyBehaviours.Contains( "FleeingMate" ) && hunterBehaviours.Contains( "ApproachingMate" ) && ( awayFromHunter > 5.0f ), preyBehaviours + " | " + hunterBehaviours + " | " + awayFromHunter );

		Finish( "scenario=mates closest=" + closest + " eaten=" + plantEaten + " fled=" + awayFromHunter, 0 );
	}

	// ---------------------------------------------------------------- phase 6: recording the lifeforms' data

	// the lifeform data not paid for yet (-1 on the code before it)
	static int UnpaidLifeformData()
	{
		var value = FieldOrNull( DataController.m_instance.m_playerData.m_general, "m_unpaidLifeformDataValue" );

		return ( value == null ) ? -1 : (int) value;
	}

	// what a species' data is worth from this base (-1 on the code before it)
	static int DataValue( GD_Species species, int baseValue )
	{
		var method = typeof( GD_Species ).GetMethod( "GetDataValue" );

		return ( method == null ) ? -1 : (int) method.Invoke( species, new object[] { baseValue } );
	}

	IEnumerator ScenarioLifeformData()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system, landed beside its first lifeform
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		var all = LifeformsOnTheGround();

		if ( all.Count < 2 )
		{
			Finish( "scenario=lifeformdata abort: " + all.Count + " lifeforms", 2 );
			yield break;
		}

		var vehicle = controller.m_terrainVehicle.transform.position;
		var plant = all[ 0 ].GetComponent<TerrainLifeform>();
		var runner = all[ 1 ].GetComponent<TerrainLifeform>();

		// everything but the plant far away
		for ( var i = 1; i < all.Count; i++ )
		{
			all[ i ].position = vehicle + Vector3.right * 600.0f + Vector3.forward * ( i * 10.0f );
		}

		// the plant beside the vehicle
		plant.transform.position = new Vector3( vehicle.x + 6.0f, plant.transform.position.y, vehicle.z );

		yield return Frames( 2 );

		// ---- 1. Cargo beside a living plant offers it, and Record records its data: a new class, worth 50 and its bonus, to be paid at the Starport
		var unpaidBefore = UnpaidLifeformData();
		var offer = Press( "TVCargoButton" );
		var buttons = controller.m_buttonController.GetCurrentButtonSet().ToString();
		var record = Press( "TVRecordButton" );

		yield return new WaitForSecondsRealtime( 4.0f );

		var recorded = MessageList();
		var unpaidAfterFirst = UnpaidLifeformData();
		var expected = DataValue( plant.m_species, 50 );

		// ---- 2. the same species again is worth nothing
		Press( "TVCargoButton" );
		Press( "TVRecordButton" );

		yield return new WaitForSecondsRealtime( 4.0f );

		var unpaidAfterSecond = UnpaidLifeformData();

		// ---- 3. a lifeform that wanders off before the recording is done got away
		plant.transform.position = vehicle + Vector3.left * 600.0f;
		runner.m_species = MakeTestAnimal( GD_Species.Niche.Herbivore, 3 );
		runner.m_aggression = 50;
		runner.transform.position = new Vector3( vehicle.x + 6.0f, vehicle.y, vehicle.z );

		yield return Frames( 2 );

		Press( "TVCargoButton" );
		Press( "TVRecordButton" );

		var end = Time.realtimeSinceStartup + 5.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !MessageList().Contains( "got away" ) )
		{
			yield return null;
		}

		var gotAway = MessageList();
		var unpaidAfterRunner = UnpaidLifeformData();

		// ---- 4. at the Starport Interstel pays for the data
		var balanceBefore = playerData.m_bank.m_currentBalance;

		controller.SwitchLocation( PD_General.Location.DockingBay );

		yield return Frames( 10 );

		var balanceAfter = playerData.m_bank.m_currentBalance;
		var docked = MessageList();
		var unpaidAfterDocking = UnpaidLifeformData();
		var ledger = playerData.m_bank.m_transactionList.Count > 0 ? playerData.m_bank.m_transactionList[ playerData.m_bank.m_transactionList.Count - 1 ].m_description : "";

		Log( "lifeformdata: offer " + offer + " [" + buttons + "] record " + record + " -> " + recorded + " | unpaid " + unpaidBefore + " -> " + unpaidAfterFirst + " (expected " + expected + ") -> " + unpaidAfterSecond + " -> " + unpaidAfterRunner + " | " + gotAway + " || balance " + balanceBefore + " -> " + balanceAfter + ", unpaid " + unpaidAfterDocking + ", ledger " + ledger + " | " + docked );

		Check( "lifeformdata: Cargo beside a lifeform offers it, and Record collects its data", offer.Contains( "A lifeform" ) && ( buttons == "TerrainVehicleLifeform" ) && recorded.Contains( "Data collection completed" ), offer + " [" + buttons + "] " + recorded );
		Check( "lifeformdata: the first of a class is worth 50 and its bonus", ( expected > 50 ) && ( unpaidAfterFirst == unpaidBefore + expected ), unpaidBefore + " -> " + unpaidAfterFirst + " (expected " + expected + ")" );
		Check( "lifeformdata: the same species again is worth nothing", ( unpaidAfterFirst > 0 ) && ( unpaidAfterSecond == unpaidAfterFirst ), unpaidAfterFirst + " -> " + unpaidAfterSecond );
		Check( "lifeformdata: a lifeform that wanders off before the recording is done got away", gotAway.Contains( "got away" ) && ( unpaidAfterRunner == unpaidAfterSecond ), gotAway + " | " + unpaidAfterRunner );
		Check( "lifeformdata: at the Starport Interstel pays for the data", ( unpaidAfterFirst > 0 ) && ( balanceAfter == balanceBefore + unpaidAfterRunner ) && ( unpaidAfterDocking == 0 ) && ( ledger == "Lifeform data" ), balanceBefore + " -> " + balanceAfter + ", unpaid " + unpaidAfterDocking + ", " + ledger );

		Finish( "scenario=lifeformdata value=" + unpaidAfterFirst + " paid=" + ( balanceAfter - balanceBefore ), 0 );
	}

	// ---------------------------------------------------------------- phase 6: capturing lifeforms

	// the specimens in a hold (-1 on the code before them)
	static int SpecimenCount( object hold )
	{
		var storage = FieldOrNull( hold, "m_specimenStorage" );
		var method = ( storage == null ) ? null : storage.GetType().GetMethod( "GetCount" );

		return ( method == null ) ? -1 : (int) method.Invoke( storage, null );
	}

	// a field of the first specimen in a hold ("" if there is none)
	static string FirstSpecimenField( object hold, string name )
	{
		var list = FieldOrNull( FieldOrNull( hold, "m_specimenStorage" ), "m_specimenList" ) as IList;

		return ( ( list == null ) || ( list.Count == 0 ) ) ? "" : ( FieldOrNull( list[ 0 ], name ) ?? "" ).ToString();
	}

	IEnumerator ScenarioCapture()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system, landed beside its first lifeform
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		var all = LifeformsOnTheGround();

		if ( all.Count < 2 )
		{
			Finish( "scenario=capture abort: " + all.Count + " lifeforms", 2 );
			yield break;
		}

		var vehicle = controller.m_terrainVehicle.transform.position;
		var plant = all[ 0 ].GetComponent<TerrainLifeform>();
		var corpse = all[ 1 ].GetComponent<TerrainLifeform>();

		// everything but the plant far away
		for ( var i = 1; i < all.Count; i++ )
		{
			all[ i ].position = vehicle + Vector3.right * 600.0f + Vector3.forward * ( i * 10.0f );
		}

		plant.transform.position = new Vector3( vehicle.x + 6.0f, plant.transform.position.y, vehicle.z );

		yield return Frames( 2 );

		// ---- 1. Cargo beside a living plant offers Capture, and the capture puts it in stasis in the terrain vehicle, worth five times its data
		var expected = DataValue( plant.m_species, 50 ) * 5;
		var offer = Press( "TVCargoButton" );
		var buttons = controller.m_buttonController.GetCurrentButtonSet().ToString();
		var labels = "";

		foreach ( var label in new[] { "TVCaptureButton", "TVRecordButton", "TVIgnoreButton" } )
		{
			var button = MakeButton( label );

			labels += ( ( button == null ) ? "missing" : button.GetLabel() ) + " ";
		}

		Press( "TVCaptureButton" );

		var end = Time.realtimeSinceStartup + 9.0f;

		while ( ( Time.realtimeSinceStartup < end ) && !MessageList().Contains( "stored as item" ) )
		{
			yield return null;
		}

		// (a destroyed object is gone at the end of the frame)
		yield return Frames( 1 );

		var captured = MessageList();
		var inVehicle = SpecimenCount( playerData.m_terrainVehicle );
		var value = FirstSpecimenField( playerData.m_terrainVehicle, "m_value" );
		var plantGone = plant == null;

		// ---- 2. Cargo beside a dead lifeform offers only Record
		corpse.m_status = TerrainLifeform.Status.Dead;
		corpse.transform.position = new Vector3( vehicle.x + 6.0f, corpse.transform.position.y, vehicle.z );

		yield return Frames( 2 );

		var deadOffer = Press( "TVCargoButton" );
		var deadButtons = controller.m_buttonController.GetCurrentButtonSet().ToString();

		Press( "TVIgnoreButton" );

		// ---- 3. back in the ship, the specimen goes into the ship's hold
		controller.m_planetside.TransferTerrainVehicleCargo();

		var inShip = SpecimenCount( playerData.m_playerShip );
		var leftInVehicle = SpecimenCount( playerData.m_terrainVehicle );

		Log( "capture: offer " + offer + " [" + buttons + "] (" + labels + ") -> " + captured + " | in vehicle " + inVehicle + ", value " + value + " (expected " + expected + "), plant gone " + plantGone + " || dead: " + deadOffer + " [" + deadButtons + "] || in ship " + inShip + ", left in vehicle " + leftInVehicle );

		Check( "capture: Cargo beside a living lifeform offers Capture, Record and Ignore", offer.Contains( "A lifeform" ) && ( buttons == "TerrainVehicleLifeform" ) && ( labels == "Capture Record Ignore " ), offer + " [" + buttons + "] " + labels );
		Check( "capture: the capture puts it in stasis in the terrain vehicle, off the surface", captured.Contains( "captured and put in stasis" ) && captured.Contains( "stored as item" ) && ( inVehicle == 1 ) && plantGone, inVehicle + ", gone " + plantGone + " | " + captured );
		Check( "capture: a captured specimen is worth five times its data", ( expected > 0 ) && ( value == expected.ToString() ), value + " (expected " + expected + ")" );
		Check( "capture: Cargo beside a dead lifeform offers only Record", deadOffer.Contains( "A dead lifeform" ) && ( deadButtons == "TerrainVehicleDeadLifeform" ), deadOffer + " [" + deadButtons + "]" );
		Check( "capture: back in the ship, the specimen goes into the ship's hold", ( inShip == 1 ) && ( leftInVehicle == 0 ), inShip + " / " + leftInVehicle );

		Finish( "scenario=capture inVehicle=" + inVehicle + " value=" + value + " inShip=" + inShip, 0 );
	}

	// ---------------------------------------------------------------- phase 6: specimens sold at the Trade Depot

	IEnumerator ScenarioStarportSpecimens()
	{
		var playerData = DataController.m_instance.m_playerData;
		var depot = FindPanel<TradeDepotPanel>();

		// a specimen in the ship's hold, worth 1234 MU
		var specimenType = typeof( PlayerData ).Assembly.GetType( "PD_Specimen" );
		var addSpecimen = typeof( PD_PlayerShip ).GetMethod( "AddSpecimen" );

		if ( ( specimenType != null ) && ( addSpecimen != null ) )
		{
			var specimen = Activator.CreateInstance( specimenType );

			SetField( specimen, "m_name", "Radial, many legs" );
			SetField( specimen, "m_volume", 16 );
			SetField( specimen, "m_value", 1234 );

			addSpecimen.Invoke( playerData.m_playerShip, new[] { specimen } );
		}

		var heldBefore = SpecimenCount( playerData.m_playerShip );
		var volumeBefore = playerData.m_playerShip.m_volumeUsed;

		yield return OpenPanel( depot, "trade depot" );

		// the sale list shows it, and selling it pays what it is worth
		Call( depot, "SwitchToSellItemState", true );

		var items = GetField( depot, "m_itemList" ) as IList;
		var index = -1;

		for ( var i = 0; i < items.Count; i++ )
		{
			if ( (int) GetField( items[ i ], "m_type" ) == 2 )
			{
				index = i;
			}
		}

		var listed = depot.m_itemListText.text;
		var balanceBefore = playerData.m_bank.m_currentBalance;

		if ( index >= 0 )
		{
			SetField( depot, "m_currentItemIndex", index );

			Call( depot, "SellSelectedItem" );
		}

		var paid = playerData.m_bank.m_currentBalance - balanceBefore;
		var heldAfter = SpecimenCount( playerData.m_playerShip );
		var volumeAfter = playerData.m_playerShip.m_volumeUsed;

		Call( depot, "SwitchToMenuBarState" );

		yield return ClosePanel( depot, "trade depot" );

		Log( "starport-specimens: held " + heldBefore + " (volume " + volumeBefore + "), listed at " + index + " | paid " + paid + ", held " + heldAfter + " (volume " + volumeAfter + ") | " + listed.Replace( "\n", "/" ) );

		Check( "starport-specimens: the Trade Depot lists a specimen in the ship's hold for sale", ( heldBefore == 1 ) && ( index >= 0 ) && listed.Contains( "Specimens" ) && listed.Contains( "Radial, many legs" ), heldBefore + ", " + index );
		Check( "starport-specimens: selling it pays its value and frees its room in the hold", ( paid == 1234 ) && ( heldAfter == 0 ) && ( volumeAfter == volumeBefore - 16 ), paid + ", " + heldAfter + ", " + volumeBefore + " -> " + volumeAfter );

		Finish( "scenario=starport-specimens paid=" + paid, 0 );
	}

	// ---------------------------------------------------------------- phase 6: Look describes the lifeforms

	IEnumerator ScenarioLook()
	{
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system, landed beside its first lifeform
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		var all = LifeformsOnTheGround();

		if ( all.Count < 2 )
		{
			Finish( "scenario=look abort: " + all.Count + " lifeforms", 2 );
			yield break;
		}

		var vehicle = controller.m_terrainVehicle.transform.position;
		var plant = all[ 0 ].GetComponent<TerrainLifeform>();
		var animal = all[ 1 ].GetComponent<TerrainLifeform>();

		// everything but the plant far away
		for ( var i = 1; i < all.Count; i++ )
		{
			all[ i ].position = vehicle + Vector3.right * 600.0f + Vector3.forward * ( i * 10.0f );
		}

		plant.transform.position = new Vector3( vehicle.x + 10.0f, plant.transform.position.y, vehicle.z );

		yield return Frames( 2 );

		// ---- 1. a living plant: its body plan, size and colour, that it is not moving, and that it is alive
		var plantLook = Press( "LookButton" );
		var bodyPlan = plant.m_species.GetBodyPlanText();

		// ---- 2. a wandering animal
		plant.transform.position = vehicle + Vector3.left * 600.0f;
		animal.m_species = MakeTestAnimal( GD_Species.Niche.Herbivore, 3 );
		animal.m_aggression = 50;
		animal.transform.position = new Vector3( vehicle.x + 15.0f, vehicle.y, vehicle.z );

		yield return new WaitForSecondsRealtime( 1.5f );

		var animalLook = Press( "LookButton" );

		// ---- 3. a dead lifeform
		animal.transform.position = vehicle + Vector3.left * 700.0f;
		plant.m_status = TerrainLifeform.Status.Dead;
		plant.transform.position = new Vector3( vehicle.x + 10.0f, plant.transform.position.y, vehicle.z );

		yield return Frames( 2 );

		var deadLook = Press( "LookButton" );

		Log( "look: plant (" + bodyPlan + ") " + plantLook + " || animal " + animalLook + " || dead " + deadLook );

		Check( "look: Look describes a living plant beside the vehicle: its body plan, that it is not moving, and that it is alive", plantLook.Contains( "A lifeform (" + bodyPlan + ")" ) && plantLook.Contains( "It is not moving" ) && plantLook.Contains( "Status: Alive" ), plantLook );
		Check( "look: Look says what a moving lifeform is doing", animalLook.Contains( "It is wandering" ), animalLook );
		Check( "look: Look says when a lifeform is dead", deadLook.Contains( "It does not move" ) && deadLook.Contains( "Status: Dead" ), deadLook );

		Finish( "scenario=look", 0 );
	}

	// ---------------------------------------------------------------- phase 6: the lifeform models

	// the bounds of everything drawn of an object (a zero box at its place if nothing is)
	static Bounds DrawnBounds( GameObject gameObject )
	{
		var bounds = new Bounds( gameObject.transform.position, Vector3.zero );
		var any = false;

		foreach ( var renderer in gameObject.GetComponentsInChildren<Renderer>() )
		{
			if ( any )
			{
				bounds.Encapsulate( renderer.bounds );
			}
			else
			{
				bounds = renderer.bounds;
				any = true;
			}
		}

		return bounds;
	}

	IEnumerator ScenarioLifeformModels()
	{
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		// ---- 1. every body plan has its model (two for each plant size): it fits a cube of one unit, and the snake lies along +Z
		var names = new[] { "Snakelike", "BilateralNoLegs", "BilateralOneLeg", "BilateralTwoLegs", "BilateralFourLegs", "BilateralManyLegs", "RadialNoLegs", "RadialFewLegs", "RadialManyLegs", "AmorphousNoLegs", "AmorphousLegs", "LeavedSmallA", "LeavedSmallB", "LeavedMediumA", "LeavedMediumB", "LeavedLargeA", "LeavedLargeB" };
		var found = 0;
		var fits = 0;
		var report = "";
		var snakeAlongZ = false;

		foreach ( var name in names )
		{
			var prefab = Resources.Load<GameObject>( "Lifeforms/" + name );

			if ( prefab == null )
			{
				report += name + " missing, ";
				continue;
			}

			found++;

			var instance = Instantiate( prefab, new Vector3( 0.0f, -5000.0f, 0.0f ), Quaternion.identity );
			var size = DrawnBounds( instance ).size;
			var largest = Mathf.Max( size.x, Mathf.Max( size.y, size.z ) );

			if ( ( largest > 0.9f ) && ( largest < 1.1f ) )
			{
				fits++;
			}
			else
			{
				report += name + " " + size + ", ";
			}

			if ( name == "Snakelike" )
			{
				snakeAlongZ = size.z > size.x * 1.5f;
			}

			Destroy( instance );
		}

		EnsureCrew();

		// ---- 2. on planet 92 the lifeforms are their models, their skin in the species colour, standing on the ground
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		yield return Frames( 2 );

		var all = LifeformsOnTheGround();
		var populator = FieldOrNull( controller.m_disembarked.m_terrainGrid, "m_terrainLifeforms" ) as TerrainLifeforms;
		var modelled = 0;
		var tinted = 0;
		var grounded = 0;
		var detail = "";

		foreach ( var lifeformTransform in all )
		{
			var lifeform = lifeformTransform.GetComponent<TerrainLifeform>();
			var renderers = lifeformTransform.GetComponentsInChildren<Renderer>();
			var skin = false;

			foreach ( var renderer in renderers )
			{
				var materials = renderer.sharedMaterials;

				for ( var index = 0; index < materials.Length; index++ )
				{
					if ( ( materials[ index ] == null ) || !materials[ index ].name.EndsWith( " Skin" ) )
					{
						continue;
					}

					skin = true;

					var block = new MaterialPropertyBlock();

					renderer.GetPropertyBlock( block, index );

					var colour = block.GetColor( "SF_AlbedoColor" );
					var species = lifeform.m_species;

					if ( ( Mathf.Abs( colour.r - species.m_red ) < 0.01f ) && ( Mathf.Abs( colour.g - species.m_green ) < 0.01f ) && ( Mathf.Abs( colour.b - species.m_blue ) < 0.01f ) )
					{
						tinted++;
					}
					else if ( detail.Length < 200 )
					{
						detail += "tint " + colour + " vs " + species.m_red + "/" + species.m_green + "/" + species.m_blue + ", ";
					}
				}
			}

			if ( skin )
			{
				modelled++;
			}

			if ( ( populator != null ) && populator.GetGround( lifeformTransform.position, out var ground ) )
			{
				var bottom = DrawnBounds( lifeformTransform.gameObject ).min.y;

				if ( Mathf.Abs( bottom - ground ) < 1.0f )
				{
					grounded++;
				}
				else if ( detail.Length < 400 )
				{
					detail += lifeformTransform.name + " bottom " + bottom + " ground " + ground + ", ";
				}
			}
		}

		Log( "lifeformmodels: models " + found + "/" + names.Length + ", fit a unit cube " + fits + ", snake along +Z " + snakeAlongZ + " | " + report + " || planet 92: " + all.Count + " lifeforms, modelled " + modelled + ", tinted " + tinted + ", on the ground " + grounded + " | " + detail );

		Check( "lifeformmodels: every body plan has its model, each fitting a cube of one unit", ( found == names.Length ) && ( fits == names.Length ), found + "/" + fits + " | " + report );
		Check( "lifeformmodels: the models face along +Z (the snake lies along it)", snakeAlongZ, snakeAlongZ.ToString() );
		Check( "lifeformmodels: the lifeforms of planet 92 are their models, the skin in the species colour", ( all.Count > 0 ) && ( modelled == all.Count ) && ( tinted == all.Count ), all.Count + " lifeforms, " + modelled + " modelled, " + tinted + " tinted | " + detail );
		Check( "lifeformmodels: they stand on the ground", ( all.Count > 0 ) && ( grounded == all.Count ), grounded + " of " + all.Count + " | " + detail );

		Finish( "scenario=lifeformmodels models=" + found + " modelled=" + modelled, 0 );
	}

	// ---------------------------------------------------------------- phase 6: pictures of the lifeform models on a planet (probe.ps1 -Graphics)

	IEnumerator ScenarioLifeformPictures()
	{
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system, landed beside its first lifeform
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var firstPlace = ( LifeformsOnTheGround().Count > 0 ) ? LifeformsOnTheGround()[ 0 ].position : Vector3.zero;

		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );
		yield return LandInTheArthSystemAt( 92, firstPlace + Vector3.forward * 10.0f, done );

		yield return Frames( 10 );

		if ( SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null )
		{
			Log( "lifeformpictures: no graphics device (-nographics), so no pictures; run probe.ps1 with -Graphics for them" );

			Finish( "scenario=lifeformpictures pictures=0", 0 );
			yield break;
		}

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "lifeform-pictures" ) );

		System.IO.Directory.CreateDirectory( folder );

		var populator = FieldOrNull( controller.m_disembarked.m_terrainGrid, "m_terrainLifeforms" ) as TerrainLifeforms;
		var vehicle = controller.m_terrainVehicle.transform.position;
		var pictures = 0;

		// the game draws its view in a part of the screen beside the console: the pictures are taken by a camera like the game's that takes the whole frame
		var pictureCameraObject = new GameObject( "Lifeform Picture Camera" );
		var pictureCamera = pictureCameraObject.AddComponent<Camera>();

		pictureCamera.CopyFrom( Camera.main );
		pictureCamera.rect = new Rect( 0.0f, 0.0f, 1.0f, 1.0f );

		// ---- 1. the game's own view, with the planet's lifeforms as they are placed
		RenderCameraPicture( pictureCamera, System.IO.Path.Combine( folder, "01_game_view.png" ) );
		pictures++;

		// ---- 2. every model in a ring around the vehicle, each in a colour of its own, as large as a middling species
		var names = new[] { "Snakelike", "BilateralNoLegs", "BilateralOneLeg", "BilateralTwoLegs", "BilateralFourLegs", "BilateralManyLegs", "RadialNoLegs", "RadialFewLegs", "RadialManyLegs", "AmorphousNoLegs", "AmorphousLegs", "LeavedSmallA", "LeavedSmallB", "LeavedMediumA", "LeavedMediumB", "LeavedLargeA", "LeavedLargeB" };
		var hues = new[] { 0.0f, 0.05f, 0.08f, 0.33f, 0.6f, 0.75f, 0.83f, 0.92f };
		var placed = new List<GameObject>();

		for ( var i = 0; i < names.Length; i++ )
		{
			var prefab = Resources.Load<GameObject>( "Lifeforms/" + names[ i ] );

			if ( prefab == null )
			{
				continue;
			}

			var angle = i * ( 360.0f / names.Length ) * Mathf.Deg2Rad;
			var place = vehicle + new Vector3( Mathf.Sin( angle ), 0.0f, Mathf.Cos( angle ) ) * 28.0f;
			var ground = vehicle.y;

			if ( populator != null )
			{
				populator.GetGround( place, out ground );
			}

			var instance = Instantiate( prefab, new Vector3( place.x, ground, place.z ), Quaternion.Euler( 0.0f, i * 47.0f, 0.0f ) );

			instance.transform.localScale = Vector3.one * ( names[ i ].StartsWith( "Leaved" ) ? 9.0f : 6.0f );

			var colour = Color.HSVToRGB( hues[ i % hues.Length ], 0.8f, 0.85f );
			var block = new MaterialPropertyBlock();

			foreach ( var renderer in instance.GetComponentsInChildren<Renderer>() )
			{
				var materials = renderer.sharedMaterials;

				for ( var index = 0; index < materials.Length; index++ )
				{
					if ( ( materials[ index ] != null ) && materials[ index ].name.EndsWith( " Skin" ) )
					{
						renderer.GetPropertyBlock( block, index );
						block.SetColor( "SF_AlbedoColor", colour );
						renderer.SetPropertyBlock( block, index );
					}
				}
			}

			placed.Add( instance );
		}

		yield return Frames( 5 );

		pictureCamera.CopyFrom( Camera.main );
		pictureCamera.rect = new Rect( 0.0f, 0.0f, 1.0f, 1.0f );

		RenderCameraPicture( pictureCamera, System.IO.Path.Combine( folder, "02_game_view_all_models.png" ) );
		pictures++;

		// ---- 3. close-ups: the picture camera low over the ground, looking at each model in turn
		var closeUp = pictureCamera;

		for ( var i = 0; i < placed.Count; i++ )
		{
			var target = placed[ i ].transform.position;
			var outward = ( target - vehicle );

			outward.y = 0.0f;
			outward = outward.normalized;

			var height = placed[ i ].transform.localScale.y;

			closeUp.transform.position = target - outward * height * 1.8f + Vector3.up * height * 0.9f + Vector3.Cross( Vector3.up, outward ) * height * 0.8f;
			closeUp.transform.LookAt( target + Vector3.up * height * 0.35f );
			closeUp.fieldOfView = 45.0f;

			RenderCameraPicture( closeUp, System.IO.Path.Combine( folder, ( 3 + i ).ToString( "00" ) + "_closeup_" + placed[ i ].name.Replace( "(Clone)", "" ) + ".png" ) );
			pictures++;

			yield return null;
		}

		Destroy( pictureCameraObject );

		foreach ( var instance in placed )
		{
			Destroy( instance );
		}

		Log( "lifeformpictures: " + pictures + " pictures in " + folder );

		Check( "lifeformpictures: a picture of the game view and a close-up of every model", pictures == 2 + names.Length, pictures.ToString() );

		Finish( "scenario=lifeformpictures pictures=" + pictures, 0 );
	}

	// ---------------------------------------------------------------- phase 6: going uphill is slow

	// drives the terrain vehicle from this place in this direction at this speed for a second - returns how far it went
	static IEnumerator DriveForASecond( Vector3 start, Vector3 direction, float speed, float[] distance )
	{
		var general = DataController.m_instance.m_playerData.m_general;

		general.m_coordinates = start;
		general.m_currentDirection = direction;

		yield return Frames( 2 );

		var from = general.m_coordinates;
		var end = Time.realtimeSinceStartup + 1.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			general.m_currentDirection = direction;
			general.m_currentSpeed = speed;

			yield return null;
		}

		general.m_currentSpeed = 0.0f;

		var delta = general.m_coordinates - from;

		delta.y = 0.0f;

		distance[ 0 ] = delta.magnitude;
	}

	IEnumerator ScenarioUphill()
	{
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 92 of the Arth system
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var populator = FieldOrNull( controller.m_disembarked.m_terrainGrid, "m_terrainLifeforms" ) as TerrainLifeforms;
		var general = DataController.m_instance.m_playerData.m_general;
		var start = general.m_coordinates;

		if ( populator == null )
		{
			Finish( "scenario=uphill abort: no populator", 2 );
			yield break;
		}

		// the steepest rise around the vehicle, over 8 units, in 16 directions, from up to 20 places nearby
		var bestRise = 0.0f;
		var bestPlace = start;
		var bestDirection = Vector3.forward;

		for ( var place = 0; place < 20; place++ )
		{
			var from = start + new Vector3( ( place % 5 ) * 40.0f - 80.0f, 0.0f, ( place / 5 ) * 40.0f - 60.0f );

			if ( !populator.GetGround( from, out var here ) )
			{
				continue;
			}

			for ( var i = 0; i < 16; i++ )
			{
				var angle = i * 22.5f * Mathf.Deg2Rad;
				var direction = new Vector3( Mathf.Sin( angle ), 0.0f, Mathf.Cos( angle ) );

				if ( populator.GetGround( from + direction * 8.0f, out var ahead ) && ( populator.GetGround( from + direction * 30.0f, out var farther ) ) )
				{
					var rise = ( ahead - here ) / 8.0f;

					if ( ( rise > bestRise ) && ( farther > ahead ) )
					{
						bestRise = rise;
						bestPlace = from;
						bestDirection = direction;
					}
				}
			}
		}

		// a second uphill, and a second downhill from the same place, at the same speed
		var uphill = new float[ 1 ];
		var downhill = new float[ 1 ];

		yield return DriveForASecond( bestPlace, bestDirection, 10.0f, uphill );
		yield return DriveForASecond( bestPlace, -bestDirection, 10.0f, downhill );

		general.m_coordinates = start;

		Log( "uphill: steepest rise " + bestRise + " at " + bestPlace + " towards " + bestDirection + " | a second uphill " + uphill[ 0 ] + ", downhill " + downhill[ 0 ] );

		Check( "uphill: there is a slope to climb near the landing place (control)", bestRise > 0.1f, bestRise.ToString() );
		Check( "uphill: the terrain vehicle goes less far up a slope than down it", ( downhill[ 0 ] > 1.0f ) && ( uphill[ 0 ] < downhill[ 0 ] * 0.85f ), uphill[ 0 ] + " / " + downhill[ 0 ] );

		Finish( "scenario=uphill rise=" + bestRise + " up=" + uphill[ 0 ] + " down=" + downhill[ 0 ], 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Flat Device

	// a lifeform attacks this many times: returns how many of the attacks hurt the captain (the crew kept from dying)
	static int AttacksThatHurt( TerrainLifeform lifeform, int count, PD_Personnel.PD_PersonnelFile captain )
	{
		var attack = typeof( TerrainLifeform ).GetMethod( "AttackPlayer", c_any );
		var hurt = 0;

		for ( var i = 0; i < count; i++ )
		{
			captain.m_vitality = 100000.0f;

			attack.Invoke( lifeform, null );

			if ( captain.m_vitality < 100000.0f )
			{
				hurt++;
			}
		}

		captain.m_vitality = 100.0f;

		return hurt;
	}

	IEnumerator ScenarioFlatDevice()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		// a crew of one, so that every attack is on the captain
		var ada = AddPerson( "Ada", 100.0f, 100 );

		for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
		{
			playerData.m_crewAssignment.Assign( role, ada.m_fileId );
		}

		// planet 92 of the Arth system
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		var all = LifeformsOnTheGround();

		if ( all.Count == 0 )
		{
			Finish( "scenario=flatdevice abort: no lifeform", 2 );
			yield break;
		}

		// a lifeform that nearly always hits: its chance is the average of its aggression and intelligence
		var lifeform = all[ 0 ].GetComponent<TerrainLifeform>();

		lifeform.m_aggression = 96;
		lifeform.m_species.m_intelligence = 96;

		// ---- 1. without the Flat Device
		var flatDevice = DataController.m_instance.m_gameData.FindArtifactId( "Flat Device" );

		if ( playerData.m_playerShip.HasArtifact( "Flat Device" ) )
		{
			playerData.m_playerShip.RemoveArtifact( flatDevice );
		}

		var hurtWithout = AttacksThatHurt( lifeform, 200, ada );

		// ---- 2. with the Flat Device in the ship's hold
		playerData.m_playerShip.AddArtifact( flatDevice );

		controller.m_messages.Clear();

		var hurtWith = AttacksThatHurt( lifeform, 200, ada );
		var messages = MessageList();

		playerData.m_playerShip.RemoveArtifact( flatDevice );

		Log( "flatdevice: of 200 attacks (a chance of 96%) " + hurtWithout + " hurt without the Flat Device, " + hurtWith + " with it | " + ( messages.Length > 300 ? messages.Substring( 0, 300 ) : messages ) );

		Check( "flatdevice: without the Flat Device nearly every attack hurts (control)", hurtWithout >= 170, hurtWithout + " of 200" );
		Check( "flatdevice: with the Flat Device about half of the attacks hurt, and the shield turns the others away", ( hurtWith >= 80 ) && ( hurtWith <= 140 ) && messages.Contains( "the shield turns it away" ), hurtWith + " of 200" );

		Finish( "scenario=flatdevice without=" + hurtWithout + " with=" + hurtWith, 0 );
	}

	// ---------------------------------------------------------------- phase 4: the Red Cylinder

	// the Red Cylinder looks at this place of the landing map (null on the code before it)
	static object RedCylinderLook( float eastWest, float northSouth )
	{
		var type = typeof( ShipButton ).Assembly.GetType( "RedCylinder" );
		var look = ( type == null ) ? null : type.GetMethod( "Look" );

		return ( look == null ) ? null : look.Invoke( null, new object[] { eastWest, northSouth } );
	}

	IEnumerator ScenarioRedCylinder()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// planet 94 of the Arth system has an ancient random ruin
		yield return LandInTheArthSystemAt( 94, Vector3.zero, done );

		yield return Frames( 5 );

		// ---- 1. where the surface placed the random ruins
		var placed = new List<string>();
		var placedPlaces = new List<Vector2>();
		var terrainRuins = controller.m_disembarked.m_terrainGrid.m_terrainRuins;

		foreach ( Transform child in terrainRuins.transform )
		{
			if ( child.name.StartsWith( "Ancient ruin" ) || child.name.StartsWith( "Old Empire ruin" ) )
			{
				Tools.WorldToLatLongCoordinates( child.position, out var eastWest, out var northSouth );

				placed.Add( ( child.name.StartsWith( "Ancient" ) ? "ancient" : "old" ) + " " + eastWest.ToString( "F1" ) + "/" + northSouth.ToString( "F1" ) );
				placedPlaces.Add( new Vector2( eastWest, northSouth ) );
			}
		}

		// ---- 2. where they are worked out from the planet's maps, as the Red Cylinder does from orbit
		var planet = gameData.m_planetList[ 94 ];
		var planetGenerator = controller.m_starSystem.GetPlanetController( 94 ).GetPlanetGenerator();
		var getPlaces = typeof( TerrainRuins ).GetMethod( "GetRandomRuinPlaces" );
		var worked = new List<string>();

		if ( getPlaces != null )
		{
			var places = getPlaces.Invoke( null, new object[] { planetGenerator, controller.m_disembarked.m_terrainGrid.m_elevationScale, planet } ) as IList;

			foreach ( var place in places )
			{
				var where = (Vector2) place.GetType().GetProperty( "Key" ).GetValue( place );
				var ancient = (bool) place.GetType().GetProperty( "Value" ).GetValue( place );

				worked.Add( ( ancient ? "ancient" : "old" ) + " " + where.x.ToString( "F1" ) + "/" + where.y.ToString( "F1" ) );
			}
		}

		// ---- 3. the Red Cylinder flashes at the ancient ruin, not far from it, and not without the Red Cylinder (the control)
		var ruin = ( placedPlaces.Count > 0 ) ? placedPlaces[ 0 ] : Vector2.zero;
		var redCylinder = gameData.FindArtifactId( "Red Cylinder" );

		controller.m_messages.Clear();

		var withoutIt = RedCylinderLook( ruin.x, ruin.y );

		playerData.m_playerShip.AddArtifact( redCylinder );

		var farAway = RedCylinderLook( ruin.x + 90.0f, -ruin.y );
		var atTheRuin = RedCylinderLook( ruin.x + 3.0f, ruin.y - 2.0f );
		var messages = MessageList();

		playerData.m_playerShip.RemoveArtifact( redCylinder );

		Log( "redcylinder: placed [" + string.Join( ", ", placed ) + "] worked out [" + string.Join( ", ", worked ) + "] | without it " + withoutIt + ", far away " + farAway + ", at the ruin " + atTheRuin + " | " + messages );

		Check( "redcylinder: the random ruins are worked out from orbit where the surface places them", ( placed.Count > 0 ) && ( string.Join( ", ", placed ) == string.Join( ", ", worked ) ), string.Join( ", ", placed ) + " | " + string.Join( ", ", worked ) );
		Check( "redcylinder: the Red Cylinder flashes on the landing map near an ancient ruin, not far from it, and only aboard", ( atTheRuin is bool ) && (bool) atTheRuin && !(bool) farAway && !(bool) withoutIt && messages.Contains( "The Red Cylinder is flashing" ), withoutIt + ", " + farAway + ", " + atTheRuin + " | " + messages );

		Finish( "scenario=redcylinder placed=" + placed.Count + " worked=" + worked.Count, 0 );
	}

	// ---------------------------------------------------------------- phase 7: the planet under an encounter that began in orbit

	// the planet drawn under the encounter (null if there is none)
	static Transform PlanetBackdrop()
	{
		return SpaceflightController.m_instance.m_encounter.transform.Find( "Planet Backdrop" );
	}

	IEnumerator ScenarioOrbitPlanet()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var starSystem = controller.m_starSystem;

		EnsureCrew();

		// a ship that survives whatever it meets
		playerData.m_playerShip.m_armorPoints = 100000;

		// ---- 1. a Spemin encounter met in hyperspace (the control: no planet)
		EnterEncounter( FindEncounter( 1, 6, 3, 0 ) );

		yield return Frames( 5 );

		var backdropInHyperspace = PlanetBackdrop();
		var shownInHyperspace = ( backdropInHyperspace != null ) && backdropInHyperspace.gameObject.activeInHierarchy;

		yield return FlyOutOfEncounter();

		// ---- 2. the derelict that waits in orbit 3 of star 30 (encounter 316): into orbit around its planet, and the encounter begins
		const int c_encounterId = 316;
		const int c_starId = 30;

		var guardedPlanetId = -1;

		foreach ( var planet in gameData.m_planetList )
		{
			if ( ( planet.m_starId == c_starId ) && ( planet.m_orbitPosition == gameData.m_encounterList[ c_encounterId ].m_orbitPosition ) )
			{
				guardedPlanetId = planet.m_id;
			}
		}

		EnterStarSystem( c_starId );

		var start = Time.realtimeSinceStartup;

		while ( starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 40.0f ) )
		{
			yield return null;
		}

		yield return Frames( 5 );
		yield return EnterOrbit( guardedPlanetId );
		yield return Frames( 5 );

		var inEncounter = playerData.m_general.m_location == PD_General.Location.Encounter;
		var backdrop = PlanetBackdrop();
		var shown = ( backdrop != null ) && backdrop.gameObject.activeInHierarchy;
		var below = shown && ( backdrop.position.y + backdrop.localScale.y < playerData.m_general.m_coordinates.y - 100.0f );
		var planetController = starSystem.GetPlanetController( guardedPlanetId );
		var samePlanet = shown && ( planetController != null ) && ( backdrop.GetComponent<MeshRenderer>().sharedMaterial == planetController.GetMaterial() );

		// ---- 3. with a graphics device, a picture of it (probe.ps1 -Graphics)
		var picture = "none (no graphics device)";

		if ( inEncounter && ( SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null ) )
		{
			var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "orbit-encounter-pictures" ) );

			System.IO.Directory.CreateDirectory( folder );

			var pictureCameraObject = new GameObject( "Orbit Encounter Picture Camera" );
			var pictureCamera = pictureCameraObject.AddComponent<Camera>();

			pictureCamera.CopyFrom( Camera.main );
			pictureCamera.rect = new Rect( 0.0f, 0.0f, 1.0f, 1.0f );

			picture = System.IO.Path.Combine( folder, "orbit_encounter.png" );

			RenderCameraPicture( pictureCamera, picture );

			Destroy( pictureCameraObject );
		}

		Log( "orbitplanet: hyperspace encounter: planet shown " + shownInHyperspace + " || encounter in orbit: in it " + inEncounter + ", planet shown " + shown + ", below the ships " + below + ", the planet's own surface " + samePlanet + ( shown ? " (radius " + backdrop.localScale.x + ", at " + backdrop.position + ", ship at " + playerData.m_general.m_coordinates + ")" : "" ) + " | picture " + picture );

		Check( "orbitplanet: an encounter met in hyperspace has no planet under it (control)", !shownInHyperspace, shownInHyperspace.ToString() );
		Check( "orbitplanet: an encounter that began in orbit is drawn over its planet's own surface, below the ships", inEncounter && shown && below && samePlanet, inEncounter + ", " + shown + ", " + below + ", " + samePlanet );

		Finish( "scenario=orbitplanet shown=" + shown, 0 );
	}

	// ---------------------------------------------------------------- phase 6: storms on the surface

	// calls a static method of PlanetWeather (null on the code before it)
	static object Weather( string method )
	{
		var type = typeof( ShipButton ).Assembly.GetType( "PlanetWeather" );
		var info = ( type == null ) ? null : type.GetMethod( method );

		return ( info == null ) ? null : info.Invoke( null, null );
	}

	// the crew on board, all at full vitality
	static List<PD_Personnel.PD_PersonnelFile> HealCrew()
	{
		var playerData = DataController.m_instance.m_playerData;
		var crew = new List<PD_Personnel.PD_PersonnelFile>();

		for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
		{
			if ( playerData.m_crewAssignment.IsAssigned( role ) )
			{
				var file = playerData.m_crewAssignment.GetPersonnelFile( role );

				if ( ( file != null ) && !crew.Contains( file ) )
				{
					file.m_vitality = 100.0f;
					file.m_isDead = false;

					crew.Add( file );
				}
			}
		}

		return crew;
	}

	// passes this many game hours of weather, the crew healed before each: returns the storm hours, the heavy snow hours, the storm hours that injured, and the
	// other hours that injured
	static int[] PassWeatherHours( int hours )
	{
		var counts = new int[ 4 ];

		for ( var i = 0; i < hours; i++ )
		{
			var crew = HealCrew();

			Weather( "PassHour" );

			var sky = Weather( "GetSky" );
			var storm = Weather( "IsStorm" ) is bool && (bool) Weather( "IsStorm" );
			var injured = crew.Exists( file => file.m_vitality < 100.0f );

			if ( storm )
			{
				counts[ 0 ]++;
			}

			if ( ( sky != null ) && ( sky.ToString() == "SnowingHeavily" ) )
			{
				counts[ 1 ]++;
			}

			if ( injured )
			{
				counts[ storm ? 2 : 3 ]++;
			}
		}

		HealCrew();

		return counts;
	}

	IEnumerator ScenarioStorms()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- 1. planet 91 of Arth, Very Violent and frozen: storms half of the hours, heavy snow some, injuries only in storms
		yield return LandInTheArthSystemAt( 91, Vector3.zero, done );

		yield return Frames( 5 );

		var firstSky = MessageList();
		var violent = PassWeatherHours( 300 );

		// ---- 2. in a storm: half the efficiency, and the bearing to the ship lost
		var tries = 0;

		while ( !( ( Weather( "IsStorm" ) is bool ) && (bool) Weather( "IsStorm" ) ) && ( tries++ < 100 ) )
		{
			HealCrew();
			Weather( "PassHour" );
		}

		HealCrew();

		yield return Frames( 3 );

		var stormEfficiency = Weather( "GetEfficiency" );
		var displayInStorm = controller.m_displayController.m_terrainVehicleDisplay.m_statusValues.text;
		var fuelEfficiencyInStorm = controller.m_terrainVehicle.GetFuelEfficiency();

		// ---- 3. back in the ship nothing happens
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		var inShip = PassWeatherHours( 50 );

		// ---- 4. planet 90 of Arth, weather None: storms now and then
		yield return LandInTheArthSystemAt( 90, Vector3.zero, done );

		yield return Frames( 5 );

		var calm = PassWeatherHours( 300 );

		Log( "storms: first sky " + firstSky + " || planet 91 (Very Violent), 300 hours: storms " + violent[ 0 ] + ", heavy snow " + violent[ 1 ] + ", storm hours that injured " + violent[ 2 ] + ", other hours that injured " + violent[ 3 ] + " | in a storm: efficiency " + stormEfficiency + ", fuel efficiency " + fuelEfficiencyInStorm + ", display " + displayInStorm.Replace( "\n", "/" ) + " || in the ship, 50 hours: storms " + inShip[ 0 ] + ", injuries " + ( inShip[ 2 ] + inShip[ 3 ] ) + " || planet 90 (None), 300 hours: storms " + calm[ 0 ] + ", injured " + calm[ 2 ] + "/" + calm[ 3 ] );

		Check( "storms: on a Very Violent planet storms come in about half of the hours, with heavy snow besides", ( violent[ 0 ] >= 120 ) && ( violent[ 0 ] <= 180 ) && ( violent[ 1 ] >= 30 ), violent[ 0 ] + " storms, " + violent[ 1 ] + " heavy snow of 300 hours" );
		Check( "storms: a storm injures the crew in about half of its hours, and nothing else does", ( violent[ 0 ] > 0 ) && ( violent[ 2 ] >= violent[ 0 ] * 3 / 10 ) && ( violent[ 2 ] <= violent[ 0 ] * 7 / 10 ) && ( violent[ 3 ] == 0 ), violent[ 2 ] + " of " + violent[ 0 ] + " storm hours, " + violent[ 3 ] + " others" );
		Check( "storms: in a storm the terrain vehicle has half its efficiency and the bearing to the ship is lost", ( stormEfficiency is float ) && Mathf.Approximately( (float) stormEfficiency, 0.5f ) && displayInStorm.Contains( "LOST!" ), stormEfficiency + " | " + displayInStorm.Replace( "\n", "/" ) );
		Check( "storms: in the ship there is no weather and no injury", ( inShip[ 0 ] == 0 ) && ( inShip[ 2 ] + inShip[ 3 ] == 0 ), inShip[ 0 ] + " storms, " + ( inShip[ 2 ] + inShip[ 3 ] ) + " injuries" );
		Check( "storms: on a planet whose weather is None storms are rare but come", ( calm[ 0 ] >= 15 ) && ( calm[ 0 ] <= 60 ), calm[ 0 ] + " storms of 300 hours" );

		Finish( "scenario=storms violent=" + violent[ 0 ] + " calm=" + calm[ 0 ], 0 );
	}

	// ---------------------------------------------------------------- phase 7: pictures of every screen, for the comparison with the original's (probe.ps1 -Graphics)

	// the 3D star system view: in Arth's system (class G) and star 156 (class A, a gas giant in orbit 4) the sunlight shines from the middle of the star, in the
	// plane of the orbits, toward the ship (so the planets in view are lit on their star's side) and has the star's colour; with -Graphics, pictures of the
	// ship beside the sun, beside each planet and of an overview from high above the sun go into Logs/screen-pictures/system-view/ (or -probePictures); the sky
	// has a Milky Way under the territory's nebula (and stars of several colours, #232), and the main view shows no Regen Starfield debug button (#240)
	IEnumerator ScenarioSystemViewPictures()
	{
		var controller = SpaceflightController.m_instance;
		var gameData = DataController.m_instance.m_gameData;
		var folder = ScreenPictureFolder( "system-view" );
		var pictures = new List<string>();

		EnsureCrew();

		// no encounter begins while the pictures are taken
		controller.m_encounterRange = 0.0f;

		var ship = controller.m_playerShip.gameObject;

		// the light the planets are lit by: the directional light of the star system that lights their layer (the scene's sunlight before the change), and the
		// one the ship is lit by
		var planetLayer = controller.m_starSystem.m_planetController[ 0 ].m_planetModel.layer;
		var shipRenderer = ship.GetComponentInChildren<MeshRenderer>();
		var shipLayer = ( shipRenderer != null ) ? shipRenderer.gameObject.layer : ship.layer;
		Light sunlight = null;
		Light shipLight = null;

		foreach ( var light in controller.m_starSystem.GetComponentsInChildren<Light>( true ) )
		{
			if ( light.type != LightType.Directional )
			{
				continue;
			}

			if ( ( light.cullingMask & ( 1 << planetLayer ) ) != 0 )
			{
				sunlight = light;
			}

			if ( ( light.cullingMask & ( 1 << shipLayer ) ) != 0 )
			{
				shipLight = light;
			}
		}

		Log( "systemviewpictures: the planets (layer " + planetLayer + ") are lit by " + ( ( sunlight != null ) ? sunlight.name : "nothing" ) + ", the ship (layer " + shipLayer + ") by " + ( ( shipLight != null ) ? shipLight.name : "nothing" ) );

		var shipLitFromAbove = true;

		System.Action<string> take = name =>
		{
			if ( folder != "" )
			{
				// the sky turned back to where it starts (it turns as the ship moves, and the ship is put in place at once here, which turns it a long way)
				if ( StarflightSkybox.m_instance != null )
				{
					StarflightSkybox.m_instance.m_currentRotation = Quaternion.identity;
					StarflightSkybox.m_instance.m_material.SetMatrix( "SF_ModelMatrix", Matrix4x4.identity );
				}

				RenderScreenPicture( System.IO.Path.Combine( folder, name + ".png" ) );
				pictures.Add( name );
			}
		};

		foreach ( var starId in new[] { gameData.m_misc.m_arthStarId, 156 } )
		{
			EnterStarSystem( starId );

			var start = Time.realtimeSinceStartup;

			while ( controller.m_starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 60.0f ) )
			{
				yield return null;
			}

			// the shine fades in
			yield return new WaitForSecondsRealtime( 3.0f );

			var starClass = gameData.m_starList[ starId ].m_class;
			var prefix = "star" + starId + "_" + starClass;

			// beside the sun
			MoveShipTo( new Vector3( 0.0f, 0.0f, -250.0f ) );

			yield return Frames( 10 );

			take( prefix + "_sun" );

			// beside each planet, south of it: the sunlight's direction is measured at each
			var worstAngle = 0.0f;
			var planetsSeen = 0;

			foreach ( var planetController in controller.m_starSystem.m_planetController )
			{
				if ( ( planetController.m_planet == null ) || !planetController.gameObject.activeInHierarchy )
				{
					continue;
				}

				var planet = planetController.m_planet;
				var position = planetController.transform.position;

				MoveShipTo( position + new Vector3( 0.0f, 0.0f, -( planet.GetScale().y * 1.5f + 120.0f ) ) );

				yield return Frames( 10 );

				if ( sunlight != null )
				{
					// from the middle of the star (in the plane of the orbits) to the ship
					var fromStar = ship.transform.position - controller.m_starSystem.transform.position;
					var angle = Vector3.Angle( sunlight.transform.forward, fromStar );

					worstAngle = Mathf.Max( worstAngle, angle );

				}

				// the ship is still lit from high above, as before (at least 15 degrees down: the scene's sunlight is aimed at the ship from 2048 above the star)
				if ( ( shipLight == null ) || ( Vector3.Angle( shipLight.transform.forward, Vector3.down ) > 75.0f ) )
				{
					shipLitFromAbove = false;
				}

				planetsSeen++;

				take( prefix + "_orbit" + planet.m_orbitPosition + "_" + gameData.m_surfaceList[ planet.m_surfaceId ].m_name.Replace( " ", "" ) );
			}

			var expectedColor = SystemDisplay.GetStarColor( starClass );
			var colorMatches = ( sunlight != null ) && ( Mathf.Abs( sunlight.color.r - expectedColor.r ) < 0.01f ) && ( Mathf.Abs( sunlight.color.g - expectedColor.g ) < 0.01f ) && ( Mathf.Abs( sunlight.color.b - expectedColor.b ) < 0.01f );

			Log( "systemviewpictures: star " + starId + " (" + starClass + "): " + planetsSeen + " planets, the sunlight's direction is at most " + worstAngle.ToString( "F1" ) + " degrees from the star's, its colour " + ( ( sunlight != null ) ? sunlight.color.ToString() : "none" ) + " (the star's " + expectedColor + ")" );

			Check( "systemviewpictures: star " + starId + ": beside every planet the sunlight comes from the star itself, in the plane of the orbits (within 2 degrees)", ( sunlight != null ) && ( planetsSeen > 0 ) && ( worstAngle <= 2.0f ), "sunlight " + ( sunlight != null ) + ", " + planetsSeen + " planets, worst " + worstAngle.ToString( "F1" ) + " degrees" );

			Check( "systemviewpictures: star " + starId + ": the sunlight has the colour of a class " + starClass + " star", colorMatches, ( ( sunlight != null ) ? sunlight.color.ToString() : "no sunlight" ) + " vs " + expectedColor );

			Check( "systemviewpictures: star " + starId + ": the ship is still lit from high above, so that it can be seen from the camera (control)", shipLitFromAbove, "light " + ( ( shipLight != null ) ? shipLight.name : "none" ) );

			// high above the sun
			MoveShipTo( Vector3.zero );

			controller.m_playerCamera.SetCameraFollow( ship, Vector3.up * 9000.0f );

			yield return Frames( 10 );

			take( prefix + "_overview" );

			controller.m_playerCamera.SetCameraFollow( ship, Vector3.up * 1024.0f );
		}

		// ---- the sky: a Milky Way under the territory's nebula, stars of several colours, and no debug button on the main view
		var sky = RenderSettings.skybox;
		var milkyWay = ( ( sky != null ) && sky.HasProperty( "_MilkyWay" ) ) ? sky.GetTexture( "_MilkyWay" ) : null;
		var milkyWayStrength = ( ( sky != null ) && sky.HasProperty( "_MilkyWayStrength" ) ) ? sky.GetFloat( "_MilkyWayStrength" ) : 0.0f;

		Check( "systemviewpictures: the sky of the star system view has a Milky Way", ( milkyWay != null ) && ( milkyWayStrength > 0.0f ), "texture " + ( ( milkyWay != null ) ? milkyWay.name : "none" ) + ", strength " + milkyWayStrength );

		var fewestColours = int.MaxValue;
		var layers = 0;

		foreach ( var starfield in UnityEngine.Object.FindObjectsByType<InfiniteStarfield>( FindObjectsInactive.Include, FindObjectsSortMode.None ) )
		{
			foreach ( var particleSystem in starfield.GetComponentsInChildren<ParticleSystem>( true ) )
			{
				var particles = new ParticleSystem.Particle[ particleSystem.particleCount ];
				var count = particleSystem.GetParticles( particles );
				var colours = new HashSet<Color32>();

				for ( var k = 0; k < count; k++ )
				{
					colours.Add( particles[ k ].startColor );
				}

				if ( count > 0 )
				{
					fewestColours = Mathf.Min( fewestColours, colours.Count );
					layers++;
				}
			}
		}

		Check( "systemviewpictures: every layer of the starfield has stars of several colours, at least 4 (control: #232)", ( layers > 0 ) && ( fewestColours >= 4 ), layers + " layers, the fewest colours in a layer " + ( ( layers > 0 ) ? fewestColours.ToString() : "none" ) );

		var debugButtonsShown = 0;

		foreach ( var gameObject in Resources.FindObjectsOfTypeAll<GameObject>() )
		{
			if ( gameObject.scene.IsValid() && ( gameObject.name == "Starfield Regen Button" ) && gameObject.activeInHierarchy )
			{
				debugButtonsShown++;
			}
		}

		Check( "systemviewpictures: the main view shows no Regen Starfield debug button (control: #240)", debugButtonsShown == 0, debugButtonsShown + " shown" );

		if ( folder == "" )
		{
			Log( "systemviewpictures: no graphics device (-nographics), so no pictures; run probe.ps1 with -Graphics for them" );
		}
		else
		{
			Log( "systemviewpictures: " + pictures.Count + " pictures in " + folder + ": " + string.Join( ", ", pictures ) );
		}

		Finish( "scenario=systemviewpictures pictures=" + pictures.Count + " checks=" + s_checksPassed + "/" + ( s_checksPassed + s_checksFailed ), 0 );
	}

	// renders every camera in its order of depth into one picture, as the screen shows them: the views, the console and its displays, the panels (cameras
	// that draw into a texture of their own are left out: the screens that show those textures draw them)
	static void RenderScreenPicture( string path )
	{
		var renderTexture = new RenderTexture( 1920, 1080, 24, RenderTextureFormat.ARGB32 );
		var picture = new Texture2D( 1920, 1080, TextureFormat.RGB24, false );
		var cameras = new List<Camera>( Camera.allCameras );

		cameras.Sort( ( a, b ) => a.depth.CompareTo( b.depth ) );

		var active = RenderTexture.active;

		RenderTexture.active = renderTexture;
		GL.Clear( true, true, Color.black );

		foreach ( var camera in cameras )
		{
			if ( !camera.enabled || ( camera.targetTexture != null ) )
			{
				continue;
			}

			camera.targetTexture = renderTexture;
			camera.Render();
			camera.targetTexture = null;
		}

		RenderTexture.active = renderTexture;
		picture.ReadPixels( new Rect( 0, 0, 1920, 1080 ), 0, 0 );
		picture.Apply();
		RenderTexture.active = active;

		System.IO.File.WriteAllBytes( path, picture.EncodeToPNG() );

		UnityEngine.Object.Destroy( picture );
		renderTexture.Release();
		UnityEngine.Object.Destroy( renderTexture );
	}

	// the folder the screen pictures go into ("" without a graphics device)
	static string ScreenPictureFolder( string scene )
	{
		if ( SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null )
		{
			return "";
		}

		var folder = GetArg( "-probePictures", System.IO.Path.Combine( System.IO.Directory.GetCurrentDirectory(), "Logs", "screen-pictures", scene ) );

		System.IO.Directory.CreateDirectory( folder );

		// the pictures of an earlier run go, so that the folder holds one run's pictures only (their numbers change when a screen is added)
		foreach ( var oldPicture in System.IO.Directory.GetFiles( folder, "*.png" ) )
		{
			System.IO.File.Delete( oldPicture );
		}

		return folder;
	}

	IEnumerator ScenarioScreenPictures()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var displays = controller.m_displayController;
		var done = new bool[ 1 ];
		var folder = ScreenPictureFolder( "spaceflight" );
		var pictures = new List<string>();

		EnsureCrew();

		if ( folder == "" )
		{
			Log( "screenpictures: no graphics device (-nographics), so no pictures; run probe.ps1 with -Graphics for them" );

			Finish( "scenario=screenpictures pictures=0", 0 );
			yield break;
		}

		// no encounter begins while the pictures are taken
		controller.m_encounterRange = 0.0f;

		System.Action<string> take = name =>
		{
			var path = System.IO.Path.Combine( folder, ( pictures.Count + 1 ).ToString( "00" ) + "_" + name + ".png" );

			RenderScreenPicture( path );
			pictures.Add( name );
		};

		// ---- the star system (Arth's), the system display, then the status display
		EnterStarSystem( DataController.m_instance.m_gameData.m_misc.m_arthStarId );

		var start = Time.realtimeSinceStartup;

		while ( controller.m_starSystem.GeneratingPlanets() && ( Time.realtimeSinceStartup - start < 40.0f ) )
		{
			yield return null;
		}

		yield return Frames( 30 );

		displays.ChangeDisplay( displays.m_systemDisplay );

		yield return Frames( 10 );

		take( "star_system" );

		displays.ChangeDisplay( displays.m_statusDisplay );

		yield return Frames( 10 );

		take( "star_system_status" );

		// ---- in orbit around Arth's planet 90, with its sensor reading
		yield return EnterOrbit( 90 );
		yield return Frames( 30 );

		take( "orbit" );

		// a real scan of the planet (a science officer of full skill, who gives every line of the analysis), then its analysis
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.ScienceOfficer ).m_science = 250;

		new SensorsButton().Execute();

		yield return new WaitForSecondsRealtime( displays.m_sensorsDisplay.m_minDuration + 1.5f );

		take( "orbit_sensors" );

		new AnalysisButton().Execute();

		yield return Frames( 10 );

		take( "orbit_analysis" );

		// ---- the landing map
		displays.ChangeDisplay( displays.m_terrainMapDisplay );
		displays.m_terrainMapDisplay.ShowSelectSite();

		yield return Frames( 10 );

		take( "landing_map" );

		// ---- the terrain vehicle on planet 92 (it has life), with its display
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );
		yield return Frames( 30 );

		take( "terrain_vehicle" );

		// ---- hyperspace, away from any star (125,100 is Arth's own place, where the view was its sun)
		playerData.m_general.m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 118.0f, 0.0f, 96.0f ) );
		controller.SwitchLocation( PD_General.Location.Hyperspace );

		yield return Frames( 60 );

		take( "hyperspace" );

		// ---- an encounter of Spemin scouts in Arth's system, once the ships have come close
		EnterStarSystem( DataController.m_instance.m_gameData.m_misc.m_arthStarId );

		yield return Frames( 10 );

		var speminId = FindEncounter( 1, 6, 3, 0 );

		ForceVessel( speminId, 2 );
		EnterEncounter( speminId );

		yield return new WaitForSecondsRealtime( 4.0f );

		take( "encounter" );

		// the sensor reading of the first Spemin scout (selection 0 is our own ship), as the Sensors button takes it, and the analysis of it
		displays.ChangeDisplay( displays.m_sensorsDisplay );
		controller.m_encounter.StartScanning( 1 );

		// (the scan of a ship is a cinematic that ends with the reading: wait for the reading itself)
		var scanEnd = Time.realtimeSinceStartup + 30.0f;

		yield return Frames( 5 );

		while ( !displays.m_sensorsDisplay.m_hasSensorData && ( Time.realtimeSinceStartup < scanEnd ) )
		{
			yield return null;
		}

		yield return Frames( 30 );

		take( "encounter_sensors" );

		new AnalysisButton().Execute();

		yield return Frames( 10 );

		take( "encounter_analysis" );

		// the comm link with them
		controller.m_encounter.Connect();

		yield return Frames( 60 );

		take( "encounter_comm" );

		Log( "screenpictures: " + pictures.Count + " pictures in " + folder + ": " + string.Join( ", ", pictures ) );

		Check( "screenpictures: a picture of every screen visited", pictures.Count == 12, pictures.Count.ToString() );

		Finish( "scenario=screenpictures pictures=" + pictures.Count, 0 );
	}

	// ---------------------------------------------------------------- phase 7: pictures of the Starport's screens (probe.ps1 -Graphics)

	IEnumerator ScenarioStarportScreenPictures()
	{
		var folder = ScreenPictureFolder( "starport" );
		var pictures = new List<string>();

		if ( folder == "" )
		{
			Log( "starport-screenpictures: no graphics device (-nographics), so no pictures; run probe.ps1 with -Graphics for them" );

			Finish( "scenario=starport-screenpictures pictures=0", 0 );
			yield break;
		}

		System.Action<string> take = name =>
		{
			var path = System.IO.Path.Combine( folder, ( pictures.Count + 1 ).ToString( "00" ) + "_" + name + ".png" );

			RenderScreenPicture( path );
			pictures.Add( name );
		};

		yield return Frames( 60 );

		// ---- the Starport, then each of its rooms
		take( "starport" );

		var panels = new Panel[] { FindPanel<OperationsPanel>(), FindPanel<PersonnelPanel>(), FindPanel<CrewAssignmentPanel>(), FindPanel<BankPanel>(), FindPanel<ShipConfigurationPanel>(), FindPanel<TradeDepotPanel>(), FindPanel<DockingBayPanel>() };
		var names = new[] { "operations", "personnel", "crew_assignment", "bank", "ship_configuration", "trade_depot", "docking_bay" };

		for ( var i = 0; i < panels.Length; i++ )
		{
			if ( panels[ i ] == null )
			{
				continue;
			}

			yield return OpenPanel( panels[ i ], names[ i ] );
			yield return Frames( 10 );

			take( names[ i ] );

			yield return ClosePanel( panels[ i ], names[ i ] );
			yield return Frames( 10 );
		}

		Log( "starport-screenpictures: " + pictures.Count + " pictures in " + folder + ": " + string.Join( ", ", pictures ) );

		Check( "starport-screenpictures: a picture of the Starport and of each of its rooms", pictures.Count == 8, pictures.Count.ToString() );

		Finish( "scenario=starport-screenpictures pictures=" + pictures.Count, 0 );
	}

	// ---------------------------------------------------------------- phase 7: latitude and longitude as the original gives them

	IEnumerator ScenarioCoordinates()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// on planet 92 of Arth
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		yield return Frames( 5 );

		// ---- 1. the terrain vehicle at 15 north, 30 west: the coordinates above the main view (the original: "15N x 30W")
		playerData.m_general.m_lastDisembarkedCoordinates = Tools.LatLongToWorldCoordinates( -30.0f, 15.0f );
		controller.m_viewport.UpdateCoordinates();

		var label = FieldOrNull( controller.m_viewport, "m_label" ) as TMPro.TMP_Text;
		var bar = ( label != null ) ? label.text : "";

		// ---- 2. the landing map's crosshair at the same place (the original: "LAT 15N  LONG 30W")
		var map = controller.m_displayController.m_terrainMapDisplay;

		playerData.m_general.m_selectedLatitude = -30.0f;
		playerData.m_general.m_selectedLongitude = 15.0f;

		map.MoveCrosshairs();

		var latitude = map.m_latitudeText.text;
		var longitude = map.m_longitudeText.text;

		Log( "coordinates: the bar above the main view reads '" + bar + "', the landing map LAT '" + latitude + "' LONG '" + longitude + "'" );

		Check( "coordinates: the terrain vehicle's coordinates read north-south first, as the original's 15N x 30W", bar == "15N x 30W", bar );
		Check( "coordinates: the landing map's LAT is the north-south place and its LONG the east-west one", ( latitude == "15N" ) && ( longitude == "30W" ), latitude + " / " + longitude );

		Finish( "scenario=coordinates bar=" + bar + " lat=" + latitude + " long=" + longitude, 0 );
	}

	// ---------------------------------------------------------------- phase 7: the analysis of an alien ship in the original's words

	// the science officer's analysis of a sensor reading of the given kind
	static string AnalyzeScan( SensorsDisplay.ScanType scanType )
	{
		var sensors = SpaceflightController.m_instance.m_displayController.m_sensorsDisplay;

		sensors.m_hasSensorData = true;
		sensors.m_scanType = scanType;

		new AnalysisButton().Execute();

		return MessageList();
	}

	IEnumerator ScenarioAlienAnalysis()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// a ship that survives whatever it meets
		playerData.m_playerShip.m_armorPoints = 100000;

		// (a science officer of full skill, who gives every line of the analysis)
		playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.ScienceOfficer ).m_science = 250;

		// a mechan encounter: its scouts have shields of class 2 and missiles, and the mechans have no mood, so their stance is their posture
		var mechanId = -1;

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			if ( gameData.m_encounterList[ i ].m_race == GameData.Race.Mechan )
			{
				mechanId = i;

				break;
			}
		}

		EnterStarSystem( gameData.m_misc.m_arthStarId );

		yield return Frames( 10 );

		EnterEncounter( mechanId );

		yield return Frames( 10 );

		var pdEncounter = controller.m_encounter.m_pdEncounter;

		// ---- 1. the shields and weapons in each posture
		pdEncounter.m_aliensAreCowardly = false;
		pdEncounter.m_alienStance = GD_Comm.Stance.Neutral;

		var neutral = AnalyzeScan( SensorsDisplay.ScanType.MechanScout );

		pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

		var hostile = AnalyzeScan( SensorsDisplay.ScanType.MechanScout );

		pdEncounter.m_alienStance = GD_Comm.Stance.Friendly;

		var friendly = AnalyzeScan( SensorsDisplay.ScanType.MechanScout );

		pdEncounter.m_aliensAreCowardly = true;
		pdEncounter.m_alienStance = GD_Comm.Stance.Obsequious;

		var surrendering = AnalyzeScan( SensorsDisplay.ScanType.MechanScout );

		pdEncounter.m_aliensAreCowardly = false;
		pdEncounter.m_alienStance = GD_Comm.Stance.Neutral;

		// ---- 2. a Gazurtoid warship and a Spemin scout
		var gazurtoid = AnalyzeScan( SensorsDisplay.ScanType.GazurtoidWarship );
		var spemin = AnalyzeScan( SensorsDisplay.ScanType.SpeminScout );

		// ---- 3. the size line, next to a ship of 500 tons
		var shipMass = playerData.m_playerShip.m_mass;

		playerData.m_playerShip.m_mass = 500;

		var nomad = AnalyzeScan( SensorsDisplay.ScanType.NomadProbe );
		var veloxi = AnalyzeScan( SensorsDisplay.ScanType.VeloxiWarship );
		var enterprise = AnalyzeScan( SensorsDisplay.ScanType.TheEnterprise );
		var minstrel = AnalyzeScan( SensorsDisplay.ScanType.Minstrel );

		playerData.m_playerShip.m_mass = shipMass;

		Log( "alienanalysis: in encounter " + mechanId + " (" + playerData.m_general.m_location + ")" );
		Log( "alienanalysis: neutral: " + neutral );
		Log( "alienanalysis: hostile: " + hostile );
		Log( "alienanalysis: friendly: " + friendly );
		Log( "alienanalysis: surrendering: " + surrendering );
		Log( "alienanalysis: gazurtoid warship: " + gazurtoid + " | spemin scout: " + spemin );
		Log( "alienanalysis: sizes: " + nomad + " | " + veloxi + " | " + enterprise + " | " + minstrel );

		Check( "alienanalysis: neutral aliens have their shields of class 2 raised and are not armed, as the original", neutral.Contains( "Shields: <color=white>Class 2 equivalent raised<" ) && neutral.Contains( "Weapon Status: <color=white>Not armed<" ), neutral );
		Check( "alienanalysis: hostile aliens have their shields raised and are armed, as the original", hostile.Contains( "Class 2 equivalent raised<" ) && hostile.Contains( "Weapon Status: <color=white>Armed<" ), hostile );
		Check( "alienanalysis: friendly aliens have their shields lowered and are not armed, as the original", friendly.Contains( "Class 2 equivalent lowered<" ) && friendly.Contains( "Weapon Status: <color=white>Not armed<" ), friendly );
		Check( "alienanalysis: surrendering aliens have their shields lowered and are not armed, as the original", surrendering.Contains( "Class 2 equivalent lowered<" ) && surrendering.Contains( "Weapon Status: <color=white>Not armed<" ), surrendering );
		Check( "alienanalysis: a Gazurtoid ship's shields are unknown and a Spemin scout has none apparent, as the original", gazurtoid.Contains( "Shields: <color=white>Unknown<" ) && spemin.Contains( "Shields: <color=white>None apparent<" ), gazurtoid + " | " + spemin );
		Check( "alienanalysis: the size is a whole number or the decimals to the first that is not zero, with no label, as the original", nomad.Contains( ">0.04 times the size of our ship.<" ) && veloxi.Contains( ">1 times the size of our ship.<" ) && enterprise.Contains( ">38 times the size of our ship.<" ) && minstrel.Contains( ">0.004 times the size of our ship.<" ) && !( nomad + veloxi + enterprise + minstrel ).Contains( "Size:" ), nomad + " | " + veloxi + " | " + enterprise + " | " + minstrel );

		Finish( "scenario=alienanalysis", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the terrain vehicle's display in the original's words

	IEnumerator ScenarioTVDisplay()
	{
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// on planet 92 of Arth, in the terrain vehicle
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );

		// (no weather: a storm would hide the bearing behind LOST!)
		PlanetWeather.End();

		yield return Frames( 10 );

		var rows = controller.m_displayController.m_terrainVehicleDisplay.m_statusValues.text.Split( '\n' );
		var date = ( rows.Length > 0 ) ? rows[ 0 ] : "";
		var ship = ( rows.Length > 0 ) ? System.Text.RegularExpressions.Regex.Replace( rows[ rows.Length - 1 ], "<[^>]*>", "" ) : "";
		var bearing = ship.Contains( "KM." ) ? ship.Substring( ship.IndexOf( "KM." ) + 3 ).Trim() : "";
		var words = new[] { "North", "Northeast", "East", "Southeast", "South", "Southwest", "West", "Northwest" };

		Log( "tvdisplay: date '" + date + "', ship '" + ship + "' (bearing '" + bearing + "') | " + string.Join( " / ", rows ) );

		Check( "tvdisplay: the terrain vehicle's date has no hour, as the original's 01-01-4620", System.Text.RegularExpressions.Regex.IsMatch( date, @"^\d\d-\d\d-\d{4}$" ), date );
		Check( "tvdisplay: the bearing to the ship is spelled out, as the original's NORTH", System.Array.IndexOf( words, bearing ) >= 0, ship );

		Finish( "scenario=tvdisplay date=" + date + " ship=" + ship, 0 );
	}

	// ---------------------------------------------------------------- phase 7: the coordinates above the main view after the terrain vehicle

	IEnumerator ScenarioCoordinateBar()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var done = new bool[ 1 ];

		EnsureCrew();

		// on planet 92 of Arth, in the terrain vehicle
		yield return LandInTheArthSystemAt( 92, Vector3.zero, done );
		yield return Frames( 10 );

		var onTheSurface = controller.m_viewport.m_label.text;

		// back into orbit, as the terrain vehicle's return does when it is lost (the ship does not move on the way)
		controller.m_disembarked.ReturnToOrbit();

		yield return Frames( 10 );

		var inOrbit = controller.m_viewport.m_label.text;
		var locationInOrbit = playerData.m_general.m_location;

		// and out of orbit into the star system
		controller.SwitchLocation( PD_General.Location.StarSystem );

		yield return Frames( 10 );

		var inTheSystem = controller.m_viewport.m_label.text;

		Log( "coordinatebar: on the surface '" + onTheSurface + "', in orbit (" + locationInOrbit + ") '" + inOrbit + "', in the star system '" + inTheSystem + "'" );

		Check( "coordinatebar: on the surface the bar has the terrain vehicle's place (control)", onTheSurface.Contains( " x " ), onTheSurface );
		Check( "coordinatebar: back in orbit the bar has the ship's coordinates again, as the original's 125 100", ( locationInOrbit == PD_General.Location.InOrbit ) && System.Text.RegularExpressions.Regex.IsMatch( inOrbit, @"^\d+ +\d+$" ), locationInOrbit + ": " + inOrbit );
		Check( "coordinatebar: in the star system the bar has the ship's coordinates", System.Text.RegularExpressions.Regex.IsMatch( inTheSystem, @"^\d+ +\d+$" ), inTheSystem );

		Finish( "scenario=coordinatebar", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the Status display in the original's words

	IEnumerator ScenarioStatusDisplay()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var displays = controller.m_displayController;
		var ship = playerData.m_playerShip;

		EnsureCrew();

		EnterStarSystem( DataController.m_instance.m_gameData.m_misc.m_arthStarId );

		yield return Frames( 10 );

		// a ship with no shields and no weapons, at 90% of its hull, with 20 cubic meters of Endurium
		ship.m_shieldingClass = 0;
		ship.m_missileLauncherClass = 0;
		ship.m_laserCannonClass = 0;
		ship.m_armorPoints = Mathf.RoundToInt( ship.GetMaximumArmorPoints() * 0.9f );

		var endurium = ship.m_elementStorage.Find( 5 );

		if ( endurium != null )
		{
			ship.RemoveElement( 5, endurium.m_volume );
		}

		ship.AddElement( 5, 200 );

		displays.ChangeDisplay( displays.m_statusDisplay );

		yield return Frames( 10 );

		var values = displays.m_statusDisplay.m_values.text.Split( '\n' );
		var shown = string.Join( " / ", values );

		// the rows: date, damage, cargo, energy, shields, weapons
		var damage = ( values.Length > 1 ) ? values[ 1 ] : "";
		var cargo = ( values.Length > 2 ) ? values[ 2 ] : "";
		var energy = ( values.Length > 3 ) ? values[ 3 ] : "";
		var shields = ( values.Length > 4 ) ? values[ 4 ] : "";
		var weapons = ( values.Length > 5 ) ? values[ 5 ] : "";

		// ---- the damage words over the hull (the original's table)
		var words = "";

		foreach ( var percent in new[] { 100, 97, 85, 60, 30, 10 } )
		{
			ship.m_armorPoints = Mathf.FloorToInt( ship.GetMaximumArmorPoints() * percent / 100.0f );

			yield return Frames( 3 );

			var rows = displays.m_statusDisplay.m_values.text.Split( '\n' );

			words += percent + "%=" + ( ( rows.Length > 1 ) ? System.Text.RegularExpressions.Regex.Replace( rows[ 1 ], "<[^>]*>", "" ) : "" ) + " ";
		}

		Log( "statusdisplay: " + shown + " || damage over the hull: " + words );

		Check( "statusdisplay: a ship with no shields reads Down and one with no weapons Unarmed, as the original", ( shields == "Down" ) && ( weapons == "Unarmed" ), shields + " / " + weapons );
		Check( "statusdisplay: energy has four figures and no unit, and cargo is a whole percent", energy.Contains( ">20.00<" ) && System.Text.RegularExpressions.Regex.IsMatch( cargo, @">\d+ %<" ), energy + " / " + cargo );
		Check( "statusdisplay: damage is the original's word for what is left of the hull", words.Trim() == "100%=None 97%=Minimal 85%=Slight 60%=Moderate 30%=Heavy 10%=Critical", words );

		Finish( "scenario=statusdisplay", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the distress call in the original's words, with its confirmation

	// presses No (0) or Yes (1) of the distress call's confirmation - false if the console is not asking it
	static bool PressDistressChoice( int buttonIndex )
	{
		ButtonController.ButtonSet distressSet;

		try
		{
			distressSet = (ButtonController.ButtonSet) Enum.Parse( typeof( ButtonController.ButtonSet ), "Distress" );
		}
		catch ( ArgumentException )
		{
			return false;
		}

		if ( SpaceflightController.m_instance.m_buttonController.GetCurrentButtonSet() != distressSet )
		{
			return false;
		}

		PressButton( distressSet, buttonIndex );

		return true;
	}

	IEnumerator ScenarioDistressConfirm()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var buttonController = controller.m_buttonController;

		EnsureCrew();

		// a ship that survives whatever it meets
		playerData.m_playerShip.m_armorPoints = 100000;

		// ---- 1. in the docking bay: the question, then Yes
		controller.SwitchLocation( PD_General.Location.DockingBay );

		yield return Frames( 10 );

		var balanceBefore = playerData.m_bank.m_currentBalance;

		PressButton( ButtonController.ButtonSet.CommunicationsA, 1 );

		yield return Frames( 2 );

		var question = MessageList();
		var questionSet = buttonController.GetCurrentButtonSet().ToString();
		var answeredAtHome = PressDistressChoice( 1 );

		yield return Frames( 2 );

		var atHome = MessageList();
		var locationAtHome = playerData.m_general.m_location;
		var paidAtHome = balanceBefore - playerData.m_bank.m_currentBalance;

		// ---- 2. in the star system: the question, then No
		EnterStarSystem( gameData.m_misc.m_arthStarId );

		yield return Frames( 10 );

		PressButton( ButtonController.ButtonSet.CommunicationsA, 1 );

		yield return Frames( 2 );

		var answeredNo = PressDistressChoice( 0 );

		yield return Frames( 2 );

		var setAfterNo = buttonController.GetCurrentButtonSet();
		var locationAfterNo = playerData.m_general.m_location;
		var paidAfterNo = balanceBefore - playerData.m_bank.m_currentBalance;

		// ---- 3. in the star system again: the question, then Yes - towed home
		PressButton( ButtonController.ButtonSet.CommunicationsA, 1 );

		yield return Frames( 2 );

		var answeredYes = PressDistressChoice( 1 );

		yield return Frames( 10 );

		var towed = MessageList();
		var locationTowed = playerData.m_general.m_location;
		var paidTowed = balanceBefore - playerData.m_bank.m_currentBalance;

		// ---- 4. in an encounter: the question, then Yes - jammed
		EnterStarSystem( gameData.m_misc.m_arthStarId );

		yield return Frames( 10 );

		EnterEncounter( FindEncounter( 1, 6, 3, 0 ) );

		yield return Frames( 10 );

		var balanceInEncounter = playerData.m_bank.m_currentBalance;

		PressButton( ButtonController.ButtonSet.CommunicationsB, 1 );

		yield return Frames( 2 );

		var answeredInEncounter = PressDistressChoice( 1 );

		yield return Frames( 2 );

		var jammed = MessageList();
		var locationJammed = playerData.m_general.m_location;
		var paidJammed = balanceInEncounter - playerData.m_bank.m_currentBalance;

		Log( "distressconfirm: in the docking bay the console asks (" + questionSet + "): " + question + " | Yes (" + answeredAtHome + "): " + atHome + " | " + locationAtHome + ", paid " + paidAtHome );
		Log( "distressconfirm: in the star system No (" + answeredNo + "): buttons " + setAfterNo + ", " + locationAfterNo + ", paid " + paidAfterNo );
		Log( "distressconfirm: in the star system Yes (" + answeredYes + "): " + towed + " | " + locationTowed + ", paid " + paidTowed );
		Log( "distressconfirm: in an encounter Yes (" + answeredInEncounter + "): " + jammed + " | " + locationJammed + ", paid " + paidJammed );

		Check( "distressconfirm: Distress asks to confirm the call, as the original's TRANSMIT EMERGENCY DISTRESS CALL / CONFIRM", question.Contains( "Transmit emergency distress call" ) && ( questionSet == "Distress" ), questionSet + ": " + question );
		Check( "distressconfirm: confirmed in the docking bay the answer is We're already home!, and nothing is paid", answeredAtHome && atHome.Contains( "We're already home!" ) && ( locationAtHome == PD_General.Location.DockingBay ) && ( paidAtHome == 0 ), answeredAtHome + ": " + atHome + " | paid " + paidAtHome );
		Check( "distressconfirm: No puts the communications buttons back and nothing happens", answeredNo && ( setAfterNo == ButtonController.ButtonSet.CommunicationsA ) && ( locationAfterNo == PD_General.Location.StarSystem ) && ( paidAfterNo == 0 ), answeredNo + ": " + setAfterNo + ", " + locationAfterNo + ", paid " + paidAfterNo );
		Check( "distressconfirm: confirmed in space the ship is towed home with the original's words, and the towing is paid", answeredYes && ( locationTowed == PD_General.Location.DockingBay ) && towed.Contains( "Initiating cryogenic stasis" ) && towed.Contains( "Retrieval complete, crew revived" ) && towed.Contains( "Towing charges" ) && ( paidTowed > 0 ), answeredYes + ": " + locationTowed + ", paid " + paidTowed + " | " + towed );
		Check( "distressconfirm: confirmed in an encounter the transmission is jammed, as the original", answeredInEncounter && jammed.Contains( "Transmission is being jammed" ) && ( locationJammed == PD_General.Location.Encounter ) && ( paidJammed == 0 ), answeredInEncounter + ": " + jammed + " | " + locationJammed + ", paid " + paidJammed );

		Finish( "scenario=distressconfirm", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the messages of travel in a star system in the original's words

	// put the ship at a place in the star system it is in
	static void PlaceShipInTheSystem( Vector3 position )
	{
		DataController.m_instance.m_playerData.m_general.m_coordinates = position;
		DataController.m_instance.m_playerData.m_general.m_currentSpeed = 0.0f;

		SpaceflightController.m_instance.m_playerShip.transform.position = position;
	}

	IEnumerator ScenarioSystemTravel()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;
		var starSystem = controller.m_starSystem;

		EnsureCrew();

		// a ship that survives whatever it meets
		playerData.m_playerShip.m_armorPoints = 100000;

		// ---- 1. just launched from the Starport, then Maneuver
		controller.SwitchLocation( PD_General.Location.JustLaunched );

		var end = Time.realtimeSinceStartup + 60.0f;

		while ( ( Time.realtimeSinceStartup < end ) && starSystem.GeneratingPlanets() )
		{
			yield return null;
		}

		yield return Frames( 10 );

		controller.m_messages.Clear();

		PressButton( ButtonController.ButtonSet.NavigationA, 0 );

		yield return Frames( 10 );

		var launched = MessageList();
		var locationAfterManeuver = playerData.m_general.m_location;

		// ---- 2. over Arth, then away from every planet, then over another planet of Arth's system
		var arth = starSystem.GetPlanetController( gameData.m_misc.m_arthPlanetId );
		var other = starSystem.GetPlanetController( 94 );

		PlaceShipInTheSystem( arth.transform.localPosition );

		yield return Frames( 5 );

		var overArth = MessageList();

		// the place on a circle around the sun furthest from every planet
		var away = Vector3.zero;
		var awayDistance = 0.0f;

		for ( var angle = 0; angle < 360; angle += 5 )
		{
			var place = Quaternion.Euler( 0.0f, angle, 0.0f ) * Vector3.forward * 4000.0f;
			var nearest = float.MaxValue;

			foreach ( var planet in starSystem.m_planetController )
			{
				if ( planet.gameObject.activeInHierarchy )
				{
					nearest = Mathf.Min( nearest, Vector3.Distance( place, planet.transform.localPosition ) );
				}
			}

			if ( nearest > awayDistance )
			{
				awayDistance = nearest;
				away = place;
			}
		}

		PlaceShipInTheSystem( away );

		yield return Frames( 5 );

		var awayText = MessageList();

		PlaceShipInTheSystem( other.transform.localPosition );

		yield return Frames( 5 );

		var overPlanet = MessageList();

		// ---- 3. into orbit around it, and out again
		ConsoleFrameWith( "m_submit" );

		yield return WaitForLocation( PD_General.Location.InOrbit, 15.0f );
		yield return Frames( 10 );

		var locationInOrbit = playerData.m_general.m_location;

		PressButton( ButtonController.ButtonSet.NavigationA, 0 );

		yield return WaitForLocation( PD_General.Location.StarSystem, 15.0f );
		yield return Frames( 10 );

		var leftOrbit = MessageList();

		// ---- 4. docking at the Starport
		PlaceShipInTheSystem( arth.transform.localPosition );

		yield return Frames( 5 );

		ConsoleFrameWith( "m_submit" );

		yield return WaitForLocation( PD_General.Location.DockingBay, 15.0f );
		yield return Frames( 10 );

		var docked = MessageList();
		var locationDocked = playerData.m_general.m_location;

		Log( "systemtravel: after Maneuver (" + locationAfterManeuver + "): " + launched );
		Log( "systemtravel: over Arth: " + overArth );
		Log( "systemtravel: " + awayDistance.ToString( "F0" ) + " from the nearest planet: " + awayText );
		Log( "systemtravel: over planet 94: " + overPlanet );
		Log( "systemtravel: out of orbit (" + locationInOrbit + " before): " + leftOrbit );
		Log( "systemtravel: docked (" + locationDocked + "): " + docked );

		Check( "systemtravel: Maneuver after the launch says Leaving orbit... and ...Commencing system travel, as the original", ( locationAfterManeuver == PD_General.Location.StarSystem ) && launched.Contains( "Leaving orbit..." ) && launched.Contains( "...Commencing system travel" ), launched );
		Check( "systemtravel: over Arth the ship is standing by to dock with Starport, as the original", overArth.Contains( "Standing by to dock with Starport" ) && !overArth.Contains( "to orbit" ), overArth );
		Check( "systemtravel: away from the planets the ship is outside orbital range, as the original", awayText.Contains( "Outside orbital range" ), awayText );
		Check( "systemtravel: over another planet the ship is standing by to orbit and the key is named, as the original", overPlanet.Contains( "Standing by to orbit" ) && overPlanet.Contains( "Press Enter to orbit" ), overPlanet );
		Check( "systemtravel: leaving orbit says Leaving orbit... and ...Commencing system travel, as the original", ( locationInOrbit == PD_General.Location.InOrbit ) && leftOrbit.Contains( "Leaving orbit..." ) && leftOrbit.Contains( "...Commencing system travel" ), locationInOrbit + ": " + leftOrbit );
		Check( "systemtravel: docking at the Starport says Closing docking bay doors, as the original", ( locationDocked == PD_General.Location.DockingBay ) && docked.Contains( "Closing docking bay doors" ), locationDocked + ": " + docked );

		Finish( "scenario=systemtravel", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the landing's lines in the original's order

	IEnumerator ScenarioLandingLines()
	{
		var playerData = DataController.m_instance.m_playerData;
		var buttonController = SpaceflightController.m_instance.m_buttonController;

		EnsureCrew();

		// planet 94 is a small rock planet in the arth system
		yield return EnterOrbit( 94 );

		SpaceflightController.m_instance.m_messages.Clear();

		// Land, then Descend
		PressButton( ButtonController.ButtonSet.CommandB, 0 );

		yield return Frames( 5 );

		PressButton( ButtonController.ButtonSet.Land, 1 );

		// every line said during the landing, in the order it first came up, with where the ship was then
		var lines = new List<string>();
		var end = Time.realtimeSinceStartup + 60.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			foreach ( var message in playerData.m_general.m_messageList )
			{
				var line = System.Text.RegularExpressions.Regex.Replace( message, "<[^>]*>", "" );

				if ( !lines.Exists( l => l.StartsWith( line + " @" ) ) )
				{
					lines.Add( line + " @" + playerData.m_general.m_location );
				}
			}

			if ( ( playerData.m_general.m_location == PD_General.Location.Planetside ) && ( buttonController.GetCurrentButtonSet() == ButtonController.ButtonSet.CommandA ) )
			{
				break;
			}

			yield return null;
		}

		var said = string.Join( " | ", lines );
		var autopilot = lines.FindIndex( l => l.StartsWith( "Autopilot engaged. Descending..." ) );
		var topography = lines.FindIndex( l => l.StartsWith( "Topography net locked on." ) );
		var safeLanding = lines.FindIndex( l => l.StartsWith( "Safe landing, captain." ) );

		Log( "landinglines: " + said );

		Check( "landinglines: the landing says the original's lines and no others (no Retro rockets firing)", !said.Contains( "Retro rockets" ) && ( autopilot >= 0 ) && ( topography >= 0 ) && ( safeLanding >= 0 ), said );
		Check( "landinglines: Topography net locked on comes after Autopilot engaged, once the terrain is there, as the original's", ( topography > autopilot ) && ( safeLanding > topography ) && ( topography >= 0 ) && lines[ topography ].EndsWith( "@Planetside" ), said );

		Finish( "scenario=landinglines", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the captain is told when the aliens raise or lower their shields, or arm or disarm

	IEnumerator ScenarioAlienShields()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// a ship that survives whatever it meets
		playerData.m_playerShip.m_armorPoints = 100000;

		// a mechan encounter: its scouts have shields and missiles, and the mechans have no mood, so their stance is their posture
		var mechanId = -1;

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			if ( gameData.m_encounterList[ i ].m_race == GameData.Race.Mechan )
			{
				mechanId = i;

				break;
			}
		}

		EnterStarSystem( gameData.m_misc.m_arthStarId );

		yield return Frames( 10 );

		EnterEncounter( mechanId );

		yield return Frames( 10 );

		var pdEncounter = controller.m_encounter.m_pdEncounter;
		var atTheStart = MessageList();

		// ---- neutral to hostile: they arm (their shields were already up)
		controller.m_messages.Clear();

		pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

		yield return Frames( 5 );

		var toHostile = MessageList();

		// ---- hostile to friendly: they lower their shields and disarm
		controller.m_messages.Clear();

		pdEncounter.m_alienStance = GD_Comm.Stance.Friendly;

		yield return Frames( 5 );

		var toFriendly = MessageList();

		// ---- friendly to neutral: they raise their shields
		controller.m_messages.Clear();

		pdEncounter.m_alienStance = GD_Comm.Stance.Neutral;

		yield return Frames( 5 );

		var toNeutral = MessageList();

		Log( "alienshields: at the start: " + atTheStart );
		Log( "alienshields: to hostile (" + pdEncounter.m_alienStance + " now): " + toHostile + " || to friendly: " + toFriendly + " || to neutral: " + toNeutral );

		Check( "alienshields: the state the encounter begins with is taken without a word", !atTheStart.Contains( "they've" ), atTheStart );
		Check( "alienshields: turning hostile, they arm their weapons and nothing is said of the shields they already had up", toHostile.Contains( "Captain, they've armed their weapons." ) && !toHostile.Contains( "shields" ), toHostile );
		Check( "alienshields: turning friendly, they lower their shields and disarm, as the original says", toFriendly.Contains( "Captain, they've lowered their shields." ) && toFriendly.Contains( "Captain, they've disarmed their weapons." ), toFriendly );
		Check( "alienshields: turning neutral again, they raise their shields", toNeutral.Contains( "Captain, they've raised their shields." ) && !toNeutral.Contains( "weapons" ), toNeutral );

		Finish( "scenario=alienshields", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the ancient ruins STRINFO's editors place, with their Endurium

	// lands the terrain vehicle at a place on a planet of any star system
	static IEnumerator LandOnPlanetAt( int starId, int planetId, Vector3 place, bool[] done )
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		// (the port's "latitude" is the east-west coordinate - see Tools.LatLongToWorldCoordinates)
		Tools.WorldToLatLongCoordinates( place, out var x, out var z );

		playerData.m_general.m_selectedLatitude = x;
		playerData.m_general.m_selectedLongitude = z;

		yield return GoIntoOrbit( starId, planetId );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );
		yield return DisembarkNow( done );
	}

	// the Endurium of the ruins within the given distance of a place, nearest first, in tenths of a cubic meter ("-" for a ruin that holds none)
	static string EnduriumInRuinsNear( Vector3 place, float distance )
	{
		var found = new List<KeyValuePair<float, string>>();

		foreach ( var ruin in RuinsOnTheGround() )
		{
			var delta = ruin.transform.position - place;

			delta.y = 0.0f;

			if ( delta.magnitude > distance )
			{
				continue;
			}

			var terrainRuin = ruin as TerrainRuin;
			var endurium = 0;

			if ( terrainRuin != null )
			{
				foreach ( var item in terrainRuin.m_randomItems )
				{
					endurium += item.m_enduriumVolume;
				}
			}

			found.Add( new KeyValuePair<float, string>( delta.magnitude, ( endurium > 0 ) ? endurium.ToString() : "-" ) );
		}

		found.Sort( ( a, b ) => a.Key.CompareTo( b.Key ) );

		return string.Join( ",", found.ConvertAll( f => f.Value ) );
	}

	IEnumerator ScenarioAncientSites()
	{
		var gameData = DataController.m_instance.m_gameData;
		var done = new bool[ 1 ];

		EnsureCrew();

		// ---- 1. the Mechans' ice planet: planet 5 of 145,107, a ruin with 3.1 cubic meters at 36N x 90E
		var mechanStar = StarAt( 145, 107 );
		var mechanPlanet = gameData.FindPlanetFromSun( 145, 107, 5 );
		var mechanSite = Tools.LatLongToWorldCoordinates( 90.0f, 36.0f );

		yield return LandOnPlanetAt( mechanStar, mechanPlanet, mechanSite, done );

		var mechanRuins = done[ 0 ] ? EnduriumInRuinsNear( mechanSite, 100.0f ) : "(never got into the terrain vehicle)";

		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 5 );

		// ---- 2. deep in Elowan space: planet 2 of 154,62, two ruins with 3.1 and 5.9 cubic meters at 42S x 13W
		var thrynnStar = StarAt( 154, 62 );
		var thrynnPlanet = gameData.FindPlanetFromSun( 154, 62, 2 );
		var thrynnSite = Tools.LatLongToWorldCoordinates( -13.0f, -42.0f );

		yield return LandOnPlanetAt( thrynnStar, thrynnPlanet, thrynnSite, done );

		var thrynnRuins = done[ 0 ] ? EnduriumInRuinsNear( thrynnSite, 100.0f ) : "(never got into the terrain vehicle)";

		Log( "ancientsites: planet " + mechanPlanet + " of star " + mechanStar + ", ruins near 36N x 90E (Endurium in tenths): [" + mechanRuins + "] | planet " + thrynnPlanet + " of star " + thrynnStar + ", ruins near 42S x 13W: [" + thrynnRuins + "]" );

		Check( "ancientsites: the Mechans' ice planet has an ancient ruin at 36N x 90E with 3.1 cubic meters of Endurium and nothing else near it", mechanRuins == "31", mechanRuins );
		Check( "ancientsites: planet 2 of 154,62 has two ancient ruins at 42S x 13W with 3.1 and 5.9 cubic meters of Endurium", thrynnRuins == "31,59", thrynnRuins );

		Finish( "scenario=ancientsites", 0 );
	}

	// ---------------------------------------------------------------- phase 7: the disembark's lines in the original's words and order

	IEnumerator ScenarioDisembarkLines()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;

		EnsureCrew();

		// on the ground of planet 94 of Arth, in the ship
		yield return GoIntoOrbit( 25, 94 );

		controller.m_planetside.UpdateTerrainGridNow();
		controller.SwitchLocation( PD_General.Location.Planetside );

		yield return Frames( 10 );

		controller.m_messages.Clear();

		// Disembark (the second button of the command set on a planet), every line in the order it first came up, until the terrain vehicle is out
		PressButton( ButtonController.ButtonSet.CommandA, 1 );

		var lines = new List<string>();
		var end = Time.realtimeSinceStartup + 15.0f;

		while ( Time.realtimeSinceStartup < end )
		{
			foreach ( var message in playerData.m_general.m_messageList )
			{
				var line = System.Text.RegularExpressions.Regex.Replace( message, "<[^>]*>", "" );

				if ( !lines.Contains( line ) )
				{
					lines.Add( line );
				}
			}

			if ( playerData.m_general.m_location == PD_General.Location.Disembarked )
			{
				break;
			}

			yield return null;
		}

		var said = string.Join( " | ", lines );
		var expected = "Standby...scanning planet. | Auto sampling devices activated. | Initiating hull integrity check. | Hull check complete. | Activating terrain vehicle.";

		Log( "disembarklines: (" + playerData.m_general.m_location + ") " + said );

		Check( "disembarklines: disembarking says the original's lines in its order, with Hull check complete", ( playerData.m_general.m_location == PD_General.Location.Disembarked ) && said.StartsWith( expected ), said );

		Finish( "scenario=disembarklines", 0 );
	}

	// ---------------------------------------------------------------- batch 1 (starport side)

	struct SellResult
	{
		public string m_state;
		public string m_error;
		public int m_volume;
		public int m_balance;
		public string m_thrown;

		public override string ToString()
		{
			return "state=" + m_state + " error='" + m_error + "' endurium=" + m_volume + " balance=" + m_balance + " thrown=" + m_thrown;
		}
	}

	// type an amount into the trade depot's sell box for endurium, the way the player would
	static SellResult TrySell( TradeDepotPanel depot, string text )
	{
		var playerData = DataController.m_instance.m_playerData;
		var result = new SellResult { m_thrown = "none", m_error = "" };

		// back to the list of things the player can sell, with endurium selected
		Call( depot, "SwitchToSellItemState", true );

		var items = GetField( depot, "m_itemList" ) as IList;
		var index = -1;

		for ( var i = 0; i < items.Count; i++ )
		{
			if ( ( (int) GetField( items[ i ], "m_type" ) == 0 ) && ( (int) GetField( items[ i ], "m_id" ) == 5 ) )
			{
				index = i;
			}
		}

		if ( index < 0 )
		{
			result.m_thrown = "endurium is not in the sell list";
		}
		else
		{
			SetField( depot, "m_currentItemIndex", index );

			Call( depot, "SwitchToSellAmountState" );

			depot.m_amountInputField.text = text;

			try
			{
				depot.OnEndEdit();
			}
			catch ( Exception exception )
			{
				result.m_thrown = exception.GetType().Name;
			}
		}

		var fuel = playerData.m_playerShip.m_elementStorage.Find( 5 );

		result.m_state = GetField( depot, "m_currentState" ).ToString();
		result.m_error = ( result.m_state == "ErrorMessage" ) ? depot.m_errorMessageText.text : "";
		result.m_volume = ( fuel == null ) ? 0 : fuel.m_volume;
		result.m_balance = playerData.m_bank.m_currentBalance;

		return result;
	}

	IEnumerator ScenarioStarport()
	{
		var dataController = DataController.m_instance;
		var gameData = dataController.m_gameData;
		var playerData = dataController.m_playerData;
		var crewAssignment = playerData.m_crewAssignment;

		var personnelPanel = FindPanel<PersonnelPanel>();
		var crewAssignmentPanel = FindPanel<CrewAssignmentPanel>();
		var dockingBayPanel = FindPanel<DockingBayPanel>();
		var tradeDepotPanel = FindPanel<TradeDepotPanel>();

		Log( "panels found: personnel=" + ( personnelPanel != null ) + " crewAssignment=" + ( crewAssignmentPanel != null ) + " dockingBay=" + ( dockingBayPanel != null ) + " tradeDepot=" + ( tradeDepotPanel != null ) );

		// ---- a crewmember with medicine maxed out and nothing else trained, assigned to two roles
		var race = gameData.m_crewRaceList[ 0 ];
		var file = playerData.m_personnel.CreateNewPersonnel();

		file.m_name = "Probe";
		file.m_crewRaceId = 0;
		file.m_vitality = 100.0f;
		file.m_medicine = race.GetMaximumSkill( 4 );

		playerData.m_personnel.m_personnelList.Add( file );

		crewAssignment.Assign( PD_CrewAssignment.Role.Captain, file.m_fileId );
		crewAssignment.Assign( PD_CrewAssignment.Role.Doctor, file.m_fileId );

		yield return OpenPanel( personnelPanel, "personnel" );

		// ---- M4: training is allowed while any skill is below its maximum
		personnelPanel.TrainClicked();
		yield return Frames( 3 );

		var stateAfterTrain = GetField( personnelPanel, "m_currentState" ).ToString();

		Check( "M4 training allowed when only medicine is maxed", stateAfterTrain == "TrainCrewmember", "medicine " + file.m_medicine + "/" + race.GetMaximumSkill( 4 ) + ", other skills 0: panel state after Train = " + stateAfterTrain );

		personnelPanel.CancelClicked();
		yield return Frames( 3 );

		// ---- H3: deleting the crewmember through the panel unassigns their roles
		personnelPanel.DeleteClicked();
		yield return Frames( 3 );

		personnelPanel.YesClicked();
		yield return Frames( 3 );

		Check( "H3 deleting crew unassigns their roles", ( playerData.m_personnel.m_personnelList.Count == 0 ) && !crewAssignment.IsAssigned( PD_CrewAssignment.Role.Captain ) && !crewAssignment.IsAssigned( PD_CrewAssignment.Role.Doctor ),
			"personnel left=" + playerData.m_personnel.m_personnelList.Count + ", captain assigned=" + crewAssignment.IsAssigned( PD_CrewAssignment.Role.Captain ) + ", doctor assigned=" + crewAssignment.IsAssigned( PD_CrewAssignment.Role.Doctor ) );

		yield return ClosePanel( personnelPanel, "personnel" );

		// ---- H3: crew assignment and the docking bay open after the delete
		var exceptionsBeforePanels = s_exceptionCount;

		yield return OpenPanel( crewAssignmentPanel, "crew assignment" );
		yield return ClosePanel( crewAssignmentPanel, "crew assignment" );
		yield return OpenPanel( dockingBayPanel, "docking bay" );
		yield return ClosePanel( dockingBayPanel, "docking bay" );

		Check( "H3 crew assignment and docking bay open after the delete", s_exceptionCount == exceptionsBeforePanels, "exceptions while opening and closing both panels: " + ( s_exceptionCount - exceptionsBeforePanels ) );

		// ---- H4 and M3: the trade depot sell box
		yield return OpenPanel( tradeDepotPanel, "trade depot" );

		tradeDepotPanel.SellClicked();
		yield return Frames( 3 );

		// what the Starport pays for Endurium (the same as it sells it for, and rising over time, since roadmap 1.5)
		var value = ElementSaleValue( 5 );

		var baseline = TrySell( tradeDepotPanel, "abc" );
		var tooMuch = TrySell( tradeDepotPanel, "25" );

		Check( "H4 cannot sell more than the hold contains", ( tooMuch.m_state == "ErrorMessage" ) && ( tooMuch.m_volume == baseline.m_volume ) && ( tooMuch.m_balance == baseline.m_balance ) && ( tooMuch.m_thrown == "none" ),
			"hold has " + baseline.m_volume + " tenths, typed 25: " + tooMuch );

		var minus = TrySell( tradeDepotPanel, "-" );

		Check( "M3 a lone minus sign is ignored", ( minus.m_thrown == "none" ) && ( minus.m_volume == baseline.m_volume ) && ( minus.m_balance == baseline.m_balance ), "typed '-': " + minus );

		var fraction = TrySell( tradeDepotPanel, "5.25" );

		Check( "M3 5.25 sells 5.2 cubic meters", ( fraction.m_thrown == "none" ) && ( fraction.m_volume == baseline.m_volume - 52 ) && ( fraction.m_balance == baseline.m_balance + value * 52 / 10 ),
			"typed 5.25 at " + value + " per cubic meter: " + fraction + " (expected endurium " + ( baseline.m_volume - 52 ) + ", balance " + ( baseline.m_balance + value * 52 / 10 ) + ")" );

		var comma = TrySell( tradeDepotPanel, "5,5" );

		Check( "M3 a comma works as the decimal point", ( comma.m_thrown == "none" ) && ( comma.m_volume == fraction.m_volume - 55 ) && ( comma.m_balance == fraction.m_balance + value * 55 / 10 ),
			"typed 5,5: " + comma + " (expected endurium " + ( fraction.m_volume - 55 ) + ")" );

		Call( tradeDepotPanel, "SwitchToMenuBarState" );

		yield return ClosePanel( tradeDepotPanel, "trade depot" );

		// ---- negative control: a role that points at a deleted file is the state the old delete left behind
		var ghost = playerData.m_personnel.CreateNewPersonnel();

		ghost.m_name = "Ghost";
		ghost.m_vitality = 100.0f;

		playerData.m_personnel.m_personnelList.Add( ghost );
		crewAssignment.Assign( PD_CrewAssignment.Role.Captain, ghost.m_fileId );
		playerData.m_personnel.m_personnelList.Remove( ghost );

		var exceptionsBeforeGhost = s_exceptionCount;

		yield return OpenPanel( dockingBayPanel, "docking bay with a dangling role" );

		var ghostExceptions = s_exceptionCount - exceptionsBeforeGhost;

		yield return ClosePanel( dockingBayPanel, "docking bay with a dangling role" );

		Log( "negative control: opening the docking bay with a role pointing at a deleted file raised " + ghostExceptions + " exception(s)" );

		// ---- H3: loading repairs a save that is already in that state
		crewAssignment.UnassignMissingCrew( playerData.m_personnel );

		var exceptionsBeforeRepairCheck = s_exceptionCount;

		yield return OpenPanel( dockingBayPanel, "docking bay after the repair" );
		yield return ClosePanel( dockingBayPanel, "docking bay after the repair" );

		Check( "H3 the load-time repair clears a dangling role", !crewAssignment.IsAssigned( PD_CrewAssignment.Role.Captain ) && ( s_exceptionCount == exceptionsBeforeRepairCheck ) && ( ghostExceptions > 0 ),
			"captain assigned after repair=" + crewAssignment.IsAssigned( PD_CrewAssignment.Role.Captain ) + ", exceptions opening the docking bay: " + ghostExceptions + " before the repair, " + ( s_exceptionCount - exceptionsBeforeRepairCheck ) + " after" );

		Finish( "scenario=starport passed=" + s_checksPassed + " failed=" + s_checksFailed, 0 );
	}
}

#endif
