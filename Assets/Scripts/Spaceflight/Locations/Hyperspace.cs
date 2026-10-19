
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

			clonedFlux.SetActive( false );
		}

		m_fluxesShown = false;

		// hide the flux template
		m_fluxTemplate.SetActive( false );

		// we are not travelling through a flux now
		m_travelingThroughFlux = false;
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

		// the fluxes can be seen with the Ring Device aboard or by a navigator of skill above 150
		var showFluxes = playerData.m_playerShip.HasArtifact( "Ring Device" ) || ( CombatController.GetNavigatorSkill() > 150 );

		if ( showFluxes != m_fluxesShown )
		{
			m_fluxesShown = showFluxes;

			foreach ( var fluxObject in m_fluxObjectList )
			{
				fluxObject.SetActive( showFluxes );
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
