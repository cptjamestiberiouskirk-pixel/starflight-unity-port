
using UnityEngine;

public class LaunchYesButton : ShipButton
{
	public override string GetLabel()
	{
		return "Yes";
	}

	public override bool Execute()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		if ( playerData.m_general.m_location == PD_General.Location.DockingBay )
		{
			// update the messages log
			SpaceflightController.m_instance.m_messages.Clear();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Opening docking bay doors...</color>" );

			// start the launch animation from the docking bay
			SpaceflightController.m_instance.m_dockingBay.StartLaunchAnimation();
		}
		else
		{
			// a launch from the surface burns the gravity's share of Endurium, and without it the ship stays down (LAND-OV ?LAUNCH and WF39D)
			var planet = DataController.m_instance.m_gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];

			if ( playerData.m_playerShip.HasTooLittleFuelToRelaunch( planet, true ) )
			{
				SoundController.m_instance.PlaySound( SoundController.Sound.Error );
				SpaceflightController.m_instance.m_messages.Clear();
				SpaceflightController.m_instance.m_messages.AddText( "<color=white>Insufficient fuel for re-launching.</color>" );
				SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.CommandA );

				return false;
			}

			playerData.m_playerShip.BurnLandingFuel( planet );

			// update the messages log
			SpaceflightController.m_instance.m_messages.Clear();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Commencing launch sequence...</color>" );

			// start the launch animation from planetside
			SpaceflightController.m_instance.m_planetside.StartLaunchAnimation();
		}

		// stop the music
		MusicController.m_instance.ChangeToTrack( MusicController.Track.None );

		return true;
	}

	public override bool Update()
	{
		// don't let the default ship button update run
		return true;
	}
}
