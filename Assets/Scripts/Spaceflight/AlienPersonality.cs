// how a race behaves in conversation, after the conversation module of the shipped game (COMM-OV) and of the 1984 design notes (Research/Notes/compart.txt,
// sections 4 to 7): the alien's "emotional disposition level" - its mood, from 0 (most calm) to 100 (most agitated), which the original keeps as EDL - is divided
// into postures by three thresholds, and each of the player's postures moves it by an effect of its own - the numbers are the shipped game's, from each race's
// record in the original's STARB.COM (its ORIGINATOR: BASE-EDL, AET, BET, CET, OPE, FPE, HPE; DevTools/OriginalRecords), by the owner's ruling of 2026-10-12;
// the strengths that decide between hostile and obsequious are the shipped game's too (Encounter.GetStrengthDifferential)
public class AlienPersonality
{
	// below this mood the aliens are friendly, below the next diplomatic, below the last hostile or obsequious (by their strength against the player's), and from
	// the last one on too agitated to talk ("fight or flight")
	public float m_friendlyBelow;
	public float m_hostileFrom;
	public float m_fightFrom;

	// the mood at the start of an encounter
	public float m_startingMood;

	// the "player posture effect" of a statement in each of the player's postures (a question has half of it, and every further phrase in the same posture less)
	public float m_friendlyEffect;
	public float m_hostileEffect;
	public float m_obsequiousEffect;

	// the Elowan: their hostile band is empty (BET = CET = 60), so they go from talking straight to fight or flight; friendship calms them, hostility angers them
	// (EDL 44, AET 25, BET 60, CET 60, OPE 0, FPE -10, HPE 20)
	static readonly AlienPersonality s_elowan = new AlienPersonality
	{
		m_friendlyBelow = 25.0f, m_hostileFrom = 60.0f, m_fightFrom = 60.0f, m_startingMood = 44.0f,
		m_friendlyEffect = -10.0f, m_hostileEffect = 20.0f, m_obsequiousEffect = 0.0f
	};

	// the Thrynn: calmed a little by friendship, angered by hostility and a little by grovelling (EDL 44, AET 30, BET 60, CET 80, OPE 5, FPE -8, HPE 20)
	static readonly AlienPersonality s_thrynn = new AlienPersonality
	{
		m_friendlyBelow = 30.0f, m_hostileFrom = 60.0f, m_fightFrom = 80.0f, m_startingMood = 44.0f,
		m_friendlyEffect = -8.0f, m_hostileEffect = 20.0f, m_obsequiousEffect = 5.0f
	};

	// the Spemin: never friendly (AET 0), they start neutral and turn hostile from 20, which a ship that meets them with its shields up and its weapons armed
	// already brings; praise calms them, hostility angers them a lot (EDL 10, AET 0, BET 20, CET 80, OPE -8, FPE 0, HPE 30)
	static readonly AlienPersonality s_spemin = new AlienPersonality
	{
		m_friendlyBelow = 0.0f, m_hostileFrom = 20.0f, m_fightFrom = 80.0f, m_startingMood = 10.0f,
		m_friendlyEffect = 0.0f, m_hostileEffect = 30.0f, m_obsequiousEffect = -8.0f
	};

	// the Veloxi: calmed by grovelling, angered by hostility (EDL 44, AET 20, BET 60, CET 80, OPE -8, FPE 0, HPE 20)
	static readonly AlienPersonality s_velox = new AlienPersonality
	{
		m_friendlyBelow = 20.0f, m_hostileFrom = 60.0f, m_fightFrom = 80.0f, m_startingMood = 44.0f,
		m_friendlyEffect = 0.0f, m_hostileEffect = 20.0f, m_obsequiousEffect = -8.0f
	};

	// the Mechans: neutral from 10, hostile from 75 and fighting from 99, and the player's postures do not move them (EDL 48, AET 10, BET 75, CET 99, OPE, FPE and HPE
	// 0) - the answers to their questions do (each right one takes 10), and once they are friendly they are friendly for good (Encounter.UnlockMechans)
	static readonly AlienPersonality s_mechan = new AlienPersonality
	{
		m_friendlyBelow = 10.0f, m_hostileFrom = 75.0f, m_fightFrom = 99.0f, m_startingMood = 48.0f,
		m_friendlyEffect = 0.0f, m_hostileEffect = 0.0f, m_obsequiousEffect = 0.0f
	};

	// the Gazurtoid: never friendly nor diplomatic (AET = BET = 0), they talk abuse from the start and fight from 40, which a ship that meets them with its shields
	// up and its weapons armed already brings; grovelling calms them a lot, hostility angers them a lot (EDL 26, AET 0, BET 0, CET 40, OPE -20, FPE 0, HPE 30)
	static readonly AlienPersonality s_gazurtoid = new AlienPersonality
	{
		m_friendlyBelow = 0.0f, m_hostileFrom = 0.0f, m_fightFrom = 40.0f, m_startingMood = 26.0f,
		m_friendlyEffect = 0.0f, m_hostileEffect = 30.0f, m_obsequiousEffect = -20.0f
	};

	// the races whose shipped starting posture is friendly: their mood (BASE-EDL) is below every threshold (the Mysterions 1 under 99, the Nomad probes 1 under
	// 100, the Veloxi drones 1 under 98, the Minstrels 0 under 99, the Enterprise 48 under 99, the Noah 9 derelict 50 under 101) - the others start neutral, as
	// they did (the Mechans are neutral in the shipped game too; the Gazurtoids' hostile posture is talk, which needs a mood of their own; the Uhlek fight)
	public static bool StartsFriendly( GameData.Race race )
	{
		switch ( race )
		{
			case GameData.Race.Mysterion:
			case GameData.Race.NomadProbe:
			case GameData.Race.VeloxProbe:
			case GameData.Race.Minstrel:
			case GameData.Race.TheEnterprise:
			case GameData.Race.NoahDerelict:
				return true;
		}

		return false;
	}

	// what the player's ship adds to the aliens' mood when they meet: 5 with its shields up and 10 with its weapons armed (COMM-OV, INIT_ACOMM)
	public static float GetMeetingMood( PD_PlayerShip ship )
	{
		return ( ship.m_shieldsAreUp ? 5.0f : 0.0f ) + ( ship.m_weaponsAreArmed ? 10.0f : 0.0f );
	}

	// the personality of a race - null for a race whose stance is set by rules of its own (the Mechans, the drones, the Uhlek) or that has lines in one stance only
	public static AlienPersonality Find( GameData.Race race )
	{
		switch ( race )
		{
			case GameData.Race.Elowan:
				return s_elowan;

			case GameData.Race.Thrynn:
				return s_thrynn;

			case GameData.Race.Spemin:
				return s_spemin;

			case GameData.Race.Velox:
				return s_velox;

			case GameData.Race.Mechan:
				return s_mechan;

			case GameData.Race.Gazurtoid:
				return s_gazurtoid;
		}

		return null;
	}

	// the effect of a statement in this posture of the player's (none for a posture the player cannot take)
	public float GetPostureEffect( GD_Comm.Stance playerPosture )
	{
		switch ( playerPosture )
		{
			case GD_Comm.Stance.Friendly:
				return m_friendlyEffect;

			case GD_Comm.Stance.Hostile:
				return m_hostileEffect;

			case GD_Comm.Stance.Obsequious:
				return m_obsequiousEffect;
		}

		return 0.0f;
	}
}
