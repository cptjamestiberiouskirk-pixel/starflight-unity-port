using System.Collections.Generic;

// the doctor's choice of a crew member, as the original's (DOCTOR, WF373): "WHO SHOULD I EXAMINE?", the crew member's name, and "CHOOSE CREWMEMBER <-> AND
// PRESS SPACEBAR" - left and right go through the crew on board, and the fire button (Enter in the port) chooses
public class CrewChooser
{
	// the crew members on board, in the order of their roles, each once
	readonly List<PD_Personnel.PD_PersonnelFile> m_crew = new List<PD_Personnel.PD_PersonnelFile>();

	// the crew member shown
	int m_index;

	// "examine" or "treat"
	string m_verb;

	// a line said before the question (the original's "I AM CEASING TREATMENT ON ..." comes before the choice)
	string m_preface;

	// true while the stick is held to one side (one step per push)
	bool m_stickWasPushed;

	// call this to ask - false if there is nobody on board to choose
	public bool Begin( string verb, string preface = null )
	{
		var crewAssignment = DataController.m_instance.m_playerData.m_crewAssignment;

		m_crew.Clear();

		for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
		{
			if ( !crewAssignment.IsAssigned( role ) )
			{
				continue;
			}

			var personnelFile = crewAssignment.GetPersonnelFile( role );

			// a crew member with more than one role is chosen once
			if ( ( personnelFile != null ) && !m_crew.Contains( personnelFile ) )
			{
				m_crew.Add( personnelFile );
			}
		}

		if ( m_crew.Count == 0 )
		{
			return false;
		}

		m_index = 0;
		m_verb = verb;
		m_preface = preface;

		// the push that chose the button is not a step
		m_stickWasPushed = true;

		Show();

		return true;
	}

	// call this every frame while choosing - returns the crew member once the fire button has chosen one, else null
	public PD_Personnel.PD_PersonnelFile Update()
	{
		var input = InputController.m_instance;

		if ( input.m_submit )
		{
			input.Debounce();

			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

			return m_crew[ m_index ];
		}

		var step = input.m_east ? 1 : ( input.m_west ? -1 : 0 );

		if ( step == 0 )
		{
			m_stickWasPushed = false;
		}
		else if ( !m_stickWasPushed )
		{
			m_stickWasPushed = true;

			// go round the crew (m_crew is never empty while choosing)
			m_index = ( m_index + step + m_crew.Count ) % m_crew.Count;

			SoundController.m_instance.PlaySound( SoundController.Sound.Click );

			Show();
		}

		return null;
	}

	void Show()
	{
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();

		if ( !string.IsNullOrEmpty( m_preface ) )
		{
			messages.AddText( "<color=white>" + m_preface + "</color>" );
		}

		messages.AddText( "<color=yellow>Who should I " + m_verb + "?</color>\n<color=white>" + m_crew[ m_index ].m_name + "</color>\nChoose crewmember <-> and press Enter." );
	}
}
