
public class LaunchButton : ShipButton
{
	public override string GetLabel()
	{
		return "Launch";
	}

	public override bool Execute()
	{
		SpaceflightController.m_instance.m_messages.Clear();

		// a ship back towed or without its terrain vehicle stays docked until Operations has given its evaluation (SHIPBUTTONS WF18E)
		var general = DataController.m_instance.m_playerData.m_general;

		if ( ( general.m_location == PD_General.Location.DockingBay ) && general.MustReportToOperations() )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Report to Operations for evaluation.</color>" );

			return false;
		}

		// engines that have broken down, or malfunction now, cannot lift the ship off a planet (CAPTAIN /LAUNCH/LAND: the equipment check with part 5 on a planet's
		// surface, and none in the Starport's docking bay)
		if ( ( general.m_location == PD_General.Location.Planetside ) && !DataController.m_instance.m_playerData.m_playerShip.UseEquipment( PD_PlayerShip.ShipSystem.Engines ) )
		{
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

			return false;
		}

		// the original's words (CAPTAIN: "READY TO LAUNCH / CONFIRM: [N Y]")
		SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Ready to launch. Confirm?</color>" );

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Launch );

		return true;
	}
}
