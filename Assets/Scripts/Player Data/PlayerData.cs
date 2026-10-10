
using System;

[Serializable]

public class PlayerData
{
	// the current save game version - increment this to invalidate obsolete save game files
	const int c_currentVersion = 23;

	// the current version of this player data file
	public int m_version;

	// is this the current game?
	public bool m_isCurrentGame;
	
	// all the parts of the player data
	public PD_General m_general;
	public PD_Starport m_starport;
	public PD_Personnel m_personnel;
	public PD_CrewAssignment m_crewAssignment;
	public PD_Bank m_bank;
	public PD_PlayerShip m_playerShip;
	public PD_KnownArtifacts m_knownArtifacts;
	public PD_Encounter[] m_encounterList;
	public PD_TerrainVehicle m_terrainVehicle;
	public PD_ShipsLog m_shipsLog;
	public PD_PlanetSurfaces m_planetSurfaces;

	// this resets our player progress to the new game state
	public void Reset()
	{
		var gameData = DataController.m_instance.m_gameData;

		m_version = c_currentVersion;
		m_isCurrentGame = false;

		m_general = new PD_General();
		m_starport = new PD_Starport();
		m_personnel = new PD_Personnel();
		m_crewAssignment = new PD_CrewAssignment();
		m_bank = new PD_Bank();
		m_playerShip = new PD_PlayerShip();
		m_knownArtifacts = new PD_KnownArtifacts();
		m_encounterList = new PD_Encounter[ gameData.m_encounterList.Length ];
		m_terrainVehicle = new PD_TerrainVehicle();
		m_shipsLog = new PD_ShipsLog();
		m_planetSurfaces = new PD_PlanetSurfaces();

		m_general.Reset();
		m_starport.Reset();
		m_personnel.Reset();
		m_crewAssignment.Reset();
		m_bank.Reset();
		m_playerShip.Reset();
		m_knownArtifacts.Reset();
		m_terrainVehicle.Reset();
		m_shipsLog.Reset();
		m_planetSurfaces.Reset();

		for ( var i = 0; i < gameData.m_encounterList.Length; i++ )
		{
			m_encounterList[ i ] = new PD_Encounter();

			m_encounterList[ i ].Reset( i );
		}
	}

	// repair save files made before the stardates were in the original's calendar of 10 months of 30 days: the dates the bank ledger and the ships log keep are moved to it
	// (the day count of the game is the same in both calendars), and the current stardate is made from the day and the hour of the game - the starport has no clock,
	// so without this a game loaded there would show the date of the save until the ship launches
	public void ValidateStardateCalendar()
	{
		if ( m_general == null )
		{
			return;
		}

		if ( m_general.m_stardateCalendar != PD_General.c_originalCalendar )
		{
			if ( ( m_bank != null ) && ( m_bank.m_transactionList != null ) )
			{
				foreach ( var transaction in m_bank.m_transactionList )
				{
					if ( transaction != null )
					{
						transaction.m_stardate = PD_General.ConvertRealCalendarYMD( transaction.m_stardate );
					}
				}
			}

			if ( m_shipsLog != null )
			{
				m_shipsLog.ConvertRealCalendarDates();
			}

			m_general.m_stardateCalendar = PD_General.c_originalCalendar;
		}

		m_general.MakeStardateTexts();
	}

	// repair save files whose encounters are not where the game data has them (the game data was corrected after they were written) - returns how many were moved
	public int ValidateEncounterLocations()
	{
		var numMoved = 0;

		// older save files may not have an encounter list
		if ( m_encounterList == null )
		{
			return numMoved;
		}

		foreach ( var encounter in m_encounterList )
		{
			if ( ( encounter != null ) && encounter.ValidateLocation() )
			{
				numMoved++;
			}
		}

		return numMoved;
	}

	// find an encounter by its id (Radar keeps m_encounterList sorted by distance, so the array index is not the encounter id)
	public PD_Encounter FindEncounter( int encounterId )
	{
		foreach ( var encounter in m_encounterList )
		{
			if ( encounter.m_encounterId == encounterId )
			{
				return encounter;
			}
		}

		return null;
	}

	// call this when the ship is back at the Starport: after the win Interstel pays the bonus that comes with its medal, once ("this award includes a special bonus of
	// 500,000 MU when you return to Starport" - STRINFO 2.12) - returns true if it was paid now
	public bool PayWinBonus()
	{
		if ( !m_general.m_gameWon || !m_general.m_winBonusPending )
		{
			return false;
		}

		m_general.m_winBonusPending = false;

		m_bank.m_currentBalance += PD_General.c_winBonus;

		m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Mission completed", PD_General.c_winBonus.ToString() + "+" ) );

		return true;
	}

	// returns true if the player data version is current
	public bool IsCurrentVersion()
	{
		return ( m_version == c_currentVersion );
	}
}
