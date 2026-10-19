
using System;

// a lifeform captured and put in stasis (planet.txt: "The alien lifeform has been captured and put in stasis"), with the value saved with it ("If the lifeform is
// captured or recorded a value is saved with it") - it is sold at the Trade Depot
[Serializable]

public class PD_Specimen
{
	// the share of its value the Trade Depot pays for a specimen, in percent: the original's sale pays the value saved with it times %VAL, which its Sell screen
	// sets to 50 (TDEPOT-OV WEF38 and WEE73), and its Sell list shows the same (IT-OV WEF89)
	const int c_salePercentage = 50;

	// its planet and the index of its species there (GD_Species.ForPlanet)
	public int m_planetId;
	public int m_speciesIndex;

	// its description (its species' body plan)
	public string m_name;

	// true for a lifeform's recorded data rather than the lifeform itself (the shipped game's BIO-DATA, ITEMS-OV WE6B8: an item of the hold with no volume, worth
	// an eighth of the lifeform's value, sold at the Trade Depot like a specimen) - false in older save files, which had no recordings in the holds
	public bool m_isBioData;

	// its volume, in tenths of a cubic meter like everything in the cargo holds
	public int m_volume;

	// the value saved with it when it was captured (TerrainLifeforms)
	public int m_value;

	// where and when it was captured, which the ship's cargo list describes (CAPTAIN WEC73: its system, planet, location and date) - a specimen from a save
	// written before has none (m_hasCapturePlace false)
	public bool m_hasCapturePlace;
	public int m_capturedNorthSouth;
	public int m_capturedEastWest;
	public string m_capturedStardateYMD;

	// what the Trade Depot pays for it
	public int GetSaleValue()
	{
		return m_value * c_salePercentage / 100;
	}
}
