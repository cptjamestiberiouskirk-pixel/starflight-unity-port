
using UnityEngine;

public class Planet : MonoBehaviour
{
	// what the player is told about a planet whose maps could not be generated
	public const string c_couldNotBeMappedMessage = "<color=white>The surface of this planet could not be mapped.</color>";

	// the current planet this controller owns
	public GD_Planet m_planet;

	// access to the planet model
	public GameObject m_planetModel;

	// access to the planet clouds
	public MeshRenderer m_planetClouds;

	// access to the planet atmosphere
	public GameObject m_planetAtmosphere;

	// access to the starport model
	public GameObject m_starportModel;

	// access to the mesh renderer (to get to the material)
	MeshRenderer m_meshRenderer;

	// set this to the material for this planet model
	Material m_material;

	// whether or not we have finished generating maps for this planet
	bool m_mapsGenerated;

	// the planet generator
	PlanetGenerator m_planetGenerator;

	// the planet generator whose maps are on the material right now (that of the planet this controller had before, until the maps of the new planet are ready)
	PlanetGenerator m_shownPlanetGenerator;

	// the planet in its natural look (made once its maps are generated, when the player has picked that look: PlanetLook)
	NaturalPlanet m_naturalPlanet;

	// the current rotation
	float m_currentRotationAngle;

	// the path of the planet's orbit in the star system: a faint ring round the star at the height of the planet's middle, brightest just behind the planet
	// (made in Awake, filled in when the planet is shown; the planet's sphere hides the part of the ring that passes behind it)
	MeshRenderer m_orbitPath;
	Mesh m_orbitPathMesh;
	Vector3[] m_orbitPathVertices;
	MaterialPropertyBlock m_orbitPathProperties;

	// one material for every orbit path (each planet sets its own angle and colour)
	static Material s_orbitPathMaterial;

	// how many pieces the ring is made of, and how wide it is (world units)
	const int c_orbitPathSegments = 720;
	const float c_orbitPathWidth = 2.5f;

	// how bright the orbit path is, in the colour of the star
	const float c_orbitPathStrength = 0.25f;

	static readonly int c_orbitPathColorId = Shader.PropertyToID( "_Color" );
	static readonly int c_orbitPathAngleId = Shader.PropertyToID( "_PlanetAngle" );
	static readonly int c_orbitPathStrengthId = Shader.PropertyToID( "_Strength" );

	// unity awake
	void Awake()
	{
		MakeOrbitPath();

		// the planet takes the look the player picks at once
		PlanetLook.Changed += ApplyLook;
	}

	// unity start
	void Start()
	{
	}

	// unity update
	void Update()
	{
		// update the rotation angle
		m_currentRotationAngle += Time.deltaTime * SpaceflightController.m_instance.m_planetRotationSpeed;

		if ( m_currentRotationAngle >= 360.0f )
		{
			m_currentRotationAngle -= 360.0f;
		}

		// update the rotation of the planet
		m_planetModel.transform.localRotation = Quaternion.AngleAxis( -30.0f, Vector3.right ) * Quaternion.AngleAxis( m_currentRotationAngle, Vector3.forward );

		// the clouds of the natural look go round a little faster than the ground
		if ( m_naturalPlanet != null )
		{
			m_naturalPlanet.SetCloudDrift( m_currentRotationAngle );
		}

		// update the rotation of the starport
		if ( m_starportModel != null )
		{
			m_starportModel.transform.localRotation = Quaternion.Euler( -90.0f, 0.0f, m_currentRotationAngle * SpaceflightController.m_instance.m_starportRotationSpeedMultiplier );
		}
	}

	// unity on destroy
	void OnDestroy()
	{
		PlanetLook.Changed -= ApplyLook;

		// the maps would stay in memory until unity next clears out its unused assets
		ReleaseUnshownMaps();
		ReleaseShownMaps();
		ReleaseNaturalLook();

		// so would the mesh of the orbit path (the material is one for every planet, and stays)
		if ( m_orbitPathMesh != null )
		{
			Destroy( m_orbitPathMesh );
		}
	}

	// destroys the maps of the current planet generator if they never made it onto the material (nothing shows them then)
	void ReleaseUnshownMaps()
	{
		if ( ( m_planetGenerator != null ) && ( m_planetGenerator != m_shownPlanetGenerator ) )
		{
			m_planetGenerator.Release();
		}
	}

	// destroys the maps that are on the material (call this when they have been replaced or the planet is no longer shown)
	void ReleaseShownMaps()
	{
		if ( m_shownPlanetGenerator != null )
		{
			m_shownPlanetGenerator.Release();

			m_shownPlanetGenerator = null;
		}
	}

	// call this before you enable the planet
	public void InitializePlanet( GD_Planet planet )
	{
		// we are done with the planet generator we had - the maps that are on the material stay until the maps of the new planet are ready (see GenerateMaps)
		ReleaseUnshownMaps();

		m_planetGenerator = null;

		// and with the natural look of the planet we had (its original material goes back on the model, as the original look's maps stay until the new ones
		// are ready)
		if ( m_naturalPlanet != null )
		{
			ShowOriginalLook();
			ReleaseNaturalLook();
		}

		// check if we have a planet
		if ( ( planet == null ) || ( planet.m_id == -1 ) )
		{
			// nope - forget this planet
			m_planet = null;

			// we don't need to generate maps for this planet
			m_mapsGenerated = true;

			// nothing is shown in this orbit now, so the maps on the material can go right away
			ReleaseShownMaps();

			// don't do anything more here
			return;
		}

		// yep - remember the planet we are controlling
		m_planet = planet;

		// get the mesh renderer component
		m_meshRenderer = m_planetModel.GetComponent<MeshRenderer>();

		// grab the material from the mesh renderer and make a clone of it
		m_material = m_meshRenderer.material;

		// start the maps generation process
		m_planetGenerator = new PlanetGenerator();

		m_planetGenerator.Start( m_planet );

		m_mapsGenerated = false;

		// set the current position of the planet
		transform.localPosition = m_planet.GetPosition();

		// set the initial rotation angle of the planet
		m_currentRotationAngle = m_planet.m_orbitPosition;
	}

	// disable a planet
	public void DisablePlanet()
	{
		// disable the game object
		gameObject.SetActive( false );
	}

	// change the planet we are controlling
	public void EnablePlanet()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// show this orbit
		gameObject.SetActive( true );

		// show or hide the starport model depending on whether or not this planet is Arth (and not after the flare of Arth's sun has destroyed it)
		if ( m_starportModel != null )
		{
			m_starportModel.SetActive( ( m_planet.m_id == gameData.m_misc.m_arthPlanetId ) && !StarSystem.IsDestroyedStarport( m_planet.m_id ) );
		}

		// scale the planet based on its mass
		m_planetModel.transform.localScale = m_planet.GetScale();
		m_planetAtmosphere.transform.localScale = m_planetModel.transform.localScale * 1.055f;

		// move the planet to be just below the zero plane (and do the same to the atmosphere)
		var position = Vector3.up * ( -16.0f - m_planetModel.transform.localScale.y );
		m_planetModel.transform.localPosition = position;
		m_planetAtmosphere.transform.localPosition = position * 0.85f;

		// the path of its orbit, through the planet's middle
		ShowOrbitPath( position.y );

		// set up the clouds and atmosphere
		SetupClouds( m_planetClouds, m_planetAtmosphere, false, false );

		// in the look the player has picked
		ApplyLook();
	}

	// makes the ring of the orbit path: its triangles and its u (once round the orbit) and v (across) are always the same, its vertices are filled in when the
	// planet is shown
	void MakeOrbitPath()
	{
		var shader = Shader.Find( "Starflight/Orbit Path" );

		if ( shader == null )
		{
			return;
		}

		if ( s_orbitPathMaterial == null )
		{
			s_orbitPathMaterial = new Material( shader );
		}

		var pathObject = new GameObject( "Orbit Path" );

		pathObject.layer = gameObject.layer;
		pathObject.transform.SetParent( transform, false );

		var vertexCount = ( c_orbitPathSegments + 1 ) * 2;

		m_orbitPathVertices = new Vector3[ vertexCount ];

		var uv = new Vector2[ vertexCount ];
		var triangles = new int[ c_orbitPathSegments * 6 ];

		for ( var i = 0; i <= c_orbitPathSegments; i++ )
		{
			var u = (float) i / c_orbitPathSegments;

			uv[ i * 2 ] = new Vector2( u, 0.0f );
			uv[ i * 2 + 1 ] = new Vector2( u, 1.0f );

			if ( i < c_orbitPathSegments )
			{
				var t = i * 6;
				var v = i * 2;

				triangles[ t ] = v;
				triangles[ t + 1 ] = v + 1;
				triangles[ t + 2 ] = v + 2;
				triangles[ t + 3 ] = v + 1;
				triangles[ t + 4 ] = v + 3;
				triangles[ t + 5 ] = v + 2;
			}
		}

		m_orbitPathMesh = new Mesh();

		m_orbitPathMesh.name = "Orbit Path";
		m_orbitPathMesh.vertices = m_orbitPathVertices;
		m_orbitPathMesh.uv = uv;
		m_orbitPathMesh.triangles = triangles;

		pathObject.AddComponent<MeshFilter>().sharedMesh = m_orbitPathMesh;

		m_orbitPath = pathObject.AddComponent<MeshRenderer>();

		m_orbitPath.sharedMaterial = s_orbitPathMaterial;
		m_orbitPath.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		m_orbitPath.receiveShadows = false;

		m_orbitPathProperties = new MaterialPropertyBlock();
	}

	// puts the orbit path round the star, at this height (the planet's middle, below the plane of the star system), in the colour of the star
	void ShowOrbitPath( float height )
	{
		if ( ( m_orbitPath == null ) || ( m_planet == null ) )
		{
			return;
		}

		// the planet is at its place in the orbit: the ring's middle is the star
		var planetPosition = transform.localPosition;
		var radius = new Vector2( planetPosition.x, planetPosition.z ).magnitude;

		m_orbitPath.transform.localPosition = new Vector3( -planetPosition.x, height, -planetPosition.z );

		// the angle goes round as GD_Planet.GetPosition's does
		for ( var i = 0; i <= c_orbitPathSegments; i++ )
		{
			var angle = ( (float) i / c_orbitPathSegments ) * Mathf.PI * 2.0f;
			var direction = new Vector3( -Mathf.Sin( angle ), 0.0f, Mathf.Cos( angle ) );

			m_orbitPathVertices[ i * 2 ] = direction * ( radius - c_orbitPathWidth * 0.5f );
			m_orbitPathVertices[ i * 2 + 1 ] = direction * ( radius + c_orbitPathWidth * 0.5f );
		}

		m_orbitPathMesh.vertices = m_orbitPathVertices;
		m_orbitPathMesh.RecalculateBounds();

		// where the planet is on the ring, and the colour of its star
		var gameData = DataController.m_instance.m_gameData;
		var starColor = ( ( m_planet.m_starId >= 0 ) && ( m_planet.m_starId < gameData.m_starList.Length ) ) ? SystemDisplay.GetStarColor( gameData.m_starList[ m_planet.m_starId ].m_class ) : Color.white;

		m_orbitPathProperties.SetColor( c_orbitPathColorId, starColor );
		m_orbitPathProperties.SetFloat( c_orbitPathAngleId, Mathf.Repeat( Mathf.Atan2( -planetPosition.x, planetPosition.z ), Mathf.PI * 2.0f ) );
		m_orbitPathProperties.SetFloat( c_orbitPathStrengthId, c_orbitPathStrength );

		m_orbitPath.SetPropertyBlock( m_orbitPathProperties );
	}

	// call this to get the distance to the player
	public float GetDistanceToPlayer()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// return the distance from the player to the planet
		return Vector3.Distance( playerData.m_general.m_coordinates, transform.localPosition );
	}

	// get the material for this planet
	public Material GetMaterial()
	{
		return m_material;
	}

	// get the surface id of this planet
	public int GetSurfaceId()
	{
		return m_planet.m_surfaceId;
	}

	// return true if all maps have been generated or false if not
	public bool MapsGenerated()
	{
		if ( m_planetGenerator == null )
		{
			return true;
		}

		return m_mapsGenerated;
	}

	// continue generating maps for this planet
	public float GenerateMaps()
	{
		var progress = m_planetGenerator.Process();

		if ( m_planetGenerator.m_abort )
		{
			m_mapsGenerated = true;

			// the maps of this planet could not be generated - show it as a plain grey ball
			// (it used to keep the maps of the planet that was in this orbit in the star system before, as if they were its own)
			m_planetGenerator.CreatePlaceholderTextures();

			// plain means no surface detail either
			m_material.DisableKeyword( "SF_DETAILNORMALMAP_ON" );

			ShowTextureMaps();
		}
		else if ( m_planetGenerator.m_mapsGenerated )
		{
			m_mapsGenerated = true;

			// is this a gas giant?
			if ( m_planet.IsGasGiant() )
			{
				// yes - turn off the detail normal map
				m_material.DisableKeyword( "SF_DETAILNORMALMAP_ON" );
			}
			else
			{
				// no - turn on the detail normal map
				m_material.EnableKeyword( "SF_DETAILNORMALMAP_ON" );
			}

			// does this planet have an atmosphere?
			if ( m_planet.HasAtmosphere() )
			{
				// yes - allow full detail normal map strength
				m_material.SetFloat( "SF_DetailNormalMapStrength", 1.0f );
			}
			else
			{
				// no - make detail normal map strength weak so craters are more apparent
				m_material.SetFloat( "SF_DetailNormalMapStrength", 0.05f );
			}

			ShowTextureMaps();

			// in the look the player has picked (the natural look's maps are made from the generator's now)
			ApplyLook();
		}

		return progress;
	}

	// true if this planet is drawn in its natural look: the player has picked it, it is made for this kind of planet, the graphics card can make its maps,
	// and the planet's own maps (its blueprint) are ready
	public bool ShowsNaturalLook()
	{
		return PlanetLook.IsNatural() && NaturalPlanet.Suits( m_planet ) && HasMaps() && NaturalPlanet.IsSupported();
	}

	// the natural look of this planet, its maps made at least this wide (false if they could not be made: it keeps its original look then)
	bool MakeNaturalLook( int width )
	{
		if ( m_naturalPlanet == null )
		{
			m_naturalPlanet = new NaturalPlanet( m_planet );
		}

		return m_naturalPlanet.Bake( m_planetGenerator, width );
	}

	// puts the look the player has picked on the planet's model, clouds and atmosphere in the star system
	void ApplyLook()
	{
		if ( ( m_planet == null ) || ( m_meshRenderer == null ) )
		{
			return;
		}

		if ( ShowsNaturalLook() && MakeNaturalLook( NaturalPlanet.c_starSystemMapWidth ) )
		{
			m_meshRenderer.sharedMaterial = m_naturalPlanet.GetSurfaceMaterial();

			m_naturalPlanet.ShowOn( m_planetClouds, m_planetAtmosphere, m_planetModel );
			m_naturalPlanet.SetCloudDrift( m_currentRotationAngle );
		}
		else
		{
			ShowOriginalLook();

			// the natural look's maps are not needed any more (the orbit's are big)
			ReleaseNaturalLook();
		}
	}

	// puts the original look back on the planet's model, clouds and atmosphere in the star system
	void ShowOriginalLook()
	{
		if ( ( m_meshRenderer != null ) && ( m_material != null ) )
		{
			m_meshRenderer.sharedMaterial = m_material;
		}

		NaturalPlanet.UseOriginalSphere( m_planetModel );

		if ( m_planet != null )
		{
			SetupOriginalCloudsAndAtmosphere( m_planetClouds, m_planetAtmosphere, m_planetModel );
		}
		else
		{
			NaturalPlanet.ShowOriginalOn( m_planetClouds, m_planetAtmosphere );
		}
	}

	// lets go of the natural look's maps and materials (call it once nothing shows them)
	void ReleaseNaturalLook()
	{
		if ( m_naturalPlanet != null )
		{
			m_naturalPlanet.Release();

			m_naturalPlanet = null;
		}
	}

	/// <summary>
	/// Puts this planet's look on the orbit's model, clouds and atmosphere: its natural look, with maps as detailed as orbit needs, when that is what is
	/// shown, else its original material, clouds and atmosphere.
	/// </summary>
	public void ApplyOrbitLook( MeshRenderer planetModel, MeshRenderer clouds, GameObject atmosphere )
	{
		if ( planetModel == null )
		{
			return;
		}

		if ( ShowsNaturalLook() && MakeNaturalLook( NaturalPlanet.GetOrbitMapWidth() ) )
		{
			planetModel.sharedMaterial = m_naturalPlanet.GetSurfaceMaterial();

			m_naturalPlanet.ShowOn( clouds, atmosphere, planetModel.gameObject );

			return;
		}

		if ( m_material != null )
		{
			planetModel.sharedMaterial = m_material;
		}

		NaturalPlanet.UseOriginalSphere( planetModel.gameObject );

		if ( m_planet != null )
		{
			SetupOriginalCloudsAndAtmosphere( clouds, atmosphere, planetModel.gameObject );
		}
	}

	// turns the clouds of the natural look round a little faster than the ground (the turn of the planet in degrees; the orbit turns its own model)
	public void SetCloudDrift( float planetAngle )
	{
		if ( m_naturalPlanet != null )
		{
			m_naturalPlanet.SetCloudDrift( planetAngle );
		}
	}

	// the material the planet is drawn with as large as in orbit (under an encounter that began in orbit): its natural one, with maps as detailed as orbit
	// needs, when it shows the natural look, else its original one
	public Material GetOrbitViewMaterial()
	{
		if ( ShowsNaturalLook() && MakeNaturalLook( NaturalPlanet.GetOrbitMapWidth() ) )
		{
			return m_naturalPlanet.GetSurfaceMaterial();
		}

		return m_material;
	}

	// true if the natural look's maps are the ones to show (it is shown, and its maps are made)
	bool NaturalMapsShown()
	{
		return ShowsNaturalLook() && ( m_naturalPlanet != null ) && m_naturalPlanet.IsBaked();
	}

	// the maps the console's displays show the planet with (the System Map, the sensor picture): its natural ones when it shows the natural look, else its
	// generator's (null while there are none); the landing map keeps the generator's maps in either look, since its colours are the heights its legend
	// explains
	public Texture GetDisplayAlbedoMap()
	{
		if ( NaturalMapsShown() )
		{
			return m_naturalPlanet.GetAlbedoMap();
		}

		return ( m_planetGenerator != null ) ? m_planetGenerator.m_albedoTexture : null;
	}

	public Texture GetDisplayNormalMap()
	{
		if ( NaturalMapsShown() )
		{
			return m_naturalPlanet.GetNormalMap();
		}

		return ( m_planetGenerator != null ) ? m_planetGenerator.m_normalTexture : null;
	}

	// (the natural maps keep the open sea in the albedo's alpha, which the displays do not read: no glint on their sea)
	public Texture GetDisplayWaterMaskMap()
	{
		if ( NaturalMapsShown() )
		{
			return Texture2D.blackTexture;
		}

		return ( m_planetGenerator != null ) ? m_planetGenerator.m_waterMaskTexture : null;
	}

	// the natural look of this planet (null when it is not shown; for the probe)
	public NaturalPlanet GetNaturalPlanet()
	{
		return m_naturalPlanet;
	}

	// puts the texture maps of the current planet generator on the material and destroys the ones that were on it
	void ShowTextureMaps()
	{
		m_material.SetTexture( "_MainTex", m_planetGenerator.m_albedoTexture );
		m_material.SetTexture( "SF_SpecularMap", m_planetGenerator.m_specularTexture );
		m_material.SetTexture( "SF_NormalMap", m_planetGenerator.m_normalTexture );
		m_material.SetTexture( "SF_WaterMaskMap", m_planetGenerator.m_waterMaskTexture );

		m_meshRenderer.material = m_material;

		// the maps of the planet that was shown before are off the material now, so destroy them (unity never frees a texture that was made at runtime by itself)
		if ( m_shownPlanetGenerator != m_planetGenerator )
		{
			ReleaseShownMaps();

			m_shownPlanetGenerator = m_planetGenerator;
		}

		SpaceflightController.m_instance.m_inOrbit.MaterialUpdated();
	}

	// get the current planet generator
	public PlanetGenerator GetPlanetGenerator()
	{
		return m_planetGenerator;
	}

	// true if this planet has its maps and its elevation data (false while they are being generated, and for good if the planet data could not be read)
	public bool HasMaps()
	{
		return ( m_planetGenerator != null ) && m_planetGenerator.m_mapsGenerated;
	}

	// true once it is known that the maps of this planet cannot be generated (its planet data is missing or damaged) - it is shown as a plain grey ball and has no surface to go down to
	// (this is false while the maps are still being generated, so it is not the opposite of HasMaps)
	public bool CouldNotBeMapped()
	{
		return ( m_planetGenerator != null ) && m_planetGenerator.m_abort;
	}

	// the planet atmosphere shader (in a Resources folder, so it is in every build: the atmosphere's material is made in code)
	static Shader m_atmosphereShader;

	// how high the atmosphere reaches above the planet (a share of its radius), and how much bigger than the halo the quad it is drawn on is (the camera looks at it at a slant)
	const float c_atmosphereHeight = 0.12f;
	const float c_atmosphereQuadMargin = 1.3f;

	// how far above the planet's top the quad is (a share of the planet's radius): in front of the planet, below the ships
	const float c_atmosphereQuadLift = 0.02f;

	/// <summary>
	/// Draw a planet's atmosphere on its quad: a halo that fades from the planet's edge into space and a haze over the planet that thickens towards its edge, lit by the sun.
	/// The quad is put just above the planet and made big enough for the halo; the shader works out the rest from the line of sight.
	/// </summary>
	public static void FitAtmosphere( GameObject atmosphere, GameObject planetModel, Color atmosphereColor, float atmosphereDensity )
	{
		if ( ( atmosphere == null ) || ( planetModel == null ) )
		{
			return;
		}

		var atmosphereRenderer = atmosphere.GetComponent<MeshRenderer>();

		if ( atmosphereRenderer == null )
		{
			return;
		}

		if ( m_atmosphereShader == null )
		{
			m_atmosphereShader = Resources.Load<Shader>( "Shaders/Planet Atmosphere" );

			if ( m_atmosphereShader == null )
			{
				Debug.LogWarning( "Planet: the planet atmosphere shader was not found" );
				return;
			}
		}

		// a material of its own for this atmosphere, made once (each atmosphere has its own size and colour)
		if ( ( atmosphereRenderer.sharedMaterial == null ) || ( atmosphereRenderer.sharedMaterial.shader != m_atmosphereShader ) )
		{
			var material = new Material( m_atmosphereShader );
			material.name = "Planet Atmosphere";

			atmosphereRenderer.sharedMaterial = material;
			atmosphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			atmosphereRenderer.receiveShadows = false;
		}

		// the quad just above the planet, big enough for the halo
		if ( !PlaceAtmosphere( atmosphere, planetModel, out var planetRadius, out var lift ) )
		{
			return;
		}

		var atmosphereHeight = planetRadius * c_atmosphereHeight;

		// the game data's atmosphere colours are often dark, and the atmosphere adds its light: bring the colour up to full brightness, and let the density say how strong it is
		var brightest = Mathf.Max( atmosphereColor.r, Mathf.Max( atmosphereColor.g, atmosphereColor.b ) );
		var color = ( brightest > 0.001f ) ? atmosphereColor / brightest : new Color( 0.6f, 0.75f, 1.0f );

		color.a = 1.0f;

		var shared = atmosphereRenderer.sharedMaterial;

		shared.SetColor( "SF_AlbedoColor", color );
		shared.SetFloat( "_AtmosphereStrength", Mathf.Lerp( 0.35f, 1.1f, Mathf.Clamp01( atmosphereDensity ) ) );
		shared.SetFloat( "_PlanetRadius", planetRadius );
		shared.SetFloat( "_AtmosphereHeight", atmosphereHeight );
		shared.SetFloat( "_CenterDepth", lift );
	}

	/// <summary>
	/// Puts a planet's atmosphere quad just above the planet, so that it is in front of the planet, and makes it big enough for the halo. Gives the planet's
	/// radius and how far above the planet's middle the quad is (both in world units); false if the planet or the quad has no mesh.
	/// </summary>
	public static bool PlaceAtmosphere( GameObject atmosphere, GameObject planetModel, out float planetRadius, out float lift )
	{
		planetRadius = 0.0f;
		lift = 0.0f;

		if ( ( atmosphere == null ) || ( planetModel == null ) )
		{
			return false;
		}

		var atmosphereMesh = atmosphere.GetComponent<MeshFilter>();
		var planetRenderer = planetModel.GetComponentInChildren<Renderer>();
		var planetMesh = planetModel.GetComponentInChildren<MeshFilter>();

		if ( ( atmosphereMesh == null ) || ( atmosphereMesh.sharedMesh == null ) || ( planetRenderer == null ) || ( planetMesh == null ) || ( planetMesh.sharedMesh == null ) )
		{
			return false;
		}

		// how big the planet is (from its mesh and its scale: the bounds of a renderer are the mesh's box turned with it, up to 41% too big for a turned sphere), and where its middle is
		var planetBounds = planetRenderer.bounds;

		planetRadius = planetMesh.sharedMesh.bounds.extents.x * planetMesh.transform.lossyScale.x;

		if ( planetRadius <= 0.0f )
		{
			return false;
		}

		var atmosphereHeight = planetRadius * c_atmosphereHeight;

		// the quad just above the planet's top, so that it is in front of the planet
		lift = planetRadius * ( 1.0f + c_atmosphereQuadLift );

		atmosphere.transform.position = planetBounds.center + Vector3.up * lift;

		// big enough for the halo (the quad mesh is flat: its half size is its largest extent)
		var meshExtents = atmosphereMesh.sharedMesh.bounds.extents;
		var meshHalfSize = Mathf.Max( meshExtents.x, Mathf.Max( meshExtents.y, meshExtents.z ) );
		var parentScale = ( atmosphere.transform.parent != null ) ? atmosphere.transform.parent.lossyScale.x : 1.0f;

		if ( ( meshHalfSize > 0.0f ) && ( parentScale > 0.0f ) )
		{
			atmosphere.transform.localScale = Vector3.one * ( ( planetRadius + atmosphereHeight ) * c_atmosphereQuadMargin / meshHalfSize / parentScale );
		}

		return true;
	}

	// sets up the clouds based on planet properties (the planet model is the one the atmosphere goes round: this planet's own if none is given)
	public void SetupClouds( MeshRenderer planetClouds, GameObject planetAtmosphere, bool updateSkybox, bool enableFog, GameObject planetModel = null )
	{
		// the clouds and the atmosphere in the original look
		SetupOriginalCloudsAndAtmosphere( planetClouds, planetAtmosphere, planetModel );

		// does the planet have an atmosphere?
		if ( m_planet.HasAtmosphere() )
		{
			// get the primary atmosphere of the planet
			var atmosphereColor = m_planet.GetAtmosphereColor();

			// get the atmosphere density
			var atmosphereDensity = m_planet.GetAtmosphereDensity();

			// update the skybox if needed
			if ( updateSkybox )
			{
				// blend to the the planet skybox
				StarflightSkybox.m_instance.SwitchSkyboxTextures( "B", "planet" );

				// set the color tint
				StarflightSkybox.m_instance.m_colorTintB = atmosphereColor;
			}

			// also do fog settings
			if ( enableFog )
			{
				RenderSettings.fog = true;
				RenderSettings.fogColor = atmosphereColor;
				RenderSettings.fogMode = FogMode.Linear;
				RenderSettings.fogDensity = 1.0f;
				RenderSettings.fogStartDistance = 1000.0f - atmosphereDensity * 950.0f;
				RenderSettings.fogEndDistance = 1000.0f;

				// Debug.Log( "Fog enabled, atmospheric density = " + atmosphereDensity );
			}
		}
		else
		{
			if ( updateSkybox )
			{
				StarflightSkybox.m_instance.SwitchSkyboxTextures( "B", "planet" );

				StarflightSkybox.m_instance.m_colorTintB = Color.black;
			}

			// we want to turn off the fog
			enableFog = false;
		}

		// turn off the fog if it is not enabled
		if ( !enableFog )
		{
			RenderSettings.fog = false;
			RenderSettings.fogColor = Color.magenta;
			RenderSettings.fogMode = FogMode.Linear;
			RenderSettings.fogDensity = 0.0f;
			RenderSettings.fogStartDistance = 50.0f;
			RenderSettings.fogEndDistance = 1000.0f;
		}
	}

	// shows or hides the clouds and the atmosphere in the original look, by whether the planet has an atmosphere (the natural look's materials come off them
	// first; the planet model is the one the atmosphere goes round: this planet's own if none is given)
	void SetupOriginalCloudsAndAtmosphere( MeshRenderer planetClouds, GameObject planetAtmosphere, GameObject planetModel )
	{
		NaturalPlanet.ShowOriginalOn( planetClouds, planetAtmosphere );

		if ( planetClouds == null )
		{
			return;
		}

		// does the planet have an atmosphere?
		if ( m_planet.HasAtmosphere() )
		{
			// yes - show the clouds
			planetClouds.gameObject.SetActive( true );

			// show the atmosphere
			if ( planetAtmosphere != null )
			{
				planetAtmosphere.SetActive( true );
			}

			// get the primary atmosphere of the planet
			var atmosphereColor = m_planet.GetAtmosphereColor();

			// get the atmosphere density
			var atmosphereDensity = m_planet.GetAtmosphereDensity();

			// set the density of the clouds
			planetClouds.material.SetFloat( "SF_Density", 2.0f - atmosphereDensity * 1.25f );

			// pick the color for the clouds
			if ( m_planet.IsMolten() )
			{
				planetClouds.material.SetColor( "SF_AlbedoColor", new Color( 0.01f, 0.01f, 0.01f ) );
				planetClouds.material.SetColor( "SF_SpecularColor", new Color( 0.25f, 0.01f, 0.01f ) );
			}
			else
			{
				var cloudSpecularColor = m_planet.GetCloudSpecularColor();

				planetClouds.material.SetColor( "SF_AlbedoColor", new Color( 1.0f, 1.0f, 1.0f ) );
				planetClouds.material.SetColor( "SF_SpecularColor", cloudSpecularColor );
			}

			// set the opacity of the clouds based on the density
			Tools.SetOpacity( planetClouds.material, atmosphereDensity * 0.5f + 0.5f );

			// draw the atmosphere round the planet, in its colour
			if ( planetAtmosphere != null )
			{
				FitAtmosphere( planetAtmosphere, ( planetModel != null ) ? planetModel : m_planetModel, atmosphereColor, atmosphereDensity );
			}
		}
		else
		{
			// no - hide the clouds
			planetClouds.gameObject.SetActive( false );

			// hide the atmosphere
			if ( planetAtmosphere != null )
			{
				planetAtmosphere.SetActive( false );
			}
		}
	}
}
