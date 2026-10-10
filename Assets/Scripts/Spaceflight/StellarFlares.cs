using UnityEngine;

// the stars flare on their days (the flare date of every star in the game data, in the original's calendar). The original looks for the flare of the star system
// the ship is in as each day ends, and not once the game has been won (disys.txt ?FLARE, ?WIN): a ship in a star system when its star flares is incinerated
// (STRINFO 2.14), and a star that flares with the ship elsewhere does it unseen. Arth's sun is the one that matters to everyone: on its day (day 300, 01-01-4621;
// the Elowan: "the final week of your Ten-month") the Starport is gone, wherever the ship is (STRINFO 2.12 and 2.14: the win message is "received even after
// Starport has been destroyed", and a distress signal gets no response)
public class StellarFlares
{
	// the star system the ship was in at the last update (-1 when it was not in one), and the game day then
	int m_lastStarId = -1;
	int m_lastDay;

	// call this every frame the game is not paused (after the game time has been updated)
	public void Update()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var location = playerData.m_general.m_location;

		// the game time passes only out in space (not in the docking bay, which is part of the Starport), and no star flares once the game has been won
		if ( ( location == PD_General.Location.DockingBay ) || ( location == PD_General.Location.Starport ) || playerData.m_general.m_gameWon )
		{
			m_lastStarId = -1;

			return;
		}

		var today = Mathf.FloorToInt( playerData.m_general.m_gameTime );
		var starId = playerData.m_general.m_currentStarId;

		if ( ( starId < 0 ) || ( starId >= gameData.m_starList.Length ) )
		{
			return;
		}

		// is the ship in a star system? (in an encounter it may be out in hyperspace, so the flares wait until it is back)
		var inStarSystem = ( location != PD_General.Location.Hyperspace ) && ( location != PD_General.Location.Encounter );

		// Arth's sun flares on its day: the Starport is gone, and a ship in Arth's system with it
		var arthStarId = gameData.m_misc.m_arthStarId;

		if ( !playerData.m_general.m_starportDestroyed && ( arthStarId >= 0 ) && ( arthStarId < gameData.m_starList.Length ) && ( playerData.m_general.m_gameTime >= gameData.m_starList[ arthStarId ].m_daysToNextFlare ) )
		{
			playerData.m_general.m_starportDestroyed = true;

			if ( inStarSystem && ( starId == arthStarId ) )
			{
				Incinerate( gameData.m_starList[ arthStarId ] );

				return;
			}
		}

		if ( !inStarSystem )
		{
			m_lastStarId = -1;

			return;
		}

		// just arrived in this star system? a star does not flare at a ship that comes in on its day (the original looks for the flare as a day ends)
		if ( starId != m_lastStarId )
		{
			m_lastStarId = starId;
			m_lastDay = today;

			return;
		}

		// has the day of this star's flare come while the ship is here?
		var star = gameData.m_starList[ starId ];

		if ( ( m_lastDay < star.m_daysToNextFlare ) && ( today >= star.m_daysToNextFlare ) )
		{
			Incinerate( star );
		}

		m_lastDay = today;
	}

	// the ship and its crew are incinerated, and the game is over
	static void Incinerate( GD_Star star )
	{
		var playerData = DataController.m_instance.m_playerData;
		var storyText = DataController.m_instance.m_gameData.FindStoryText( "FlareDeath" );

		// STRINFO's text, with the star, the stardate and the ship's name filled in
		var text = "INCINERATED.\nGAME OVER";

		if ( storyText != null )
		{
			text = storyText.m_text.Replace( "[XX, YY]", star.m_xCoordinate + ", " + star.m_yCoordinate ).Replace( "[DD-MM-YY]", PD_General.GetDisplayStardate( playerData.m_general.m_currentStardateYMD ) ).Replace( "[VESSEL NAME]", ( playerData.m_playerShip.m_name ?? "" ).ToUpper() );
		}

		// the messages say it now, and the game over screen says it again once the ship has exploded
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + text + "</color>" );

		SpaceflightController.m_instance.m_combatController.DestroyPlayerShip( text );
	}
}
