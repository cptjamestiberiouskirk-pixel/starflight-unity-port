
using System;
using UnityEngine;

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
	// Interstel pays for the lifeform data recorded since the last visit (planet.txt: "When the player returns to starport recorded information is automatically
	// paid for") - returns what it paid
	public int PayLifeformData()
	{
		var value = m_general.m_unpaidLifeformDataValue;

		m_general.m_unpaidLifeformDataValue = 0;

		if ( value <= 0 )
		{
			return 0;
		}

		m_bank.m_currentBalance += value;

		m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Lifeform data", value.ToString() + "+" ) );

		return value;
	}

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

	// what an unsuitable recommendation costs: the first is a warning of 100 MU ("this is only a warning, your next fine will be heavy"), and later fines "vary with
	// how unsuitable the planet is" (STRINFO 1.2) - 5000 MU for each reason is the port's choice
	public const int c_firstUnsuitableFine = 100;
	public const int c_unsuitableFinePerReason = 5000;

	// call this when the ship is back at the Starport: Interstel evaluates the planets recommended for colonization since the last visit - a planet of its list
	// earns its bonus, any other is unsuitable and fined (STRINFO 1.2) - returns how many it evaluated
	public int EvaluateColonyRecommendations()
	{
		var gameData = DataController.m_instance.m_gameData;

		if ( ( m_general.m_colonyRecommendationList == null ) || ( m_general.m_colonyRecommendationList.Count == 0 ) )
		{
			return 0;
		}

		var evaluated = 0;

		foreach ( var planetId in m_general.m_colonyRecommendationList )
		{
			if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) )
			{
				continue;
			}

			evaluated++;

			// is it a planet of Interstel's list?
			GD_ColonyEvaluation colonyEvaluation = null;

			foreach ( var candidate in gameData.m_colonyEvaluationList )
			{
				if ( candidate.m_planetId == planetId )
				{
					colonyEvaluation = candidate;
				}
			}

			if ( colonyEvaluation != null )
			{
				// yes - the bonus
				m_bank.m_currentBalance += colonyEvaluation.m_bonus;
				m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Colony bonus", colonyEvaluation.m_bonus + "+" ) );

				var text = gameData.FindStoryText( ( colonyEvaluation.m_rating == "Optimal" ) ? "EvaluationOptimal" : "EvaluationSuitable" );

				if ( text != null )
				{
					m_general.AddSupplementalEvaluation( text.m_text.Replace( "[REWARD]", colonyEvaluation.m_bonus.ToString() ) );
				}

				continue;
			}

			// no - unsuitable, and fined
			var reasons = gameData.m_planetList[ planetId ].GetUnsuitableReasons();
			var firstTime = ( m_general.m_unsuitableRecommendationCount == 0 );
			var fine = firstTime ? c_firstUnsuitableFine : ( c_unsuitableFinePerReason * System.Math.Max( 1, reasons.Count ) );

			m_general.m_unsuitableRecommendationCount++;

			m_bank.m_currentBalance -= fine;
			m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Colony fine", fine + "-" ) );

			var unsuitable = gameData.FindStoryText( "EvaluationUnsuitable" );

			if ( unsuitable != null )
			{
				var evaluation = unsuitable.m_text;

				// the reasons in place of STRINFO's bracket (which lists them all)
				var bracketStart = evaluation.IndexOf( '[' );
				var bracketEnd = evaluation.IndexOf( ']' );

				if ( ( bracketStart >= 0 ) && ( bracketEnd > bracketStart ) )
				{
					var reasonText = ( reasons.Count == 0 ) ? "ITS CONDITIONS" : string.Join( ", ", reasons.GetRange( 0, reasons.Count - 1 ) ) + ( ( reasons.Count > 1 ) ? " AND " : "" ) + reasons[ reasons.Count - 1 ];

					evaluation = evaluation.Substring( 0, bracketStart ) + reasonText + evaluation.Substring( bracketEnd + 1 );
				}

				// the fine, and the warning only the first time
				evaluation = evaluation.Replace( "FINED 100 MU'S", "FINED " + fine + " MU'S" );

				if ( !firstTime )
				{
					evaluation = evaluation.Replace( "  THIS IS ONLY A WARNING, YOUR NEXT FINE WILL BE HEAVY.", "" );
				}

				m_general.AddSupplementalEvaluation( evaluation );
			}
		}

		m_general.m_colonyRecommendationList.Clear();

		return evaluated;
	}

	// the fine for being towed back to the Starport after a distress call: "fines vary directly with the distance from Arth and range from 15000 to 80000 MU"
	// (STRINFO 1.2) - in proportion to the distance from Arth against the farthest star from it (that scale is the port's choice)
	public const int c_smallestTowingFine = 15000;
	public const int c_largestTowingFine = 80000;

	public int GetTowingFine()
	{
		var gameData = DataController.m_instance.m_gameData;
		var arthStar = gameData.m_starList[ gameData.m_misc.m_arthStarId ];
		var arth = new Vector2( arthStar.m_xCoordinate, arthStar.m_yCoordinate );

		// where the ship is, in the coordinates of the starmap: in hyperspace its own, anywhere else its star's
		Vector2 here;

		var location = ( m_general.m_location == PD_General.Location.Encounter ) ? m_general.m_lastLocation : m_general.m_location;

		if ( location == PD_General.Location.Hyperspace )
		{
			var game = Tools.WorldToGameCoordinates( m_general.m_lastHyperspaceCoordinates );

			here = new Vector2( game.x, game.z );
		}
		else
		{
			var star = gameData.m_starList[ m_general.m_currentStarId ];

			here = new Vector2( star.m_xCoordinate, star.m_yCoordinate );
		}

		// the farthest star from Arth
		var farthest = 1.0f;

		foreach ( var star in gameData.m_starList )
		{
			farthest = Mathf.Max( farthest, Vector2.Distance( arth, new Vector2( star.m_xCoordinate, star.m_yCoordinate ) ) );
		}

		var share = Mathf.Clamp01( Vector2.Distance( arth, here ) / farthest );

		return c_smallestTowingFine + Mathf.RoundToInt( share * ( c_largestTowingFine - c_smallestTowingFine ) );
	}

	// returns true if the player data version is current
	public bool IsCurrentVersion()
	{
		return ( m_version == c_currentVersion );
	}
}
