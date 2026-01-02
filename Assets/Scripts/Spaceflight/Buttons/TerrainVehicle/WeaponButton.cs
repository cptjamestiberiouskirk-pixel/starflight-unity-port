
public class WeaponButton : ShipButton
{
	public override string GetLabel()
	{
		return "Weapon";
	}

	public override bool Execute()
	{
		SpaceflightController.m_instance.m_messages.Clear();

		// check for hostile lifeforms (currently none implemented)
		// in the future, this would check for nearby hostile creatures
		SpaceflightController.m_instance.m_messages.AddText(
			"<color=yellow>Weapon Systems:</color>\n" +
			"<color=white>No hostile lifeforms detected.\n" +
			"Terrain vehicle weapons standing by.</color>"
		);

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
