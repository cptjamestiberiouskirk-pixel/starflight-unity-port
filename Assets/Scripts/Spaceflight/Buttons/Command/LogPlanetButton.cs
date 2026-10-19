
public class LogPlanetButton : ShipButton
{
	// the command buttons that were on the console when the question was asked (Yes and No go back to them)
	static ButtonController.ButtonSet s_commandButtonSet = ButtonController.ButtonSet.CommandA;

	// back to the command buttons after the question
	public static void BackToCommandButtons()
	{
		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( s_commandButtonSet );
	}

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

				// Arth itself is not a planet to recommend (CAPTAIN (/LOG-PLAN): "THAT'S ARTH YOU FOOL!")
				if ( playerData.m_general.m_currentPlanetId == gameData.m_misc.m_arthPlanetId )
				{
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );

					SpaceflightController.m_instance.m_messages.Clear();

					SpaceflightController.m_instance.m_messages.AddText( "<color=white>That's Arth you fool!</color>" );

					SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

					break;
				}

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
							  "Bio Density: " + PlayerData.GetBioDensityOf( planet ) + "%\n" +
							  "Mineral Density: " + planet.m_mineralDensity + "%";

				// get stardate
				var stardate = playerData.m_general.m_currentStardateDHMY;

				// the planet's data goes into the port's ship's log, once
				playerData.m_shipsLog.AddPlanetLog( planet.m_id, stardate, planetName, message );

				// the original clears the window and asks only whether to recommend the planet, every time (CAPTAIN (/LOG-PLAN): "RECOMMEND THIS PLANET FOR /
				// COLONIZATION? [N Y]") - a planet recommended before is answered after Yes (RecommendYesButton)
				s_commandButtonSet = SpaceflightController.m_instance.m_buttonController.GetCurrentButtonSet();

				SpaceflightController.m_instance.m_messages.Clear();
				SpaceflightController.m_instance.m_messages.AddText( "<color=white>Recommend this planet for colonization?</color>" );

				SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

				SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.RecommendPlanet );

				return false;
		}

		return false;
	}
}
