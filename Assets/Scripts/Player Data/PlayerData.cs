
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

	// the bank pays its interest up to today, as the original's does when the crew goes aboard the ship at the Starport and when a planet is recommended for
	// colonization (BANK-OV, INT%, called from the Starport's docking bay and from the captain's Log Planet), with an INTEREST row in the ledger - nothing is paid
	// while the interest comes to less than 1 M.U., and those days count on (the original moves its PORTDAT only when it pays); a recommendation starts the
	// count again whatever was paid (the original sets PORTDAT after it), so pass restartTheCount for it - returns what it paid
	public int PayInterest( bool restartTheCount )
	{
		var today = m_general.m_day;
		var interest = m_bank.GetInterestDue( today );

		if ( interest > 0 )
		{
			m_bank.m_currentBalance += interest;

			m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Interest", interest.ToString() + "+" ) );
		}

		// a save file from before the interest starts counting now
		if ( ( interest > 0 ) || restartTheCount || !m_bank.m_interestDayIsSet )
		{
			m_bank.m_interestDay = today;
			m_bank.m_interestDayIsSet = true;
		}

		return interest;
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

		// the original books the win's bonus as a BONUS (DPART-OV in the decompiled original, transaction type 3)
		m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Bonus", PD_General.c_winBonus.ToString() + "+" ) );

		return true;
	}

	// what an unsuitable recommendation costs, as the shipped game works it out (CAPTAIN overlay WF348 and WF292, OP-OV WED0C, in the decompiled original): 10,000 MU
	// for each unsuitable planet recommended before this one, at most 8 of them, and never less than 100 MU - so the first is a warning of 100 MU ("this is only a
	// warning, your next fine will be heavy", shown with that one only), then 10,000, 20,000 and on to 80,000 (STRINFO 1.2 only says they "vary")
	public const int c_firstUnsuitableFine = 100;
	public const int c_unsuitableFineStep = 10000;
	public const int c_unsuitableFineSteps = 8;

	// the planet of Interstel's list that this one is (null for an unsuitable planet)
	GD_ColonyEvaluation FindColonyEvaluation( int planetId )
	{
		GD_ColonyEvaluation colonyEvaluation = null;

		foreach ( var candidate in DataController.m_instance.m_gameData.m_colonyEvaluationList )
		{
			if ( candidate.m_planetId == planetId )
			{
				colonyEvaluation = candidate;
			}
		}

		return colonyEvaluation;
	}

	// call this when a planet is recommended for colonization: the original books its bonus or its fine in the ledger at once, after the interest (CAPTAIN overlay
	// (/LOG-PLAN): I-TRANS, then WF37A with WF348 and WF292, T+BALANCE and TRANSACT as BONUS or FINE) - Interstel's evaluation itself is read at the Starport
	public void BookColonyRecommendation( PD_General.ColonyRecommendation record )
	{
		if ( ( record == null ) || record.m_booked )
		{
			return;
		}

		var colonyEvaluation = FindColonyEvaluation( record.m_planetId );

		if ( colonyEvaluation != null )
		{
			// a planet of Interstel's list - the bonus, a BONUS in the original's ledger (transaction type 3)
			m_bank.m_currentBalance += colonyEvaluation.m_bonus;
			m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Bonus", colonyEvaluation.m_bonus + "+" ) );

			record.m_fine = 0;
			record.m_firstUnsuitable = false;
		}
		else
		{
			// an unsuitable planet - the fine, a FINE in the original's ledger (transaction type 0)
			var unsuitableBefore = System.Math.Max( 0, m_general.m_unsuitableRecommendationCount );
			var fine = System.Math.Max( c_firstUnsuitableFine, c_unsuitableFineStep * System.Math.Min( unsuitableBefore, c_unsuitableFineSteps ) );

			m_general.m_unsuitableRecommendationCount++;

			m_bank.m_currentBalance -= fine;
			m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Fine", fine + "-" ) );

			record.m_fine = fine;
			record.m_firstUnsuitable = ( unsuitableBefore == 0 );
		}

		record.m_booked = true;
	}

	// call this when the ship is back at the Starport: Interstel evaluates the planets recommended for colonization since the last visit - a planet of its list
	// earns its bonus, any other is unsuitable and fined (STRINFO 1.2), both booked when the planet was recommended - returns how many it evaluated
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

			// the heading the original's evaluation screen puts above each evaluation
			var heading = GetColonyEvaluationHeading( planetId );

			// the bonus or the fine was booked when the planet was recommended (a recommendation from a save file before that is booked now; one with no record
			// at all, from before the records, gets one for the booking)
			var record = m_general.FindColonyRecommendation( planetId ) ?? new PD_General.ColonyRecommendation { m_planetId = planetId };

			BookColonyRecommendation( record );

			// is it a planet of Interstel's list?
			var colonyEvaluation = FindColonyEvaluation( planetId );

			if ( colonyEvaluation != null )
			{
				// yes - the bonus
				var text = gameData.FindStoryText( ( colonyEvaluation.m_rating == "Optimal" ) ? "EvaluationOptimal" : "EvaluationSuitable" );

				if ( text != null )
				{
					m_general.AddColonyEvaluation( heading + text.m_text.Replace( "[REWARD]", colonyEvaluation.m_bonus.ToString() ) );
				}

				continue;
			}

			// no - unsuitable, and fined
			var reasons = gameData.m_planetList[ planetId ].GetUnsuitableReasons();
			var firstTime = record.m_firstUnsuitable;
			var fine = record.m_fine;

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

				m_general.AddColonyEvaluation( heading + evaluation );
			}
		}

		m_general.m_colonyRecommendationList.Clear();

		if ( m_general.m_colonyRecommendationRecordList != null )
		{
			m_general.m_colonyRecommendationRecordList.Clear();
		}

		return evaluated;
	}

	// the lines above an evaluation on the original's evaluation screen (OP-OV in the decompiled original): "RECOMMENDATION: ", "DATE: ", "SYSTEM: ", "ORBIT: ",
	// "NAME: " and "EVALUATION:" - a recommendation made before the records were saved has no number, date or name, and gets only the planet's lines
	string GetColonyEvaluationHeading( int planetId )
	{
		var gameData = DataController.m_instance.m_gameData;
		var planet = gameData.m_planetList[ planetId ];
		var record = m_general.FindColonyRecommendation( planetId );
		var heading = "";

		if ( record != null )
		{
			heading += "RECOMMENDATION: " + record.m_number + "\n";

			if ( !string.IsNullOrEmpty( record.m_stardateYMD ) )
			{
				heading += "DATE: " + PD_General.GetDisplayStardate( record.m_stardateYMD ) + "\n";
			}
		}

		if ( ( planet.m_starId >= 0 ) && ( planet.m_starId < gameData.m_starList.Length ) )
		{
			var star = gameData.m_starList[ planet.m_starId ];

			heading += "SYSTEM: " + star.m_xCoordinate + "," + star.m_yCoordinate + "\n";
		}

		heading += "ORBIT: " + planet.m_orbitPosition + "\n";

		if ( record != null )
		{
			heading += "NAME: " + ( record.m_name ?? "" ).ToUpperInvariant() + "\n";
		}

		return heading + "EVALUATION:\n";
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
