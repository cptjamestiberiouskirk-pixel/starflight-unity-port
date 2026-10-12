
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

		// update the messages display
		SpaceflightController.m_instance.m_messages.Clear();

		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Computing descent profile...</color>" );

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
