
// marks the next item of the terrain vehicle's cargo list (the manual, page 21: cargo "gives you the option of dropping anything")
public class CargoNextButton : ShipButton
{
	public override string GetLabel()
	{
		return "Next";
	}

	public override bool Execute()
	{
		TVCargoButton.MarkNextItem();

		return false;
	}
}
