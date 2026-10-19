
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

	// how much a crew member left at the Starport recovers in a day (the original's 20 points of vitality, PORTMENU-OV WEA0B)
	public const int c_starportRestPerDay = 20;

	// call this when the ship leaves the Starport: the crew left behind has rested, every living crew member on file who is not on board gaining 20 points of
	// vitality for each day since the ship last left, up to 100, as the original's does at launch (PORTMENU-OV WEA0B, after the interest) - returns how many rested
	public int RestCrewAtTheStarport()
	{
		var days = Mathf.Max( 0, m_general.m_day - m_general.m_lastLaunchDay );
		var rested = 0;

		if ( ( days > 0 ) && ( m_personnel.m_personnelList != null ) )
		{
			foreach ( var file in m_personnel.m_personnelList )
			{
				if ( ( file == null ) || file.m_isDead || ( file.m_vitality <= 0.0f ) || ( file.m_vitality >= 100.0f ) || m_crewAssignment.IsOnBoard( file.m_fileId ) )
				{
					continue;
				}

				file.m_vitality = Mathf.Min( 100.0f, file.m_vitality + days * c_starportRestPerDay );

				rested++;
			}
		}

		m_general.m_lastLaunchDay = m_general.m_day;

		return rested;
	}

	// the bank pays its interest up to today, as the original's does when the crew goes aboard the ship at the Starport and when a planet is recommended for
	// colonization (BANK-OV, INT%, called from the Starport's docking bay and from the captain's Log Planet), with an INTEREST row in the ledger - nothing is paid
	// while the interest comes to less than 1 M.U., and those days count on (the original moves its PORTDAT only when it pays); a recommendation starts the
	// count again whatever was paid (the original sets PORTDAT after it), so pass restartTheCount for it - returns what it paid
	public int PayInterest( bool restartTheCount )
	{
		var today = m_general.m_day;
		var interest = m_bank.GetInterestDue( today );

		// (a balance below 0 is charged its interest)
		if ( interest != 0 )
		{
			m_bank.m_currentBalance += interest;

			m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Interest", ( interest > 0 ) ? ( interest.ToString() + "+" ) : ( ( -interest ).ToString() + "-" ) ) );
		}

		// a save file from before the interest starts counting now
		if ( ( interest != 0 ) || restartTheCount || !m_bank.m_interestDayIsSet )
		{
			m_bank.m_interestDay = today;
			m_bank.m_interestDayIsSet = true;
		}

		return interest;
	}

	// Interstel pays for lifeform data an older save file recorded before roadmap 8.18 (the 1984 notes paid it on docking; the shipped game's recordings are items
	// of the hold, sold at the Trade Depot) - returns what it paid
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

	// call this as the Crystal Planet goes: Interstel pays the bonus that comes with its medal at once, though the medal's message speaks of the return to the
	// Starport (DPART-OV WF09C adds 500,000 MU to the balance and books it as a BONUS, transaction type 3, right after the award)
	public void PayWinBonus()
	{
		m_general.m_winBonusPending = false;

		m_bank.m_currentBalance += PD_General.c_winBonus;

		m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Bonus", PD_General.c_winBonus.ToString() + "+" ) );
	}

	// call this when the ship is back at the Starport: a game saved between the win and the return, when the bonus was paid at the Starport, still has it to come -
	// returns true if it was paid now
	public bool PayPendingWinBonus()
	{
		if ( !m_general.m_gameWon || !m_general.m_winBonusPending )
		{
			return false;
		}

		PayWinBonus();

		return true;
	}

	// call this when the evaluation screen shows the supplemental evaluations: their fines are booked now, each a FINE in the ledger, as the original's (OP-OV
	// WE78B: WF045 and WF12C, WF02B) - returns what they came to
	public int BookSupplementalFines()
	{
		if ( ( m_general.m_supplementalFineList == null ) || ( m_general.m_supplementalFineList.Count == 0 ) )
		{
			return 0;
		}

		var total = 0;

		foreach ( var fine in m_general.m_supplementalFineList )
		{
			m_bank.m_currentBalance -= fine;
			m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Fine", fine + "-" ) );

			total += fine;
		}

		m_general.m_supplementalFineList.Clear();

		return total;
	}

	// what an unsuitable recommendation costs, as the shipped game works it out (CAPTAIN overlay WF348 and WF292, OP-OV WED0C, in the decompiled original): 10,000 MU
	// for each unsuitable planet recommended before this one, at most 8 of them, and never less than 100 MU - so the first is a warning of 100 MU ("this is only a
	// warning, your next fine will be heavy", shown with that one only), then 10,000, 20,000 and on to 80,000 (STRINFO 1.2 only says they "vary")
	public const int c_firstUnsuitableFine = 100;
	public const int c_unsuitableFineStep = 10000;
	public const int c_unsuitableFineSteps = 8;

	// a planet of the game data (null if there is no such planet)
	GD_Planet FindPlanet( int planetId )
	{
		var planetList = DataController.m_instance.m_gameData.m_planetList;

		return ( ( planetId >= 0 ) && ( planetId < planetList.Length ) ) ? planetList[ planetId ] : null;
	}

	// call this when a planet is recommended for colonization: the original books its bonus or its fine in the ledger at once, after the interest (CAPTAIN overlay
	// (/LOG-PLAN): I-TRANS, then WF37A with WF348 and WF292, T+BALANCE and TRANSACT as BONUS or FINE) - Interstel's evaluation itself is read at the Starport
	public void BookColonyRecommendation( PD_General.ColonyRecommendation record )
	{
		if ( ( record == null ) || record.m_booked )
		{
			return;
		}

		// the original rates the planet as the captain logs it (CAPTAIN overlay WF30E and WF348): a planet with none of the reasons to be unsuitable earns its
		// rating's bonus, any other is fined
		var planet = FindPlanet( record.m_planetId );

		if ( ( planet != null ) && ( planet.GetUnsuitableReasons().Count == 0 ) )
		{
			// the bonus, a BONUS in the original's ledger (transaction type 3)
			var bonus = planet.GetColonyBonus();

			m_bank.m_currentBalance += bonus;
			m_bank.m_transactionList.Add( new PD_Bank.Transaction( m_general.m_currentStardateYMD, "Bonus", bonus + "+" ) );

			record.m_bonus = bonus;
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

	// call this when the ship is back at the Starport: Interstel evaluates the planets recommended for colonization since the last visit - a suitable planet earns
	// its bonus, any other is unsuitable and fined (STRINFO 1.2), both booked when the planet was recommended - returns how many it evaluated
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

			// was it suitable? (a record booked by a save file from before the bonus was kept has its planet's)
			if ( record.m_fine <= 0 )
			{
				// yes - the bonus, optimal above 40,000 MU (OP-OV WEEE3)
				var bonus = ( record.m_bonus > 0 ) ? record.m_bonus : gameData.m_planetList[ planetId ].GetColonyBonus();
				var text = gameData.FindStoryText( ( bonus > GD_Planet.c_optimalColonyBonus ) ? "EvaluationOptimal" : "EvaluationSuitable" );

				if ( text != null )
				{
					m_general.AddColonyEvaluation( heading + text.m_text.Replace( "[REWARD]", bonus.ToString() ) );
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

		// the evaluations wait to be read before the ship can leave again
		if ( evaluated > 0 )
		{
			m_general.m_colonyEvaluationUnread = true;
		}

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

	// the fine for being towed back to the Starport after a distress call, as the original works it out (COMM-OV WEF2D and WEF6D): the distance from Arth (the larger
	// of the two, in the original's coordinates, eight to a coordinate of the starmap) times the engines' fuel per step (7 less their class, halved with the
	// Tesseract aboard) over 5, times what a tenth of Endurium is worth in flight (twice its Starport price: SHIPBUTTONS >FLT), and at least 15,000 M.U. STRINFO
	// 1.2's "range from 15000 to 80000 MU" is the evaluation's words; the code has no upper limit
	public const int c_smallestTowingFine = 15000;

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

		// the distance from Arth, in the original's coordinates
		var distance = (long) Mathf.FloorToInt( Mathf.Max( Mathf.Abs( here.x - arth.x ), Mathf.Abs( here.y - arth.y ) ) * 8.0f );

		// the engines' fuel per step, in hundredths of a cubic meter of Endurium (none installed counts as the most)
		var fuelPerStep = (long) Mathf.Max( 1, Mathf.FloorToInt( ( 7 - Mathf.Clamp( m_playerShip.m_enginesClass, 0, 6 ) ) * m_playerShip.GetEngineFuelFactor() ) );

		// a tenth of Endurium in flight
		var tenthValue = (long) gameData.m_elementList[ gameData.m_misc.m_enduriumElementId ].GetStarportPrice() / 10 * 2;

		var fine = distance * fuelPerStep / 5 * tenthValue;

		if ( fine < c_smallestTowingFine )
		{
			fine = c_smallestTowingFine;
		}

		return (int) System.Math.Min( fine, int.MaxValue );
	}

	// a planet's life, in percent: the game data's bio density, or none once a flare of its star has sterilised it (ANALYZE-OV WEC26) - every read of a planet's life
	// for the game goes through this (the sensors, the terrain vehicle's scan, the ship's log, a colony's evaluation, the species and lifeforms, the survey from
	// orbit); the look of the planet (its trees, grass and vegetation) keeps the game data's, since the objects of a surface are placed by its random numbers - and
	// none for a planet whose type the shipped game has no life tables for (ShippedLifeforms.HasRecord: of the planets the game data gives life, only one, whose
	// generic type has none in the original, so its sensors read none there)
	public int GetBioDensity( GD_Planet planet )
	{
		if ( ( planet == null ) || !ShippedLifeforms.HasRecord( planet ) )
		{
			return 0;
		}

		return ( ( m_planetSurfaces != null ) && m_planetSurfaces.IsPlanetSterilised( planet.m_id ) ) ? 0 : planet.m_bioDensity;
	}

	// the same, from code that may run without the player data (the game data's own code, the Editor's tools): the game data's value then
	public static int GetBioDensityOf( GD_Planet planet )
	{
		var playerData = ( DataController.m_instance != null ) ? DataController.m_instance.m_playerData : null;

		return ( playerData != null ) ? playerData.GetBioDensity( planet ) : ( ( ( planet != null ) && ShippedLifeforms.HasRecord( planet ) ) ? planet.m_bioDensity : 0 );
	}

	// returns true if the player data version is current
	public bool IsCurrentVersion()
	{
		return ( m_version == c_currentVersion );
	}
}
