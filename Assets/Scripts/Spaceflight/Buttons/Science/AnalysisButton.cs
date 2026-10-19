
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

	// makes the science officer's survey of the planet the ship orbits (PlanetSurvey) and returns its lines of the analysis: the mineral fields and the
	// lifeforms found (each line by the skill's chance, as every other line of a planet's analysis)
	static string GetSurveyText( GD_Planet planet, int skill )
	{
		var controller = SpaceflightController.m_instance;
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var planetController = controller.m_starSystem.GetPlanetController( planet.m_id );

		// (a survey is made from orbit, of a planet whose maps are there to work it out from)
		if ( ( playerData.m_general.m_location != PD_General.Location.InOrbit ) || ( planetController == null ) || !planetController.HasMaps() || planet.IsGasGiant() )
		{
			return "";
		}

		var survey = PlanetSurvey.Make( planet, planetController.GetPlanetGenerator(), controller.m_disembarked.m_terrainGrid, skill, playerData.m_playerShip.GetSystemFactor( PD_PlayerShip.ShipSystem.Sensors ) );

		if ( survey == null )
		{
			return "";
		}

		controller.m_planetSurvey = survey;

		string minerals;

		if ( survey.m_mineralFields == 0 )
		{
			minerals = "None";
		}
		else if ( survey.m_mineralFieldsFound == 0 )
		{
			minerals = "None found";
		}
		else
		{
			var element = ( ( survey.m_mainElementId >= 0 ) && ( survey.m_mainElementId < gameData.m_elementList.Length ) ) ? ", mostly " + gameData.m_elementList[ survey.m_mainElementId ].m_name : "";

			minerals = survey.m_mineralFieldsFound + ( ( survey.m_mineralFieldsFound == 1 ) ? " field" : " fields" ) + element;
		}

		// the species, and the latitude bands their lifeforms were found in
		var bandsFound = survey.m_lifeBands.Count;
		var life = ( survey.m_speciesCount == 0 ) ? "None" : ( survey.m_speciesCount + " species, " + ( ( bandsFound == 0 ) ? "none found" : ( "found in " + bandsFound + ( ( bandsFound == 1 ) ? " band" : " bands" ) ) ) );

		return "\nMineral Fields: <color=white>" + Known( minerals, skill ) + "</color>\nLifeforms: <color=white>" + Known( life, skill ) + "</color>";
	}

	// a line of an analysis (an alien ship's, a planet's, a star's state), or the original's "NOT CERTAIN" when the science officer cannot tell (?.CERTAIN, rolled
	// for each line: certain when the skill beats RRND 0 to 200, or with a creature file loaded, PLHI, which is never so in space: DEPART and OSET reset it)
	public static string Certain( string value, int scienceSkill )
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

	// a line of a planet's analysis the science officer gives with a chance of their science skill / 2 percent, else the original's "NOT CERTAIN" (ANALYZE-OV
	// ?.CERTAIN, as for an alien ship's lines)
	static string Known( string value, int scienceSkill )
	{
		return Certain( value, scienceSkill );
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
					// (the object's class and the orbit number are given whatever the science officer's skill, as the original's .ORBIT is never in doubt, every
					// other line by the skill's chance)
					text += "Object: <color=white>Planet</color>   Orbit Number: <color=white>" + planet.m_orbitPosition + "</color>\n";

					// surface
					text += "Predominant Surface: <color=white>" + Known( GetSurfaceWord( planet ), skill ) + "</color>\n";

					// gravity (once it is known, Land warns of a gravity the ship cannot land in or launch from: the original's ?G-AWARE)
					var gravityText = Known( planet.GetGravityText(), skill );

					if ( gravityText != "Not certain" )
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

					// the survey: the mineral fields and the lifeforms the officer finds on the planet, as well as the officer's skill and the sensors allow
					// (decided 2026-10-17, ROADMAP D6), marked on the sensor picture and the landing map
					text += GetSurveyText( planet, skill );

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
					// debris is its class and what it is made of, whatever the science officer's skill (ANALYZE-OV .CONSTIT: "CONSTITUENTS:" and the elements of the
					// vessel's debris)
					text += "Object: <color=white>Debris</color>\nConstituents:" + SensorsDisplay.GetConstituentLines( sensorsDisplay.m_debrisVesselId );

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

	// the predominant surface as the original's analysis words it (ANALYZE-OV (.SURF)): GAS for a gas giant, and UKNOWN [sic] for the one surface it has no
	// word for, the Crystal Planet's
	static string GetSurfaceWord( GD_Planet planet )
	{
		switch ( planet.m_surfaceId )
		{
			case 0: return "Unknown";
			case 1: return "Gas";
		}

		return planet.GetSurfaceText();
	}
}
