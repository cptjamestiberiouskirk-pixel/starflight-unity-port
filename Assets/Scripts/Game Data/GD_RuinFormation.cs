
using System;

// a formation of ancient ruins around the ruin of a special artifact (recovered from STRINFO 4.1, see RecoveredData): the Most Magnificent Hexagon on Sphexi,
// six ruins in a circle around the Crystal Orb, and the City of the Ancients, fifteen ruins with the Crystal Pearl in their southwest
[Serializable]

public class GD_RuinFormation
{
	public int m_id;

	// the name STRINFO gives it
	public string m_name;

	// the artifact whose ruin the formation is around (its site is in the artifact site list)
	public string m_artifactName;

	// how many ruins, and how they stand: "Ring" (in a circle around the artifact's ruin) or "NorthEast" (a block of rows and columns, with the artifact's ruin in its southwest corner)
	public int m_count;
	public string m_shape;

	// how far apart, in world units (the sources do not say - the port's choice)
	public float m_spacing;

	public string m_text;
	public string m_note;

	// the artifact site the formation is around - worked out when the game data is initialized (negative if it could not be found)
	[NonSerialized] public int m_artifactSiteId = -1;
}
