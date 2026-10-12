
public class StopTreatmentNoButton : ShipButton
{
	public override string GetLabel()
	{
		return "No";
	}

	public override bool Execute()
	{
		var patient = DataController.m_instance.m_playerData.m_crewAssignment.GetPatient();

		SpaceflightController.m_instance.m_messages.Clear();

		// the treatment goes on (the original's DOCTOR, WF2F1: "I AM CONTINUING TREATMENT ON ...")
		if ( patient != null )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>I am continuing treatment on " + patient.m_name + ".</color>" );
		}

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Medical );

		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

		return false;
	}
}
