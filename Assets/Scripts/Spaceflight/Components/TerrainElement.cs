
using UnityEngine;

// This component is added to spawned element objects on the planet surface.
// It tracks which element type this is and handles pickup by the terrain vehicle.
public class TerrainElement : MonoBehaviour
{
	// the element id (index into gameData.m_elementList)
	public int m_elementId;

	// how much this deposit contains, in tenths of a cubic meter (the unit the cargo holds count in)
	public int m_volume;

	// the planet, and the deposit by the order it was placed in - what has been taken from it is saved under these (PD_PlanetSurfaces)
	public int m_planetId;
	public int m_depositIndex;

	// reference to the terrain vehicle for pickup detection
	TerrainVehicle m_terrainVehicle;

	// true once this deposit has been picked up (the object stays around for a moment while its transporter effect plays)
	bool m_pickedUp;

	// the pickup distance threshold
	const float c_pickupDistance = 10.0f;

	// minimum volume per deposit, in cubic meters
	const int c_minVolume = 1;

	// maximum volume per deposit, in cubic meters
	const int c_maxVolume = 5;

	// the cargo holds count in tenths of a cubic meter
	const int c_tenthsPerCubicMeter = 10;

	// initialize this element
	public void Initialize( int elementId, TerrainVehicle terrainVehicle, int planetId, int depositIndex )
	{
		m_elementId = elementId;
		m_terrainVehicle = terrainVehicle;
		m_planetId = planetId;
		m_depositIndex = depositIndex;

		// random volume for this deposit - 1 to 5 cubic meters, kept in tenths like everything in the cargo holds
		// (still one random number per deposit - the objects of a planet are placed with random numbers seeded from the planet, and a second number here would move everything placed after it)
		m_volume = Random.Range( c_minVolume, c_maxVolume + 1 ) * c_tenthsPerCubicMeter;

		// has something been taken from this deposit before? (this comes after the random number, so that nothing placed after it moves)
		var playerData = ( DataController.m_instance != null ) ? DataController.m_instance.m_playerData : null;

		if ( ( playerData != null ) && ( playerData.m_planetSurfaces != null ) )
		{
			var volumeLeft = playerData.m_planetSurfaces.GetDepositVolumeLeft( m_planetId, m_depositIndex );

			if ( volumeLeft == 0 )
			{
				// yes - all of it: the deposit is gone
				m_volume = 0;
				m_pickedUp = true;

				gameObject.SetActive( false );
			}
			else if ( volumeLeft > 0 )
			{
				// yes - some of it: this is what is left
				m_volume = Mathf.Min( m_volume, volumeLeft );
			}
		}
	}

	// check if this element is close enough to the terrain vehicle to pick up
	public bool IsInPickupRange()
	{
		// a deposit that has been picked up is gone - only its transporter effect is still there
		if ( m_pickedUp )
		{
			return false;
		}

		// get the terrain vehicle dynamically if we don't have a reference
		if ( m_terrainVehicle == null )
		{
			m_terrainVehicle = ( SpaceflightController.m_instance != null ) ? SpaceflightController.m_instance.m_terrainVehicle : null;

			if ( m_terrainVehicle == null )
			{
				return false;
			}
		}

		// calculate distance to terrain vehicle
		var distance = Vector3.Distance( transform.position, m_terrainVehicle.transform.position );

		return distance <= c_pickupDistance;
	}

	// true once this deposit has been picked up (it is still there for a moment while its transporter effect plays)
	public bool HasBeenPickedUp()
	{
		return m_pickedUp;
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
		// a deposit can only be picked up once
		if ( m_pickedUp )
		{
			return 0;
		}

		m_pickedUp = true;

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
