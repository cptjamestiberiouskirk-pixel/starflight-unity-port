using UnityEngine;

// the weather the terrain vehicle meets on a planet's surface, as the original game has it (observed in the GOG copy of Starflight 1, 2026-10-11, on planets 90
// and 91 of Arth's system; the phrases are the shipped game's, STARB.COM): every game hour the sky is rolled again from the planet's weather and shown in the
// text window, with no message of its own when a storm begins or ends - in a storm (a windstorm, hail, a thunder storm or an electrical storm) the vehicle's
// efficiency is halved (EFF 14% to 7%), the bearing to the ship reads "LOST!", and in about half of the storm hours a crew member is injured by 10 to 50 points
// of vitality (a Human, of durability 6), which can kill; heavy snow costs some efficiency (EFF 11%) and also loses the bearing, but injures no one; in the ship
// nothing happens - the original picks its phrases from a probability table in STARB.COM (STORMS) whose keys were not decoded, so the port picks them by the
// planet's surface, with the share of storm hours rising with its weather class from the 12% seen on planet 90 (None) to the half seen on planet 91 (Very
// Violent); the pools for the surfaces not seen, the shares of the classes between and the chances within a storm hour are the port's
public static class PlanetWeather
{
	// the original's phrases, in its order
	public enum Sky
	{
		RainingHeavily,
		Overcast,
		SnowingLightly,
		Misty,
		ElectricalStorm,
		GroundShaking,
		SnowingHeavily,
		Hailing,
		ThunderStorm,
		Windstorm,
		Foggy,
		Hazy,
		Raining,
		Clear
	}

	static readonly string[] c_skyTexts = { "It is raining heavily.", "It is overcast.", "It is snowing lightly.", "It is misty.", "You are in an electrical storm.", "The ground is shaking and rumbling.", "It is snowing heavily.", "It is hailing.", "You are in a thunder storm.", "You are in a windstorm.", "It is foggy.", "It is hazy.", "It is raining.", "It is clear." };

	// the share of the hours that are storms, by weather class (None, Calm, Moderate, Violent, Very Violent): 12% and 50% as observed, the others between
	static readonly float[] c_stormShare = { 0.12f, 0.2f, 0.3f, 0.4f, 0.5f };

	// on a frozen planet, the share of the hours with heavy snow, for each storm hour (4 heavy snow hours to 9 storm hours on planet 91)
	const float c_heavySnowPerStorm = 0.45f;

	// the vehicle's efficiency in a storm and in heavy snow (EFF 7% and 11% of the 14% of a clear sky)
	public const float c_stormEfficiency = 0.5f;
	public const float c_heavySnowEfficiency = 11.0f / 14.0f;

	// a storm hour injures nobody half of the time, one crew member a third of it, and two the rest (4 of 10 and 4 of 7 storm hours injured, sometimes twice)
	const float c_noInjuryChance = 0.5f;
	const float c_oneInjuryChance = 0.85f;

	// an injury takes 10 to 50 points of vitality (by tens, as seen) from a Human, and as much more or less as the crew member's race is weaker or tougher
	const int c_humanDurability = 6;

	// the sky now, and the game hour it was rolled for (-1 while the terrain vehicle is not out)
	static Sky s_sky = Sky.Clear;
	static int s_rolledHour = -1;

	// the terrain vehicle goes out: the first sky is rolled and shown
	public static void Begin()
	{
		s_rolledHour = GetGameHour();

		Roll();

		Show();
	}

	// the terrain vehicle is back in the ship: no more weather
	public static void End()
	{
		s_rolledHour = -1;
		s_sky = Sky.Clear;
	}

	// call this every frame while the terrain vehicle is out: every game hour the sky is rolled again, shown when it has changed, and a storm may injure the crew
	public static void Update()
	{
		if ( s_rolledHour < 0 )
		{
			return;
		}

		var hour = GetGameHour();

		if ( hour == s_rolledHour )
		{
			return;
		}

		s_rolledHour = hour;

		PassHour();
	}

	// one game hour of weather (Update calls it; the probe too)
	public static void PassHour()
	{
		if ( ( DataController.m_instance.m_playerData.m_general.m_location != PD_General.Location.Disembarked ) || ( s_rolledHour < 0 ) )
		{
			return;
		}

		var skyBefore = s_sky;

		Roll();

		if ( s_sky != skyBefore )
		{
			Show();
		}

		if ( IsStorm() )
		{
			InjureCrew();
		}
	}

	// the sky now, in the original's words
	public static string GetSkyText()
	{
		return c_skyTexts[ (int) s_sky ];
	}

	public static Sky GetSky()
	{
		return s_sky;
	}

	// true in a windstorm, hail, a thunder storm or an electrical storm (while the terrain vehicle is out)
	public static bool IsStorm()
	{
		return ( s_rolledHour >= 0 ) && ( ( s_sky == Sky.Windstorm ) || ( s_sky == Sky.Hailing ) || ( s_sky == Sky.ThunderStorm ) || ( s_sky == Sky.ElectricalStorm ) );
	}

	// true when the bearing to the ship is lost (a storm or heavy snow)
	public static bool IsShipLost()
	{
		return IsStorm() || ( ( s_rolledHour >= 0 ) && ( s_sky == Sky.SnowingHeavily ) );
	}

	// how much of its speed (and of its fuel's worth) the weather leaves the terrain vehicle
	public static float GetEfficiency()
	{
		if ( IsStorm() )
		{
			return c_stormEfficiency;
		}

		return ( ( s_rolledHour >= 0 ) && ( s_sky == Sky.SnowingHeavily ) ) ? c_heavySnowEfficiency : 1.0f;
	}

	// the game hour as one number (days and hours)
	static int GetGameHour()
	{
		var general = DataController.m_instance.m_playerData.m_general;

		return general.m_day * 24 + general.m_hour;
	}

	// rolls the sky for the current planet
	static void Roll()
	{
		var gameData = DataController.m_instance.m_gameData;
		var planetId = DataController.m_instance.m_playerData.m_general.m_currentPlanetId;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) )
		{
			s_sky = Sky.Clear;

			return;
		}

		var planet = gameData.m_planetList[ planetId ];
		var stormShare = c_stormShare[ Mathf.Clamp( planet.m_weatherId, 0, c_stormShare.Length - 1 ) ];
		var frozen = planet.m_surfaceId == 3;
		var molten = planet.m_surfaceId == 4;
		var heavySnowShare = frozen ? stormShare * c_heavySnowPerStorm : 0.0f;

		var roll = Random.value;

		if ( roll < stormShare )
		{
			// a storm: on a molten planet electrical storms and windstorms, elsewhere any of the four
			s_sky = molten ? Pick( Sky.ElectricalStorm, Sky.Windstorm ) : Pick( Sky.Windstorm, Sky.Hailing, Sky.ThunderStorm, Sky.ElectricalStorm );
		}
		else if ( roll < stormShare + heavySnowShare )
		{
			s_sky = Sky.SnowingHeavily;
		}
		else if ( frozen )
		{
			s_sky = Pick( Sky.Hazy, Sky.Overcast, Sky.Clear, Sky.SnowingLightly );
		}
		else if ( molten )
		{
			s_sky = Pick( Sky.Hazy, Sky.Clear, Sky.GroundShaking );
		}
		else if ( planet.m_surfaceId == 2 )
		{
			s_sky = Pick( Sky.Raining, Sky.RainingHeavily, Sky.Misty, Sky.Foggy, Sky.Overcast, Sky.Hazy, Sky.Clear );
		}
		else
		{
			s_sky = Pick( Sky.Hazy, Sky.Overcast, Sky.Clear, Sky.Raining, Sky.Misty );
		}
	}

	static Sky Pick( params Sky[] skies )
	{
		return skies[ Random.Range( 0, skies.Length ) ];
	}

	// shows the sky in the text window
	static void Show()
	{
		if ( SpaceflightController.m_instance == null )
		{
			return;
		}

		SpaceflightController.m_instance.m_messages.AddText( ( IsStorm() ? "<color=yellow>" : "<color=white>" ) + GetSkyText() + "</color>" );
	}

	// a storm hour may injure one or two of the crew in the terrain vehicle ("ALPHA IS INJURED.")
	static void InjureCrew()
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;

		var roll = Random.value;
		var injuries = ( roll < c_noInjuryChance ) ? 0 : ( ( roll < c_oneInjuryChance ) ? 1 : 2 );

		for ( var i = 0; i < injuries; i++ )
		{
			// the crew on board who are alive
			var crew = new System.Collections.Generic.List<PD_Personnel.PD_PersonnelFile>();

			for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
			{
				if ( !playerData.m_crewAssignment.IsAssigned( role ) )
				{
					continue;
				}

				var file = playerData.m_crewAssignment.GetPersonnelFile( role );

				if ( ( file != null ) && !file.m_isDead && ( file.m_vitality > 0.0f ) && !crew.Contains( file ) )
				{
					crew.Add( file );
				}
			}

			if ( crew.Count == 0 )
			{
				return;
			}

			var victim = crew[ Random.Range( 0, crew.Count ) ];
			var durability = c_humanDurability;

			if ( ( victim.m_crewRaceId >= 0 ) && ( victim.m_crewRaceId < gameData.m_crewRaceList.Length ) )
			{
				durability = Mathf.Max( 1, gameData.m_crewRaceList[ victim.m_crewRaceId ].m_durability );
			}

			var damage = Random.Range( 1, 6 ) * 10.0f * c_humanDurability / durability;

			if ( SpaceflightController.m_instance != null )
			{
				SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + victim.m_name + " is injured.</color>" );
			}

			// (at 0 the crew member dies, and with nobody left the game is lost: PD_CrewAssignment.InjureCrewMember)
			playerData.m_crewAssignment.InjureCrewMember( victim, damage );
		}
	}
}
