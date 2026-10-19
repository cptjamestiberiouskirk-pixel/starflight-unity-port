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

	// its place among the lifeforms of its planet (its block and the attempt that placed it there, the same every time, TerrainLifeforms): what has become of
	// it is kept by it until the ship leaves the orbit (PD_PlanetSurfaces.LoseLifeform)
	public int m_placeIndex;

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

	// its renderers (for its colour): a model's skin takes the species colour and its details (eyes, claws, wood) keep theirs, and a plain shape is all
	// species colour
	Renderer[] m_renderers;
	MaterialPropertyBlock m_propertyBlock;

	// how far its pivot is above the ground (0 for a model, which stands on its base; half the height of a plain shape)
	public float m_groundOffset;

	// how fast it turns to face the way it goes, in turns a second (the port's)
	const float c_turnRate = 4.0f;

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

	// how fast a lifeform moves for each point of its movement rate, in world units a second, how high a flyer flies and how high a floater floats (the port's)
	const float c_speedPerMovementRate = 4.0f;
	const float c_flyingHeight = 8.0f;
	const float c_floatingHeight = 3.0f;

	// the notes' ground-air transitions (lf-words.txt): "?WANTS-TO-FLY : Some randomly determined chance with a fairly high %", "?WANTS-TO-LAND : Some randomly
	// determined chance with a low to moderate %" - the chances at each step of the behaviour are the port's own, by design decision - and "?OK-TO-LAND : Fasle if
	// the square the alien is on is liquid, lava, or occupied by another alien or the player", the square taken as half of one around it
	const float c_wantsToFlyChance = 0.75f;

	// "?WANTS-TO-FLOAT : same" (as ?WANTS-TO-FLY)
	const float c_wantsToFloatChance = 0.75f;
	const float c_wantsToLandChance = 0.2f;
	const float c_squareTaken = 4.5f;

	// "BEHAVIOR : Has 5 states - Fleeing Mate, Approaching Mate, Attacking Mate, Wandering, and Other" (lf-words.txt)
	public enum Behaviour
	{
		Other,
		Wandering,
		ApproachingMate,
		FleeingMate,
		AttackingMate,

		// the response to the player, for Look (the notes' "Other")
		ApproachingPlayer,
		FleeingPlayer,
		AttackingPlayer
	}

	// its mate (lf-behav.txt: "Pair all un-paired lifeforms in the encounter area"): another lifeform of any species that it may hunt, flee, attack or eat by the
	// notes' rules - the pairs are made again on every visit, as the lifeforms are
	TerrainLifeform m_mate;

	public Behaviour m_behaviour = Behaviour.Other;

	// "?ATTACKED : Set to true once an alien has attacked its mate. Set to false if an alien is not next to its mate."
	bool m_attackedMate;

	// "?RANDOMLY-UNPAIRED : A random determination such that this will happen every now and then" - the chance at each step is the port's
	const float c_unpairChance = 0.02f;

	// "EAT-ITS-MATE : Roll a RND no. from 1 - 100. If it is < 10 then the mate is eaten and it dissappears."
	const int c_eatChance = 10;

	// "?BIG-ENOUGH : Is the alien's size (1-9) >= its mate's size - 2 ?", "?ULTRA-AGGR : True if alien's aggression is >= 80", "?AGGR-ENOUGH : Is its
	// aggression level >= 40 ?", "?CAN-BREAK-AWAY? : There is a 10% chance this is true"
	const int c_sizeAdvantage = 2;
	const int c_ultraAggressive = 80;
	const int c_aggressiveEnough = 40;
	const float c_breakAwayChance = 0.1f;

	// when it last attacked the crew (a recording of it is delayed: planet.txt, "If the alien attacks before the process is completed, completion of the process is
	// delayed")
	public float m_lastAttackTime = -100.0f;

	// when the player last fired the laser (for "?PLAYER-FIRING-LASER : Set to True when player fires. Set back to false shortly after")
	public static float s_laserFiredTime = -100.0f;

	// its aggression now (the laser changes it, lf-words.txt: REDUCE-AGGR-LEVEL, INCREASES-AGGR-LEVEL), and whether it has noticed the terrain vehicle
	public int m_aggression;
	public bool m_noticedPlayer;

	// what it is doing: the direction it moves in, if it is moving, if it is in the air (the notes' ON-GROUND the other way round: "Set to false when the alien
	// floats or flys and is set to true again when it lands"), and when it next decides
	Vector3 m_moveDirection;
	bool m_moving;
	bool m_flying;
	bool m_floating;
	float m_behaviourTimer;

	// true if it flies or floats: "Airborne creatures may not be dealt with by the player" (planet.txt), and the notes' ON-GROUND false
	public bool IsAirborne()
	{
		return m_flying || m_floating;
	}

	// its populator (for the ground it moves on)
	TerrainLifeforms m_populator;

	// sets it up as one of this species, placed by this populator (passed in: the lifeforms are placed while the surface is hidden, and a search of the parents
	// does not find an inactive one)
	public void Initialize( GD_Species species, TerrainLifeforms populator )
	{
		m_renderers = GetComponentsInChildren<Renderer>();
		m_propertyBlock = new MaterialPropertyBlock();
		m_species = species;
		m_hits = species.m_hitPoints;
		m_stuns = species.m_stunPoints;
		m_status = Status.Alive;
		m_aggression = species.m_aggression;
		m_populator = populator;

		// a species wanders in its own direction ("base direction on species address value so aliens will move in straight lines")
		var angle = species.m_index * 137.0f * Mathf.Deg2Rad;

		m_moveDirection = new Vector3( Mathf.Sin( angle ), 0.0f, Mathf.Cos( angle ) );

		Tint( 1.0f );
	}

	// "any current Movement behavior" in words, for Look (planet.txt)
	public string GetMovementText()
	{
		if ( m_status == Status.Dead )
		{
			return "It does not move.";
		}

		if ( m_status == Status.Stunned )
		{
			return "It lies stunned.";
		}

		switch ( m_behaviour )
		{
			case Behaviour.Wandering: return "It is wandering.";
			case Behaviour.ApproachingMate: return "It is stalking another lifeform.";
			case Behaviour.FleeingMate: return "It is fleeing another lifeform.";
			case Behaviour.AttackingMate: return "It is attacking another lifeform.";
			case Behaviour.ApproachingPlayer: return "It is coming at the terrain vehicle.";
			case Behaviour.FleeingPlayer: return "It is fleeing from the terrain vehicle.";
			case Behaviour.AttackingPlayer: return "It is attacking the terrain vehicle!";
		}

		return "It is not moving.";
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

	// a lifeform that was killed on an earlier run of the terrain vehicle in this orbit lies dead where it is placed
	public void LieDead()
	{
		m_hits = 0;

		UpdateStatus();
	}

	// the notes' status check: "DIE <-- ?HITS>0 FALSE", "STUNNED <-- ?STUNS>0 FALSE ?STUNNED FALSE"
	void UpdateStatus()
	{
		if ( m_hits <= 0 )
		{
			// it stays dead on later runs of the terrain vehicle until the ship leaves the orbit
			if ( ( m_status != Status.Dead ) && ( m_populator != null ) )
			{
				m_populator.LifeformDied( this );
			}

			m_hits = 0;
			m_status = Status.Dead;

			// a dead flyer or floater comes down ("LAND <-- ?ALIVE FALSE ?ON-GROUND FALSE ?OK-TO-LAND TRUE", and over a liquid onto its surface, by design decision)
			if ( IsAirborne() )
			{
				Land();
			}

			// a dead lifeform lies on its side on the ground, darkened
			transform.rotation = Quaternion.Euler( 0.0f, transform.eulerAngles.y, 90.0f );

			Settle();

			Tint( 0.35f );

			return;
		}

		if ( ( m_stuns <= 0 ) && ( m_status == Status.Alive ) )
		{
			m_status = Status.Stunned;
			m_stunRecovery = 0.0f;

			// so does a stunned one ("LAND <-- ?STUNNED TRUE ?ON-GROUND FALSE ?OK-TO-LAND TRUE", the same over a liquid)
			if ( IsAirborne() )
			{
				Land();
			}

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

			// the notes' loop for a lifeform in the encounter area: its behaviour, then the ground-air transitions
			if ( Behave() )
			{
				UpdateAirGround();
			}
		}

		if ( m_moving )
		{
			Move( Time.deltaTime );
		}
	}

	// one step of the notes' expert system: the reaction to the weapons, then the response to the player, then wandering - returns false outside the encounter
	// area, where it does nothing
	bool Behave()
	{
		var vehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( vehicle == null )
		{
			return false;
		}

		var toVehicle = vehicle.transform.position - transform.position;

		toVehicle.y = 0.0f;

		var distance = toVehicle.magnitude;

		m_moving = false;
		m_behaviour = Behaviour.Other;

		// outside the encounter area it does nothing
		if ( distance > c_encounterArea )
		{
			return false;
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
			m_behaviour = Behaviour.AttackingPlayer;

			AttackPlayer();

			return true;
		}

		// "APPROACH-PLAYER <-- ?MOBILE TRUE ... ?NOTICE-PLAYER TRUE ?DANGEROUS TRUE ?NEXT-TO-PLAYER FALSE"
		if ( mobile && m_noticedPlayer && dangerous && ( distance > 0.01f ) )
		{
			m_moveDirection = toVehicle / distance;
			m_moving = true;
			m_behaviour = Behaviour.ApproachingPlayer;

			return true;
		}

		// "FLEE-PLAYER <-- ?MOBILE TRUE ?NOTICE-PLAYER TRUE ... ?WIMPY TRUE"
		if ( mobile && m_noticedPlayer && wimpy && ( distance > 0.01f ) )
		{
			m_moveDirection = -toVehicle / distance;
			m_moving = true;
			m_behaviour = Behaviour.FleeingPlayer;

			return true;
		}

		// the response to another lifeform: its mate
		if ( RespondToMate( vehicle.transform.position, mobile ) )
		{
			return true;
		}

		// "WANDER <-- ?ALIVE TRUE ?STUNNED FALSE ?MOBILE TRUE": in its species' direction, with "some random variation"
		if ( mobile )
		{
			m_moveDirection = Quaternion.Euler( 0.0f, Random.Range( -20.0f, 20.0f ), 0.0f ) * m_moveDirection;
			m_moving = true;
			m_behaviour = Behaviour.Wandering;
		}

		return true;
	}

	// true if this lifeform is in the encounter area around the terrain vehicle
	static bool IsInEncounterArea( TerrainLifeform lifeform, Vector3 vehiclePosition )
	{
		var delta = lifeform.transform.position - vehiclePosition;

		delta.y = 0.0f;

		return delta.magnitude <= c_encounterArea;
	}

	// pairs it with the nearest unpaired lifeform in the encounter area, if there is one (the notes do not say which: the nearest is the port's)
	void FindMate( Vector3 vehiclePosition )
	{
		if ( m_populator == null )
		{
			return;
		}

		TerrainLifeform nearest = null;
		var nearestDistance = float.MaxValue;

		foreach ( var other in m_populator.m_lifeformList )
		{
			if ( ( other == null ) || ( other == this ) || ( other.m_mate != null ) || !IsInEncounterArea( other, vehiclePosition ) )
			{
				continue;
			}

			var distance = Vector3.Distance( other.transform.position, transform.position );

			if ( distance < nearestDistance )
			{
				nearest = other;
				nearestDistance = distance;
			}
		}

		if ( nearest != null )
		{
			m_mate = nearest;
			nearest.m_mate = this;
		}
	}

	// "BECOME-UNPAIRED : Un-link lifeform from it's mate." (both ways)
	void BecomeUnpaired()
	{
		if ( ( m_mate != null ) && ( m_mate.m_mate == this ) )
		{
			m_mate.m_mate = null;
			m_mate.m_attackedMate = false;
		}

		m_mate = null;
		m_attackedMate = false;
	}

	// the notes' response to another lifeform (lf-behav.txt): a lifeform approaches the mate it can eat, attacks it when it is next to it, and eats it, and one
	// whose mate comes at it flees - returns true if it did something - the notes list the rules without saying which comes first where two could apply: the port
	// tries eating before attacking, so that a grazer that has bitten a plant may eat it ("?ATTACKED"), and the rules to become unpaired first, since the others
	// need a mate
	bool RespondToMate( Vector3 vehiclePosition, bool mobile )
	{
		if ( m_mate == null )
		{
			FindMate( vehiclePosition );
		}

		if ( m_mate == null )
		{
			return false;
		}

		// "BECOME-UNPAIRED <-- ?HAS-MATE TRUE ?MATE-OUT-OF-BOUNDS TRUE", "... ?RANDOMLY-UNPAIRED TRUE", "... ?MATE-JUST-EATEN TRUE" (an eaten mate is gone)
		if ( !IsInEncounterArea( m_mate, vehiclePosition ) || ( Random.value < c_unpairChance ) )
		{
			BecomeUnpaired();

			return true;
		}

		var toMate = m_mate.transform.position - transform.position;

		toMate.y = 0.0f;

		var mateDistance = toMate.magnitude;
		var nextToMate = mateDistance <= c_nextToPlayer;

		if ( !nextToMate )
		{
			m_attackedMate = false;
		}

		// the niches as the shipped game's bits (2 a producer, 4 eats plants, 8 eats animals): a herbivorous or carnivorous producer is a plant that hunts as a
		// herbivore or a carnivore does
		var niche = m_species.GetNicheBits();
		var mateNiche = m_mate.m_species.GetNicheBits();

		var producer = ( niche & 2 ) != 0;
		var herbivore = ( niche & 12 ) == 4;
		var carnivore = ( niche & 12 ) == 8;
		var omnivore = ( niche & 12 ) == 12;
		var mateProducer = ( mateNiche & 2 ) != 0;
		var mateHerbivore = ( mateNiche & 12 ) == 4;

		var mateAlive = m_mate.m_status != Status.Dead;
		var mateOnGround = !m_mate.IsAirborne();
		var mateApproaching = m_mate.m_behaviour == Behaviour.ApproachingMate;
		var mateAttacking = m_mate.m_behaviour == Behaviour.AttackingMate;
		var bigEnough = m_species.m_size >= m_mate.m_species.m_size - c_sizeAdvantage;
		var ultraAggressive = m_aggression >= c_ultraAggressive;
		var aggressiveEnough = m_aggression >= c_aggressiveEnough;

		// the mates it hunts: "?HERBIVORE TRUE ?MATE-PRODUCER TRUE", "?CARNIVORE TRUE ?MATE-HERBIVORE TRUE ?BIG-ENOUGH TRUE", "?OMNIVORE TRUE ?MATE-PRODUCER TRUE",
		// "?OMNIVORE TRUE ?MATE-HERBIVORE TRUE ?BIG-ENOUGH TRUE", "?HERBIVORE FALSE ?PRODUCER FALSE ?MATE-HERBIVORE FALSE ?MATE-PRODUCER FALSE ?BIG-ENOUGH TRUE
		// ?ULTRA-AGGR TRUE"
		var hunts = ( herbivore && mateProducer ) || ( carnivore && mateHerbivore && bigEnough ) || ( omnivore && mateProducer ) || ( omnivore && mateHerbivore && bigEnough ) || ( !herbivore && !producer && !mateHerbivore && !mateProducer && bigEnough && ultraAggressive );

		// "EAT-ITS-MATE <-- ?CARNIVORE FALSE ?PRODUCER FALSE ?MATE-PRODUCER TRUE ?ATTACKED TRUE ?MATE-IS-ATTACKING FALSE ?NEXT-TO-MATE TRUE", "... ?CARNIVORE FALSE
		// ?PRODUCER FALSE ?MATE-PRODUCER TRUE ?MATE-ALIVE FALSE ?NEXT-TO-MATE TRUE", "... ?PRODUCER FALSE ?HERBIVORE FALSE ?MATE-PRODUCER FALSE ?MATE-ALIVE FALSE
		// ?NEXT-TO-MATE TRUE"
		var eatsPlant = !carnivore && !producer && mateProducer && ( ( m_attackedMate && !mateAttacking ) || !mateAlive );
		var eatsCarcass = !producer && !herbivore && !mateProducer && !mateAlive;

		if ( nextToMate && ( eatsPlant || eatsCarcass ) )
		{
			m_behaviour = Behaviour.Other;

			if ( Random.Range( 1, 101 ) < c_eatChance )
			{
				EatMate();
			}

			return true;
		}

		// "APPROACH-ITS-MATE <-- ?MOBILE TRUE ?ALIVE TRUE ?STUNNED FALSE (the mate it hunts) ?NEXT-TO-MATE FALSE ?MATE-ON-GROUND TRUE ?HAS-MATE TRUE"
		if ( mobile && hunts && !nextToMate && mateOnGround && ( mateDistance > 0.01f ) )
		{
			m_moveDirection = toMate / mateDistance;
			m_moving = true;
			m_behaviour = Behaviour.ApproachingMate;

			return true;
		}

		// "FLEE-ITS-MATE <-- ?MOBILE TRUE ?ALIVE TRUE ?STUNNED FALSE ?MATE-APPROACHING TRUE", "... ?MATE-ATTACKING TRUE ?AGGR-ENOUGH FALSE ?CAN-BREAK-AWAY TRUE"
		if ( mobile && ( mateApproaching || ( mateAttacking && !aggressiveEnough && ( Random.value < c_breakAwayChance ) ) ) && ( mateDistance > 0.01f ) )
		{
			m_moveDirection = -toMate / mateDistance;
			m_moving = true;
			m_behaviour = Behaviour.FleeingMate;

			return true;
		}

		// "ATTACK-ITS-MATE <-- (the mate it hunts) ?NEXT-TO-MATE TRUE ?MATE-ALIVE TRUE", "... ?MATE-ATTACKING TRUE ?AGGR-ENOUGH TRUE"
		if ( ( hunts && nextToMate && mateAlive ) || ( mateAttacking && aggressiveEnough ) )
		{
			AttackMate();

			return true;
		}

		return false;
	}

	// "ATTACK-ITS-MATE : Sets moving flag to No, and Attacking flag to Yes." - and its mate's status check, "REDUCE-HITS-BY-1-TO-40 <-- ?MATE-IS-ATTACKING
	// ?HITS>0 TRUE", is done here, since a stunned mate has no behaviour step of its own
	void AttackMate()
	{
		m_moving = false;
		m_behaviour = Behaviour.AttackingMate;
		m_attackedMate = true;

		if ( ( m_mate.m_status != Status.Dead ) && ( m_mate.m_hits > 0 ) )
		{
			m_mate.m_hits -= Random.Range( 1, 41 );

			m_mate.UpdateStatus();
		}
	}

	// "the mate is eaten and it dissappears", and "BECOME-UNPAIRED <-- ?HAS-MATE TRUE ?MATE-JUST-EATEN TRUE"
	void EatMate()
	{
		var mate = m_mate;

		BecomeUnpaired();

		if ( m_populator != null )
		{
			m_populator.RemoveEaten( mate );
		}
	}

	// the notes' ground-air transitions (lf-behav.txt), after each step of the behaviour: a flyer that moves takes to the air, a floater that moves floats up (a
	// floating plant always, a floating animal when it wants to), and one in the air comes down where it may when it wanders and wants to, or when it attacks
	// its mate or the terrain vehicle (a dead or stunned one comes down at once, see UpdateStatus)
	void UpdateAirGround()
	{
		if ( !IsAirborne() )
		{
			// "FLY <-- ?MOVING TRUE ?ON-GROUND TRUE ?CAN-FLY TRUE ?WANTS-TO-FLY TRUE"
			if ( m_moving && m_species.m_canFly && ( Random.value < c_wantsToFlyChance ) )
			{
				m_flying = true;
			}
			// "FLOAT <-- ?MOVING TRUE ?ON-GROUND TRUE ?CAN-FLOAT TRUE ?PRODUCER TRUE", "FLOAT <-- ?MOVING TRUE ?ON-GROUND TRUE ?CAN-FLOAT TRUE ?PRODUCER FALSE
			// ?WANTS-TO-FLOAT TRUE"
			else if ( m_moving && m_species.m_canFloat && ( m_species.IsPlant() || ( Random.value < c_wantsToFloatChance ) ) )
			{
				m_floating = true;
			}

			return;
		}

		// "LAND <-- ?ON-GROUND FALSE ?WANDERING TRUE ?WANTS-TO-LAND TRUE ?OK-TO-LAND TRUE", "LAND <-- ?ON-GROUND FALSE ?ATTACKING TRUE ?OK-TO-LAND TRUE"
		var attacking = ( m_behaviour == Behaviour.AttackingMate ) || ( m_behaviour == Behaviour.AttackingPlayer );
		var wantsToLand = ( m_behaviour == Behaviour.Wandering ) && ( Random.value < c_wantsToLandChance );

		if ( ( attacking || wantsToLand ) && IsOkToLand() )
		{
			Land();
		}
	}

	// "?OK-TO-LAND : Fasle if the square the alien is on is liquid, lava, or occupied by another alien or the player." - another lifeform on the ground, alive or
	// not, or the terrain vehicle
	bool IsOkToLand()
	{
		if ( ( m_populator == null ) || !m_populator.GetGround( transform.position, out _ ) )
		{
			return false;
		}

		var vehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( ( vehicle != null ) && IsInSquareOf( vehicle.transform.position ) )
		{
			return false;
		}

		foreach ( var other in m_populator.m_lifeformList )
		{
			if ( ( other != null ) && ( other != this ) && !other.IsAirborne() && IsInSquareOf( other.transform.position ) )
			{
				return false;
			}
		}

		return true;
	}

	// true if this place is in the square this lifeform is on
	bool IsInSquareOf( Vector3 position )
	{
		var delta = position - transform.position;

		delta.y = 0.0f;

		return delta.magnitude < c_squareTaken;
	}

	// "LAND : Sets current movment mode to ground." - it comes down where it is, onto the ground or the surface of a liquid
	void Land()
	{
		m_flying = false;
		m_floating = false;

		if ( ( m_populator != null ) && m_populator.GetSurface( transform.position, out var height ) )
		{
			var position = transform.position;

			position.y = height + m_groundOffset;

			transform.position = position;
		}
	}

	// moves on: a lifeform on the ground keeps to dry ground (another direction where the way is wet, "Aliens in the air do not need to make any of these
	// checks"), and a flyer in the air goes over anything
	void Move( float deltaTime )
	{
		var populator = m_populator;

		if ( populator == null )
		{
			return;
		}

		var next = transform.position + m_moveDirection * m_species.m_movementRate * c_speedPerMovementRate * deltaTime;

		if ( !populator.GetGround( next, out var height ) && !IsAirborne() )
		{
			// the way is wet: turn
			m_moveDirection = Quaternion.Euler( 0.0f, 90.0f, 0.0f ) * m_moveDirection;

			return;
		}

		next.y = height + m_groundOffset + ( m_flying ? c_flyingHeight : ( m_floating ? c_floatingHeight : 0.0f ) );

		transform.position = next;

		// it faces the way it goes
		if ( m_moveDirection.sqrMagnitude > 0.0001f )
		{
			transform.rotation = Quaternion.Slerp( transform.rotation, Quaternion.LookRotation( m_moveDirection, Vector3.up ), Mathf.Clamp01( deltaTime * c_turnRate ) );
		}
	}

	// "ATTACK-PLAYER : Determine whether or not alien hits or misses. Distribute any damage, and print out result-phrase." - one to three attacks (70%, 25%,
	// 5%), each on a crew member at random, a hit if a number from 1 to 100 is under the average of its aggression and intelligence, for damage of up to
	// (aggression + 2 x hit points) / 3 / the crew member's durability
	// true if the terrain vehicle is shielded by the Flat Device, in its hold or the ship's (the Starport's analysis: "This device will project a shield around
	// your terrain vehicle which makes it difficult for dangerous lifeforms to attack you"; the 1986 kernel's ?FLAT, "TRUE if TV has flat device")
	static bool TerrainVehicleIsShielded()
	{
		var playerData = DataController.m_instance.m_playerData;

		return playerData.m_playerShip.HasArtifact( "Flat Device" ) || ( ( playerData.m_terrainVehicle != null ) && playerData.m_terrainVehicle.HasArtifact( "Flat Device" ) );
	}

	// how much harder the Flat Device makes it for a lifeform to hit: the original multiplies its hit roll of 0 to 99 by 10 when the terrain vehicle has the
	// Flat Device (BEHAV-OV WE8C2, ?FLAT), so a lifeform hits about a tenth as often
	const int c_flatDeviceRollFactor = 10;

	void AttackPlayer()
	{
		m_lastAttackTime = Time.time;

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

		var shielded = TerrainVehicleIsShielded();

		SoundController.m_instance.PlaySound( SoundController.Sound.Error );

		for ( var i = 0; i < attacks; i++ )
		{
			var victim = crew[ Random.Range( 0, crew.Count ) ];
			var phrase = "The " + m_species.GetBodyPlanText().ToLower() + " lifeform attacks " + victim.m_name;

			var hitChance = ( m_aggression + m_species.m_intelligence ) / 2;
			var hitRoll = Random.Range( 1, 101 );

			if ( hitRoll >= hitChance )
			{
				messages.AddText( "<color=white>" + phrase + " and misses.</color>" );

				continue;
			}

			// the Flat Device's shield turns away the attacks whose roll from 0 to 99, times 10, is not under the chance (BEHAV-OV WE8C2)
			if ( shielded && ( ( hitRoll - 1 ) * c_flatDeviceRollFactor >= hitChance ) )
			{
				messages.AddText( "<color=white>" + phrase + ", but the shield turns it away.</color>" );

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
		if ( ( m_renderers == null ) || ( m_species == null ) )
		{
			return;
		}

		var speciesColour = new Color( m_species.m_red * brightness, m_species.m_green * brightness, m_species.m_blue * brightness );
		var detailColour = new Color( brightness, brightness, brightness );

		foreach ( var renderer in m_renderers )
		{
			if ( renderer == null )
			{
				continue;
			}

			var materials = renderer.sharedMaterials;

			for ( var index = 0; index < materials.Length; index++ )
			{
				var material = materials[ index ];

				if ( material == null )
				{
					continue;
				}

				// a model's materials (SF - Standard): the skin takes the species colour, the details only the brightness
				if ( material.HasProperty( "SF_AlbedoColor" ) )
				{
					renderer.GetPropertyBlock( m_propertyBlock, index );

					m_propertyBlock.SetColor( "SF_AlbedoColor", material.name.EndsWith( " Skin" ) ? speciesColour : detailColour );

					renderer.SetPropertyBlock( m_propertyBlock, index );
				}
				else
				{
					// a plain shape
					renderer.material.color = speciesColour;
				}
			}
		}
	}

	// puts it back on the ground after it has turned over: the bottom of what is drawn of it on what is under it
	void Settle()
	{
		if ( ( m_populator == null ) || ( m_renderers == null ) || ( m_renderers.Length == 0 ) )
		{
			return;
		}

		// (on the ground, or the surface of a liquid)
		if ( !m_populator.GetSurface( transform.position, out var height ) )
		{
			return;
		}

		var bounds = new Bounds( transform.position, Vector3.zero );
		var any = false;

		foreach ( var renderer in m_renderers )
		{
			if ( renderer == null )
			{
				continue;
			}

			if ( any )
			{
				bounds.Encapsulate( renderer.bounds );
			}
			else
			{
				bounds = renderer.bounds;
				any = true;
			}
		}

		if ( any )
		{
			transform.position += Vector3.up * ( height - bounds.min.y );
		}
	}
}
