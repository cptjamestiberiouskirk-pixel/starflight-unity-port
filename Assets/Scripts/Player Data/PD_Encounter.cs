
using UnityEngine;

using System;
using System.Collections.Generic;

[Serializable]

public class PD_Encounter : IComparable
{
	// these fields must be public for JsonUtility serialization
	public int m_encounterId;
	public PD_General.Location m_location;
	public int m_starId;
	public Vector3 m_homeCoordinates;
	public Vector3 m_currentCoordinates;
	public PD_AlienShip[] m_alienShipList;
	public float m_currentDistance;

	// cached reference to game data (not serialized)
	[NonSerialized] GD_Encounter m_encounter;

	public List<int> m_shownCommList;
	public bool m_connected;
	public bool m_disconnected;
	public float m_connectionTimer;
	public bool m_aliensWantToConnect;
	public bool m_playerWantsToConnect;
	public GD_Comm.Subject m_lastSubjectFromPlayer;
	public int m_lastQuestionFromAliens;
	public bool m_alreadyScanned;
	public float m_scanTimer;
	public float m_conversationTimer;
	public GD_Comm.Stance m_alienStance;
	public GD_Comm.Stance m_playerStance;

	public int m_questionLikelihood;
	public int m_numCorrectAnswers;
	public bool m_mechan9NoHumansWarningDone;

	public bool m_inCombat;

	// the turns of the encounter, as the original's (COMBAT-OV COMBAT): whose turn the last pass was (0 the player's ship, then each alien ship's slot plus one, ^VES),
	// the aliens' fire latch (WDE55: on once an alien has fired, and off again whenever their posture is neutral or calmer), the seconds since an alien last fired
	// (WE03D), and whether the comm link has been terminated since the last hail (TERMINA) - 0 and false in older save files
	public int m_combatTurn;
	public bool m_aliensOpenedFire;
	public float m_secondsSinceAlienFire;
	public bool m_commTerminated;

	// set when the player fires on the aliens - they stay hostile until the player leaves the encounter (false in older save files)
	public bool m_attackedByPlayer;

	// the subjects the aliens will say no more about in this encounter, a bit each (1 << subject): the Old Empire and the Ancients can lock out
	// (Encounter.IsLockedOut, the original's (LOCK-OUT)) - 0 in older save files
	public int m_lockedOutSubjects;

	// how many of their homeworld's warnings a home fleet has given, and whether it has given its last one (it attacks then) - 0 and false in older save files
	public int m_homeworldWarningCount;
	public bool m_homeworldWarningsGiven;

	// how many lines a home fleet has said from each of its homeworld lists: its hails, its responses and its refusals (null in older save files)
	public int[] m_homeworldWarningCounts;

	// set once the aliens have lost some of their ships and act cowardly (the Spemin, the Thrynn and the Veloxi) - false in older save files
	public bool m_aliensAreCowardly;

	// the race's special actions as a comm link opens (Encounter.SpecialAction, COMMSPEC-OV DO-SA and XY/N): the next one to run, counted from 1 (0 once they have
	// all run or one has stopped them), the one waiting for the crew's answer (0 for none) and how much longer it waits, in seconds - 0 in older save files
	public int m_specialActionStep;
	public int m_specialActionAsked;
	public float m_specialActionTimer;

	// what the special action waiting for the answer offers or demands: the Endurium, in tenths of a cubic meter, and the artifact the Thrynn offer to buy (an id
	// into the game data's artifact list) - 0 in older save files
	public int m_specialActionAmount;
	public int m_specialActionArtifactId;

	// the aliens' mood (the original's "emotional disposition level", from 0 most calm to 100 most agitated, see AlienPersonality) and whether it has been set for
	// this encounter - 0 and false in older save files, and the mood is set again then
	public float m_mood;
	public bool m_moodSet;

	// how many phrases the aliens have said in this comm link (#A-PHRASES), and how many they say before they are tired of talking (#A-PHRASES-LIMIT, drawn again
	// whenever their mood changes) - 0 in older save files, and the limit is drawn then
	public int m_alienPhrases;
	public int m_alienPhraseLimit;

	// what moves the aliens' mood in the shipped game (COMM-OV <EDL>): how many phrases the player has made in this comm link (the reply to a hail, statements and
	// questions: P-PHRASES), whether the player hailed first (HAILER), the postures the player has hailed or spoken in after hailing first, as posture bits (P-POSTURES),
	// and how many times the aliens have hailed without a reply (#A-HAILS) - 0 and false in older save files
	public int m_playerPhrases;
	public bool m_playerHailedFirst;
	public int m_playerPostures;
	public int m_alienHails;

	// set while the aliens' distress call has not been answered yet: more of their ships are on the way (the original's ?CALLED; false in older save files)
	public bool m_distressCalled;

	public void Reset( int encounterId )
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// remember the encounter id
		m_encounterId = encounterId;

		// get access to the encounter
		m_encounter = gameData.m_encounterList[ encounterId ];

		// put the encounter where the game data says it is
		PlaceAtHome();

		// allocate and initialize each of the alien ships in the encounter
		m_alienShipList = new PD_AlienShip[ m_encounter.m_maxNumShips ];

		for ( var i = 0; i < m_alienShipList.Length; i++ )
		{
			var alienShip = new PD_AlienShip();

			alienShip.Initialize( m_encounter );

			m_alienShipList[ i ] = alienShip;
		}

		// allocate the shown comm list
		m_shownCommList = new List<int>();

		// initialize the encounter distance
		m_currentDistance = float.MaxValue;
	}

	// the location the game data gives this encounter (translated) - an unknown value leaves the location as it is
	PD_General.Location GetLocationInGameData()
	{
		switch ( m_encounter.m_location )
		{
			case 0: return PD_General.Location.Hyperspace;
			case 1: return PD_General.Location.StarSystem;
			case 2: return PD_General.Location.InOrbit;
		}

		return m_location;
	}

	// sets the location, the star and the home coordinates of this encounter from the game data, and puts it at home
	void PlaceAtHome()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// set the location (translated)
		m_location = GetLocationInGameData();

		// a hyperspace encounter is at no star
		m_starId = 0;

		if ( m_location != PD_General.Location.Hyperspace )
		{
			foreach ( var star in gameData.m_starList )
			{
				if ( star.m_xCoordinate == m_encounter.m_xCoordinate )
				{
					if ( star.m_yCoordinate == m_encounter.m_yCoordinate )
					{
						m_starId = star.m_id;
						break;
					}
				}
			}
		}

		// set the home position
		if ( m_location == PD_General.Location.Hyperspace )
		{
			m_homeCoordinates = Tools.GameToWorldCoordinates( new Vector3( m_encounter.m_xCoordinate, 0.0f, m_encounter.m_yCoordinate ) );
		}
		else if ( m_location == PD_General.Location.StarSystem )
		{
			var randomPosition = UnityEngine.Random.insideUnitCircle * ( 8192.0f - 512.0f );

			m_homeCoordinates = new Vector3( randomPosition.x, 0.0f, randomPosition.y );
		}
		else
		{
			m_homeCoordinates = Vector3.zero;
		}

		// set the current coordinates to be at home
		m_currentCoordinates = m_homeCoordinates;
	}

	// puts a group in a star system at a random place of the system, its home there too, as the original's SETRNDXY (HYPER-OV): each axis RRND(-395, 395) of the
	// system's 400 units (SYSRADIUS less 5), the original's system being a square; a place in its corners, outside the port's round system, is brought in to 395
	// from the middle along its direction
	public void PlaceAtRandomInStarSystem()
	{
		var place = new Vector3( UnityEngine.Random.Range( -395, 395 ), 0.0f, UnityEngine.Random.Range( -395, 395 ) );

		if ( place.magnitude > 395.0f )
		{
			place = place.normalized * 395.0f;
		}

		m_homeCoordinates = place * ( 8192.0f / 400.0f );
		m_currentCoordinates = m_homeCoordinates;
	}

	// repair save files written before the game data moved this encounter (encounters 144 to 146 were in a star system, the original has them in hyperspace) - its ships and what was said are kept
	public bool ValidateLocation()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// an encounter the game data does not have is left alone
		if ( ( m_encounterId < 0 ) || ( m_encounterId >= gameData.m_encounterList.Length ) )
		{
			return false;
		}

		// get access to the encounter
		m_encounter = gameData.m_encounterList[ m_encounterId ];

		// nothing to do if the save has it where the game data has it
		if ( GetLocationInGameData() == m_location )
		{
			return false;
		}

		// move it to where the game data has it
		PlaceAtHome();

		return true;
	}

	public PD_General.Location GetLocation()
	{
		return m_location;
	}

	public int GetStarId()
	{
		return m_starId;
	}

	public int CompareTo( object obj )
	{
		if ( obj is PD_Encounter )
		{
			int compareTo = m_currentDistance.CompareTo( ( obj as PD_Encounter ).m_currentDistance );

			if ( compareTo == 0 )
			{
				return ( obj as PD_Encounter ).m_currentDistance.CompareTo( m_currentDistance );
			}
			else
			{
				return compareTo;
			}
		}

		throw new ArgumentException( "Object is not a PD_Encounter" );
	}

	public float CalculateDistance( Vector3 coordinates )
	{
		m_currentDistance = Vector3.Distance( coordinates, m_currentCoordinates );

		return m_currentDistance;
	}

	public void SetDistance( float distance )
	{
		m_currentDistance = distance;
	}

	public float GetDistance()
	{
		return m_currentDistance;
	}

	public void SetCoordinates( Vector3 coordinates )
	{
		m_currentCoordinates = coordinates;
	}

	public PD_AlienShip[] GetAlienShipList()
	{
		return m_alienShipList;
	}

	// returns true if this encounter still has at least one living alien ship (whether or not it has shown up in the encounter yet)
	public bool HasLivingAlienShips()
	{
		// an encounter that has not been set up yet still has all of its ships
		if ( m_alienShipList == null )
		{
			return true;
		}

		foreach ( var alienShip in m_alienShipList )
		{
			if ( !alienShip.m_isDead )
			{
				return true;
			}
		}

		return false;
	}
}
