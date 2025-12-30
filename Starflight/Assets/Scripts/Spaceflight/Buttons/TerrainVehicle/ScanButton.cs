
public class ScanButton : ShipButton
{
	public override string GetLabel()
	{
		return "Scan";
	}

	public override bool Execute()
	{
		// get to the game data and player data
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		// get current planet
		var planet = gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];

		SpaceflightController.m_instance.m_messages.Clear();

		// build elements list
		var elementsText = "";
		var elementIds = new int[] { planet.m_elementIdA, planet.m_elementIdB, planet.m_elementIdC };

		foreach ( var elementId in elementIds )
		{
			if ( elementId >= 0 && elementId < gameData.m_elementList.Length )
			{
				if ( elementsText.Length > 0 )
					elementsText += ", ";
				elementsText += gameData.m_elementList[ elementId ].m_name;
			}
		}

		if ( elementsText.Length == 0 )
			elementsText = "None detected";

		// build scan report
		var report = "<color=yellow>Local Scan Results:</color>\n";
		report += "Mineral Density: <color=white>" + planet.m_mineralDensity + "%</color>\n";
		report += "Bio Density: <color=white>" + planet.m_bioDensity + "%</color>\n";
		report += "Elements: <color=white>" + elementsText + "</color>";

		SpaceflightController.m_instance.m_messages.AddText( report );
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
