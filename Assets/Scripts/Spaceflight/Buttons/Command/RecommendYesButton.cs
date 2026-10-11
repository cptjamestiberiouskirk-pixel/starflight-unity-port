public class RecommendYesButton : ShipButton
{
	public override string GetLabel()
	{
		return "Yes";
	}

	// the planet that has just been logged is recommended for colonization - Interstel evaluates it when the ship is back at the Starport (STRINFO 1.2)
	public override bool Execute()
	{
		var playerData = DataController.m_instance.m_playerData;

		playerData.m_general.AddColonyRecommendation( playerData.m_general.m_currentPlanetId );

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Recommendation noted.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		LogPlanetButton.BackToCommandButtons();

		return false;
	}
}
