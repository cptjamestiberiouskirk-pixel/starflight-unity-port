
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

		// reset timer and step
		m_timer = 0.0f;
		m_step = 0;

		return true;
	}

	public override bool Update()
	{
		m_timer += Time.deltaTime;

		if ( m_timer >= 1.0f && m_step == 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Autopilot engaged. Descending...</color>" );
			m_step++;
		}
		else if ( m_timer >= 6.0f && m_step == 1 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Topography net locked on.</color>" );
			m_step++;
		}
		else if ( m_timer >= 12.0f && m_step == 2 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Safe landing, captain.</color>" );
			m_step++;
		}

		return false;
	}

	float m_timer;
	int m_step;
}
