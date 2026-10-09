
using UnityEngine;
using System.Collections.Generic;

public class TVCargoButton : ShipButton
{
	// state for pickup mode
	bool m_inPickupMode;

	public override string GetLabel()
	{
		return "Cargo";
	}

	public override bool Execute()
	{
		// get player data
		var playerData = DataController.m_instance.m_playerData;

		// is the terrain vehicle beside a ruin with messages in it that have not been recorded yet, or artifacts that have not been taken? (the manual, page 21:
		// cargo records the messages found in ruins, and picks up any item next to the terrain vehicle)
		var messagesRecorded = RecordMessagesInRuin();
		var artifactsFound = TakeArtifactsInRuin( !messagesRecorded );

		if ( messagesRecorded || artifactsFound )
		{
			return false;
		}

		// find all elements in pickup range
		var elementsInRange = FindElementsInRange();

		if ( elementsInRange.Count == 0 )
		{
			// check if there's a non-pickable object nearby and provide feedback
			var nearbyObjectName = FindNearbyNonPickableObject();

			if ( nearbyObjectName != null )
			{
				// there's something nearby but it can't be picked up
				SpaceflightController.m_instance.m_messages.Clear();
				SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>" + nearbyObjectName + " detected nearby.</color>\n<color=#808080>This object cannot be collected.</color>" );
				SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
				return false;
			}

			// no elements nearby - show cargo contents
			ShowCargoContents();
			return false;
		}

		// check if we have cargo space
		var remainingVolume = playerData.m_terrainVehicle.GetRemainingVolume();

		if ( remainingVolume <= 0 )
		{
			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Terrain vehicle cargo hold is full!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// pick up the closest element
		var closestElement = GetClosestElement( elementsInRange );

		if ( closestElement != null )
		{
			PickupElement( closestElement, remainingVolume );
		}

		return false;
	}

	// records the messages of the ruin the terrain vehicle is beside, if it has any that have not been recorded yet - returns true if it did
	bool RecordMessagesInRuin()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( ( terrainGrid == null ) || ( terrainGrid.m_terrainRuins == null ) || ( terrainVehicle == null ) )
		{
			return false;
		}

		var text = "";

		foreach ( Transform child in terrainGrid.m_terrainRuins.transform )
		{
			var ruin = child.GetComponent<TerrainRuin>();

			if ( ( ruin == null ) || !ruin.IsInReach( terrainVehicle.transform.position ) )
			{
				continue;
			}

			foreach ( var messageId in ruin.m_messageIds )
			{
				if ( ( messageId < 0 ) || ( messageId >= gameData.m_planetMessageList.Length ) || playerData.m_shipsLog.HasFoundMessage( messageId ) )
				{
					continue;
				}

				var planetMessage = gameData.m_planetMessageList[ messageId ];

				// the header names where it was found
				var header = string.IsNullOrEmpty( planetMessage.m_placeName ) ? ( "Planet " + planetMessage.m_planetFromSun + " of " + planetMessage.m_starX + ", " + planetMessage.m_starY ) : planetMessage.m_placeName;

				// dated by the day it was found
				playerData.m_shipsLog.AddFoundMessage( messageId, playerData.m_general.m_currentStardateDHMY, header, planetMessage.m_text );

				text += ( ( text == "" ) ? "" : "\n\n" ) + planetMessage.m_text;
			}
		}

		if ( text == "" )
		{
			return false;
		}

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Message recorded in the ruins:</color>\n<color=#0aa>" + text + "</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		return true;
	}

	// takes the artifacts of the ruin the terrain vehicle is beside that have not been taken yet, as far as there is room in its hold - returns true if the ruin had any
	bool TakeArtifactsInRuin( bool clearMessages )
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( ( terrainGrid == null ) || ( terrainGrid.m_terrainRuins == null ) || ( terrainVehicle == null ) || ( playerData.m_planetSurfaces == null ) )
		{
			return false;
		}

		var text = "";
		var anythingTaken = false;

		foreach ( Transform child in terrainGrid.m_terrainRuins.transform )
		{
			var ruin = child.GetComponent<TerrainRuin>();

			if ( ( ruin == null ) || !ruin.IsInReach( terrainVehicle.transform.position ) )
			{
				continue;
			}

			foreach ( var artifactSiteId in ruin.m_artifactSiteIds )
			{
				if ( ( artifactSiteId < 0 ) || ( artifactSiteId >= gameData.m_artifactSiteList.Length ) || playerData.m_planetSurfaces.IsArtifactSiteTaken( artifactSiteId ) )
				{
					continue;
				}

				var artifactId = gameData.m_artifactSiteList[ artifactSiteId ].m_artifactId;

				if ( ( artifactId < 0 ) || ( artifactId >= gameData.m_artifactList.Length ) )
				{
					continue;
				}

				var artifact = gameData.m_artifactList[ artifactId ];

				// is there room for it in the terrain vehicle? (artifact volumes are in tenths of a cubic meter, as the holds count)
				if ( artifact.m_volume > playerData.m_terrainVehicle.GetRemainingVolume() )
				{
					text += ( ( text == "" ) ? "" : "\n" ) + "<color=yellow>There is a " + artifact.m_name + " here (" + Tools.VolumeToText( artifact.m_volume ) + " cubic meters), but the cargo hold has no room for it.</color>";

					continue;
				}

				// yes - take it, and remember that it has been taken
				playerData.m_terrainVehicle.AddArtifact( artifactId );
				playerData.m_planetSurfaces.TakeArtifactSite( artifactSiteId );

				text += ( ( text == "" ) ? "" : "\n" ) + "<color=green>Picked up the " + artifact.m_name + " (" + Tools.VolumeToText( artifact.m_volume ) + " cubic meters).</color>";

				anythingTaken = true;
			}
		}

		if ( text == "" )
		{
			return false;
		}

		if ( clearMessages )
		{
			SpaceflightController.m_instance.m_messages.Clear();
		}

		SpaceflightController.m_instance.m_messages.AddText( text );

		SoundController.m_instance.PlaySound( anythingTaken ? SoundController.Sound.Transporter : SoundController.Sound.Error );

		// update the terrain vehicle display (the cargo it shows has changed)
		SpaceflightController.m_instance.m_displayController.m_terrainVehicleDisplay.Show();

		return true;
	}

	// detection range for non-pickable objects (slightly larger than pickup range)
	const float c_detectionDistance = 15.0f;

	// find all TerrainElement objects within pickup range
	List<TerrainElement> FindElementsInRange()
	{
		var elementsInRange = new List<TerrainElement>();

		// get the terrain elements container
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;

		if ( terrainGrid == null || terrainGrid.m_terrainElements == null )
		{
			return elementsInRange;
		}

		// search all children of the terrain elements container
		var elementsContainer = terrainGrid.m_terrainElements.gameObject;

		foreach ( Transform child in elementsContainer.transform )
		{
			var terrainElement = child.GetComponent<TerrainElement>();

			if ( terrainElement != null && terrainElement.IsInPickupRange() )
			{
				elementsInRange.Add( terrainElement );
			}
		}

		return elementsInRange;
	}

	// find the closest non-pickable object (rock or tree) within detection range
	string FindNearbyNonPickableObject()
	{
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( terrainGrid == null || terrainVehicle == null )
		{
			return null;
		}

		float closestDistance = float.MaxValue;
		string closestObjectName = null;

		// check rocks
		if ( terrainGrid.m_terrainRocks != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainRocks.transform )
			{
				var distance = Vector3.Distance( child.position, terrainVehicle.transform.position );

				if ( distance <= c_detectionDistance && distance < closestDistance )
				{
					closestDistance = distance;
					closestObjectName = "Rock";
				}
			}
		}

		// check trees
		if ( terrainGrid.m_terrainTrees != null )
		{
			foreach ( Transform child in terrainGrid.m_terrainTrees.transform )
			{
				var distance = Vector3.Distance( child.position, terrainVehicle.transform.position );

				if ( distance <= c_detectionDistance && distance < closestDistance )
				{
					closestDistance = distance;
					closestObjectName = "Vegetation";
				}
			}
		}

		return closestObjectName;
	}

	// get the closest element from a list
	TerrainElement GetClosestElement( List<TerrainElement> elements )
	{
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;
		TerrainElement closestElement = null;
		float closestDistance = float.MaxValue;

		foreach ( var element in elements )
		{
			var distance = Vector3.Distance( element.transform.position, terrainVehicle.transform.position );

			if ( distance < closestDistance )
			{
				closestDistance = distance;
				closestElement = element;
			}
		}

		return closestElement;
	}

	// pick up an element
	void PickupElement( TerrainElement element, int remainingVolume )
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		// get the element info
		var elementId = element.m_elementId;
		var elementName = element.GetElementName();
		var volumeAvailable = element.m_volume;

		// clamp to available cargo space
		var volumeToPickup = Mathf.Min( volumeAvailable, remainingVolume );

		// DEBUG: Log pickup attempt
		Debug.Log($"[PickupElement] Attempting pickup: elementId={elementId}, name={elementName}, volumeAvailable={volumeAvailable}, volumeToPickup={volumeToPickup}, remainingCargo={remainingVolume}");

		// add to terrain vehicle cargo
		playerData.m_terrainVehicle.AddElement( elementId, volumeToPickup );

		// DEBUG: Log cargo state after pickup
		var cargoList = playerData.m_terrainVehicle.m_elementStorage?.m_elementList;
		if (cargoList != null)
		{
			foreach (var elem in cargoList)
			{
				Debug.Log($"[PickupElement] Cargo: elementId={elem.m_elementId}, volume={elem.m_volume}");
			}
		}

		// the volumes are in tenths of a cubic meter, as the cargo holds count them
		var pickupText = "<color=green>Picked up " + Tools.VolumeToText( volumeToPickup ) + " cubic meters of " + elementName + ".</color>";

		// did all of the deposit fit into the cargo hold?
		if ( volumeToPickup < volumeAvailable )
		{
			// no - the rest of the deposit stays where it is
			element.m_volume = volumeAvailable - volumeToPickup;

			pickupText += "\n<color=yellow>The cargo hold is full. " + Tools.VolumeToText( element.m_volume ) + " cubic meters are left behind.</color>";
		}
		else
		{
			// yes - remove the element from the planet
			element.Pickup();

			element.m_volume = 0;
		}

		// remember what is left of the deposit, so that it is not back the next time the terrain vehicle goes out on this planet
		if ( playerData.m_planetSurfaces != null )
		{
			playerData.m_planetSurfaces.SetDepositVolumeLeft( element.m_planetId, element.m_depositIndex, element.m_volume );
		}

		// show pickup message
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( pickupText );

		// play transporter sound for cargo pickup
		SoundController.m_instance.PlaySound( SoundController.Sound.Transporter );

		// update button sprites
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		// update the terrain vehicle display
		SpaceflightController.m_instance.m_displayController.m_terrainVehicleDisplay.Show();
	}

	// show the current cargo contents
	void ShowCargoContents()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		SpaceflightController.m_instance.m_messages.Clear();

		var elementStorage = playerData.m_terrainVehicle.m_elementStorage;
		var artifactStorage = playerData.m_terrainVehicle.m_artifactStorage;

		// null-safe count checks
		int elementCount = ( elementStorage?.m_elementList?.Count ) ?? 0;
		int artifactCount = ( artifactStorage?.m_artifactList?.Count ) ?? 0;

		if ( elementCount == 0 && artifactCount == 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Terrain Vehicle Cargo:</color>\n<color=white>Empty</color>" );
		}
		else
		{
			var text = "<color=yellow>Terrain Vehicle Cargo:</color>\n";

			// list elements
			if ( elementStorage != null && elementStorage.m_elementList != null )
			{
				foreach ( var elementRef in elementStorage.m_elementList )
				{
					var elementName = gameData.m_elementList[ elementRef.m_elementId ].m_name;
					text += "<color=white>" + elementName + ": " + Tools.VolumeToText( elementRef.m_volume ) + " m³</color>\n";
				}
			}

			// list artifacts
			if ( artifactStorage != null && artifactStorage.m_artifactList != null )
			{
				foreach ( var artifactRef in artifactStorage.m_artifactList )
				{
					var artifactName = gameData.m_artifactList[ artifactRef.m_artifactId ].m_name;
					text += "<color=cyan>" + artifactName + "</color>\n";
				}
			}

			// show remaining capacity
			var remaining = playerData.m_terrainVehicle.GetRemainingVolume();
			var total = gameData.m_misc.m_terrainVehicleVolume;
			text += "\n<color=#808080>Capacity: " + Tools.VolumeToText( total - remaining ) + "/" + Tools.VolumeToText( total ) + " m³</color>";

			SpaceflightController.m_instance.m_messages.AddText( text );
		}

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
	}

	public override bool Update()
	{
		// check if we want to turn off the map
		if ( InputController.m_instance.m_submit )
		{
			// debounce the input
			InputController.m_instance.Debounce();

			// deactivate the current button
			SpaceflightController.m_instance.m_buttonController.DeactivateButton();

			// play the deactivate sound
			SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
		}
		else
		{
		}

		// returning true prevents the default spaceflight update from running
		return true;
	}
}
