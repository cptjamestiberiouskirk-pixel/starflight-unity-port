
using UnityEngine;

public class DockingBay : MonoBehaviour
{
	// particle systems
	public ParticleSystem m_decompressionParticleSystem;

	// the animator
	Animator m_animator;

	// unity awake
	void Awake()
	{
		// get the animator
		m_animator = GetComponent<Animator>();
	}

	// call this to hide the docking bay
	public void Hide()
	{
		if ( !gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Hiding the docking bay location." );

		// hide the docking bay
		gameObject.SetActive( false );
	}

	// call this to switch to the docking bay
	public void Show()
	{
		if ( gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Showing the docking bay location." );

		// show the docking bay
		gameObject.SetActive( true );

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// put us in the right spot for the docking bay launch sequence
		SpaceflightController.m_instance.m_playerShip.transform.position = playerData.m_general.m_coordinates = new Vector3( 0.0f, 0.0f, 0.0f );

		// freeze the player
		SpaceflightController.m_instance.m_playerShip.Freeze();

		// play the in space camera animation
		SpaceflightController.m_instance.m_playerCamera.StartAnimation( "In Space" );

		// reset the buttons
		SpaceflightController.m_instance.m_buttonController.SetBridgeButtons();

		// fade in the map
		SpaceflightController.m_instance.m_viewport.StartFade( 1.0f, 2.0f );

		// make sure we have the status display up
		SpaceflightController.m_instance.m_displayController.ChangeDisplay( SpaceflightController.m_instance.m_displayController.m_statusDisplay );

		// play the docking bay music track
		MusicController.m_instance.ChangeToTrack( MusicController.Track.DockingBay );

		// back at the Starport: Interstel evaluates the planets recommended for colonization
		if ( playerData.EvaluateColonyRecommendations() > 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Interstel has evaluated your colony recommendations - see Operations.</color>" );
		}

		// back at the Starport after the terrain vehicle was lost: a new one is installed, and the crew is fined (STRINFO 1.2)
		if ( playerData.m_terrainVehicle.m_isLost )
		{
			playerData.m_terrainVehicle.m_isLost = false;

			playerData.m_bank.m_currentBalance -= PD_TerrainVehicle.c_lossFine;
			playerData.m_bank.m_transactionList.Add( new PD_Bank.Transaction( playerData.m_general.m_currentStardateYMD, "Terrain vehicle replaced", PD_TerrainVehicle.c_lossFine + "-" ) );

			var lossText = DataController.m_instance.m_gameData.FindStoryText( "TerrainVehicleLost" );

			if ( lossText != null )
			{
				playerData.m_general.AddSupplementalEvaluation( lossText.m_text );
			}

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>A new terrain vehicle has been installed. Interstel has fined you " + PD_TerrainVehicle.c_lossFine.ToString( "N0", System.Globalization.CultureInfo.InvariantCulture ) + " MU - see Operations.</color>" );
		}

		// back at the Starport with lifeform data: Interstel pays for it
		var lifeformDataValue = playerData.PayLifeformData();

		if ( lifeformDataValue > 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=green>Interstel has paid " + lifeformDataValue.ToString( "N0", System.Globalization.CultureInfo.InvariantCulture ) + " MU for the lifeform data you recorded.</color>" );
		}

		// back at the Starport after the win: Interstel pays the bonus that comes with its medal
		if ( playerData.PayWinBonus() )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=green>Interstel has paid the bonus of " + PD_General.c_winBonus.ToString( "N0", System.Globalization.CultureInfo.InvariantCulture ) + " MU into your account.</color>" );
		}
	}

	// call this to start the launch animation
	public void StartLaunchAnimation()
	{
		m_animator.Play( "Docking Bay Launch" );

		// play the launch sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Launch );

		// play the docking bay door open sound
		SoundController.m_instance.PlaySound( SoundController.Sound.DockingBayDoorOpen );

		// play the decompression sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Decompression );

		// run the launching camera animation
		SpaceflightController.m_instance.m_playerCamera.StartAnimation( "Launching (Docking Bay)" );

		// fire up the particle system
		m_decompressionParticleSystem.Play();
	}

	// call this to start the landing animation
	public void StartLandingAnimation()
	{
		m_animator.Play( "Docking Bay Landing" );

		// play the docking bay door open sound
		SoundController.m_instance.PlaySound( SoundController.Sound.DockingBayDoorClose );

		// the doors close behind the ship: the first thing said on arrival (the original's "CLOSING DOCKING BAY DOORS"), before the ship computer and Interstel's notices
		SpaceflightController.m_instance.m_messages.InsertText( "<color=white>Closing docking bay doors.</color>" );

		// run the landing camera animation
		SpaceflightController.m_instance.m_playerCamera.StartAnimation( "Landing (Docking Bay)" );
	}
}
