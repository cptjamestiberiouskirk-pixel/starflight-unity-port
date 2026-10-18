
using System.Collections.Generic;
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

	// the window in which the artifact marked on the cargo list turns (made the first time it is needed), the artifact it was last given (-2 before the
	// first), whether it is placed for the text as it is, and the ids it is given (kept, so that nothing is made while nothing changes)
	ArtifactPreview m_artifactPreview;
	int m_shownArtifactId = -2;
	bool m_artifactWindowPlaced;
	readonly List<int> m_artifactWindowIds = new List<int>();

	// the window goes under the list, this far from it and from the display's bottom, at most and at least this big (smaller, it is not shown), with a
	// texture this many pixels across
	const float c_artifactWindowMargin = 12.0f;
	const float c_artifactWindowLargest = 220.0f;
	const float c_artifactWindowSmallest = 90.0f;
	const int c_artifactWindowTextureSize = 384;

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

		// and place the artifact window for it
		m_artifactWindowPlaced = false;

		UpdateArtifactWindow();
	}

	public override void Update()
	{
		UpdateCargoDisplay();
		UpdateArtifactWindow();
	}

	// shows the artifact marked on the cargo list turning in a window under the list, where there is room for it (nothing is made while nothing changes)
	void UpdateArtifactWindow()
	{
		var artifactId = TVCargoButton.GetMarkedArtifactId();

		if ( m_artifactWindowPlaced && ( artifactId == m_shownArtifactId ) )
		{
			return;
		}

		var fits = PlaceArtifactWindow();

		if ( m_artifactPreview == null )
		{
			return;
		}

		m_artifactWindowPlaced = true;
		m_shownArtifactId = artifactId;

		m_artifactWindowIds.Clear();

		if ( fits && ( artifactId >= 0 ) )
		{
			m_artifactWindowIds.Add( artifactId );
		}

		m_artifactPreview.Show( m_artifactWindowIds );
	}

	// puts the artifact window under the list's text, in the middle of the display (making it the first time) - false if there is too little room
	bool PlaceArtifactWindow()
	{
		if ( m_labelsText == null )
		{
			return false;
		}

		var display = m_labelsText.rectTransform.parent as RectTransform;

		if ( display == null )
		{
			return false;
		}

		// the bottom of the list's text and of the display, in the display's own units (from its middle)
		m_labelsText.ForceMeshUpdate();

		var middle = display.rect.center;
		var textBottom = display.InverseTransformPoint( m_labelsText.rectTransform.TransformPoint( new Vector3( 0.0f, m_labelsText.textBounds.min.y, 0.0f ) ) ).y - middle.y;
		var displayBottom = display.rect.yMin - middle.y;

		var side = Mathf.Min( c_artifactWindowLargest, Mathf.Min( display.rect.width * 0.6f, textBottom - displayBottom - c_artifactWindowMargin * 2.0f ) );
		var fits = side >= c_artifactWindowSmallest;

		side = Mathf.Max( side, c_artifactWindowSmallest );

		var rect = new Rect( -side * 0.5f, displayBottom + c_artifactWindowMargin, side, side );

		if ( m_artifactPreview == null )
		{
			m_artifactPreview = ArtifactPreview.Make( display, "Artifact Preview", rect, c_artifactWindowTextureSize, c_artifactWindowTextureSize );
		}
		else
		{
			m_artifactPreview.SetRect( rect );
		}

		return fits;
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
		var itemCount = ( ( terrainVehicle.m_elementStorage != null ) && ( terrainVehicle.m_elementStorage.m_elementList != null ) ? terrainVehicle.m_elementStorage.m_elementList.Count : 0 ) + ( ( terrainVehicle.m_artifactStorage != null ) && ( terrainVehicle.m_artifactStorage.m_artifactList != null ) ? terrainVehicle.m_artifactStorage.m_artifactList.Count : 0 ) + ( ( terrainVehicle.m_specimenStorage != null ) ? terrainVehicle.m_specimenStorage.GetCount() : 0 );

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
				var elementGameData = gameData.m_elementList[ elementRef.m_elementId ];
				labels += elementGameData.m_name + "\n";
				// (the cargo hold counts in tenths of a cubic meter) - and its value, what the Starport pays for a cubic meter of it, as the original's list
				// (starflt_190: MOLYBDENUM 9.6 160)
				values += Tools.VolumeToText( elementRef.m_volume ) + " m³  " + elementGameData.GetActualValue() + "\n";
			}
		}

		// list artifacts (with null checks)
		if ( artifactStorage != null && artifactStorage.m_artifactList != null && artifactStorage.m_artifactList.Count > 0 )
		{
			foreach ( var artifactRef in artifactStorage.m_artifactList )
			{
				var artifactGameData = gameData.m_artifactList[ artifactRef.m_artifactId ];
				labels += "<color=#00FFFF>" + artifactGameData.m_name + "</color>\n";
				// its volume and its value
				values += Tools.VolumeToText( artifactGameData.m_volume ) + " m³  " + artifactGameData.m_actualValue + "\n";
			}
		}

		// list the specimens in stasis
		var specimenStorage = playerData.m_terrainVehicle.GetSpecimenStorage();

		if ( specimenStorage.GetCount() > 0 )
		{
			foreach ( var specimen in specimenStorage.m_specimenList )
			{
				labels += "<color=#00FF00>" + specimen.m_name + "</color>\n";
				values += Tools.VolumeToText( specimen.m_volume ) + " m³  " + specimen.m_value + "\n";
			}
		}

		// show empty message if nothing (with null checks)
		int elementCount = ( elementStorage?.m_elementList?.Count ) ?? 0;
		int artifactCount = ( artifactStorage?.m_artifactList?.Count ) ?? 0;
		if ( elementCount == 0 && artifactCount == 0 && specimenStorage.GetCount() == 0 )
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

		// the artifact window goes under the new text
		m_artifactWindowPlaced = false;
	}
}
