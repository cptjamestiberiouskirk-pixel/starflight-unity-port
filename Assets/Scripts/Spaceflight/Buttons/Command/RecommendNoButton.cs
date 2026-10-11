public class RecommendNoButton : ShipButton
{
	public override string GetLabel()
	{
		return "No";
	}

	// the planet stays logged and is not recommended
	public override bool Execute()
	{
		SpaceflightController.m_instance.m_messages.Clear();

		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

		LogPlanetButton.BackToCommandButtons();

		return false;
	}
}
