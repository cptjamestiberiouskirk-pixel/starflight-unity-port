using System.Collections.Generic;
using UnityEngine;

// the Red Cylinder, the "ancient-ruin locater device" (STRINFO: "the Red Cylinder ancient-ruin locater device"; the Starport's analysis: "an orbital scanner of
// some sort") - in the shipped game, when the landing map opens with the Red Cylinder aboard, it flashes the planet's message and artifact sites on the map, up to
// ten, and says "THE RED CYLINDER IS FLASHING" (SITE-OV .MERCATOR, WF389, WF290); the ancient ruins it finds from orbit are marked with the survey's marks
// (SurveyMarks, decided 2026-10-17, ROADMAP D6)
public static class RedCylinder
{
	// the most sites it flashes (the shipped game keeps ten, SITE-OV WF261), and for how long, in seconds (the shipped game cycles their colours 80 times)
	public const int c_maxSites = 10;
	public const float c_flashSeconds = 3.0f;

	// the ancient ruins of the planet last looked at
	static int s_planetId = -1;
	static List<Vector2> s_places;

	// none (what GetAncientRuinPlaces returns without the Red Cylinder)
	static readonly List<Vector2> s_none = new List<Vector2>();

	// the sites flashing on the landing map, and until when
	static List<Vector2> s_sites = new List<Vector2>();
	static float s_flashUntil;

	// the landing map has opened: with the Red Cylinder aboard, the planet's message and artifact sites flash on it, and the Red Cylinder says so
	public static void MapOpened()
	{
		s_sites = GetFlashingSites();
		s_flashUntil = ( s_sites.Count > 0 ) ? Time.time + c_flashSeconds : 0.0f;

		if ( ( s_sites.Count > 0 ) && ( SpaceflightController.m_instance != null ) )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>The Red Cylinder is flashing.</color>" );
		}
	}

	// true while the sites flash on the landing map
	public static bool IsFlashing()
	{
		return ( s_sites.Count > 0 ) && ( Time.time < s_flashUntil );
	}

	/// <summary>
	/// The sites flashing on the landing map (east-west and north-south, in degrees). The list is kept: do not change it.
	/// </summary>
	public static List<Vector2> GetSites()
	{
		return s_sites;
	}

	// the planet's message sites and the sites of the artifacts still lying there (SITE-OV WF389: the planet's messages and artifacts that have a place), up to ten,
	// if the ship carries the Red Cylinder - none without it (a message placed at random spots is left out: the port draws its spot on the surface)
	public static List<Vector2> GetFlashingSites()
	{
		var sites = new List<Vector2>();
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		if ( !playerData.m_playerShip.HasArtifact( "Red Cylinder" ) )
		{
			return sites;
		}

		var planetId = playerData.m_general.m_currentPlanetId;

		foreach ( var planetMessage in gameData.m_planetMessageList )
		{
			if ( ( planetMessage.m_planetId == planetId ) && ( planetMessage.m_placement == "Site" ) )
			{
				AddSite( sites, planetMessage.m_longitude, planetMessage.m_latitude );
			}
		}

		foreach ( var artifactSite in gameData.m_artifactSiteList )
		{
			if ( ( artifactSite.m_planetId == planetId ) && ( ( playerData.m_planetSurfaces == null ) || !playerData.m_planetSurfaces.IsArtifactSiteTaken( artifactSite.m_id ) ) )
			{
				AddSite( sites, artifactSite.m_longitude, artifactSite.m_latitude );
			}
		}

		return sites;
	}

	// adds a site once (a message and an artifact can lie at one), while there is room for it
	static void AddSite( List<Vector2> sites, int eastWest, int northSouth )
	{
		var place = new Vector2( eastWest, northSouth );

		if ( ( sites.Count < c_maxSites ) && !sites.Contains( place ) )
		{
			sites.Add( place );
		}
	}

	/// <summary>
	/// The ancient ruins of the planet the ship orbits (east-west and north-south, in degrees) if the ship carries the Red Cylinder, none without it (the
	/// landing map marks them, decided 2026-10-17, ROADMAP D6). The list is kept: do not change it.
	/// </summary>
	public static List<Vector2> GetAncientRuinPlaces()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		if ( !playerData.m_playerShip.HasArtifact( "Red Cylinder" ) || ( SpaceflightController.m_instance == null ) )
		{
			return s_none;
		}

		var planetId = playerData.m_general.m_currentPlanetId;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) )
		{
			return s_none;
		}

		if ( ( s_places == null ) || ( s_planetId != planetId ) )
		{
			var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( planetId );

			// (the random ruins are worked out from the planet's maps)
			if ( ( planetController == null ) || !planetController.HasMaps() )
			{
				return s_none;
			}

			var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
			var elevationScale = ( terrainGrid != null ) ? terrainGrid.m_elevationScale : 100.0f;

			s_places = TerrainRuins.GetAncientRuinPlaces( planetController.GetPlanetGenerator(), elevationScale, gameData.m_planetList[ planetId ] );
			s_regions = RandomRuinRegions.GetAncientRegions( planetController.GetPlanetGenerator(), elevationScale, gameData.m_planetList[ planetId ] );
			s_planetId = planetId;
		}

		return s_places;
	}

	// the regions of the planet last looked at that hold a random ancient ruin
	static List<int> s_regions;

	// none (what GetAncientRuinRegions returns without the Red Cylinder)
	static readonly List<int> s_noRegions = new List<int>();

	/// <summary>
	/// The regions of the planet the ship orbits that hold a random ancient ruin (RandomRuinRegions) if the ship carries the Red Cylinder, none without it:
	/// the landing map tints them (decided 2026-10-19). The list is kept: do not change it.
	/// </summary>
	public static List<int> GetAncientRuinRegions()
	{
		// (worked out with the places)
		if ( ( GetAncientRuinPlaces() == s_none ) || ( s_regions == null ) )
		{
			return s_noRegions;
		}

		return s_regions;
	}
}
