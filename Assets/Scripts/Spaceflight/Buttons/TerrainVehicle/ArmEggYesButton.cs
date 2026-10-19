// the crew's answer to "Arm this device?", asked when the terrain vehicle drops the Black Egg (ITEMS-OV WE9AA): Yes arms it, and it counts down once the ship is
// back above the planets of its star system (BlackEgg)
public class ArmEggYesButton : ShipButton
{
	public override string GetLabel()
	{
		return "Yes";
	}

	public override bool Execute()
	{
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		TVCargoButton.AnswerArmEgg( true );

		return false;
	}
}
