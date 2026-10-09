
// goes back from the cargo list to the terrain vehicle's buttons (the manual, page 21: cargo "gives you the option of dropping anything")
public class CargoBackButton : ShipButton
{
	public override string GetLabel()
	{
		return "Back";
	}

	public override bool Execute()
	{
		TVCargoButton.BackToTerrainVehicle();

		return false;
	}
}
