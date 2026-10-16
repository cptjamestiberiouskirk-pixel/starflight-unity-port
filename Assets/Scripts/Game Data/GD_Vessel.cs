using System;

[Serializable]

public class GD_Vessel
{
	public int m_id;

	public string m_name;
	public string m_object;
	public string m_type;
	public GameData.Race m_race;

	public int m_mineralDensity;
	public int m_bioDensity;
	public int m_mass;

	// (the vessel record's move delay: the released game reads it nowhere, so it does not set the vessel's speed)
	public int m_moveDelay;

	// how fast this vessel moves compared with the encounter's alien ship speed: every vessel alike, as the original's (COMBAT-OV moves an alien ship one unit in
	// each of its turns, whatever its vessel), and not at all if its vessel record's byte 6 is 0 (+A-VESS copies it to the ship, and WE06B lets only a ship whose
	// byte is not 0 move: the Mechan Scout, the Veloxi Drone, the Nomad Probe and the Noah 9 derelict stay where they are) - the game data keeps that byte as
	// m_bioDensity, 100 for every other vessel
	public float GetSpeedFactor()
	{
		return ( m_bioDensity != 0 ) ? 1.0f : 0.0f;
	}
	public int m_fireDelay;

	public int m_armor;
	public int m_shields;

	public float m_armorClass;
	public int m_shieldClass;

	public bool m_immuneToMissiles;
	public bool m_immuneToLasers;

	public int m_laserClass;
	public int m_missileClass;

	public bool m_hasPlasmaBolts;

	public int m_elementIdA;
	public int m_elementVolumeA;

	public int m_elementIdB;
	public int m_elementVolumeB;

	public int m_elementIdC;
	public int m_elementVolumeC;

	public int m_enduriumVolume;

	// the shipped game's strength of one ship of this vessel at full armor and shields (GD_VesselStrength, from the recovered data - 0 for none)
	[NonSerialized] public int m_strength;
}
