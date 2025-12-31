
using UnityEngine;

// This component is added to spawned element objects on the planet surface.
// It tracks which element type this is and handles pickup by the terrain vehicle.
public class TerrainElement : MonoBehaviour
{
	// the element id (index into gameData.m_elementList)
	public int m_elementId;

	// how many cubic meters this deposit contains
	public int m_volume;

	// reference to the terrain vehicle for pickup detection
	TerrainVehicle m_terrainVehicle;

	// the pickup distance threshold
	const float c_pickupDistance = 10.0f;

	// minimum volume per deposit
	const int c_minVolume = 1;

	// maximum volume per deposit
	const int c_maxVolume = 5;

	// initialize this element
	public void Initialize( int elementId, TerrainVehicle terrainVehicle )
	{
		m_elementId = elementId;
		m_terrainVehicle = terrainVehicle;

		// random volume for this deposit
		m_volume = Random.Range( c_minVolume, c_maxVolume + 1 );
	}

	// check if this element is close enough to the terrain vehicle to pick up
	public bool IsInPickupRange()
	{
		// get the terrain vehicle dynamically if we don't have a reference
		if ( m_terrainVehicle == null )
		{
			m_terrainVehicle = SpaceflightController.m_instance?.m_terrainVehicle;

			if ( m_terrainVehicle == null )
			{
				return false;
			}
		}

		// calculate distance to terrain vehicle
		var distance = Vector3.Distance( transform.position, m_terrainVehicle.transform.position );

		return distance <= c_pickupDistance;
	}

	// get the name of this element
	public string GetElementName()
	{
		var gameData = DataController.m_instance.m_gameData;

		if ( m_elementId >= 0 && m_elementId < gameData.m_elementList.Length )
		{
			return gameData.m_elementList[ m_elementId ].m_name;
		}

		return "Unknown";
	}

	// call this when the element is picked up - returns the volume that was picked up
	public int Pickup()
	{
		var volumePickedUp = m_volume;

		// hide any label on this object
		var label = GetComponent<TerrainObjectLabel>();
		if ( label != null )
		{
			label.HideLabel();
		}

		// add transporter effect and destroy when complete
		var effect = gameObject.AddComponent<TransporterEffect>();
		effect.Play();

		return volumePickedUp;
	}
}
