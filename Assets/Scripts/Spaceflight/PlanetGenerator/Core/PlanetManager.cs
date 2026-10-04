using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PlanetManager : MonoBehaviour
{
    [SerializeField] private PlanetData _planetData;
    [SerializeField] private Material _planetMaterial;

    private MeshFilter _meshFilter;
    private Mesh _mesh;

    private Color _oceanColor;
    private Color _landColor;
    private Color _mountainColor;

    [SerializeField, Range(0f, 1f)] private float _seaLevel = 0.45f;
    [SerializeField, Range(0f, 1f)] private float _mountainLevel = 0.75f;

    public Color OceanColor { get => _oceanColor; set => _oceanColor = value; }
    public Color LandColor { get => _landColor; set => _landColor = value; }
    public Color MountainColor { get => _mountainColor; set => _mountainColor = value; }
    public float SeaLevel { get => _seaLevel; set => _seaLevel = value; }
    public float MountainLevel { get => _mountainLevel; set => _mountainLevel = value; }

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();

        if (_planetData != null)
        {
            _oceanColor = _planetData.OceanColor;
            _landColor = _planetData.LandColor;
            _mountainColor = _planetData.MountainColor;
        }

        _mesh = new Mesh { name = "Planet Mesh" };
        _meshFilter.sharedMesh = _mesh;
    }

    private void Start()
    {
        GeneratePlanet();
    }

    private void OnDestroy()
    {
        // The mesh was made here, so it does not go away with the object. It is ours to destroy.
        if (_mesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_mesh);
            }
            else
            {
                DestroyImmediate(_mesh);
            }

            _mesh = null;
        }
    }

    public void Cleanup()
    {
        if (_mesh != null)
        {
            _mesh.Clear();
        }

        if (_meshFilter != null)
        {
            _meshFilter.sharedMesh = null;
        }
    }

    [ContextMenu("Generate Planet")]
    public void GeneratePlanet()
    {
        if (_planetData == null)
        {
            Debug.LogError("GeneratePlanet failed: _planetData is null.");
            return;
        }

        // Lazy initialization
        if (_meshFilter == null)
        {
            _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null) _meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        if (GetComponent<MeshRenderer>() == null)
        {
            gameObject.AddComponent<MeshRenderer>();
        }

        // MESH CHECK
        if (_mesh == null)
        {
            _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null) _meshFilter = gameObject.AddComponent<MeshFilter>();

            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();

            // --- AUTO-ASSIGN MATERIAL ---
            if (_planetMaterial == null)
            {
                _planetMaterial = Resources.Load<Material>("Materials/ProceduralPlanetMat");
            }
            renderer.sharedMaterial = _planetMaterial;
            // ----------------------------

            _mesh = new Mesh { name = "Planet Mesh" };
            _meshFilter.sharedMesh = _mesh;
        }

        // Ensure colors are synced from data
        _oceanColor = _planetData.OceanColor;
        _landColor = _planetData.LandColor;
        _mountainColor = _planetData.MountainColor;

        // Simple Icosphere or UV Sphere approach? 
        // For a basic goal of "A sphere mesh generated via Jobs", let's do a basic UV Sphere or Cube-Sphere.
        // Cube-Sphere is often better for planets. Let's do one face of a cube for simplicity or all 6.
        
        List<Vector3> verticesList = new List<Vector3>();
        List<int> trianglesList = new List<int>();

        // Generate a simple Cube-Sphere
        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        
        for (int i = 0; i < 6; i++)
        {
            CreateFace(directions[i], verticesList, trianglesList);
        }

        Vector3[] verticesArray = verticesList.ToArray();
        // --- FIX: GENERATE DUMMY UVS ---
        Vector2[] uvs = new Vector2[verticesArray.Length];
        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = Vector2.zero; // Fill with zeroes to satisfy the shader
        }        
        NativeArray<float3> baseVertices = new NativeArray<float3>(verticesArray.Length, Allocator.TempJob);
        NativeArray<float3> modifiedVertices = new NativeArray<float3>(verticesArray.Length, Allocator.TempJob);
        NativeArray<Color32> colors = new NativeArray<Color32>(verticesArray.Length, Allocator.TempJob);

        try
        {
            for (int i = 0; i < verticesArray.Length; i++)
            {
                baseVertices[i] = verticesArray[i];
            }

            TerrainJob job = new TerrainJob
            {
                baseVertices = baseVertices,
                modifiedVertices = modifiedVertices,
                colors = colors,
                radius = 1.0f, // _planetData.Radius, -- FORCE 1.0 TO PREVENT DOUBLE SCALING
                heightMultiplier = _planetData.HeightMultiplier,
                noiseSettings = _planetData.NoiseSettings,
                oceanColor = _oceanColor,
                landColor = _landColor,
                mountainColor = _mountainColor,
                seaLevel = _seaLevel,
                mountainLevel = _mountainLevel
            };

            JobHandle handle = job.Schedule(verticesArray.Length, 64);
            handle.Complete();

            Vector3[] finalVertices = new Vector3[verticesArray.Length];
            Color32[] finalColors = new Color32[verticesArray.Length];
            for (int i = 0; i < verticesArray.Length; i++)
            {
                finalVertices[i] = modifiedVertices[i];
                finalColors[i] = colors[i];
            }

            _mesh.Clear();

            // A mesh numbers its vertices with 16 bits unless it is told otherwise. With more than 65535 vertices (a resolution of 105 and up)
            // the triangles then point at the wrong vertices, and nothing reports it.
            _mesh.indexFormat = (finalVertices.Length > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;

            _mesh.vertices = finalVertices;
            _mesh.uv = uvs;         
            _mesh.colors32 = finalColors;
            _mesh.triangles = trianglesList.ToArray();
            _mesh.RecalculateNormals();

            // --- FIX: INHERIT LAYER FROM PARENT ---
            // A planet that was put into a scene by itself has no parent and keeps its own layer.
            if (transform.parent != null)
            {
                gameObject.layer = transform.parent.gameObject.layer;
            }
            // ------------------------------------

            // Add and configure the SphereCollider
            SphereCollider sphereCollider = GetComponent<SphereCollider>();
            if (sphereCollider == null)
            {
                sphereCollider = gameObject.AddComponent<SphereCollider>();
            }
            sphereCollider.radius = _planetData.Radius;
            sphereCollider.isTrigger = false; // For physical collisions
        }
        finally
        {
            if (baseVertices.IsCreated) baseVertices.Dispose();
            if (modifiedVertices.IsCreated) modifiedVertices.Dispose();
            if (colors.IsCreated) colors.Dispose();
        }
    }

    private void CreateFace(Vector3 localUp, List<Vector3> vertices, List<int> triangles)
    {
        int vOffset = vertices.Count;
        Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        Vector3 axisB = Vector3.Cross(localUp, axisA);
        int resolution = _planetData.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = x + y * resolution;
                Vector2 percent = new Vector2(x, y) / (resolution - 1);
                Vector3 pointOnUnitCube = localUp + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                Vector3 pointOnUnitSphere = pointOnUnitCube.normalized;
                vertices.Add(pointOnUnitSphere);

                if (x != resolution - 1 && y != resolution - 1)
                {
                    triangles.Add(vOffset + i);
                    triangles.Add(vOffset + i + resolution + 1);
                    triangles.Add(vOffset + i + resolution);

                    triangles.Add(vOffset + i);
                    triangles.Add(vOffset + i + 1);
                    triangles.Add(vOffset + i + resolution + 1);
                }
            }
        }
    }
}