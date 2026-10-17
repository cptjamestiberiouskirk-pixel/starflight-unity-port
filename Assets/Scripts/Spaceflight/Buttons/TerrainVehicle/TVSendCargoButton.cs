using UnityEngine;

// the terrain vehicle's Send button in its cargo list (since 2026-10-17): the ship's transporter takes what the vehicle carries into the ship's hold from a distance,
// within 20 km of the ship, as far as the ship has room, so the crew can go on exploring with an empty hold. A crate of the cargo shimmers away on the vehicle
public class TVSendCargoButton : ShipButton
{
	// how far from the ship the transporter reaches, in kilometers
	public const float c_sendRangeKm = 20.0f;

	// how big the crate that beams away is, and how high above the vehicle's middle it shows
	const float c_crateSize = 2.5f;
	const float c_crateHeight = 3.0f;

	public override string GetLabel()
	{
		return "Send";
	}

	public override bool Execute()
	{
		var playerData = DataController.m_instance.m_playerData;
		var controller = SpaceflightController.m_instance;
		var messages = controller.m_messages;
		var distanceInKm = MoveButton.GetKmToShip();

		if ( distanceInKm > c_sendRangeKm )
		{
			messages.Clear();
			messages.AddText( "<color=red>The ship is " + Mathf.RoundToInt( distanceInKm ) + " km away, beyond the transporter's " + Mathf.RoundToInt( c_sendRangeKm ) + " km.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			controller.m_buttonController.UpdateButtonSprites();

			return false;
		}

		var moved = controller.m_planetside.MoveCargoToTheShip();

		if ( moved <= 0 )
		{
			messages.Clear();
			messages.AddText( "<color=red>The ship's cargo hold has no room for it.</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			controller.m_buttonController.UpdateButtonSprites();

			return false;
		}

		SoundController.m_instance.PlaySound( SoundController.Sound.Transporter );

		BeamACrateAway();

		var left = playerData.m_terrainVehicle.m_volumeUsed;
		var text = "<color=green>" + Tools.VolumeToText( moved ) + " cubic meters of cargo transported to the ship.</color>";

		if ( left > 0 )
		{
			// the rest did not fit: the list stays open with what is left
			text += "\n<color=yellow>" + Tools.VolumeToText( left ) + " cubic meters did not fit in the ship's hold.</color>";

			TVCargoButton.OpenCargoList();
		}
		else
		{
			TVCargoButton.BackToTerrainVehicle();
		}

		messages.Clear();
		messages.AddText( text );

		controller.m_displayController.m_terrainVehicleDisplay.Show();

		return false;
	}

	// a crate of the cargo on the vehicle, which the transporter effect takes away (no collider, so it does not push the vehicle)
	static void BeamACrateAway()
	{
		var vehicle = SpaceflightController.m_instance.m_terrainVehicle;
		var crate = GameObject.CreatePrimitive( PrimitiveType.Cube );

		crate.name = "Transported Cargo";

		if ( crate.TryGetComponent<Collider>( out var collider ) )
		{
			Object.Destroy( collider );
		}

		crate.transform.SetParent( vehicle.transform.parent, false );
		crate.transform.position = vehicle.transform.position + Vector3.up * c_crateHeight;
		crate.transform.rotation = vehicle.transform.rotation;
		crate.transform.localScale = Vector3.one * c_crateSize;

		// in the vehicle's own colours
		var vehicleRenderer = vehicle.GetComponentInChildren<Renderer>();

		if ( ( vehicleRenderer != null ) && crate.TryGetComponent<Renderer>( out var crateRenderer ) )
		{
			crateRenderer.sharedMaterial = vehicleRenderer.sharedMaterial;
		}

		crate.AddComponent<TransporterEffect>().Play();
	}
}
