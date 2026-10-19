
using UnityEngine;

public class TerrainElements : TerrainGridPopulator
{
	// the element templates
	public GameObject[] m_elementTemplates;

	// the maximum number of elements we can place (based on 100% mineral density)
	public int m_maxNumElements = 5000;

	// reference to the terrain vehicle for element pickup
	public TerrainVehicle m_terrainVehicle;

	// the planet, and how many deposits have been placed on it so far (a deposit is known by the order it is placed in, which the planet's seed keeps the same)
	int m_planetId;
	int m_numPlaced;

	// populate this planet with elements
	public void Initialize( PlanetGenerator planetGenerator, float elevationScale, int randomSeed )
	{
		// get to this planet
		var planet = planetGenerator.GetPlanet();

		// calculate the number of elements to place
		var numElements = ( planet.m_mineralDensity * m_maxNumElements ) / 100;

		// count the deposits of this planet from the first
		m_planetId = planet.m_id;
		m_numPlaced = 0;

		// place them, each deposit's template the one of its element (TerrainElement.ChooseDepositElement: the original's shares of the planet's three elements
		// and the rest), which moves none of the planet's seeded draws (this will call OnElementSpawned for each object)
		InitializeWithCallback( elevationScale, m_elementTemplates, numElements, randomSeed, true, 1.0f, 1.0f, OnElementSpawned, i => TerrainElement.ChooseDepositElement( planet, i ) );
	}

	// callback for when an element object is spawned
	void OnElementSpawned( GameObject spawnedObject, int templateIndex )
	{
		// add the TerrainElement component to track this element
		var terrainElement = spawnedObject.AddComponent<TerrainElement>();

		// the templates are the elements' own, so the template's index is the element's id
		var elementId = templateIndex;
		terrainElement.Initialize( elementId, m_terrainVehicle, m_planetId, m_numPlaced );

		m_numPlaced++;
	}
}
