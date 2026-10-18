using System;

public class ShipCargoButton : ShipButton
{
	public override string GetLabel()
	{
		return "Cargo";
	}

	public override bool Execute()
	{
		SoundController.m_instance.PlaySound( SoundController.Sound.Error );

		SpaceflightController.m_instance.m_messages.Clear();

		// an empty hold gets a message and no list, as the original's (CAPTAIN (/ITEMS) WEEF2)
		if ( IsHoldEmpty() )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Starship hold is empty.</color>" );
		}
		else
		{
			SpaceflightController.m_instance.m_messages.RenderTable(GetCargoDataTable(), new int[] { 55, 25, 20}, new TMPro.TextAlignmentOptions[] { TMPro.TextAlignmentOptions.TopLeft, TMPro.TextAlignmentOptions.TopRight, TMPro.TextAlignmentOptions.TopRight});
		}

		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}

	// true if the ship's hold has no elements, no artifacts and no specimens
	bool IsHoldEmpty()
	{
		var playerShip = DataController.m_instance.m_playerData.m_playerShip;

		var elementCount = ( ( playerShip.m_elementStorage != null ) && ( playerShip.m_elementStorage.m_elementList != null ) ) ? playerShip.m_elementStorage.m_elementList.Count : 0;
		var artifactCount = ( ( playerShip.m_artifactStorage != null ) && ( playerShip.m_artifactStorage.m_artifactList != null ) ) ? playerShip.m_artifactStorage.m_artifactList.Count : 0;

		return ( elementCount == 0 ) && ( artifactCount == 0 ) && ( playerShip.GetSpecimenStorage().GetCount() == 0 );
	}

	// the list of everything in the ship's hold, as the original's (IT-OV BOX>LIST): the hold keeps each kind of item in a box of its own (IT-OV >BOX), and the
	// list shows each box's name with its items under it, the item put in last first (IINSERT puts it at the front of its box); a box exists only while it has
	// something in it, so a kind the hold does not have is not listed
	private string[] GetCargoDataTable()
	{
		var playerData = DataController.m_instance.m_playerData;
		var playerShip = playerData.m_playerShip;

		// the headings of the three columns
		var columns = new string[] { "ITEM\n", "VOLUME\n", "VALUE\n" };

		// the elements, each at what the Trade Depot pays for a cubic meter of it (IT-OV WEFE9, the value shown at the Sell list's %VAL of 50)
		var elementStorage = playerShip.m_elementStorage;

		if ( ( elementStorage != null ) && ( elementStorage.m_elementList != null ) && ( elementStorage.m_elementList.Count > 0 ) )
		{
			AddRow( columns, "ELEMENTS", "", "" );

			for ( var i = elementStorage.m_elementList.Count - 1; i >= 0; i-- )
			{
				var elementReference = elementStorage.m_elementList[ i ];

				AddRow( columns, "   " + elementReference.GetElementGameData().m_name, Tools.VolumeToText( elementReference.GetVolume() ), elementReference.GetElementGameData().GetActualValue().ToString() );
			}
		}

		// the artifacts, each at what the Trade Depot pays for it, or "???" for one of a kind that has not been analysed (IT-OV WF096 with ???IT off, as the
		// Trade Depot's Sell list leaves it)
		var artifactStorage = playerShip.m_artifactStorage;

		if ( ( artifactStorage != null ) && ( artifactStorage.m_artifactList != null ) && ( artifactStorage.m_artifactList.Count > 0 ) )
		{
			AddRow( columns, "ARTIFACTS", "", "" );

			for ( var i = artifactStorage.m_artifactList.Count - 1; i >= 0; i-- )
			{
				var artifactReference = artifactStorage.m_artifactList[ i ];
				var artifactGameData = artifactReference.GetArtifactGameData();

				// do we know what this artifact is?
				var isKnown = ( playerData.m_knownArtifacts != null ) && playerData.m_knownArtifacts.IsKnown( artifactReference.m_artifactId );

				AddRow( columns, "   " + artifactGameData.m_name, Tools.VolumeToText( artifactGameData.m_volume ), isKnown ? artifactGameData.m_actualValue.ToString() : "???" );
			}
		}

		// the specimens, each at what the Trade Depot pays for it (IT-OV WF0C6)
		var specimenStorage = playerShip.GetSpecimenStorage();

		if ( specimenStorage.GetCount() > 0 )
		{
			AddRow( columns, "SPECIMENS", "", "" );

			for ( var i = specimenStorage.m_specimenList.Count - 1; i >= 0; i-- )
			{
				var specimen = specimenStorage.m_specimenList[ i ];

				AddRow( columns, "   " + specimen.m_name, Tools.VolumeToText( specimen.m_volume ), specimen.GetSaleValue().ToString() );
			}
		}

		return columns;
	}

	// adds a line to the three columns of the list
	static void AddRow( string[] columns, string item, string volume, string value )
	{
		columns[ 0 ] += item + "\n";
		columns[ 1 ] += volume + "\n";
		columns[ 2 ] += value + "\n";
	}
}
