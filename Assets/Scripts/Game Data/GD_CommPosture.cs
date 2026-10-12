using System;

// the shipped game's fields of a comm line (recovered from the original's STARB.COM, its PHRASECONTRL records, see RecoveredData): which of the speaker's postures
// the line is said in, where, what saying it does, and what the player's answer does when the line is a question - the game data gives each line one stance only
[Serializable]

public class GD_CommPosture
{
	// the comm line of the game data (GD_Comm.m_id)
	public int m_commId;

	// the speaker's postures the line is said in, as bits: 1 obsequious, 2 friendly, 4 neutral, 8 hostile (COMM-OV, FIND-PHRASE: LP)
	public int m_postureMask;

	// where the line is said, as bits: 1 in space, 2 at the race's homeworld (LSC)
	public int m_contextMask;

	// what saying the line does (UA): 127 ends the comm link, -127 nothing, anything else is added to the aliens' mood
	public int m_action;

	// how much the player's No and Yes move the aliens' mood when the line is their question (DEDLN, DEDLY)
	public int m_deltaNo;
	public int m_deltaYes;
}
