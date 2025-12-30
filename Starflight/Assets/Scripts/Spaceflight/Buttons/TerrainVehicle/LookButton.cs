
public class LookButton : ShipButton
{
	public override string GetLabel()
	{
		return "Look";
	}

	public override bool Execute()
	{
		// get to the game data and player data
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		// get current planet
		var planet = gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];

		SpaceflightController.m_instance.m_messages.Clear();

		// get environmental info
		var atmosphere = planet.GetAtmosphereText();
		var weather = gameData.m_weatherList[ planet.m_weatherId ].m_name;
		var surface = gameData.m_surfaceList[ planet.m_surfaceId ].m_name;
		var gravity = planet.m_gravity;

		// determine gravity description
		string gravityDesc;
		if ( gravity < 50 )
			gravityDesc = "Very Low";
		else if ( gravity < 80 )
			gravityDesc = "Low";
		else if ( gravity < 120 )
			gravityDesc = "Normal";
		else if ( gravity < 200 )
			gravityDesc = "High";
		else
			gravityDesc = "Crushing";

		// build observation report
		var report = "<color=yellow>You observe your surroundings:</color>\n";
		report += "Surface: <color=white>" + surface + "</color>\n";
		report += "Weather: <color=white>" + weather + "</color>\n";
		report += "Atmosphere: <color=white>" + atmosphere + "</color>\n";
		report += "Gravity: <color=white>" + gravityDesc + " (" + gravity + "%)</color>";

		SpaceflightController.m_instance.m_messages.AddText( report );
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
