
public class AnalysisButton : ShipButton
{
	public override string GetLabel()
	{
		return "Analysis";
	}

	// how many times the mass of our ship something has, as the original's .SIZE (ANALYZE-OV): the whole number when it is at least as big as our ship ("2", "38"),
	// else "0." and the decimals up to the first one that is not zero ("0.8", "0.04", "0.004")
	static string GetSizeText( int mass, int shipMass )
	{
		// never divide by zero (our ship always has the mass of its hull)
		if ( shipMass <= 0 )
		{
			return "?";
		}

		var whole = mass / shipMass;

		if ( ( whole > 0 ) || ( mass <= 0 ) )
		{
			return whole.ToString( System.Globalization.CultureInfo.InvariantCulture );
		}

		var text = "0.";
		var remainder = mass;

		// (a mass of at least 1 has a digit that is not zero within as many decimals as our ship's mass has digits - 10 is more than enough)
		for ( var decimalPlace = 0; decimalPlace < 10; decimalPlace++ )
		{
			remainder = ( remainder % shipMass ) * 10;

			var digit = remainder / shipMass;

			text += digit.ToString( System.Globalization.CultureInfo.InvariantCulture );

			if ( digit != 0 )
			{
				break;
			}
		}

		return text;
	}

	// a line of an alien ship's analysis, or the original's "NOT CERTAIN" when the science officer cannot tell (?.CERTAIN, rolled for each line)
	static string Certain( string value, int scienceSkill )
	{
		return ( UnityEngine.Random.Range( 0.0f, 100.0f ) < scienceSkill / 2.0f ) ? value : "Not certain";
	}

	// an alien ship's shields, as the original's .SHIELDS (ANALYZE-OV): none apparent with no shields, unknown for the Gazurtoids, else the class equivalent of the
	// vessel's shields (200 points a class) and whether the aliens have raised them
	static string GetAlienShieldsText( GD_Vessel vessel )
	{
		if ( vessel.m_shields <= 0 )
		{
			return "None apparent";
		}

		if ( vessel.m_race == GameData.Race.Gazurtoid )
		{
			return "Unknown";
		}

		var encounter = SpaceflightController.m_instance.m_encounter;
		var raised = ( DataController.m_instance.m_playerData.m_general.m_location == PD_General.Location.Encounter ) && ( encounter != null ) && encounter.AliensHaveShieldsUp();

		return "Class " + ( vessel.m_shields / 200 ) + " equivalent " + ( raised ? "raised" : "lowered" );
	}

	// an alien ship's weapons, as the original's .WEAP (ANALYZE-OV)
	static string GetAlienWeaponsText()
	{
		var encounter = SpaceflightController.m_instance.m_encounter;
		var armed = ( DataController.m_instance.m_playerData.m_general.m_location == PD_General.Location.Encounter ) && ( encounter != null ) && encounter.AliensAreArmed();

		return armed ? "Armed" : "Not armed";
	}

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

		// do we have sensor data to analyze? an analysis uses the reading up, whatever was read (ANALYZE-OV (/ANALYSIS)), so a second one needs a new reading
		var hasCurrentReading = sensorsDisplay.m_hasSensorData && ( sensorsDisplay.m_readingCount != sensorsDisplay.m_analyzedReadingCount );

		if ( hasCurrentReading )
		{
			sensorsDisplay.m_analyzedReadingCount = sensorsDisplay.m_readingCount;

			// yes - start building the new messages text
			// (the original's heading has a colon: ANALYZE-OV, "ANALYSIS OF LAST SENSOR READING:")
			text = "<color=white>Analysis of Last Sensor Reading:</color>\n";

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

					// gravity (once it is known, Land warns of a gravity the ship cannot land in or launch from: the original's ?G-AWARE)
					var gravityText = Known( planet.GetGravityText(), skill );

					if ( gravityText != "Unknown" )
					{
						SpaceflightController.m_instance.m_gravityAnalysedPlanetId = planet.m_id;
					}

					text += "Gravity: <color=white>" + gravityText + "</color>\n";

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

						// size (the original's line has no label)
						text += "<color=white>" + GetSizeText( vessel.m_mass, playerData.m_playerShip.m_mass ) + " times the size of our ship.</color>\n";
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
					text += "Type: <color=white>" + Certain( vessel.m_type, skill ) + "</color>\n";

					// size (the original's line has no label, and the science officer is always sure of it)
					text += "<color=white>" + GetSizeText( vessel.m_mass, playerData.m_playerShip.m_mass ) + " times the size of our ship.</color>\n";

					// shields
					text += "Shields: <color=white>" + Certain( GetAlienShieldsText( vessel ), skill ) + "</color>\n";

					// weapon status
					text += "Weapon Status: <color=white>" + Certain( GetAlienWeaponsText(), skill ) + "</color>\n";

					break;
				}
			}
		}
		else
		{
			// nope - buzz
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			// complain to the player
			// (the original's ANALYZE-OV: "I NEED A CURRENT SENSOR READING.")
			text = "<color=white>I need a current sensor reading.</color>";
		}

		// update the messages text
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( text );

		// turn off the active button dot
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
