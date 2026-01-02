
using UnityEngine;

public class Planetside : MonoBehaviour
{
	// the terrain grid
	public TerrainGrid m_terrainGrid;

	// the clouds
	public MeshRenderer m_clouds;

	// the player camera (for getting altitude)
	public Camera m_camera;

	// the dust storm particle effect
	public ParticleSystem m_dustStorm;

	// at what altitude should clouds and planet skybox become completely transparent
	public float m_fadeMaxAltitude;
	public float m_fadeMinAltitude;

	// the opacity of the clouds
	float m_cloudOpacity;

	// unity late update
	void LateUpdate()
	{
		//  calculate the opacity of the clouds based on altitude
		var opacity = Mathf.SmoothStep( 1.0f, 0.0f, ( m_camera.transform.position.y - m_fadeMinAltitude ) / ( m_fadeMaxAltitude - m_fadeMinAltitude ) );

		// update the material
		Tools.SetOpacity( m_clouds.material, opacity * m_cloudOpacity );

		// do the same for the skybox blend factor
		StarflightSkybox.m_instance.m_currentBlendFactor = Mathf.Lerp( 0.0f, 1.0f, opacity );
	}

	// call this to hide the in orbit objects
	public void Hide()
	{
		if ( !gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Hiding the planetside location." );

		// turn off the fog
		RenderSettings.fog = false;

		// hide the planetside location
		gameObject.SetActive( false );
	}

	// call this to show the planetside objects
	public void Show()
	{
		if ( gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Showing the planetside location." );

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// if we are returning from disembarked, transfer TV cargo to ship and refuel TV
		if ( playerData.m_general.m_lastLocation == PD_General.Location.Disembarked )
		{
			TransferTerrainVehicleCargo();
		}

		// get to the star data
		var star = gameData.m_starList[ playerData.m_general.m_currentStarId ];

		// show the planetside objects
		gameObject.SetActive( true );

		// get the planet controller
		var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

		Debug.Log( "Showing planet number " + planetController.m_planet.m_id + "." );

		// move the player object
		SpaceflightController.m_instance.m_playerShip.transform.position = playerData.m_general.m_coordinates = new Vector3( 0.0f, 0.0f, 0.0f );

		// freeze the player
		SpaceflightController.m_instance.m_playerShip.Freeze();

		// are we in the middle of the landing animation?
		if ( !SpaceflightController.m_instance.m_playerCamera.IsCurrentlyPlaying( "Landing (Planetside)" ) )
		{
			// no - play the on planet animation
			SpaceflightController.m_instance.m_playerCamera.StartAnimation( "On Planet" );

			// reset the buttons
			SpaceflightController.m_instance.m_buttonController.SetBridgeButtons();

			// play the planetside music track
			MusicController.m_instance.ChangeToTrack( MusicController.Track.Planetside );
		}

		// fade in the map
		SpaceflightController.m_instance.m_viewport.StartFade( 1.0f, 2.0f );

		// set up the clouds and atmosphere
		planetController.SetupClouds( m_clouds, null, true, true );

		// save the opacity of the clouds
		m_cloudOpacity = Tools.GetOpacity( m_clouds.material );

		// turn skybox autorotate off
		StarflightSkybox.m_instance.m_autorotateSkybox = false;

		// make sure skybox rotation is reset
		StarflightSkybox.m_instance.m_currentRotation = Quaternion.identity;

		// change the ship display
		SpaceflightController.m_instance.m_displayController.ChangeDisplay( SpaceflightController.m_instance.m_displayController.m_statusDisplay );
	}

	// update the terrain grid now (after planet generator has run)
	public void UpdateTerrainGridNow()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get the planet controller
		var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

		// get the planet generator
		var planetGenerator = planetController.GetPlanetGenerator();

		// tell the terrain grid to bake in the elevation
		m_terrainGrid.BakeInElevation( playerData.m_general.m_selectedLatitude, playerData.m_general.m_selectedLongitude, planetGenerator );
	}

	// call this to start the launch animation
	public void StartLaunchAnimation()
	{
		// play the planet launch sound
		SoundController.m_instance.PlaySound( SoundController.Sound.PlanetLaunching );

		// run the launching camera animation
		SpaceflightController.m_instance.m_playerCamera.StartAnimation( "Launching (Planetside)" );
	}

	// start the dust storm effect
	public void StartDustStorm()
	{
		m_dustStorm.Clear();
		m_dustStorm.Play();
	}

	// transfer all cargo from terrain vehicle to ship and refuel the terrain vehicle
	void TransferTerrainVehicleCargo()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;

		// refuel the terrain vehicle
		playerData.m_terrainVehicle.Refuel();

		// transfer elements from TV to ship
		var tvElements = playerData.m_terrainVehicle.m_elementStorage;
		var shipRemainingVolume = playerData.m_playerShip.GetRemainingVolume();
		var totalVolumeTransferred = 0;

		// copy the list since we're modifying it (check for null first)
		if ( tvElements != null && tvElements.m_elementList != null )
		{
			var elementsToTransfer = new System.Collections.Generic.List<PD_ElementReference>( tvElements.m_elementList );

			foreach ( var elementRef in elementsToTransfer )
			{
				var volumeToTransfer = Mathf.Min( elementRef.m_volume, shipRemainingVolume - totalVolumeTransferred );

				if ( volumeToTransfer > 0 )
				{
					// add to ship
					playerData.m_playerShip.AddElement( elementRef.m_elementId, volumeToTransfer );

					// remove from TV
					playerData.m_terrainVehicle.RemoveElement( elementRef.m_elementId, volumeToTransfer );

					totalVolumeTransferred += volumeToTransfer;

					Debug.Log( "Transferred " + volumeToTransfer + " m³ of element " + elementRef.m_elementId + " from TV to ship." );
				}
			}
		}

		// transfer artifacts from TV to ship
		var tvArtifacts = playerData.m_terrainVehicle.m_artifactStorage;

		// copy the list since we're modifying it (check for null first)
		if ( tvArtifacts != null && tvArtifacts.m_artifactList != null )
		{
			var artifactsToTransfer = new System.Collections.Generic.List<PD_ArtifactReference>( tvArtifacts.m_artifactList );

			foreach ( var artifactRef in artifactsToTransfer )
			{
				var artifactVolume = gameData.m_artifactList[ artifactRef.m_artifactId ].m_volume;

				if ( playerData.m_playerShip.GetRemainingVolume() >= artifactVolume )
				{
					// add to ship
					playerData.m_playerShip.AddArtifact( artifactRef.m_artifactId );

					// remove from TV
					playerData.m_terrainVehicle.RemoveArtifact( artifactRef.m_artifactId );

					Debug.Log( "Transferred artifact " + artifactRef.m_artifactId + " from TV to ship." );
				}
			}
		}

		// play transporter sound if anything was transferred
		if ( totalVolumeTransferred > 0 )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Transporter );
		}

		// recalculate volumes
		playerData.m_terrainVehicle.RecalculateVolumeUsed();
		playerData.m_playerShip.RecalculateVolumeUsed();
	}
}
