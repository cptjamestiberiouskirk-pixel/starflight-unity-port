
public class TreatButton : ShipButton
{
	public override string GetLabel()
	{
		return "Treat";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		SpaceflightController.m_instance.m_messages.Clear();

		// check if we have a doctor assigned
		if ( !playerData.m_crewAssignment.IsAssigned( PD_CrewAssignment.Role.Doctor ) )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>No doctor assigned!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// get the doctor's medical skill
		var doctor = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Doctor );

		// check if doctor is alive
		if ( doctor.m_vitality <= 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>The doctor is incapacitated!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// the doctor treats one patient at a time - is someone still being treated?
		var patient = playerData.m_crewAssignment.GetPatient();

		var alreadyUnderWay = ( patient != null ) && ( patient.m_vitality > 0 ) && ( patient.m_vitality < 100 );

		if ( !alreadyUnderWay )
		{
			// no - treat the living crew member on board who is hurt the most (people who were left at starport are not on the ship)
			patient = playerData.m_crewAssignment.FindMostInjuredCrewMember();

			if ( patient != null )
			{
				playerData.m_crewAssignment.StartTreatment( patient );
			}
		}

		if ( patient != null )
		{
			// treatment takes time - the patient recovers a little at a time (the better the doctor the faster it goes)
			var treatmentRate = playerData.m_crewAssignment.GetTreatmentRate();

			var timeRemaining = ( treatmentRate > 0.0f ) ? ( ( 100.0f - patient.m_vitality ) / treatmentRate ) : 0.0f;

			SpaceflightController.m_instance.m_messages.AddText(
				( alreadyUnderWay ? "<color=#00FF00>Still treating " : "<color=#00FF00>Beginning treatment of " ) + patient.m_name + ", Captain.</color>\n" +
				"Vitality: <color=white>" + UnityEngine.Mathf.CeilToInt( patient.m_vitality ) + "%</color>\n" +
				"Estimated time: <color=white>" + FormatDuration( timeRemaining ) + "</color>"
			);
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		}
		else
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>All crew members are healthy.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		}

		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		return false;
	}
}
