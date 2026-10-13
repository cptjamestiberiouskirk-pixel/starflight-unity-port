
public class ExamineButton : ShipButton
{
	// the choice of the crew member to examine
	readonly CrewChooser m_crewChooser = new CrewChooser();

	public override string GetLabel()
	{
		return "Examine";
	}

	public override bool Execute()
	{
		// the doctor asks who to examine (the original's DOCTOR, (/EXAMINE))
		if ( m_crewChooser.Begin( "examine" ) )
		{
			return true;
		}

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>No crew assigned.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Error );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}

	public override bool Update()
	{
		var crewMember = m_crewChooser.Update();

		if ( crewMember != null )
		{
			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>" + GetExamination( crewMember ) + "</color>" );

			SpaceflightController.m_instance.m_buttonController.DeactivateButton();
		}

		// the stick and the fire button are the chooser's until it is done
		return true;
	}

	// the original's examination (DOCTOR, WF421): "DATA IS NOT WOUNDED, AND HAS A VITALITY OF 100 AND A DURABILITY OF 10." - "IS DEAD." for the dead, and a line
	// more for the crew member being treated
	public static string GetExamination( PD_Personnel.PD_PersonnelFile crewMember )
	{
		var vitality = UnityEngine.Mathf.Clamp( UnityEngine.Mathf.CeilToInt( crewMember.m_vitality ), 0, 100 );

		string text;

		if ( crewMember.m_isDead || ( vitality <= 0 ) )
		{
			text = crewMember.m_name + " is dead.";
		}
		else
		{
			var crewRaceList = DataController.m_instance.m_gameData.m_crewRaceList;
			var durability = ( ( crewMember.m_crewRaceId >= 0 ) && ( crewMember.m_crewRaceId < crewRaceList.Length ) ) ? crewRaceList[ crewMember.m_crewRaceId ].m_durability : 0;

			text = crewMember.m_name + " is " + GetWoundWord( vitality ) + " wounded, and has a vitality of " + vitality + " and a durability of " + durability + ".";
		}

		var patient = DataController.m_instance.m_playerData.m_crewAssignment.GetPatient();

		if ( patient == crewMember )
		{
			text += "\n" + crewMember.m_name + " is currently being treated.";
		}

		return text;
	}

	// how badly a crew member is wounded, by the bands of the original (DOCTOR, WF0E6)
	public static string GetWoundWord( int vitality )
	{
		if ( vitality <= 20 )
		{
			return "critically";
		}

		if ( vitality <= 50 )
		{
			return "heavily";
		}

		if ( vitality <= 80 )
		{
			return "moderately";
		}

		if ( vitality < 100 )
		{
			return "slightly";
		}

		return "not";
	}
}
