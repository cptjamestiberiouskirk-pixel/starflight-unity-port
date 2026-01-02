
public class DistressButton : ShipButton
{
	public override string GetLabel()
	{
		return "Distress";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		SpaceflightController.m_instance.m_messages.Clear();

		// check location - distress makes no sense when docked
		if ( playerData.m_general.m_location == PD_General.Location.DockingBay )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>You're safely docked. No need for a distress signal.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// check if we're already in an encounter
		if ( playerData.m_general.m_location == PD_General.Location.Encounter )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>We're already in contact with another vessel!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// send distress signal
		SpaceflightController.m_instance.m_messages.AddText(
			"<color=yellow>DISTRESS SIGNAL SENT</color>\n" +
			"<color=white>Broadcasting on all frequencies...</color>\n" +
			"<color=red>Warning: Signal may attract hostiles!</color>"
		);

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
