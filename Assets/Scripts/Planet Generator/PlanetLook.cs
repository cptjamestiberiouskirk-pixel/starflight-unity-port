
using UnityEngine;

// how the planets are drawn in the star system and in orbit: as the port has always drawn them (Original), or in natural colours from their climate,
// with a sky of scattered sunlight and clouds from their weather (Natural, NaturalPlanet); the player picks it on the save panel's Colors screen
public static class PlanetLook
{
	public enum Look
	{
		Original, Natural
	}

	// where the player's choice is kept: a setting of this computer, not of a saved game
	public const string c_lookKey = "Planet Look";

	// the look a player who has not picked one gets
	public const Look c_defaultLook = Look.Natural;

	// the look in use (read from the setting the first time it is asked for)
	static bool s_lookKnown;
	static Look s_look;

	// called when the look changes, so that the planets on screen take it at once
	public static event System.Action Changed;

	// returns the look in use (the player's setting, the default if there is none or it cannot be read)
	public static Look GetLook()
	{
		if ( !s_lookKnown )
		{
			s_look = c_defaultLook;

			try
			{
				var name = PlayerPrefs.GetString( c_lookKey, c_defaultLook.ToString() );

				if ( name == Look.Original.ToString() )
				{
					s_look = Look.Original;
				}
				else if ( name == Look.Natural.ToString() )
				{
					s_look = Look.Natural;
				}
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The planet look could not be read: " + exception.Message );
			}

			s_lookKnown = true;
		}

		return s_look;
	}

	// true if the planets are drawn in their natural look
	public static bool IsNatural()
	{
		return GetLook() == Look.Natural;
	}

	// changes the look and lets the planets on screen know (remember false leaves the player's setting as it is, for a test)
	public static void SetLook( Look look, bool remember = true )
	{
		var previousLook = GetLook();

		if ( remember )
		{
			try
			{
				PlayerPrefs.SetString( c_lookKey, look.ToString() );
				PlayerPrefs.Save();
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The planet look could not be saved: " + exception.Message );
			}
		}

		if ( look == previousLook )
		{
			return;
		}

		s_look = look;

		Changed?.Invoke();
	}
}
