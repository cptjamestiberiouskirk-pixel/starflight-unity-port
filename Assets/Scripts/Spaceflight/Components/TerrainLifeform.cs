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

	// set when it has just been hit (lf-words.txt: "ALIEN-HIT-WITH-LASER ... these flags should be set when the alien is hit, and cleared when this flag is poled")
	public bool m_hitWithLaser;
	public bool m_hitWithStunner;

	// a stunned lifeform gets its stuns back one by one (lf-words.txt: "INCREMENT-STUNS : Add one to the Stuns of that alien", while "?STUNS<STUN-POINTS") - the
	// pace is the port's: two a second, so that a lifeform stays stunned for 5 to 50 seconds
	const float c_stunsRecoveredPerSecond = 2.0f;

	float m_stunRecovery;

	// its renderer (for its colour)
	Renderer m_renderer;

	// sets it up as one of this species
	public void Initialize( GD_Species species )
	{
		m_renderer = GetComponent<Renderer>();
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

	// the laser: "if it hits, it does from 20 to 100 points of damage" (planet.txt) - returns the damage
	public int HitWithLaser()
	{
		if ( m_status == Status.Dead )
		{
			return 0;
		}

		var damage = Random.Range( 20, 101 );

		m_hits -= damage;
		m_hitWithLaser = true;

		UpdateStatus();

		return damage;
	}

	// the stunner: "the effect of the stunner on any given species is drawn from that specie's Species record. The lifeform may either be 1) unaffected 2) stunned
	// for some period of time or 3) killed" (planet.txt), and a stunner hit takes 10 to 50 stuns (lf-words.txt: REDUCE-STUNS-BY-10-to-50)
	public void HitWithStunner()
	{
		if ( m_status == Status.Dead )
		{
			return;
		}

		m_hitWithStunner = true;

		switch ( m_species.m_stunEffect )
		{
			case GD_Species.StunEffect.Unaffected:

				return;

			case GD_Species.StunEffect.Killed:

				m_hits = 0;
				break;

			default:

				m_stuns -= Random.Range( 10, 51 );

				// "STUN-EFFECTS-HITS : Take the absolute value of the Stuns and subtract it from the Hits. Then set Stuns to 0."
				if ( m_stuns < 0 )
				{
					m_hits += m_stuns;
					m_stuns = 0;
				}

				break;
		}

		UpdateStatus();
	}

	// the notes' status check: "DIE <-- ?HITS>0 FALSE", "STUNNED <-- ?STUNS>0 FALSE ?STUNNED FALSE"
	void UpdateStatus()
	{
		if ( m_hits <= 0 )
		{
			m_hits = 0;
			m_status = Status.Dead;

			// a dead lifeform lies on its side, darkened
			transform.rotation = Quaternion.Euler( 0.0f, transform.eulerAngles.y, 90.0f );

			Tint( 0.35f );

			return;
		}

		if ( ( m_stuns <= 0 ) && ( m_status == Status.Alive ) )
		{
			m_status = Status.Stunned;
			m_stunRecovery = 0.0f;

			Tint( 0.6f );
		}
	}

	// a stunned lifeform recovers (the port reads "UNSTUNNED <-- ?STUNS>0 TRUE ?STUNNED TRUE" as once its stuns are all back, so that a stun lasts)
	void Update()
	{
		if ( ( m_status != Status.Stunned ) || ( SpaceflightController.m_instance == null ) || SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		m_stunRecovery += Time.deltaTime * c_stunsRecoveredPerSecond;

		var recovered = Mathf.FloorToInt( m_stunRecovery );

		m_stunRecovery -= recovered;
		m_stuns = Mathf.Min( m_species.m_stunPoints, m_stuns + recovered );

		if ( m_stuns >= m_species.m_stunPoints )
		{
			m_status = Status.Alive;

			Tint( 1.0f );
		}
	}

	// shows its species' colour, at this brightness
	void Tint( float brightness )
	{
		if ( m_renderer != null )
		{
			m_renderer.material.color = new Color( m_species.m_red * brightness, m_species.m_green * brightness, m_species.m_blue * brightness );
		}
	}
}
