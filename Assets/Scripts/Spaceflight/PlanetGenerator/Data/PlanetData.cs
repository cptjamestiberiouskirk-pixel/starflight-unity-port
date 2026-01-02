using UnityEngine;
using System;

[Serializable]
public struct NoiseSettings
{
    public float frequency;
    public float amplitude;
    public float lacunarity;
    public float persistence;
    public int octaves;

    public static NoiseSettings Default => new NoiseSettings
    {
        frequency = 1.0f,
        amplitude = 1.0f,
        lacunarity = 2.0f,
        persistence = 0.5f,
        octaves = 4
    };
}

[CreateAssetMenu(fileName = "NewPlanetData", menuName = "Starflight/Planet/PlanetData")]
public class PlanetData : ScriptableObject
{
    [SerializeField] private float _radius = 100f;
    [SerializeField, Range(10, 255)] private int _resolution = 100;
    [SerializeField] private NoiseSettings _noiseSettings = NoiseSettings.Default;
    [SerializeField] private float _heightMultiplier = 10f;
    [SerializeField] private Color _oceanColor = Color.blue;
    [SerializeField] private Color _landColor = Color.green;
    [SerializeField] private Color _mountainColor = Color.white;

    public float Radius => _radius;
    public int Resolution => _resolution;
    public NoiseSettings NoiseSettings => _noiseSettings;
    public float HeightMultiplier => _heightMultiplier;
    public Color OceanColor => _oceanColor;
    public Color LandColor => _landColor;
    public Color MountainColor => _mountainColor;
}
