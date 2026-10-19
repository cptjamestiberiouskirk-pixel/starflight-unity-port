// describes the marked item of the ship's cargo list, as the original's DESCRIBE (CAPTAIN 1STOPTION): an artifact by its analysis, a specimen by where and when it
// was captured; an element has no description
public class ShipCargoDescribeButton : ShipButton
{
	public override string GetLabel()
	{
		return "Describe";
	}

	public override bool Execute()
	{
		if ( ShipCargoButton.DescribeMarkedItem() )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		}
		else
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
		}

		return false;
	}
}
