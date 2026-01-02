
public class CombatButton : ShipButton
{
	public override string GetLabel()
	{
		return "Combat";
	}

	public override bool Execute()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		switch ( playerData.m_general.m_location )
		{
			case PD_General.Location.Encounter:

				// check if we have weapons
				if ( ( playerData.m_playerShip.m_laserCannonClass == 0 ) && ( playerData.m_playerShip.m_missileLauncherClass == 0 ) )
				{
					SpaceflightController.m_instance.m_messages.AddText( "<color=red>No weapons installed!</color>" );
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );
					break;
				}

				// check if weapons are armed
				if ( !playerData.m_playerShip.m_weaponsAreArmed )
				{
					SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Weapons are not armed. Arm weapons first.</color>" );
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );
					break;
				}

				// switch to combat button set
				SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Combat );

				SoundController.m_instance.PlaySound( SoundController.Sound.Click );

				return true;

			default:

				SpaceflightController.m_instance.m_messages.AddText( "<color=red>Combat only available in encounters!</color>" );
				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				break;
		}

		return false;
	}
}
