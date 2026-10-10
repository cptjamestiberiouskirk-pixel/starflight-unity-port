using UnityEngine;

// Arth's sun flares on its day: day 300 of the game, 01-01-4621 in the original's calendar (its flare date in the game data; the Elowan: "the sun of the planet thou
// call'st Arth shall flare in the final week of your Ten-month"). The original looks for the flare of the star system it is in as each day ends, and not once the
// game has been won (disys.txt ?FLARE, ?WIN): a ship in Arth's system is incinerated (STRINFO 2.14), and the Starport is gone from then on (STRINFO 2.12 and 2.14:
// the win message is "received even after Starport has been destroyed", and a distress signal gets no response)
public static class ArthFlare
{
	// call this every frame the game is not paused (after the game time has been updated)
	public static void Update()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		// once is enough, and never after the win
		if ( playerData.m_general.m_starportDestroyed || playerData.m_general.m_gameWon )
		{
			return;
		}

		// the game time passes only out in space (not in the docking bay, which is part of the Starport)
		var location = playerData.m_general.m_location;

		if ( ( location == PD_General.Location.DockingBay ) || ( location == PD_General.Location.Starport ) )
		{
			return;
		}

		// has the day of the flare come?
		var arthStarId = gameData.m_misc.m_arthStarId;

		if ( ( arthStarId < 0 ) || ( arthStarId >= gameData.m_starList.Length ) )
		{
			return;
		}

		var arthStar = gameData.m_starList[ arthStarId ];

		if ( playerData.m_general.m_gameTime < arthStar.m_daysToNextFlare )
		{
			return;
		}

		// the Starport is gone
		playerData.m_general.m_starportDestroyed = true;

		// a ship in Arth's star system is caught in the flare
		if ( ( playerData.m_general.m_currentStarId == arthStarId ) && ( location != PD_General.Location.Hyperspace ) )
		{
			Incinerate( arthStar );
		}
	}

	// the ship and its crew are incinerated, and the game is over
	static void Incinerate( GD_Star star )
	{
		var spaceflightController = SpaceflightController.m_instance;
		var playerData = DataController.m_instance.m_playerData;
		var storyText = DataController.m_instance.m_gameData.FindStoryText( "FlareDeath" );

		if ( storyText != null )
		{
			// fill in the star, the stardate and the ship's name
			var text = storyText.m_text.Replace( "[XX, YY]", star.m_xCoordinate + ", " + star.m_yCoordinate ).Replace( "[DD-MM-YY]", PD_General.GetDisplayStardate( playerData.m_general.m_currentStardateYMD ) ).Replace( "[VESSEL NAME]", ( playerData.m_playerShip.m_name ?? "" ).ToUpper() );

			spaceflightController.m_messages.Clear();
			spaceflightController.m_messages.AddText( "<color=red>" + text + "</color>" );
		}

		// the ship is destroyed, which ends the game the way every loss of the ship does
		playerData.m_playerShip.m_shieldPoints = 0;
		playerData.m_playerShip.m_armorPoints = 0;

		spaceflightController.m_combatController.ApplyDamageToPlayer( 1, Vector3.up );
	}
}
