
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TerrainVehicleDisplay : ShipDisplay
{
	// the status values text
	public TextMeshProUGUI m_statusValues;

	// the vitality names text
	public TextMeshProUGUI m_vitalityNames;

	// the vitality values text
	public TextMeshProUGUI m_vitalityValues;

	// the vitality bars
	public Image[] m_vitalityBars;

	PD_Personnel.PD_PersonnelFile[] m_personnelFiles;

	int m_numCrewInList;

	// for gizmo drawing
	Vector3[] m_debugVectors;

	public static readonly string[] c_cardinalDirections = { "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N" };

	TerrainVehicleDisplay()
	{
		m_debugVectors = new Vector3[ 2 ];
	}

	// the display label
	public override string GetLabel()
	{
		return "Status";
	}

	public override void Show()
	{
		base.Show();

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// (re)allocate the personnel file array
		m_personnelFiles = new PD_Personnel.PD_PersonnelFile[ (int) PD_CrewAssignment.Role.Count ];

		m_numCrewInList = 0;

		// go through each crew member
		for ( var i = PD_CrewAssignment.Role.First; i < PD_CrewAssignment.Role.Count; i++ )
		{
			var personnelFile = playerData.m_crewAssignment.GetPersonnelFile( i );

			bool alreadyThere = false;

			for ( var j = 0; j < m_numCrewInList; j++ )
			{
				if ( m_personnelFiles[ j ].m_fileId == personnelFile.m_fileId )
				{
					alreadyThere = true;
					break;
				}
			}

			if ( !alreadyThere )
			{
				m_personnelFiles[ m_numCrewInList++ ] = personnelFile;
			}
		}

		// update the crew member list
		m_vitalityNames.text = "";
		m_vitalityValues.text = "";

		for ( var i = 0; i < (int) PD_CrewAssignment.Role.Count; i++ )
		{
			if ( i < m_numCrewInList )
			{
				if ( i > 0 )
				{
					m_vitalityNames.text += "\n";
					m_vitalityValues.text += "\n";
				}

				m_vitalityNames.text += m_personnelFiles[ i ].m_name;
				m_vitalityValues.text += Mathf.CeilToInt( m_personnelFiles[ i ].m_vitality ) + "%";

				m_vitalityBars[ i ].enabled = true;
			}
			else
			{
				m_vitalityBars[ i ].enabled = false;
			}
		}
	}

	// what the status text was last built from (building it takes memory, and this runs every frame, so the text is only built again when one of these changes)
	string m_shownStardate;
	int m_shownPercentFuelRemaining = int.MinValue;
	int m_shownFuelEfficiency;
	int m_shownPercentFull;
	int m_shownDistanceInKm;
	int m_shownDirection;

	public override void Update()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// calculate ship coordinates
		var shipCoordinates = Tools.LatLongToWorldCoordinates( playerData.m_general.m_selectedLatitude, playerData.m_general.m_selectedLongitude );

		shipCoordinates = SpaceflightController.m_instance.m_disembarked.ApplyElevation( shipCoordinates, false );

		// calculate vector from TV to ship coordinates
		var vectorToShip = shipCoordinates - playerData.m_general.m_lastDisembarkedCoordinates;

		// how far is it in kilometers?
		var distanceInKm = vectorToShip.magnitude * 225.0f / 2048.0f - 2.0f;

		if ( distanceInKm < 0.0f )
		{
			distanceInKm = 0.0f;
		}

		// convert rotation to euler angles
		var eulerAngles = Quaternion.FromToRotation( Vector3.forward, vectorToShip ).eulerAngles;

		// convert euler angles to cardinal directions
		var index = Mathf.FloorToInt( ( eulerAngles.y - 22.5f ) / 45.0f ) + 1;

		// get the amount of fuel remaining as a percent
		var percentFuelRemaining = playerData.m_terrainVehicle.GetPercentFuelRemaining();

		// get the current terrain vehicle efficiency at this elevation (as a percent)
		var fuelEfficiency = Mathf.RoundToInt( SpaceflightController.m_instance.m_terrainVehicle.GetFuelEfficiency() * 100.0f );

		// get the amount of cargo space used as a percentage
		var percentFull = 100 - playerData.m_terrainVehicle.GetPercentRemainingVolume();

		// the distance as it is shown
		var roundedDistanceInKm = Mathf.RoundToInt( distanceInKm );

		// has anything the text shows changed since we last built it?
		if ( ( m_shownStardate != playerData.m_general.m_currentStardateDHMY ) || ( m_shownPercentFuelRemaining != percentFuelRemaining ) || ( m_shownFuelEfficiency != fuelEfficiency ) || ( m_shownPercentFull != percentFull ) || ( m_shownDistanceInKm != roundedDistanceInKm ) || ( m_shownDirection != index ) )
		{
			// yes - remember what the text is built from this time
			m_shownStardate = playerData.m_general.m_currentStardateDHMY;
			m_shownPercentFuelRemaining = percentFuelRemaining;
			m_shownFuelEfficiency = fuelEfficiency;
			m_shownPercentFull = percentFull;
			m_shownDistanceInKm = roundedDistanceInKm;
			m_shownDirection = index;

			// date and time
			var text = playerData.m_general.m_currentStardateDHMY + "\n";

			// the fuel that is left
			if ( percentFuelRemaining <= -3 )
			{
				text += "<color=yellow>None</color>\n";
			}
			else if ( percentFuelRemaining <= 0 )
			{
				text += "<color=red>Reserve</color>\n";
			}
			else
			{
				text += percentFuelRemaining + "%\n";
			}

			// the efficiency, the cargo, and the way back to the ship
			text += fuelEfficiency + "%\n";

			text += percentFull + "% Full\n";

			text += roundedDistanceInKm + " KM. " + c_cardinalDirections[ index ];

			// put the text on the display (it used to be put there five times a frame, a piece at a time)
			m_statusValues.text = text;
		}

		m_debugVectors[ 0 ] = shipCoordinates;
		m_debugVectors[ 1 ] = playerData.m_general.m_lastDisembarkedCoordinates;
	}

#if UNITY_EDITOR

	// draw gizmos to help debug the game
	void OnDrawGizmos()
	{
		Gizmos.color = Color.blue;

		for ( var i = 0; i < m_debugVectors.Length; i += 2 )
		{
			Gizmos.DrawLine( m_debugVectors[ i ], m_debugVectors[ i + 1 ] );
		}
	}

#endif
}
