
using UnityEngine;

public class InOrbit : MonoBehaviour
{
	// the nebula overlay
	public GameObject m_nebula;

	// the planet model
	public MeshRenderer m_planetModel;

	// the planet cloud
	public MeshRenderer m_clouds;

	// the planet atmosphere
	public GameObject m_planetAtmosphere;

	// current planet spin
	float m_spin;

	// true from the moment the ship goes into orbit until we have looked for an encounter that waits in orbit around this planet (we look once every time)
	bool m_lookForEncounter;

	// how many points of damage the field of the Crystal Planet does to a ship that orbits it without the Crystal Orb, and how often (in seconds)
	// (the sources say only that the ship "will take on damage rapidly" - these are the port's values)
	const int c_crystalFieldDamage = 5;
	const float c_crystalFieldInterval = 1.0f;

	// the time until the field of the Crystal Planet does damage again
	float m_crystalFieldTimer;

	// unity awake
	void Awake()
	{
	}

	// unity start
	void Start()
	{
	}

	// unity update
	void Update()
	{
		// don't do anything if the game is paused
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// has the ship just gone into orbit? then an encounter that waits in orbit around this planet begins now
		// (not while the launch from the surface is still bringing the ship up - its camera animation and the events of that animation have to finish first)
		if ( m_lookForEncounter && !SpaceflightController.m_instance.m_playerCamera.IsLaunchingOrLanding() )
		{
			// we look only once every time the ship goes into orbit
			m_lookForEncounter = false;

			if ( SpaceflightController.m_instance.BeginInOrbitEncounter() )
			{
				// the encounter location has taken over
				return;
			}
		}

		// the field of the Crystal Planet damages a ship that orbits it without the Crystal Orb
		UpdateCrystalPlanetField();

		// slowly spin the planet
		m_spin += Time.deltaTime * SpaceflightController.m_instance.m_planetRotationSpeed;

		// wrap the spin around to avoid FP issues
		if ( m_spin >= 360.0f )
		{
			m_spin -= 360.0f;
		}

		// calculate the new rotation quaternion
		var newRotation = Quaternion.Euler( 0.0f, 0.0f, m_spin );

		// apply it to the planet
		m_planetModel.transform.localRotation = newRotation;
	}

	// the Crystal Planet generates a powerful magnetic force that only the Crystal Orb negates: a ship that orbits it without the orb takes on damage rapidly (STRINFO 5.1, the kernel's crystal planet heating routine)
	void UpdateCrystalPlanetField()
	{
		// no damage while the ship comes up from the surface or goes down to it
		if ( SpaceflightController.m_instance.m_playerCamera.IsLaunchingOrLanding() )
		{
			return;
		}

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get the planet we are orbiting (guard against an id that is not in the list)
		var planetId = playerData.m_general.m_currentPlanetId;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) )
		{
			return;
		}

		var planet = gameData.m_planetList[ planetId ];

		// is this the Crystal Planet, and is the Crystal Orb not in the ship's hold?
		var orbId = gameData.FindArtifactId( "Crystal Orb" );
		var artifactStorage = playerData.m_playerShip.m_artifactStorage;
		var shipHasTheOrb = ( artifactStorage != null ) && ( artifactStorage.Find( orbId ) != null );

		if ( !planet.IsCrystalPlanet() || shipHasTheOrb )
		{
			// no - the field does nothing, and it begins again with a full interval
			m_crystalFieldTimer = c_crystalFieldInterval;

			return;
		}

		// count down to the next damage
		m_crystalFieldTimer -= Time.deltaTime;

		if ( m_crystalFieldTimer > 0.0f )
		{
			return;
		}

		m_crystalFieldTimer += c_crystalFieldInterval;

		// the field damages the ship (shields first, then armor, as every damage does)
		SpaceflightController.m_instance.m_combatController.ApplyDamageToPlayer( c_crystalFieldDamage, Vector3.down );
	}

	// call this to hide the in orbit objects
	public void Hide()
	{
		if ( !gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Hiding the in orbit location." );

		// hide the in orbit location
		gameObject.SetActive( false );
	}

	// call this to show the in orbit objects
	public void Show()
	{
		if ( gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Showing the in orbit location." );

		// the ship has just gone into orbit - look for an encounter that waits here (in the next update, not in the middle of this switch of location)
		m_lookForEncounter = true;

		// the field of the Crystal Planet does its first damage a full interval after the ship goes into orbit
		m_crystalFieldTimer = c_crystalFieldInterval;

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the star data
		var star = gameData.m_starList[ playerData.m_general.m_currentStarId ];

		// show the in orbit objects
		gameObject.SetActive( true );

		// get the planet controller
		var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

		// set the scale of the planet model and atmosphere
		var scale = planetController.m_planet.GetScale();
		m_planetModel.transform.localScale = scale * 3.0f;
		m_planetAtmosphere.transform.localScale = m_planetModel.transform.localScale * 1.055f;

		// position the planet and atmosphere
		var position = Vector3.up * -m_planetModel.transform.localScale.y;
		m_planetModel.transform.localPosition = position;
		m_planetAtmosphere.transform.localPosition = position * 0.85f;

		// move the player object
		SpaceflightController.m_instance.m_playerShip.transform.position = playerData.m_general.m_coordinates = Vector3.zero;

		// freeze the player
		SpaceflightController.m_instance.m_playerShip.Freeze();

		// are we in the middle of the launching animation?
		if ( !SpaceflightController.m_instance.m_playerCamera.IsCurrentlyPlaying( "Launching (Planetside)" ) )
		{
			// no - play the in orbit animation
			SpaceflightController.m_instance.m_playerCamera.StartAnimation( "In Space" );

			// reset the buttons
			SpaceflightController.m_instance.m_buttonController.SetBridgeButtons();
		}

		// fade in the map
		SpaceflightController.m_instance.m_viewport.StartFade( 1.0f, 2.0f );

		// show / hide the nebula depending on if we are in one
		m_nebula.SetActive( star.m_insideNebula );

		// play the docking bay music track
		MusicController.m_instance.ChangeToTrack( MusicController.Track.InOrbit );

		// let the player know we've established orbit
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Orbit established.</color>" );

		// say so if this is a planet whose maps could not be generated (that is why it is a plain grey ball, and why we can't land on it)
		if ( planetController.CouldNotBeMapped() )
		{
			SpaceflightController.m_instance.m_messages.AddText( Planet.c_couldNotBeMappedMessage );
		}

		// set up the clouds and atmosphere
		planetController.SetupClouds( m_clouds, m_planetAtmosphere, true, false );

		// make sure skybox blend is off
		StarflightSkybox.m_instance.m_currentBlendFactor = 0.0f;

		// turn skybox autorotate off
		StarflightSkybox.m_instance.m_autorotateSkybox = false;

		// make sure skybox rotation is reset
		StarflightSkybox.m_instance.m_currentRotation = Quaternion.identity;

		// apply the material to the planet model
		MaterialUpdated();
	}

	public void MaterialUpdated()
	{
		if ( gameObject.activeInHierarchy )
		{
			// get to the player data
			var playerData = DataController.m_instance.m_playerData;

			// get the planet controller
			var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

			// apply the material to the planet model
			m_planetModel.material = planetController.GetMaterial();
		}
	}
}
