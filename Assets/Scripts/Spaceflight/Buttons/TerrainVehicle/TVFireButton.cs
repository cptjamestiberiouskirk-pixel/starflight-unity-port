using UnityEngine;

// fires the terrain vehicle's mounted weapon at the nearest lifeform within its range - the 1984 design notes (planet.txt): the laser "goes about 3/4 of the distance
// from the TV to the edge of the View Screen", the stunner "about 1/2 as far as the laser", the laser "uses 8 units of energy each time it is fired. The stunner
// uses 4." (of a full terrain vehicle's 999) - the original aims with the joystick, the port at the nearest lifeform, and the ranges are the port's
public class TVFireButton : ShipButton
{
	public const float c_laserRange = 60.0f;
	public const float c_stunnerRange = 30.0f;

	const float c_laserEnergy = 8.0f / 999.0f;
	const float c_stunnerEnergy = 4.0f / 999.0f;

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

		if ( terrainVehicle.m_fuelRemaining < energy )
		{
			messages.AddText( "<color=red>There is not enough energy to fire the " + weaponName + ".</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

			return false;
		}

		terrainVehicle.UseUpFuel( energy );

		SoundController.m_instance.PlaySound( stunner ? SoundController.Sound.PhaserFire : SoundController.Sound.LaserFire );

		// the lifeforms near by notice the laser (lf-behav.txt: ?PLAYER-FIRING-LASER)
		if ( !stunner )
		{
			TerrainLifeform.s_laserFiredTime = Time.time;
		}

		var target = FindNearestLifeform( stunner ? c_stunnerRange : c_laserRange );

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
				messages.AddText( "<color=white>The stunner " + ( ( target.m_species.m_stunEffect == GD_Species.StunEffect.Unaffected ) ? "has no effect on the lifeform." : "hits the lifeform." ) + "</color>" );
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

			if ( ( lifeform == null ) || ( lifeform.m_status == TerrainLifeform.Status.Dead ) )
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
