
public class AnswerYesButton : ShipButton
{
	public override string GetLabel()
	{
		return "Yes";
	}

	public override bool Execute()
	{
		var encounter = SpaceflightController.m_instance.m_encounter;

		encounter.AddComm( GD_Comm.Subject.Yes, true );

		// the comm buttons again, unless the answer has ended the comm link (a refused demand of the Veloxi's, or the comm's equipment check)
		if ( encounter.m_pdEncounter.m_connected )
		{
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Comm );
		}

		return false;
	}
}
