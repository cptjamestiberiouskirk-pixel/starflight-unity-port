using UnityEngine;

// No namespace = Global Access (Fixes CS0103)
public class ProceduralAdapter : MonoBehaviour
{
    // The Master Kill Switch
    public static bool EnableProceduralGeneration = true;

    private GameObject _planetManagerPrefab;
    private PlanetManager _currentManager;

    public void Initialize(Planet planetController, GD_Planet data)
    {
        // 1. SAFETY CHECK: If Kill Switch is OFF, do nothing.
        if (!EnableProceduralGeneration)
        {
            Cleanup(); // Remove any existing procedural planet
            // Ensure legacy planet is visible
            if (planetController.m_planetModel != null)
            {
                var renderer = planetController.m_planetModel.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = true;
            }
            var revertHostRenderer = planetController.GetComponent<MeshRenderer>();
            if (revertHostRenderer != null) revertHostRenderer.enabled = true;            return;
        }

        // 2. PREFAB LOADER: Find the prefab automatically
        if (_planetManagerPrefab == null)
        {
            // IMPORTANT: You must move your prefab to a "Resources" folder!
            _planetManagerPrefab = Resources.Load<GameObject>("Prefabs/PlanetManager");
        }

        if (_planetManagerPrefab == null)
        {
            Debug.LogError("ProceduralAdapter: Could not find 'PlanetManager' in Resources folder! Reverting to legacy.");
            // Revert to legacy: ensure both renderers are visible
            if (planetController.m_planetModel != null)
            {
                var renderer = planetController.m_planetModel.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = true;
            }
            var revertHostRenderer2 = planetController.GetComponent<MeshRenderer>();
            if (revertHostRenderer2 != null) revertHostRenderer2.enabled = true;

            return;
        }

        // 3. HIDE LEGACY (Renderer only, keeping scripts alive) - Aggressive Hiding
        // 1. Hide Linked Model
        if (planetController != null && planetController.m_planetModel != null)
        {
            var modelRenderer = planetController.m_planetModel.GetComponent<MeshRenderer>();
            if (modelRenderer != null) modelRenderer.enabled = false;
        }

        // 2. Hide Host Object (The Fix: legacy renderer is often on the Planet object itself)
        if (planetController != null)
        {
            var hostRenderer = planetController.GetComponent<MeshRenderer>();
            if (hostRenderer != null) hostRenderer.enabled = false;
        }

        // 4. SPAWN NEW SYSTEM
        if (_currentManager == null)
        {
            // FIX: Parent to m_planetModel if available, so it inherits the rotation logic from Planet.cs Update()
            Transform parentTransform = transform; // Default to this object's transform
            
            if (planetController != null)
            {
                parentTransform = planetController.transform;
                if (planetController.m_planetModel != null)
                {
                    parentTransform = planetController.m_planetModel.transform;
                }
            }

            GameObject go = Instantiate(_planetManagerPrefab, parentTransform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one; // Keep internal scale 1, parent handles sizing
            _currentManager = go.GetComponent<PlanetManager>();
        }

        // 5. APPLY DATA (The Translation Layer)
        ApplyBiomeData(_currentManager, data);

        // 6. GENERATE
        _currentManager.GeneratePlanet();

        // 7. TEST SCENE FIXES (Hazy Blue Background)
        // Only run this if we are in the Test Scene (or just force it for now as requested)
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Test") || 
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Procedural"))
        {
            // Disable Fog
            RenderSettings.fog = false;
            
            // Set Background to Black
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = Color.black;
            }
        }
    }

    private void ApplyBiomeData(PlanetManager manager, GD_Planet data)
    {
        // --- TRANSLATE 1987 DATA TO 2026 VISUALS ---

        // A. Ocean Color
        if (data.IsMolten())
        {
            manager.OceanColor = new Color32(255, 60, 0, 255); // Lava
        }
        else if (data.m_surfaceId == 2) // Suppose ID 2 is "Ice"
        {
            manager.OceanColor = new Color32(200, 220, 255, 255); // Icy Blue
        }
        else
        {
            // Default deep blue ocean
            manager.OceanColor = new Color32(10, 40, 100, 255);
        }

        // B. Land Color (Bio Density 0-100)
        // 0 = Barren (Grey/Brown), 100 = Lush (Green)
        float bioFactor = data.m_bioDensity / 100f;
        Color barrenColor = new Color32(120, 110, 100, 255);
        Color lushColor = new Color32(30, 150, 50, 255);
        manager.LandColor = Color.Lerp(barrenColor, lushColor, bioFactor);

        // C. Mountain Color (Mineral Density)
        // High minerals = Darker/Metallic mountains? Or keep snow white?
        manager.MountainColor = Color.white; 

        // D. Atmosphere
        if (data.HasAtmosphere())
        {
            Color atmColor = data.GetAtmosphereColor();
            // Find the Atmosphere child in the new prefab or apply to the shader
            // Note: Since PlanetManager generates the mesh, the atmosphere is separate.
            // For now, let's trust the Planet.cs to handle the outer atmosphere shell
            // OR we can tweak our specific shader if accessible.
        }
    }

    public void Cleanup()
    {
        if (_currentManager != null)
        {
            Destroy(_currentManager.gameObject);
            _currentManager = null;
        }
    }
}