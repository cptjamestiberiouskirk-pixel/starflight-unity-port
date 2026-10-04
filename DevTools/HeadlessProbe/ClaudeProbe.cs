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
