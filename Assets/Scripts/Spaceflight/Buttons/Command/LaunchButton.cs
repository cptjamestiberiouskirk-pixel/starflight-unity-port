
public class LaunchButton : ShipButton
{
	public override string GetLabel()
	{
		return "Launch";
	}

	public override bool Execute()
	{
		SpaceflightController.m_instance.m_messages.Clear();

		// the original's words (CAPTAIN: "READY TO LAUNCH / CONFIRM: [N Y]")
		SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Ready to launch. Confirm?</color>" );

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Launch );

		return true;
	}
}
