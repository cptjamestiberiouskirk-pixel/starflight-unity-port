
public class TreatButton : ShipButton
{
	// the choice of the crew member to treat
	readonly CrewChooser m_crewChooser = new CrewChooser();

	// a line said before the question when the button is run again after a treatment was stopped (StopTreatmentYesButton)
	public static string s_preface;

	public override string GetLabel()
	{
		return "Treat";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		var preface = s_preface;

		s_preface = null;

		SpaceflightController.m_instance.m_messages.Clear();

		// check if we have a doctor assigned
		if ( !playerData.m_crewAssignment.IsAssigned( PD_CrewAssignment.Role.Doctor ) )
		{
			return Refuse( "<color=red>No doctor assigned!</color>" );
		}

		// check if doctor is alive
		var doctor = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Doctor );

		if ( doctor.m_vitality <= 0 )
		{
			return Refuse( "<color=red>The doctor is incapacitated!</color>" );
		}

		// the doctor treats one patient at a time: with someone under treatment the original asks first (DOCTOR, (/TREAT): "SHOULD I STOP TREATING ... TO TREAT
		// SOMEONE ELSE? [N Y]") - No and Yes on the console
		var patient = playerData.m_crewAssignment.GetPatient();

		if ( ( patient != null ) && ( patient.m_vitality > 0 ) && ( patient.m_vitality < 100 ) )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Should I stop treating " + patient.m_name + " to treat someone else?</color>" );

			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.StopTreatment );

			return false;
		}

		// the doctor asks who to treat
		if ( m_crewChooser.Begin( "treat", preface ) )
		{
			return true;
		}

		return Refuse( "<color=white>No crew assigned.</color>" );
	}

	public override bool Update()
	{
		var crewMember = m_crewChooser.Update();

		if ( crewMember != null )
		{
			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>" + Treat( crewMember ) + "</color>" );

			SpaceflightController.m_instance.m_buttonController.DeactivateButton();
		}

		// the stick and the fire button are the chooser's until it is done
		return true;
	}

	// the doctor's answer for the crew member chosen, in the original's words (DOCTOR, WF26F) - the treatment itself takes its time (PD_CrewAssignment.UpdateTreatment)
	static string Treat( PD_Personnel.PD_PersonnelFile crewMember )
	{
		if ( crewMember.m_isDead || ( crewMember.m_vitality <= 0 ) )
		{
			return crewMember.m_name + " is beyond help.";
		}

		if ( crewMember.m_vitality >= 100 )
		{
			return crewMember.m_name + " doesn't need medical treatment.";
		}

		DataController.m_instance.m_playerData.m_crewAssignment.StartTreatment( crewMember );

		return "I am starting treatment on " + crewMember.m_name + ".";
	}

	static bool Refuse( string text )
	{
		SpaceflightController.m_instance.m_messages.AddText( text );

		SoundController.m_instance.PlaySound( SoundController.Sound.Error );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
