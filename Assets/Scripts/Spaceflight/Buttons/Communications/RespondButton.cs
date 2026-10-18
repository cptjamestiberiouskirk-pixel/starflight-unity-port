
public class RespondButton : ShipButton
{
	public override string GetLabel()
	{
		return "Respond";
	}

	public override bool Execute()
	{
		// comm equipment that has broken down, or malfunctions now, cannot answer (COMM-OV COMMUNICATE: the equipment check with part 2)
		if ( !DataController.m_instance.m_playerData.m_playerShip.UseEquipment( PD_PlayerShip.ShipSystem.Comm ) )
		{
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

			return false;
		}

		// change the buttons
		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Respond );

		return true;
	}
}
