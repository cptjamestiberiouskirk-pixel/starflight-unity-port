
using UnityEngine;

using System;
using System.Collections.Generic;

[Serializable]

public class PD_Encounter : IComparable
{
	const float c_alienHyperspaceMoveSpeed = 32.0f;
	const float c_alienStarSystemMoveSpeed = 128.0f;

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

	// combat timer for alien attacks
	public float m_combatTimer;
	public bool m_inCombat;

	// set when the player fires on the aliens - they stay hostile until the player leaves the encounter (false in older save files)
	public bool m_attackedByPlayer;

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

	public void MoveTowards( Vector3 coordinates )
	{
		// get the move speed
		var moveSpeed = ( m_location == PD_General.Location.Hyperspace ) ? c_alienHyperspaceMoveSpeed : c_alienStarSystemMoveSpeed;

		// go torwards given coordinates
		m_currentCoordinates += Vector3.Normalize( coordinates - m_currentCoordinates ) * Time.deltaTime * moveSpeed;
	}

	public void GoHome()
	{
		MoveTowards( m_homeCoordinates );
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
