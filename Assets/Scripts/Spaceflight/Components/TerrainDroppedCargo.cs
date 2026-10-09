
using UnityEngine;

// something the terrain vehicle has dropped on a planet's surface - it can be picked up again with the cargo button (the manual, page 21)
public class TerrainDroppedCargo : MonoBehaviour
{
	// what was dropped (an id into PD_PlanetSurfaces.m_droppedCargoList)
	public int m_droppedCargoId;

	// how close the terrain vehicle has to be to pick it up again (as for a deposit)
	const float c_reachDistance = 10.0f;

	// true if the terrain vehicle is close enough to pick it up
	public bool IsInReach( Vector3 terrainVehiclePosition )
	{
		var delta = transform.position - terrainVehiclePosition;

		delta.y = 0.0f;

		return delta.magnitude <= c_reachDistance;
	}
}
