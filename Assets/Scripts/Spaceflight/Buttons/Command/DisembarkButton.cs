
using UnityEngine;

public class DisembarkButton : ShipButton
{
	// the artifacts the terrain vehicle takes out of the ship's hold every time it goes out (the original's disembark, TV-OV WEE39): the Flat Device, whose shield is
	// around the terrain vehicle, and the Black Egg, which is armed by dropping it from the terrain vehicle's cargo; they come back on board with the rest of its cargo
	// (Planetside.TransferTerrainVehicleCargo)
	static readonly string[] c_artifactsTheTerrainVehicleTakes = { "Flat Device", "Black Egg" };

	// if this is true we are transitioning to disembarked
	bool m_isTransitioning;

	// what are we transitioning to?
	PD_General.Location m_nextLocation;

	// the message state
	int m_messageState;

	// the message timer
	float m_messageTimer;

	// get the label for this button
	public override string GetLabel()
	{
		return "Disembark";
	}

	public override bool Execute()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		switch ( playerData.m_general.m_location )
		{
			case PD_General.Location.DockingBay:

				// Debug.Log( "Player is disembarking - switching to starport." );

				// play the update sound
				SoundController.m_instance.PlaySound( SoundController.Sound.Update );

				// make sure we are standing in the middle of the transporter pad
				playerData.m_general.m_lastStarportCoordinates = new Vector3( 0.0f, 0.5f, 0.0f );

				// back towed or without the terrain vehicle: the crew is sent to Operations first (SHIPBUTTONS WF077, WF03F)
				if ( playerData.m_general.MustReportToOperations() )
				{
					SpaceflightController.m_instance.m_messages.Clear();
					SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Report to Operations for evaluation.</color>" );
				}

				// switch to the starport location
				SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Starport );

				return true;

			case PD_General.Location.JustLaunched:
			case PD_General.Location.InOrbit:
			case PD_General.Location.StarSystem:
			case PD_General.Location.Hyperspace:
			case PD_General.Location.Encounter:

				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_messages.Clear();

				SpaceflightController.m_instance.m_messages.AddText( "<color=white>We can't disembark in space!</color>" );

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				break;

			case PD_General.Location.Planetside:

				// a ship whose terrain vehicle has been lost has none until the Starport installs a new one
				if ( playerData.m_terrainVehicle.m_isLost )
				{
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );

					SpaceflightController.m_instance.m_messages.Clear();

					SpaceflightController.m_instance.m_messages.AddText( "<color=white>We have no terrain vehicle, Captain.\nThe Starport will install a new one.</color>" );

					SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

					break;
				}

				// the terrain vehicle has nothing to drive on if the maps of this planet could not be generated (a saved game can be on such a planet if its planet file was damaged later)
				var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

				if ( ( planetController == null ) || !planetController.HasMaps() )
				{
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );

					SpaceflightController.m_instance.m_messages.Clear();

					SpaceflightController.m_instance.m_messages.AddText( "<color=white>We can't disembark here.\nThe surface of this planet could not be mapped.</color>" );

					SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

					break;
				}

				// move the player to the arth ship coordinates on the surface
				playerData.m_general.m_lastDisembarkedCoordinates = Tools.LatLongToWorldCoordinates( playerData.m_general.m_selectedLatitude, playerData.m_general.m_selectedLongitude );

				// nudge the terrain vehicle to the south a bit
				playerData.m_general.m_lastDisembarkedCoordinates += Vector3.back * 16.0f;

				// update the terrain grid
				SpaceflightController.m_instance.m_disembarked.UpdateTerrainGridNow();

				// fade the map to black
				SpaceflightController.m_instance.m_viewport.StartFade( 0.0f, 2.0f );

				// we are now transitioning
				m_isTransitioning = true;

				// transition to the star system
				m_nextLocation = PD_General.Location.Disembarked;

				// display message
				SpaceflightController.m_instance.m_messages.Clear();

				// the shields go down and the weapons are disarmed before the terrain vehicle goes out, each saying so, as the original's (SHIPBUTTONS WF095 calls WEDEC,
				// which runs the shield and the arms overlays when they are up or armed); the shields keep their charge
				playerData.m_playerShip.DropShields();
				playerData.m_playerShip.DisarmWeapons();

				// the original's words (SHIPBUTTONS: "STANDBY...SCANNING PLANET.")
				SpaceflightController.m_instance.m_messages.AddText( "<color=white>Standby...scanning planet.</color>" );

				// refuel the terrain vehicle
				playerData.m_terrainVehicle.Refuel();

				// it takes the Black Egg and the Flat Device with it
				TakeArtifactsIntoTerrainVehicle( playerData );

				// reset the message state and timer
				m_messageState = 0;
				m_messageTimer = 0.0f;

				return true;
		}

		return false;
	}

	public override bool Update()
	{
		// are we currently transitioning?
		if ( m_isTransitioning )
		{
			m_messageTimer += Time.deltaTime;

			// (the four lines come within the map's fade of 2 seconds)
			if ( m_messageTimer >= 0.4f )
			{
				m_messageTimer -= 0.4f;

				switch ( m_messageState )
				{
					case 0:
						SpaceflightController.m_instance.m_messages.AddText( "<color=white>Auto sampling devices activated.</color>" );
						break;

					case 1:
						SpaceflightController.m_instance.m_messages.AddText( "<color=white>Initiating hull integrity check.</color>" );
						break;

					case 2:
						// the hull check ends before the terrain vehicle goes out, as the original's (HP-OV, C+OPARMS: "HULL CHECK COMPLETE")
						SpaceflightController.m_instance.m_messages.AddText( "<color=white>Hull check complete.</color>" );
						break;

					case 3:
						SpaceflightController.m_instance.m_messages.AddText( "<color=white>Activating terrain vehicle.</color>" );
						break;

				}

				m_messageState++;
			}

			// has the map stopped fading yet?
			if ( !SpaceflightController.m_instance.m_viewport.IsFading() )
			{
				// we are not transitioning any more
				m_isTransitioning = false;

				// switch to the next location
				SpaceflightController.m_instance.SwitchLocation( m_nextLocation );
			}
		}

		// always return true to prevent player from chagning buttons while transitioning to the docking bay
		return true;
	}

	// moves one of each of the artifacts the terrain vehicle takes from the ship's hold into the terrain vehicle's (as the original does, without a message and
	// whatever room is left in the terrain vehicle)
	static void TakeArtifactsIntoTerrainVehicle( PlayerData playerData )
	{
		var gameData = DataController.m_instance.m_gameData;

		foreach ( var artifactName in c_artifactsTheTerrainVehicleTakes )
		{
			var artifactId = gameData.FindArtifactId( artifactName );

			// only an artifact the ship has on board
			if ( ( artifactId >= 0 ) && playerData.m_playerShip.RemoveArtifact( artifactName ) )
			{
				playerData.m_terrainVehicle.AddArtifact( artifactId );

				Debug.Log( "The terrain vehicle takes the " + artifactName + " out of the ship's hold." );
			}
		}
	}
}
