using System;

// the shipped game's strength of a vessel (recovered from the original's VESSEL records, see RecoveredData): what one ship of it adds to its encounter's strength
// at full armor and shields - the Spemin's ships 0, 5 and 10, the Uhlek's 255 (COMM-OV IAPWR, DAMAGE-OV)
[Serializable]

public class GD_VesselStrength
{
	// the vessel of the game data (GD_Vessel.m_id)
	public int m_vesselId;

	// its strength (the original's VESSEL byte 5, which the data extract calls Energy)
	public int m_strength;
}
