public class RepairNoButton : ShipButton
{
	public override string GetLabel()
	{
		return "No";
	}

	// the mineral stays in the hold, and the repair stops
	public override bool Execute()
	{
		RepairButton.Answer( false );

		return false;
	}
}
