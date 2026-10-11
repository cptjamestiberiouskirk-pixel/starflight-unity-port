using UnityEngine;

// a lifeform on a planet's surface: its species, and the state of this one (the 1984 design notes' instance record, lf-words.txt: hits, stuns and status)
public class TerrainLifeform : MonoBehaviour
{
	// "STATUS : Has 3 states - Alive, Dead, and Stunned"
	public enum Status
	{
		Alive,
		Stunned,
		Dead
	}

	// its species (GD_Species.ForPlanet)
	public GD_Species m_species;

	// its hit points and stuns left, starting with its species'
	public int m_hits;
	public int m_stuns;

	public Status m_status = Status.Alive;

	// sets it up as one of this species
	public void Initialize( GD_Species species )
	{
		m_species = species;
		m_hits = species.m_hitPoints;
		m_stuns = species.m_stunPoints;
		m_status = Status.Alive;
	}

	// the status in words, as the scan gives it
	public string GetStatusText()
	{
		return ( m_status == Status.Alive ) ? "Alive" : ( ( m_status == Status.Stunned ) ? "Stunned" : "Dead" );
	}
}
