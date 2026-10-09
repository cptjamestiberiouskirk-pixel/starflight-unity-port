
using System;

// a text of the original game that is not a comm or a planet message: evaluations, the win, the flare (recovered from STRINFO, see RecoveredData)
[Serializable]

public class GD_StoryText
{
	// what the text is for (for example "EvaluationOptimal" or "FlareDeath")
	public string m_key;

	// the text as STRINFO prints it - the parts in brackets are values the game fills in
	public string m_text;

	public string m_note;
}
