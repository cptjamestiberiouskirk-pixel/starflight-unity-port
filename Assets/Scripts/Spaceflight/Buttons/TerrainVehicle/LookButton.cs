using UnityEngine;

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
		var report = "<color=#FFFF00>You observe your surroundings:</color>\n";
		report += "Surface: <color=white>" + surface + "</color>\n";
		report += "Weather: <color=white>" + weather + "</color>\n";
		report += "Sky: <color=white>" + PlanetWeather.GetSkyText() + "</color>\n";
		report += "Atmosphere: <color=white>" + atmosphere + "</color>\n";
		report += "Gravity: <color=white>" + gravityDesc + " (" + gravity + "%)</color>";

		// what is next to the terrain vehicle (planet.txt, LOOK: "a description of the icon the eyeball is over is given" - the port describes what is nearest)
		report += DescribeNearby();

		SpaceflightController.m_instance.m_messages.AddText( report );
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}

	// how far Look sees a lifeform: the notes' "within 5 squares" (TerrainLifeform's close enough)
	const float c_lookRange = 45.0f;

	// the nearest lifeform in sight and what lies next to the terrain vehicle, in the notes' words (planet.txt, LOOK) - "" if there is nothing
	static string DescribeNearby()
	{
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( ( terrainGrid == null ) || ( terrainVehicle == null ) )
		{
			return "";
		}

		var vehiclePosition = terrainVehicle.transform.position;
		var text = "";

		// "Lifeforms : The detailed description is used from the template. In addition to the physical description any current Movement behavior is given as well
		// as Alive/Dead/Stunned info is given."
		if ( terrainGrid.m_terrainLifeforms != null )
		{
			TerrainLifeform nearest = null;
			var nearestDistance = c_lookRange;

			foreach ( var lifeform in terrainGrid.m_terrainLifeforms.m_lifeformList )
			{
				if ( ( lifeform == null ) || ( lifeform.m_species == null ) )
				{
					continue;
				}

				var delta = lifeform.transform.position - vehiclePosition;

				delta.y = 0.0f;

				if ( delta.magnitude <= nearestDistance )
				{
					nearest = lifeform;
					nearestDistance = delta.magnitude;
				}
			}

			if ( nearest != null )
			{
				var species = nearest.m_species;

				text += "\n\n<color=#FFFF00>A lifeform (" + species.GetBodyPlanText() + "):</color> <color=white>" + species.GetSizeText() + " and " + species.GetColourText() + ". " + nearest.GetMovementText() + " Status: " + nearest.GetStatusText() + ".</color>";
			}
		}

		// "Minerals : "An outcropping of minerals. Possibly incredibly valuable and useful""
		if ( terrainGrid.m_terrainElements != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainElements.transform )
			{
				var element = child.GetComponent<TerrainElement>();

				if ( ( element != null ) && element.IsInPickupRange() )
				{
					text += "\n\n<color=white>An outcropping of minerals. Possibly incredibly valuable and useful.</color>";

					break;
				}
			}
		}

		// "Ancient Ruins : "A very ancient looking crystaline structure, beautiful and mysterious."", the others of the Old Empire (the notes' scan: "Ruins from the
		// period of the Old Empire"), and "Dropped Cargo : "Ex-Cargo (good with garlic and butter sauce)""
		if ( terrainGrid.m_terrainRuins != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainRuins.transform )
			{
				var ruin = child.GetComponent<TerrainRuin>();

				if ( ( ruin != null ) && ruin.IsInReach( vehiclePosition ) )
				{
					text += ruin.m_isAncient ? "\n\n<color=white>A very ancient looking crystalline structure, beautiful and mysterious.</color>" : "\n\n<color=white>Ruins from the period of the Old Empire.</color>";
				}

				var dropped = child.GetComponent<TerrainDroppedCargo>();

				if ( ( dropped != null ) && dropped.IsInReach( vehiclePosition ) )
				{
					text += "\n\n<color=white>Ex-Cargo (good with garlic and butter sauce).</color>";
				}
			}
		}

		return text;
	}
}
