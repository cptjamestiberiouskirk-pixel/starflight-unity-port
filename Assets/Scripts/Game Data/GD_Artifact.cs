
using System;

[Serializable]

public class GD_Artifact
{
	public int m_id;
	public string m_name;
	public int m_volume;
	public int m_starportPrice;
	public int m_thrynnPrice;
	public int m_actualValue;
	public bool m_availableInStarport;
	public bool m_knownAtStart;
	public string m_analysisText;

	// the original's special artifacts, its ARTIFACT records 1 to 15 in the shipped game's data (START\STARB.COM): the ones with an analysis of their own, which
	// the Trade Depot never throws away (TDEPOT-OV <TDART keeps every artifact of a record number up to 15); every other artifact is a curio
	static readonly string[] c_specialArtifactNames =
	{
		"Crystal Orb", "Crystal Cone", "Black Egg", "Whining Orb", "Flat Device", "Rod Device", "Tesseract", "Ring Device", "Hypercube", "Shimmering Ball",
		"Red Cylinder", "Dodecahedron", "Crystal Pearl", "Ellipsoid", "Black Box"
	};

	// true if this is one of the original's special artifacts
	public bool IsSpecial()
	{
		return Array.IndexOf( c_specialArtifactNames, m_name ) >= 0;
	}
}
