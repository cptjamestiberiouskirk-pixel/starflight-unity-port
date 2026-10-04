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

		// a recognizable amount of armor, shields down so that every hit goes to the hull
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
