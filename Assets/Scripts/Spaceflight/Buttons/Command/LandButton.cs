
public class LandButton : ShipButton
{
	public override string GetLabel()
	{
		return "Land";
	}

	public override bool Execute()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		switch ( playerData.m_general.m_location )
		{
			case PD_General.Location.JustLaunched:

				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_messages.Clear();

				SpaceflightController.m_instance.m_messages.AddText( "<color=white>We can't land on Arth.</color>" );

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				break;

			case PD_General.Location.InOrbit:

				// a planet whose maps could not be generated has no surface we could go down to (the planet generator has aborted it and logged why)
				var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

				if ( ( planetController == null ) || !planetController.HasMaps() )
				{
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );

					SpaceflightController.m_instance.m_messages.Clear();

					SpaceflightController.m_instance.m_messages.AddText( "<color=white>We can't land here.\nThe surface of this planet could not be mapped.</color>" );

					SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

					break;
				}

				// show the terrian map display
				SpaceflightController.m_instance.m_displayController.ChangeDisplay( SpaceflightController.m_instance.m_displayController.m_terrainMapDisplay );

				// change the buttons
				SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Land );

				// over the Crystal Planet the Crystal Cone reports where its control nexus is, for picking the landing site
				CrystalCone.ReportNexus();

				return true;

			default:

				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_messages.Clear();

				SpaceflightController.m_instance.m_messages.AddText( "<color=white>We're not in orbit.</color>" );

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				break;
		}

		return false;
	}
}
