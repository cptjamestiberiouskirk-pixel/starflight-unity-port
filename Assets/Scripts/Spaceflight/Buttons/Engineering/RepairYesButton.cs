public class RepairYesButton : ShipButton
{
	public override string GetLabel()
	{
		return "Yes";
	}

	// the engineer takes the mineral from the hold and starts the repair
	public override bool Execute()
	{
		RepairButton.Answer( true );

		return false;
	}
}
