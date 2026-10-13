
public class RaiseShieldsButton : ShipButton
{
	public override string GetLabel()
	{
		return "Raise Shields";
	}

	public override bool Execute()
	{
		var playerData = DataController.m_instance.m_playerData;

		if ( playerData.m_playerShip.m_shieldingClass == 0 )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.Clear();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Ship is not equipped with shields.</color>" );

			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		}
		else if ( SpaceflightController.m_instance.ShipIsInsideANebula() || ( playerData.m_playerShip.m_elementStorage.Find( 5 ) == null ) )
		{
			// a nebula keeps the shields down, and shields burn endurium, so they can't be raised without any - the original says the same for both, with a beep
			// (NAVIGATR (/(UD)SHIELD): ?NEB or no fuel, SHIELDS CAN'T BE RAISED)
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.Clear();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Shields can't be raised.</color>" );

			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		}
		else
		{
			playerData.m_playerShip.RaiseShields();

			SoundController.m_instance.PlaySound( SoundController.Sound.Update );

			SpaceflightController.m_instance.m_buttonController.SetManeuverButtons();
		}

		return false;
	}
}
