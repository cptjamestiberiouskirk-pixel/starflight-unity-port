
// drops the marked item of the terrain vehicle's cargo list on the ground beside it (the manual, page 21: cargo "gives you the option of dropping anything")
public class CargoDropButton : ShipButton
{
	public override string GetLabel()
	{
		return "Drop";
	}

	public override bool Execute()
	{
		TVCargoButton.DropMarkedItem();

		return false;
	}
}
