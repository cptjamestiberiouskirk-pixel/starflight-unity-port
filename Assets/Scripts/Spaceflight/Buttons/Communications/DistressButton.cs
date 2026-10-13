
public class DistressButton : ShipButton
{
	// the buttons the call was asked from (No puts them back)
	public static ButtonController.ButtonSet s_returnButtonSet = ButtonController.ButtonSet.Bridge;

	public override string GetLabel()
	{
		return "Distress";
	}

	public override bool Execute()
	{
		var buttonController = SpaceflightController.m_instance.m_buttonController;

		// remember where the question was asked
		s_returnButtonSet = buttonController.GetCurrentButtonSet();

		SpaceflightController.m_instance.m_messages.Clear();

		// the original asks first, wherever the ship is (SHIPCON, TOW-US: "TRANSMIT EMERGENCY DISTRESS CALL / CONFIRM: [N Y]") - Yes sends it (DistressYesButton)
		SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Transmit emergency distress call\nConfirm?</color>" );

		buttonController.ChangeButtonSet( ButtonController.ButtonSet.Distress );

		return true;
	}
}
