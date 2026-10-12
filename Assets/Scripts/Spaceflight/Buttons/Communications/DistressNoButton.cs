
public class DistressNoButton : ShipButton
{
	public override string GetLabel()
	{
		return "No";
	}

	public override bool Execute()
	{
		// no call: back to the buttons the question was asked from
		SpaceflightController.m_instance.m_messages.Clear();

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( DistressButton.s_returnButtonSet );

		// play the deactivate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

		return false;
	}
}
