
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

	// dimensions of the last comm ids table (race x subject)
	public const int c_numLastCommRaces = 20;
	public const int c_numLastCommSubjects = 16;

	// keep track of responses to questions on a per race basis
	// stored flat ( race * c_numLastCommSubjects + subject ) because JsonUtility does not serialize multi-dimensional arrays
	// always go through GetLastCommId / SetLastCommId
	public int[] m_lastCommIds;

	// lines of messages
	public List<string> m_messageList;

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
		m_hour = 0;
		m_minute = 0;
		m_second = 0;
		m_millisecond = 0;

		// reset current ids
		m_currentStarId = gameData.m_misc.m_arthStarId;
		m_currentPlanetId = 0;
		m_currentEncounterId = 0;

		// facing north
		m_currentDirection = Vector3.forward;

		// not moving
		m_currentSpeed = 0.0f;
		m_currentMaximumSpeed = 10.0f;

		// allocate memory for last comms
		m_lastCommIds = new int[ c_numLastCommRaces * c_numLastCommSubjects ];

		// message list
		m_messageList = new List<string>();
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
	public void UpdateGameTime( float deltaTime )
	{
		// 175.2 game hours pass in an hour of play (365 days in 50 hours - the sources do not give the original's rate, so the rate stays as it was when the calendar became the original's)
		var scale = ( 365.0f * 24.0f ) / 50.0f;

		deltaTime *= scale;

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
