// how a race behaves in conversation, after the conversation module of the 1984 design notes (Research/Notes/compart.txt, sections 4 to 7): the alien's
// "emotional disposition level" - its mood, from 0 (most calm) to 100 (most agitated), which the original's kernel keeps as EDL - is divided into postures by three
// thresholds, each of the player's postures moves it by an effect of its own, and the race's aggression weighs its strength against the player's - the notes give
// the model and none of the numbers, so the numbers here are the port's, read from each race's character in STRINFO (the owner's ruling of 2026-10-11 for roadmap
// item 2.1)
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

	// the aggression level (the notes weigh the alien's strength by aggression / 5)
	public float m_aggression;

	// the Elowan "bear thee no ill will", ask "commit neither violence nor trespass against us", and are "wise in thy peaceful dealings" with a friendly crew: slow
	// to anger, calmed by friendship, and little aggression
	static readonly AlienPersonality s_elowan = new AlienPersonality
	{
		m_friendlyBelow = 40.0f, m_hostileFrom = 75.0f, m_fightFrom = 95.0f, m_startingMood = 45.0f,
		m_friendlyEffect = -10.0f, m_hostileEffect = 15.0f, m_obsequiousEffect = 5.0f, m_aggression = 2.0f
	};

	// the Thrynn are "practical, realistic people" who "cannot stand races who cower and debase themselves": calmed a little by friendship, angered by hostility
	// and more by grovelling
	static readonly AlienPersonality s_thrynn = new AlienPersonality
	{
		m_friendlyBelow = 30.0f, m_hostileFrom = 65.0f, m_fightFrom = 90.0f, m_startingMood = 45.0f,
		m_friendlyEffect = -8.0f, m_hostileEffect = 12.0f, m_obsequiousEffect = 15.0f, m_aggression = 5.0f
	};

	// the Spemin bully ("you will obey us or you will cease to exist") and grovel to the strong ("so, you wish to praise us. This is acceptable"): they start
	// between hostile and obsequious, praise calms them, and little aggression (they "act cowardly")
	static readonly AlienPersonality s_spemin = new AlienPersonality
	{
		m_friendlyBelow = 15.0f, m_hostileFrom = 40.0f, m_fightFrom = 90.0f, m_startingMood = 55.0f,
		m_friendlyEffect = 0.0f, m_hostileEffect = 10.0f, m_obsequiousEffect = -10.0f, m_aggression = 3.0f
	};

	// the Veloxi are "arrogant" and want worship ("you are coming to worship of illustrious queen of Veloxi?"): calmed by grovelling, angered by hostility, and
	// fairly aggressive
	static readonly AlienPersonality s_velox = new AlienPersonality
	{
		m_friendlyBelow = 25.0f, m_hostileFrom = 60.0f, m_fightFrom = 90.0f, m_startingMood = 45.0f,
		m_friendlyEffect = -5.0f, m_hostileEffect = 15.0f, m_obsequiousEffect = -10.0f, m_aggression = 6.0f
	};

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
