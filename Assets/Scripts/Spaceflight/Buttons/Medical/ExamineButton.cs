
public class ExamineButton : ShipButton
{
	public override string GetLabel()
	{
		return "Examine";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		SpaceflightController.m_instance.m_messages.Clear();

		var report = "<color=#FFFF00>Crew Health Report:</color>\n";
		var hasAnyCrew = false;

		// go through each crew role and report their health
		for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
		{
			if ( playerData.m_crewAssignment.IsAssigned( role ) )
			{
				hasAnyCrew = true;
				var personnel = playerData.m_crewAssignment.GetPersonnelFile( role );
				var vitality = UnityEngine.Mathf.CeilToInt( personnel.m_vitality );

				// color code based on health status
				string statusColor;
				if ( personnel.m_vitality <= 0 )
					statusColor = "<color=#808080>";
				else if ( personnel.m_vitality >= 75 )
					statusColor = "<color=#00FF00>";
				else if ( personnel.m_vitality >= 25 )
					statusColor = "<color=#FFFF00>";
				else
					statusColor = "<color=#FF0000>";

				report += personnel.m_name + ": " + statusColor + vitality + "%</color>\n";
			}
		}

		if ( !hasAnyCrew )
		{
			report += "<color=white>No crew assigned.</color>";
		}

		SpaceflightController.m_instance.m_messages.AddText( report );
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
