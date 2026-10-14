
using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
	// the camera
	public Camera m_camera;

	// the space warp effect
	public SpaceWarp m_spaceWarp;

	// fire particle effects
	public ParticleSystem[] m_fireParticleSystems;

	// the object to follow
	public GameObject m_followGameObject;

	// the follow offset
	public Vector3 m_followOffset;

	// the minimum elevation above the terrain
	public float m_minimumElevation;

	// how quickly to correct the camera hitting the terrain
	public float m_elevationCorrectionTime;

	// the current elevation correction
	float m_currentElevationDelta;

	// the camera animation controller
	Animator m_animator;

	// unity awake
	void Awake()
	{
		// get the animator component
		m_animator = GetComponent<Animator>();

		// ensure the camera has an audio listener
		EnsureAudioListener();
	}

	// make sure there's an audio listener on the camera
	void EnsureAudioListener()
	{
		// check if there's already an audio listener in the scene
		var existingListener = FindFirstObjectByType<AudioListener>();

		if ( existingListener == null )
		{
			// no audio listener found - add one to the camera
			if ( m_camera != null )
			{
				m_camera.gameObject.AddComponent<AudioListener>();
			}
			else
			{
				// fallback: add to this game object
				gameObject.AddComponent<AudioListener>();
			}
		}
	}

	// unity late update (because we want to do this after the follow game object has moved in update)
	void LateUpdate()
	{
		// are we following an object?
		if ( m_followGameObject != null )
		{
			// yes - get to the player data
			var playerData = DataController.m_instance.m_playerData;

			// update the player camera position
			transform.localPosition = m_followGameObject.transform.localPosition + m_followOffset;

			// are we in the disembark location?
			if ( playerData.m_general.m_location == PD_General.Location.Disembarked )
			{
				// yes - get the position of the terrain below the camera
				var terrainPosition = SpaceflightController.m_instance.m_disembarked.ApplyElevation( transform.localPosition, false );

				// how far below the minimum elevation are we?
				var delta = Mathf.Max( 0.0f, m_minimumElevation - ( transform.localPosition.y - terrainPosition.y ) );

				// smoothly interpolate the elevation correction
				m_currentElevationDelta = Mathf.Lerp( m_currentElevationDelta, delta, Time.deltaTime / m_elevationCorrectionTime );

				// update the player camera position
				transform.localPosition += Vector3.up * m_currentElevationDelta;
			}

			// update the player camera rotation
			transform.localRotation = Quaternion.LookRotation( m_followGameObject.transform.localPosition - transform.localPosition, Vector3.forward );
		}
	}

	public void Hide()
	{
		gameObject.SetActive( false );
	}

	public void Show()
	{
		gameObject.SetActive( true );
	}

	// get the game object we are currently following
	public GameObject GetCameraFollowGameObject()
	{
		return m_followGameObject;
	}

	// update the follow game object with a follow offset and rotation
	public void SetCameraFollow( GameObject followGameObject, Vector3 followOffset )
	{
		m_followGameObject = followGameObject;
		m_followOffset = followOffset;

		// do we want to follow a game object?
		if ( m_followGameObject == null )
		{
			// no - let the camera animation control everything
			transform.localPosition = Vector3.zero;
			transform.localRotation = Quaternion.identity;
		}
		else
		{
			// yes - we control it all
			transform.localPosition = m_followGameObject.transform.position + m_followOffset;
			transform.localRotation = Quaternion.LookRotation( -m_followOffset, Vector3.forward );

			// make the camera animation do nothing
			m_animator.Play( "Idle" );
		}
	}

	// check if we are playing a certain animation
	public bool IsCurrentlyPlaying( string animationName )
	{
		var animatorStateInfo = m_animator.GetCurrentAnimatorStateInfo( 0 );

		return animatorStateInfo.IsName( animationName );
	}

	// check if we are playing one of the launching or landing animations (the player is only watching while they run)
	public bool IsLaunchingOrLanding()
	{
		// the camera is switched off while we are talking to aliens - nothing is launching or landing then
		if ( !isActiveAndEnabled )
		{
			return false;
		}

		var animatorStateInfo = m_animator.GetCurrentAnimatorStateInfo( 0 );

		return animatorStateInfo.IsName( "Launching (Docking Bay)" ) || animatorStateInfo.IsName( "Landing (Docking Bay)" ) || animatorStateInfo.IsName( "Launching (Planetside)" ) || animatorStateInfo.IsName( "Landing (Planetside)" );
	}

	// start a camera animation
	public void StartAnimation( string animationName )
	{
		Debug.Log( "Switching to the " + animationName + " camera animation." );

		// reset the camera to animation position
		SetCameraFollow( null, Vector3.zero );

		// play the new animation
		m_animator.Play( animationName );
	}

	//
	// everything below this point are animation event callbacks
	//

	public void BeginCountdown()
	{
		// play the countdown sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Countdown );

		// start the countdown animation
		SpaceflightController.m_instance.m_countdown.Show();

		// out of the docking bay the countdown is under the original's COMMENCING LAUNCH SEQUENCE..., on a cleared window (LAUNCH-OV LAUNCH: CTERASE, the line,
		// COUNTDOWN) - a launch from a planet has said it already, as the countdown is all of its launch
		if ( DataController.m_instance.m_playerData.m_general.m_location == PD_General.Location.DockingBay )
		{
			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Commencing launch sequence...</color>" );
		}
	}

	public void PlayerIsLaunching()
	{
		// yes - start fading the map out
		SpaceflightController.m_instance.m_viewport.StartFade( 0.0f, 5.0f );

		// yes - update the messages text
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Leaving starport...</color>" );
	}

	public void PlayerHasLaunched()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// are we in the docking bay location?
		if ( playerData.m_general.m_location == PD_General.Location.DockingBay )
		{
			// yes - calculate the new position of the player (just north of the arth spaceport)
			Vector3 playerPosition = gameData.m_planetList[ gameData.m_misc.m_arthPlanetId ].GetPosition();

			playerPosition.y = 0.0f;
			playerPosition.z += 128.0f;

			// update the player's system coordinates to the new position
			playerData.m_general.m_lastStarSystemCoordinates = playerPosition;

			// update the player location
			SpaceflightController.m_instance.SwitchLocation( PD_General.Location.JustLaunched );

			// the captain's menu, as the original's after the launch (this also ends the launch function)
			SpaceflightController.m_instance.m_buttonController.SetCaptainButtons();
		}
		else
		{
			// change to the alternate command button set
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.CommandB );
		}
	}

	public void PlayerHasLanded()
	{
		var spaceflightController = SpaceflightController.m_instance;

		// a landing without the fuel for it is a crash, and a planet of more than 7.99 G crushes the hull: the game is over (LAND-OV WF258 and WF1F1)
		if ( spaceflightController.m_landingCrashes || spaceflightController.m_landingCrushes )
		{
			var playerData = DataController.m_instance.m_playerData;
			var gameData = DataController.m_instance.m_gameData;
			var planet = gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];
			var star = gameData.m_starList[ planet.m_starId ];
			string cause;

			if ( spaceflightController.m_landingCrashes )
			{
				var shipName = string.IsNullOrEmpty( playerData.m_playerShip.m_name ) ? "" : playerData.m_playerShip.m_name.Trim() + " ";

				cause = "Emergency locator transmission: starship ISS " + shipName + "crashed on planet " + planet.m_orbitPosition + " in system " + star.m_xCoordinate + "," + star.m_yCoordinate + ". No survivors.";
			}
			else
			{
				cause = "Captain, the ship is being crushed by extreme gravity. The ...";
			}

			spaceflightController.m_landingCrashes = false;
			spaceflightController.m_landingCrushes = false;

			spaceflightController.m_combatController.EndGame( cause );

			return;
		}

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.CommandA );

		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Safe landing, captain.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

		MusicController.m_instance.ChangeToTrack( MusicController.Track.Planetside );
	}

	public void StartingDescent()
	{
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Autopilot engaged. Descending...</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Update );
	}

	// the terrain is there below the ship: the original says so then, after the autopilot (ORBIT-OV, DESCEND: "TOPOGRAPHY NET LOCKED ON" when the grid shows)
	public void TopographyNetLockedOn()
	{
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Topography net locked on.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Update );
	}

	public void SwitchToPlanetsideLocation()
	{
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );
	}

	public void SwitchToInOrbitLocation()
	{
		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.InOrbit );
	}

	public void StartDustStorm()
	{
		SpaceflightController.m_instance.m_planetside.StartDustStorm();
	}

	public void StartFireParticleSystems()
	{
		foreach ( var particleSystem in m_fireParticleSystems )
		{
			particleSystem.Play();
		}
	}

	public void StopFireParticleSystems()
	{
		foreach ( var particleSystem in m_fireParticleSystems )
		{
			particleSystem.Stop();
		}
	}
}
