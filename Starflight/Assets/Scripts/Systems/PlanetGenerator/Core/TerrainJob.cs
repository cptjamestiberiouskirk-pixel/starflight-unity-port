using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Starflight.PlanetGenerator
{
    [BurstCompile]
    public struct TerrainJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> baseVertices;
        public NativeArray<float3> modifiedVertices;
        public NativeArray<Color32> colors;

        public float radius;
        public float heightMultiplier;
        public NoiseSettings noiseSettings;

        public Color32 oceanColor;
        public Color32 landColor;
        public Color32 mountainColor;

        public void Execute(int index)
        {
            float3 pos = baseVertices[index];
            float3 normalizedPos = math.normalize(pos);

            float noiseValue = 0;
            float frequency = noiseSettings.frequency;
            float amplitude = noiseSettings.amplitude;

            for (int i = 0; i < noiseSettings.octaves; i++)
            {
                // Using snoise (Simplex Noise) as requested (snoise or cnoise)
                float v = noise.snoise(normalizedPos * frequency);
                noiseValue += v * amplitude;
                
                frequency *= noiseSettings.lacunarity;
                amplitude *= noiseSettings.persistence;
            }

            float elevation = radius + (noiseValue * heightMultiplier);
            modifiedVertices[index] = normalizedPos * elevation;

            // Normalize noise value to 0-1 range for biome selection
            // snoise output is roughly -1 to 1, but multi-octave sum depends on amplitude/persistence
            // Let's assume a reasonable range or use the raw noiseValue directly if normalized
            // The instructions say "Calculate the normalized noise value (0.0 to 1.0)"
            // Given the previous code, noiseValue is the sum of octaves.
            // A simple normalization for 0.0 to 1.0 based on height (normalized by heightMultiplier)
            float normalizedHeight = (noiseValue + 1f) * 0.5f;

            if (normalizedHeight < 0.45f)
            {
                colors[index] = oceanColor;
            }
            else if (normalizedHeight < 0.75f)
            {
                colors[index] = landColor;
            }
            else
            {
                colors[index] = mountainColor;
            }
        }
    }
}