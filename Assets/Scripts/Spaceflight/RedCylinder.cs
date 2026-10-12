using System.Collections.Generic;
using UnityEngine;

// the Red Cylinder, the "ancient-ruin locater device" (STRINFO: "the Red Cylinder ancient-ruin locater device"; the Starport's analysis: "an orbital scanner of
// some sort") - the shipped game says "THE RED CYLINDER IS FLASHING" in the code of its landing map (STARB.COM, beside the site and region words), so the port
// has it flash while the crosshair of the landing map is near an ancient ruin of the planet: one of its random ancient ruins (worked out from orbit as the surface
// places them) or a formation of ancient ruins - how near is the port's
public static class RedCylinder
{
	// how near the crosshair has to be, in degrees
	public const float c_flashRange = 8.0f;

	// the ancient ruins of the planet last looked at
	static int s_planetId = -1;
	static List<Vector2> s_places;

	// whether it was flashing at the last look (so that the message comes once each time the crosshair comes near)
	static bool s_flashing;

	// the landing map has opened: the next look reports afresh
	public static void Reset()
	{
		s_flashing = false;
	}

	// looks at the crosshair (east-west and north-south, in degrees): returns true if the Red Cylinder is flashing, and says so when it begins
	public static bool Look( float eastWest, float northSouth )
	{
		var near = IsNearAncientRuin( eastWest, northSouth );

		if ( near && !s_flashing && ( SpaceflightController.m_instance != null ) )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>The Red Cylinder is flashing.</color>" );
		}

		s_flashing = near;

		return near;
	}

	// true if the ship carries the Red Cylinder and this place is near an ancient ruin of the planet it orbits
	public static bool IsNearAncientRuin( float eastWest, float northSouth )
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		if ( !playerData.m_playerShip.HasArtifact( "Red Cylinder" ) || ( SpaceflightController.m_instance == null ) )
		{
			return false;
		}

		var planetId = playerData.m_general.m_currentPlanetId;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) )
		{
			return false;
		}

		if ( ( s_places == null ) || ( s_planetId != planetId ) )
		{
			var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( planetId );

			// (the random ruins are worked out from the planet's maps)
			if ( ( planetController == null ) || !planetController.HasMaps() )
			{
				return false;
			}

			var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
			var elevationScale = ( terrainGrid != null ) ? terrainGrid.m_elevationScale : 100.0f;

			s_places = TerrainRuins.GetAncientRuinPlaces( planetController.GetPlanetGenerator(), elevationScale, gameData.m_planetList[ planetId ] );
			s_planetId = planetId;
		}

		foreach ( var place in s_places )
		{
			// (east-west goes round the planet)
			var eastWestApart = Mathf.Abs( Mathf.DeltaAngle( place.x, eastWest ) );

			if ( ( eastWestApart <= c_flashRange ) && ( Mathf.Abs( place.y - northSouth ) <= c_flashRange ) )
			{
				return true;
			}
		}

		return false;
	}
}
