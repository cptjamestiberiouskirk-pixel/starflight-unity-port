
using UnityEngine;

public class MoveButton : ShipButton
{
	// how near the ship (in kilometers along the ground) Move must stop for the vehicle to go back in. The original takes it in only on the ship's own map cell
	// (BEHAV-OV ?EMBARK: the vehicle's grid position equal to the ship's, the console's "0 KM."), within half a move of 1 km, and so does a stop there here; the
	// port's vehicle drives smoothly rather than a cell at a time, so that spot was hard to stop on, and since 2026-10-17 a vehicle coming back that stops
	// anywhere within 3 km of the ship is asked whether to go back in (TVBoardYesButton, TVBoardNoButton). It is asked, not taken in, as a stop to mine or look
	// near the ship must not end the trip; and only once it has been farther away than that, as the vehicle disembarks 1.8 km behind the ship
	public const float c_boardingDistanceKm = 3.0f;
	public const float c_shipCellKm = 0.5f;

	// if this is true we are transitioning to disembarked
	protected bool m_isTransitioning;

	// this is set to true if we are out of fuel
	bool m_outOfFuel;

	// what are we transitioning to?
	protected PD_General.Location m_nextLocation;

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

			// how far is the vehicle from the ship?
			var distanceInKm = GetKmToShip();

			// on the ship's own cell the vehicle goes back in at once, as the original's
			if ( distanceInKm <= c_shipCellKm )
			{
				StartBoarding();

				// we are now transitioning to planetside
				m_isTransitioning = true;
				m_nextLocation = PD_General.Location.Planetside;
			}
			else if ( SpaceflightController.m_instance.m_disembarked.m_hasBeenAway && ( distanceInKm <= c_boardingDistanceKm ) )
			{
				// within 3 km on the way back the crew are asked first (a stop to mine or look near the ship must not end the trip)
				var buttonController = SpaceflightController.m_instance.m_buttonController;

				buttonController.DeactivateButton();
				buttonController.ChangeButtonSet( ButtonController.ButtonSet.TerrainVehicleBoard );

				SpaceflightController.m_instance.m_messages.Clear();
				SpaceflightController.m_instance.m_messages.AddText( "<color=white>The ship is " + Mathf.Max( 1, Mathf.RoundToInt( distanceInKm ) ) + " km away. Return to the ship?</color>" );

				return true;
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

					// the walk is a map cell at a time, and each cell has a chance in five of injuring one of the crew, by the ground walked over (MOVE-OV WF16E
					// walks the cells to the ship and WF122 rolls RRND(0,5) for 'INJURE at each)
					WalkBackToTheShip();

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
				// once the vehicle has been farther from the ship than the boarding distance, a stop within it on the way back takes the vehicle in
				var disembarked = SpaceflightController.m_instance.m_disembarked;

				if ( !disembarked.m_hasBeenAway && ( GetKmToShip() > c_boardingDistanceKm ) )
				{
					disembarked.m_hasBeenAway = true;
				}

				// where to steer: the stick for Move, the ship for Return (TVReturnButton) - nowhere when the stick is centered
				if ( GetSteering( out var moveVector ) )
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

	// where the vehicle steers while the button runs: Move goes where the stick points, and only while it is pushed
	protected virtual bool GetSteering( out Vector3 moveVector )
	{
		// get the controller stick position
		var x = InputController.m_instance.m_x;
		var z = InputController.m_instance.m_y;

		// create our 3d move vector from the controller position
		moveVector = new Vector3( x, 0.0f, z );

		// check if the move vector will actually move the vehicle (that the controller is not centered)
		return moveVector.magnitude > 0.5f;
	}

	// the vehicle has reached the ship's own cell while the button runs: it stops and goes aboard, as Move stopped there takes it in
	protected void GoAboard()
	{
		SpaceflightController.m_instance.m_terrainVehicle.TurnOffEngines();
		SoundController.m_instance.StopSound( SoundController.Sound.DieselEngine );

		StartBoarding();

		m_isTransitioning = true;
		m_nextLocation = PD_General.Location.Planetside;

		// remove the "active" dot from the current button
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
	}

	// the terrain vehicle goes back into the ship: the map fades to black while the vehicle is refuelled and its cargo moves into the ship (the caller switches
	// to planetside once the fade is over)
	public static void StartBoarding()
	{
		// make sure the planetside terrain grid has been updated
		SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();

		// fade the map to black
		SpaceflightController.m_instance.m_viewport.StartFade( 0.0f, 2.0f );

		// play the activate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		// display message
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Refueling terrain vehicle and transferring all cargo...</color>" );
	}

	// how far the terrain vehicle is from the ship along the ground, in kilometers (2048 units are 225 km). The vehicle's saved place is flat (the ground's height
	// is only drawn), so the ship's height is left out: measured with it, a ship on raised ground was never near enough to go back in
	public static float GetKmToShip()
	{
		return GetVectorToShip().magnitude * 225.0f / 2048.0f;
	}

	// the way from the terrain vehicle to the ship along the ground, the short way round the planet (the ground wraps east to west every 8192 units)
	public static Vector3 GetVectorToShip()
	{
		var playerData = DataController.m_instance.m_playerData;
		var vectorToShip = Tools.LatLongToWorldCoordinates( playerData.m_general.m_selectedLatitude, playerData.m_general.m_selectedLongitude ) - playerData.m_general.m_lastDisembarkedCoordinates;

		vectorToShip.y = 0.0f;

		if ( vectorToShip.x > c_planetWidth * 0.5f )
		{
			vectorToShip.x -= c_planetWidth;
		}
		else if ( vectorToShip.x < c_planetWidth * -0.5f )
		{
			vectorToShip.x += c_planetWidth;
		}

		return vectorToShip;
	}

	// the width of a planet's ground in world units (4 to a pixel of its 2048 wide maps, as TerrainVehicle wraps it)
	const float c_planetWidth = 2048.0f * 4.0f;

	// the crew's walk from where the vehicle stopped to the ship, a cell (a kilometer) at a time: each cell a chance in five of an injury, by the E/M of the cell
	static void WalkBackToTheShip()
	{
		var playerData = DataController.m_instance.m_playerData;
		var vehicle = SpaceflightController.m_instance.m_terrainVehicle;
		var from = playerData.m_general.m_lastDisembarkedCoordinates;
		var to = Tools.LatLongToWorldCoordinates( playerData.m_general.m_selectedLatitude, playerData.m_general.m_selectedLongitude );
		var steps = Mathf.Min( 2000, Mathf.FloorToInt( Tools.SurfaceStepsInKm( from, to ) ) );
		var injured = new System.Collections.Generic.List<PD_Personnel.PD_PersonnelFile>();

		for ( var step = 1; step <= steps; step++ )
		{
			if ( Random.Range( 0, 5 ) == 0 )
			{
				var place = Vector3.Lerp( from, to, (float) step / steps );
				var victim = PlanetWeather.InjureOnTheSurface( vehicle.GetEnergyPerStepAt( place ), PlanetWeather.GetEfficiency(), false );

				if ( ( victim != null ) && !injured.Contains( victim ) )
				{
					injured.Add( victim );
				}
			}
		}

		// (the original's walk shows each injury as it comes; the port's walk is over at once, so each crew member hurt on the way is named once)
		foreach ( var victim in injured )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + victim.m_name + " is injured.</color>" );
		}
	}
}
