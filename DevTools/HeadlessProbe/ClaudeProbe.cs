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
using UnityEngine.SceneManagement;

public class ClaudeProbe : MonoBehaviour
{
	const string c_activeKey = "ClaudeProbe.Active";
	const string c_startKey = "ClaudeProbe.Start";
	const string c_tag = "[ClaudeProbe] ";

	static int s_exceptionCount;
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

			case "visual":
				yield return ScenarioVisual();
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

		// a complete scan of an elowan scout, then the science officer's analysis
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
		Check( "planet readout reads minerals", sensors.m_bioMinText.text.Contains( "Min: " ) && !sensors.m_bioMinText.text.Contains( "Energy" ), "readout=[" + sensors.m_bioMinText.text + "]" );

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

		// an empty window is black inside a magenta border, as the original shows an unidentified object
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

		Check( "unknown object scan shows a black window inside a magenta border", ( emptyImage != null ) && ( panelImage != null ) && emptyImage.gameObject.activeInHierarchy && ( emptyImage.color == Color.black ) && ( Mathf.Abs( panelImage.rectTransform.rect.width - emptyImage.rectTransform.rect.width - 32.0f ) < 0.5f ) && ( Mathf.Abs( panelImage.rectTransform.rect.height - emptyImage.rectTransform.rect.height - 32.0f ) < 0.5f ), emptyText );
		Check( "the black inside is drawn over the panel and under the pictures", ( emptyTransform != null ) && ( panelTransform != null ) && ( panelTransform.GetSiblingIndex() < emptyTransform.GetSiblingIndex() ) && ( emptyTransform.GetSiblingIndex() < sensors.m_backgroundImage.transform.GetSiblingIndex() ), emptyText );

		// the original shows no readout for an unidentified object
		Check( "unknown object scan shows no readout", !sensors.m_massText.gameObject.activeInHierarchy && !sensors.m_bioMinText.gameObject.activeInHierarchy, "mass=" + sensors.m_massText.gameObject.activeInHierarchy + " bio=" + sensors.m_bioMinText.gameObject.activeInHierarchy );

		// a scan with a picture, and a display that has just been shown, keep the magenta window
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

		// the readout of a finished scan: a vessel's mass in tons, its bio density and its energy, as the original shows them
		// (spemin scout 4x10^2 bio 100 energy 100, mechan scout 2x10^2 bio 0 energy 100, minstrel "2" with no power of ten, bio 100 energy 0)
		var scan = "";

		yield return ScanShip( 2, false, true, result => scan = result );
		Check( "spemin scout readout: 4x10^2 tons, bio 100, energy 100", scan.Contains( "finished=True" ) && scan.Contains( "4x10<sup>2</sup>" ) && scan.Contains( "Bio: <color=\"white\">100%</color>" ) && scan.Contains( "Energy: <color=\"white\">100%</color>" ), scan );

		yield return ScanShip( 4, false, true, result => scan = result );
		Check( "mechan scout readout: 2x10^2 tons, bio 0, energy 100", scan.Contains( "finished=True" ) && scan.Contains( "2x10<sup>2</sup>" ) && scan.Contains( "Bio: <color=\"white\">0%</color>" ) && scan.Contains( "Energy: <color=\"white\">100%</color>" ), scan );

		yield return ScanShip( 22, false, true, result => scan = result );
		Check( "minstrel readout: 2 tons with no power of ten, bio 100, energy 0", scan.Contains( "finished=True" ) && scan.Contains( "<color=\"white\">2</color> Tons" ) && !scan.Contains( "<sup>" ) && scan.Contains( "Bio: <color=\"white\">100%</color>" ) && scan.Contains( "Energy: <color=\"white\">0%</color>" ), scan );

		// a wreck has the mass of its vessel in tons (it was one power of ten too high) and its salvage potential as minerals
		yield return ScanShip( 2, true, true, result => scan = result );
		Check( "spemin scout wreck readout: 4x10^2 tons, min", scan.Contains( "finished=True" ) && scan.Contains( "4x10<sup>2</sup>" ) && scan.Contains( "Min: " ) && !scan.Contains( "Energy" ), scan );

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
				var expected = Mathf.CeilToInt( ( Mathf.Max( 1, Mathf.RoundToInt( vessel.m_armorClass * 100 ) ) + Mathf.Max( 0, vessel.m_shieldClass * 100 ) ) / 20.0f );
				var hits = HitsToKill( index, 20, 400 );

				Log( vessel.m_name + " (armor " + vessel.m_armorClass + ", shield " + vessel.m_shieldClass + "): " + ( ( hits < 0 ) ? "still alive after 400 hits of 20" : ( "destroyed by hit " + hits + " of 20" ) ) + ", 100 points per class would be " + expected );

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

		Check( "M12 aliens that have not been fired on leave the player alone", ( speminStanceAtStart == "Neutral" ) && ( speminDamageBefore == 0 ), "stance " + speminStanceAtStart + ", damage " + speminDamageBefore );
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

		Check( "M12 leaving the encounter ends the hostility", ( stanceAfterReturn == "Neutral" ) && ( damageAfterReturn == 0 ), "stance " + stanceAfterReturn + ", attacked flag " + attackedAfterReturn + ", damage in 4 s " + damageAfterReturn );

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

		combat.SetTarget( FirstLivingAlien() );

		BringAliensClose();

		var enterpriseFiredOn = combat.FirePlayerLaser();

		yield return DamageTaken( 8.0f, value => damage = value );

		var enterpriseDamageAfter = damage;

		Log( "M12 enterprise encounter " + enterpriseId + ": stance " + enterpriseStance + ", damage in 4 s before firing " + enterpriseDamageBefore + " | fired on=" + enterpriseFiredOn + " stance " + Stance() + ", damage in the next 8 s " + enterpriseDamageAfter );

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

		new RepairButton().Execute();

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

		deposit.transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

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

		// the message box during the landing: every step once, in the order it happens
		var plainMessages = System.Text.RegularExpressions.Regex.Replace( messagesAfterLanding, "<[^>]+>", "" );

		Check( "M23 the landing reports each step once and in order", plainMessages == "Computing descent profile... / Autopilot engaged. Descending... / Topography net locked on. / Retro rockets firing... / Safe landing, captain.", plainMessages );

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

		messages.Clear();
		new RepairButton().Execute();

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

		Check( "M11 a bare hull at 200 of 250 shows 20% damage and a gauge at four fifths", hurtDamageLine.Contains( ">20% Hull Damage<" ) && Mathf.Approximately( hurtArmorGauge, 0.8f ), "status damage line '" + hurtDamageLine + "', armor gauge " + hurtArmorGauge.ToString( "F3" ) );
		Check( "M11 the engineer repairs a bare hull", repairsStarted && ( armorAfter10s == 220 ), "repair started " + repairsStarted + ", armor after 10 s " + armorAfter10s + " (was 200) | " + repairMessage );
		Check( "M11 the repair of a bare hull stops at its 250 points", ( armorWhenDone == 250 ) && !stillUnderWay && doneMessage.Contains( "completed" ), "armor when done " + armorWhenDone + ", still under way " + stillUnderWay + " | " + doneMessage );
		Check( "M11 an undamaged bare hull needs no repairs", !startedWithNothingToRepair && nothingToRepairMessage.Contains( "No repairs needed" ), "started " + startedWithNothingToRepair + " | " + nothingToRepairMessage );

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
		Check( "M11 class 2 armor at 300 of 750 shows 60% damage and a gauge at two fifths", class2HurtDamageLine.Contains( ">60% Hull Damage<" ) && Mathf.Approximately( class2HurtArmorGauge, 0.4f ), "status damage line '" + class2HurtDamageLine + "', armor gauge " + class2HurtArmorGauge.ToString( "F3" ) );
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

		sensors.m_scanType = (SensorsDisplay.ScanType) 6;

		new AnalysisButton().Execute();

		var smallVessel = MessageList();

		sensors.m_scanType = (SensorsDisplay.ScanType) 1;

		new AnalysisButton().Execute();

		var largeVessel = MessageList();

		sensors.m_hasSensorData = hadSensorData;
		sensors.m_scanType = scanType;

		var expectedSmall = ( (float) gameData.m_vesselList[ 6 ].m_mass / shipMass ).ToString( "F1", System.Globalization.CultureInfo.InvariantCulture );
		var expectedLarge = ( (float) gameData.m_vesselList[ 1 ].m_mass / shipMass ).ToString( "F1", System.Globalization.CultureInfo.InvariantCulture );

		Log( "cargo: our ship has a mass of " + shipMass + "; analysis of a vessel of mass " + gameData.m_vesselList[ 6 ].m_mass + ": " + smallVessel );
		Log( "cargo: analysis of a vessel of mass " + gameData.m_vesselList[ 1 ].m_mass + ": " + largeVessel );

		Check( "cargo: the analysis gives the size of a vessel with one decimal", smallVessel.Contains( ">" + expectedSmall + " times the size" ) && largeVessel.Contains( ">" + expectedLarge + " times the size" ), "expected " + expectedSmall + " and " + expectedLarge + " | " + smallVessel + " | " + largeVessel );

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
		first.transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

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
		second.transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

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

		otherSlot.m_general.m_location = PD_General.Location.Disembarked;

		Call( saveGamePanel, "UpdateDescriptions" );

		var descriptionTexts = GetField( saveGamePanel, "m_slotDescriptionText" ) as TMPro.TextMeshProUGUI[];
		var description = descriptionTexts[ 1 ].text;

		otherSlot.m_general.m_location = otherLocationBefore;

		Call( saveGamePanel, "UpdateDescriptions" );

		var opened = description.Split( new string[] { "<color=" }, StringSplitOptions.None ).Length - 1;
		var closed = description.Split( new string[] { "</color>" }, StringSplitOptions.None ).Length - 1;
		var locationLine = description.Split( '\n' )[ 1 ];

		Log( "save and data: the save panel's line for a game in the terrain vehicle: " + locationLine + " (" + opened + " colour tags opened, " + closed + " closed)" );

		Check( "save and data: the save panel names the location of a game in the terrain vehicle", ( opened == closed ) && locationLine.Contains( "Terrain Vehicle</color>" ), locationLine + " | opened " + opened + ", closed " + closed );

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

		near.transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;
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

		Log( "terrain: scan with " + nearName + " 2 away and " + farName + " 20 away: " + scanBefore );
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
		Check( "perframe: the status display still shows what changes", damagedText.Contains( "50% Hull Damage" ) && ( armedText != statusText ) && ( statusTextAtTheEnd == statusText ), "damaged: " + damagedText.Replace( '\n', '/' ) + " | back to: " + statusTextAtTheEnd.Replace( '\n', '/' ) );

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

		deposits[ 0 ].transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

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

		// ---- 1. the materials of a deposit that is picked up: its transporter effect makes its own copies of them
		yield return Frames( 5 );

		var materialsBefore = MaterialCount();

		new TVCargoButton().Execute();

		yield return Frames( 5 );

		var materialsDuring = MaterialCount();

		// the effect takes a second and a half, then the deposit is destroyed
		yield return new WaitForSecondsRealtime( 2.5f );

		var materialsAfter = MaterialCount();

		Log( "leaks: materials in memory: " + materialsBefore + " before the pickup, " + materialsDuring + " during the transporter effect, " + materialsAfter + " after the deposit is gone" );

		Check( "leaks: the materials of a transporter effect go with it", ( materialsDuring > materialsBefore ) && ( materialsAfter <= materialsBefore ), materialsBefore + " before, " + materialsDuring + " during, " + materialsAfter + " after" );

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

		first.transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

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
			deposits[ i ].transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

			new TVCargoButton().Execute();

			lastMessage = MessageList();

			pickups++;

			// out of the way again (a deposit that did not fit stays on the surface with what is left of it)
			deposits[ i ].transform.position += Vector3.right * 1000.0f;
		}

		var holdUsed = holdSize - playerData.m_terrainVehicle.GetRemainingVolume();

		// one more press with the hold full and a fresh deposit in reach
		deposits[ deposits.Length - 1 ].transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

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

		Check( "unmapped: within orbital range the player is told that the planet could not be mapped", rangeMessage.Contains( "within orbital range" ) && rangeMessage.Contains( "could not be mapped" ), rangeMessage );
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
		var toContainer = container.worldToLocalMatrix;
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

			// the debris: destroy the first ship and wait for its explosion to call back (1.5 s)
			if ( hasModel && ( debrisTemplates != null ) && ( vesselId < debrisTemplates.Length ) && ( debrisTemplates[ vesselId ] != null ) )
			{
				Kill( 0 );

				// catch the debris on the frame it appears and measure it in the ship's frame before it has tumbled
				var until = Time.realtimeSinceStartup + 3.0f;
				DebrisTumble tumble = null;

				while ( ( tumble == null ) && ( Time.realtimeSinceStartup < until ) )
				{
					yield return null;

					tumble = model.GetComponentInChildren<DebrisTumble>( false );
				}

				var debrisSize = ( tumble != null ) ? SizeInFrameOf( model.transform, tumble.gameObject ) : Vector3.zero;
				var debrisLongest = Mathf.Max( debrisSize.x, debrisSize.y, debrisSize.z );
				var ratio = ( longest > 0.0f ) ? ( debrisLongest / longest ) : 0.0f;
				var sameShape = true;

				for ( var axis = 0; axis < 3; axis++ )
				{
					sameShape &= Mathf.Abs( debrisSize[ axis ] - size[ axis ] ) <= longest * 0.1f;
				}

				sizes += "(debris " + ratio.ToString( "F2" ) + ( sameShape ? "" : " turned" ) + ")";

				Check( "vessel " + vesselId + " leaves debris of its own size", ( tumble != null ) && ( Mathf.Abs( ratio - 1.0f ) <= 0.1f ), "debris=" + debrisSize.ToString( "F1" ) + " ratio=" + ratio.ToString( "F2" ) );
				Check( "vessel " + vesselId + " leaves debris turned like the ship", ( tumble != null ) && sameShape, "debris=" + debrisSize.ToString( "F1" ) + " ship=" + size.ToString( "F1" ) );
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
		whole.transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

		new TVCargoButton().Execute();

		yield return Frames( 5 );

		var wholeTaken = whole.HasBeenPickedUp();

		// ---- 2. part of the second: the hold has room for 1 cubic meter only
		var room = playerData.m_terrainVehicle.GetRemainingVolume();

		playerData.m_terrainVehicle.AddElement( part.m_elementId, room - 10 );

		part.transform.position = terrainVehicle.transform.position + Vector3.forward * 2.0f;

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

		var value = gameData.m_elementList[ 5 ].m_actualValue;

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
