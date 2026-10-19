
using UnityEngine;
using System.Collections.Generic;

public class Hyperspace : MonoBehaviour
{
	// our star template (that we will duplicate all over the place)
	public GameObject m_starTemplate;

	// our flux template (that we will duplicate all over the place)
	public GameObject m_fluxTemplate;

	// true if we are currently traveling through a flux
	bool m_travelingThroughFlux;

	// the copies of the flux template, one for each flux, and whether they are shown - they are with the Ring Device aboard, which "will identify any nearby
	// continuum flux" (the Starport's analysis), or to a navigator of skill above 150 (the original's HYPER-OV .FLUX-ICON draws a flux, as (skill - 150) * 6 / 100 + 1
	// points, when NAV-SKILL is above 150 or ?HAS-RING-DEVICE is set). A flux that is not shown is still there: a ship that flies into it goes through
	readonly List<GameObject> m_fluxObjectList = new List<GameObject>();
	bool m_fluxesShown;

	// a flux is drawn as the original's dots (HYPER-OV .FLUX-ICON): GetFluxDotCount grey points, each at the flux's place plus RNDCPT(15) (up to 22 pixels up or
	// down, and across up to the ellipse's half width at that height, 15 pixels at the middle; the original's view is 4 pixels a unit across and 6 up, so up to 120
	// of the port's units across and 117 along), placed afresh at every step of the ship (PlayerShip.c_hyperspaceStepsPerSecond), so they twinkle. The particle
	// systems of the flux copies are given their points by hand, with numbers of their own so that the game's Random is not touched
	readonly List<ParticleSystem> m_fluxParticleSystemList = new List<ParticleSystem>();
	readonly ParticleSystem.Particle[] m_fluxDots = new ParticleSystem.Particle[ c_mostFluxDots ];
	readonly System.Random m_fluxDotRandom = new System.Random( 13 );
	int m_fluxDotCount;
	float m_fluxDotTimer;
	const int c_mostFluxDots = 16;
	const float c_fluxDotSize = 4.0f;
	static readonly Color32 c_fluxDotColour = new Color32( 190, 190, 190, 255 );

	// flux travel timer
	float m_timer;

	// flux travel duration (the warp effect's: the jump itself is instant)
	float m_fluxTravelDuration;

	// how long the warp effect of a flux jump plays, with the ship already at the far end (the original plays its FLUX-EFFECT and then puts the ship there)
	const float c_fluxEffectSeconds = 1.5f;

	// the eight directions the ship can come out of a flux in, one step of the original's along an axis or along both (HYPER-OV JMPSHP: HEADDXDY of a random
	// heading), and how far, 4 of the original's units (RCOLLIDE + 1), each 32 of the port's
	static readonly Vector3[] c_fluxExitDirections =
	{
		new Vector3( 1.0f, 0.0f, 0.0f ), new Vector3( 1.0f, 0.0f, 1.0f ), new Vector3( 0.0f, 0.0f, 1.0f ), new Vector3( -1.0f, 0.0f, 1.0f ),
		new Vector3( -1.0f, 0.0f, 0.0f ), new Vector3( -1.0f, 0.0f, -1.0f ), new Vector3( 0.0f, 0.0f, -1.0f ), new Vector3( 1.0f, 0.0f, -1.0f )
	};

	const float c_fluxExitDistance = 128.0f;

	// starting point
	Vector3 m_fluxTravelStartPosition;

	// ending point
	Vector3 m_fluxTravelEndPosition;

	// remember the maximum speed of the player before flux travel
	float m_oldMaximumSpeed;

	// unity start
	void Start()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// make copies of the star template
		foreach ( var star in gameData.m_starList )
		{
			// clone the star
			var clonedStar = Instantiate( m_starTemplate, star.GetWorldCoordinates(), Quaternion.identity, transform ) as GameObject;

			// activate the star
			clonedStar.SetActive( true );

			// get to the star quad mesh object
			var starQuad = clonedStar.transform.Find( "Star Quad" );

			// calculate the scale of the star quad
			float scale = star.GetBreachDistance() * 3.0f;

			// scale the star image based on the class of the star system
			starQuad.localScale = new Vector3( scale, scale, 1.0f );
		}

		// hide the star template
		m_starTemplate.SetActive( false );

		// make copies of the flux template
		foreach ( GD_Flux flux in gameData.m_fluxList )
		{
			// clone the flux
			var clonedFlux = Instantiate( m_fluxTemplate, flux.GetFrom(), Quaternion.identity, transform );

			// remember it (it is shown or hidden in the update, with the Ring Device)
			m_fluxObjectList.Add( clonedFlux );

			// its particle system draws the dots it is given, and nothing of its own
			var particleSystem = clonedFlux.GetComponentInChildren<ParticleSystem>( true );

			if ( particleSystem != null )
			{
				var emission = particleSystem.emission;

				emission.enabled = false;

				var main = particleSystem.main;

				main.maxParticles = Mathf.Max( main.maxParticles, c_mostFluxDots );
				main.prewarm = false;
			}

			m_fluxParticleSystemList.Add( particleSystem );

			clonedFlux.SetActive( false );
		}

		m_fluxesShown = false;

		// hide the flux template
		m_fluxTemplate.SetActive( false );

		// we are not travelling through a flux now
		m_travelingThroughFlux = false;
	}

	// how many dots a flux shows (the original's HYPER-OV .FLUX-ICON): none unless the navigator's skill is above 150 or the Ring Device is aboard; then
	// (skill - 150) * 6 / 100 + 1, 1 at 151 to 7 at 250, and 16 with the Ring Device whatever the skill (SETUP-MOV: ?HAS-RING-DEVICE is 16 with artifact 8 aboard)
	public static int GetFluxDotCount( int navigatorSkill, bool ringDevice )
	{
		if ( ( navigatorSkill <= 150 ) && !ringDevice )
		{
			return 0;
		}

		return Mathf.Max( ( navigatorSkill - 150 ) * 6 / 100 + 1, ringDevice ? c_mostFluxDots : 0 );
	}

	// gives every flux shown its dots, each at the flux's place plus the original's RNDCPT(15): a = RRND(0, 23) pixels up or down, then b = RRND(0, the
	// ellipse's half width at that height + 1) across, each negated half the time (HYPER-OV RNDCPT), 8 of the port's units a pixel across and 32 / 6 up
	void PlaceFluxDots()
	{
		for ( var i = 0; i < m_fluxParticleSystemList.Count; i++ )
		{
			var particleSystem = m_fluxParticleSystemList[ i ];

			if ( particleSystem == null )
			{
				continue;
			}

			var fluxPlace = m_fluxObjectList[ i ].transform.position;

			for ( var dot = 0; dot < m_fluxDotCount; dot++ )
			{
				var up = m_fluxDotRandom.Next( 0, 23 );
				var halfWidth = Mathf.Sqrt( Mathf.Max( 0.0f, 225.0f - Mathf.Pow( up * 2.0f / 3.0f, 2.0f ) ) );
				var across = m_fluxDotRandom.Next( 0, Mathf.FloorToInt( halfWidth ) + 1 );

				if ( m_fluxDotRandom.Next( 0, 2 ) == 0 )
				{
					up = -up;
				}

				if ( m_fluxDotRandom.Next( 0, 2 ) == 0 )
				{
					across = -across;
				}

				var place = fluxPlace + new Vector3( across * 8.0f, 0.0f, up * 32.0f / 6.0f );

				m_fluxDots[ dot ].position = particleSystem.transform.InverseTransformPoint( place );
				m_fluxDots[ dot ].velocity = Vector3.zero;
				m_fluxDots[ dot ].startSize = c_fluxDotSize;
				m_fluxDots[ dot ].startColor = c_fluxDotColour;
				m_fluxDots[ dot ].startLifetime = 1.0f;
				m_fluxDots[ dot ].remainingLifetime = 1.0f;
			}

			particleSystem.SetParticles( m_fluxDots, m_fluxDotCount );
		}
	}

	// unity update
	void Update()
	{
		// don't do anything if the game is paused
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// the fluxes can be seen with the Ring Device aboard or by a navigator of skill above 150, as dots, more of them the better the navigator
		m_fluxDotCount = GetFluxDotCount( CombatController.GetNavigatorSkill(), playerData.m_playerShip.HasArtifact( "Ring Device" ) );

		var showFluxes = ( m_fluxDotCount > 0 );

		if ( showFluxes != m_fluxesShown )
		{
			m_fluxesShown = showFluxes;

			foreach ( var fluxObject in m_fluxObjectList )
			{
				fluxObject.SetActive( showFluxes );
			}

			m_fluxDotTimer = 0.0f;
		}

		// the dots are placed afresh at every step of the ship
		if ( m_fluxesShown )
		{
			m_fluxDotTimer -= Time.deltaTime;

			if ( m_fluxDotTimer <= 0.0f )
			{
				m_fluxDotTimer = 1.0f / PlayerShip.c_hyperspaceStepsPerSecond;

				PlaceFluxDots();
			}
		}

		// are we travelling through a flux right now?
		if ( m_travelingThroughFlux )
		{
			// update the timer
			m_timer += Time.deltaTime;

			// calculate a smoothed curve for moving our position to the other end of the flux
			float t = Mathf.SmoothStep( 0.0f, 1.0f, m_timer / m_fluxTravelDuration );

			// update the position of the ship
			var newPosition = Vector3.Lerp( m_fluxTravelStartPosition, m_fluxTravelEndPosition, t );

			// update the ship position
			playerData.m_general.m_coordinates = SpaceflightController.m_instance.m_playerShip.transform.position = newPosition;

			// have we arrived?
			if ( m_timer >= m_fluxTravelDuration )
			{
				// end the space warp effect
				SpaceflightController.m_instance.m_playerCamera.m_spaceWarp.ExitWarp();

				// let the ship move again
				SpaceflightController.m_instance.m_playerShip.Unfreeze();

				// update the map coordinates
				SpaceflightController.m_instance.m_viewport.UpdateCoordinates();

				// update the last hyperspace location
				playerData.m_general.m_lastHyperspaceCoordinates = newPosition;

				// play the exit warp sound
				SoundController.m_instance.PlaySound( SoundController.Sound.ExitWarp );

				// turn the radar back on and reset it
				SpaceflightController.m_instance.m_radar.Show();

				// not travelling through the flux any more
				m_travelingThroughFlux = false;

				// restore the maximum speed of the player
				playerData.m_general.m_currentMaximumSpeed = m_oldMaximumSpeed;

				// turn back on the ship particle systems
				SpaceflightController.m_instance.m_playerShip.EnableParticles( true );
			}
		}
		else
		{
			// go through each star in the game
			foreach ( GD_Star star in gameData.m_starList )
			{
				// did we breach it?
				float distance = Vector3.Distance( playerData.m_general.m_coordinates, star.GetWorldCoordinates() );

				if ( distance < star.GetBreachDistance() )
				{
					Debug.Log( "Entering star system at " + star.m_xCoordinate + " x " + star.m_yCoordinate + " - switching to the star system location." );

					// change the system
					playerData.m_general.m_currentStarId = star.m_id;

					// its alien groups are placed afresh
					SpaceflightController.m_instance.PlaceStarSystemEncounters( star.m_id );

					// set the position of the player inside this system
					var starToShip = playerData.m_general.m_coordinates - star.GetWorldCoordinates();
					starToShip.Normalize();
					playerData.m_general.m_coordinates = starToShip * ( 8192.0f - 16.0f );
					playerData.m_general.m_lastStarSystemCoordinates = playerData.m_general.m_coordinates;

					// scale the speed of the player
					playerData.m_general.m_currentSpeed *= PlayerShip.c_starSystemSpeedFactor;
					playerData.m_general.m_currentMaximumSpeed *= PlayerShip.c_starSystemSpeedFactor;

					// switch to the star system location
					SpaceflightController.m_instance.SwitchLocation( PD_General.Location.StarSystem );
				}
			}

			// go through each flux in the game
			foreach ( GD_Flux flux in gameData.m_fluxList )
			{
				// did we breach it?
				var distance = Vector3.Distance( playerData.m_general.m_coordinates, flux.GetFrom() );

				if ( distance < flux.GetBreachDistance() )
				{
					Debug.Log( "Entering flux at " + flux.m_x1 + " x " + flux.m_y1 + ", distance = " + distance );

					// prevent the player from maneuvering
					SpaceflightController.m_instance.m_playerShip.Freeze();

					// reset the timer
					m_timer = 0.0f;

					// the jump is instant, as the original's (HYPER-OV >FLUX-NODE and JMPSHP): the ship comes out just past the far end, a step off it in one of
					// the eight directions at random (outside the flux there, which is 96 across), keeping its heading, and the warp effect plays with it there
					var exitDirection = c_fluxExitDirections[ Random.Range( 0, c_fluxExitDirections.Length ) ];
					var arrival = flux.GetTo() + exitDirection * c_fluxExitDistance;

					playerData.m_general.m_coordinates = arrival;
					playerData.m_general.m_lastHyperspaceCoordinates = arrival;
					SpaceflightController.m_instance.m_playerShip.transform.position = arrival;

					m_fluxTravelDuration = c_fluxEffectSeconds;
					m_fluxTravelStartPosition = arrival;
					m_fluxTravelEndPosition = arrival;

					// and the navigator loses the ship's bearings for a while
					SpaceflightController.m_instance.LoseBearings();

					// the flux is known from now on, and the starmap draws it (HYPER-OV >FLUX-NODE, SET-FLUX-STATE)
					playerData.m_general.LearnFlux( flux );

					// start the warp cinematics
					m_travelingThroughFlux = true;

					// disable the ship particle systems
					SpaceflightController.m_instance.m_playerShip.EnableParticles( false );

					// save the maximum speed of the player and cut it by a large amount so the skybox rotation seems wild
					m_oldMaximumSpeed = playerData.m_general.m_currentMaximumSpeed;

					playerData.m_general.m_currentMaximumSpeed *= 0.1f;

					// turn off the radar
					SpaceflightController.m_instance.m_radar.Hide();

					// start the warp effect
					SpaceflightController.m_instance.m_playerCamera.m_spaceWarp.EnterWarp();

					// play the enter warp sound
					SoundController.m_instance.PlaySound( SoundController.Sound.EnterWarp );

					// one flux at a time (the ship is now at the far end of this one)
					break;
				}
			}

			// update encounters
			SpaceflightController.m_instance.UpdateEncounters();
		}
	}

	// call this to hide the hyperspace stuff
	public void Hide()
	{
		if ( !gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Hiding the hyperspace location." );

		// turn off auto skybox blending
		StarflightSkybox.m_instance.m_autoblendSkyboxes = false;

		// hide the hyperspace objects
		gameObject.SetActive( false );
	}

	// call this to show the hyperspace stuff
	public void Show()
	{
		if ( gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Showing the hyperspace location." );

		// show the hyperspace objects
		gameObject.SetActive( true );

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// move the ship to where we are in hyperspace
		SpaceflightController.m_instance.m_playerShip.transform.position = playerData.m_general.m_coordinates = playerData.m_general.m_lastHyperspaceCoordinates;

		// calculate the new rotation of the player
		var newRotation = Quaternion.LookRotation( playerData.m_general.m_currentDirection, Vector3.up );

		// update the player rotation
		SpaceflightController.m_instance.m_playerShip.m_ship.rotation = newRotation;

		// unfreeze the player
		SpaceflightController.m_instance.m_playerShip.Unfreeze();

		// follow the player ship
		SpaceflightController.m_instance.m_playerCamera.SetCameraFollow( SpaceflightController.m_instance.m_playerShip.gameObject, Vector3.up * 1024.0f );

		// fade in the map
		SpaceflightController.m_instance.m_viewport.StartFade( 1.0f, 2.0f );

		// show the status display
		SpaceflightController.m_instance.m_displayController.ChangeDisplay( SpaceflightController.m_instance.m_displayController.m_statusDisplay );

		// show the radar
		SpaceflightController.m_instance.m_radar.Show();

		// play the star system music track
		MusicController.m_instance.ChangeToTrack( MusicController.Track.Hyperspace );

		// turn on auto skybox blending
		StarflightSkybox.m_instance.m_autoblendSkyboxes = true;
	}
}
