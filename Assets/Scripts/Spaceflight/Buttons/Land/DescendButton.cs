
using UnityEngine;

public class DescendButton : ShipButton
{
	public override string GetLabel()
	{
		return "Descend";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// engines that have broken down, or malfunction now, cannot take the ship down (SHIPBUTTONS WEFFC: the equipment check with part 5 before TRY-LAND)
		if ( !playerData.m_playerShip.UseEquipment( PD_PlayerShip.ShipSystem.Engines ) )
		{
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

			return false;
		}

		// update the messages display
		SpaceflightController.m_instance.m_messages.Clear();

		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Computing descent profile...</color>" );

		// the landing burns the gravity's share of Endurium, and with too little of it the ship crashes, on a planet of more than 7.99 G its hull is crushed
		// (LAND-OV WF430: ?FUEL-DIE is decided before the fuel burns) - the touchdown tells (PlayerCamera.PlayerHasLanded)
		var planet = DataController.m_instance.m_gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];

		SpaceflightController.m_instance.m_landingCrashes = playerData.m_playerShip.HasTooLittleFuelToLand( planet );
		SpaceflightController.m_instance.m_landingCrushes = PD_PlayerShip.GravityCrushesHull( planet );

		playerData.m_playerShip.BurnLandingFuel( planet );

		// set the landing coordinates
		SpaceflightController.m_instance.m_planetside.UpdateTerrainGridNow();

		// start the landing animation
		SpaceflightController.m_instance.m_playerCamera.StartAnimation( "Landing (Planetside)" );

		// start the landing sound
		SoundController.m_instance.PlaySound( SoundController.Sound.PlanetLanding );

		// stop the music
		MusicController.m_instance.ChangeToTrack( MusicController.Track.None );

		return true;
	}

	public override bool Update()
	{
		// the landing animation says the rest of the original's lines (PlayerCamera.StartingDescent, TopographyNetLockedOn and PlayerHasLanded)

		// don't let the default ship button update run - the controls stay locked until the landing animation changes the buttons (see PlayerCamera.PlayerHasLanded)
		return true;
	}
}
