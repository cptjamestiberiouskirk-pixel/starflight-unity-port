
/// <summary>
/// Button to fire laser cannon at current target.
/// </summary>
public class FireLaserButton : ShipButton
{
	public override string GetLabel()
	{
		var playerData = DataController.m_instance.m_playerData;

		if ( playerData.m_playerShip.m_laserCannonClass <= 0 )
		{
			return "Laser (N/A)";
		}

		return "Laser";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// only works in encounter
		if ( playerData.m_general.m_location != PD_General.Location.Encounter )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// the weapons fire only once they are armed (COMBAT-OV: "WEAPONS NOT ARMED")
		if ( !playerData.m_playerShip.m_weaponsAreArmed )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Weapons not armed</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// check if we have laser cannon (COMBAT-OV: "WEAPON NOT AVAILABLE")
		if ( playerData.m_playerShip.m_laserCannonClass <= 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Weapon not available</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// check if we have fuel (every shot uses a little endurium)
		if ( !playerData.m_playerShip.HasFuel() )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Out of fuel!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// check if we have a target
		if ( CombatController.m_instance.GetTargetIndex() < 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>No target present</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// attempt to fire
		if ( !CombatController.m_instance.FirePlayerLaser() )
		{
			// couldn't fire (cooldown, out of range, etc.)
			return false;
		}

		return true;
	}
}
