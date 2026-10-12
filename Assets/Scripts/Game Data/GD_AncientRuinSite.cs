using System;

// ancient ruins whose place and Endurium STRINFO's editors give (recovered from STRINFO 2.1.4 and 2.5.4, see RecoveredData): the Mechans' ruin on the ice planet of
// their system, and the two Thrynn ruins deep in Elowan space - each ruin holds the Endurium of its entry and nothing else
[Serializable]

public class GD_AncientRuinSite
{
	public int m_id;

	// the planet, as STRINFO names it: the star by its coordinates and the planet by its number counted from the sun
	public int m_starX;
	public int m_starY;
	public int m_planetFromSun;

	// where (latitude positive to the north, longitude positive to the east)
	public int m_latitude;
	public int m_longitude;

	// the Endurium of each ruin at the site, in tenths of a cubic meter as the cargo holds count it (one ruin per entry)
	public int[] m_enduriumVolumes;

	// how far apart the ruins of a site stand, in world units (STRINFO does not say - the port's choice)
	public float m_spacing;

	public string m_text;
	public string m_note;

	// the planet - worked out when the game data is initialized (negative if it could not be found)
	[NonSerialized] public int m_planetId = -1;
}
