
using UnityEngine;

using System;
using System.Collections.Generic;

[Serializable]

public class PD_General
{
	public enum Location
	{
		Starport,
		DockingBay,
		JustLaunched,
		StarSystem,
		Hyperspace,
		InOrbit,
		Planetside,
		Encounter,
		Disembarked,
	}

	// the player location
	public Location m_location;

	// the last player location
	public Location m_lastLocation;

	// the player coordinates
	public Vector3 m_coordinates;

	// the last known coordinates for various locations
	public Vector3 m_lastStarportCoordinates;
	public Vector3 m_lastHyperspaceCoordinates;
	public Vector3 m_lastStarSystemCoordinates;
	public Vector3 m_lastEncounterCoordinates;
	public Vector3 m_lastDisembarkedCoordinates;

	// crosshair position
	public float m_selectedLatitude;
	public float m_selectedLongitude;

	// game time stuff
	public string m_currentStardateYMD;
	public string m_currentStardateDHMY;

	public int m_day;
	public int m_hour;
	public int m_minute;

	// the day the ship last left the Starport, from which the crew left behind has rested (the original's WEA07, PORTMENU-OV WEA0B; 0 in a new game and in a
	// save from before it was kept)
	public int m_lastLaunchDay;
	public int m_second;
	public int m_millisecond;

	public int m_lastHour;

	public float m_gameTime;

	// the current star we are in (or the last star we visited if we are in hyperspace)
	public int m_currentStarId;

	// the current planet we are visiting (or the last planet we visited)
	public int m_currentPlanetId;

	// the current encounter we are in (or the last encounter we were in)
	public int m_currentEncounterId;

	// keep track of the player's current speed and maximum speed
	public float m_currentSpeed;
	public float m_currentMaximumSpeed;

	// keep track of the player's current direction
	public Vector3 m_currentDirection;

	// various game play variables
	public bool m_mechan9Unlocked;

	// set once the crew has answered the Veloxi's demand for the Ellipsoid: they no longer say their line about the lost focusing stone (COMMSPEC-OV WEF26) - false
	// in older save files
	public bool m_veloxStoneLineRemoved;

	// the lifeforms whose data the crew has collected, for the value of new data (GD_Species.GetBodyClass; a species is its planet's id x 16 + its index) - null in
	// older save files
	public List<int> m_knownLifeformClassList;
	public List<int> m_knownLifeformOrderList;
	public List<int> m_knownLifeformSpeciesList;

	// what the lifeform data recorded since the last visit to the Starport is worth (planet.txt: "When the player returns to starport recorded information is
	// automatically paid for") - 0 in older save files
	public int m_unpaidLifeformDataValue;

	// true once a Black Egg has destroyed the Crystal Planet at its control nexus: the game is won, and no star flares any more (the original's ?WIN, disys.txt ?FLARE)
	public bool m_gameWon;

	// the game time of the win (the original's ?WIN keeps its stardate), below 0 before the win and in a game saved after the win before it was kept
	public float m_winTime = -1.0f;

	// true in a game saved between the win and the return to the Starport when Interstel paid its bonus there: the bonus is paid on docking (a new win pays it at
	// once, PlayerData.PayWinBonus)
	public bool m_winBonusPending;

	// the bonus that comes with the Interstel Medal of Sublime Achievement (STRINFO 1.2 and 2.12)
	public const int c_winBonus = 500000;

	// true once Arth's sun has flared before the game was won: the Starport has been destroyed (STRINFO 2.12 and 2.14) and the game goes on without it
	public bool m_starportDestroyed;

	// dimensions of the last comm ids table (race x subject)
	public const int c_numLastCommRaces = 20;
	public const int c_numLastCommSubjects = 16;

	// keep track of responses to questions on a per race basis
	// stored flat ( race * c_numLastCommSubjects + subject ) because JsonUtility does not serialize multi-dimensional arrays
	// always go through GetLastCommId / SetLastCommId
	public int[] m_lastCommIds;

	// how many times each comm line has been said in this game, as the shipped game counts them in its phrase records (FC, raised by PHRASE>CT in COMM-OV) to
	// say the least used line first - by the line's id, for the lines said at least once (save files from before the counts have none: every line at 0)
	[Serializable] public class CommUse
	{
		public int m_commId;
		public int m_count;
	}

	public List<CommUse> m_commUseList;

	// lines of messages
	public List<string> m_messageList;

	// Interstel's supplemental evaluations (towing charges and the like - STRINFO 1.2) the Operations evaluation screen has not shown yet, oldest first - as in the
	// original (OP-OV in the decompiled original), each is shown once, on a page of its own, before the colony evaluations
	public List<string> m_supplementalEvaluationList;

	// the fines of the supplemental evaluations not shown yet (the towing charges, the lost terrain vehicle): the original books each in the ledger when the
	// evaluation screen shows it (OP-OV WE78B: WF045 and WF12C call WF02B, T+BALANCE and TRANSACT as a FINE) - PlayerData.BookSupplementalFines (save files from
	// before this list have their fines booked already)
	public List<int> m_supplementalFineList;

	// Interstel's evaluations of the planets recommended for colonization, oldest first - shown one to a page on every visit to the evaluation screen until the
	// ship leaves the Starport with them read, which deletes them (CHKFLIGHT-OV WF40A), the last followed by "END OF EVALUATIONS" (save files from before this list keep theirs in m_supplementalEvaluationList: GetColonyEvaluationList moves them)
	public List<string> m_colonyEvaluationList;

	// whether the evaluation screen has shown Interstel's supplemental evaluation on the completion of the mission (the original shows it once, WMSG)
	public bool m_missionCompleteShown;

	// whether a colony evaluation has come since the evaluation screen was last seen (the original's ?EVAL, set by a recommendation in CAPTAIN WF37A and cleared
	// by the evaluation screen in OP-OV WF47A): the ship cannot leave the Starport until it has been read (CHKFLIGHT-OV ?CAN-LEAVE)
	public bool m_colonyEvaluationUnread;

	// the planets recommended for colonization that Interstel has not evaluated yet (it does when the ship is back at the Starport), and how many recommended
	// planets have proved unsuitable so far (the first one is only a warning - STRINFO 1.2)
	public List<int> m_colonyRecommendationList;

	// every planet recommended in the game, evaluated or not: the original marks a planet as logged when it is recommended and never again lets it be
	// (CAPTAIN WF3D3: "THIS PLANET HAS ALREADY BEEN LOGGED.") - save files from before this list have the planets still waiting in m_colonyRecommendationList
	public List<int> m_loggedPlanetList;

	// the encounters in orbit that have let the ship into orbit (a veloxi drone that was answered correctly), until the ship leaves the star system - saved,
	// so that the drones do not ask again after the game is loaded (older saves have none: see GetOrbitPermissions)
	public List<int> m_orbitPermissionList;

	// returns the encounters in orbit that have let the ship into orbit (never null)
	public List<int> GetOrbitPermissions()
	{
		if ( m_orbitPermissionList == null )
		{
			m_orbitPermissionList = new List<int>();
		}

		return m_orbitPermissionList;
	}

	// the continuum fluxes the ship has been through, by their GD_Flux ids, both ways: the original marks a flux known when the ship goes through it and keeps
	// that in the save, and its starmap draws the known ones (HYPER-OV SET-FLUX-STATE, MAP-OV WEA70) - older saves have none: see GetKnownFluxes
	public List<int> m_knownFluxList;

	// returns the fluxes the ship has been through (never null)
	public List<int> GetKnownFluxes()
	{
		if ( m_knownFluxList == null )
		{
			m_knownFluxList = new List<int>();
		}

		return m_knownFluxList;
	}

	// remembers a flux the ship has gone through: it and the flux that runs the other way between the same two ends
	public void LearnFlux( GD_Flux flux )
	{
		var known = GetKnownFluxes();
		var fluxList = DataController.m_instance.m_gameData.m_fluxList;

		if ( ( flux == null ) || ( fluxList == null ) )
		{
			return;
		}

		foreach ( var other in fluxList )
		{
			var sameWay = ( other.m_x1 == flux.m_x1 ) && ( other.m_y1 == flux.m_y1 ) && ( other.m_x2 == flux.m_x2 ) && ( other.m_y2 == flux.m_y2 );
			var otherWay = ( other.m_x1 == flux.m_x2 ) && ( other.m_y1 == flux.m_y2 ) && ( other.m_x2 == flux.m_x1 ) && ( other.m_y2 == flux.m_y1 );

			if ( ( sameWay || otherWay ) && !known.Contains( other.m_id ) )
			{
				known.Add( other.m_id );
			}
		}
	}

	// each race's base mood, the mood its aliens start an encounter from: the original's BASE-EDL of the race's originator, which the end of every encounter moves
	// toward the mood the encounter ended in (HYPER-OV ENCCLEANUP), so a race remembers how it was treated - saved; a race not in the list starts from its
	// personality's starting mood (older saves have none)
	[Serializable] public class RaceMood
	{
		public int m_race;
		public float m_baseMood;
	}

	public List<RaceMood> m_raceMoodList;

	// a race's base mood, or this starting mood if the race has none yet
	public float GetRaceBaseMood( GameData.Race race, float startingMood )
	{
		if ( m_raceMoodList != null )
		{
			foreach ( var raceMood in m_raceMoodList )
			{
				if ( raceMood.m_race == (int) race )
				{
					return raceMood.m_baseMood;
				}
			}
		}

		return startingMood;
	}

	// sets a race's base mood (kept between 0 and 100)
	public void SetRaceBaseMood( GameData.Race race, float baseMood )
	{
		if ( m_raceMoodList == null )
		{
			m_raceMoodList = new List<RaceMood>();
		}

		baseMood = Mathf.Clamp( baseMood, 0.0f, 100.0f );

		foreach ( var raceMood in m_raceMoodList )
		{
			if ( raceMood.m_race == (int) race )
			{
				raceMood.m_baseMood = baseMood;

				return;
			}
		}

		m_raceMoodList.Add( new RaceMood { m_race = (int) race, m_baseMood = baseMood } );
	}

	// the order the aliens' eleven combat rules are tried in, by their numbers in the original's shipped order: a rule that fires swaps places with the rule halfway
	// up the list, and the order lasts from one encounter to the next (COMBAT-OV <COMBAT>, the rule engine's swap, kept by SAVE-OVERLAY) - saved; older saves
	// start from the shipped order
	public int[] m_combatRuleOrder;

	// the combat rules' order (never null, and always the eleven rules once each)
	public int[] GetCombatRuleOrder( int ruleCount )
	{
		var valid = ( m_combatRuleOrder != null ) && ( m_combatRuleOrder.Length == ruleCount );

		for ( var rule = 1; valid && ( rule <= ruleCount ); rule++ )
		{
			valid = Array.IndexOf( m_combatRuleOrder, rule ) >= 0;
		}

		if ( !valid )
		{
			m_combatRuleOrder = new int[ ruleCount ];

			for ( var position = 0; position < ruleCount; position++ )
			{
				m_combatRuleOrder[ position ] = position + 1;
			}
		}

		return m_combatRuleOrder;
	}

	public int m_unsuitableRecommendationCount;

	// what the original keeps of a recommendation for its evaluation screen (CAPTAIN overlay, (/LOG-PLAN) in the decompiled original): its number, counted over
	// the whole game, the stardate it was logged on and the name the player gave the planet ("NAME THIS PLANET: ", up to 15 characters) - one record for each
	// planet of m_colonyRecommendationList (save files from before the records have none)
	[Serializable] public class ColonyRecommendation
	{
		public int m_planetId;
		public int m_number;
		public string m_stardateYMD;
		public string m_name;

		// the bonus or the fine is booked in the ledger when the planet is recommended, as the original's (CAPTAIN overlay WF37A and WF292): whether it has been
		// (a save file from before this has its recommendations booked when the ship is back at the Starport), the fine of an unsuitable planet (0 for a suitable
		// planet) and whether it was the first unsuitable one (the one that is only a warning)
		public bool m_booked;
		public int m_fine;
		public bool m_firstUnsuitable;

		// the bonus a suitable planet earned (0 for an unsuitable one, and in a save file from before it was kept)
		public int m_bonus;
	}

	public const int c_maximumPlanetNameLength = 15;

	public List<ColonyRecommendation> m_colonyRecommendationRecordList;
	public int m_colonyRecommendationCount;

	// the day and the hour the two stardate texts were last made for (not saved - they are made again in the first update after a load)
	[NonSerialized] int m_stardateDay = -1;
	[NonSerialized] int m_stardateHour = -1;

	// the original's calendar: 10 months of 30 days, and the first day of the game is 01-01-4620
	public const int c_firstYear = 4620;
	public const int c_daysPerMonth = 30;
	public const int c_monthsPerYear = 10;
	public const int c_daysPerYear = c_daysPerMonth * c_monthsPerYear;

	// the calendar the stardates of this save are in: 1 for the original's (saves made before 2026-10-09 have 0, the real-world calendar, and are repaired when they are loaded)
	public const int c_originalCalendar = 1;
	public int m_stardateCalendar;

	// the value of the data of a lifeform of this species, which becomes known (planet.txt): its base is that of a new class, order or species, or nothing for a
	// species already known, and "If the lifeform was recorded and it is dead cut its value in half"
	public int LearnLifeform( GD_Species species, int planetId, bool dead )
	{
		if ( m_knownLifeformClassList == null )
		{
			m_knownLifeformClassList = new List<int>();
		}

		if ( m_knownLifeformOrderList == null )
		{
			m_knownLifeformOrderList = new List<int>();
		}

		if ( m_knownLifeformSpeciesList == null )
		{
			m_knownLifeformSpeciesList = new List<int>();
		}

		var bodyClass = species.GetBodyClass();
		var order = (int) species.m_bodyPlan;
		var speciesKey = planetId * 16 + species.m_index;

		var baseValue = 0;

		if ( !m_knownLifeformClassList.Contains( bodyClass ) )
		{
			baseValue = GD_Species.c_newClassValue;
		}
		else if ( !m_knownLifeformOrderList.Contains( order ) )
		{
			baseValue = GD_Species.c_newOrderValue;
		}
		else if ( !m_knownLifeformSpeciesList.Contains( speciesKey ) )
		{
			baseValue = GD_Species.c_newSpeciesValue;
		}

		if ( !m_knownLifeformClassList.Contains( bodyClass ) )
		{
			m_knownLifeformClassList.Add( bodyClass );
		}

		if ( !m_knownLifeformOrderList.Contains( order ) )
		{
			m_knownLifeformOrderList.Add( order );
		}

		if ( !m_knownLifeformSpeciesList.Contains( speciesKey ) )
		{
			m_knownLifeformSpeciesList.Add( speciesKey );
		}

		var value = species.GetDataValue( baseValue );

		return dead ? value / 2 : value;
	}

	// this resets everything to initial game state
	public void Reset()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// reset location
		m_location = Location.Starport;

		// reset coordinates (standing in front of the operations door)
		m_coordinates = new Vector3( -35.18f, 0.0f, 20.86f );

		// reset last coordinates
		m_lastStarportCoordinates = m_coordinates;
		m_lastHyperspaceCoordinates = Tools.GameToWorldCoordinates( new Vector3( 125.0f, 0.0f, 100.0f ) );
		m_lastStarSystemCoordinates = Vector3.zero;
		m_lastEncounterCoordinates = Vector3.zero;

		// reset the current stardate
		m_currentStardateYMD = "4620-01-01";
		m_currentStardateDHMY = "01.00-01-4620";

		// the stardates of a new game are in the original's calendar
		m_stardateCalendar = c_originalCalendar;

		// reset the current game time
		m_day = 0;
		m_lastLaunchDay = 0;
		m_hour = 0;
		m_minute = 0;
		m_second = 0;
		m_millisecond = 0;

		// reset current ids
		m_currentStarId = gameData.m_misc.m_arthStarId;
		m_currentPlanetId = 0;

		// no lifeform data yet
		m_knownLifeformClassList = new List<int>();
		m_knownLifeformOrderList = new List<int>();
		m_knownLifeformSpeciesList = new List<int>();
		m_unpaidLifeformDataValue = 0;

		// the game has not been won yet, and the Starport is still there
		m_gameWon = false;
		m_winTime = -1.0f;
		m_winBonusPending = false;
		m_starportDestroyed = false;
		m_currentEncounterId = 0;

		// facing north
		m_currentDirection = Vector3.forward;

		// not moving
		m_currentSpeed = 0.0f;
		m_currentMaximumSpeed = 10.0f;

		// allocate memory for last comms
		m_lastCommIds = new int[ c_numLastCommRaces * c_numLastCommSubjects ];

		// no comm line said yet
		m_commUseList = new List<CommUse>();

		// message list
		m_messageList = new List<string>();

		m_supplementalEvaluationList = new List<string>();
		m_colonyEvaluationList = new List<string>();
		m_missionCompleteShown = false;

		m_colonyRecommendationList = new List<int>();
		m_unsuitableRecommendationCount = 0;

		m_colonyRecommendationRecordList = new List<ColonyRecommendation>();
		m_colonyRecommendationCount = 0;
	}

	// how many times this comm line has been said in this game
	public int GetCommUseCount( int commId )
	{
		if ( m_commUseList != null )
		{
			foreach ( var commUse in m_commUseList )
			{
				if ( commUse.m_commId == commId )
				{
					return commUse.m_count;
				}
			}
		}

		return 0;
	}

	// call this when a comm line is said
	public void AddCommUse( int commId )
	{
		if ( m_commUseList == null )
		{
			m_commUseList = new List<CommUse>();
		}

		foreach ( var commUse in m_commUseList )
		{
			if ( commUse.m_commId == commId )
			{
				commUse.m_count++;

				return;
			}
		}

		m_commUseList.Add( new CommUse { m_commId = commId, m_count = 1 } );
	}

	// get the id of the last comm used to answer a question about this subject for this race
	public int GetLastCommId( GameData.Race race, GD_Comm.Subject subject )
	{
		var index = GetLastCommIndex( race, subject );

		if ( index < 0 )
		{
			return 0;
		}

		ValidateLastCommIds();

		return m_lastCommIds[ index ];
	}

	// set the id of the last comm used to answer a question about this subject for this race
	public void SetLastCommId( GameData.Race race, GD_Comm.Subject subject, int commId )
	{
		var index = GetLastCommIndex( race, subject );

		if ( index < 0 )
		{
			return;
		}

		ValidateLastCommIds();

		m_lastCommIds[ index ] = commId;
	}

	// convert race and subject to an index into m_lastCommIds (returns -1 if out of range)
	int GetLastCommIndex( GameData.Race race, GD_Comm.Subject subject )
	{
		var raceIndex = (int) race;
		var subjectIndex = (int) subject;

		// check each dimension separately so a bad subject can't spill over into the next race
		if ( ( raceIndex < 0 ) || ( raceIndex >= c_numLastCommRaces ) || ( subjectIndex < 0 ) || ( subjectIndex >= c_numLastCommSubjects ) )
		{
			Debug.LogWarning( "Last comm id out of range (" + race + ", " + subject + ")" );

			return -1;
		}

		return ( raceIndex * c_numLastCommSubjects ) + subjectIndex;
	}

	// make sure m_lastCommIds is allocated (save files from before it was serialized do not have it)
	void ValidateLastCommIds()
	{
		if ( ( m_lastCommIds == null ) || ( m_lastCommIds.Length != c_numLastCommRaces * c_numLastCommSubjects ) )
		{
			m_lastCommIds = new int[ c_numLastCommRaces * c_numLastCommSubjects ];
		}
	}

	// turns a stardate that is stored as year-month-day (the form that sorts - the bank ledger and the starport notices use it) into the form the game shows, day-month-year
	// (it only moves the three parts about, so no date class and no calendar of the computer's region is involved)
	public static string GetDisplayStardate( string stardateYMD )
	{
		var parts = ( stardateYMD == null ) ? null : stardateYMD.Split( '-' );

		if ( ( parts != null ) && ( parts.Length == 3 ) && ( parts[ 0 ].Length == 4 ) && ( parts[ 1 ].Length == 2 ) && ( parts[ 2 ].Length == 2 ) )
		{
			return parts[ 2 ] + "-" + parts[ 1 ] + "-" + parts[ 0 ];
		}

		// not a stardate we can read - show it as it is
		return stardateYMD ?? "";
	}

	// the stardate of a day of the game (0 is the first day) as year-month-day, in the original's calendar of 10 months of 30 days that begins on 01-01-4620
	// (the flare dates of the original's star data are in this calendar - Arth's sun flares on day 300, 01-01-4621: the original looks for a flare as a day ends, so it comes
	// as 30-10-4620, the last day of the year, ends, which the Elowan call "the final week of your Ten-month")
	public static string GetStardateYMD( int day )
	{
		day = Math.Max( 0, day );

		return ( c_firstYear + day / c_daysPerYear ).ToString( "D4" ) + "-" + ( ( day % c_daysPerYear ) / c_daysPerMonth + 1 ).ToString( "D2" ) + "-" + ( day % c_daysPerMonth + 1 ).ToString( "D2" );
	}

	// the stardate of a day and an hour of the game as the status display shows it: day.hour-month-year, in the original's calendar
	public static string GetStardateDHMY( int day, int hour )
	{
		day = Math.Max( 0, day );

		return ( day % c_daysPerMonth + 1 ).ToString( "D2" ) + "." + hour.ToString( "D2" ) + "-" + ( ( day % c_daysPerYear ) / c_daysPerMonth + 1 ).ToString( "D2" ) + "-" + ( c_firstYear + day / c_daysPerYear ).ToString( "D4" );
	}

	// a year-month-day stardate a save made before the port used the original's calendar holds (the real-world calendar from 01-01-4620) in the original's calendar
	// - returns it as it is if it cannot be read
	public static string ConvertRealCalendarYMD( string stardateYMD )
	{
		if ( DateTime.TryParseExact( stardateYMD, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dateTime ) )
		{
			return GetStardateYMD( ( dateTime - new DateTime( c_firstYear, 1, 1 ) ).Days );
		}

		return stardateYMD;
	}

	// the same for a day.hour-month-year stardate (the ship's log dates what the aliens said and the planets that were logged with it)
	public static string ConvertRealCalendarDHMY( string stardateDHMY )
	{
		if ( DateTime.TryParseExact( stardateDHMY, "dd.HH-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dateTime ) )
		{
			return GetStardateDHMY( ( dateTime.Date - new DateTime( c_firstYear, 1, 1 ) ).Days, dateTime.Hour );
		}

		return stardateDHMY;
	}

	// makes the two stardate texts from the day and the hour of the game
	public void MakeStardateTexts()
	{
		m_stardateDay = m_day;
		m_stardateHour = m_hour;

		m_currentStardateYMD = GetStardateYMD( m_day );
		m_currentStardateDHMY = GetStardateDHMY( m_day, m_hour );
	}

	// this updates the game time
	// call this to add a supplemental evaluation from Interstel (the Operations evaluation screen shows them)
	// whether an evaluation waits unread that keeps the ship docked: a colony evaluation or a supplemental evaluation (the lost terrain vehicle, the towing
	// charges) - not the completion of the mission, which Operations shows once all the same (CHKFLIGHT-OV WF40A checks ?EVAL, ?TV and ?RECALL, not the win's WMSG)
	public bool HasUnreadEvaluation()
	{
		return m_colonyEvaluationUnread || ( ( m_supplementalEvaluationList != null ) && ( m_supplementalEvaluationList.Count > 0 ) );
	}

	// true while the ship is back towed or without its terrain vehicle and Operations has not shown that evaluation yet: the original's ?>OP, set when the crew
	// disembarks with ?TV off or ?RECALL on (SHIPBUTTONS WF077) and cleared when Operations shows the evaluation (OP-OV WF47A) - meanwhile the ship cannot launch
	// from its docking bay (WF18E), and the Starport's rooms but Operations and the docking bay are locked (PORTMENU-OV WEA7F)
	public bool MustReportToOperations()
	{
		return ( m_supplementalEvaluationList != null ) && ( m_supplementalEvaluationList.Count > 0 );
	}

	// call this when a supplemental evaluation comes with a fine: it is booked when the evaluation screen shows the evaluation
	public void AddSupplementalFine( int fine )
	{
		if ( fine <= 0 )
		{
			return;
		}

		if ( m_supplementalFineList == null )
		{
			m_supplementalFineList = new List<int>();
		}

		m_supplementalFineList.Add( fine );
	}

	public void AddSupplementalEvaluation( string evaluation )
	{
		// save files from before the list have none
		if ( m_supplementalEvaluationList == null )
		{
			m_supplementalEvaluationList = new List<string>();
		}

		m_supplementalEvaluationList.Add( evaluation );
	}

	// the colony evaluations - a save file from before the list kept them among the supplemental ones, which all begin "SUPPLEMENTAL EVALUATION": any other there
	// moves here, oldest first (whether the loaded list is missing or empty)
	public List<string> GetColonyEvaluationList()
	{
		if ( m_colonyEvaluationList == null )
		{
			m_colonyEvaluationList = new List<string>();
		}

		if ( ( m_supplementalEvaluationList != null ) && m_supplementalEvaluationList.Exists( evaluation => ( evaluation == null ) || !evaluation.StartsWith( "SUPPLEMENTAL EVALUATION" ) ) )
		{
			m_colonyEvaluationList.InsertRange( 0, m_supplementalEvaluationList.FindAll( evaluation => ( evaluation != null ) && !evaluation.StartsWith( "SUPPLEMENTAL EVALUATION" ) ) );

			m_supplementalEvaluationList.RemoveAll( evaluation => ( evaluation == null ) || !evaluation.StartsWith( "SUPPLEMENTAL EVALUATION" ) );
		}

		return m_colonyEvaluationList;
	}

	// call this to add Interstel's evaluation of a planet recommended for colonization
	public void AddColonyEvaluation( string evaluation )
	{
		GetColonyEvaluationList().Add( evaluation );
	}

	// call this when a planet is recommended for colonization
	public void AddColonyRecommendation( int planetId )
	{
		// save files from before the list have none
		if ( m_colonyRecommendationList == null )
		{
			m_colonyRecommendationList = new List<int>();
		}

		if ( !m_colonyRecommendationList.Contains( planetId ) )
		{
			m_colonyRecommendationList.Add( planetId );

			// logged for good
			if ( m_loggedPlanetList == null )
			{
				m_loggedPlanetList = new List<int>();
			}

			if ( !m_loggedPlanetList.Contains( planetId ) )
			{
				m_loggedPlanetList.Add( planetId );
			}

			// the record the evaluation screen shows: the next number and today's stardate (the name comes when the player has typed it)
			if ( m_colonyRecommendationRecordList == null )
			{
				m_colonyRecommendationRecordList = new List<ColonyRecommendation>();
			}

			m_colonyRecommendationCount++;

			m_colonyRecommendationRecordList.Add( new ColonyRecommendation { m_planetId = planetId, m_number = m_colonyRecommendationCount, m_stardateYMD = m_currentStardateYMD, m_name = "" } );
		}
	}

	// whether this planet has been recommended before in the game (the original's logged planets, CAPTAIN WF3D3)
	public bool IsPlanetLogged( int planetId )
	{
		return ( ( m_loggedPlanetList != null ) && m_loggedPlanetList.Contains( planetId ) ) || ( ( m_colonyRecommendationList != null ) && m_colonyRecommendationList.Contains( planetId ) );
	}

	// the record of a pending recommendation of this planet (null if there is none: a save file from before the records)
	public ColonyRecommendation FindColonyRecommendation( int planetId )
	{
		if ( m_colonyRecommendationRecordList == null )
		{
			return null;
		}

		foreach ( var record in m_colonyRecommendationRecordList )
		{
			if ( record.m_planetId == planetId )
			{
				return record;
			}
		}

		return null;
	}

	// call this when the player has named the planet of a pending recommendation (cut to the original's 15 characters)
	public void NameColonyRecommendation( int planetId, string name )
	{
		var record = FindColonyRecommendation( planetId );

		if ( record != null )
		{
			name = ( name ?? "" ).Trim();

			record.m_name = ( name.Length > c_maximumPlanetNameLength ) ? name.Substring( 0, c_maximumPlanetNameLength ) : name;
		}
	}

	// a star hour passes in 10 seconds of play, as the original's kernel keeps it (?NEW-HO starts a new star hour every REAL-MS, 10,000 ms): 360 game hours in an
	// hour of play, a year of the original's calendar in 20 hours
	public const float c_gameSecondsPerSecond = 3600.0f / 10.0f;

	public void UpdateGameTime( float deltaTime )
	{
		deltaTime *= c_gameSecondsPerSecond;

		// convert deltaTime to milliseconds as an integer
		var deltaMilliseconds = Mathf.RoundToInt( deltaTime * 1000.0f );

		// update the day hour minute second and millisecond
		m_millisecond += deltaMilliseconds;
		m_second += m_millisecond / 1000;
		m_millisecond %= 1000;
		m_minute += m_second / 60;
		m_second %= 60;
		m_hour += m_minute / 60;
		m_minute %= 60;
		m_day += m_hour / 24;
		m_hour %= 24;

		// update the game time (represented as days with fractional precision up to seconds)
		m_gameTime = (float) m_day + ( (float) m_hour / 24 ) + ( (float) m_minute / ( 60 * 24 ) ) + ( (float) m_second / ( 60 * 60 * 24 ) );

		// update the current stardate - only when the day or the hour has changed, which is all the two texts show (making them every frame made garbage every frame)
		if ( ( m_stardateDay != m_day ) || ( m_stardateHour != m_hour ) )
		{
			MakeStardateTexts();
		}

		// if the player has shields up then deplete it every "star" hour
		if ( m_lastHour != m_hour )
		{
			m_lastHour = m_hour;

			var playerData = DataController.m_instance.m_playerData;

			if ( playerData.m_playerShip.m_shieldsAreUp )
			{
				playerData.m_playerShip.UseUpFuel( 0.1f );
			}
		}
	}
}
