
using System;

// a message the original game left on a planet: at its coordinates, at random places on the planet or at several places (recovered from STRINFO 3.1, see RecoveredData)
[Serializable]

public class GD_PlanetMessage
{
	public int m_id;

	// the planet, as STRINFO names it: the star by its coordinates and the planet by its number counted from the sun
	public int m_starX;
	public int m_starY;
	public int m_planetFromSun;
	public string m_placeName;

	// "Site" (at the coordinates), "Random" (at random places on the planet) or "Several" (at several places)
	public string m_placement;

	// latitude is positive to the north, longitude positive to the east (both 0 when the placement is not a site)
	public int m_latitude;
	public int m_longitude;

	public string m_text;
	public string m_note;

	// the planet in the game data - worked out when the game data is initialized (negative if it could not be found)
	[NonSerialized] public int m_planetId = -1;
}
