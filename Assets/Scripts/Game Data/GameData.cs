
using System;

[Serializable]

public class GameData
{
	public enum Race
	{
		None = 0,
		Elowan = 1,
		Gazurtoid = 2,
		Mechan = 3,
		Mysterion = 4,
		NomadProbe = 5,
		Spemin = 6,
		Thrynn = 7,
		Velox = 8,
		Uhlek = 9,
		VeloxProbe = 10,
		Minstrel = 11,
		Human = 16,
		TheEnterprise = 18,
		NoahDerelict = 19,
		Debris = 20,
		InterstelPolice = 21,
		Android = 255,
	}

	public GD_Misc m_misc;
	public GD_Notice[] m_noticeList;
	public GD_CrewRace[] m_crewRaceList;
	public GD_Engines[] m_enginesList;
	public GD_Shielding[] m_shieldingList;
	public GD_Armor[] m_armorList;
	public GD_MissileLauncher[] m_missileLauncherList;
	public GD_LaserCannon[] m_laserCannonList;
	public GD_Artifact[] m_artifactList;
	public GD_Element[] m_elementList;
	public GD_Star[] m_starList;
	public GD_Planet[] m_planetList;
	public GD_PlanetType[] m_planetTypeList;
	public GD_Atmosphere[] m_atmosphereList;
	public GD_AtmosphereDensity[] m_atmosphereDensityList;
	public GD_Hydrosphere[] m_hydrosphereList;
	public GD_Surface[] m_surfaceList;
	public GD_Temperature[] m_temperatureList;
	public GD_Weather[] m_weatherList;
	public GD_Flux[] m_fluxList;
	public GD_Territory[] m_territoryList;
	public GD_Nebula[] m_nebulaList;
	public GD_SpectralClass[] m_spectralClassList;
	public GD_Encounter[] m_encounterList;
	public GD_Vessel[] m_vesselList;
	public GD_Comm[] m_commList;
	public GD_Garble[] m_garbleList;

	// what the original had and the game data file does not - these come from the recovered data file (see AddRecoveredData), never from the game data file
	[NonSerialized] public GD_PlanetMessage[] m_planetMessageList = new GD_PlanetMessage[ 0 ];
	[NonSerialized] public GD_ArtifactSite[] m_artifactSiteList = new GD_ArtifactSite[ 0 ];
	[NonSerialized] public GD_ColonyEvaluation[] m_colonyEvaluationList = new GD_ColonyEvaluation[ 0 ];
	[NonSerialized] public GD_StoryText[] m_storyTextList = new GD_StoryText[ 0 ];

	public void Initialize()
	{
		// go through each planet
		foreach ( GD_Planet planet in m_planetList )
		{
			// initialize it
			planet.Initialize();
		}

		// go through each star
		foreach ( GD_Star star in m_starList )
		{
			// initialize it
			star.Initialize( this );
		}

		// go through each flux
		foreach ( GD_Flux flux in m_fluxList )
		{
			// initialize it
			flux.Initialize();
		}

		// go through each territory
		foreach ( GD_Territory territory in m_territoryList )
		{
			// initialize it
			territory.Initialize();
		}

		// go through each nebula
		foreach ( GD_Nebula nebula in m_nebulaList )
		{
			// initialize it
			nebula.Initialize();
		}

		// find the planets and the artifacts the recovered data names (this needs the stars to be initialized)
		ResolveRecoveredData();
	}

	// finds the planets and the artifacts the recovered data names - a record that names something the game data does not have is logged and keeps a negative id
	void ResolveRecoveredData()
	{
		foreach ( var planetMessage in m_planetMessageList )
		{
			planetMessage.m_planetId = FindPlanetFromSun( planetMessage.m_starX, planetMessage.m_starY, planetMessage.m_planetFromSun );

			if ( planetMessage.m_planetId < 0 )
			{
				UnityEngine.Debug.LogError( "Recovered planet message " + planetMessage.m_id + " names a planet the game data does not have" );
			}
		}

		foreach ( var artifactSite in m_artifactSiteList )
		{
			artifactSite.m_planetId = FindPlanetFromSun( artifactSite.m_starX, artifactSite.m_starY, artifactSite.m_planetFromSun );
			artifactSite.m_artifactId = FindArtifactId( artifactSite.m_artifactName );

			if ( ( artifactSite.m_planetId < 0 ) || ( artifactSite.m_artifactId < 0 ) )
			{
				UnityEngine.Debug.LogError( "Recovered artifact site " + artifactSite.m_id + " names a planet or an artifact the game data does not have" );
			}
		}

		foreach ( var colonyEvaluation in m_colonyEvaluationList )
		{
			colonyEvaluation.m_planetId = FindPlanetFromSun( colonyEvaluation.m_starX, colonyEvaluation.m_starY, colonyEvaluation.m_planetFromSun );

			if ( colonyEvaluation.m_planetId < 0 )
			{
				UnityEngine.Debug.LogError( "Recovered colony evaluation " + colonyEvaluation.m_id + " names a planet the game data does not have" );
			}
		}
	}

	// this finds a planet the way STRINFO names it: the star by its coordinates and the planet by its number counted from the sun (1 is the planet nearest to the sun) - returns -1 if there is no such planet
	public int FindPlanetFromSun( int starX, int starY, int planetFromSun )
	{
		foreach ( var star in m_starList )
		{
			if ( ( star.m_xCoordinate != starX ) || ( star.m_yCoordinate != starY ) )
			{
				continue;
			}

			// the planets of a star are kept by orbit, from the sun outward (an empty orbit has no planet)
			var planetList = star.GetPlanetList();
			var count = 0;

			for ( var orbitIndex = 0; orbitIndex < planetList.Length; orbitIndex++ )
			{
				if ( planetList[ orbitIndex ] == null )
				{
					continue;
				}

				count++;

				if ( count == planetFromSun )
				{
					return planetList[ orbitIndex ].m_id;
				}
			}

			return -1;
		}

		return -1;
	}

	// this finds the artifact in the list by its name - returns -1 if there is none
	public int FindArtifactId( string name )
	{
		for ( var artifactId = 0; artifactId < m_artifactList.Length; artifactId++ )
		{
			if ( m_artifactList[ artifactId ].m_name == name )
			{
				return artifactId;
			}
		}

		return -1;
	}

	// this finds a story text by its key - returns null if there is none
	public GD_StoryText FindStoryText( string key )
	{
		foreach ( var storyText in m_storyTextList )
		{
			if ( storyText.m_key == key )
			{
				return storyText;
			}
		}

		return null;
	}

	// this adds what was recovered from the other sources of the original game (STRINFO) to the game data - call it before Initialize
	public void AddRecoveredData( RecoveredData recoveredData )
	{
		// nothing to add if there is no recovered data
		if ( recoveredData == null )
		{
			return;
		}

		// add the recovered comms whose id is not taken
		if ( recoveredData.m_commList != null )
		{
			// collect the comm ids the game data already has
			var commIds = new System.Collections.Generic.HashSet<int>();

			foreach ( var comm in m_commList )
			{
				commIds.Add( comm.m_id );
			}

			var commList = new System.Collections.Generic.List<GD_Comm>( m_commList );

			foreach ( var comm in recoveredData.m_commList )
			{
				if ( commIds.Add( comm.m_id ) )
				{
					commList.Add( comm );
				}
				else
				{
					UnityEngine.Debug.LogError( "Recovered comm " + comm.m_id + " has the id of a comm the game data already has - it is left out" );
				}
			}

			m_commList = commList.ToArray();
		}

		// the records the game data file has nothing of (a list the recovered data file does not have stays empty)
		if ( recoveredData.m_planetMessageList != null )
		{
			m_planetMessageList = recoveredData.m_planetMessageList;
		}

		if ( recoveredData.m_artifactSiteList != null )
		{
			m_artifactSiteList = recoveredData.m_artifactSiteList;
		}

		if ( recoveredData.m_colonyEvaluationList != null )
		{
			m_colonyEvaluationList = recoveredData.m_colonyEvaluationList;
		}

		if ( recoveredData.m_storyTextList != null )
		{
			m_storyTextList = recoveredData.m_storyTextList;
		}
	}

	// this finds the element in the list by its name
	public int FindElementId( string name )
	{
		for ( int elementId = 0; elementId < m_elementList.Length; elementId++ )
		{
			if ( m_elementList[ elementId ].m_name == name )
			{
				return elementId;
			}
		}

		return -1;
	}
}
