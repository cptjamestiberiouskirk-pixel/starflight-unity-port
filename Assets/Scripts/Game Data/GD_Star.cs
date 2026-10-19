
using UnityEngine;
using System;

[Serializable]

public class GD_Star
{
	public const int c_maxNumPlanets = 8;

	// the flare day of the original is a signed 16 bit number
	const int c_maxSignedDayCount = 32767;
	const int c_dayCountRange = 65536;

	public int m_id;

	public int m_xCoordinate;
	public int m_yCoordinate;

	public string m_class;

	// the day of the game (0 is the first day) the star flares on - negative for a star that flared that many days before the game began (see Initialize)
	public int m_daysToNextFlare;

	// the days before its flare from which a star reads stable, and within which it gives the days to its flare (ANALYZE-OV WF31E)
	const int c_stableDays = 399;
	const int c_unstableDays = 60;
	public int m_daysSincePreviousFlare;

	// the same day as a date of the original's calendar of 10 months of 30 days - not used by the game
	public int m_yearOfNextFlare;
	public int m_monthOfNextFlare;
	public int m_dayOfNextFlare;

	public bool m_insideNebula;

	GD_Planet[] m_planetList;
	Vector3 m_gameCoordinates;
	Vector3 m_worldCoordinates;
	GD_SpectralClass m_spectralClass;

	public void Initialize( GameData gameData )
	{
		// the original kept the flare day of a star as one signed 16 bit number, negative for a star that had flared before the game began. The game data has it
		// unsigned for the 36 stars that had (65536 more: Earth's sun at 65476 flared 60 days before the start, and m_daysSincePreviousFlare holds the -60), so put the sign back
		if ( m_daysToNextFlare > c_maxSignedDayCount )
		{
			m_daysToNextFlare -= c_dayCountRange;
		}

		// allocate planets array
		m_planetList = new GD_Planet[ 8 ];

		// initialize planets array with null (means no planet in that orbit)
		for ( var i = 0; i < c_maxNumPlanets; i++ )
		{
			m_planetList[ i ] = null;
		}

		// find planets and put them in the right spot in the planets array
		foreach ( var planet in gameData.m_planetList )
		{
			if ( planet.m_starId == m_id )
			{
				m_planetList[ planet.m_orbitPosition - 1 ] = planet;
			}
		}

		// generate coordinate vectors
		m_gameCoordinates = new Vector3( m_xCoordinate, 0.0f, m_yCoordinate );
		m_worldCoordinates = Tools.GameToWorldCoordinates( m_gameCoordinates );

		// find and link to the spectral class data
		foreach ( var spectralClass in gameData.m_spectralClassList )
		{
			if ( spectralClass.m_class == m_class )
			{
				m_spectralClass = spectralClass;
				break;
			}
		}
	}

	// the star's stability as the analysis of a star gives it (ANALYZE-OV WF31E): "STABLE" while its flare is 399 Arth days or more away, once it has flared and
	// once the game is won, with "(POST-FLARE)" after a flare that came before the win (WEC04); "SLIGHTLY UNSTABLE" from 398 to 61 days before its flare; and
	// within 60 days "UNSTABLE - ESTIMATED TIME TO FLARE:" and the days - winTime is the game time of the win, below 0 if it is not known
	public string GetStabilityText( float today, bool won, float winTime )
	{
		var days = Mathf.CeilToInt( m_daysToNextFlare - today );

		if ( won || ( days < 1 ) || ( days >= c_stableDays ) )
		{
			return HasFlared( today, won, winTime ) ? "Stable (post-flare)" : "Stable";
		}

		if ( days > c_unstableDays )
		{
			return "Slightly unstable";
		}

		return "Unstable - estimated time to flare: " + days + ( ( days == 1 ) ? " Arth day" : " Arth days" );
	}

	// true once the star has flared: its flare day has come, and the game was not won before it (ANALYZE-OV WEC04: STARDATE not below the flare date, and ?WIN
	// none or after it) - a flare day after the win had no flare (with no time of the win, a game saved after the win before it was kept, the flare counts as
	// before it)
	public bool HasFlared( float today, bool won, float winTime )
	{
		var days = Mathf.CeilToInt( m_daysToNextFlare - today );

		return ( days < 1 ) && ( !won || ( winTime < 0.0f ) || ( Mathf.FloorToInt( winTime ) > m_daysToNextFlare ) );
	}

	// call this to get the breach distance
	public float GetBreachDistance()
	{
		return ( 1.0f + m_spectralClass.m_scale / 0.6f ) * 48.0f;
	}

	public Vector3 GetWorldCoordinates()
	{
		return m_worldCoordinates;
	}

	public GD_SpectralClass GetSpectralClass()
	{
		return m_spectralClass;
	}

	public GD_Planet[] GetPlanetList()
	{
		return m_planetList;
	}

	public GD_Planet GetPlanet( int orbitPositionMinusOne )
	{
		return m_planetList[ orbitPositionMinusOne ];
	}
}
