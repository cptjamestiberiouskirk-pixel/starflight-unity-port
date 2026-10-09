
using System;

// a planet the starport pays a bonus for when it is recommended for colonization (recovered from STRINFO 1.2, see RecoveredData)
[Serializable]

public class GD_ColonyEvaluation
{
	public int m_id;

	// the planet, as STRINFO names it: the star by its coordinates and the planet by its number counted from the sun
	public int m_starX;
	public int m_starY;
	public int m_planetFromSun;
	public string m_placeName;

	// "Optimal" or "Suitable", and the bonus in MU
	public string m_rating;
	public int m_bonus;

	public string m_note;

	// the planet in the game data - worked out when the game data is initialized (negative if it could not be found)
	[NonSerialized] public int m_planetId = -1;
}
