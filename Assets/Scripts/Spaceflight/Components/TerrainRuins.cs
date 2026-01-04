
using UnityEngine;

public class TerrainRuins : TerrainGridPopulator
{
	// the ruin templates
	public GameObject[] m_ruinTemplates;

	// the artifact templates
	public GameObject[] m_artifactTemplates;

	// the maximum number of ruins we can place
	public int m_maxNumRuins = 5;

	// reference to the terrain vehicle for artifact pickup
	public TerrainVehicle m_terrainVehicle;

	// populate this planet with ruins and artifacts
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
		// get to this planet
		var planet = planetGenerator.GetPlanet();

		// calculate the number of ruins to place (based on mineral density as a proxy for "interestingness")
		var numRuins = ( planet.m_mineralDensity * m_maxNumRuins ) / 100;

		// always place at least one ruin if mineral density is high enough
		if ( numRuins == 0 && planet.m_mineralDensity > 50 )
		{
			numRuins = 1;
		}

		if ( numRuins > 0 )
		{
			// place ruins
			InitializeWithCallback( elevationScale, m_ruinTemplates, numRuins, randomSeed, true, 1.0f, 1.0f, OnRuinSpawned );
		}
	}

	// callback for when a ruin object is spawned
	void OnRuinSpawned( GameObject spawnedObject, int templateIndex )
	{
		// ruins might have artifacts inside them
		if ( Random.Range( 0, 100 ) < 50 )
		{
			// pick a random artifact template
			int artifactIndex = Random.Range( 0, m_artifactTemplates.Length );
			GameObject artifactTemplate = m_artifactTemplates[ artifactIndex ];

			// spawn the artifact near the ruin
			Vector3 artifactPosition = spawnedObject.transform.position + spawnedObject.transform.forward * 5.0f;
			GameObject artifact = Instantiate( artifactTemplate, artifactPosition, spawnedObject.transform.rotation, transform );

			// add the TerrainArtifact component (assuming it exists or will be created)
			var terrainArtifact = artifact.AddComponent<TerrainArtifact>();
			
			// get a random artifact ID from the game data (this is a bit of a hack, ideally we'd have a list of valid artifacts for this planet)
			var gameData = DataController.m_instance.m_gameData;
			int artifactId = Random.Range( 0, gameData.m_artifactList.Length );
			
			terrainArtifact.Initialize( artifactId, m_terrainVehicle );
		}
	}
}
