
public class StarmapButton : ShipButton
{
	public override string GetLabel()
	{
		return "Starmap";
	}

	public override bool Execute()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		if ( playerData.m_general.m_location != PD_General.Location.Hyperspace )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.Clear();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>We're not in hyperspace, captain.</color>" );

			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		}
		else if ( SpaceflightController.m_instance.HasLostBearings() )
		{
			// after a flux jump the navigator has to get the bearings back first (the original's MAP-OV (/STARMAP) waits for NAV-TIME, and WEC57 says this)
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.Clear();

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>I can't get our bearings, captain.</color>" );

			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		}
		else
		{
			// show the starmap
			SpaceflightController.m_instance.m_starmap.Show();
		}

		return false;
	}
}
