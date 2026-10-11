
public class AnalysisButton : ShipButton
{
	public override string GetLabel()
	{
		return "Analysis";
	}

	// how many times the mass of our ship something has, with one decimal (dividing the two whole numbers made everything smaller than our ship "0 times the size")
	static string GetSizeText( int mass, int shipMass )
	{
		// never divide by zero (our ship always has the mass of its hull)
		if ( shipMass <= 0 )
		{
			return "?";
		}

		var ratio = (float) mass / (float) shipMass;

		// something that has a mass is never "0.0 times" our size
		if ( ( mass > 0 ) && ( ratio < 0.05f ) )
		{
			return "less than 0.1";
		}

		return ratio.ToString( "F1", System.Globalization.CultureInfo.InvariantCulture );
	}

	// the reading of a planet the science officer has analyzed last (the 1984 notes, starship.txt: "I have already attempted analysis of this data, Captain" - alien
	// ships "may be read and analyzed as often as desired")
	static int s_analyzedReading = -1;

	// a line of the analysis the science officer gives with a chance of their science skill / 2 percent, else "Unknown" (starship.txt: "For each header there is a
	// chance that the Science Officer will give some information. That % chance is = the Science Officer's skill / 2")
	static string Known( string value, int scienceSkill )
	{
		return ( UnityEngine.Random.Range( 0.0f, 100.0f ) < scienceSkill / 2.0f ) ? value : "Unknown";
	}

	public override bool Execute()
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// new message text
		string text;

		// the analysis is the science officer's
		var scienceOfficer = playerData.m_crewAssignment.IsAssigned( PD_CrewAssignment.Role.ScienceOfficer ) ? playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.ScienceOfficer ) : null;
		var sensorsDisplay = SpaceflightController.m_instance.m_displayController.m_sensorsDisplay;

		if ( ( scienceOfficer == null ) || ( scienceOfficer.m_vitality <= 0 ) )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( ( scienceOfficer == null ) ? "<color=red>No science officer assigned!</color>" : "<color=red>The science officer is incapacitated!</color>" );

			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

			return false;
		}

		var skill = scienceOfficer.m_science;

		// a planet's reading is analyzed once
		if ( sensorsDisplay.m_hasSensorData && ( sensorsDisplay.m_scanType == SensorsDisplay.ScanType.Planet ) )
		{
			if ( sensorsDisplay.m_readingCount == s_analyzedReading )
			{
				SoundController.m_instance.PlaySound( SoundController.Sound.Error );

				SpaceflightController.m_instance.m_messages.Clear();
				SpaceflightController.m_instance.m_messages.AddText( "<color=white>I have already attempted analysis of this data, Captain.</color>" );

				SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

				return false;
			}

			s_analyzedReading = sensorsDisplay.m_readingCount;
		}

		// do we have sensor data to analyze?
		if ( sensorsDisplay.m_hasSensorData )
		{
			// yes - start building the new messages text
			text = "<color=white>Analysis of Last Sensor Reading</color>\n";

			// what did we just scan
			switch ( SpaceflightController.m_instance.m_displayController.m_sensorsDisplay.m_scanType )
			{
				case SensorsDisplay.ScanType.Planet:
				{
					// get to the planet
					GD_Planet planet = gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];

					// object and orbit number
					// (the object's class is given whatever the science officer's skill, every other line by the skill's chance)
					text += "Object: <color=white>Planet</color>   Orbit Number: <color=white>" + Known( planet.m_orbitPosition.ToString(), skill ) + "</color>\n";

					// surface
					text += "Predominant Surface: <color=white>" + Known( planet.GetSurfaceText(), skill ) + "</color>\n";

					// gravity
					text += "Gravity: <color=white>" + Known( planet.GetGravityText(), skill ) + "</color>\n";

					// atmospheric density
					text += "Atmospheric Density: <color=white>" + Known( planet.GetAtmosphericDensityText(), skill ) + "</color>\n";

					// temperature
					text += "Temperature: <color=white>" + Known( planet.GetTemperatureText(), skill ) + "</color>\n";

					// weather
					text += "Global Weather: <color=white>" + Known( planet.GetGlobalWeatherText(), skill ) + "</color>";

					break;
				}

				case SensorsDisplay.ScanType.Minstrel:
				{
					// get scan type index and validate bounds
					int minstrelIndex = (int) SpaceflightController.m_instance.m_displayController.m_sensorsDisplay.m_scanType;

					if ( minstrelIndex >= 0 && minstrelIndex < gameData.m_vesselList.Length )
					{
						// display the ship information
						var vessel = gameData.m_vesselList[ minstrelIndex ];

						// object
						text += "Object: <color=white>" + vessel.m_object + "</color>\n";

						// size
						text += "Size: <color=white>" + GetSizeText( vessel.m_mass, playerData.m_playerShip.m_mass ) + " times the size of our ship</color>\n";
					}
					else
					{
						text += "Object: <color=yellow>Unknown Vessel</color>\n";
					}

					break;
				}

				case SensorsDisplay.ScanType.Debris:
				{
					// debris is only its class (the 1984 notes, starship.txt: "DEBRIS - Nothing") - what it holds is in the sensor reading
					text += "Object: <color=white>Debris</color>";

					break;
				}

				case SensorsDisplay.ScanType.Unknown:
				{
					// display unknown object analysis
					text += "Object: <color=yellow>Unknown</color>\n";
					text += "Type: <color=yellow>Unidentified</color>\n";
					text += "Status: <color=yellow>Insufficient data for analysis</color>\n";
					text += "Recommendation: <color=white>Proceed with caution</color>";

					break;
				}

				default:
				{
						// get scan type index and validate bounds
						int scanTypeIndex = (int) SpaceflightController.m_instance.m_displayController.m_sensorsDisplay.m_scanType;

						if ( scanTypeIndex < 0 || scanTypeIndex >= gameData.m_vesselList.Length )
						{
							// invalid scan type, show unknown
							text += "Object: <color=yellow>Unknown</color>\n";
							text += "Type: <color=yellow>Unidentified</color>\n";
							text += "Status: <color=yellow>Insufficient data for analysis</color>\n";
							break;
						}

						// display the ship information
						var vessel = gameData.m_vesselList[ scanTypeIndex ];
					text += "Object: <color=white>" + vessel.m_object + "</color>\n";

					// type
					text += "Type: <color=white>" + Known( vessel.m_type, skill ) + "</color>\n";

					// size
					text += "Size: <color=white>" + Known( GetSizeText( vessel.m_mass, playerData.m_playerShip.m_mass ) + " times the size of our ship", skill ) + "</color>\n";

					// shields
					text += "Shields: <color=yellow>Not certain</color>\n";

					// weapon status
					text += "Weapon Status: <color=yellow>Not certain</color>\n";

					break;
				}
			}
		}
		else
		{
			// nope - buzz
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			// complain to the player
			text = "<color=white>I need a current senor reading.</color>";
		}

		// update the messages text
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( text );

		// turn off the active button dot
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
