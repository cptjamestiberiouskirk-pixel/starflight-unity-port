
using UnityEngine;

public class TerrainElements : TerrainGridPopulator
{
	// the element templates
	public GameObject[] m_elementTemplates;

	// the maximum number of elements we can place (based on 100% mineral density)
	public int m_maxNumElements = 5000;

	// reference to the terrain vehicle for element pickup
	public TerrainVehicle m_terrainVehicle;

	// the element IDs for this planet (set during initialization)
	int[] m_planetElementIds;

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

		// build the list of element templates for this planet
		var elementTemplates = new GameObject[ 3 ];

		elementTemplates[ 0 ] = m_elementTemplates[ planet.m_elementIdA ];
		elementTemplates[ 1 ] = m_elementTemplates[ planet.m_elementIdB ];
		elementTemplates[ 2 ] = m_elementTemplates[ planet.m_elementIdC ];

		// save the element IDs so we can tag spawned objects
		m_planetElementIds = new int[] { planet.m_elementIdA, planet.m_elementIdB, planet.m_elementIdC };

		// count the deposits of this planet from the first
		m_planetId = planet.m_id;
		m_numPlaced = 0;

		// place them (this will call OnObjectSpawned for each object)
		InitializeWithCallback( elevationScale, elementTemplates, numElements, randomSeed, true, 1.0f, 1.0f, OnElementSpawned );
	}

	// callback for when an element object is spawned
	void OnElementSpawned( GameObject spawnedObject, int templateIndex )
	{
		// add the TerrainElement component to track this element
		var terrainElement = spawnedObject.AddComponent<TerrainElement>();

		// initialize with the element ID for this template
		var elementId = m_planetElementIds[ templateIndex % m_planetElementIds.Length ];
		terrainElement.Initialize( elementId, m_terrainVehicle, m_planetId, m_numPlaced );

		m_numPlaced++;
	}
}
