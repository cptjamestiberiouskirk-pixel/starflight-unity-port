using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Starflight.PlanetGenerator
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PlanetManager : MonoBehaviour
    {
        [SerializeField] private PlanetData _planetData;

        private MeshFilter _meshFilter;
        private Mesh _mesh;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _mesh = new Mesh { name = "Planet Mesh" };
            _meshFilter.sharedMesh = _mesh;
        }

        private void Start()
        {
            GeneratePlanet();
        }

        [ContextMenu("Generate Planet")]
        public void GeneratePlanet()
        {
            if (_planetData == null)
            {
                Debug.LogError("PlanetData is missing!");
                return;
            }

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
            NativeArray<float3> baseVertices = new NativeArray<float3>(verticesArray.Length, Allocator.TempJob);
            NativeArray<float3> modifiedVertices = new NativeArray<float3>(verticesArray.Length, Allocator.TempJob);
            NativeArray<Color32> colors = new NativeArray<Color32>(verticesArray.Length, Allocator.TempJob);

            for (int i = 0; i < verticesArray.Length; i++)
            {
                baseVertices[i] = verticesArray[i];
            }

            TerrainJob job = new TerrainJob
            {
                baseVertices = baseVertices,
                modifiedVertices = modifiedVertices,
                colors = colors,
                radius = _planetData.Radius,
                heightMultiplier = _planetData.HeightMultiplier,
                noiseSettings = _planetData.NoiseSettings,
                oceanColor = _planetData.OceanColor,
                landColor = _planetData.LandColor,
                mountainColor = _planetData.MountainColor
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
            _mesh.vertices = finalVertices;
            _mesh.colors32 = finalColors;
            _mesh.triangles = trianglesList.ToArray();
            _mesh.RecalculateNormals();

            baseVertices.Dispose();
            modifiedVertices.Dispose();
            colors.Dispose();
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
}