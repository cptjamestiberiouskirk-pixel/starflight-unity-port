using UnityEngine;

// fires the terrain vehicle's mounted weapon at the nearest lifeform within its range - the 1984 design notes (planet.txt): the laser "goes about 3/4 of the distance
// from the TV to the edge of the View Screen", the stunner "about 1/2 as far as the laser"; a shot takes the released game's fuel (TV-OV DO.WEAPON: the weapon
// chosen, 1 for the stunner and 2 for the laser, times 300 of the vehicle's tank, with no check of what is left) - the original aims with the joystick, the port at
// the nearest lifeform, and the ranges are the port's
public class TVFireButton : ShipButton
{
	public const float c_laserRange = 60.0f;
	public const float c_stunnerRange = 30.0f;

	public const float c_laserEnergy = 600.0f / TerrainVehicle.c_tankUnits;
	public const float c_stunnerEnergy = 300.0f / TerrainVehicle.c_tankUnits;

	public override string GetLabel()
	{
		return "Fire";
	}

	public override bool Execute()
	{
		var terrainVehicle = DataController.m_instance.m_playerData.m_terrainVehicle;
		var messages = SpaceflightController.m_instance.m_messages;
		var stunner = terrainVehicle.m_stunnerMounted;
		var energy = stunner ? c_stunnerEnergy : c_laserEnergy;
		var weaponName = stunner ? "stunner" : "laser";

		messages.Clear();

		// (the original fires whatever is left in the tank; the port refuses only once the reserve is gone too, when the crew would be walking back)
		if ( terrainVehicle.IsOutOfFuel() )
		{
			messages.AddText( "<color=red>There is not enough energy to fire the " + weaponName + ".</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

			return false;
		}

		terrainVehicle.UseUpFuel( energy );

		SoundController.m_instance.PlaySound( stunner ? SoundController.Sound.PhaserFire : SoundController.Sound.LaserFire );

		var target = FindNearestLifeform( stunner ? c_stunnerRange : c_laserRange );

		// the lifeforms near where the laser is aimed notice it (BEHAV-OV ?PLAYER-FI and ?ALIEN-CLO: the target, or the vehicle with nothing to aim at)
		if ( !stunner )
		{
			TerrainLifeform.s_laserFiredTime = Time.time;
			TerrainLifeform.s_laserAimPosition = ( target != null ) ? target.transform.position : SpaceflightController.m_instance.m_terrainVehicle.transform.position;
		}

		if ( target == null )
		{
			messages.AddText( "<color=white>The " + weaponName + " hits nothing.</color>" );
		}
		else if ( !stunner )
		{
			target.HitWithLaser();

			messages.AddText( "<color=white>The laser hits the lifeform." + ( ( target.m_status == TerrainLifeform.Status.Dead ) ? " It is dead." : "" ) + "</color>" );
		}
		else
		{
			target.HitWithStunner();

			if ( target.m_status == TerrainLifeform.Status.Dead )
			{
				messages.AddText( "<color=white>The stunner kills the lifeform.</color>" );
			}
			else if ( target.m_status == TerrainLifeform.Status.Stunned )
			{
				messages.AddText( "<color=white>The lifeform is stunned.</color>" );
			}
			else
			{
				messages.AddText( "<color=white>The stunner hits the lifeform.</color>" );
			}
		}

		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}

	// the nearest lifeform that is not dead within this distance of the terrain vehicle (null if there is none)
	public static TerrainLifeform FindNearestLifeform( float range )
	{
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( ( terrainGrid == null ) || ( terrainGrid.m_terrainLifeforms == null ) || ( terrainVehicle == null ) )
		{
			return null;
		}

		TerrainLifeform nearest = null;
		var nearestDistance = range;

		foreach ( Transform child in terrainGrid.m_terrainLifeforms.transform )
		{
			var lifeform = child.GetComponent<TerrainLifeform>();

			// nothing in the air: "Airborne creatures may not be dealt with by the player" (planet.txt)
			if ( ( lifeform == null ) || ( lifeform.m_status == TerrainLifeform.Status.Dead ) || lifeform.IsAirborne() )
			{
				continue;
			}

			var distance = Vector3.Distance( child.position, terrainVehicle.transform.position );

			if ( distance <= nearestDistance )
			{
				nearest = lifeform;
				nearestDistance = distance;
			}
		}

		return nearest;
	}
}
