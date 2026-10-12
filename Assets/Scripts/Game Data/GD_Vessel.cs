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

	public int m_moveDelay;

	// the move delay at which an alien ship moves at the encounter's alien ship speed (the delay of most of the vessels between the fastest and the slowest - the
	// port's choice: the game data gives how the vessels' speeds compare, not how fast any of them is)
	public const float c_referenceMoveDelay = 2200.0f;

	// how fast this vessel moves compared with the encounter's alien ship speed: the shorter its move delay in the game data, the faster
	public float GetSpeedFactor()
	{
		return ( m_moveDelay > 0 ) ? ( c_referenceMoveDelay / m_moveDelay ) : 1.0f;
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
