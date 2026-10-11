
// goes back from the terrain vehicle's weapons to its buttons
public class TVWeaponBackButton : ShipButton
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
