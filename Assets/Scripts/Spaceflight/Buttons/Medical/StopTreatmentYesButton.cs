
public class StopTreatmentYesButton : ShipButton
{
	public override string GetLabel()
	{
		return "Yes";
	}

	public override bool Execute()
	{
		var crewAssignment = DataController.m_instance.m_playerData.m_crewAssignment;
		var buttonController = SpaceflightController.m_instance.m_buttonController;

		var patient = crewAssignment.GetPatient();

		// the treatment stops (the original's DOCTOR, WF2AF: "I AM CEASING TREATMENT ON ..."), and the doctor asks who to treat instead
		crewAssignment.m_treatmentIsUnderWay = false;

		TreatButton.s_preface = ( patient != null ) ? "I am ceasing treatment on " + patient.m_name + "." : null;

		buttonController.ChangeButtonSet( ButtonController.ButtonSet.Medical );

		// Treat is the second button of the doctor's
		buttonController.SetSelectedButton( 1 );
		buttonController.ActivateButton();

		return false;
	}
}
