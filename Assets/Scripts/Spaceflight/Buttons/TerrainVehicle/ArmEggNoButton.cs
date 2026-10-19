// the crew's answer to "Arm this device?", asked when the terrain vehicle drops the Black Egg (ITEMS-OV WE9AA): No leaves the egg on the ground unarmed
public class ArmEggNoButton : ShipButton
{
	public override string GetLabel()
	{
		return "No";
	}

	public override bool Execute()
	{
		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

		TVCargoButton.AnswerArmEgg( false );

		return false;
	}
}
