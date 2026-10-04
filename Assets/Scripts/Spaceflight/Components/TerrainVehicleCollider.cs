
using UnityEngine;

public class TerrainVehicleCollider : MonoBehaviour
{
	public TerrainVehicle m_terrainVehicle;

	void OnCollisionStay( Collision other )
	{
		if ( other.gameObject.CompareTag( "Terrain Vehicle" ) )
		{
			// a collision can be reported with no contact points, and the terrain vehicle is wired up in the editor
			if ( ( other.contactCount > 0 ) && ( m_terrainVehicle != null ) )
			{
				var contact = other.GetContact( 0 );

				m_terrainVehicle.AddPushBack( contact.normal, contact.separation );
			}
		}
	}
}
