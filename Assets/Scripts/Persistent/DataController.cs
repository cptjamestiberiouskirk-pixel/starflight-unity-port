using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Diagnostics;

public class DataController : MonoBehaviour
{
	// the number of save game slots
	public const int c_numSaveGameSlots = 5;

	// static reference to this instance
	public static DataController m_instance;
	public static string m_sceneToLoad = "Intro";

	// the name of the game data file
	public string m_gameDataFileName;
	
	// the name of the save game file
	public string m_playerDataFileName;

	// the loaded game data
	public GameData m_gameData;

	// the save game slots
	public PlayerData[] m_playerDataList;

	// the player data from the current save game slot
	public PlayerData m_playerData;

	// the active save game slot number
	public int m_activeSaveGameSlotNumber;

	// set this to switch to a different save game slot
	int m_targetSaveGameSlotNumber;

	// the save system
	private ISaveSystem _saveSystem;

	// unity awake
	void Awake()
	{
		// remember this instance to this
		m_instance = this;

		// initialize the save system
		_saveSystem = new JsonSaveSystem();
	}

	// unity start
	void Start()
	{
		// load the game data
		LoadGameData();

		// load the save game slots
		LoadPlayerDataList();

		// debug info
		UnityEngine.Debug.Log( "Loading scene " + m_sceneToLoad );

		// load the next scene
		SceneManager.LoadScene( m_sceneToLoad );
	}

	// unity late update
	void LateUpdate()
	{
		// if we want to switch to a different save game do it now and load the next scene
		if ( m_targetSaveGameSlotNumber != m_activeSaveGameSlotNumber )
		{
			// report the change
			UnityEngine.Debug.Log( "Switching to save game slot number " + m_targetSaveGameSlotNumber );

			// a lost game is never saved - put the slot we are leaving back to its last save first
			if ( IsLost( m_playerData ) )
			{
				ReloadActiveGame();
			}

			// make the current slot not the current game
			m_playerData.m_isCurrentGame = false;

			// save the active game in the old save game slot number
			SaveActiveGame();

			// update the active save game slot number
			m_activeSaveGameSlotNumber = m_targetSaveGameSlotNumber;

			// point the current player data to the new slot
			m_playerData = m_playerDataList[ m_activeSaveGameSlotNumber ];

			// make the current slot the active game
			m_playerData.m_isCurrentGame = true;

			// save the active game
			SaveActiveGame();

			// turn off controller navigation of the UI
			EventSystem.current.sendNavigationEvents = false;

			// figure out which scene to load (based on the player location in the save data)
			var nextSceneName = GetCurrentSceneName();

			// debug info
			UnityEngine.Debug.Log( "Loading scene " + nextSceneName );

			// load the next scene
			SceneManager.LoadScene( nextSceneName );
		}
	}

	// load the game data files
	void LoadGameData()
	{
		// load it as an asset
		var textAsset = Resources.Load( m_gameDataFileName ) as TextAsset;

		// convert it from the json string to our game data class
		m_gameData = JsonUtility.FromJson<GameData>( textAsset.text );

		// initalize the game data
		m_gameData.Initialize();
	}

	// this loads the save game slots from disk
	void LoadPlayerDataList()
	{
		// whether or not we have found the current game
		var currentGameFound = false;

		// create the player data list
		m_playerDataList = new PlayerData[ c_numSaveGameSlots ];

		// go through each save game slot
		for ( var i = 0; i < c_numSaveGameSlots; i++ )
		{
			// load this slot (or start a new game in it if there is nothing usable on disk)
			m_playerDataList[ i ] = LoadPlayerData( i );

			// check if this is the active save game slot
			if ( m_playerDataList[ i ].m_isCurrentGame )
			{
				// yes - remember the slot number
				m_activeSaveGameSlotNumber = i;

				// point the current player data to this slot
				m_playerData = m_playerDataList[ m_activeSaveGameSlotNumber ];

				// we have found the current game
				currentGameFound = true;
			}
		}

		// did we not find the current game?
		if ( !currentGameFound )
		{
			// nope - use the first slot
			m_activeSaveGameSlotNumber = 0;

			// point the current player data to this slot
			m_playerData = m_playerDataList[ m_activeSaveGameSlotNumber ];
		}

		// set the target save game slot number to be the same as the active one
		m_targetSaveGameSlotNumber = m_activeSaveGameSlotNumber;
	}

	// this loads one save game slot from disk - if there is nothing usable there it gives back a new game
	PlayerData LoadPlayerData( int saveGameSlotNumber )
	{
		PlayerData playerData = null;

		// check if the file exists
		if ( _saveSystem.Exists( m_playerDataFileName, saveGameSlotNumber ) )
		{
			try
			{
				// load and deserialize the player data file
				playerData = _saveSystem.Load<PlayerData>( m_playerDataFileName, saveGameSlotNumber );
			}
			catch
			{
				UnityEngine.Debug.LogWarning( "Failed to load save slot " + saveGameSlotNumber );

				playerData = null;
			}
		}

		// if we could not load the player data or it is from an old version then we have to start over
		if ( ( playerData == null ) || !playerData.IsCurrentVersion() )
		{
			// debug info
			UnityEngine.Debug.Log( "Creating and resetting player data " + saveGameSlotNumber );

			playerData = new PlayerData();

			playerData.Reset();
		}

		// repair save files where a deleted crewmember was left assigned to a role
		playerData.m_crewAssignment.UnassignMissingCrew( playerData.m_personnel );

		// repair save files that were written with a destroyed ship (the game over screen used to let that happen) - without this they could never be saved again
		if ( IsLost( playerData ) )
		{
			playerData.m_playerShip.m_armorPoints = 1;
		}

		return playerData;
	}

	// a game is lost once its ship has been destroyed - a lost game is never saved, the save on disk is the one the player goes back to
	static bool IsLost( PlayerData playerData )
	{
		return ( playerData.m_playerShip.m_armorPoints <= 0 );
	}

	// call this to throw away the game in memory and go back to the last save of the active slot (after the ship has been lost)
	public void ReloadActiveGame()
	{
		// debug info
		UnityEngine.Debug.Log( "Reloading save game slot number " + m_activeSaveGameSlotNumber );

		// load the active slot again
		m_playerDataList[ m_activeSaveGameSlotNumber ] = LoadPlayerData( m_activeSaveGameSlotNumber );

		// point the current player data to it
		m_playerData = m_playerDataList[ m_activeSaveGameSlotNumber ];

		// this is still the current game (a slot that had no save comes back as a new game)
		m_playerData.m_isCurrentGame = true;
	}

	// save the active game when the player closes the game (otherwise everything since the last location change is lost)
	void OnApplicationQuit()
	{
		// nothing to save if the player data was never loaded
		if ( m_playerData == null )
		{
			return;
		}

		// save the active game (this does nothing if the ship has been destroyed)
		SaveActiveGame();
	}

	// this saves our current save game slot to disk
	public void SaveActiveGame()
	{
		SavePlayerData( m_activeSaveGameSlotNumber );
	}

	// this saves a save game slot to disk
	public void SavePlayerData( int saveGameSlotNumber )
	{
		// never save a lost game - the save on disk still has the game from before the ship was destroyed
		if ( IsLost( m_playerDataList[ saveGameSlotNumber ] ) )
		{
			UnityEngine.Debug.Log( "Not saving slot " + saveGameSlotNumber + " because its ship has been destroyed." );

			return;
		}

		// measure performance
		var stopwatch = new Stopwatch();

		stopwatch.Start();

		try
		{
			// serialize and save the player data file
			_saveSystem.Save( m_playerDataFileName, saveGameSlotNumber, m_playerDataList[ saveGameSlotNumber ] );

			// report how long it took
			UnityEngine.Debug.Log( "Saving the player data took " + stopwatch.ElapsedMilliseconds + " milliseconds." );
		}
		catch ( System.Exception exception )
		{
			// report if we got an exception
			UnityEngine.Debug.Log( "Saving player data failed - " + exception.Message );
		}
	}

	// call this to get the name of the current scene for the active save game slot
	public string GetCurrentSceneName()
	{
		// figure out what the current scene is
		switch ( m_playerData.m_general.m_location )
		{
			case PD_General.Location.DockingBay:
			case PD_General.Location.Hyperspace:
			case PD_General.Location.InOrbit:
			case PD_General.Location.Planetside:
			case PD_General.Location.JustLaunched:
			case PD_General.Location.StarSystem:
			case PD_General.Location.Encounter:
			case PD_General.Location.Disembarked:
				return "Spaceflight";

			default:
				return "Starport";
		}
	}

	// call this to change the target save game slot number
	public void SetTargetSaveGameSlotNumber( int targetSaveGameSlotNumber )
	{
		// update the target save game slot number
		m_targetSaveGameSlotNumber = targetSaveGameSlotNumber;
	}

	// call this top copy the active save game slot to another slot
	public void CopyActiveSaveGameSlot( int targetSaveGameSlotNumber )
	{
		// a lost game cannot be copied (the copy would be a destroyed ship)
		if ( IsLost( m_playerData ) )
		{
			UnityEngine.Debug.Log( "Not copying the active game because its ship has been destroyed." );

			return;
		}

		// save the active game in the current slot
		SaveActiveGame();

		// clone the player data
		var clonedPlayerData = Tools.CloneObject( m_playerData );

		// the cloned copy is not the current game
		clonedPlayerData.m_isCurrentGame = false;

		// set the cloned player data to the target slot
		m_playerDataList[ targetSaveGameSlotNumber ] = clonedPlayerData;

		// save the game in the target save game slot
		SavePlayerData( targetSaveGameSlotNumber );
	}

	// call this to reset the active game
	public void ResetGame()
	{
		// reset the player data for the current slot
		m_playerData.Reset();

		// keep this game the active one
		m_playerData.m_isCurrentGame = true;

		// save the active game (with freshly reset data)
		SaveActiveGame();

		// turn off controller navigation of the UI
		EventSystem.current.sendNavigationEvents = false;

		// figure out which scene to load (based on the player location in the save data)
		var nextSceneName = GetCurrentSceneName();

		// debug info
		UnityEngine.Debug.Log( "Loading scene " + nextSceneName );

		// load the next scene
		SceneManager.LoadScene( nextSceneName );
	}
}
