// closes the ship's cargo list, back to the command buttons it was opened from
public class ShipCargoBackButton : ShipButton
{
	public override string GetLabel()
	{
		return "Back";
	}

	public override bool Execute()
	{
		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

		ShipCargoButton.CloseCargoList();

		return false;
	}
}
