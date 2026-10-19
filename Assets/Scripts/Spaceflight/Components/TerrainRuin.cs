
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

	// the special artifacts shown beside the ruin while it holds them (TerrainRuins.ShowArtifacts)
	public List<GameObject> m_artifactDisplays = new List<GameObject>();

	// the key of an item of a ruin of an ancient ruin site: the planet, the ruin's place among the planet's ruins (5 on: TerrainRuins.IsSiteRuinIndex) and the
	// item's place in the ruin - the random ruins of saves from before roadmap 8.32 used places 0 and 1
	public static int GetRandomItemKey( int planetId, int ruinIndex, int itemIndex )
	{
		return planetId * 100 + ruinIndex * 10 + itemIndex;
	}

	// the keys of the items of the random ruins of the regions (RandomRuinRegions) start here, above every key of the form above (811 planets * 100)
	const int c_firstRegionItemKey = 1000000000;

	// the key of an item of a random ruin of a region: the planet, the region, the kind of ruin (0 ancient, 1 recent) and the item's place in the ruin (it fits
	// an int: 811 planets * 4096 regions * 2 kinds * 10 items is 66,437,120)
	public static int GetRegionItemKey( int planetId, int region, int kind, int itemIndex )
	{
		return c_firstRegionItemKey + ( ( planetId * RandomRuinRegions.c_columns * RandomRuinRegions.c_rows + region ) * 2 + kind ) * 10 + itemIndex;
	}

	// the planet of an item's key, of either form
	public static int GetRandomItemPlanetId( int itemKey )
	{
		if ( itemKey >= c_firstRegionItemKey )
		{
			return ( itemKey - c_firstRegionItemKey ) / 10 / 2 / ( RandomRuinRegions.c_columns * RandomRuinRegions.c_rows );
		}

		return itemKey / 100;
	}

	// true if an item's key is of a ruin of an ancient ruin site (its Endurium is taken once: the site is not seeded again)
	public static bool IsSiteRuinItemKey( int itemKey )
	{
		return ( itemKey < c_firstRegionItemKey ) && TerrainRuins.IsSiteRuinIndex( ( itemKey / 10 ) % 10 );
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
