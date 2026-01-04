
using UnityEngine;

// This component is added to spawned artifact objects on the planet surface.
// It tracks which artifact type this is and handles pickup by the terrain vehicle.
public class TerrainArtifact : MonoBehaviour
{
	// the artifact id (index into gameData.m_artifactList)
	public int m_artifactId;

	// reference to the terrain vehicle for pickup detection
	TerrainVehicle m_terrainVehicle;

	// the pickup distance threshold
	const float c_pickupDistance = 10.0f;

	// initialize this artifact
	public void Initialize( int artifactId, TerrainVehicle terrainVehicle )
	{
		m_artifactId = artifactId;
		m_terrainVehicle = terrainVehicle;
	}

	// check if this artifact is close enough to the terrain vehicle to pick up
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

	// get the name of this artifact
	public string GetArtifactName()
	{
		var gameData = DataController.m_instance.m_gameData;

		if ( m_artifactId >= 0 && m_artifactId < gameData.m_artifactList.Length )
		{
			return gameData.m_artifactList[ m_artifactId ].m_name;
		}

		return "Unknown Artifact";
	}

	// call this when the artifact is picked up
	public void Pickup()
	{
		// hide any label on this object
		var label = GetComponent<TerrainObjectLabel>();
		if ( label != null )
		{
			label.HideLabel();
		}

		// add transporter effect and destroy when complete
		var effect = gameObject.AddComponent<TransporterEffect>();
		effect.Play();
	}
}
