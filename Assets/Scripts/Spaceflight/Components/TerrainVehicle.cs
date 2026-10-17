
using UnityEngine;

public class TerrainVehicle : MonoBehaviour
{
	// the terrain vehicle model (we apply body roll rotation to it instead of this object)
	public GameObject m_terrainVehicleModel;

	// the terrain vehicle wheels (fr, fl, mr, ml, rr, rl)
	public GameObject[] m_wheels;

	// the steering joints (fr, fl, mr, ml, rr, rl)
	public GameObject[] m_steeringJoints;

	// how fast to spin the wheels
	public float m_wheelTurnSpeed;

	// how fast the wheels steer
	public float m_wheelSteerSpeed;

	// the resting suspension height above the ground
	public float m_neutralSuspensionHeight;

	// the maximum speed of the player
	public float m_maximumSpeed;

	// speed multiplier (increase this to make the vehicle faster)
	public float m_speedMultiplier = 3.0f;

	// the time to reach the maximum speed
	public float m_timeToReachMaximumSpeed;

	// the time to slow down (coast) to a stop
	public float m_timeToStop;

	// how much to sink the TV in water
	public float m_floatDepth;

	// how fast to bob the TV in water
	public float m_waterBobSpeed;

	// how much to bob the TV in water
	public Vector2 m_waterBobAmount;

	// the terrain grid elevation scale
	public float m_elevationScale = 100.0f;

	// the planet generator
	PlanetGenerator m_planetGenerator;

	// the fuel the engines burn for each step of the original's, a map cell (a kilometer of the display), by the contour of the ground the vehicle is on (MOVE-OV
	// SET-SPEED sets E/M, and WF29C takes E/M times 100 over %EFF from the tank at every step): 170 in a liquid (contour below 16), else one and a half times the
	// contour (16 to 127). Neither the slope nor the weather changes the speed (the released game reads only the cell the vehicle is in; the draft notes'
	// "uphill is very slow" is not in it). The port's heights are the original maps' raised to a sea level of their own, so the contour is taken to rise from 16
	// at the shore to 127 at the port's highest ground (INFERRED)
	const float c_liquidEnergyPerStep = 170.0f;
	const float c_shoreContour = 16.0f;
	const float c_highestContour = 127.0f;

	// the tank, in the original's units: full is 32767 (TV-OV WED9C), and the display's 0% is 2000 of them, the reserve below it
	public const float c_tankUnits = 30767.0f;

	// the E/M of the ground the vehicle is on
	float m_energyPerStep = c_liquidEnergyPerStep;

	// whether or not the engine is on or off
	bool m_enginesAreOn;

	// keep track of the last direction (for steering the wheels)
	Vector3 m_lastDirection;

	// the amount of wheel slip (reduces effiency)
	float m_wheelEfficiency;

	// how deep in water are we
	float m_waterEffectAmount;

	// the most one contact pushes the terrain vehicle in a physics step, so that a deep overlap is left over a few frames rather than in one jump (the port's)
	const float c_largestPushBack = 0.5f;

	// for gizmo drawing
	Vector3[] m_debugVectors;

	TerrainVehicle()
	{
		m_debugVectors = new Vector3[ 12 ];
	}

	// unity update
	void Update()
	{
		// don't do anything if the game is paused
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// the weather of the surface, rolled every game hour (a storm halves the vehicle's efficiency)
		PlanetWeather.Update();

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// are the engines turned on?
		if ( m_enginesAreOn )
		{
			// calculate the acceleration
			var acceleration = Time.deltaTime * playerData.m_playerShip.m_acceleration / ( m_timeToReachMaximumSpeed * 25.0f );

			// calculate effective maximum speed with multiplier
			var effectiveMaxSpeed = m_maximumSpeed * m_speedMultiplier;

			// increase the current speed
			playerData.m_general.m_currentSpeed = Mathf.Lerp( playerData.m_general.m_currentSpeed, effectiveMaxSpeed, acceleration );

		}
		else
		{
			// slow the ship to a stop
			playerData.m_general.m_currentSpeed = Mathf.Lerp( playerData.m_general.m_currentSpeed, 0.0f, Time.deltaTime / m_timeToStop );
		}

		// update the diesel engine sound
		var effectiveMaxSpeedForSound = m_maximumSpeed * m_speedMultiplier;
		var enginePitch = Mathf.Lerp( 1.0f, 1.5f, playerData.m_general.m_currentSpeed / effectiveMaxSpeedForSound );

		SoundController.m_instance.SetFrequency( SoundController.Sound.DieselEngine, enginePitch );

		// check if the ship is moving
		if ( playerData.m_general.m_currentSpeed >= 0.1f )
		{
			// calculate the new position of the player (from where it is saved, which holds every push-back)
			var oldPosition = playerData.m_general.m_coordinates;
			var newPosition = oldPosition + (Vector3) playerData.m_general.m_currentDirection * m_wheelEfficiency * playerData.m_general.m_currentSpeed * Time.deltaTime;

			// the fuel for the steps driven: a step on the diagonal is one cell along both axes for the fuel of one, so the steps are the longer of the two
			// distances, and the weather's efficiency (%EFF) makes each dearer
			if ( m_enginesAreOn )
			{
				var steps = Tools.SurfaceStepsInKm( oldPosition, newPosition );

				playerData.m_terrainVehicle.UseUpFuel( steps * m_energyPerStep / Mathf.Max( 0.01f, PlanetWeather.GetEfficiency() ) / c_tankUnits );
			}

			// wrap player around the planet
			var planetWidth = 2048.0f * 4.0f;
			var maxPlanetCoordinateX = planetWidth * 0.5f;
			var minPlanetCoordinateX = planetWidth * -0.5f;

			if ( newPosition.x >= maxPlanetCoordinateX )
			{
				newPosition.x -= planetWidth;
			}
			else if ( newPosition.x < minPlanetCoordinateX )
			{
				newPosition.x += planetWidth;
			}

			// update the player position
			transform.localPosition = newPosition;

			// update the player data (it will save out to disk eventually)
			playerData.m_general.m_coordinates = newPosition;

			// update the last known disembarked coordinates
			playerData.m_general.m_lastDisembarkedCoordinates = playerData.m_general.m_coordinates;

			// turn the wheels
			foreach ( var wheel in m_wheels )
			{
				wheel.transform.localRotation *= Quaternion.Euler( 0.0f, 0.0f, playerData.m_general.m_currentSpeed * m_wheelTurnSpeed * Time.deltaTime );
			}

			// update the map coordinates
			SpaceflightController.m_instance.m_viewport.UpdateCoordinates();
		}

		// set the rotation of the terrain vehicle
		m_terrainVehicleModel.transform.localRotation = Quaternion.LookRotation( playerData.m_general.m_currentDirection, Vector3.up ) * Quaternion.Euler( -90.0f, 0.0f, 0.0f );

		// add a random bob if in water
		var bobX = ( Mathf.PerlinNoise( Time.time * m_waterBobSpeed, 10.0f ) * 2.0f - 1.0f ) * m_waterBobAmount.x * m_waterEffectAmount;
		var bobY = ( Mathf.PerlinNoise( Time.time * m_waterBobSpeed, 90.0f ) * 2.0f - 1.0f ) * m_waterBobAmount.y * m_waterEffectAmount;

		m_terrainVehicleModel.transform.rotation *= Quaternion.Euler( bobX, bobY, 0.0f );

		// get the number of degrees we are turning the terrain vehicle (compared to the last frame)
		var steeringAngle = Vector3.SignedAngle( playerData.m_general.m_currentDirection, m_lastDirection, Vector3.up );

		// scale the angle enough so we actually see the wheels turning (but max it out at 45 degrees in either direction)
		steeringAngle = Mathf.Max( -45.0f, Mathf.Min( 45.0f, steeringAngle * 12.0f ) );

		// steer the wheels
		m_steeringJoints[ 0 ].transform.localRotation = Quaternion.Slerp( m_steeringJoints[ 0 ].transform.localRotation, Quaternion.Euler( 0.0f, -steeringAngle, 0.0f ), Time.deltaTime * m_wheelSteerSpeed );
		m_steeringJoints[ 1 ].transform.localRotation = Quaternion.Slerp( m_steeringJoints[ 1 ].transform.localRotation, Quaternion.Euler( 0.0f, -steeringAngle, 0.0f ), Time.deltaTime * m_wheelSteerSpeed );
		m_steeringJoints[ 4 ].transform.localRotation = Quaternion.Slerp( m_steeringJoints[ 4 ].transform.localRotation, Quaternion.Euler( 0.0f, steeringAngle, 0.0f ), Time.deltaTime * m_wheelSteerSpeed );
		m_steeringJoints[ 5 ].transform.localRotation = Quaternion.Slerp( m_steeringJoints[ 5 ].transform.localRotation, Quaternion.Euler( 0.0f, steeringAngle, 0.0f ), Time.deltaTime * m_wheelSteerSpeed );

		// update the last direction
		m_lastDirection = playerData.m_general.m_currentDirection;

		// get the current position of the terrain vehicle
		var tvPosition = ApplyElevation( playerData.m_general.m_coordinates, true );

		// update the tv game object position
		transform.localPosition = tvPosition;

		// reset the steering joints
		foreach ( var steeringJoint in m_steeringJoints )
		{
			steeringJoint.transform.localPosition = Vector3.zero;
		}

		// lava destroys the terrain vehicle that stays in it: the liquid of a molten planet (the 1984 notes, planet.txt: "If the TV moves into a lava terrain a
		// warning message is given ('The heat from the lava is too much - we're burning up'). If the TV still does not move off it is destroyed.")
		if ( IsInLava( playerData.m_general.m_coordinates ) )
		{
			if ( m_timeInLava <= 0.0f )
			{
				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_messages.AddText( "<color=red>The heat from the lava is too much - we're burning up!</color>" );
			}

			m_timeInLava += Time.deltaTime;

			if ( m_timeInLava >= c_lavaGraceTime )
			{
				m_timeInLava = 0.0f;

				LoseToLava();

				return;
			}
		}
		else
		{
			m_timeInLava = 0.0f;
		}

		// the fuel a step takes on the ground the vehicle is on
		m_energyPerStep = GetEnergyPerStepAtHeight( tvPosition.y );

		// calculate the normal of the terrain between the center and the front wheels
		var wheel1 = ApplyElevation( m_wheels[ 1 ].transform.position, false );
		var wheel2 = ApplyElevation( m_wheels[ 0 ].transform.position, false );

		var side1 = wheel1 - tvPosition;
		var side2 = wheel2 - tvPosition;

		var normal1 = Vector3.Cross( side1, side2 );

		m_debugVectors[ 0 ] = tvPosition;
		m_debugVectors[ 1 ] = wheel1;
		m_debugVectors[ 2 ] = tvPosition;
		m_debugVectors[ 3 ] = wheel2;
		m_debugVectors[ 4 ] = ( wheel1 + wheel2 ) * 0.5f;
		m_debugVectors[ 5 ] = m_debugVectors[ 4 ] + normal1;

		wheel1 = ApplyElevation( m_wheels[ 4 ].transform.position, false );
		wheel2 = ApplyElevation( m_wheels[ 5 ].transform.position, false );

		side1 = wheel1 - tvPosition;
		side2 = wheel2 - tvPosition;

		var normal2 = Vector3.Cross( side1, side2 );

		m_debugVectors[ 6 ] = tvPosition;
		m_debugVectors[ 7 ] = wheel1;
		m_debugVectors[ 8 ] = tvPosition;
		m_debugVectors[ 9 ] = wheel2;
		m_debugVectors[ 10 ] = ( wheel1 + wheel2 ) * 0.5f;
		m_debugVectors[ 11 ] = m_debugVectors[ 10 ] + normal2;

		var averagedNormal = Vector3.Normalize( normal1 + normal2 );

		// update the attitude of the body based on the average normal
		m_terrainVehicleModel.transform.rotation = Quaternion.FromToRotation( Vector3.up, averagedNormal ) * m_terrainVehicleModel.transform.rotation;

		// move the tv wheels up and down depending on the height of the terrain under the wheels
		foreach ( var steeringJoint in m_steeringJoints )
		{
			var wheelPosition = ApplyElevation( steeringJoint.transform.position, false );

			var offset = wheelPosition.y - steeringJoint.transform.position.y;

			wheelPosition.x = 0.0f;
			wheelPosition.y = offset + m_neutralSuspensionHeight;
			wheelPosition.z = 0.0f;

			steeringJoint.transform.localPosition = wheelPosition;
		}
	}

	public void Initialize()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get the planet controller
		var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

		// save the planet generator
		m_planetGenerator = planetController.GetPlanetGenerator();

		// jump start the last direction
		m_lastDirection = playerData.m_general.m_currentDirection;
	}

	// the efficiency the display shows, as a fraction: %EFF over 4 times E/M, at most 99% (MOVE-OV's display: 99% at the shore, 14% in a liquid, and the
	// weather's share on top: a storm halves it)
	public float GetFuelEfficiency()
	{
		var percent = Mathf.Floor( PlanetWeather.GetEfficiency() * 100.0f * 100.0f / ( 4.0f * Mathf.Max( 1.0f, m_energyPerStep ) ) );

		return Mathf.Min( 99.0f, percent ) / 100.0f;
	}

	// E/M at a height of the ground (where the vehicle stands: the float height on a liquid): 170 on a liquid, else one and a half times the contour
	float GetEnergyPerStepAtHeight( float height )
	{
		var maximumElevation = 1.25f * m_elevationScale;
		var waterElevation = m_planetGenerator.m_waterElevation * m_elevationScale;

		if ( height < waterElevation )
		{
			return c_liquidEnergyPerStep;
		}

		var contour = Mathf.Floor( Mathf.Lerp( c_shoreContour, c_highestContour, Mathf.Clamp01( ( height - waterElevation ) / ( maximumElevation - waterElevation ) ) ) );

		return Mathf.Floor( contour * 3.0f / 2.0f );
	}

	// E/M at a place on the planet's surface (the crew's walk back to the ship goes cell by cell over it)
	public float GetEnergyPerStepAt( Vector3 worldCoordinates )
	{
		if ( m_planetGenerator == null )
		{
			return c_liquidEnergyPerStep;
		}

		return GetEnergyPerStepAtHeight( ApplyElevation( worldCoordinates, false ).y );
	}

	// the fuel a step takes on the ground the vehicle is on, in the original's units (E/M: for the probe)
	public float GetEnergyPerStep()
	{
		return m_energyPerStep;
	}

	// an object on the terrain overlaps the terrain vehicle by this much (a negative separation): push the vehicle out of it and slow it down
	public void AddPushBack( Vector3 pushBackNormal, float pushBackAmount )
	{
		// a contact inside the contact offset (a positive separation) only touches the vehicle: it neither pushes it nor speeds it up
		if ( pushBackAmount >= 0.0f )
		{
			return;
		}

		// nothing moves the vehicle while the game is paused (the physics steps go on)
		if ( ( SpaceflightController.m_instance == null ) || SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// along the ground only (the height comes from the terrain), and no more than a part of the overlap in one step
		var pushBack = pushBackNormal * pushBackAmount;

		pushBack.y = 0.0f;
		pushBack = Vector3.ClampMagnitude( pushBack, c_largestPushBack );

		// into the saved place, so that it lasts and the vehicle is drawn where it is, and into the transform, so that the next physics step finds the vehicle
		// moved and does not push it again for the same overlap (several steps can run before the next Update)
		playerData.m_general.m_coordinates += pushBack;
		playerData.m_general.m_lastDisembarkedCoordinates = playerData.m_general.m_coordinates;

		transform.localPosition += pushBack;

		// reduce the current speed of the terrain vehicle
		playerData.m_general.m_currentSpeed = Mathf.Max( 0.0f, playerData.m_general.m_currentSpeed + pushBackAmount * 10.0f );
	}

	Vector3 ApplyElevation( Vector3 worldCoordinates, bool updateWheelEfficiency )
	{
		var x = worldCoordinates.x * 0.25f + m_planetGenerator.m_textureMapWidth * 0.5f - 0.5f;
		var y = worldCoordinates.z * 0.25f + m_planetGenerator.m_textureMapHeight * 0.5f - 0.5f;

		var groundElevation = m_planetGenerator.GetBicubicSmoothedElevation( x, y ) * m_elevationScale;
		var waterElevation = m_planetGenerator.m_waterElevation * m_elevationScale;
		var floatElevation = waterElevation - m_floatDepth;

		worldCoordinates.y = Mathf.Max( floatElevation, groundElevation );

		if ( updateWheelEfficiency )
		{
			m_wheelEfficiency = Mathf.Clamp( ( worldCoordinates.y - floatElevation ) / m_floatDepth, 0.0f, 1.0f );
			m_waterEffectAmount = Mathf.Pow( 1.0f - m_wheelEfficiency, 2.0f );
			m_wheelEfficiency = m_wheelEfficiency * 0.5f + 0.5f;
		}

		return worldCoordinates;
	}

	// how long the terrain vehicle has been in lava, and how long it can stay before it is destroyed (the notes give no time: the port's)
	float m_timeInLava;
	const float c_lavaGraceTime = 3.0f;

	// a terrain vehicle that comes out of the ship has not been in lava yet
	void OnEnable()
	{
		m_timeInLava = 0.0f;
	}

	// true if this place of a molten planet is under its liquid, which is lava (the Return order looks ahead with it, TVReturnButton)
	public bool IsInLava( Vector3 worldCoordinates )
	{
		if ( ( m_planetGenerator == null ) || ( m_planetGenerator.GetPlanet() == null ) || !m_planetGenerator.GetPlanet().IsMolten() )
		{
			return false;
		}

		var x = worldCoordinates.x * 0.25f + m_planetGenerator.m_textureMapWidth * 0.5f - 0.5f;
		var y = worldCoordinates.z * 0.25f + m_planetGenerator.m_textureMapHeight * 0.5f - 0.5f;

		return m_planetGenerator.GetBicubicSmoothedElevation( x, y ) < m_planetGenerator.m_waterElevation;
	}

	// the terrain vehicle is lost in the lava with everything in its hold, and the crew gets back to the ship, which has no terrain vehicle until the Starport
	// installs a new one and fines the crew (STRINFO 1.2) - how the crew gets back is the port's choice
	void LoseToLava()
	{
		var playerData = DataController.m_instance.m_playerData;

		TurnOffEngines();

		SoundController.m_instance.StopSound( SoundController.Sound.DieselEngine );
		SoundController.m_instance.PlaySound( SoundController.Sound.Explosion );

		playerData.m_terrainVehicle.Reset();
		playerData.m_terrainVehicle.m_isLost = true;
		playerData.m_general.m_currentSpeed = 0.0f;

		SpaceflightController.m_instance.m_buttonController.ClearCurrentButton();
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );

		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=red>The terrain vehicle has been destroyed by lava, with everything it carried!</color>\n<color=white>The crew made it back to the ship.</color>" );
	}

	// call this to turn on the engines (accelerate)
	public void TurnOnEngines()
	{
		m_enginesAreOn = true;
	}

	// call this to turn off the engines (brake)
	public void TurnOffEngines()
	{
		m_enginesAreOn = false;
	}

#if UNITY_EDITOR

	// draw gizmos to help debug the game
	void OnDrawGizmos()
	{
		Gizmos.color = Color.green;

		for ( var i = 0; i < m_debugVectors.Length; i += 2 )
		{
			Gizmos.DrawLine( m_debugVectors[ i ], m_debugVectors[ i + 1 ] );
		}
	}

#endif
}
