
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
		var report = "<color=yellow>Local Scan Results:</color>\n";
		report += "Mineral Density: <color=white>" + planet.m_mineralDensity + "%</color>\n";
		report += "Bio Density: <color=white>" + planet.m_bioDensity + "%</color>\n";
		report += "Elements: <color=white>" + elementsText + "</color>";

		// add nearby objects if any found
		if ( nearbyReport.Length > 0 )
		{
			report += "\n\n<color=yellow>Nearby Objects:</color>\n" + nearbyReport;
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

		// scan for mineral deposits (elements)
		if ( terrainGrid.m_terrainElements != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainElements.transform )
			{
				var distance = Vector3.Distance( child.position, vehiclePos );

				if ( distance <= c_scanRange )
				{
					var terrainElement = child.GetComponent<TerrainElement>();

					if ( terrainElement != null )
					{
						var elementName = terrainElement.GetElementName();

						if ( mineralDeposits.ContainsKey( elementName ) )
							mineralDeposits[ elementName ]++;
						else
							mineralDeposits[ elementName ] = 1;

						// add floating label to this element
						ShowLabelOnObject( child.gameObject, elementName, Color.green );
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
					ShowLabelOnObject( child.gameObject, "Rock", Color.gray );
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
					ShowLabelOnObject( child.gameObject, "Vegetation", new Color( 0.4f, 0.7f, 0.4f ) );
				}
			}
		}

		// build the report
		var report = "";

		// list mineral deposits (pickable)
		foreach ( var deposit in mineralDeposits )
		{
			report += "<color=green>" + deposit.Key + " deposit" + ( deposit.Value > 1 ? "s" : "" ) + ": " + deposit.Value + "</color>\n";
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
