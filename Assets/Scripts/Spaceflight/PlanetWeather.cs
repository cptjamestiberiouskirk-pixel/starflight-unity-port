using UnityEngine;

// the weather the terrain vehicle meets on a planet's surface, as the original's (BEHAV-OV ?STORM, MOVE-OV WEA6E, STORM-OV (DO.STORM): a planet with no atmosphere
// has none; otherwise, every star hour, a spell of weather counts down, and when it is over a new one is rolled from the planet's weather activity (its byte 0x11,
// the port's m_weatherId: 0 none, 1 calm to 4 very violent): a storm with a chance of 0, 20, 40, 60 or 85 in 99 (%STORM against RRND(1,100)), picked by the
// planet's surface from the STORMS table, else a calm sky (hazy, clear or overcast), either lasting 1 to (5 - activity) * 3 - 1 hours (at most 12). In an
// electrical storm, a thunder storm, ground shaking, hail or a windstorm (codes 6 to 10) each kilometre costs the vehicle twice the fuel (%EFF 50) and each hour
// may injure the crew: 29 times in 99 while the vehicle stands, a third of the time while it moves, and once more as the storm begins; snow, mist, fog and rain
// cost a third more (%EFF 75); from code 4 to 11 the bearing to the ship is lost unless the navigator's skill is above 199
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

	// the original's weather codes (#STORM, MAPS-OV WPHRASE: 0 clear, 1 hazy, 2 clear, 3 overcast, 4 snowing lightly, 5 misty, 6 electrical storm, 7 thunder storm,
	// 8 ground shaking, 9 hailing, 10 windstorm, 11 snowing heavily, 12 foggy, 13 raining) and the port's sky for each
	static readonly Sky[] c_codeSkies = { Sky.Clear, Sky.Hazy, Sky.Clear, Sky.Overcast, Sky.SnowingLightly, Sky.Misty, Sky.ElectricalStorm, Sky.ThunderStorm, Sky.GroundShaking, Sky.Hailing, Sky.Windstorm, Sky.SnowingHeavily, Sky.Foggy, Sky.Raining };

	// the chance of a storm in 99 by weather activity (BEHAV-OV %STORM: 0, 20, 40, 60, 85 against RRND(1,100))
	static readonly int[] c_stormChance = { 0, 20, 40, 60, 85 };

	// the storms of each surface and how many of the 99 rolls give each (STORM-OV STORMS, by the planet's surface byte: 2 liquid, 3 frozen, 4 molten, 5 rock;
	// a gas giant has none)
	static readonly int[][] c_liquidStorms = { new[] { 6, 16 }, new[] { 7, 15 }, new[] { 10, 15 }, new[] { 12, 15 }, new[] { 13, 38 } };
	static readonly int[][] c_frozenStorms = { new[] { 6, 16 }, new[] { 7, 15 }, new[] { 11, 15 }, new[] { 9, 15 }, new[] { 10, 38 } };
	static readonly int[][] c_moltenStorms = { new[] { 6, 26 }, new[] { 7, 25 }, new[] { 8, 48 } };
	static readonly int[][] c_rockStorms = { new[] { 6, 33 }, new[] { 7, 33 }, new[] { 10, 33 } };

	// an injury in a storm hour: 29 rolls of the 99 under 30 while the vehicle stands (BEHAV-OV WE91A, and STORM-OV WF2B2 as a storm begins), one in three while
	// it moves (MOVE-OV WEA1D)
	const int c_injuryRollsOf99 = 29;

	// a Human's durability, for crew members of no known race
	const int c_humanDurability = 6;

	// the weather code now (#STORM), the star hours its spell has left (STORM), and the game hour it was last moved on for (-1 while the terrain vehicle is not out)
	static int s_code;
	static int s_hoursLeft;
	static int s_rolledHour = -1;

	// the terrain vehicle goes out: the first sky is rolled and shown, alone in the text window, as the original's first line on the surface ("IT IS CLEAR.",
	// starflt_179: the lines of the disembarking are gone by then)
	public static void Begin()
	{
		s_rolledHour = GetGameHour();
		s_code = 0;
		s_hoursLeft = 0;

		Hour( false );

		if ( SpaceflightController.m_instance != null )
		{
			SpaceflightController.m_instance.m_messages.Clear();
		}

		Show();
	}

	// the terrain vehicle is back in the ship: no more weather
	public static void End()
	{
		s_rolledHour = -1;
		s_code = 0;
		s_hoursLeft = 0;
	}

	// call this every frame while the terrain vehicle is out: every star hour the weather moves on, and a storm may injure the crew
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

	// one star hour of weather (Update calls it; the probe too)
	public static void PassHour()
	{
		if ( ( DataController.m_instance.m_playerData.m_general.m_location != PD_General.Location.Disembarked ) || ( s_rolledHour < 0 ) )
		{
			return;
		}

		if ( Hour( true ) )
		{
			Show();
		}
	}

	// the sky now, in the original's words
	public static string GetSkyText()
	{
		return c_skyTexts[ (int) GetSky() ];
	}

	public static Sky GetSky()
	{
		return c_codeSkies[ Mathf.Clamp( s_code, 0, c_codeSkies.Length - 1 ) ];
	}

	// the original's weather code now (#STORM: for the probe)
	public static int GetCode()
	{
		return s_code;
	}

	// the star hours the spell of weather now has left (STORM: for the probe)
	public static int GetHoursLeft()
	{
		return s_hoursLeft;
	}

	// true in an electrical storm, a thunder storm, ground shaking, hail or a windstorm (codes 6 to 10, while the terrain vehicle is out): the weather that
	// injures and halves the efficiency
	public static bool IsStorm()
	{
		return ( s_rolledHour >= 0 ) && ( s_code >= 6 ) && ( s_code <= 10 );
	}

	// true when the bearing to the ship is lost: codes 4 to 11, unless the navigator's skill is above 199 (the original's LOST! on the vehicle's display)
	public static bool IsShipLost()
	{
		return ( s_rolledHour >= 0 ) && ( s_code >= 4 ) && ( s_code <= 11 ) && ( CombatController.GetNavigatorSkill() <= 199 );
	}

	// the weather's share of the vehicle's efficiency (%EFF over 100: STORM-OV WF42E): 1 for a calm sky, 0.5 in a storm, 0.75 in snow, mist, fog or rain
	public static float GetEfficiency()
	{
		if ( ( s_rolledHour < 0 ) || ( s_code <= 3 ) )
		{
			return 1.0f;
		}

		return ( s_code <= 10 ) && ( s_code >= 6 ) ? 0.5f : 0.75f;
	}

	// the game hour as one number (days and hours)
	static int GetGameHour()
	{
		var general = DataController.m_instance.m_playerData.m_general;

		return general.m_day * 24 + general.m_hour;
	}

	// one star hour of the original's ?STORM: the spell counts down, or a new one is rolled, and a storm may injure the crew; returns true when the sky is to be
	// shown again, as the original shows it: every hour of a spell of snow, mist, fog, rain or a storm, a new calm sky, and a new storm that is not the one before
	static bool Hour( bool injure )
	{
		var gameData = DataController.m_instance.m_gameData;
		var planetId = DataController.m_instance.m_playerData.m_general.m_currentPlanetId;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) )
		{
			s_code = 0;
			s_hoursLeft = 0;

			return false;
		}

		var planet = gameData.m_planetList[ planetId ];

		// no atmosphere, no weather
		if ( planet.m_atmosphereDensityId == 0 )
		{
			s_code = 0;
			s_hoursLeft = 0;

			return false;
		}

		var activity = Mathf.Clamp( planet.m_weatherId, 0, c_stormChance.Length - 1 );

		var show = false;

		if ( s_hoursLeft > 0 )
		{
			// the spell goes on (shown again while it has hours left, unless the sky is calm: ?.STORM)
			s_hoursLeft--;

			show = ( s_code > 3 ) && ( s_hoursLeft > 0 );
		}
		else if ( c_stormChance[ activity ] > Random.Range( 1, 100 ) )
		{
			// a storm
			var codeBefore = s_code;

			s_hoursLeft = SpellLength( activity );
			s_code = PickStorm( planet.m_surfaceId );

			show = ( s_code != codeBefore );

			// (the original's FORCED tick: a storm may injure as it begins)
			if ( injure && IsStorm() && ( Random.Range( 1, 100 ) <= c_injuryRollsOf99 ) )
			{
				InjureInTheStorm();
			}
		}
		else
		{
			// a calm sky: hazy, clear or overcast
			s_code = Random.Range( 1, 4 );
			s_hoursLeft = SpellLength( activity );

			show = true;
		}

		// the hour's injury in a storm: 29 times in 99 while the vehicle stands, one in three while it moves
		if ( injure && IsStorm() )
		{
			var moving = DataController.m_instance.m_playerData.m_general.m_currentSpeed >= 0.1f;
			var injured = moving ? ( Random.Range( 0, 3 ) == 0 ) : ( Random.Range( 1, 100 ) <= c_injuryRollsOf99 );

			if ( injured )
			{
				InjureInTheStorm();
			}
		}

		return show;
	}

	// how many star hours a spell of weather lasts: RRND(1, (5 - activity) * 3), at most 12
	static int SpellLength( int activity )
	{
		return Mathf.Clamp( Random.Range( 1, Mathf.Max( 2, ( 5 - activity ) * 3 ) ), 0, 12 );
	}

	// a storm of the planet's surface, by the original's STORMS table (a roll of 1 to 99 against its shares)
	static int PickStorm( int surfaceId )
	{
		int[][] storms;

		switch ( surfaceId )
		{
			case 2: storms = c_liquidStorms; break;
			case 3: storms = c_frozenStorms; break;
			case 4: storms = c_moltenStorms; break;
			case 5: storms = c_rockStorms; break;
			default: return 0;
		}

		var roll = Random.Range( 1, 100 );

		foreach ( var storm in storms )
		{
			roll -= storm[ 1 ];

			if ( roll <= 0 )
			{
				return storm[ 0 ];
			}
		}

		return storms[ storms.Length - 1 ][ 0 ];
	}

	// an injury in a storm: the original's INJURE-PL with the E/M of the ground the vehicle is on
	static void InjureInTheStorm()
	{
		var energyPerStep = 24.0f;

		if ( ( SpaceflightController.m_instance != null ) && ( SpaceflightController.m_instance.m_terrainVehicle != null ) )
		{
			energyPerStep = SpaceflightController.m_instance.m_terrainVehicle.GetEnergyPerStep();
		}

		InjureOnTheSurface( energyPerStep, GetEfficiency() );
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

	// the original's injury on a planet's surface (STORM-OV INJURE-PL): one of the six posts at random (WF298), and its crew member loses the larger of 1 and a
	// roll (1 to 3 times the ground's E/M, 2 to 30, less one: WF2DA) over the race's durability, times the larger of 1 and 3 times E/M over the weather's %EFF,
	// so harder over high ground and a liquid and in bad weather - "ALPHA IS INJURED." (at 0 the crew member dies: PD_CrewAssignment.InjureCrewMember)
	// - gives back who was injured (null if nobody: the post is empty or its crew member is dead), and says so unless told not to
	public static PD_Personnel.PD_PersonnelFile InjureOnTheSurface( float energyPerStep, float efficiency, bool announce = true )
	{
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var role = (PD_CrewAssignment.Role) Random.Range( (int) PD_CrewAssignment.Role.First, (int) PD_CrewAssignment.Role.Count );

		if ( !playerData.m_crewAssignment.IsAssigned( role ) )
		{
			return null;
		}

		var victim = playerData.m_crewAssignment.GetPersonnelFile( role );

		if ( ( victim == null ) || victim.m_isDead || ( victim.m_vitality <= 0.0f ) )
		{
			return null;
		}

		var durability = c_humanDurability;

		if ( ( victim.m_crewRaceId >= 0 ) && ( victim.m_crewRaceId < gameData.m_crewRaceList.Length ) )
		{
			durability = Mathf.Max( 1, gameData.m_crewRaceList[ victim.m_crewRaceId ].m_durability );
		}

		var energy = Mathf.Abs( Mathf.RoundToInt( energyPerStep ) );
		var roll = Random.Range( 1, Mathf.Clamp( energy * 3, 2, 30 ) );
		var percentEfficiency = Mathf.Max( 1, Mathf.RoundToInt( efficiency * 100.0f ) );
		var loss = Mathf.Max( 1, roll / durability ) * Mathf.Max( 1, energy * 3 / percentEfficiency );

		if ( announce && ( SpaceflightController.m_instance != null ) )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + victim.m_name + " is injured.</color>" );
		}

		playerData.m_crewAssignment.InjureCrewMember( victim, loss );

		return victim;
	}
}
