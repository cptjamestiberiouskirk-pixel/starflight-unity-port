
using UnityEngine;
using UnityEngine.UI;

public class SystemDisplay : ShipDisplay
{
	// the size of the star image in map units (its corona fades out before the edge)
	const float c_starImageSize = 340.0f;

	// the space around a ring or the ecosphere band inside its image, in map units
	const float c_ringPadding = 4.0f;

	// the smallest and the largest planet on the map, in map units (the size follows the mass, as in the star system)
	const float c_smallestPlanetSize = 18.0f;
	const float c_largestPlanetSize = 34.0f;

	// how much of the star field one tile of its texture covers, in map units
	const float c_starFieldTileSize = 512.0f;

	// how fast the planets turn, in turns per second
	const float c_planetSpinSpeed = 0.01f;

	// the shader properties set every frame
	static readonly int c_lightDirectionId = Shader.PropertyToID( "_LightDirection" );
	static readonly int c_spinId = Shader.PropertyToID( "_Spin" );

	// the orbit rings
	public GameObject[] m_orbitList;

	// the ring drawn on each orbit (it turns with its orbit)
	public Image[] m_ringList;

	// the planets
	public Image[] m_planetList;

	// the star
	public Image m_star;

	// the band of the ecosphere
	public Image m_ecosphere;

	// the star field behind the map
	public RawImage m_background;

	// the ship
	public Image m_player;

	// how far each planet has turned
	float[] m_planetSpin;

	// remember whether or not we have been initialized
	bool m_alreadyInitialized;

	// unity start
	public override void Start()
	{
		Initialize();
	}

	// unity update
	public override void Update()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the star data
		var star = gameData.m_starList[ playerData.m_general.m_currentStarId ];

		// the planets turn while the game runs
		var spin = SpaceflightController.m_instance.m_gameIsPaused ? 0.0f : Time.deltaTime * c_planetSpinSpeed;

		// update the positions of the planets
		var planetList = star.GetPlanetList();

		foreach ( var planet in planetList )
		{
			if ( ( planet != null ) && ( planet.m_id != -1 ) )
			{
				// validate orbit position is within bounds
				int orbitIndex = planet.m_orbitPosition - 1;
				if ( orbitIndex < 0 || orbitIndex >= m_orbitList.Length )
				{
					continue;
				}

				// get the orbit angle
				float orbitAngle = planet.GetOrbitAngle();

				// calculate the game object rotation
				Quaternion rotation = Quaternion.Euler( 0.0f, 0.0f, orbitAngle );

				// set the new rotation on the orbit game object
				m_orbitList[ orbitIndex ].transform.rotation = rotation;

				// the planet image turns with its orbit - keep it upright
				if ( orbitIndex < m_planetList.Length )
				{
					var planetImage = m_planetList[ orbitIndex ];

					planetImage.rectTransform.localRotation = Quaternion.Euler( 0.0f, 0.0f, -orbitAngle );

					// the light comes from the star, in the middle of the map
					var angle = orbitAngle * Mathf.Deg2Rad;

					planetImage.material.SetVector( c_lightDirectionId, new Vector4( Mathf.Sin( angle ), -Mathf.Cos( angle ), 0.0f, 0.0f ) );

					// turn the planet
					if ( ( m_planetSpin != null ) && ( orbitIndex < m_planetSpin.Length ) )
					{
						m_planetSpin[ orbitIndex ] = Mathf.Repeat( m_planetSpin[ orbitIndex ] + spin, 1.0f );

						planetImage.material.SetFloat( c_spinId, m_planetSpin[ orbitIndex ] );
					}
				}
			}
		}

		// get the position of the ship and convert it to map coordinates
		var position = SpaceflightController.m_instance.m_playerShip.GetPosition() * 256.0f / 8192.0f;

		// set the new position of the ship game object
		m_player.transform.localPosition = new Vector3( position.x, position.z, -30.0f );

		// point the ship marker where the ship is heading (the marker points up, along the z axis of the star system)
		var direction = playerData.m_general.m_currentDirection;

		if ( ( direction.x != 0.0f ) || ( direction.z != 0.0f ) )
		{
			m_player.transform.localRotation = Quaternion.Euler( 0.0f, 0.0f, Mathf.Atan2( -direction.x, direction.z ) * Mathf.Rad2Deg );
		}

		// hide the player dot if we are in orbit
		m_player.enabled = playerData.m_general.m_location != PD_General.Location.InOrbit;
	}

	// the system map display label
	public override string GetLabel()
	{
		return "System Map";
	}

	public void Initialize()
	{
		// have we been initialized yet?
		if ( !m_alreadyInitialized )
		{
			// no - clone materials so they don't get saved out when running in the editor
			m_star.material = new Material( m_star.material );
			m_ecosphere.material = new Material( m_ecosphere.material );

			foreach ( var planet in m_planetList )
			{
				planet.material = new Material( planet.material );
			}

			foreach ( var ring in m_ringList )
			{
				ring.material = new Material( ring.material );
			}

			// size the rings to their orbits (the ring is drawn by its shader at a fraction of the image)
			for ( var i = 0; i < m_ringList.Length; i++ )
			{
				var radius = GetOrbitRadius( i + 1 );
				var halfSize = radius + c_ringPadding;

				m_ringList[ i ].rectTransform.sizeDelta = new Vector2( halfSize * 2.0f, halfSize * 2.0f );
				m_ringList[ i ].material.SetFloat( "_Radius", radius / halfSize );
			}

			// the star image is large enough for its corona
			m_star.rectTransform.sizeDelta = new Vector2( c_starImageSize, c_starImageSize );
			m_star.material.SetFloat( "_Size", c_starImageSize );

			// how far each planet has turned
			m_planetSpin = new float[ m_planetList.Length ];

			// remember that we have been initialized
			m_alreadyInitialized = true;
		}
	}

	// call this to change the system currently being displayed
	public void ChangeSystem()
	{
		// make sure we have been initialized
		Initialize();

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the star data
		var star = gameData.m_starList[ playerData.m_general.m_currentStarId ];

		// change the color of the star based on the class (the colours of real stars of that class)
		var starColor = GetStarColor( star.m_class );

		m_star.material.SetColor( "_StarColor", starColor );

		// the planets are lit by the star - a little of its colour
		var lightColor = Color.Lerp( Color.white, starColor, 0.35f );

		// show the ecosphere of the star
		ShowEcosphere( star );

		// a part of the star field of its own for each star
		ShowStarField( star );

		// update each planet in the system
		for ( var i = 0; i < GD_Star.c_maxNumPlanets; i++ )
		{
			// guard the scene's arrays
			if ( ( i >= m_orbitList.Length ) || ( i >= m_planetList.Length ) )
			{
				break;
			}

			var planet = SpaceflightController.m_instance.m_starSystem.m_planetController[ i ].m_planet;

			// is there a planet?
			if ( ( planet == null ) || ( planet.m_id == -1 ) )
			{
				// no - disable the orbit game object
				m_orbitList[ i ].SetActive( false );
			}
			else
			{
				// yes - enable the orbit game object
				m_orbitList[ i ].SetActive( true );

				var planetImage = m_planetList[ i ];

				// the size of the planet follows its mass
				var size = GetPlanetSize( planet );

				planetImage.rectTransform.sizeDelta = new Vector2( size, size );

				// the light of the star and the atmosphere of the planet
				planetImage.material.SetColor( "_LightColor", lightColor );
				planetImage.material.SetColor( "_AtmosphereColor", GetAtmosphereRimColor( planet ) );

				// the maps of the planet are made after this - until then it is a plain ball in the colour of its surface
				var surface = planet.GetSurface();

				planetImage.material.SetTexture( "_PlanetTex", Texture2D.whiteTexture );
				planetImage.color = new Color( surface.m_colorR / 255.0f, surface.m_colorG / 255.0f, surface.m_colorB / 255.0f );

				// each planet starts turned its own way
				if ( i < m_planetSpin.Length )
				{
					m_planetSpin[ i ] = Mathf.Repeat( planet.m_id * 0.137f, 1.0f );
				}
			}
		}
	}

	// call this once the maps of the planets of the current system have been made (StarSystem.GeneratePlanets)
	public void ShowPlanetMaps()
	{
		// make sure we have been initialized
		Initialize();

		for ( var i = 0; i < GD_Star.c_maxNumPlanets; i++ )
		{
			// guard the scene's array
			if ( i >= m_planetList.Length )
			{
				break;
			}

			var planetController = SpaceflightController.m_instance.m_starSystem.m_planetController[ i ];

			// is there a planet in this orbit?
			if ( ( planetController.m_planet == null ) || ( planetController.m_planet.m_id == -1 ) )
			{
				continue;
			}

			// its albedo map (a plain grey one for a planet that could not be mapped)
			var planetGenerator = planetController.GetPlanetGenerator();

			if ( ( planetGenerator == null ) || ( planetGenerator.m_albedoTexture == null ) )
			{
				continue;
			}

			m_planetList[ i ].material.SetTexture( "_PlanetTex", planetGenerator.m_albedoTexture );
			m_planetList[ i ].color = Color.white;
		}
	}

	// the radius of an orbit on the map, in map units (as GD_Planet.GetPosition places the planets)
	public static float GetOrbitRadius( int orbitPosition )
	{
		return Mathf.Lerp( 50.0f, 225.0f, ( orbitPosition - 1 ) / 7.0f );
	}

	// the size of a planet on the map, in map units (the same curve as GD_Planet.GetScale)
	public static float GetPlanetSize( GD_Planet planet )
	{
		return Mathf.Lerp( c_smallestPlanetSize, c_largestPlanetSize, Mathf.Sqrt( Mathf.Max( 0.0f, planet.m_mass - 6.0f ) / 500000.0f ) );
	}

	// the colour of a star of the given spectral class (close to the colour of real stars of that class)
	public static Color GetStarColor( string spectralClass )
	{
		switch ( spectralClass )
		{
			case "M": return new Color( 1.0f, 0.52f, 0.32f );
			case "K": return new Color( 1.0f, 0.70f, 0.42f );
			case "G": return new Color( 1.0f, 0.88f, 0.60f );
			case "F": return new Color( 1.0f, 0.97f, 0.90f );
			case "A": return new Color( 0.80f, 0.85f, 1.0f );
			case "B": return new Color( 0.68f, 0.76f, 1.0f );
			case "O": return new Color( 0.61f, 0.69f, 1.0f );
			default: return new Color( 1.0f, 1.0f, 1.0f );
		}
	}

	// the colour of the rim of a planet's atmosphere on the map (its alpha is the strength)
	static Color GetAtmosphereRimColor( GD_Planet planet )
	{
		// a gas giant is all atmosphere - a pale rim
		if ( planet.IsGasGiant() )
		{
			return new Color( 0.85f, 0.72f, 0.95f, 0.6f );
		}

		// no atmosphere, no rim
		if ( !planet.HasAtmosphere() )
		{
			return new Color( 0.0f, 0.0f, 0.0f, 0.0f );
		}

		// the hue of the planet's atmosphere (as the star system shows it), paler, as strong as the atmosphere is dense
		Color.RGBToHSV( planet.GetAtmosphereColor(), out var hue, out var saturation, out var value );

		var color = Color.Lerp( Color.HSVToRGB( hue, 1.0f, 1.0f ), Color.white, 0.45f );

		color.a = Mathf.Clamp01( planet.GetAtmosphereDensity() * 1.2f );

		return color;
	}

	// show the band of the ecosphere of a star (its orbits come from the spectral class)
	void ShowEcosphere( GD_Star star )
	{
		var spectralClass = star.GetSpectralClass();

		// hide the band if the star has no ecosphere on the map
		if ( ( spectralClass == null ) || ( spectralClass.m_ecosphereMin < 1 ) || ( spectralClass.m_ecosphereMax > GD_Star.c_maxNumPlanets ) || ( spectralClass.m_ecosphereMin > spectralClass.m_ecosphereMax ) )
		{
			m_ecosphere.enabled = false;

			return;
		}

		m_ecosphere.enabled = true;

		// the band reaches half way to the orbits on either side
		var innerRadius = GetOrbitRadius( spectralClass.m_ecosphereMin ) - 12.5f;
		var outerRadius = GetOrbitRadius( spectralClass.m_ecosphereMax ) + 12.5f;
		var halfSize = outerRadius + c_ringPadding * 2.0f;

		m_ecosphere.rectTransform.sizeDelta = new Vector2( halfSize * 2.0f, halfSize * 2.0f );

		m_ecosphere.material.SetFloat( "_InnerRadius", innerRadius / halfSize );
		m_ecosphere.material.SetFloat( "_OuterRadius", outerRadius / halfSize );
		m_ecosphere.material.SetFloat( "_Softness", 6.0f / halfSize );
		m_ecosphere.material.SetFloat( "_EdgeWidth", 1.2f / halfSize );
	}

	// show the part of the star field that belongs to a star
	void ShowStarField( GD_Star star )
	{
		// the star field covers the whole display
		var size = m_background.rectTransform.rect.size;

		// guard against a display that has not been laid out yet
		if ( ( size.x <= 0.0f ) || ( size.y <= 0.0f ) )
		{
			size = new Vector2( c_starFieldTileSize, c_starFieldTileSize );
		}

		// the texture tiles, so each star can look at its own part of it
		var offset = new Vector2( Mathf.Repeat( star.m_id * 0.618034f, 1.0f ), Mathf.Repeat( star.m_id * 0.414214f, 1.0f ) );

		m_background.uvRect = new Rect( offset, size / c_starFieldTileSize );
	}
}
