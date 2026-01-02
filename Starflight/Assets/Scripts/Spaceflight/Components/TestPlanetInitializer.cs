using UnityEngine;

public class TestPlanetInitializer : MonoBehaviour
{
    public Planet planetController;
    public ProceduralAdapter proceduralAdapter;

    void Start()
    {
        if (planetController == null) planetController = GetComponent<Planet>();
        if (proceduralAdapter == null) proceduralAdapter = GetComponent<ProceduralAdapter>();

        // Create dummy data for testing
        GD_Planet dummyData = new GD_Planet();
        dummyData.m_bioDensity = 50;
        dummyData.m_mineralDensity = 50;
        dummyData.m_surfaceId = 1; // Standard
        dummyData.m_atmosphereDensityId = 5; // Fixed: m_atmosphereDensity -> m_atmosphereDensityId
        dummyData.m_hydrosphereId = 1; // Fixed: m_hydrosphereDensity -> m_hydrosphereId
        // dummyData.m_radius = 256; // Fixed: m_radius does not exist. Scale is derived from m_mass.
        dummyData.m_mass = 500000; // Sets scale roughly to 256 via GetScale() logic

        // Manually trigger the adapter since the game controller isn't running
        if (proceduralAdapter != null)
        {
            Debug.Log("TestPlanetInitializer: Triggering ProceduralAdapter...");
            proceduralAdapter.Initialize(planetController, dummyData);
        }
        else
        {
            Debug.LogError("TestPlanetInitializer: ProceduralAdapter not found!");
        }
    }
}
