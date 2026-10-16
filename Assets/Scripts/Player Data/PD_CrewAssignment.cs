
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

	// the crew heals in ticks, one every (260 - the doctor's medicine) quarters of a second (the original's HEALTI, (260 - medicine) x 250 ms, HEAL-OV WF292); each
	// tick gives every living crew member on board 1 point of vitality and the doctor's patient 2, or 1 on a planet's surface (WF2CD)
	public const int c_healTickBase = 260;
	public const float c_healTickSecondsPerPoint = 0.25f;
	const int c_healTickPoints = 1;
	const int c_patientHealTickPoints = 2;

	// the most heal ticks one update makes up for (a guard: a long pause in the game is not a reason to heal forever)
	const int c_maximumHealTicksPerUpdate = 1000;

	// true while the doctor is treating someone, and the personnel file of that patient (false in save files from before treatment took time)
	public bool m_treatmentIsUnderWay;
	public int m_patientFileId;

	// the seconds since the last heal tick (not saved: a loaded game starts a new tick)
	float m_healTimer;

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

	// the personnel file of a post, null if the post is not filled or its file is gone (GetPersonnelFile throws for those)
	PD_Personnel.PD_PersonnelFile FindPersonnelFile( Role role )
	{
		var fileId = GetFileId( role );
		var personnel = DataController.m_instance.m_playerData.m_personnel;

		return ( ( fileId != -1 ) && personnel.HasPersonnelFile( fileId ) ) ? personnel.GetPersonnelFile( fileId ) : null;
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

	// the seconds between two heal ticks: (260 - the doctor's medicine) quarters of a second, so from 1.25 s with the best doctor to 65 s with none (a dead
	// member's skills are gone, and a missing doctor counts as no medicine)
	public float GetHealTickSeconds()
	{
		var doctor = FindPersonnelFile( Role.Doctor );
		var medicine = ( ( doctor != null ) && !IsDeadFile( doctor ) ) ? Mathf.Clamp( doctor.m_medicine, 0, 255 ) : 0;

		return ( c_healTickBase - medicine ) * c_healTickSecondsPerPoint;
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

	// call this every frame during spaceflight - the crew heals in ticks (HEAL-OV WF2CD), and the treatment is over when the patient is healed, has died or is not
	// on board any more
	public void UpdateTreatment( float deltaTime )
	{
		if ( m_treatmentIsUnderWay )
		{
			var patient = GetPatient();

			if ( ( patient == null ) || IsDeadFile( patient ) )
			{
				m_treatmentIsUnderWay = false;
			}
		}

		var tickSeconds = GetHealTickSeconds();

		m_healTimer += deltaTime;

		for ( var tick = 0; ( tick < c_maximumHealTicksPerUpdate ) && ( m_healTimer >= tickSeconds ); tick++ )
		{
			m_healTimer -= tickSeconds;

			HealTick();
		}

		// a long pause is not made up for beyond the guard
		m_healTimer = Mathf.Min( m_healTimer, tickSeconds );
	}

	// a heal tick (WF2CD): each living crew member on board gains a point of vitality, once however many posts they hold, and the doctor's patient two, except on
	// a planet's surface; a patient healed to 100 off the surface is done ("<NAME> IS HEALED", WF2B4) - nothing heals while the whole crew is whole (WF280)
	void HealTick()
	{
		var playerData = DataController.m_instance.m_playerData;
		var location = playerData.m_general.m_location;
		var onTheSurface = ( location == PD_General.Location.Planetside ) || ( location == PD_General.Location.Disembarked );

		if ( !SomeoneOnBoardIsHurt() )
		{
			return;
		}

		for ( var role = Role.First; role < Role.Count; role++ )
		{
			var file = FindPersonnelFile( role );

			if ( ( file == null ) || IsDeadFile( file ) || HoldsAnEarlierPost( file, role ) )
			{
				continue;
			}

			var isPatient = m_treatmentIsUnderWay && ( file.m_fileId == m_patientFileId );

			if ( isPatient && !onTheSurface )
			{
				file.m_vitality = Mathf.Min( 100.0f, file.m_vitality + c_patientHealTickPoints );

				if ( file.m_vitality >= 100.0f )
				{
					m_treatmentIsUnderWay = false;

					if ( SpaceflightController.m_instance != null )
					{
						SpaceflightController.m_instance.m_messages.AddText( "<color=#00FF00>" + file.m_name + " is healed.</color>" );
					}
				}
			}
			else
			{
				file.m_vitality = Mathf.Min( 100.0f, file.m_vitality + c_healTickPoints );
			}
		}
	}

	// true if a living crew member on board is below full vitality (WF280: the original heals nobody while every one of them is at 100)
	bool SomeoneOnBoardIsHurt()
	{
		for ( var role = Role.First; role < Role.Count; role++ )
		{
			var file = FindPersonnelFile( role );

			if ( ( file != null ) && !IsDeadFile( file ) && ( file.m_vitality < 100.0f ) )
			{
				return true;
			}
		}

		return false;
	}

	// true if a crew member also holds a post before this one (ROLE-C lists each crew member on board once)
	bool HoldsAnEarlierPost( PD_Personnel.PD_PersonnelFile file, Role role )
	{
		for ( var earlier = Role.First; earlier < role; earlier++ )
		{
			if ( GetFileId( earlier ) == file.m_fileId )
			{
				return true;
			}
		}

		return false;
	}

	// true if a personnel file is of a crew member who has died
	public static bool IsDeadFile( PD_Personnel.PD_PersonnelFile file )
	{
		return file.m_isDead || ( file.m_vitality <= 0.0f );
	}

	// the post holder with the best skill for a post, the captain's holder if none of them has any (HEAL-OV WF42F: the six post holders, in the order of the
	// posts, the first with the highest skill)
	int FindBestHolder( Role role )
	{
		var best = m_captainFileId;
		var bestSkill = 0;

		for ( var candidate = Role.First; candidate < Role.Count; candidate++ )
		{
			var file = FindPersonnelFile( candidate );

			if ( file == null )
			{
				continue;
			}

			// the posts after the captain's are the five skills in their order (science officer: science ... doctor: medicine)
			var skill = file.GetSkill( (int) role - 1 );

			if ( skill > bestSkill )
			{
				best = file.m_fileId;
				bestSkill = skill;
			}
		}

		return best;
	}

	// what the original does after a crew member dies (HEAL-OV OBITS, run after every wound): a dead member's skills are gone, each post but the captain's that a
	// dead member holds goes to whichever post holder has the best skill for it, and a dead captain's command goes to whoever then holds the science officer's
	// post - returns true if the captain changed
	public bool ReassignPostsOfTheDead()
	{
		var captainBefore = m_captainFileId;

		// WF4C1: the posts after the captain's
		for ( var role = Role.ScienceOfficer; role < Role.Count; role++ )
		{
			var file = FindPersonnelFile( role );

			if ( ( file == null ) || !IsDeadFile( file ) )
			{
				continue;
			}

			// WF489: the dead member's skills are lost (so they are never the best holder)
			for ( var skillIndex = 0; skillIndex < 5; skillIndex++ )
			{
				file.SetSkill( skillIndex, 0 );
			}

			Assign( role, FindBestHolder( role ) );
		}

		// WF497: a dead captain
		var captain = FindPersonnelFile( Role.Captain );

		if ( ( captain != null ) && IsDeadFile( captain ) )
		{
			for ( var skillIndex = 0; skillIndex < 5; skillIndex++ )
			{
				captain.SetSkill( skillIndex, 0 );
			}

			Assign( Role.Captain, m_scienceOfficerFileId );
		}

		return m_captainFileId != captainBefore;
	}

	// a crew member takes an injury: their vitality falls, and at 0 they are dead - their posts go to the crew members best at them (ReassignPostsOfTheDead), and
	// with nobody left alive the game is lost - returns true if the injury killed them
	public bool InjureCrewMember( PD_Personnel.PD_PersonnelFile file, float damage )
	{
		if ( ( file == null ) || file.m_isDead )
		{
			return false;
		}

		file.m_vitality = Mathf.Max( 0.0f, file.m_vitality - damage );

		if ( file.m_vitality > 0.0f )
		{
			return false;
		}

		file.m_isDead = true;

		var messages = ( SpaceflightController.m_instance != null ) ? SpaceflightController.m_instance.m_messages : null;

		if ( messages != null )
		{
			messages.AddText( "<color=red>" + file.m_name + " is dead!</color>" );
		}

		// the dead member's posts go to the crew members best at them, and a dead captain's command to the science officer
		if ( ReassignPostsOfTheDead() )
		{
			var captain = FindPersonnelFile( Role.Captain );

			if ( ( messages != null ) && ( captain != null ) && !IsDeadFile( captain ) )
			{
				messages.AddText( "<color=yellow>" + captain.m_name + " takes command.</color>" );
			}
		}

		// with nobody left alive the game is lost
		if ( !HasLivingCrew() && ( CombatController.m_instance != null ) )
		{
			CombatController.m_instance.EndGame( "The entire crew has died." );
		}

		return true;
	}

	// true if a crew member on board is alive
	public bool HasLivingCrew()
	{
		for ( var role = Role.First; role < Role.Count; role++ )
		{
			if ( !IsAssigned( role ) )
			{
				continue;
			}

			var file = GetPersonnelFile( role );

			if ( ( file != null ) && !file.m_isDead && ( file.m_vitality > 0.0f ) )
			{
				return true;
			}
		}

		return false;
	}

	// return true if there is at least one (living) crew member of this race (by the race of the game data's crew race: an Elowan, a Thrynn, a Veloxi)
	public bool HasCrewOfRace( GameData.Race race )
	{
		var gameData = DataController.m_instance.m_gameData;

		for ( var role = Role.First; role < Role.Count; role++ )
		{
			if ( !IsAssigned( role ) )
			{
				continue;
			}

			var personnelFile = GetPersonnelFile( role );

			if ( ( personnelFile == null ) || ( personnelFile.m_vitality <= 0 ) || ( personnelFile.m_crewRaceId < 0 ) || ( personnelFile.m_crewRaceId >= gameData.m_crewRaceList.Length ) )
			{
				continue;
			}

			if ( gameData.m_crewRaceList[ personnelFile.m_crewRaceId ].m_race == race )
			{
				return true;
			}
		}

		return false;
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
