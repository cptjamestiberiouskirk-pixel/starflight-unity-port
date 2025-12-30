
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TerrainVehicleCargoDisplay : ShipDisplay
{
	// text component for cargo labels
	public TextMeshProUGUI m_labelsText;

	// text component for cargo values
	public TextMeshProUGUI m_valuesText;

	TerrainVehicleCargoDisplay()
	{
	}

	// the display label
	public override string GetLabel()
	{
		return "Cargo";
	}

	public override void Show()
	{
		base.Show();
		UpdateCargoDisplay();
	}

	public override void Update()
	{
		UpdateCargoDisplay();
	}

	void UpdateCargoDisplay()
	{
		if ( m_labelsText == null || m_valuesText == null )
		{
			return;
		}

		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		var elementStorage = playerData.m_terrainVehicle.m_elementStorage;
		var artifactStorage = playerData.m_terrainVehicle.m_artifactStorage;

		var labels = "";
		var values = "";

		// header
		labels += "<color=yellow>CARGO</color>\n\n";
		values += "\n\n";

		// list elements
		if ( elementStorage.m_elementList.Count > 0 )
		{
			foreach ( var elementRef in elementStorage.m_elementList )
			{
				var elementName = gameData.m_elementList[ elementRef.m_elementId ].m_name;
				labels += elementName + "\n";
				values += elementRef.m_volume + " m³\n";
			}
		}

		// list artifacts
		if ( artifactStorage.m_artifactList.Count > 0 )
		{
			foreach ( var artifactRef in artifactStorage.m_artifactList )
			{
				var artifactName = gameData.m_artifactList[ artifactRef.m_artifactId ].m_name;
				labels += "<color=cyan>" + artifactName + "</color>\n";
				values += "\n";
			}
		}

		// show empty message if nothing
		if ( elementStorage.m_elementList.Count == 0 && artifactStorage.m_artifactList.Count == 0 )
		{
			labels += "<color=gray>Empty</color>\n";
			values += "\n";
		}

		// capacity footer
		var remaining = playerData.m_terrainVehicle.GetRemainingVolume();
		var total = gameData.m_misc.m_terrainVehicleVolume;
		var used = total - remaining;

		labels += "\n<color=gray>Capacity:</color>";
		values += "\n<color=gray>" + used + "/" + total + " m³</color>";

		m_labelsText.text = labels;
		m_valuesText.text = values;
	}
}
