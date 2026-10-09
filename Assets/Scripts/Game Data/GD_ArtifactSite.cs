
using System;

// where the original game put one of its special artifacts on a planet (recovered from STRINFO 4.1, see RecoveredData)
[Serializable]

public class GD_ArtifactSite
{
	public int m_id;

	// the artifact, by its name in the game data
	public string m_artifactName;

	// the planet, as STRINFO names it: the star by its coordinates and the planet by its number counted from the sun
	public int m_starX;
	public int m_starY;
	public int m_planetFromSun;

	// latitude is positive to the north, longitude positive to the east
	public int m_latitude;
	public int m_longitude;

	public string m_text;
	public string m_note;

	// the planet and the artifact in the game data - worked out when the game data is initialized (negative if they could not be found)
	[NonSerialized] public int m_planetId = -1;
	[NonSerialized] public int m_artifactId = -1;
}
