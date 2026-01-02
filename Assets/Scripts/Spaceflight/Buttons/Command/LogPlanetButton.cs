
public class LogPlanetButton : ShipButton
{
	public override string GetLabel()
	{
		return "Log Planet";
	}

	public override bool Execute()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		switch ( playerData.m_general.m_location )
		{
			case PD_General.Location.JustLaunched:

				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_messages.Clear();

				SpaceflightController.m_instance.m_messages.AddText( "<color=white>That's Arth you fool!</color>" );

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				break;

			case PD_General.Location.Hyperspace:
			case PD_General.Location.StarSystem:
			case PD_General.Location.DockingBay:
			case PD_General.Location.Encounter:

				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_messages.Clear();

				SpaceflightController.m_instance.m_messages.AddText( "<color=white>We're not in orbit.</color>" );

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				break;

			case PD_General.Location.InOrbit:
			case PD_General.Location.Planetside:
			case PD_General.Location.Disembarked:

				// get planet and star data
				var planet = gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];
				var star = gameData.m_starList[ playerData.m_general.m_currentStarId ];

				// build planet name (coordinates + orbit position)
				var planetName = star.m_xCoordinate + ", " + star.m_yCoordinate + " - Planet " + planet.m_orbitPosition;

				// get planet info
				var atmosphere = planet.GetAtmosphereText();
				var hydrosphere = planet.GetHydrosphereText();
				var lithosphere = planet.GetLithosphereText();
				var surface = gameData.m_surfaceList[ planet.m_surfaceId ].m_name;
				var weather = gameData.m_weatherList[ planet.m_weatherId ].m_name;

				// build log message
				var message = "Mass: " + planet.m_mass + "\n" +
							  "Gravity: " + planet.m_gravity + "%\n" +
							  "Atmosphere: " + atmosphere + "\n" +
							  "Hydrosphere: " + hydrosphere + "\n" +
							  "Lithosphere: " + lithosphere + "\n" +
							  "Surface: " + surface + "\n" +
							  "Weather: " + weather + "\n" +
							  "Bio Density: " + planet.m_bioDensity + "%\n" +
							  "Mineral Density: " + planet.m_mineralDensity + "%";

				// get stardate
				var stardate = playerData.m_general.m_currentStardateDHMY;

				// try to add to ship's log
				bool wasAdded = playerData.m_shipsLog.AddPlanetLog( planet.m_id, stardate, planetName, message );

				SpaceflightController.m_instance.m_messages.Clear();

				if ( wasAdded )
				{
					SpaceflightController.m_instance.m_messages.AddText( "<color=green>Planet logged:</color>\n<color=white>" + planetName + "</color>" );
					SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
				}
				else
				{
					SpaceflightController.m_instance.m_messages.AddText( "<color=white>This planet has already been logged.</color>" );
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );
				}

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				break;
		}

		return false;
	}
}
