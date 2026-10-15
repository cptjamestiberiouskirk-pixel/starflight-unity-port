
using UnityEngine;

public class MoveButton : ShipButton
{
	// how near the ship (in kilometers along the ground) Move must stop for the vehicle to go back in: on the ship's own map cell, as the original's (a move is 1 km)
	const float c_boardingDistanceKm = 0.5f;

	// if this is true we are transitioning to disembarked
	bool m_isTransitioning;

	// this is set to true if we are out of fuel
	bool m_outOfFuel;

	// what are we transitioning to?
	PD_General.Location m_nextLocation;

	public override string GetLabel()
	{
		return "Move";
	}

	public override bool Execute()
	{
		// reset the out of fuel warning
		m_outOfFuel = false;

		// start playing the diesel engine sound
		SoundController.m_instance.PlaySound( SoundController.Sound.DieselEngine, 0.5f, 1.0f, true );

		// return true to keep the button lit and active
		return true;
	}

	public override bool Update()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// are we currently transitioning?
		if ( m_isTransitioning )
		{
			// has the map stopped fading yet?
			if ( !SpaceflightController.m_instance.m_viewport.IsFading() )
			{
				// we are not transitioning any more
				m_isTransitioning = false;

				// switch to the next location
				SpaceflightController.m_instance.SwitchLocation( m_nextLocation );

				// turn off the maneuver function
				SpaceflightController.m_instance.m_buttonController.ClearCurrentButton();
			}

			return true;
		}

		// check if we want to stop moving
		if ( InputController.m_instance.m_submit )
		{
			// debounce the input
			InputController.m_instance.Debounce();

			// turn off the engines
			SpaceflightController.m_instance.m_terrainVehicle.TurnOffEngines();

			// stop playing the diesel engine sound
			SoundController.m_instance.StopSound( SoundController.Sound.DieselEngine );

			// calculate ship coordinates
			var shipCoordinates = Tools.LatLongToWorldCoordinates( playerData.m_general.m_selectedLatitude, playerData.m_general.m_selectedLongitude );

			shipCoordinates = SpaceflightController.m_instance.m_disembarked.ApplyElevation( shipCoordinates, false );

			// calculate vector from TV to ship coordinates, along the ground: the vehicle's saved place is flat (the ground's height is only drawn) while the
			// ship's has the ground's height, so measured with the height a ship on raised ground was never near enough to go back in
			var vectorToShip = shipCoordinates - playerData.m_general.m_lastDisembarkedCoordinates;

			vectorToShip.y = 0.0f;

			// how far is it in kilometers?
			var distanceInKm = vectorToShip.magnitude * 225.0f / 2048.0f;

			// is the vehicle on the ship's map cell? the original takes it in only there (BEHAV-OV ?EMBARK: the vehicle's grid position equal to the ship's, the
			// console's "0 KM."), and a move is 1 km, so within half a move
			if ( distanceInKm <= c_boardingDistanceKm )
			{
				// yes - make sure the planetside terrain grid has been updated
				SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();

				// fade the map to black
				SpaceflightController.m_instance.m_viewport.StartFade( 0.0f, 2.0f );

				// we are now transitioning
				m_isTransitioning = true;

				// play the activate sound
				SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

				// transition to planetside
				m_nextLocation = PD_General.Location.Planetside;

				// display message
				SpaceflightController.m_instance.m_messages.Clear();
				SpaceflightController.m_instance.m_messages.AddText( "<color=white>Refueling terrain vehicle and transferring all cargo...</color>" );
			}

			if ( m_isTransitioning )
			{
				// remove the "active" dot from the current button
				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				// play the deactivate sound
				SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
			}
			else
			{
				// deactivate the current button
				SpaceflightController.m_instance.m_buttonController.DeactivateButton();
			}
		}
		else
		{
			// check if we are out of fuel
			if ( playerData.m_terrainVehicle.IsOutOfFuel() )
			{
				if ( !m_outOfFuel )
				{
					// remember we already did this
					m_outOfFuel = true;

					// turn the engines off
					SpaceflightController.m_instance.m_terrainVehicle.TurnOffEngines();

					// play the error sound
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );

					// stop playing the diesel engine sound
					SoundController.m_instance.StopSound( SoundController.Sound.DieselEngine );

					// the original's (MOVE-OV): the crew leave the vehicle's cargo where it stopped and walk back to the ship, where the vehicle is
					// refuelled - so no crew is stranded on a planet with an empty tank
					var dropped = TVCargoButton.DropAllCargo();

					SpaceflightController.m_instance.m_messages.Clear();
					SpaceflightController.m_instance.m_messages.AddText( "<color=red>Out of fuel.</color>\n<color=white>Returning to ship on foot.</color>" + ( ( dropped > 0 ) ? "\n<color=yellow>The cargo is left where the vehicle stopped.</color>" : "" ) );

					// the walk back: the map fades, and the crew are in the ship
					SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();
					SpaceflightController.m_instance.m_viewport.StartFade( 0.0f, 2.0f );

					m_nextLocation = PD_General.Location.Planetside;
					m_isTransitioning = true;

					// remove the "active" dot from the current button
					SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

					// play the deactivate sound
					SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
				}
			}
			else
			{
				// get the controller stick position
				var x = InputController.m_instance.m_x;
				var z = InputController.m_instance.m_y;

				// create our 3d move vector from the controller position
				var moveVector = new Vector3( x, 0.0f, z );

				// check if the move vector will actually move the ship (that the controller is not centered)
				if ( moveVector.magnitude > 0.5f )
				{
					// normalize the move vector to a length of 1.0
					moveVector.Normalize();

					// update the direction
					playerData.m_general.m_currentDirection = Vector3.Slerp( playerData.m_general.m_currentDirection, moveVector, Time.deltaTime * 2.0f );

					// turn the engines on
					SpaceflightController.m_instance.m_terrainVehicle.TurnOnEngines();
				}
				else
				{
					// turn the engines off
					SpaceflightController.m_instance.m_terrainVehicle.TurnOffEngines();
				}
			}
		}

		// returning true prevents the default spaceflight update from running
		return true;
	}
}
