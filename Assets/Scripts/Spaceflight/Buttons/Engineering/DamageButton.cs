
public class DamageButton : ShipButton
{
	public override string GetLabel()
	{
		return "Damage";
	}

	public override bool Execute()
	{
		// the original's Damage shows the DAMAGE display, a bar for each of the hull and the systems, and writes nothing (ENGINEER in the decompiled original:
		// (/DAMAGE) draws on the auxiliary view only)
		var displayController = SpaceflightController.m_instance.m_displayController;

		if ( displayController.m_damageDisplay != null )
		{
			displayController.ChangeDisplay( displayController.m_damageDisplay );
		}

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
