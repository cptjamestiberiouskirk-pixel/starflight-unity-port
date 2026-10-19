using System;

public class ShipCargoButton : ShipButton
{
	// what can be marked in the list: an element, an artifact or a specimen of the ship's hold, by its index in its storage
	public enum ItemKind
	{
		Element,
		Artifact,
		Specimen
	};

	public struct CargoItem
	{
		public ItemKind m_kind;
		public int m_index;
	}

	// the marked item of the list (an index into BuildCargoItems), and the console's buttons to go back to (the command set the list was opened from)
	static int s_markedItem;
	static ButtonController.ButtonSet s_returnSet = ButtonController.ButtonSet.CommandA;

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

			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

			return false;
		}

		// the list, with its first item marked, and the console's buttons for it (the original's (/ITEMS): the cursor on an item, its options DESCRIBE and JETTISON)
		s_markedItem = 0;
		s_returnSet = SpaceflightController.m_instance.m_buttonController.GetCurrentButtonSet();

		ShowCargoList();

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.ShipCargo );

		return false;
	}

	// shows the list of the ship's hold with the marked item
	public static void ShowCargoList()
	{
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.RenderTable( GetCargoDataTable(), new int[] { 55, 25, 20 }, new TMPro.TextAlignmentOptions[] { TMPro.TextAlignmentOptions.TopLeft, TMPro.TextAlignmentOptions.TopRight, TMPro.TextAlignmentOptions.TopRight } );
	}

	// marks the next item of the list
	public static void MarkNextItem()
	{
		var count = BuildCargoItems().Count;

		s_markedItem = ( count == 0 ) ? 0 : ( s_markedItem + 1 ) % count;

		ShowCargoList();
	}

	// back to the command buttons the list was opened from
	public static void CloseCargoList()
	{
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( s_returnSet );
	}

	// describes the marked item, as the original's DESCRIBE (CAPTAIN 1STOPTION): an artifact by its Starport analysis, or that it has not been analysed (WEA62); a
	// specimen by where and when it was captured (WEC73) - returns false for an element, which has no description (the original offers it only JETTISON)
	public static bool DescribeMarkedItem()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var playerShip = playerData.m_playerShip;
		var items = BuildCargoItems();

		if ( items.Count == 0 )
		{
			return false;
		}

		var item = items[ UnityEngine.Mathf.Clamp( s_markedItem, 0, items.Count - 1 ) ];
		string text;

		switch ( item.m_kind )
		{
			case ItemKind.Artifact:
			{
				var artifactId = playerShip.m_artifactStorage.m_artifactList[ item.m_index ].m_artifactId;

				if ( ( artifactId < 0 ) || ( artifactId >= gameData.m_artifactList.Length ) )
				{
					return false;
				}

				var artifact = gameData.m_artifactList[ artifactId ];
				var isKnown = ( playerData.m_knownArtifacts != null ) && playerData.m_knownArtifacts.IsKnown( artifactId );

				text = "<color=yellow>" + artifact.m_name + "</color>\n" + ( ( isKnown && !string.IsNullOrEmpty( artifact.m_analysisText ) ) ? "<color=white>" + artifact.m_analysisText + "</color>" : "<color=white>This artifact has not been analyzed.</color>" );

				break;
			}

			case ItemKind.Specimen:
			{
				var specimen = playerShip.GetSpecimenStorage().m_specimenList[ item.m_index ];

				text = "<color=yellow>" + specimen.m_name + "</color>";

				if ( ( specimen.m_planetId >= 0 ) && ( specimen.m_planetId < gameData.m_planetList.Length ) )
				{
					var planet = gameData.m_planetList[ specimen.m_planetId ];
					var star = gameData.m_starList[ planet.m_starId ];

					text += "\n<color=white>System " + star.m_xCoordinate + "," + star.m_yCoordinate + "\nPlanet " + planet.m_orbitPosition + "</color>";
				}

				if ( specimen.m_hasCapturePlace )
				{
					text += "\n<color=white>Location " + Math.Abs( specimen.m_capturedNorthSouth ) + ( ( specimen.m_capturedNorthSouth < 0 ) ? "S" : "N" ) + " x " + Math.Abs( specimen.m_capturedEastWest ) + ( ( specimen.m_capturedEastWest < 0 ) ? "W" : "E" ) + "\nDate " + PD_General.GetDisplayStardate( specimen.m_capturedStardateYMD ) + "</color>";
				}

				break;
			}

			default:
				return false;
		}

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( text );

		return true;
	}

	// true if the marked item is an element, with its volume in tenths of a cubic meter (the original asks how much of an element to jettison)
	public static bool MarkedItemIsElement( out int volume )
	{
		var items = BuildCargoItems();

		volume = 0;

		if ( items.Count == 0 )
		{
			return false;
		}

		var item = items[ UnityEngine.Mathf.Clamp( s_markedItem, 0, items.Count - 1 ) ];

		if ( item.m_kind != ItemKind.Element )
		{
			return false;
		}

		volume = DataController.m_instance.m_playerData.m_playerShip.m_elementStorage.m_elementList[ item.m_index ].m_volume;

		return true;
	}

	// jettisons the marked item, as the original's JETTISON (CAPTAIN WECDF: TOSS, the Trade Depot's SELL-IT with no depot, so nothing is paid): an artifact or a
	// specimen goes whole, an element by this volume in tenths of a cubic meter (0 jettisons nothing); then the list again, or back to the command buttons with
	// the hold empty - returns false if nothing was jettisoned
	public static bool JettisonMarkedItem( int elementVolume )
	{
		var playerShip = DataController.m_instance.m_playerData.m_playerShip;
		var items = BuildCargoItems();

		if ( items.Count == 0 )
		{
			return false;
		}

		var item = items[ UnityEngine.Mathf.Clamp( s_markedItem, 0, items.Count - 1 ) ];

		switch ( item.m_kind )
		{
			case ItemKind.Element:
			{
				var elementReference = playerShip.m_elementStorage.m_elementList[ item.m_index ];
				var volume = UnityEngine.Mathf.Clamp( elementVolume, 0, elementReference.m_volume );

				if ( volume <= 0 )
				{
					ShowCargoList();

					return false;
				}

				playerShip.RemoveElement( elementReference.m_elementId, volume );

				break;
			}

			case ItemKind.Artifact:
				playerShip.RemoveArtifact( playerShip.m_artifactStorage.m_artifactList[ item.m_index ].m_artifactId );
				break;

			case ItemKind.Specimen:
				playerShip.RemoveSpecimen( playerShip.GetSpecimenStorage().m_specimenList[ item.m_index ] );
				break;
		}

		// the hold empty: back to the command buttons, as the original's list ends
		if ( IsHoldEmpty() )
		{
			CloseCargoList();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Starship hold is empty.</color>" );

			return true;
		}

		s_markedItem = UnityEngine.Mathf.Clamp( s_markedItem, 0, BuildCargoItems().Count - 1 );

		ShowCargoList();

		return true;
	}

	// everything in the ship's hold that can be marked, in the order of the list (each kind's items the one put in last first)
	public static System.Collections.Generic.List<CargoItem> BuildCargoItems()
	{
		var playerShip = DataController.m_instance.m_playerData.m_playerShip;
		var items = new System.Collections.Generic.List<CargoItem>();

		if ( ( playerShip.m_elementStorage != null ) && ( playerShip.m_elementStorage.m_elementList != null ) )
		{
			for ( var i = playerShip.m_elementStorage.m_elementList.Count - 1; i >= 0; i-- )
			{
				items.Add( new CargoItem { m_kind = ItemKind.Element, m_index = i } );
			}
		}

		if ( ( playerShip.m_artifactStorage != null ) && ( playerShip.m_artifactStorage.m_artifactList != null ) )
		{
			for ( var i = playerShip.m_artifactStorage.m_artifactList.Count - 1; i >= 0; i-- )
			{
				items.Add( new CargoItem { m_kind = ItemKind.Artifact, m_index = i } );
			}
		}

		var specimenStorage = playerShip.GetSpecimenStorage();

		for ( var i = specimenStorage.m_specimenList.Count - 1; i >= 0; i-- )
		{
			items.Add( new CargoItem { m_kind = ItemKind.Specimen, m_index = i } );
		}

		return items;
	}

	// true if the ship's hold has no elements, no artifacts and no specimens
	static bool IsHoldEmpty()
	{
		var playerShip = DataController.m_instance.m_playerData.m_playerShip;

		var elementCount = ( ( playerShip.m_elementStorage != null ) && ( playerShip.m_elementStorage.m_elementList != null ) ) ? playerShip.m_elementStorage.m_elementList.Count : 0;
		var artifactCount = ( ( playerShip.m_artifactStorage != null ) && ( playerShip.m_artifactStorage.m_artifactList != null ) ) ? playerShip.m_artifactStorage.m_artifactList.Count : 0;

		return ( elementCount == 0 ) && ( artifactCount == 0 ) && ( playerShip.GetSpecimenStorage().GetCount() == 0 );
	}

	// the list of everything in the ship's hold, as the original's (IT-OV BOX>LIST): the hold keeps each kind of item in a box of its own (IT-OV >BOX), and the
	// list shows each box's name with its items under it, the item put in last first (IINSERT puts it at the front of its box); a box exists only while it has
	// something in it, so a kind the hold does not have is not listed
	static string[] GetCargoDataTable()
	{
		var playerData = DataController.m_instance.m_playerData;
		var playerShip = playerData.m_playerShip;

		// the items are counted in the order of BuildCargoItems, so the marked one can be shown
		var markedItem = s_markedItem;
		var itemNumber = 0;

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

				AddRow( columns, ItemName( elementReference.GetElementGameData().m_name, itemNumber++ == markedItem ), Tools.VolumeToText( elementReference.GetVolume() ), elementReference.GetElementGameData().GetActualValue().ToString() );
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

				AddRow( columns, ItemName( artifactGameData.m_name, itemNumber++ == markedItem ), Tools.VolumeToText( artifactGameData.m_volume ), isKnown ? artifactGameData.m_actualValue.ToString() : "???" );
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

				AddRow( columns, ItemName( specimen.m_name, itemNumber++ == markedItem ), Tools.VolumeToText( specimen.m_volume ), specimen.GetSaleValue().ToString() );
			}
		}

		return columns;
	}

	// an item's name in the list, marked if it is the one the list's buttons act on
	static string ItemName( string name, bool isMarked )
	{
		return isMarked ? "<color=white> > " + name + "</color>" : "   " + name;
	}

	// adds a line to the three columns of the list
	static void AddRow( string[] columns, string item, string volume, string value )
	{
		columns[ 0 ] += item + "\n";
		columns[ 1 ] += volume + "\n";
		columns[ 2 ] += value + "\n";
	}
}
