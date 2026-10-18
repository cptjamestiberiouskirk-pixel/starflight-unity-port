
public class ArmWeaponButton : ShipButton
{
	public override string GetLabel()
	{
		return "Arm Weapon";
	}

	public override bool Execute()
	{
		PlayerData playerData = DataController.m_instance.m_playerData;

		if ( ( playerData.m_playerShip.m_laserCannonClass == 0 ) && ( playerData.m_playerShip.m_missileLauncherClass == 0 ) )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.Clear();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Ship is not equipped with weapons.</color>" );

			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		}
		else
		{
			// each weapon goes through the equipment check, and one that has broken down or malfunctions now is not armed (NAVIGATR WF3FB)
			if ( playerData.m_playerShip.ArmWeapons() )
			{
				SoundController.m_instance.PlaySound( SoundController.Sound.Update );
			}

			SpaceflightController.m_instance.m_buttonController.SetManeuverButtons();
		}

		return false;
	}
}
