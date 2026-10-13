
/// <summary>
/// Button to fire missile at current target.
/// </summary>
public class FireMissileButton : ShipButton
{
	public override string GetLabel()
	{
		var playerData = DataController.m_instance.m_playerData;

		if ( playerData.m_playerShip.m_missileLauncherClass <= 0 )
		{
			return "Missile (N/A)";
		}

		return "Missile";
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

		// check if we have missile launcher (COMBAT-OV: "WEAPON NOT AVAILABLE")
		if ( playerData.m_playerShip.m_missileLauncherClass <= 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Weapon not available</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// check if we have fuel (every launch uses a little endurium)
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
		if ( !CombatController.m_instance.FirePlayerMissile() )
		{
			// couldn't fire (cooldown, out of range, etc.)
			return false;
		}

		// update the buttons
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return true;
	}
}
