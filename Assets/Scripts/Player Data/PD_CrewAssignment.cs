
using System;
using UnityEngine;

[Serializable]

public class PD_CrewAssignment
{
	public enum Role
	{
		First = 0,
		Captain = 0,
		ScienceOfficer,
		Navigator,
		Engineer,
		CommunicationsOfficer,
		Doctor,
		Count
	};

	public int m_captainFileId;
	public int m_scienceOfficerFileId;
	public int m_navigatorFileId;
	public int m_engineerFileId;
	public int m_communicationsOfficerFileId;
	public int m_doctorFileId;

	// how much vitality the doctor restores each second for every point of medicine skill, and the slowest a doctor ever works
	public const float c_treatmentRatePerSkillPoint = 0.02f;
	public const float c_minimumTreatmentRate = 0.5f;

	// true while the doctor is treating someone, and the personnel file of that patient (false in save files from before treatment took time)
	public bool m_treatmentIsUnderWay;
	public int m_patientFileId;

	public void Reset()
	{
		// unassign all crew member roles
		m_captainFileId = -1;
		m_scienceOfficerFileId = -1;
		m_navigatorFileId = -1;
		m_engineerFileId = -1;
		m_communicationsOfficerFileId = -1;
		m_doctorFileId = -1;

		// the doctor is not treating anyone
		m_treatmentIsUnderWay = false;
		m_patientFileId = -1;
	}

	public int GetFileId( Role role )
	{
		switch ( role )
		{
			case Role.Captain: return m_captainFileId;
			case Role.ScienceOfficer: return m_scienceOfficerFileId;
			case Role.Navigator: return m_navigatorFileId;
			case Role.Engineer: return m_engineerFileId;
			case Role.CommunicationsOfficer: return m_communicationsOfficerFileId;
			case Role.Doctor: return m_doctorFileId;
		}

		return -1;
	}

	public bool IsAssigned( Role role )
	{
		var fileId = GetFileId( role );

		return ( fileId == -1 ) ? false : true;
	}

	public void Assign( Role role, int fileId )
	{
		switch ( role )
		{
			case Role.Captain: m_captainFileId = fileId; break;
			case Role.ScienceOfficer: m_scienceOfficerFileId = fileId; break;
			case Role.Navigator: m_navigatorFileId = fileId; break;
			case Role.Engineer: m_engineerFileId = fileId; break;
			case Role.CommunicationsOfficer: m_communicationsOfficerFileId = fileId; break;
			case Role.Doctor: m_doctorFileId = fileId; break;
		}
	}

	// unassign this personnel file from every role it has (call this before deleting the file)
	public void Unassign( int fileId )
	{
		for ( var role = Role.First; role < Role.Count; role++ )
		{
			if ( GetFileId( role ) == fileId )
			{
				Assign( role, -1 );
			}
		}
	}

	// unassign roles that point at personnel files that no longer exist (deleting crew used to leave them behind)
	public void UnassignMissingCrew( PD_Personnel personnel )
	{
		for ( var role = Role.First; role < Role.Count; role++ )
		{
			if ( IsAssigned( role ) && !personnel.HasPersonnelFile( GetFileId( role ) ) )
			{
				Assign( role, -1 );
			}
		}
	}

	public PD_Personnel.PD_PersonnelFile GetPersonnelFile( Role role )
	{
		var fileId = GetFileId( role );

		var playerData = DataController.m_instance.m_playerData;

		return playerData.m_personnel.GetPersonnelFile( fileId );
	}

	// returns true if this personnel file has at least one role (in other words this person is on board the ship)
	public bool IsOnBoard( int fileId )
	{
		// an unassigned role is not a person
		if ( fileId == -1 )
		{
			return false;
		}

		for ( var role = Role.First; role < Role.Count; role++ )
		{
			if ( GetFileId( role ) == fileId )
			{
				return true;
			}
		}

		return false;
	}

	// the living crew member on board who is hurt the most (null if nobody on board needs treatment)
	public PD_Personnel.PD_PersonnelFile FindMostInjuredCrewMember()
	{
		PD_Personnel.PD_PersonnelFile mostInjured = null;

		// go through each role (only the crew on board can be treated)
		for ( var role = Role.First; role < Role.Count; role++ )
		{
			if ( IsAssigned( role ) )
			{
				var personnelFile = GetPersonnelFile( role );

				// only the living can be treated
				if ( ( personnelFile.m_vitality > 0 ) && ( personnelFile.m_vitality < 100 ) )
				{
					if ( ( mostInjured == null ) || ( personnelFile.m_vitality < mostInjured.m_vitality ) )
					{
						mostInjured = personnelFile;
					}
				}
			}
		}

		return mostInjured;
	}

	// the amount of vitality the doctor restores each second (zero if there is no doctor who can work)
	public float GetTreatmentRate()
	{
		// nobody gets treated without a doctor
		if ( !IsAssigned( Role.Doctor ) )
		{
			return 0.0f;
		}

		var doctor = GetPersonnelFile( Role.Doctor );

		// or with one who is incapacitated
		if ( doctor.m_vitality <= 0 )
		{
			return 0.0f;
		}

		// the better the doctor the faster the patient recovers
		return Mathf.Max( c_minimumTreatmentRate, doctor.m_medicine * c_treatmentRatePerSkillPoint );
	}

	// the crew member the doctor is treating (null if the doctor is not treating anyone)
	public PD_Personnel.PD_PersonnelFile GetPatient()
	{
		// the patient has to be on board
		if ( !m_treatmentIsUnderWay || !IsOnBoard( m_patientFileId ) )
		{
			return null;
		}

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		return playerData.m_personnel.GetPersonnelFile( m_patientFileId );
	}

	// call this to have the doctor start treating a crew member
	public void StartTreatment( PD_Personnel.PD_PersonnelFile patient )
	{
		m_treatmentIsUnderWay = true;
		m_patientFileId = patient.m_fileId;
	}

	// call this every frame during spaceflight - the patient recovers a little at a time
	public void UpdateTreatment( float deltaTime )
	{
		// nothing to do unless the doctor has been told to treat someone
		if ( !m_treatmentIsUnderWay )
		{
			return;
		}

		// the treatment is over if the patient is not on board any more or has died
		var patient = GetPatient();

		if ( ( patient == null ) || ( patient.m_vitality <= 0 ) )
		{
			m_treatmentIsUnderWay = false;

			return;
		}

		// the treatment stops if the doctor is gone or incapacitated
		var treatmentRate = GetTreatmentRate();

		if ( treatmentRate <= 0.0f )
		{
			m_treatmentIsUnderWay = false;

			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Treatment has stopped. No doctor is available!</color>" );

			return;
		}

		// the patient recovers a little more
		patient.m_vitality = Mathf.Min( 100.0f, patient.m_vitality + treatmentRate * deltaTime );

		// has the patient fully recovered?
		if ( patient.m_vitality >= 100.0f )
		{
			// yes - the doctor is done
			m_treatmentIsUnderWay = false;

			SpaceflightController.m_instance.m_messages.AddText( "<color=#00FF00>" + patient.m_name + " has fully recovered.</color>" );
		}
	}

	// return true if there is at least one (living) human crew member
	public bool HasAtLeastOneHumanCrew()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;
		
		// go through each role
		for ( var role = Role.First; role < Role.Count; role++ )
		{
			if ( IsAssigned( role ) )
			{
				var personnelFile = GetPersonnelFile( role );

				if ( personnelFile.m_vitality > 0 )
				{
					if ( gameData.m_crewRaceList[ personnelFile.m_crewRaceId ].m_race == GameData.Race.Human )
					{
						// found one!
						return true;
					}
				}
			}
		}

		// no human crew member
		return false;
	}
}
