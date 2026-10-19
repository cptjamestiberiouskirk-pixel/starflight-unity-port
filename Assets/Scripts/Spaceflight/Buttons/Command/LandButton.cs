
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

				// engines that have broken down, or malfunction now, cannot take the ship down (CAPTAIN /LAUNCH/LAND: the equipment check with part 5 in orbit)
				if ( !playerData.m_playerShip.UseEquipment( PD_PlayerShip.ShipSystem.Engines ) )
				{
					SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

					break;
				}

				// the heat of the Crystal Planet keeps the landing computer from responding to a ship without the Crystal Orb (LAND-OV ?LAND)
				if ( ( SpaceflightController.m_instance.m_inOrbit != null ) && SpaceflightController.m_instance.m_inOrbit.IsCrystalHeatActive() )
				{
					SoundController.m_instance.PlaySound( SoundController.Sound.Error );

					SpaceflightController.m_instance.m_messages.Clear();

					SpaceflightController.m_instance.m_messages.AddText( "<color=white>Computer not responding!</color>" );

					SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

					break;
				}

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

				// the gravity has been analysed: the landing computer warns once of a landing the ship would not survive or launch from, and a second Land goes down
				// anyway (LAND-OV ?LAND and WF45E)
				var planet = DataController.m_instance.m_gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];

				if ( ( SpaceflightController.m_instance.m_gravityAnalysedPlanetId == planet.m_id ) && ( SpaceflightController.m_instance.m_landingWarnedPlanetId != planet.m_id ) )
				{
					string warning = null;

					if ( PD_PlayerShip.GravityCrushesHull( planet ) )
					{
						warning = "This planet's gravity will crush the ship's hull.";
					}
					else if ( playerData.m_playerShip.HasTooLittleFuelToLand( planet ) )
					{
						warning = "Insufficient fuel for safe landing.";
					}
					else if ( playerData.m_playerShip.HasTooLittleFuelToRelaunch( planet, false ) )
					{
						warning = "Insufficient fuel for re-launching.";
					}

					if ( warning != null )
					{
						SpaceflightController.m_instance.m_landingWarnedPlanetId = planet.m_id;

						SoundController.m_instance.PlaySound( SoundController.Sound.Error );
						SpaceflightController.m_instance.m_messages.Clear();
						SpaceflightController.m_instance.m_messages.AddText( "<color=white>" + warning + "</color>" );
						SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
						break;
					}
				}

				// show the terrian map display
				SpaceflightController.m_instance.m_displayController.ChangeDisplay( SpaceflightController.m_instance.m_displayController.m_terrainMapDisplay );

				// change the buttons
				SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Land );

				// over the Crystal Planet the Crystal Cone reports where its control nexus is, for picking the landing site
				CrystalCone.ReportNexus();

				// with the Red Cylinder aboard, the planet's message and artifact sites flash on the map (SITE-OV .MERCATOR)
				RedCylinder.MapOpened();

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
