
using System;

// a lifeform captured and put in stasis (planet.txt: "The alien lifeform has been captured and put in stasis"), with the value saved with it ("If the lifeform is
// captured or recorded a value is saved with it") - it is sold at the Trade Depot
[Serializable]

public class PD_Specimen
{
	// its planet and the index of its species there (GD_Species.ForPlanet)
	public int m_planetId;
	public int m_speciesIndex;

	// its description (its species' body plan)
	public string m_name;

	// its volume, in tenths of a cubic meter like everything in the cargo holds
	public int m_volume;

	// what the Trade Depot pays for it
	public int m_value;
}
