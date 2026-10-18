using UnityEngine;

// the terrain vehicle's Return order (since 2026-10-17): the vehicle drives itself back to the ship, on the fuel the trip takes, and goes aboard once it is beside
// it. It is Move with the steering done for the player: the fire button stops it as it stops Move, and out of fuel the crew walks back. It stops short of lava
// ahead, and when the vehicle makes no headway (a rock, a tree, a ruin or the ship in the way: the ground itself never stops the vehicle, which goes up any
// slope), and says why
public class TVReturnButton : MoveButton
{
	// how far ahead of the vehicle, toward the ship, lava stops it (world units; 9.1 are a kilometer)
	public const float c_lavaLookAhead = 12.0f;

	// how near the ship the vehicle stops turning gradually and heads straight for it (turning at Move's rate, it circles a point it drives at)
	public const float c_straightKm = 2.0f;

	// how near the ship the vehicle goes aboard: beside it (the ship's hull keeps the vehicle about 0.9 km from its middle on some sides, inside which the
	// original's own cell, half a kilometer, lies)
	public const float c_besideTheShipKm = 1.5f;

	// how long the vehicle may go without getting nearer to the ship before it stops, and how much nearer counts as headway
	public const float c_stuckSeconds = 4.0f;
	const float c_headwayKm = 0.1f;

	// the nearest the vehicle has been to the ship since the last headway, and how long ago that headway was
	float m_bestKm;
	float m_stuckTimer;

	public override string GetLabel()
	{
		return "Return";
	}

	public override bool Execute()
	{
		base.Execute();

		m_bestKm = GetKmToShip();
		m_stuckTimer = 0.0f;

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Returning to the ship...</color>" );

		// keep the button lit while the vehicle drives
		return true;
	}

	public override bool Update()
	{
		if ( !m_isTransitioning && !SpaceflightController.m_instance.m_gameIsPaused )
		{
			var distanceInKm = GetKmToShip();

			// beside the ship: aboard
			if ( distanceInKm <= c_besideTheShipKm )
			{
				GoAboard();

				return true;
			}

			// lava between the vehicle and the ship
			var playerData = DataController.m_instance.m_playerData;
			var ahead = playerData.m_general.m_coordinates + GetVectorToShip().normalized * c_lavaLookAhead;

			if ( SpaceflightController.m_instance.m_terrainVehicle.IsInLava( ahead ) )
			{
				Halt( "There is lava between the vehicle and the ship." );

				return true;
			}

			// no headway for a while: something is in the way
			if ( distanceInKm < m_bestKm - c_headwayKm )
			{
				m_bestKm = distanceInKm;
				m_stuckTimer = 0.0f;
			}
			else
			{
				m_stuckTimer += Time.deltaTime;

				if ( m_stuckTimer > c_stuckSeconds )
				{
					Halt( "The way back to the ship is blocked." );

					return true;
				}
			}
		}

		var result = base.Update();

		// near the ship it heads straight for it, or it would circle it
		if ( !m_isTransitioning && ( GetKmToShip() < c_straightKm ) )
		{
			var toShip = GetVectorToShip();

			if ( toShip.sqrMagnitude > 0.0001f )
			{
				DataController.m_instance.m_playerData.m_general.m_currentDirection = toShip.normalized;
			}
		}

		return result;
	}

	// Return steers toward the ship, whatever the stick does
	protected override bool GetSteering( out Vector3 moveVector )
	{
		moveVector = GetVectorToShip();

		return moveVector.sqrMagnitude > 0.0001f;
	}

	// the vehicle stops where it is, and the crew says why
	void Halt( string reason )
	{
		SpaceflightController.m_instance.m_terrainVehicle.TurnOffEngines();
		SoundController.m_instance.StopSound( SoundController.Sound.DieselEngine );

		SpaceflightController.m_instance.m_buttonController.DeactivateButton();

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + reason + "</color>" );
	}
}
