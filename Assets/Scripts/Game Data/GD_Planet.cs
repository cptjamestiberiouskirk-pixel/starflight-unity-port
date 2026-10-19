
using UnityEngine;
using System;

[Serializable]

public class GD_Planet
{
	public const int c_mapWidth = 48;
	public const int c_mapHeight = 24;

	// the name of the planet type of the Crystal Planet in the game data
	public const string c_crystalPlanetTypeName = "The Crystal Planet";

	public int m_id;
	public int m_starId;
	public int m_planetTypeId;

	public int m_orbitPosition;

	public int m_mass;
	public int m_bioDensity;
	public int m_mineralDensity;

	public int m_atmosphereIdA;
	public int m_atmosphereIdB;
	public int m_atmosphereIdC;

	public int m_atmosphereDensityId;

	public int m_hydrosphereId;

	public bool m_hasEthanol;

	public int m_elementIdA;
	public int m_elementIdB;
	public int m_elementIdC;

	public int m_surfaceId;

	public int m_gravity;

	public int m_temperatureIdA;
	public int m_temperatureIdB;

	public int m_weatherId;

	public GD_Planet()
	{
		m_id = -1;
	}

	public void Initialize()
	{
		// make sure we don't go below mass of 6
		if ( m_mass < 6 )
		{
			m_mass = 6;
		}
	}

	// call this to get the surface of the planet
	public GD_Surface GetSurface()
	{
		var gameData = DataController.m_instance.m_gameData;

		return gameData.m_surfaceList[ m_surfaceId ];
	}

	// call this to find out if this is the Crystal Planet (planet 1 of 192,152 - the one planet of its own type)
	public bool IsCrystalPlanet()
	{
		var gameData = DataController.m_instance.m_gameData;

		// guard against a planet type that is not in the list
		if ( ( m_planetTypeId < 0 ) || ( m_planetTypeId >= gameData.m_planetTypeList.Length ) )
		{
			return false;
		}

		return gameData.m_planetTypeList[ m_planetTypeId ].m_name == c_crystalPlanetTypeName;
	}

	// the gravity above which a planet's gravity is crushing, and the gravity that is best for a colony (in hundredths of a G: the original's CAPTAIN overlay
	// WF1BD, 2.00 G, and 0.70 to 1.30 G)
	public const int c_crushingGravity = 200;
	const int c_lowestBestGravity = 70;
	const int c_highestBestGravity = 130;

	// the temperatures and the weather a colony can stand (the codes of the game data's lists, which are the original's: sub-arctic 0 to inferno 5, no weather 0
	// to very violent 4)
	const int c_lowestLivableTemperatureId = 2;
	const int c_highestLivableTemperatureId = 3;
	const int c_roughestLivableWeatherId = 2;
	const int c_veryViolentWeatherId = 4;

	// what a point of a colony's rating is worth: the original's score is 5 a point (WF348) and its bonus 1000 MU a point of the score (WF292)
	public const int c_colonyBonusPerPoint = 5000;

	// the bonus above which a colony is optimal: the original's score over 40 (OP-OV WEEE3)
	public const int c_optimalColonyBonus = 40000;

	// why this planet is unsuitable for colonization, in the words and the order of Interstel's evaluation (OP-OV (BAD-PLAN)), by the checks the original makes
	// when the captain logs the planet (CAPTAIN overlay WF1AB to WF219) - an empty list if none of them applies
	public System.Collections.Generic.List<string> GetUnsuitableReasons()
	{
		var reasons = new System.Collections.Generic.List<string>();

		// the whole temperature range too hot (WF1AB: its cold end above tropical), or too cold (WF199: its hot end below temperate)
		if ( m_temperatureIdA > c_highestLivableTemperatureId )
		{
			reasons.Add( "EXTREME HEAT" );
		}

		if ( m_temperatureIdB < c_lowestLivableTemperatureId )
		{
			reasons.Add( "EXTREME COLD" );
		}

		// WF1BD
		if ( m_gravity > c_crushingGravity )
		{
			reasons.Add( "CRUSHING GRAVITY" );
		}

		// WF1DF: weather rougher than moderate
		if ( m_weatherId > c_roughestLivableWeatherId )
		{
			reasons.Add( "VIOLENT STORMS" );
		}

		// WF1FB and WF219
		if ( !HasOxygen() )
		{
			reasons.Add( "LACK OF OXYGEN" );
		}

		if ( !HasWater() )
		{
			reasons.Add( "LACK OF WATER" );
		}

		return reasons;
	}

	// the points the original rates a planet with when the captain logs it (CAPTAIN overlay WF1AB to WF23F, added up in WF18D): 1 for each end of the temperature
	// range a colony can stand, 2 for a gravity from 0.70 to 1.30 G and 1 for any other, 2 for weather that is moderate at most and 1 for violent, 1 for life, and
	// for the minerals their density byte over 50, at least 1 for any minerals at all
	public int GetColonyPoints()
	{
		var points = 0;

		if ( m_temperatureIdA <= c_highestLivableTemperatureId )
		{
			points++;
		}

		if ( m_temperatureIdB >= c_lowestLivableTemperatureId )
		{
			points++;
		}

		points += ( ( m_gravity >= c_lowestBestGravity ) && ( m_gravity <= c_highestBestGravity ) ) ? 2 : 1;

		if ( m_weatherId < c_veryViolentWeatherId )
		{
			points++;
		}

		if ( m_weatherId <= c_roughestLivableWeatherId )
		{
			points++;
		}

		// (none for a planet a flare has sterilised: PlayerData.GetBioDensity)
		if ( PlayerData.GetBioDensityOf( this ) > 0 )
		{
			points++;
		}

		// the game data holds the densities in percent of the original's byte (byte * 100 / 255, rounded down): the smallest byte of the percentage stands for
		// it, which gives every planet that passes the checks the same points as any other byte of its percentage would
		var mineralByte = ( Mathf.Clamp( m_mineralDensity, 0, 100 ) * 255 + 99 ) / 100;

		points += Mathf.Max( ( mineralByte > 0 ) ? 1 : 0, mineralByte / 50 );

		return points;
	}

	// the bonus Interstel pays for a planet with none of the reasons above (the original's score times 1000 MU)
	public int GetColonyBonus()
	{
		return GetColonyPoints() * c_colonyBonusPerPoint;
	}

	// the gases of the atmosphere, none if it has no density
	bool HasGas( string name )
	{
		var gameData = DataController.m_instance.m_gameData;

		if ( ( m_atmosphereDensityId < 0 ) || ( m_atmosphereDensityId >= gameData.m_atmosphereDensityList.Length ) || !gameData.m_atmosphereDensityList[ m_atmosphereDensityId ].m_habitable )
		{
			return false;
		}

		foreach ( var atmosphereId in new[] { m_atmosphereIdA, m_atmosphereIdB, m_atmosphereIdC } )
		{
			if ( ( atmosphereId >= 0 ) && ( atmosphereId < gameData.m_atmosphereList.Length ) && ( gameData.m_atmosphereList[ atmosphereId ].m_name == name ) )
			{
				return true;
			}
		}

		return false;
	}

	// the hydrosphere's name
	string GetHydrosphereName()
	{
		var gameData = DataController.m_instance.m_gameData;

		return ( ( m_hydrosphereId >= 0 ) && ( m_hydrosphereId < gameData.m_hydrosphereList.Length ) ) ? gameData.m_hydrosphereList[ m_hydrosphereId ].m_name : "";
	}

	// oxygen a colony can breathe (WF1FB): an atmosphere of nitrogen and oxygen or of oxygen and carbon dioxide (the original's compounds 15 and 16; oxygen with
	// hydrogen does not count), or a hydrosphere of oxygen
	public bool HasOxygen()
	{
		return ( HasGas( "Oxygen" ) && ( HasGas( "Nitrogen" ) || HasGas( "Carbon Dioxide" ) ) ) || ( GetHydrosphereName() == "Oxygen" );
	}

	// water (WF219): a hydrosphere of water, or an atmosphere of oxygen and hydrogen (the original's compound 21)
	public bool HasWater()
	{
		return ( GetHydrosphereName() == "Water" ) || ( HasGas( "Oxygen" ) && HasGas( "Hydrogen" ) );
	}

	// call this to get the orbit angle (in degrees) of the planet
	public float GetOrbitAngle()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// calculate number of days per year for each planet based on orbit number - orbit 3 should = 366 days like earth
		var daysPerYear = 122 * m_orbitPosition;

		// update the orbit angle
		return ( ( playerData.m_general.m_gameTime + 1000.0f + ( m_starId * 4 ) ) / daysPerYear ) * 360.0f;
	}

	// call this to get the position of the planet (in world coordinates)
	public Vector3 GetPosition()
	{
		// get the current orbit angle and convert to radians
		var orbitAngle = GetOrbitAngle() * ( Mathf.PI / 180.0f );

		// calculate the distance of the planet from the sun
		var distance = Mathf.Lerp( 50.0f, 225.0f, ( m_orbitPosition - 1 ) / 7.0f ) * ( 8192.0f / 256.0f );

		// calculate the position of the planet
		return new Vector3( -Mathf.Sin( orbitAngle ) * distance, 0.0f, Mathf.Cos( orbitAngle ) * distance );
	}

	// get the scale for this planet (in world units)
	public Vector3 GetScale()
	{
		// scale the planet based on its mass
		var scale = Mathf.Lerp( 32.0f, 256.0f, Mathf.Sqrt( ( m_mass - 6.0f ) / 500000.0f ) );

		// return the scale as a vector
		return new Vector3( scale, scale, scale );
	}

	// whether or not this planet is a gas giant
	public bool IsGasGiant()
	{
		return ( m_surfaceId == 1 );
	}

	// whether or not this planet is molten
	public bool IsMolten()
	{
		return ( m_surfaceId == 4 );
	}

	// whether or not this planet has an atmosphere
	public bool HasAtmosphere()
	{
		return !IsGasGiant() && ( m_atmosphereDensityId != 0 );
	}

	// return the atmosphere density as a float
	public float GetAtmosphereDensity()
	{
		return m_atmosphereDensityId / 5.0f;
	}

	// return the blended atmosphere color of the planet
	public Color GetAtmosphereColor()
	{
		var gameData = DataController.m_instance.m_gameData;

		var atmosphereA = gameData.m_atmosphereList[ m_atmosphereIdA ];

		var color = Color.HSVToRGB( atmosphereA.m_hue / 360.0f, 1.0f, 1.0f );

		// Debug.Log( "Atmosphere A = " + atmosphereA.m_name + ", hue = " + atmosphereA.m_hue );

		if ( m_atmosphereIdB != -1 )
		{
			var atmosphereB = gameData.m_atmosphereList[ m_atmosphereIdB ];

			color += Color.HSVToRGB( atmosphereB.m_hue / 360.0f, 1.0f, 1.0f ) * 0.5f;

			// Debug.Log( "Atmosphere B = " + atmosphereB.m_name + ", hue = " + atmosphereB.m_hue );
		}

		if ( m_atmosphereIdC != -1 )
		{
			var atmosphereC = gameData.m_atmosphereList[ m_atmosphereIdC ];

			color += Color.HSVToRGB( atmosphereC.m_hue / 360.0f, 1.0f, 1.0f ) * 0.25f;

			// Debug.Log( "Atmosphere C = " + atmosphereC.m_name + ", hue = " + atmosphereC.m_hue );
		}

		Color.RGBToHSV( color, out var hue, out var saturation, out var value );

		var density = GetAtmosphereDensity();

		return Color.HSVToRGB( hue, 1.0f, density );
	}

	// return the blended cloud specular color of the planet
	public Color GetCloudSpecularColor()
	{
		var atmosphereColor = GetAtmosphereColor();

		Color.RGBToHSV( atmosphereColor, out var hue, out var saturation, out var value );

		value = Mathf.Max( 0.5f, value );

		return Color.HSVToRGB( hue, saturation, value );
	}

	// get the atmosphere text
	public string GetAtmosphereText()
	{
		var gameData = DataController.m_instance.m_gameData;

		var text = gameData.m_atmosphereList[ m_atmosphereIdA ].m_name;

		if ( m_atmosphereIdB != -1 )
		{
			text += ", " + gameData.m_atmosphereList[ m_atmosphereIdB ].m_name;
		}

		if ( m_atmosphereIdC != -1 )
		{
			text += ", " + gameData.m_atmosphereList[ m_atmosphereIdC ].m_name;
		}

		return text;
	}

	// get the hydrosphere text
	public string GetHydrosphereText()
	{
		var gameData = DataController.m_instance.m_gameData;

		return gameData.m_hydrosphereList[ m_hydrosphereId ].m_name;
	}

	// get the lithosphere text
	public string GetLithosphereText()
	{
		var gameData = DataController.m_instance.m_gameData;

		var text = gameData.m_elementList[ m_elementIdA ].m_name;
		text += ", " + gameData.m_elementList[ m_elementIdB ].m_name;
		text += ", " + gameData.m_elementList[ m_elementIdC ].m_name;

		return text;
	}

	// get the predominant surface text
	public string GetSurfaceText()
	{
		var gameData = DataController.m_instance.m_gameData;

		return gameData.m_surfaceList[ m_surfaceId ].m_name;
	}

	// the original's words for the land Look describes (PLSET-OV (TERRAIN picks them when the ship lands, MAPS-OV .SURFACE prints them): the colours of frozen
	// land (CLIST1), of oceans (CLIST2) and of rock (CLIST3), the sands (SAND), and the region temperatures (the REGIONS file, coldest first)
	static readonly string[] c_frozenColours = { "white", "blue tinted", "green tinted", "red tinted", "yellow tinted" };
	static readonly string[] c_oceanColours = { "black", "dark blue", "turquoise", "green", "muddy brown" };
	static readonly string[] c_rockColours = { "grey", "yellowish", "reddish", "brownish", "black" };
	static readonly string[] c_sands = { "rocky sand", "pebbly sand", "coarse sand", "powdery sand" };
	static readonly string[] c_regionTemperatures = { "Subfreezing", "Arctic", "Temperate", "Tropical", "Searing", "Furnace" };

	// the land as Look describes it (PLSET-OV (TERRAIN and MAPS-OV .SURFACE): by the surface, with colours the original draws when the ship lands - the port draws
	// them from the planet's id, so a planet looks the same on every visit
	public string GetLandText()
	{
		var random = new System.Random( m_id * 31 + 7 );
		var hasAtmosphere = m_atmosphereIdA >= 0;
		string text;

		switch ( m_surfaceId )
		{
			// a world of water: its oceans' colour, and the land as rock (WF40E, .LIQUID)
			case 2:
				text = c_oceanColours[ random.Next( 0, 5 ) ] + " oceans and " + GetRockText( random, hasAtmosphere );
				break;

			// frozen (WF389, .ICE)
			case 3:
				text = "cracked plains and hills of " + c_frozenColours[ random.Next( 0, 5 ) ] + " snow and ice";
				break;

			// molten (WF41C, .MOLTEN)
			case 4:
				text = "volcanic rock, steaming vents, boiling pools and flowing lava";
				break;

			// rock (WF3BA, .ROCK)
			case 5:
				text = GetRockText( random, hasAtmosphere );
				break;

			// a gas giant has no land to describe (nobody lands there)
			case 1:
				return GetSurfaceText();

			// crystal (WF434, .CRYSTAL)
			default:
				text = "smooth and featureless";
				break;
		}

		return char.ToUpper( text[ 0 ] ) + text.Substring( 1 );
	}

	// rock under an atmosphere is coloured, rock and dirt or, three times in ten, one of the sands; without one it is cratered rock and dust (WF3BA)
	static string GetRockText( System.Random random, bool hasAtmosphere )
	{
		if ( !hasAtmosphere )
		{
			return "cratered rock and dust";
		}

		var colour = c_rockColours[ random.Next( 0, 5 ) ];
		var phrase = ( random.Next( 1, 100 ) < 31 ) ? c_sands[ random.Next( 0, 4 ) ] : "rock and dirt";

		return colour + " " + phrase;
	}

	// the temperature of the region at this latitude (MAPS-OV WEDB2, WED78): the planet's coldest at the poles to its hottest at the equator, in as many bands as it
	// has temperatures, counted on the original's map of 960 rows from pole to pole folded at the equator
	public string GetRegionTemperatureText( float latitude )
	{
		var coldest = Mathf.Clamp( m_temperatureIdA, 0, c_regionTemperatures.Length - 1 );
		var hottest = Mathf.Clamp( m_temperatureIdB, coldest, c_regionTemperatures.Length - 1 );
		var regions = hottest - coldest + 1;
		var rowsFromPole = Mathf.Clamp( Mathf.RoundToInt( ( 90.0f - Mathf.Abs( latitude ) ) * 480.0f / 90.0f ), 0, 480 );
		var region = coldest + rowsFromPole / ( 480 / regions + 1 );

		return c_regionTemperatures[ Mathf.Clamp( region, coldest, hottest ) ];
	}

	// get the gravity text
	public string GetGravityText()
	{
		// gravity is in hundredths of a g, so the fraction always needs two digits (105 is 1.05 G, not 1.5 G)
		return ( m_gravity / 100 ) + "." + ( m_gravity % 100 ).ToString( "D2" ) + " G";
	}

	// get the atmospheric density text
	public string GetAtmosphericDensityText()
	{
		var gameData = DataController.m_instance.m_gameData;

		return gameData.m_atmosphereDensityList[ m_atmosphereDensityId ].m_name;
	}

	// get the temperature text: the range, or the one word when both ends are the same (the original drops the second, ANALYZE-OV WF068)
	public string GetTemperatureText()
	{
		var gameData = DataController.m_instance.m_gameData;

		if ( m_temperatureIdA == m_temperatureIdB )
		{
			return gameData.m_temperatureList[ m_temperatureIdA ].m_name;
		}

		return gameData.m_temperatureList[ m_temperatureIdA ].m_name + " to " + gameData.m_temperatureList[ m_temperatureIdB ].m_name;
	}

	// get the global weather text
	public string GetGlobalWeatherText()
	{
		var gameData = DataController.m_instance.m_gameData;

		return gameData.m_weatherList[ m_weatherId ].m_name;
	}
}
