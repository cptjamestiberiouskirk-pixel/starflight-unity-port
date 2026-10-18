// marks the next item of the ship's cargo list (the original's (/ITEMS) moves its cursor down the list)
public class ShipCargoNextButton : ShipButton
{
	public override string GetLabel()
	{
		return "Next";
	}

	public override bool Execute()
	{
		SoundController.m_instance.PlaySound( SoundController.Sound.Click );

		ShipCargoButton.MarkNextItem();

		return false;
	}
}
