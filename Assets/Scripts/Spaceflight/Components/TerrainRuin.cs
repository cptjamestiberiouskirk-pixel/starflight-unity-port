
using UnityEngine;

using System.Collections.Generic;

// a ruin on a planet's surface, and the messages of the original game that are found in it (GameData.m_planetMessageList, recovered from STRINFO 3.1)
public class TerrainRuin : MonoBehaviour
{
	// the planet messages found in this ruin (ids into the game data's planet message list)
	public List<int> m_messageIds = new List<int>();

	// the artifact sites in this ruin (ids into the game data's artifact site list, recovered from STRINFO 4.1)
	public List<int> m_artifactSiteIds = new List<int>();

	// what a random ruin holds (the 1984 design notes, planet.txt): an artifact (its id, and the value the crew estimates for it) or lumps of Endurium (its
	// volume, in tenths of a cubic meter), each known by its key in PD_PlanetSurfaces once taken
	public class RandomItem
	{
		public int m_key;
		public int m_artifactId = -1;
		public int m_estimatedValue;
		public int m_enduriumVolume;
	}

	public List<RandomItem> m_randomItems = new List<RandomItem>();

	// a random ruin is ancient (crystal, with Endurium or "truly amazing" artifacts) or of the Old Empire (with "amazing" artifacts)
	public bool m_isAncient;

	// the key of an item of a random ruin: the planet, the ruin's place among the planet's random ruins and the item's place in the ruin
	public static int GetRandomItemKey( int planetId, int ruinIndex, int itemIndex )
	{
		return planetId * 100 + ruinIndex * 10 + itemIndex;
	}

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
