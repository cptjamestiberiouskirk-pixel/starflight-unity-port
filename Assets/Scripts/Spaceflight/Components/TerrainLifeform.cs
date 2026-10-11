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

	// the behaviour of the 1984 notes (lf-behav.txt), its response to the player: the laser makes the smart scared and the dumb angry, the dangerous that have
	// noticed the terrain vehicle approach it and attack it, the wimpy flee it, and the rest wander - the distances, speeds and chances not in the notes are the
	// port's (the notes count in squares of the tactical map, about 9 world units)
	const float c_behaviourInterval = 0.5f;

	// "?ALIEN-CLOSE-ENOUGH : True if alien is within 5 squares", "?NEXT-TO-PLAYER : True if alien is in adjacent square", and the encounter area ("18x by 30y
	// cells - twice the visible area") outside which a lifeform does nothing
	const float c_closeEnough = 45.0f;
	const float c_nextToPlayer = 12.0f;
	const float c_encounterArea = 150.0f;

	// "?NOTICE-PLAYER : ... a determination is made with a small random chance that it will become true" - a chance at each step of the behaviour
	const float c_noticeChance = 0.15f;

	// how fast a lifeform moves for each point of its movement rate, in world units a second, and how high a flyer flies
	const float c_speedPerMovementRate = 4.0f;
	const float c_flyingHeight = 8.0f;

	// when the player last fired the laser (for "?PLAYER-FIRING-LASER : Set to True when player fires. Set back to false shortly after")
	public static float s_laserFiredTime = -100.0f;

	// its aggression now (the laser changes it, lf-words.txt: REDUCE-AGGR-LEVEL, INCREASES-AGGR-LEVEL), and whether it has noticed the terrain vehicle
	public int m_aggression;
	public bool m_noticedPlayer;

	// what it is doing: the direction it moves in, if it is moving, and when it next decides
	Vector3 m_moveDirection;
	bool m_moving;
	bool m_flying;
	float m_behaviourTimer;

	// its populator (for the ground it moves on)
	TerrainLifeforms m_populator;

	// sets it up as one of this species, placed by this populator (passed in: the lifeforms are placed while the surface is hidden, and a search of the parents
	// does not find an inactive one)
	public void Initialize( GD_Species species, TerrainLifeforms populator )
	{
		m_renderer = GetComponent<Renderer>();
		m_species = species;
		m_hits = species.m_hitPoints;
		m_stuns = species.m_stunPoints;
		m_status = Status.Alive;
		m_aggression = species.m_aggression;
		m_populator = populator;

		// a species wanders in its own direction ("base direction on species address value so aliens will move in straight lines")
		var angle = species.m_index * 137.0f * Mathf.Deg2Rad;

		m_moveDirection = new Vector3( Mathf.Sin( angle ), 0.0f, Mathf.Cos( angle ) );
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

	void Update()
	{
		if ( ( SpaceflightController.m_instance == null ) || SpaceflightController.m_instance.m_gameIsPaused || ( m_species == null ) )
		{
			return;
		}

		if ( m_status == Status.Stunned )
		{
			UpdateStunned();
		}

		if ( ( m_status != Status.Alive ) || ( DataController.m_instance.m_playerData.m_general.m_location != PD_General.Location.Disembarked ) )
		{
			return;
		}

		m_behaviourTimer -= Time.deltaTime;

		if ( m_behaviourTimer <= 0.0f )
		{
			m_behaviourTimer = c_behaviourInterval;

			Behave();
		}

		if ( m_moving )
		{
			Move( Time.deltaTime );
		}
	}

	// one step of the notes' expert system: the reaction to the weapons, then the response to the player, then wandering
	void Behave()
	{
		var vehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( vehicle == null )
		{
			return;
		}

		var toVehicle = vehicle.transform.position - transform.position;

		toVehicle.y = 0.0f;

		var distance = toVehicle.magnitude;

		m_moving = false;

		// outside the encounter area it does nothing
		if ( distance > c_encounterArea )
		{
			return;
		}

		// the weapons: "REDUCE-AGGR-LEVEL <-- ?PLAYER-FIRING-LASER TRUE ?ALIEN-CLOSE-ENOUGH TRUE ?ALIEN-SMART-ENOUGH TRUE ?ULTRA-AGGR FALSE ?RANDOM-SCARED TRUE",
		// "INCREASE-AGGR-LEVEL <-- ?ALIEN-HIT-WITH-LASER TRUE ?DUMB-ENOUGH TRUE ?RANDOM-ANGRY TRUE ?PRODUCER FALSE" (and the same for the stunner)
		if ( ( Time.time - s_laserFiredTime < 1.0f ) && ( distance <= c_closeEnough ) && ( m_species.m_intelligence >= 70 ) && ( m_aggression < 80 ) && ( Random.value < 0.1f ) )
		{
			m_aggression -= Random.Range( 1, 51 );
		}

		if ( ( m_hitWithLaser || m_hitWithStunner ) && ( m_species.m_intelligence < 35 ) && ( Random.value < 0.1f ) && !m_species.IsPlant() )
		{
			m_aggression += Random.Range( 1, 21 );
		}

		m_aggression = Mathf.Clamp( m_aggression, 1, 100 );
		m_hitWithLaser = false;
		m_hitWithStunner = false;

		// the response to the player
		if ( !m_noticedPlayer && ( Random.value < c_noticeChance ) )
		{
			m_noticedPlayer = true;
		}

		var mobile = m_species.m_movementRate > 0;
		var dangerous = m_aggression > 65;
		var wimpy = m_aggression < 35;

		// "ATTACK-PLAYER <-- ?ALIVE TRUE ?STUNNED FALSE ?NOTICE-PLAYER TRUE ?DANGEROUS TRUE ?NEXT-TO-PLAYER TRUE"
		if ( m_noticedPlayer && dangerous && ( distance <= c_nextToPlayer ) )
		{
			AttackPlayer();

			return;
		}

		// "APPROACH-PLAYER <-- ?MOBILE TRUE ... ?NOTICE-PLAYER TRUE ?DANGEROUS TRUE ?NEXT-TO-PLAYER FALSE"
		if ( mobile && m_noticedPlayer && dangerous && ( distance > 0.01f ) )
		{
			m_moveDirection = toVehicle / distance;
			m_moving = true;

			return;
		}

		// "FLEE-PLAYER <-- ?MOBILE TRUE ?NOTICE-PLAYER TRUE ... ?WIMPY TRUE"
		if ( mobile && m_noticedPlayer && wimpy && ( distance > 0.01f ) )
		{
			m_moveDirection = -toVehicle / distance;
			m_moving = true;

			return;
		}

		// "WANDER <-- ?ALIVE TRUE ?STUNNED FALSE ?MOBILE TRUE": in its species' direction, with "some random variation"
		if ( mobile )
		{
			m_moveDirection = Quaternion.Euler( 0.0f, Random.Range( -20.0f, 20.0f ), 0.0f ) * m_moveDirection;
			m_moving = true;
		}
	}

	// moves on: a lifeform on the ground keeps to dry ground (another direction where the way is wet), and a flyer goes over anything, taking to the air when it
	// moves ("FLY <-- ?MOVING TRUE ?ON-GROUND TRUE ?CAN-FLY TRUE ?WANTS-TO-FLY TRUE") and landing when it stops
	void Move( float deltaTime )
	{
		var populator = m_populator;

		if ( populator == null )
		{
			return;
		}

		var next = transform.position + m_moveDirection * m_species.m_movementRate * c_speedPerMovementRate * deltaTime;

		if ( !populator.GetGround( next, out var height ) && !m_species.m_canFly )
		{
			// the way is wet: turn
			m_moveDirection = Quaternion.Euler( 0.0f, 90.0f, 0.0f ) * m_moveDirection;

			return;
		}

		m_flying = m_species.m_canFly;

		next.y = height + transform.localScale.y * 0.5f + ( m_flying ? c_flyingHeight : 0.0f );

		transform.position = next;
	}

	// "ATTACK-PLAYER : Determine whether or not alien hits or misses. Distribute any damage, and print out result-phrase." - one to three attacks (70%, 25%,
	// 5%), each on a crew member at random, a hit if a number from 1 to 100 is under the average of its aggression and intelligence, for damage of up to
	// (aggression + 2 x hit points) / 3 / the crew member's durability
	void AttackPlayer()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var messages = SpaceflightController.m_instance.m_messages;

		var roll = Random.Range( 1, 101 );
		var attacks = ( roll <= 70 ) ? 1 : ( ( roll <= 95 ) ? 2 : 3 );

		// the crew on board who are alive
		var crew = new System.Collections.Generic.List<PD_Personnel.PD_PersonnelFile>();

		for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
		{
			if ( !playerData.m_crewAssignment.IsAssigned( role ) )
			{
				continue;
			}

			var file = playerData.m_crewAssignment.GetPersonnelFile( role );

			if ( ( file != null ) && ( file.m_vitality > 0 ) && !crew.Contains( file ) )
			{
				crew.Add( file );
			}
		}

		if ( crew.Count == 0 )
		{
			return;
		}

		messages.AddText( "<color=red>Captain, we're being attacked!</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Error );

		for ( var i = 0; i < attacks; i++ )
		{
			var victim = crew[ Random.Range( 0, crew.Count ) ];
			var phrase = "The " + m_species.GetBodyPlanText().ToLower() + " lifeform attacks " + victim.m_name;

			if ( Random.Range( 1, 101 ) >= ( m_aggression + m_species.m_intelligence ) / 2 )
			{
				messages.AddText( "<color=white>" + phrase + " and misses.</color>" );

				continue;
			}

			var durability = 1;

			if ( ( victim.m_crewRaceId >= 0 ) && ( victim.m_crewRaceId < gameData.m_crewRaceList.Length ) )
			{
				durability = Mathf.Max( 1, gameData.m_crewRaceList[ victim.m_crewRaceId ].m_durability );
			}

			var maximumDamage = Mathf.Max( 1, ( m_aggression + 2 * m_hits ) / 3 / durability );
			var damage = Random.Range( 1, maximumDamage + 1 );

			messages.AddText( "<color=red>" + phrase + ". " + victim.m_name + "'s vitality is " + Mathf.CeilToInt( Mathf.Max( 0.0f, victim.m_vitality - damage ) ) + "%.</color>" );

			// (at 0 the crew member dies, see PD_CrewAssignment.InjureCrewMember)
			if ( playerData.m_crewAssignment.InjureCrewMember( victim, damage ) )
			{
				crew.Remove( victim );

				if ( crew.Count == 0 )
				{
					return;
				}
			}
		}
	}

	// a stunned lifeform recovers (the port reads "UNSTUNNED <-- ?STUNS>0 TRUE ?STUNNED TRUE" as once its stuns are all back, so that a stun lasts)
	void UpdateStunned()
	{
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
