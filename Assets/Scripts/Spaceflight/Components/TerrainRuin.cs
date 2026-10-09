
using UnityEngine;

using System.Collections.Generic;

// a ruin on a planet's surface, and the messages of the original game that are found in it (GameData.m_planetMessageList, recovered from STRINFO 3.1)
public class TerrainRuin : MonoBehaviour
{
	// the planet messages found in this ruin (ids into the game data's planet message list)
	public List<int> m_messageIds = new List<int>();

	// the artifact sites in this ruin (ids into the game data's artifact site list, recovered from STRINFO 4.1)
	public List<int> m_artifactSiteIds = new List<int>();

	// how close the terrain vehicle has to be to record what is in the ruin (a ruin is larger than a deposit)
	const float c_reachDistance = 20.0f;

	// true if the terrain vehicle is close enough to this ruin to record what is in it
	public bool IsInReach( Vector3 terrainVehiclePosition )
	{
		var delta = transform.position - terrainVehiclePosition;

		delta.y = 0.0f;

		return delta.magnitude <= c_reachDistance;
	}
}
