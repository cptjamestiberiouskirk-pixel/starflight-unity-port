using System;

[Serializable]

public class GD_Comm
{
	public enum Subject
	{
		None = 0,
		GreetingHail = 1,
		GreetingResponse = 2,
		Yes = 3,
		No = 4,
		NoMoreInformation = 5,
		WaitingForResponse = 6,
		Themselves = 7,
		OtherRaces = 8,
		GeneralInfo = 9,
		OldEmpire = 10,
		TheAncients = 11,
		Statement = 13,
		Question = 14,
		Terminate = 15,
		Custom = 16,
		OrbitGranted = 17,
		OrbitDenied = 18
	}

	public enum Stance
	{
		None = 0,
		Neutral = 1,
		Friendly = 2,
		Hostile = 3,
		Obsequious = 4
	};

	public int m_id;
	public GameData.Race m_race;
	public Subject m_subject;
	public Stance m_stance;
	public bool m_homeworld;
	public bool m_surrender;
	public string m_text;

	// the shipped game's postures of the speaker that the line is said in, as bits (GD_CommPosture, from the recovered data - 0 for a line the shipped game does
	// not have, which is said in its m_stance only)
	[NonSerialized] public int m_postureMask;

	// what saying the line does in the shipped game (GD_CommPosture.m_action, COMM-OV DO-UA): it ends the comm link (127), or moves the speaking aliens' mood
	// by this much (0 for nothing, the shipped game's -127)
	[NonSerialized] public bool m_endsCommLink;
	[NonSerialized] public int m_moodAction;

	// how much the player's No and Yes move the aliens' mood when the line is their question (GD_CommPosture, COMM-OV DELTA-NO and DELTA-YES)
	[NonSerialized] public int m_deltaNo;
	[NonSerialized] public int m_deltaYes;

	// the bit of a posture in a posture mask (COMM-OV: 1 obsequious, 2 friendly, 4 neutral, 8 hostile)
	public static int GetPostureBit( Stance stance )
	{
		switch ( stance )
		{
			case Stance.Obsequious:
				return 1;

			case Stance.Friendly:
				return 2;

			case Stance.Neutral:
				return 4;

			case Stance.Hostile:
				return 8;
		}

		return 0;
	}

	// true if the line is said in this posture of the speaker's: by the shipped game's posture mask where it has one, else by the game data's stance
	public bool IsSaidIn( Stance stance )
	{
		if ( m_postureMask != 0 )
		{
			return ( m_postureMask & GetPostureBit( stance ) ) != 0;
		}

		return m_stance == stance;
	}

	public GD_Comm( string text )
	{
		m_text = text;
	}
}
