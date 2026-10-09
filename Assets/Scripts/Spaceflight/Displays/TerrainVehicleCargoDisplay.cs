
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TerrainVehicleCargoDisplay : ShipDisplay
{
	// text component for cargo labels
	public TextMeshProUGUI m_labelsText;

	// text component for cargo values
	public TextMeshProUGUI m_valuesText;

	// what the hold held when the text was made last (the text is made again only when it changes)
	int m_shownVolumeUsed = -1;
	int m_shownItemCount = -1;

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

		// make the text again
		m_shownVolumeUsed = -1;

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

		// has the hold changed since the text was made? (making it every frame makes garbage every frame)
		var terrainVehicle = DataController.m_instance.m_playerData.m_terrainVehicle;
		var volumeUsed = DataController.m_instance.m_gameData.m_misc.m_terrainVehicleVolume - terrainVehicle.GetRemainingVolume();
		var itemCount = ( ( terrainVehicle.m_elementStorage != null ) && ( terrainVehicle.m_elementStorage.m_elementList != null ) ? terrainVehicle.m_elementStorage.m_elementList.Count : 0 ) + ( ( terrainVehicle.m_artifactStorage != null ) && ( terrainVehicle.m_artifactStorage.m_artifactList != null ) ? terrainVehicle.m_artifactStorage.m_artifactList.Count : 0 );

		if ( ( volumeUsed == m_shownVolumeUsed ) && ( itemCount == m_shownItemCount ) )
		{
			return;
		}

		m_shownVolumeUsed = volumeUsed;
		m_shownItemCount = itemCount;

		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		var elementStorage = playerData.m_terrainVehicle.m_elementStorage;
		var artifactStorage = playerData.m_terrainVehicle.m_artifactStorage;

		var labels = "";
		var values = "";

		// header
		labels += "<color=#FFFF00>CARGO</color>\n\n";
		values += "\n\n";

		// list elements (with null checks)
		if ( elementStorage != null && elementStorage.m_elementList != null && elementStorage.m_elementList.Count > 0 )
		{
			foreach ( var elementRef in elementStorage.m_elementList )
			{
				var elementName = gameData.m_elementList[ elementRef.m_elementId ].m_name;
				labels += elementName + "\n";
				// (the cargo hold counts in tenths of a cubic meter)
				values += Tools.VolumeToText( elementRef.m_volume ) + " m³\n";
			}
		}

		// list artifacts (with null checks)
		if ( artifactStorage != null && artifactStorage.m_artifactList != null && artifactStorage.m_artifactList.Count > 0 )
		{
			foreach ( var artifactRef in artifactStorage.m_artifactList )
			{
				var artifactName = gameData.m_artifactList[ artifactRef.m_artifactId ].m_name;
				labels += "<color=#00FFFF>" + artifactName + "</color>\n";
				values += "\n";
			}
		}

		// show empty message if nothing (with null checks)
		int elementCount = ( elementStorage?.m_elementList?.Count ) ?? 0;
		int artifactCount = ( artifactStorage?.m_artifactList?.Count ) ?? 0;
		if ( elementCount == 0 && artifactCount == 0 )
		{
			labels += "<color=#808080>Empty</color>\n";
			values += "\n";
		}

		// capacity footer
		var remaining = playerData.m_terrainVehicle.GetRemainingVolume();
		var total = gameData.m_misc.m_terrainVehicleVolume;
		var used = total - remaining;

		labels += "\n<color=#808080>Capacity:</color>";
		values += "\n<color=#808080>" + Tools.VolumeToText( used ) + "/" + Tools.VolumeToText( total ) + " m³</color>";

		m_labelsText.text = labels;
		m_valuesText.text = values;
	}
}
