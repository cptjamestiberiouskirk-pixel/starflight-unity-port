
using UnityEngine;
using System;

public class PlayerShip : MonoBehaviour
{
	// the ship
	public Transform m_ship;

	// the contrail particle systems
	public ParticleSystem[] m_contrailParticleSystem;

	// the exhaust particle system
	public ParticleSystem m_exhaustParticleSystem;

	// the missile launcher
	public GameObject m_missileLauncher;

	// the laser cannon
	public GameObject m_laserCannon;

	// the cargo pods
	public GameObject[] m_cargoPods;

	// the maximum speed of the player
	public float m_maximumSpeed;

	// the minimum time to reach the maximum speed
	public float m_minimumTimeToReachMaximumSpeed;

	// the time to slow down (coast) to a stop (in an encounter: in hyperspace and in a star system the ship stops at once)
	public float m_timeToStop;

	// the speeds as the original's (HYPER-OV CSPEED, !MAXSPEED, ACCELERATE): its ship moves a step at a time, as many of its units a step as its top speed allows,
	// 1 in hyperspace and 12 in a star system, and only while a direction is held. In hyperspace the top speed is there from the first step; in a star system
	// the step grows by one each step from 1 to 12. A unit of the star system is 8192 / 400 of the port's (SYSRADIUS 400), one of hyperspace 32, so the star
	// system's top speed in a step is 12 * 20.48 / 32 = 7.68 times hyperspace's. The original takes a step each pass of its main loop, as fast as the computer
	// runs it, and the port takes the original's pace at GOG's speed (DOSBox at 1500 cycles): in hyperspace 1.45 coordinates a second in open space, 11.6 steps
	// of 32 of the port's units (371 a second); in a star system 400 units from the sun to the edge in 2.72 s at the top step, 12.2 steps of 12 units of 20.48
	// (2998 a second), the step growing from 1 to 12 in about a second. These are the top speeds of class 5 engines here, and the speed grows in hyperspace as well,
	// as the ship's acceleration allows (D8, below)
	public const float c_starSystemSpeedFactor = 12.0f * ( 8192.0f / 400.0f ) / 32.0f;
	const int c_starSystemSteps = 12;
	public const float c_hyperspaceStepsPerSecond = 11.6f;
	const float c_hyperspaceUnit = 32.0f;
	public const float c_starSystemStepsPerSecond = 12.2f;
	const float c_starSystemUnit = 8192.0f / 400.0f;

	// the speed by the engines (a ruling of 2026-10-17, ROADMAP.md D8: the original's ship flies at the same speed whatever its engines, which made a new ship too fast
	// to land with): in hyperspace and in a star system the top speed above is class 5 engines'; class 1 engines give a third of it, each class a sixth more (the
	// port's speeds before GOG's pace were about class 1's). The ship gets there from a twelfth of it (the original's first step of 12) in 5 s over its acceleration in
	// G (engines against mass, the original's SHIPGRPH-OV (.ACC)): 1 s for a new ship's 5 G, as the original's 12 steps at GOG's pace, 5 s at 1 G, a quarter of
	// a second at most 20 G and more
	const float c_slowestEnginesShare = 1.0f / 3.0f;
	const float c_rampSecondsAtOneG = 5.0f;
	const float c_shortestRampSeconds = 0.25f;
	const float c_longestRampSeconds = 5.0f;

	// the ship's steps in an encounter, as the original's (HYPER-OV #VES-SPEED, ACCELERATE, !S/A): each pass of its main loop (Encounter.c_passSeconds) with a
	// direction held the ship moves its step, in cells (CombatController.c_unitsPerCell); the step starts at 1 and grows by one every 96 / ESPEED + 1 passes, where
	// ESPEED is the larger of 2 and the ship's acceleration in G times the engines' health in percent over 100, up to the larger of 2 and 6 over the alien ships
	// in the encounter (6 with one ship, 3 with two, 2 from three); with no direction held the ship stops at once and the step goes back to 1
	const int c_encounterStepPasses = 96;
	const int c_encounterSteps = 6;

	// the edge of hyperspace holds the ship, as the original's (HYPER-OV FLY, WEE53): each axis is kept within the map, 0 to 1992 of the original's units across
	// (map x 0 to 249) and 0 to 1752 along (map y 0 to 219), with no message and the heading kept, so a ship flying into the edge at a slant slides along it. A
	// place outside the map (a ship coming out of a star system or a flux at the edge) is not moved, and the next step brings the ship back in
	public const float c_hyperspaceLowestCoordinate = -32768.0f;
	public const float c_hyperspaceHighestX = ( 249.0f - 128.0f ) * 256.0f;
	public const float c_hyperspaceHighestZ = ( 219.0f - 128.0f ) * 256.0f;
	float m_encounterPassTimer;
	int m_encounterPasses;

	// set to true when the engines are on (to accelerate) or off (to brake)
	bool m_enginesAreOn;

	// set to true to prevent the ship from moving (temporarily)
	bool m_freezeShip;

	// keep track of the last direction (for banking the ship)
	Vector3 m_lastDirection;

	// the way the ship's model faces: it turns toward the way the ship flies in about a fifth of a second, so that the ship does not snap round when it changes
	// course at once (as the original's does); only the model, the ship's path is the flight direction's
	Vector3 m_modelDirection;
	const float c_modelTurnRate = 12.0f;

	// keep track of the last banking angle (for interpolation)
	float m_currentBankingAngle;

	// keep track of whether we allow particles to emit or not
	bool m_particlesAreEnabled = true;

	// the glow of the engines, at the exhaust: faint while the ship stands still, brighter as it goes faster (made in Awake)
	MeshRenderer m_engineGlow;
	Material m_engineGlowMaterial;
	MaterialPropertyBlock m_engineGlowProperties;

	// the clock of the glow's slight pulse (it stops while the game is paused)
	float m_engineGlowClock;

	// how big the glow is (world units), and how strong at rest and at full speed
	const float c_engineGlowSize = 16.0f;
	const float c_engineGlowAtRest = 0.35f;
	const float c_engineGlowAtFullSpeed = 1.0f;

	static readonly int c_engineGlowStrengthId = Shader.PropertyToID( "_Strength" );

	// unity awake
	void Awake()
	{
		MakeEngineGlow();
	}

	// unity start
	void Start()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// show only as many cargo pods as we have purchased
		for ( int cargoPodId = 0; cargoPodId < m_cargoPods.Length; cargoPodId++ )
		{
			m_cargoPods[ cargoPodId ].SetActive( cargoPodId < playerData.m_playerShip.m_numCargoPods );
		}

		// hide or show the missile launchers depending on if we have them
		m_missileLauncher.SetActive( playerData.m_playerShip.m_missileLauncherClass > 0 );

		// hide or show the missile launchers depending on if we have them
		m_laserCannon.SetActive( playerData.m_playerShip.m_laserCannonClass > 0 );

		// jump start the last direction
		m_lastDirection = playerData.m_general.m_currentDirection;
		m_modelDirection = playerData.m_general.m_currentDirection;
	}

	// the ship's top speed in hyperspace or in a star system with engines of this class (D8): class 5's is the original's at GOG's pace, class 1's a third of it
	public static float GetTopSpeed( PD_General.Location location, int enginesClass )
	{
		var originalTopSpeed = ( location == PD_General.Location.StarSystem ) ? c_starSystemStepsPerSecond * c_starSystemSteps * c_starSystemUnit : c_hyperspaceStepsPerSecond * c_hyperspaceUnit;
		var share = c_slowestEnginesShare + ( 1.0f - c_slowestEnginesShare ) * ( Mathf.Clamp( enginesClass, 1, 5 ) - 1 ) / 4.0f;

		return originalTopSpeed * share;
	}

	// how long the ship takes to reach its top speed from a twelfth of it, with this acceleration in G (D8)
	public static float GetRampSeconds( int accelerationG )
	{
		return ( accelerationG > 0 ) ? Mathf.Clamp( c_rampSecondsAtOneG / accelerationG, c_shortestRampSeconds, c_longestRampSeconds ) : c_longestRampSeconds;
	}

	// unity update
	void Update()
	{
		// don't do anything if the game is paused
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get the amount of enduruium remaining in storage
		var elementReference = playerData.m_playerShip.m_elementStorage.Find( 5 );

		// do this part only if we havent frozen the player (in flux travel)
		if ( !m_freezeShip )
		{
			// are we out of fuel?
			if ( elementReference == null )
			{
				// yes - turn the engines off
				m_enginesAreOn = false;
			}

			// are the engines turned on?
			if ( m_enginesAreOn )
			{
				var location = playerData.m_general.m_location;
				var enginesFactor = playerData.m_playerShip.GetSystemFactor( PD_PlayerShip.ShipSystem.Engines );

				if ( location == PD_General.Location.Encounter )
				{
					var aliens = ( SpaceflightController.m_instance.m_encounter != null ) ? SpaceflightController.m_instance.m_encounter.GetAlienShipsPresent() : 0;
					var topStep = Mathf.Max( 2, c_encounterSteps / Mathf.Max( 1, aliens ) );
					var espeed = Mathf.Max( 2, Mathf.FloorToInt( playerData.m_playerShip.m_acceleration * enginesFactor ) );
					var passesPerStep = c_encounterStepPasses / espeed + 1;

					m_encounterPassTimer += Time.deltaTime;

					for ( var pass = 0; ( pass < 8 ) && ( m_encounterPassTimer >= Encounter.c_passSeconds ); pass++ )
					{
						m_encounterPassTimer -= Encounter.c_passSeconds;
						m_encounterPasses++;
					}

					var step = Mathf.Min( 1 + m_encounterPasses / passesPerStep, topStep );
					var cellsPerSecond = CombatController.c_unitsPerCell / Encounter.c_passSeconds;

					// destroyed engines move nothing (DAMAGE-OV WF2AC)
					playerData.m_general.m_currentMaximumSpeed = ( enginesFactor > 0.0f ) ? topStep * cellsPerSecond : 0.0f;
					playerData.m_general.m_currentSpeed = ( enginesFactor > 0.0f ) ? step * cellsPerSecond : 0.0f;
				}
				else if ( ( location != PD_General.Location.Hyperspace ) && ( location != PD_General.Location.StarSystem ) )
				{
					// elsewhere (an encounter above all) damaged engines are slower, and accelerate more slowly, in proportion to their health (the original's %ENGINE
					// counts in an encounter's acceleration, HYPER-OV !S/A)
					playerData.m_general.m_currentMaximumSpeed = m_maximumSpeed * enginesFactor;

					var acceleration = Time.deltaTime * playerData.m_playerShip.m_acceleration * enginesFactor / ( m_minimumTimeToReachMaximumSpeed * 25.0f );

					playerData.m_general.m_currentSpeed = Mathf.Lerp( playerData.m_general.m_currentSpeed, playerData.m_general.m_currentMaximumSpeed, acceleration );
				}
				else
				{
					// in hyperspace and in a star system the top speed is the engines' class's (D8, GetTopSpeed); the engines' health does not change it, but destroyed
					// engines move nothing (DAMAGE-OV WF2AC)
					var topSpeed = ( enginesFactor > 0.0f ) ? GetTopSpeed( location, playerData.m_playerShip.m_enginesClass ) : 0.0f;

					playerData.m_general.m_currentMaximumSpeed = topSpeed;

					// from a twelfth of the top speed (at least that at once) to all of it, in the time the ship's acceleration takes (GetRampSeconds)
					var rampedSpeed = Mathf.Max( playerData.m_general.m_currentSpeed, topSpeed / c_starSystemSteps ) + topSpeed * ( c_starSystemSteps - 1 ) / c_starSystemSteps * Time.deltaTime / GetRampSeconds( playerData.m_playerShip.m_acceleration );

					playerData.m_general.m_currentSpeed = Mathf.Min( rampedSpeed, topSpeed );
				}

				// are we in hyperspace?
				if ( playerData.m_general.m_location == PD_General.Location.Hyperspace )
				{
					// the original burns the engines' fuel at every step of the ship, an eighth of a coordinate, and a step on the diagonal moves the ship an
					// eighth of a coordinate along both axes (HYPER-OV FLY, -ENDURIUM): the fuel goes by the longer of the two distances the ship covers along
					// the axes, not by the straight distance
					var direction = playerData.m_general.m_currentDirection;
					var axisShare = ( direction.magnitude > 0.0f ) ? ( Mathf.Max( Mathf.Abs( direction.x ), Mathf.Abs( direction.z ) ) / direction.magnitude ) : 1.0f;
					var steps = playerData.m_general.m_currentSpeed * Time.deltaTime * axisShare / 256.0f * 8.0f;
					var stepHundredths = playerData.m_playerShip.GetFuelPerStepHundredths();

					// calculate the amount of fuel used up
					var fuelAmount = steps * stepHundredths / 100.0f;

					var enduriumBefore = GetEnduriumTenths();

					// use it up
					playerData.m_playerShip.UseUpFuel( fuelAmount );

					// the warnings as the fuel goes down (HYPER-OV (-ENDURIUM) and HYPERMSG-OV): at 150 and 75 steps' worth and at none
					var stepTenths = stepHundredths / 10.0f;

					WarnOfFuel( enduriumBefore, GetEnduriumTenths(), stepTenths );
				}
			}
			else if ( ( playerData.m_general.m_location == PD_General.Location.Hyperspace ) || ( playerData.m_general.m_location == PD_General.Location.StarSystem ) || ( playerData.m_general.m_location == PD_General.Location.Encounter ) )
			{
				// with no direction held the original's ship takes no step: it stops at once (in an encounter the step goes back to 1: HYPER-OV, KEYINC)
				playerData.m_general.m_currentSpeed = 0.0f;

				m_encounterPassTimer = 0.0f;
				m_encounterPasses = 0;
			}
			else
			{
				// slow the ship to a stop
				playerData.m_general.m_currentSpeed = Mathf.Lerp( playerData.m_general.m_currentSpeed, 0.0f, Time.deltaTime / m_timeToStop );
			}

			// check if the ship is moving
			if ( playerData.m_general.m_currentSpeed >= 0.1f )
			{
				// calculate the new position of the player
				var newPosition = transform.position + (Vector3) playerData.m_general.m_currentDirection * playerData.m_general.m_currentSpeed * Time.deltaTime;

				// make sure the ship stays on the zero plane
				newPosition.y = 0.0f;

				// in hyperspace the edge of the map holds the ship
				if ( playerData.m_general.m_location == PD_General.Location.Hyperspace )
				{
					newPosition.x = Mathf.Clamp( newPosition.x, c_hyperspaceLowestCoordinate, c_hyperspaceHighestX );
					newPosition.z = Mathf.Clamp( newPosition.z, c_hyperspaceLowestCoordinate, c_hyperspaceHighestZ );
				}

				// update the player position
				transform.position = newPosition;

				// update the player data (it will save out to disk eventually)
				playerData.m_general.m_coordinates = newPosition;

				// update the last coordinate based on the location
				switch ( playerData.m_general.m_location )
				{
					case PD_General.Location.Hyperspace:
						playerData.m_general.m_lastHyperspaceCoordinates = playerData.m_general.m_coordinates;
						break;

					case PD_General.Location.StarSystem:
						playerData.m_general.m_lastStarSystemCoordinates = playerData.m_general.m_coordinates;
						break;

					case PD_General.Location.Encounter:
						playerData.m_general.m_lastEncounterCoordinates = playerData.m_general.m_coordinates;
						break;
				}

				// update the map coordinates
				SpaceflightController.m_instance.m_viewport.UpdateCoordinates();
			}

			// the model turns toward the way the ship flies (a model facing nowhere, as at the first frame, takes it at once)
			m_modelDirection = ( m_modelDirection.sqrMagnitude > 0.0001f ) ? Vector3.Slerp( m_modelDirection, playerData.m_general.m_currentDirection, Mathf.Min( 1.0f, Time.deltaTime * c_modelTurnRate ) ) : playerData.m_general.m_currentDirection;

			// set the rotation of the ship
			m_ship.rotation = Quaternion.LookRotation( m_modelDirection, Vector3.up );

			// get the number of degrees we are turning the ship (compared to the last frame)
			var bankingAngle = Vector3.SignedAngle( m_modelDirection, m_lastDirection, Vector3.up );

			// scale the angle enough so we actually see the ship banking (but max it out at 60 degrees in either direction)
			bankingAngle = Mathf.Max( -60.0f, Mathf.Min( 60.0f, bankingAngle * 12.0f ) );

			// interpolate towards the new banking angle
			m_currentBankingAngle = Mathf.Lerp( m_currentBankingAngle, bankingAngle, Time.deltaTime * 4.0f );

			// bank the ship based on the calculated angle
			m_ship.rotation = Quaternion.AngleAxis( m_currentBankingAngle, m_modelDirection ) * m_ship.rotation * Quaternion.Euler( -90.0f, 0.0f, 0.0f );

			// update the last direction
			m_lastDirection = m_modelDirection;
		}

		// adjust the engine exhaust glow opacity and scale based on the current speed
		if ( m_particlesAreEnabled )
		{
			var emission = m_exhaustParticleSystem.emission;

			emission.enabled = m_enginesAreOn;

			// adjust opacity of start color of contrails based on speed of the ship
			var opacity = Mathf.Lerp( -1.0f, 2.0f, playerData.m_general.m_currentSpeed / playerData.m_general.m_currentMaximumSpeed );

			foreach ( var particleSystem in m_contrailParticleSystem )
			{
				var main = particleSystem.main;
				var startColor = main.startColor;
				var color = startColor.color;

				color.a = opacity;
				startColor.color = color;
				main.startColor = startColor;
			}
		}

		// get the current hyperspace coordinates (if in hyperspace get it from the player transform due to flux travel not updating m_hyperspaceCoordinates)
		var hyperspaceCoordinates = ( playerData.m_general.m_location == PD_General.Location.Hyperspace ) ? transform.position : playerData.m_general.m_lastHyperspaceCoordinates;

		// figure out how far we are from each nebula
		foreach ( var nebula in gameData.m_nebulaList )
		{
			nebula.Update( hyperspaceCoordinates );
		}

		// sort the results
		Array.Sort( gameData.m_nebulaList );

		// (a nebula keeps the shields down - see SpaceflightController.Update)
	}

	// call this to show the player ship
	// unity late update (after the ship has moved and turned)
	void LateUpdate()
	{
		UpdateEngineGlow();
	}

	// unity on destroy
	void OnDestroy()
	{
		// the glow's material is the ship's own: it would stay in memory until unity next clears out its unused assets
		if ( m_engineGlowMaterial != null )
		{
			Destroy( m_engineGlowMaterial );
		}
	}

	// makes the glow of the engines: a flat quad at the exhaust with a material of its own
	void MakeEngineGlow()
	{
		var shader = Shader.Find( "Starflight/Engine Glow" );

		if ( ( shader == null ) || ( m_exhaustParticleSystem == null ) )
		{
			return;
		}

		var glowObject = new GameObject( "Engine Glow" );

		glowObject.layer = m_exhaustParticleSystem.gameObject.layer;
		glowObject.transform.SetParent( m_exhaustParticleSystem.transform, false );
		glowObject.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>( "Quad.fbx" );

		m_engineGlow = glowObject.AddComponent<MeshRenderer>();

		m_engineGlowMaterial = new Material( shader );

		m_engineGlow.sharedMaterial = m_engineGlowMaterial;
		m_engineGlow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		m_engineGlow.receiveShadows = false;

		m_engineGlowProperties = new MaterialPropertyBlock();
	}

	// the glow lies flat, facing the camera above the ship, as big whatever the scale of the exhaust, and grows brighter with the ship's speed
	void UpdateEngineGlow()
	{
		if ( m_engineGlow == null )
		{
			return;
		}

		m_engineGlow.enabled = m_particlesAreEnabled;

		if ( !m_particlesAreEnabled )
		{
			return;
		}

		var glowTransform = m_engineGlow.transform;

		glowTransform.rotation = Quaternion.Euler( 90.0f, 0.0f, 0.0f );
		glowTransform.localScale = Vector3.one * ( c_engineGlowSize / Mathf.Max( 0.0001f, glowTransform.parent.lossyScale.x ) );

		if ( ( SpaceflightController.m_instance != null ) && !SpaceflightController.m_instance.m_gameIsPaused )
		{
			m_engineGlowClock += Time.deltaTime;
		}

		var general = DataController.m_instance.m_playerData.m_general;
		var speed = ( general.m_currentMaximumSpeed > 0.0f ) ? Mathf.Clamp01( general.m_currentSpeed / general.m_currentMaximumSpeed ) : 0.0f;
		var strength = Mathf.Lerp( c_engineGlowAtRest, c_engineGlowAtFullSpeed, speed ) * ( 1.0f + 0.08f * Mathf.Sin( m_engineGlowClock * 19.0f ) );

		m_engineGlowProperties.SetFloat( c_engineGlowStrengthId, strength );

		m_engineGlow.SetPropertyBlock( m_engineGlowProperties );
	}

	public void Show()
	{
		if ( gameObject.activeInHierarchy )
		{
			return;
		}

		gameObject.SetActive( true );

		Debug.Log( "Showing the player ship." );
	}

	// call this to hide the player ship
	public void Hide()
	{
		if ( !gameObject.activeInHierarchy )
		{
			return;
		}

		gameObject.SetActive( false );

		Debug.Log( "Hiding the player ship." );
	}

	// call this to turn on the engines (accelerate)
	// the Endurium in the hold, in tenths of a cubic meter
	static int GetEnduriumTenths()
	{
		var endurium = DataController.m_instance.m_playerData.m_playerShip.m_elementStorage.Find( 5 );

		return ( endurium == null ) ? 0 : endurium.m_volume;
	}

	// says so when the fuel goes down past 150 steps' worth ("Fuel supply low."), 75 ("Fuel supply critical.") or none ("Out of fuel."), in the original's colours
	// (HYPERMSG-OV WF149, WF163 and WF182: yellow, orange and pink, with a beep)
	static void WarnOfFuel( int before, int after, float stepTenths )
	{
		if ( after >= before )
		{
			return;
		}

		string warning = null;

		if ( ( before > 0 ) && ( after <= 0 ) )
		{
			warning = "<color=#FF80FF>Out of fuel.</color>";
		}
		else if ( ( before > 75 * stepTenths ) && ( after <= 75 * stepTenths ) )
		{
			warning = "<color=#FF8000>Fuel supply critical.</color>";
		}
		else if ( ( before > 150 * stepTenths ) && ( after <= 150 * stepTenths ) )
		{
			warning = "<color=yellow>Fuel supply low.</color>";
		}

		if ( warning != null )
		{
			SpaceflightController.m_instance.m_messages.AddText( warning );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
		}
	}

	public void TurnOnEngines()
	{
		m_enginesAreOn = true;
	}

	// call this to turn off the engines (brake)
	public void TurnOffEngines()
	{
		m_enginesAreOn = false;
	}

	// call this to enable or disable the ship particle systems
	public void EnableParticles( bool enabled )
	{
		m_particlesAreEnabled = enabled;

		var emission = m_exhaustParticleSystem.emission;

		emission.enabled = m_particlesAreEnabled;

		foreach ( var particleSystem in m_contrailParticleSystem )
		{
			emission = particleSystem.emission;

			emission.enabled = m_particlesAreEnabled;
		}
	}

	// call this to get the current position of the player ship
	public Vector3 GetPosition()
	{
		return transform.position;
	}

	// call this to temporarily stop the ship from moving (e.g. while travelling in flux)
	public void Freeze()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// make sure the player has no momentum to start with
		playerData.m_general.m_currentSpeed = 0.0f;

		// freeze the ship
		m_freezeShip = true;
	}

	// call this to allow the ship to move again
	public void Unfreeze()
	{
		// unfreeze the ship
		m_freezeShip = false;
	}
}
