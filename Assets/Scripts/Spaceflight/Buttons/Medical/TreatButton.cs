
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

		// calculate healing power based on medicine skill (0.5% per skill point)
		var healingPower = doctor.m_medicine * 0.5f;

		// find injured crew and heal them
		bool healedSomeone = false;
		string healedNames = "";

		foreach ( var personnel in playerData.m_personnel.m_personnelList )
		{
			// only heal living crew members who are injured
			if ( personnel.m_vitality > 0 && personnel.m_vitality < 100 )
			{
				personnel.m_vitality = UnityEngine.Mathf.Min( 100f, personnel.m_vitality + healingPower );
				healedSomeone = true;
				if ( healedNames.Length > 0 )
					healedNames += ", ";
				healedNames += personnel.m_name;
			}
		}

		if ( healedSomeone )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=#00FF00>Treatment applied.</color>\nPatients: <color=white>" + healedNames + "</color>" );
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
