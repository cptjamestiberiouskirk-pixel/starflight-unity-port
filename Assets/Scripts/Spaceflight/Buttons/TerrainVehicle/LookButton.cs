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

		// the planet's fields as the original gives them on the surface, in its order (MAPS-OV WF4F4): SKY, TEMPERATURE and GRAVITY on one line, ATMO, HYDRO and
		// TERRAIN - the atmosphere and the hydrosphere by their main compound, the temperature of the region the terrain vehicle is in (WEDB2), and the land in
		// its colours (.SURFACE)
		Tools.WorldToLatLongCoordinates( playerData.m_general.m_lastDisembarkedCoordinates, out _, out var northSouth );
		var atmosphere = ( ( planet.m_atmosphereIdA >= 0 ) && ( planet.m_atmosphereIdA < gameData.m_atmosphereList.Length ) ) ? gameData.m_atmosphereList[ planet.m_atmosphereIdA ].m_name : "None";
		var hydrosphere = ( ( planet.m_hydrosphereId >= 0 ) && ( planet.m_hydrosphereId < gameData.m_hydrosphereList.Length ) ) ? gameData.m_hydrosphereList[ planet.m_hydrosphereId ].m_name : "None";

		var report = "<color=#FFFF00>You observe your surroundings:</color>\n";
		report += "Sky: <color=white>" + PlanetWeather.GetSkyText() + "</color>\n";
		report += "Temperature: <color=white>" + planet.GetRegionTemperatureText( northSouth ) + "</color>  Gravity: <color=white>" + planet.GetGravityText() + "</color>\n";
		report += "Atmo: <color=white>" + atmosphere + "</color>\n";
		report += "Hydro: <color=white>" + hydrosphere + "</color>\n";
		// the land, then the carpet lifeform of the region if it has one (MAPS-OV .CARPET: "<land>, WITH <kind> CARPET LIFEFORM." - PLSET-OV SETC draws the
		// kind once at the landing, for the landing's band; the port draws it once for the planet, ShippedLifeforms.Make)
		var carpet = GD_Species.GetLife( planet ).m_carpet;

		report += "Terrain: <color=white>" + planet.GetLandText() + ( string.IsNullOrEmpty( carpet ) ? "" : ", with " + carpet.ToLower() + " carpet lifeform" ) + "</color>";

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

				// its description as the shipped game gives it (SENT-OV SAYIT)
				text += "\n\n<color=#FFFF00>A lifeform (" + species.GetBodyPlanText() + "):</color> <color=white>" + species.GetDescription() + " " + nearest.GetMovementText() + " Status: " + nearest.GetStatusText() + ".</color>";
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
					var surfaces = DataController.m_instance.m_playerData.m_planetSurfaces;
					var droppedCargo = ( surfaces != null ) ? surfaces.FindDroppedCargo( dropped.m_droppedCargoId ) : null;

					// (the wreck of a terrain vehicle that ran out of fuel here: the port's words)
					text += ( ( droppedCargo != null ) && droppedCargo.m_isVehicleWreck ) ? "\n\n<color=white>The wreck of a terrain vehicle, its tank empty.</color>" : "\n\n<color=white>Ex-Cargo (good with garlic and butter sauce).</color>";
				}
			}
		}

		return text;
	}
}
