
using System.Collections.Generic;

using UnityEngine;

// a planet in its natural look (PlanetLook.Natural): maps made on the graphics card from the planet's own maps as a blueprint and its climate in the game
// data (the passes of Natural Planet Bake), and the materials that show them (Natural Planet for the ground, Natural Clouds for the clouds, Natural
// Atmosphere for the halo of the air); nothing here changes the planet's data: the ground the terrain vehicle drives on is still the generator's elevation,
// and the coastlines of the natural maps stay close to its own
public class NaturalPlanet
{
	// how wide the maps are in the star system, where a planet is small on the screen, and in orbit (they are half as high as they are wide, the cloud
	// cover half as wide as the others)
	public const int c_starSystemMapWidth = 1024;
	public const int c_orbitMapWidth = 4096;

	// how wide the orbit's maps are on a graphics card with less memory than this (in megabytes)
	const int c_smallOrbitMapWidth = 2048;
	const int c_smallGraphicsMemory = 2048;

	// how much finer the coastlines of the natural maps are than the blueprint's (in its elevation, which runs from -1 to 1): more moves the shore further
	// from where the terrain vehicle finds it
	const float c_coastAmount = 0.1f;

	// how much faster than the ground the clouds go round (a share of the planet's turn)
	const float c_cloudDrift = 0.15f;

	// how the air looks, in planet radii: how high it reaches, how fast it thins out (a share of that), how much sunlight it scatters
	const float c_airHeight = 0.06f;
	const float c_airScaleHeight = 0.1f;
	const float c_airLight = 14.0f;

	// how strongly the thinnest and the thickest air of the game data scatter
	const float c_thinnestAir = 1.2f;
	const float c_thickestAir = 2.6f;

	// a gas's colour in the natural look (where it draws the sky from a clean sky's blue, and how far), and the colour of its clouds
	struct Gas
	{
		public Color m_color;
		public float m_tint;
		public Color m_cloudColor;

		public Gas( Color color, float tint, Color cloudColor )
		{
			m_color = color;
			m_tint = tint;
			m_cloudColor = cloudColor;
		}
	}

	// the gases of the game data (by name), as they would look: the light gases scatter blue as the air of Earth does, carbon dioxide gives a pale golden
	// sky, methane a cyan one, ammonia a cream one, chlorine a yellow-green one, hydrogen cyanide and metal compounds an orange-tan haze
	static readonly Dictionary<string, Gas> c_gases = new Dictionary<string, Gas>
	{
		{ "Nitrogen", new Gas( new Color( 0.35f, 0.55f, 1.0f ), 0.0f, Color.white ) },
		{ "Oxygen", new Gas( new Color( 0.35f, 0.55f, 1.0f ), 0.0f, Color.white ) },
		{ "Argon", new Gas( new Color( 0.35f, 0.55f, 1.0f ), 0.0f, Color.white ) },
		{ "Helium", new Gas( new Color( 0.6f, 0.8f, 1.0f ), 0.2f, Color.white ) },
		{ "Hydrogen", new Gas( new Color( 0.6f, 0.8f, 1.0f ), 0.2f, Color.white ) },
		{ "Carbon Dioxide", new Gas( new Color( 1.0f, 0.85f, 0.6f ), 0.75f, new Color( 1.0f, 0.95f, 0.85f ) ) },
		{ "Methane", new Gas( new Color( 0.55f, 0.85f, 0.95f ), 0.5f, Color.white ) },
		{ "Ammonia", new Gas( new Color( 1.0f, 0.9f, 0.7f ), 0.6f, new Color( 1.0f, 0.96f, 0.88f ) ) },
		{ "Ammonia Compounds", new Gas( new Color( 1.0f, 0.9f, 0.7f ), 0.6f, new Color( 1.0f, 0.96f, 0.88f ) ) },
		{ "Chlorine", new Gas( new Color( 0.75f, 1.0f, 0.45f ), 0.7f, new Color( 0.95f, 1.0f, 0.85f ) ) },
		{ "Chlorine Compounds", new Gas( new Color( 0.75f, 1.0f, 0.45f ), 0.7f, new Color( 0.95f, 1.0f, 0.85f ) ) },
		{ "Fluorine", new Gas( new Color( 1.0f, 1.0f, 0.6f ), 0.6f, new Color( 1.0f, 1.0f, 0.9f ) ) },
		{ "Fluorine Compounds", new Gas( new Color( 1.0f, 1.0f, 0.6f ), 0.6f, new Color( 1.0f, 1.0f, 0.9f ) ) },
		{ "Hydrogen Cyanide", new Gas( new Color( 1.0f, 0.7f, 0.35f ), 0.75f, new Color( 1.0f, 0.92f, 0.8f ) ) },
		{ "Metal Compounds", new Gas( new Color( 0.9f, 0.75f, 0.6f ), 0.6f, new Color( 0.95f, 0.9f, 0.85f ) ) },
	};

	// what a sea looks like (over its shallows, far out, and frozen)
	struct Sea
	{
		public Color m_shallow;
		public Color m_deep;
		public Color m_ice;

		public Sea( Color shallow, Color deep, Color ice )
		{
			m_shallow = shallow;
			m_deep = deep;
			m_ice = ice;
		}
	}

	// the hydrospheres of the game data (by name), as they would look: water blue with turquoise shallows, the seas of methane dark brown as Titan's,
	// liquid nitrogen slate under pink ice, ammonia pale tan, chlorine compounds yellow-green, sulfur compounds yellow, metal compounds silver
	static readonly Dictionary<string, Sea> c_seas = new Dictionary<string, Sea>
	{
		{ "Water", new Sea( new Color( 0.06f, 0.48f, 0.58f ), new Color( 0.02f, 0.16f, 0.38f ), new Color( 0.88f, 0.93f, 0.97f ) ) },
		{ "Ammonia", new Sea( new Color( 0.55f, 0.52f, 0.38f ), new Color( 0.18f, 0.16f, 0.10f ), new Color( 0.92f, 0.89f, 0.80f ) ) },
		{ "Ammonia Compounds", new Sea( new Color( 0.55f, 0.52f, 0.38f ), new Color( 0.18f, 0.16f, 0.10f ), new Color( 0.92f, 0.89f, 0.80f ) ) },
		{ "Carbon Monoxide", new Sea( new Color( 0.20f, 0.22f, 0.26f ), new Color( 0.05f, 0.05f, 0.07f ), new Color( 0.93f, 0.93f, 0.95f ) ) },
		{ "Chlorine Compounds", new Sea( new Color( 0.50f, 0.62f, 0.20f ), new Color( 0.15f, 0.22f, 0.05f ), new Color( 0.85f, 0.92f, 0.70f ) ) },
		{ "Ethanol", new Sea( new Color( 0.20f, 0.35f, 0.38f ), new Color( 0.04f, 0.09f, 0.11f ), new Color( 0.90f, 0.93f, 0.95f ) ) },
		{ "Methanol", new Sea( new Color( 0.20f, 0.35f, 0.38f ), new Color( 0.04f, 0.09f, 0.11f ), new Color( 0.90f, 0.93f, 0.95f ) ) },
		{ "Fluorine Compounds", new Sea( new Color( 0.62f, 0.62f, 0.30f ), new Color( 0.20f, 0.20f, 0.06f ), new Color( 0.94f, 0.94f, 0.80f ) ) },
		{ "Metal Compounds", new Sea( new Color( 0.45f, 0.45f, 0.48f ), new Color( 0.20f, 0.20f, 0.22f ), new Color( 0.60f, 0.60f, 0.62f ) ) },
		{ "Methane", new Sea( new Color( 0.22f, 0.17f, 0.12f ), new Color( 0.05f, 0.04f, 0.03f ), new Color( 0.93f, 0.85f, 0.78f ) ) },
		{ "Nitrogen", new Sea( new Color( 0.40f, 0.45f, 0.55f ), new Color( 0.10f, 0.12f, 0.18f ), new Color( 0.95f, 0.88f, 0.86f ) ) },
		{ "Oxygen", new Sea( new Color( 0.45f, 0.62f, 0.85f ), new Color( 0.12f, 0.20f, 0.40f ), new Color( 0.80f, 0.88f, 0.98f ) ) },
		{ "Silicon Compounds", new Sea( new Color( 0.30f, 0.30f, 0.30f ), new Color( 0.12f, 0.12f, 0.12f ), new Color( 0.55f, 0.55f, 0.55f ) ) },
		{ "Sodium Compounds", new Sea( new Color( 0.85f, 0.70f, 0.40f ), new Color( 0.40f, 0.25f, 0.10f ), new Color( 0.95f, 0.92f, 0.85f ) ) },
		{ "Sulfur Compounds", new Sea( new Color( 0.85f, 0.70f, 0.20f ), new Color( 0.45f, 0.28f, 0.05f ), new Color( 0.95f, 0.90f, 0.55f ) ) },
	};

	// water ice, for a frozen world without a sea
	static readonly Color c_waterIce = new Color( 0.88f, 0.92f, 0.96f );

	// the bands of a gas giant: its light zones, its dark belts and its storms
	struct Bands
	{
		public Color m_light;
		public Color m_dark;
		public Color m_accent;

		public Bands( Color light, Color dark, Color accent )
		{
			m_light = light;
			m_dark = dark;
			m_accent = accent;
		}
	}

	// the gases of the game data (by name), as a gas giant's bands would look in them: hydrogen and helium cream and brown with a red storm, as Jupiter,
	// methane cyan and blue with a dark storm, as Neptune, ammonia pale gold, nitrogen and oxygen pale blue-gray, chlorine and fluorine yellow-green
	static readonly Dictionary<string, Bands> c_gasGiantBands = new Dictionary<string, Bands>
	{
		{ "Hydrogen", new Bands( new Color( 0.92f, 0.86f, 0.74f ), new Color( 0.66f, 0.48f, 0.34f ), new Color( 0.80f, 0.42f, 0.28f ) ) },
		{ "Helium", new Bands( new Color( 0.92f, 0.86f, 0.74f ), new Color( 0.66f, 0.48f, 0.34f ), new Color( 0.80f, 0.42f, 0.28f ) ) },
		{ "Methane", new Bands( new Color( 0.62f, 0.84f, 0.90f ), new Color( 0.24f, 0.46f, 0.72f ), new Color( 0.12f, 0.22f, 0.50f ) ) },
		{ "Ammonia", new Bands( new Color( 0.93f, 0.87f, 0.72f ), new Color( 0.72f, 0.56f, 0.38f ), new Color( 0.95f, 0.93f, 0.86f ) ) },
		{ "Ammonia Compounds", new Bands( new Color( 0.93f, 0.87f, 0.72f ), new Color( 0.72f, 0.56f, 0.38f ), new Color( 0.95f, 0.93f, 0.86f ) ) },
		{ "Nitrogen", new Bands( new Color( 0.86f, 0.88f, 0.90f ), new Color( 0.55f, 0.62f, 0.72f ), new Color( 0.92f, 0.80f, 0.62f ) ) },
		{ "Argon", new Bands( new Color( 0.86f, 0.88f, 0.90f ), new Color( 0.55f, 0.62f, 0.72f ), new Color( 0.92f, 0.80f, 0.62f ) ) },
		{ "Oxygen", new Bands( new Color( 0.84f, 0.90f, 0.94f ), new Color( 0.52f, 0.64f, 0.78f ), new Color( 0.90f, 0.78f, 0.60f ) ) },
		{ "Carbon Dioxide", new Bands( new Color( 0.92f, 0.86f, 0.76f ), new Color( 0.70f, 0.58f, 0.48f ), new Color( 0.82f, 0.50f, 0.35f ) ) },
		{ "Hydrogen Cyanide", new Bands( new Color( 0.90f, 0.78f, 0.60f ), new Color( 0.62f, 0.44f, 0.28f ), new Color( 0.70f, 0.36f, 0.20f ) ) },
		{ "Fluorine", new Bands( new Color( 0.92f, 0.93f, 0.70f ), new Color( 0.62f, 0.68f, 0.36f ), new Color( 0.86f, 0.78f, 0.40f ) ) },
		{ "Fluorine Compounds", new Bands( new Color( 0.92f, 0.93f, 0.70f ), new Color( 0.62f, 0.68f, 0.36f ), new Color( 0.86f, 0.78f, 0.40f ) ) },
		{ "Chlorine", new Bands( new Color( 0.86f, 0.94f, 0.66f ), new Color( 0.50f, 0.64f, 0.30f ), new Color( 0.80f, 0.84f, 0.40f ) ) },
		{ "Chlorine Compounds", new Bands( new Color( 0.86f, 0.94f, 0.66f ), new Color( 0.50f, 0.64f, 0.30f ), new Color( 0.80f, 0.84f, 0.40f ) ) },
		{ "Metal Compounds", new Bands( new Color( 0.80f, 0.74f, 0.66f ), new Color( 0.50f, 0.44f, 0.38f ), new Color( 0.70f, 0.50f, 0.36f ) ) },
	};

	// the ash of a molten world without seas, on its crust, and the clouds of ash over a molten world
	static readonly Color c_ash = new Color( 0.35f, 0.33f, 0.30f );
	static readonly Color c_ashClouds = new Color( 0.32f, 0.30f, 0.29f );

	// how brightly lava glows (in the light of the sun's 1.9)
	const float c_lavaIntensity = 1.6f;

	// how much of the light a gas giant's bands send back (the colours of the table are their hues at full brightness), and how strongly its air scatters
	const float c_gasGiantAlbedo = 0.6f;
	const float c_gasGiantAir = 1.2f;

	// the shaders (in a Resources folder, so that they are in every build), and the material the maps are baked with
	static Shader s_bakeShader;
	static Shader s_surfaceShader;
	static Shader s_cloudShader;
	static Shader s_atmosphereShader;
	static Material s_bakeMaterial;
	static bool s_shadersLoaded;

	// the materials renderers had before the natural look's went on them (they go back on when the original look is shown)
	static readonly Dictionary<Renderer, Material> s_originalMaterials = new Dictionary<Renderer, Material>();

	// the blueprint's elevations, kept between bakes so that a planet does not need a new array every time
	static float[] s_blueprintData;

	// the smooth copies of the sphere meshes the planets are drawn with (UseSmoothSphere), and the meshes the models had before
	static readonly Dictionary<Mesh, Mesh> s_smoothSpheres = new Dictionary<Mesh, Mesh>();
	static readonly Dictionary<MeshFilter, Mesh> s_originalMeshes = new Dictionary<MeshFilter, Mesh>();

	// how many smaller triangles each side of a triangle of a sphere mesh is cut into for its smooth copy
	const int c_sphereSubdivisions = 8;

	// how big the smooth copy is (a share of the mesh's radius): a little smaller, so that the camera of a launch from the surface, which starts at the
	// top of the planet in orbit, is above the ground and sees it below the horizon (on a sphere of the mesh's full radius it was inside the planet and saw
	// space through it); in orbit and in the star system the difference does not show
	const float c_smoothSphereScale = 0.99f;

	static readonly int c_elevationId = Shader.PropertyToID( "_Elevation" );
	static readonly int c_blueprintId = Shader.PropertyToID( "_Blueprint" );
	static readonly int c_heightMapId = Shader.PropertyToID( "_HeightMap" );
	static readonly int c_albedoId = Shader.PropertyToID( "_Albedo" );
	static readonly int c_normalMapId = Shader.PropertyToID( "_NormalMap" );
	static readonly int c_cloudsId = Shader.PropertyToID( "_Clouds" );
	static readonly int c_cloudDriftId = Shader.PropertyToID( "_CloudDrift" );
	static readonly int c_cloudScaleId = Shader.PropertyToID( "_CloudScale" );
	static readonly int c_planetRadiusId = Shader.PropertyToID( "_PlanetRadius" );
	static readonly int c_centerDepthId = Shader.PropertyToID( "_CenterDepth" );

	// the planet
	readonly GD_Planet m_planet;

	// the maps (the albedo's alpha is where the open sea is)
	RenderTexture m_albedoMap;
	RenderTexture m_normalMap;
	RenderTexture m_cloudMap;

	// how wide the maps are (0 until they are made)
	int m_mapWidth;

	// the materials of the ground, the clouds and the halo of the air
	Material m_surfaceMaterial;
	Material m_cloudMaterial;
	Material m_atmosphereMaterial;

	// the share of the blueprint's land and sea that is still land and sea in the natural maps (worked out by a bake only while s_measureCoasts is true,
	// for the probe: it reads the height map back)
	public static bool s_measureCoasts;
	public float m_coastAgreement = -1.0f;

	public NaturalPlanet( GD_Planet planet )
	{
		m_planet = planet;
	}

	// true if the graphics card can make the maps of the natural look (not in a headless run, which has none)
	public static bool IsSupported()
	{
		if ( SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null )
		{
			return false;
		}

		if ( !SystemInfo.SupportsRenderTextureFormat( RenderTextureFormat.RFloat ) || !SystemInfo.SupportsTextureFormat( TextureFormat.RFloat ) )
		{
			return false;
		}

		LoadShaders();

		return ( s_bakeMaterial != null ) && ( s_surfaceShader != null ) && s_surfaceShader.isSupported && ( s_cloudShader != null ) && s_cloudShader.isSupported && ( s_atmosphereShader != null ) && s_atmosphereShader.isSupported;
	}

	// true if the natural look is made for this kind of planet (every kind but the Crystal Planet, which keeps the original look)
	public static bool Suits( GD_Planet planet )
	{
		return ( planet != null ) && ( planet.m_surfaceId >= 1 ) && ( planet.m_surfaceId <= 5 ) && !planet.IsCrystalPlanet();
	}

	// how wide the maps of the planet in orbit are on this computer
	public static int GetOrbitMapWidth()
	{
		var width = ( SystemInfo.graphicsMemorySize >= c_smallGraphicsMemory ) ? c_orbitMapWidth : c_smallOrbitMapWidth;

		return Mathf.Min( width, SystemInfo.maxTextureSize );
	}

	static void LoadShaders()
	{
		if ( s_shadersLoaded )
		{
			return;
		}

		s_shadersLoaded = true;

		s_bakeShader = Resources.Load<Shader>( "Shaders/Natural Planet Bake" );
		s_surfaceShader = Resources.Load<Shader>( "Shaders/Natural Planet" );
		s_cloudShader = Resources.Load<Shader>( "Shaders/Natural Clouds" );
		s_atmosphereShader = Resources.Load<Shader>( "Shaders/Natural Atmosphere" );

		if ( ( s_bakeShader == null ) || ( s_surfaceShader == null ) || ( s_cloudShader == null ) || ( s_atmosphereShader == null ) )
		{
			Debug.LogWarning( "NaturalPlanet: a shader of the natural look was not found - the planets keep their original look" );

			return;
		}

		if ( s_bakeShader.isSupported )
		{
			s_bakeMaterial = new Material( s_bakeShader );
			s_bakeMaterial.name = "Natural Planet Bake";
		}
	}

	// true once the maps are made (and still there: a graphics card can lose what is in a render texture)
	public bool IsBaked()
	{
		return ( m_mapWidth > 0 ) && ( m_albedoMap != null ) && m_albedoMap.IsCreated() && ( m_normalMap != null ) && m_normalMap.IsCreated() && ( m_cloudMap != null ) && m_cloudMap.IsCreated();
	}

	// how wide the maps are (0 until they are made)
	public int GetMapWidth()
	{
		return IsBaked() ? m_mapWidth : 0;
	}

	// true if the planet has clouds (it has air and some weather; a gas giant's clouds are its bands)
	public bool HasClouds()
	{
		return m_planet.HasAtmosphere() && ( m_planet.m_weatherId > 0 );
	}

	// true if the planet has air round it: an atmosphere, or the deep air of a gas giant
	public bool HasAir()
	{
		return m_planet.HasAtmosphere() || m_planet.IsGasGiant();
	}

	// the material of the ground (null until the maps are made)
	public Material GetSurfaceMaterial()
	{
		return m_surfaceMaterial;
	}

	// the maps of the ground and the clouds (for the probe)
	public RenderTexture GetAlbedoMap()
	{
		return m_albedoMap;
	}

	public RenderTexture GetCloudMap()
	{
		return m_cloudMap;
	}

	/// <summary>
	/// Makes the natural look's maps of the planet, at least this wide, from the maps its generator has made (nothing to do when they are already that
	/// wide). Returns false if they could not be made: the planet then keeps its original look. Never throws (it runs in the middle of the generation of
	/// the star system's planets).
	/// </summary>
	public bool Bake( PlanetGenerator generator, int width )
	{
		if ( IsBaked() && ( m_mapWidth >= width ) )
		{
			return true;
		}

		if ( ( generator == null ) || !generator.m_mapsGenerated || ( generator.m_elevation == null ) || !IsSupported() )
		{
			return false;
		}

		Texture2D blueprint = null;
		RenderTexture heightMap = null;
		RenderTexture albedoMap = null;
		RenderTexture normalMap = null;
		RenderTexture cloudMap = null;

		try
		{
			width = Mathf.Clamp( Mathf.ClosestPowerOfTwo( width ), 256, SystemInfo.maxTextureSize );

			var height = width / 2;

			blueprint = MakeBlueprint( generator );

			SetBakeProperties( blueprint, generator, width, height );

			// the height first: the other maps are worked out from it (a gas giant has none: its clouds are flat)
			var isGasGiant = m_planet.IsGasGiant();

			heightMap = MakeMap( "Natural Height", width, height, RenderTextureFormat.RFloat, true, false );

			if ( isGasGiant )
			{
				Graphics.Blit( Texture2D.blackTexture, heightMap );
			}
			else
			{
				Graphics.Blit( null, heightMap, s_bakeMaterial, 0 );
			}

			s_bakeMaterial.SetTexture( c_heightMapId, heightMap );

			albedoMap = MakeMap( "Natural Albedo", width, height, RenderTextureFormat.ARGB32, false, true );
			normalMap = MakeMap( "Natural Normal", width, height, RenderTextureFormat.ARGB32, true, true );
			cloudMap = MakeMap( "Natural Clouds", width / 2, height / 2, SystemInfo.SupportsRenderTextureFormat( RenderTextureFormat.R8 ) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32, true, true );

			// the colours: a gas giant's bands, a molten world's crust and lava, or the ground and sea of any other
			Graphics.Blit( null, albedoMap, s_bakeMaterial, isGasGiant ? 4 : ( m_planet.IsMolten() ? 5 : 1 ) );
			Graphics.Blit( null, normalMap, s_bakeMaterial, 2 );
			Graphics.Blit( null, cloudMap, s_bakeMaterial, 3 );

			if ( s_measureCoasts )
			{
				m_coastAgreement = isGasGiant ? 1.0f : MeasureCoastAgreement( heightMap, generator );
			}

			// the new maps take the place of the old ones
			ReleaseMaps();

			m_albedoMap = albedoMap;
			m_normalMap = normalMap;
			m_cloudMap = cloudMap;
			m_mapWidth = width;

			albedoMap = null;
			normalMap = null;
			cloudMap = null;

			MakeMaterials();

			return true;
		}
		catch ( System.Exception exception )
		{
			Debug.LogWarning( "NaturalPlanet: the natural maps of planet " + m_planet.m_id + " could not be made - it keeps its original look (" + exception.Message + ")" );

			return false;
		}
		finally
		{
			if ( s_bakeMaterial != null )
			{
				s_bakeMaterial.SetTexture( c_elevationId, null );
				s_bakeMaterial.SetTexture( c_blueprintId, null );
				s_bakeMaterial.SetTexture( c_heightMapId, null );
			}

			ReleaseMap( ref heightMap );
			ReleaseMap( ref albedoMap );
			ReleaseMap( ref normalMap );
			ReleaseMap( ref cloudMap );

			if ( blueprint != null )
			{
				Object.Destroy( blueprint );
			}
		}
	}

	// the blueprint: the generator's elevation, the sea below 0 (-1 at the deepest point, 1 at the highest)
	static Texture2D MakeBlueprint( PlanetGenerator generator )
	{
		var elevation = generator.m_elevation;
		var width = elevation.GetLength( 1 );
		var height = elevation.GetLength( 0 );

		var middle = generator.m_waterElevation;
		var up = Mathf.Max( generator.m_maximumElevation - generator.m_waterElevation, 1e-4f );
		var down = Mathf.Max( generator.m_waterElevation - generator.m_minimumElevation, 1e-4f );

		// a gas giant has no sea and no highest point worked out: its bands run from its lowest elevation (-1) to its highest (1)
		if ( ( generator.GetPlanet() != null ) && generator.GetPlanet().IsGasGiant() )
		{
			var lowest = float.MaxValue;
			var highest = float.MinValue;

			foreach ( var value in elevation )
			{
				lowest = Mathf.Min( lowest, value );
				highest = Mathf.Max( highest, value );
			}

			middle = ( lowest + highest ) * 0.5f;
			up = Mathf.Max( ( highest - lowest ) * 0.5f, 1e-4f );
			down = up;
		}

		if ( ( s_blueprintData == null ) || ( s_blueprintData.Length != width * height ) )
		{
			s_blueprintData = new float[ width * height ];
		}

		var index = 0;

		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				var aboveWater = elevation[ y, x ] - middle;

				s_blueprintData[ index++ ] = Mathf.Clamp( ( aboveWater >= 0.0f ) ? aboveWater / up : aboveWater / down, -1.0f, 1.0f );
			}
		}

		var blueprint = new Texture2D( width, height, TextureFormat.RFloat, false, true );

		blueprint.name = "Natural Blueprint";
		blueprint.wrapModeU = TextureWrapMode.Repeat;
		blueprint.wrapModeV = TextureWrapMode.Clamp;
		blueprint.filterMode = FilterMode.Bilinear;

		blueprint.SetPixelData( s_blueprintData, 0 );
		blueprint.Apply( false, true );

		return blueprint;
	}

	// what the bake needs to know: the blueprint and the planet's climate in the game data
	void SetBakeProperties( Texture2D blueprint, PlanetGenerator generator, int width, int height )
	{
		var material = s_bakeMaterial;
		var gameData = DataController.m_instance.m_gameData;

		var hydrosphere = ( ( m_planet.m_hydrosphereId >= 0 ) && ( m_planet.m_hydrosphereId < gameData.m_hydrosphereList.Length ) ) ? gameData.m_hydrosphereList[ m_planet.m_hydrosphereId ].m_name : "None";
		var hasSea = c_seas.TryGetValue( hydrosphere, out var sea );
		var isWater = hydrosphere == "Water";
		var frozen = m_planet.m_surfaceId == 3;
		var density = m_planet.HasAtmosphere() ? m_planet.GetAtmosphereDensity() : 0.0f;

		// a sea without air above it is frozen too (nothing stays liquid in a vacuum)
		var seaFrozen = frozen || !m_planet.HasAtmosphere();

		// the temperatures of the game data (sub-arctic 0 to inferno 5): the warmer end at the equator, the colder one (a little colder) at the poles
		var temperatureA = m_planet.m_temperatureIdA / 5.0f;
		var temperatureB = m_planet.m_temperatureIdB / 5.0f;

		material.SetTexture( c_elevationId, blueprint );
		material.SetTexture( c_blueprintId, ( generator.m_albedoTexture != null ) ? (Texture) generator.m_albedoTexture : Texture2D.grayTexture );
		material.SetVector( "_Seed", new Vector4( ( m_planet.m_id * 7.31f ) % 10.0f, ( m_planet.m_id * 3.17f ) % 10.0f, ( m_planet.m_id * 5.53f ) % 10.0f, 0.0f ) );
		material.SetVector( "_TexelSize", new Vector4( 1.0f / width, 1.0f / height, 0.0f, 0.0f ) );
		material.SetFloat( "_CoastAmount", c_coastAmount );
		material.SetFloat( "_TemperatureMin", Mathf.Max( Mathf.Min( temperatureA, temperatureB ) - 0.1f, 0.0f ) );
		material.SetFloat( "_TemperatureMax", Mathf.Max( temperatureA, temperatureB ) );

		// life grows where it is wet and mild, as much as the planet has; a world of water is wet (wetter the thicker its air), one of another liquid less so,
		// one without a sea dry
		material.SetFloat( "_Vegetation", Mathf.Clamp01( m_planet.m_bioDensity / 60.0f ) );
		material.SetFloat( "_Moisture", isWater ? Mathf.Lerp( 0.7f, 1.0f, density ) : ( hasSea ? 0.5f : 0.2f ) );

		material.SetFloat( "_HasSea", hasSea ? 1.0f : 0.0f );
		material.SetFloat( "_SeaFrozen", seaFrozen ? 1.0f : 0.0f );
		// (on a molten world the colour of the deposits its seas leave on its crust)
		material.SetColor( "_SeaShallow", hasSea ? sea.m_shallow : ( m_planet.IsMolten() ? c_ash : Color.black ) );
		material.SetColor( "_SeaDeep", hasSea ? sea.m_deep : Color.black );
		material.SetColor( "_SeaIce", hasSea ? sea.m_ice : c_waterIce );
		material.SetFloat( "_Airless", m_planet.HasAtmosphere() ? 0.0f : 1.0f );
		material.SetFloat( "_Icy", frozen ? 1.0f : 0.0f );
		material.SetFloat( "_Weather", HasClouds() ? m_planet.m_weatherId / 4.0f : 0.0f );

		// a molten world's crust: broad yellow fields of sulfur, as on Io, where its seas are of sulfur compounds, fewer deposits of other stuff
		material.SetFloat( "_DepositAmount", ( hydrosphere == "Sulfur Compounds" ) ? 0.6f : ( hasSea ? 0.3f : 0.15f ) );

		// a gas giant's bands
		GetBands( out var bands );

		material.SetColor( "_GasLight", bands.m_light );
		material.SetColor( "_GasDark", bands.m_dark );
		material.SetColor( "_GasAccent", bands.m_accent );
	}

	// the colours of a gas giant's bands: its gases' (the first counts most, as for the colour of its air)
	void GetBands( out Bands bands )
	{
		var gameData = DataController.m_instance.m_gameData;
		var ids = new[] { m_planet.m_atmosphereIdA, m_planet.m_atmosphereIdB, m_planet.m_atmosphereIdC };
		var weights = new[] { 1.0f, 0.5f, 0.25f };
		var light = Color.black;
		var dark = Color.black;
		var accent = Color.black;
		var totalWeight = 0.0f;

		for ( var i = 0; i < ids.Length; i++ )
		{
			if ( ( ids[ i ] < 0 ) || ( ids[ i ] >= gameData.m_atmosphereList.Length ) || !c_gasGiantBands.TryGetValue( gameData.m_atmosphereList[ ids[ i ] ].m_name, out var gas ) )
			{
				continue;
			}

			light += gas.m_light * weights[ i ];
			dark += gas.m_dark * weights[ i ];
			accent += gas.m_accent * weights[ i ];
			totalWeight += weights[ i ];
		}

		if ( totalWeight <= 0.0f )
		{
			light = c_gasGiantBands[ "Hydrogen" ].m_light;
			dark = c_gasGiantBands[ "Hydrogen" ].m_dark;
			accent = c_gasGiantBands[ "Hydrogen" ].m_accent;
			totalWeight = 1.0f;
		}

		light /= totalWeight;
		dark /= totalWeight;
		accent /= totalWeight;

		light.a = 1.0f;
		dark.a = 1.0f;
		accent.a = 1.0f;

		bands = new Bands( light * c_gasGiantAlbedo, dark * c_gasGiantAlbedo, accent * c_gasGiantAlbedo );

		bands.m_light.a = 1.0f;
		bands.m_dark.a = 1.0f;
		bands.m_accent.a = 1.0f;
	}

	// the colour of a molten world's lava, by what its seas are made of: molten metal white-hot, sodium compounds yellow-orange, sulfur deep red-orange,
	// rock (and anything else) orange
	static Color GetLavaColor( string hydrosphere )
	{
		switch ( hydrosphere )
		{
			case "Metal Compounds":
				return new Color( 1.0f, 0.72f, 0.40f );

			case "Sodium Compounds":
				return new Color( 1.0f, 0.55f, 0.12f );

			case "Sulfur Compounds":
				return new Color( 1.0f, 0.30f, 0.06f );

			default:
				return new Color( 1.0f, 0.38f, 0.08f );
		}
	}

	// the colour of the planet's air, how far it draws the sky from a clean sky's blue, and the colour of its clouds: its gases' (the first counts most,
	// as GD_Planet.GetAtmosphereColor has it)
	void GetAir( out Color color, out float tint, out Color cloudColor )
	{
		var gameData = DataController.m_instance.m_gameData;
		var ids = new[] { m_planet.m_atmosphereIdA, m_planet.m_atmosphereIdB, m_planet.m_atmosphereIdC };
		var weights = new[] { 1.0f, 0.5f, 0.25f };
		var totalWeight = 0.0f;

		color = Color.black;
		tint = 0.0f;
		cloudColor = Color.black;

		for ( var i = 0; i < ids.Length; i++ )
		{
			if ( ( ids[ i ] < 0 ) || ( ids[ i ] >= gameData.m_atmosphereList.Length ) || !c_gases.TryGetValue( gameData.m_atmosphereList[ ids[ i ] ].m_name, out var gas ) )
			{
				continue;
			}

			color += gas.m_color * weights[ i ];
			tint += gas.m_tint * weights[ i ];
			cloudColor += gas.m_cloudColor * weights[ i ];
			totalWeight += weights[ i ];
		}

		if ( totalWeight <= 0.0f )
		{
			color = c_gases[ "Nitrogen" ].m_color;
			tint = 0.0f;
			cloudColor = Color.white;

			return;
		}

		color /= totalWeight;
		tint /= totalWeight;
		cloudColor /= totalWeight;

		color.a = 1.0f;
		cloudColor.a = 1.0f;
	}

	// makes the materials the first time, and puts the maps and the planet's air on them
	void MakeMaterials()
	{
		if ( m_surfaceMaterial == null )
		{
			m_surfaceMaterial = new Material( s_surfaceShader );
			m_surfaceMaterial.name = "Natural Planet " + m_planet.m_id;
		}

		if ( m_cloudMaterial == null )
		{
			m_cloudMaterial = new Material( s_cloudShader );
			m_cloudMaterial.name = "Natural Clouds " + m_planet.m_id;
		}

		if ( m_atmosphereMaterial == null )
		{
			m_atmosphereMaterial = new Material( s_atmosphereShader );
			m_atmosphereMaterial.name = "Natural Atmosphere " + m_planet.m_id;
		}

		var hasAir = HasAir();
		var density = hasAir ? Mathf.Clamp01( m_planet.GetAtmosphereDensity() ) : 0.0f;
		var airDensity = hasAir ? ( m_planet.IsGasGiant() ? c_gasGiantAir : Mathf.Lerp( c_thinnestAir, c_thickestAir, density ) ) : 0.0f;

		GetAir( out var airColor, out var airTint, out var cloudColor );

		foreach ( var material in new[] { m_surfaceMaterial, m_cloudMaterial, m_atmosphereMaterial } )
		{
			material.SetColor( "_AirColor", airColor );
			material.SetFloat( "_AirTint", airTint );
			material.SetFloat( "_AirDensity", airDensity );
			material.SetFloat( "_AirHeight", c_airHeight );
			material.SetFloat( "_AirScaleHeight", c_airScaleHeight );
			material.SetFloat( "_AirLight", c_airLight );
			material.SetFloat( "_AirThickness", hasAir ? Mathf.Lerp( 0.6f, 2.0f, density ) : 0.0f );
		}

		m_surfaceMaterial.SetTexture( c_albedoId, m_albedoMap );
		m_surfaceMaterial.SetTexture( c_normalMapId, m_normalMap );
		m_surfaceMaterial.SetTexture( c_cloudsId, HasClouds() ? (Texture) m_cloudMap : Texture2D.blackTexture );
		m_surfaceMaterial.SetFloat( "_CloudShadow", HasClouds() ? 0.6f : 0.0f );

		// the sea shows the sky above it
		m_surfaceMaterial.SetColor( "_SkyColor", Color.Lerp( c_gases[ "Nitrogen" ].m_color, airColor, airTint ) );

		// a molten world glows by its lava
		var gameData = DataController.m_instance.m_gameData;
		var hydrosphere = ( ( m_planet.m_hydrosphereId >= 0 ) && ( m_planet.m_hydrosphereId < gameData.m_hydrosphereList.Length ) ) ? gameData.m_hydrosphereList[ m_planet.m_hydrosphereId ].m_name : "None";

		m_surfaceMaterial.SetFloat( "_Molten", m_planet.IsMolten() ? 1.0f : 0.0f );

		// how big the maps are (close up the ground gets finer detail where a texel covers more than a pixel), and no such detail on a gas giant's clouds
		m_surfaceMaterial.SetVector( "_MapSize", new Vector4( m_mapWidth, m_mapWidth / 2, 0.0f, 0.0f ) );
		m_surfaceMaterial.SetFloat( "_GasGiant", m_planet.IsGasGiant() ? 1.0f : 0.0f );
		m_surfaceMaterial.SetColor( "_LavaColor", GetLavaColor( hydrosphere ) );
		m_surfaceMaterial.SetFloat( "_LavaIntensity", c_lavaIntensity );

		// (the clouds of a molten world are ash)
		m_cloudMaterial.SetTexture( c_cloudsId, m_cloudMap );
		m_cloudMaterial.SetColor( "_CloudColor", m_planet.IsMolten() ? c_ashClouds : cloudColor );
	}

	// turns the clouds round a little faster than the ground (the turn of the planet in degrees)
	public void SetCloudDrift( float planetAngle )
	{
		var drift = Mathf.Repeat( planetAngle / 360.0f * c_cloudDrift, 1.0f );

		if ( m_surfaceMaterial != null )
		{
			m_surfaceMaterial.SetFloat( c_cloudDriftId, drift );
		}

		if ( m_cloudMaterial != null )
		{
			m_cloudMaterial.SetFloat( c_cloudDriftId, drift );
		}
	}

	/// <summary>
	/// Puts the natural look of the planet's clouds and air on these (its ground's material goes on its model separately): the clouds only if the planet
	/// has weather, the halo only if it has air. Call it once the maps are made.
	/// </summary>
	public void ShowOn( MeshRenderer clouds, GameObject atmosphere, GameObject planetModel )
	{
		if ( !IsBaked() )
		{
			return;
		}

		var planetRadius = GetRadius( planetModel );

		// a round edge, where the air glows
		UseSmoothSphere( planetModel );

		if ( clouds != null )
		{
			var hasClouds = HasClouds() && ( planetRadius > 0.0f );

			clouds.gameObject.SetActive( hasClouds );

			if ( hasClouds )
			{
				PutOn( clouds, m_cloudMaterial );
				UseSmoothSphere( clouds.gameObject );

				// the cloud sphere's radius in planet radii, so that the shader knows where the ground is
				var properties = new MaterialPropertyBlock();

				properties.SetFloat( c_cloudScaleId, Mathf.Max( GetRadius( clouds.gameObject ) / planetRadius, 1.0f ) );

				clouds.SetPropertyBlock( properties );
			}
		}

		if ( atmosphere != null )
		{
			var hasAir = HasAir() && ( planetRadius > 0.0f ) && PlaceAirDome( atmosphere, planetModel, planetRadius );

			atmosphere.SetActive( hasAir );

			if ( hasAir && atmosphere.TryGetComponent<MeshRenderer>( out var atmosphereRenderer ) )
			{
				PutOn( atmosphereRenderer, m_atmosphereMaterial );

				atmosphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				atmosphereRenderer.receiveShadows = false;

				// where the planet is, for this dome (its middle is the planet's)
				var properties = new MaterialPropertyBlock();

				properties.SetFloat( c_planetRadiusId, planetRadius );
				properties.SetFloat( c_centerDepthId, 0.0f );

				atmosphereRenderer.SetPropertyBlock( properties );
			}
		}
	}

	/// <summary>
	/// Puts the halo of the air on a dome round the planet: the atmosphere object's quad swapped for a smooth sphere, centred on the planet and a little
	/// bigger than its air. The halo's shader draws only the dome's far side, which is behind the planet wherever the planet is (the planet works out the
	/// air in front of itself), and all round the camera when the camera is in the air, as at the start of a launch from the surface (a quad above the
	/// planet left the view below its own plane without any air).
	/// </summary>
	static bool PlaceAirDome( GameObject atmosphere, GameObject planetModel, float planetRadius )
	{
		var atmosphereFilter = atmosphere.GetComponent<MeshFilter>();
		var planetFilter = ( planetModel != null ) ? planetModel.GetComponent<MeshFilter>() : null;
		var planetRenderer = ( planetModel != null ) ? planetModel.GetComponent<Renderer>() : null;

		if ( ( atmosphereFilter == null ) || ( planetFilter == null ) || ( planetFilter.sharedMesh == null ) || ( planetRenderer == null ) )
		{
			return false;
		}

		ForgetDestroyedFilters();

		var planetMesh = s_originalMeshes.TryGetValue( planetFilter, out var original ) ? original : planetFilter.sharedMesh;
		var dome = GetSmoothSphere( planetMesh );

		if ( ( dome == null ) || ( dome.bounds.extents.x <= 0.0f ) )
		{
			return false;
		}

		if ( atmosphereFilter.sharedMesh != dome )
		{
			if ( !s_originalMeshes.ContainsKey( atmosphereFilter ) )
			{
				s_originalMeshes[ atmosphereFilter ] = atmosphereFilter.sharedMesh;
			}

			atmosphereFilter.sharedMesh = dome;
		}

		var parentScale = ( atmosphere.transform.parent != null ) ? atmosphere.transform.parent.lossyScale.x : 1.0f;

		if ( parentScale <= 0.0f )
		{
			return false;
		}

		atmosphere.transform.position = planetRenderer.bounds.center;
		atmosphere.transform.localScale = Vector3.one * ( planetRadius * ( 1.0f + c_airHeight ) * 1.02f / dome.bounds.extents.x / parentScale );

		return true;
	}

	// takes the natural look's materials off these and puts back the ones they had (call it before the original look is set up on them)
	public static void ShowOriginalOn( MeshRenderer clouds, GameObject atmosphere )
	{
		TakeOff( clouds );

		if ( clouds != null )
		{
			UseOriginalSphere( clouds.gameObject );
		}

		if ( ( atmosphere != null ) && atmosphere.TryGetComponent<MeshRenderer>( out var atmosphereRenderer ) )
		{
			TakeOff( atmosphereRenderer );

			// (the quad back in place of the dome; the original look puts it above the planet again)
			UseOriginalSphere( atmosphere );
		}
	}

	/// <summary>
	/// Draws a sphere model (a planet, its clouds) with a smooth copy of its mesh: each triangle cut into smaller ones and pushed out onto the sphere, so
	/// that the planet's edge is round where its air glows (the maps sit on the copy as they sit on the mesh it is made from).
	/// </summary>
	public static void UseSmoothSphere( GameObject model )
	{
		var filter = ( model != null ) ? model.GetComponent<MeshFilter>() : null;

		if ( ( filter == null ) || ( filter.sharedMesh == null ) )
		{
			return;
		}

		ForgetDestroyedFilters();

		var original = s_originalMeshes.TryGetValue( filter, out var known ) ? known : filter.sharedMesh;
		var smooth = GetSmoothSphere( original );

		if ( ( smooth == null ) || ( smooth == original ) )
		{
			return;
		}

		s_originalMeshes[ filter ] = original;

		filter.sharedMesh = smooth;
	}

	// draws a sphere model with the mesh it had before UseSmoothSphere again
	public static void UseOriginalSphere( GameObject model )
	{
		var filter = ( model != null ) ? model.GetComponent<MeshFilter>() : null;

		if ( ( filter == null ) || !s_originalMeshes.TryGetValue( filter, out var original ) )
		{
			return;
		}

		s_originalMeshes.Remove( filter );

		filter.sharedMesh = original;
	}

	// the smooth copy of a sphere mesh, made the first time it is asked for (null if the mesh cannot be read)
	static Mesh GetSmoothSphere( Mesh original )
	{
		if ( s_smoothSpheres.TryGetValue( original, out var smooth ) && ( smooth != null ) )
		{
			return smooth;
		}

		if ( !original.isReadable )
		{
			return null;
		}

		var vertices = original.vertices;
		var uvs = original.uv;
		var triangles = original.triangles;

		if ( ( vertices.Length == 0 ) || ( uvs.Length != vertices.Length ) || ( triangles.Length < 3 ) )
		{
			return null;
		}

		var center = original.bounds.center;
		var radius = original.bounds.extents.x * c_smoothSphereScale;
		var newVertices = new List<Vector3>();
		var newNormals = new List<Vector3>();
		var newUvs = new List<Vector2>();
		var newTriangles = new List<int>();

		// the points shared by two triangles are one vertex (a point of the seam of the maps, or of a pole, keeps one for each place on the maps)
		var welded = new Dictionary<(int, int, int, int, int), int>();
		var points = new int[ c_sphereSubdivisions + 1, c_sphereSubdivisions + 1 ];

		for ( var t = 0; t + 2 < triangles.Length; t += 3 )
		{
			var a = triangles[ t ];
			var b = triangles[ t + 1 ];
			var c = triangles[ t + 2 ];

			// the points of the triangle, i steps from its first corner towards the second and j towards the third
			for ( var i = 0; i <= c_sphereSubdivisions; i++ )
			{
				for ( var j = 0; i + j <= c_sphereSubdivisions; j++ )
				{
					var weightB = (float) i / c_sphereSubdivisions;
					var weightC = (float) j / c_sphereSubdivisions;
					var weightA = 1.0f - weightB - weightC;

					var direction = ( vertices[ a ] * weightA + vertices[ b ] * weightB + vertices[ c ] * weightC - center ).normalized;
					var position = center + direction * radius;
					var uv = uvs[ a ] * weightA + uvs[ b ] * weightB + uvs[ c ] * weightC;
					var key = ( Mathf.RoundToInt( position.x * 100000.0f ), Mathf.RoundToInt( position.y * 100000.0f ), Mathf.RoundToInt( position.z * 100000.0f ), Mathf.RoundToInt( uv.x * 100000.0f ), Mathf.RoundToInt( uv.y * 100000.0f ) );

					if ( !welded.TryGetValue( key, out var index ) )
					{
						index = newVertices.Count;

						newVertices.Add( position );
						newNormals.Add( direction );
						newUvs.Add( uv );

						welded[ key ] = index;
					}

					points[ i, j ] = index;
				}
			}

			// the smaller triangles, turned as the triangle they are cut from
			for ( var i = 0; i < c_sphereSubdivisions; i++ )
			{
				for ( var j = 0; i + j < c_sphereSubdivisions; j++ )
				{
					newTriangles.Add( points[ i, j ] );
					newTriangles.Add( points[ i + 1, j ] );
					newTriangles.Add( points[ i, j + 1 ] );

					if ( i + j + 1 < c_sphereSubdivisions )
					{
						newTriangles.Add( points[ i + 1, j ] );
						newTriangles.Add( points[ i + 1, j + 1 ] );
						newTriangles.Add( points[ i, j + 1 ] );
					}
				}
			}
		}

		smooth = new Mesh();

		smooth.name = original.name + " (smooth)";
		smooth.indexFormat = ( newVertices.Count > 65000 ) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
		smooth.SetVertices( newVertices );
		smooth.SetNormals( newNormals );
		smooth.SetUVs( 0, newUvs );
		smooth.SetTriangles( newTriangles, 0 );
		smooth.RecalculateTangents();
		smooth.RecalculateBounds();

		s_smoothSpheres[ original ] = smooth;

		return smooth;
	}

	// the mesh filters of a scene that has gone are not needed any more
	static void ForgetDestroyedFilters()
	{
		List<MeshFilter> destroyed = null;

		foreach ( var filter in s_originalMeshes.Keys )
		{
			if ( filter == null )
			{
				if ( destroyed == null )
				{
					destroyed = new List<MeshFilter>();
				}

				destroyed.Add( filter );
			}
		}

		if ( destroyed != null )
		{
			foreach ( var filter in destroyed )
			{
				s_originalMeshes.Remove( filter );
			}
		}
	}

	// the radius of a sphere model (its mesh's, scaled as it is drawn)
	static float GetRadius( GameObject model )
	{
		if ( model == null )
		{
			return 0.0f;
		}

		var meshFilter = model.GetComponentInChildren<MeshFilter>();

		if ( ( meshFilter == null ) || ( meshFilter.sharedMesh == null ) )
		{
			return 0.0f;
		}

		return meshFilter.sharedMesh.bounds.extents.x * meshFilter.transform.lossyScale.x;
	}

	// puts a material on a renderer, remembering the one it had
	static void PutOn( Renderer renderer, Material material )
	{
		if ( ( renderer == null ) || ( material == null ) || ( renderer.sharedMaterial == material ) )
		{
			return;
		}

		ForgetDestroyedRenderers();

		if ( !s_originalMaterials.ContainsKey( renderer ) )
		{
			s_originalMaterials[ renderer ] = renderer.sharedMaterial;
		}

		renderer.sharedMaterial = material;
	}

	// puts back the material a renderer had before the natural look's went on it
	static void TakeOff( Renderer renderer )
	{
		if ( ( renderer == null ) || !s_originalMaterials.TryGetValue( renderer, out var original ) )
		{
			return;
		}

		s_originalMaterials.Remove( renderer );

		renderer.sharedMaterial = original;
		renderer.SetPropertyBlock( null );
	}

	// the renderers of a scene that has gone are not needed any more
	static void ForgetDestroyedRenderers()
	{
		List<Renderer> destroyed = null;

		foreach ( var renderer in s_originalMaterials.Keys )
		{
			if ( renderer == null )
			{
				if ( destroyed == null )
				{
					destroyed = new List<Renderer>();
				}

				destroyed.Add( renderer );
			}
		}

		if ( destroyed != null )
		{
			foreach ( var renderer in destroyed )
			{
				s_originalMaterials.Remove( renderer );
			}
		}
	}

	// how much of the blueprint's land is still land, and its sea still sea, in the height map (read back at a lower size)
	static float MeasureCoastAgreement( RenderTexture heightMap, PlanetGenerator generator )
	{
		const int c_width = 512;
		const int c_height = 256;

		var small = RenderTexture.GetTemporary( c_width, c_height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear );
		var readBack = new Texture2D( c_width, c_height, TextureFormat.RFloat, false, true );
		var previous = RenderTexture.active;

		try
		{
			// point sampling, so that each pixel is one of the height map's and not a blend across the coast
			heightMap.filterMode = FilterMode.Point;

			Graphics.Blit( heightMap, small );

			RenderTexture.active = small;

			readBack.ReadPixels( new Rect( 0, 0, c_width, c_height ), 0, 0 );
			readBack.Apply();

			var heights = readBack.GetPixelData<float>( 0 );
			var elevation = generator.m_elevation;
			var elevationWidth = elevation.GetLength( 1 );
			var elevationHeight = elevation.GetLength( 0 );
			var agreeing = 0;

			for ( var y = 0; y < c_height; y++ )
			{
				for ( var x = 0; x < c_width; x++ )
				{
					var blueprintIsSea = elevation[ Mathf.Min( ( y * elevationHeight + elevationHeight / 2 ) / c_height, elevationHeight - 1 ), Mathf.Min( ( x * elevationWidth + elevationWidth / 2 ) / c_width, elevationWidth - 1 ) ] < generator.m_waterElevation;
					var bakedIsSea = heights[ y * c_width + x ] < 0.0f;

					if ( blueprintIsSea == bakedIsSea )
					{
						agreeing++;
					}
				}
			}

			return agreeing / (float) ( c_width * c_height );
		}
		finally
		{
			heightMap.filterMode = FilterMode.Bilinear;
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary( small );
			Object.Destroy( readBack );
		}
	}

	// makes one of the maps
	static RenderTexture MakeMap( string name, int width, int height, RenderTextureFormat format, bool linear, bool mipmaps )
	{
		var map = new RenderTexture( width, height, 0, format, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB );

		map.name = name;
		map.useMipMap = mipmaps;
		map.autoGenerateMips = mipmaps;
		map.wrapModeU = TextureWrapMode.Repeat;
		map.wrapModeV = TextureWrapMode.Clamp;
		map.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
		map.anisoLevel = mipmaps ? 4 : 0;

		map.Create();

		return map;
	}

	static void ReleaseMap( ref RenderTexture map )
	{
		if ( map != null )
		{
			map.Release();

			Object.Destroy( map );
		}

		map = null;
	}

	void ReleaseMaps()
	{
		ReleaseMap( ref m_albedoMap );
		ReleaseMap( ref m_normalMap );
		ReleaseMap( ref m_cloudMap );

		m_mapWidth = 0;
	}

	// lets go of the maps and the materials (unity never frees what was made at runtime by itself): call it once nothing shows them any more
	public void Release()
	{
		ReleaseMaps();

		foreach ( var material in new[] { m_surfaceMaterial, m_cloudMaterial, m_atmosphereMaterial } )
		{
			if ( material != null )
			{
				Object.Destroy( material );
			}
		}

		m_surfaceMaterial = null;
		m_cloudMaterial = null;
		m_atmosphereMaterial = null;
	}
}
