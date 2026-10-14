
using UnityEngine;
using System.Collections.Generic;

public class ScanButton : ShipButton
{
	// scan range for detecting nearby objects
	const float c_scanRange = 50.0f;

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

		// build elements list for this planet
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

		// scan for nearby objects
		var nearbyReport = ScanNearbyObjects();

		// build scan report
		var report = "<color=#FFFF00>Local Scan Results:</color>\n";
		report += "Mineral Density: <color=white>" + planet.m_mineralDensity + "%</color>\n";
		report += "Bio Density: <color=white>" + planet.m_bioDensity + "%</color>\n";
		report += "Elements: <color=white>" + elementsText + "</color>";

		// add nearby objects if any found
		if ( nearbyReport.Length > 0 )
		{
			report += "\n\n<color=#FFFF00>Nearby Objects:</color>\n" + nearbyReport;
		}
		else
		{
			report += "\n\n<color=#808080>No objects within scan range.</color>";
		}

		SpaceflightController.m_instance.m_messages.AddText( report );
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}

	// scan for nearby objects and build a report
	string ScanNearbyObjects()
	{
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( terrainGrid == null || terrainVehicle == null )
		{
			return "";
		}

		var vehiclePos = terrainVehicle.transform.position;

		// track what we find
		var mineralDeposits = new Dictionary<string, int>(); // element name -> count
		int rockCount = 0;
		int vegetationCount = 0;
		int ruinCount = 0;
		int artifactCount = 0;
		int droppedCount = 0;

		// scan for mineral deposits (elements)
		if ( terrainGrid.m_terrainElements != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainElements.transform )
			{
				var distance = Vector3.Distance( child.position, vehiclePos );

				if ( distance <= c_scanRange )
				{
					var terrainElement = child.GetComponent<TerrainElement>();

					// a deposit that has just been picked up is still there while its transporter effect plays - it is not a deposit any more
					if ( ( terrainElement != null ) && !terrainElement.HasBeenPickedUp() )
					{
						var elementName = terrainElement.GetElementName();

						if ( mineralDeposits.ContainsKey( elementName ) )
							mineralDeposits[ elementName ]++;
						else
							mineralDeposits[ elementName ] = 1;

						// add floating label to this element
						ShowLabelOnObject( child.gameObject, elementName, LabelColour( UIKitTheme.ColorRole.StatusFuel, Color.green ) );
					}
				}
			}
		}

		// scan for rocks
		if ( terrainGrid.m_terrainRocks != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainRocks.transform )
			{
				var distance = Vector3.Distance( child.position, vehiclePos );

				if ( distance <= c_scanRange )
				{
					rockCount++;

					// add floating label to this rock
					ShowLabelOnObject( child.gameObject, "Rock", LabelColour( UIKitTheme.ColorRole.TextSecondary, Color.gray ) );
				}
			}
		}

		// scan for vegetation
		if ( terrainGrid.m_terrainTrees != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainTrees.transform )
			{
				var distance = Vector3.Distance( child.position, vehiclePos );

				if ( distance <= c_scanRange )
				{
					vegetationCount++;

					// add floating label to this vegetation
					ShowLabelOnObject( child.gameObject, "Vegetation", LabelColour( UIKitTheme.ColorRole.StatusFuel, new Color( 0.4f, 0.7f, 0.4f ) ) );
				}
			}
		}

		// scan for ruins
		if ( terrainGrid.m_terrainRuins != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainRuins.transform )
			{
				var distance = Vector3.Distance( child.position, vehiclePos );

				// something the terrain vehicle dropped lies with the ruins
				if ( ( distance <= c_scanRange ) && ( child.GetComponent<TerrainDroppedCargo>() != null ) )
				{
					droppedCount++;

					ShowLabelOnObject( child.gameObject, "Dropped cargo", LabelColour( UIKitTheme.ColorRole.TextWarning, Color.yellow ) );

					continue;
				}

				if ( distance <= c_scanRange )
				{
					ruinCount++;

					// does it still hold an artifact? (the sensors see what is in a ruin - the label is the artifact's marker for now)
					var label = "Ruin";
					var ruin = child.GetComponent<TerrainRuin>();
					var playerData = DataController.m_instance.m_playerData;

					if ( ( ruin != null ) && ( playerData.m_planetSurfaces != null ) )
					{
						foreach ( var artifactSiteId in ruin.m_artifactSiteIds )
						{
							if ( !playerData.m_planetSurfaces.IsArtifactSiteTaken( artifactSiteId ) )
							{
								label = "Ruin - artifact";
								artifactCount++;
							}
						}

						// and what a random ruin still holds
						foreach ( var item in ruin.m_randomItems )
						{
							if ( playerData.m_planetSurfaces.IsRandomRuinItemTaken( item.m_key ) )
							{
								continue;
							}

							if ( item.m_artifactId >= 0 )
							{
								label = "Ruin - artifact";
								artifactCount++;
							}
							else if ( label == "Ruin" )
							{
								label = "Ruin - Endurium";
							}
						}
					}

					// add floating label to this ruin
					ShowLabelOnObject( child.gameObject, label, LabelColour( UIKitTheme.ColorRole.TextPrimary, Color.white ) );
				}
			}
		}

		// scan for lifeforms: the nearest one is read as the 1984 notes' scan has it (planet.txt, info display 3)
		var lifeformCount = 0;
		TerrainLifeform nearestLifeform = null;
		var nearestDistance = float.MaxValue;

		if ( terrainGrid.m_terrainLifeforms != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainLifeforms.transform )
			{
				var lifeform = child.GetComponent<TerrainLifeform>();
				var distance = Vector3.Distance( child.position, vehiclePos );

				if ( ( lifeform == null ) || ( distance > c_scanRange ) )
				{
					continue;
				}

				lifeformCount++;

				ShowLabelOnObject( child.gameObject, "Lifeform", LabelColour( UIKitTheme.ColorRole.TextAlert, Color.magenta ) );

				if ( distance < nearestDistance )
				{
					nearestDistance = distance;
					nearestLifeform = lifeform;
				}
			}
		}

		// build the report
		var report = "";

		// the lifeforms, and the reading of the nearest
		if ( nearestLifeform != null )
		{
			var species = nearestLifeform.m_species;

			report += "<color=#FF80FF>Lifeform" + ( lifeformCount > 1 ? "s" : "" ) + ": " + lifeformCount + "</color>\n";
			report += "Nearest: <color=white>" + species.GetBodyPlanText() + " (" + nearestLifeform.GetStatusText() + ")</color>\n";
			report += "Volume: <color=white>" + Tools.VolumeToText( species.m_volume ) + " cubic meters</color>\n";
			report += "Aggression analog: <color=white>" + species.GetAggressionAnalog() + "</color>\n";
			report += "Intelligence analog: <color=white>" + species.GetIntelligenceAnalog() + "</color>\n";
			report += "Niche: <color=white>" + species.GetNicheText() + "</color>\n";
			report += "Composition: <color=white>" + string.Join( ", ", species.m_composition ) + "</color>\n";
		}

		// list ruins (the messages in them are recorded with the cargo button)
		if ( ruinCount > 0 )
		{
			report += "<color=white>Ruin" + ( ruinCount > 1 ? "s" : "" ) + ": " + ruinCount + "</color>\n";
		}

		// list what the terrain vehicle has dropped (picked up again with the cargo button)
		if ( droppedCount > 0 )
		{
			report += "<color=yellow>Dropped cargo: " + droppedCount + "</color>\n";
		}

		// list the artifacts the ruins still hold (picked up with the cargo button)
		if ( artifactCount > 0 )
		{
			report += "<color=#00FFFF>Artifact" + ( artifactCount > 1 ? "s" : "" ) + " in the ruins: " + artifactCount + "</color>\n";
		}

		// list mineral deposits (pickable)
		foreach ( var deposit in mineralDeposits )
		{
			report += "<color=#00FF00>" + deposit.Key + " deposit" + ( deposit.Value > 1 ? "s" : "" ) + ": " + deposit.Value + "</color>\n";
		}

		// list rocks (not pickable)
		if ( rockCount > 0 )
		{
			report += "<color=#808080>Rock" + ( rockCount > 1 ? "s" : "" ) + ": " + rockCount + "</color>\n";
		}

		// list vegetation (not pickable)
		if ( vegetationCount > 0 )
		{
			report += "<color=#808080>Vegetation: " + vegetationCount + "</color>\n";
		}

		return report.TrimEnd( '\n' );
	}

	// the colour of a label: the UI kit's colour role (the elements and vegetation in its green, rocks in its secondary text colour, dropped cargo in its amber,
	// ruins and artifacts in its primary text colour, lifeforms in its alert colour), or the original colour without the theme
	static Color LabelColour( UIKitTheme.ColorRole role, Color original )
	{
		// (the labels stand over the terrain, so they take the theme of the 3D views: dark in either mode)
		var theme = UIKitTheme.GetViewTheme();

		return ( theme != null ) ? theme.GetColor( role ) : original;
	}

	// show a floating label on an object
	void ShowLabelOnObject( GameObject obj, string text, Color color )
	{
		// get or add the label component
		var label = obj.GetComponent<TerrainObjectLabel>();

		if ( label == null )
		{
			label = obj.AddComponent<TerrainObjectLabel>();
		}

		// show the label
		label.ShowLabel( text, color );
	}
}
