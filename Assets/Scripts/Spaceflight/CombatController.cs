
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages combat operations including weapon firing, damage calculation,
/// and visual effects pooling.
/// </summary>
public class CombatController : MonoBehaviour
{
	// singleton instance
	public static CombatController m_instance;

	// effect pool sizes
	const int c_laserPoolSize = 4;
	const int c_missilePoolSize = 8;
	const int c_explosionPoolSize = 4;
	const int c_shieldHitPoolSize = 4;
	const int c_hullHitPoolSize = 8;

	// effect pools
	List<LaserBeam> m_laserPool;
	List<MissileProjectile> m_missilePool;
	List<ExplosionEffect> m_explosionPool;
	List<ShieldHitEffect> m_shieldHitPool;
	List<HullHitEffect> m_hullHitPool;

	// cooldown timers
	float m_playerLaserCooldown;
	float m_playerMissileCooldown;

	// cooldown durations (based on weapon class)
	const float c_baseLaserCooldown = 1.0f;
	const float c_baseMissileCooldown = 2.0f;

	// the damage of a laser and of a missile by its class, the original's (DAMAGE-OV LDAMAGE and MDAMAGE): the same for the player's weapons and the aliens', and
	// a missile of class 6 is the plasma bolt
	static readonly int[] c_laserDamage = { 0, 90, 210, 360, 510, 660 };
	static readonly int[] c_missileDamage = { 0, 200, 400, 700, 1000, 1500, 4000 };
	const int c_plasmaBoltClass = 6;

	// how many of the port's units in an encounter are one cell of the original's combat map: a laser's damage falls to nothing at 10 cells (DAMAGE-OV WED12), and
	// the port's laser reaches 800 units, so a cell is 80 (the port's encounters are larger than the original's, so this keeps the original's proportions)
	public const float c_unitsPerCell = 80.0f;

	// a laser's damage at a distance (DAMAGE-OV WED12): (10 - cells) / 10 of its class's damage, the cells counted whole as the original's square root does, and
	// nothing at 10 cells and beyond
	public static int GetLaserDamage( int laserClass, float distance )
	{
		var damage = c_laserDamage[ Mathf.Clamp( laserClass, 0, c_laserDamage.Length - 1 ) ];
		var cells = Mathf.FloorToInt( Mathf.Max( 0.0f, distance ) / c_unitsPerCell );

		return Mathf.Max( 0, ( 10 - cells ) * damage / 10 );
	}

	// an alien laser's damage to the player ship (DAMAGE-OV WF3EA and WED36): a laser's damage at that distance, a quarter of it with the Rod Device aboard, then
	// twice that
	public static int GetAlienLaserDamage( int laserClass, float distance, bool rodDevice )
	{
		var damage = GetLaserDamage( laserClass, distance );

		if ( rodDevice )
		{
			damage /= 4;
		}

		return damage * 2;
	}

	// a missile's damage, whatever the distance (DAMAGE-OV WF23A and WF47F): class 6 is the plasma bolt
	public static int GetMissileDamage( int missileClass )
	{
		return c_missileDamage[ Mathf.Clamp( missileClass, 0, c_missileDamage.Length - 1 ) ];
	}

	// weapon ranges
	const float c_laserRange = 800.0f;
	const float c_missileRange = 1500.0f;

	// endurium used up by every shot, in cubic meters: a tenth for a laser and half a cubic meter for a missile (COMBAT-OV WF1DC and WF1B6, WF058)
	const float c_laserFuelPerShot = 0.1f;
	const float c_missileFuelPerShot = 0.5f;

	// the navigator's aim (COMBAT-OV WF0D8 and WF0B6): each axis of a shot is off by whole cells of the original's combat map, a roll from 0 to 249 less the
	// navigator's skill over 50 (either way), so a shot is on target with the chance (50 / (250 - skill)) squared, and always from skill 200 - returns how many
	// cells it is off across and along
	public static Vector2Int RollAimOffset( int navigationSkill )
	{
		var spread = Mathf.Max( 1, 250 - Mathf.Clamp( navigationSkill, 0, 250 ) );

		var across = UnityEngine.Random.Range( 0, spread ) / 50;
		var along = UnityEngine.Random.Range( 0, spread ) / 50;

		if ( UnityEngine.Random.Range( 0, 2 ) == 0 )
		{
			across = -across;
		}

		if ( UnityEngine.Random.Range( 0, 2 ) == 0 )
		{
			along = -along;
		}

		return new Vector2Int( across, along );
	}

	// the skill the aim comes from: the navigator's, none without a navigator who can work
	public static int GetNavigatorSkill()
	{
		var crewAssignment = DataController.m_instance.m_playerData.m_crewAssignment;

		if ( !crewAssignment.IsAssigned( PD_CrewAssignment.Role.Navigator ) )
		{
			return 0;
		}

		var navigator = crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Navigator );

		return ( ( navigator != null ) && !navigator.m_isDead && ( navigator.m_vitality > 0.0f ) ) ? navigator.m_navigation : 0;
	}

	// current target
	int m_currentTargetIndex = -1;

	// true once the player ship has been destroyed (it can only be destroyed once)
	bool m_playerIsDestroyed;

	// what destroyed the player ship, when it was not the damage it took (null for that) - the game over screen says it
	string m_gameOverCause;

	// the Shimmering Ball cloaks the ship in combat, and a shot of the player drops the cloak for a while (decided 2026-10-10; how long is the port's
	// choice) - the time until it cloaks the ship again
	const float c_uncloakedTime = 3.0f;
	float m_uncloakedTimer;

	// player debris template (assign in inspector)
	public GameObject m_playerDebrisTemplate;

	// unity awake
	void Awake()
	{
		m_instance = this;
	}

	// unity start
	void Start()
	{
		InitializeEffectPools();
	}

	// ensure pools are initialized
	void EnsurePoolsInitialized()
	{
		if ( m_explosionPool == null )
		{
			InitializeEffectPools();
		}
	}

	// initialize the effect pools
	void InitializeEffectPools()
	{
		// create container
		var effectContainer = new GameObject( "Combat Effects" );
		effectContainer.transform.SetParent( transform );

		// laser pool
		m_laserPool = new List<LaserBeam>();
		for ( int i = 0; i < c_laserPoolSize; i++ )
		{
			var laserObject = new GameObject( $"Laser_{i}" );
			laserObject.transform.SetParent( effectContainer.transform );
			m_laserPool.Add( laserObject.AddComponent<LaserBeam>() );
		}

		// missile pool
		m_missilePool = new List<MissileProjectile>();
		for ( int i = 0; i < c_missilePoolSize; i++ )
		{
			var missileObject = new GameObject( $"Missile_{i}" );
			missileObject.transform.SetParent( effectContainer.transform );
			m_missilePool.Add( missileObject.AddComponent<MissileProjectile>() );
		}

		// explosion pool
		m_explosionPool = new List<ExplosionEffect>();
		for ( int i = 0; i < c_explosionPoolSize; i++ )
		{
			var explosionObject = new GameObject( $"Explosion_{i}" );
			explosionObject.transform.SetParent( effectContainer.transform );
			m_explosionPool.Add( explosionObject.AddComponent<ExplosionEffect>() );
		}

		// shield hit pool
		m_shieldHitPool = new List<ShieldHitEffect>();
		for ( int i = 0; i < c_shieldHitPoolSize; i++ )
		{
			var shieldObject = new GameObject( $"ShieldHit_{i}" );
			shieldObject.transform.SetParent( effectContainer.transform );
			m_shieldHitPool.Add( shieldObject.AddComponent<ShieldHitEffect>() );
		}

		// hull hit pool
		m_hullHitPool = new List<HullHitEffect>();
		for ( int i = 0; i < c_hullHitPoolSize; i++ )
		{
			var hullObject = new GameObject( $"HullHit_{i}" );
			hullObject.transform.SetParent( effectContainer.transform );
			m_hullHitPool.Add( hullObject.AddComponent<HullHitEffect>() );
		}
	}

	// unity update
	void Update()
	{
		// update cooldowns
		if ( m_playerLaserCooldown > 0.0f )
		{
			m_playerLaserCooldown -= Time.deltaTime;
		}

		if ( m_playerMissileCooldown > 0.0f )
		{
			m_playerMissileCooldown -= Time.deltaTime;
		}

		if ( m_uncloakedTimer > 0.0f )
		{
			m_uncloakedTimer -= Time.deltaTime;
		}
	}

	// true while the Shimmering Ball cloaks the ship: "this artifact seems to be a cloaking device. As far as we can tell it should operate automatically during
	// combat" (the Starport's analysis) - the aliens cannot fire at a ship they cannot see
	public bool PlayerIsCloaked()
	{
		return ( m_uncloakedTimer <= 0.0f ) && DataController.m_instance.m_playerData.m_playerShip.HasArtifact( "Shimmering Ball" );
	}

	/// <summary>
	/// Set the current target for combat.
	/// </summary>
	public void SetTarget( int targetIndex )
	{
		m_currentTargetIndex = targetIndex;
	}

	/// <summary>
	/// Take every missile out of the air. The encounter calls this when it begins and when it ends, because a missile
	/// that arrives after the player has left would still do its damage, to the player or to a ship of the next encounter.
	/// </summary>
	public void ClearMissiles()
	{
		// the pools may not have been created yet
		if ( m_missilePool == null )
		{
			return;
		}

		foreach ( var missile in m_missilePool )
		{
			missile.Cancel();
		}

		// a missile that is cancelled never calls back, so none of the aliens' is in the air any more
		m_alienMissilesInFlight = 0;
	}

	// the aliens' missiles in the air, and how many there can be (COMBAT-OV WDFD2: fewer than 10 for another to be launched)
	int m_alienMissilesInFlight;
	const int c_maximumAlienMissilesInFlight = 10;

	// how near the place an alien missile was aimed at the ship has to be for it to be hit (the port's map cell)
	const float c_alienMissileHitRadius = 100.0f;

	/// <summary>
	/// True once the player ship has been destroyed (its explosion may still be playing, and the game over screen follows it).
	/// </summary>
	public bool PlayerIsDestroyed()
	{
		return m_playerIsDestroyed;
	}

	/// <summary>
	/// Get the current target index.
	/// </summary>
	public int GetTargetIndex()
	{
		return m_currentTargetIndex;
	}

	/// <summary>
	/// Get the current target if it is a living ship in the current encounter.
	/// Otherwise the target is forgotten and this returns null.
	/// </summary>
	PD_AlienShip GetValidTarget()
	{
		// get the alien ships in the current encounter
		var encounter = SpaceflightController.m_instance.m_encounter;
		var alienShipList = ( encounter.m_pdEncounter != null ) ? encounter.m_pdEncounter.GetAlienShipList() : null;

		// is the target one of them?
		if ( ( alienShipList != null ) && ( m_currentTargetIndex >= 0 ) && ( m_currentTargetIndex < alienShipList.Length ) )
		{
			var targetShip = alienShipList[ m_currentTargetIndex ];

			// is it alive and in the encounter?
			if ( !targetShip.m_isDead && targetShip.m_addedToEncounter )
			{
				// yes
				return targetShip;
			}
		}

		// no - forget the target and let the player know
		m_currentTargetIndex = -1;

		SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Target lost. Select a new target.</color>" );

		return null;
	}

	/// <summary>
	/// Check if player can fire laser.
	/// </summary>
	public bool CanFireLaser()
	{
		var playerData = DataController.m_instance.m_playerData;

		// a ship that has been destroyed does not fire
		if ( m_playerIsDestroyed )
		{
			return false;
		}

		// must have laser cannon (and one that has not been destroyed)
		if ( ( playerData.m_playerShip.m_laserCannonClass <= 0 ) || ( playerData.m_playerShip.GetSystemHealth( PD_PlayerShip.ShipSystem.Lasers ) <= 0 ) )
		{
			return false;
		}

		// must have fuel (every shot uses a little endurium)
		if ( !playerData.m_playerShip.HasFuel() )
		{
			return false;
		}

		// must be off cooldown
		if ( m_playerLaserCooldown > 0.0f )
		{
			return false;
		}

		// must have target
		if ( m_currentTargetIndex < 0 )
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// Check if player can fire missile.
	/// </summary>
	public bool CanFireMissile()
	{
		var playerData = DataController.m_instance.m_playerData;

		// a ship that has been destroyed does not fire
		if ( m_playerIsDestroyed )
		{
			return false;
		}

		// must have missile launcher (and one that has not been destroyed)
		if ( ( playerData.m_playerShip.m_missileLauncherClass <= 0 ) || ( playerData.m_playerShip.GetSystemHealth( PD_PlayerShip.ShipSystem.Missiles ) <= 0 ) )
		{
			return false;
		}

		// must have fuel (missiles are not counted - every launch uses a little endurium instead)
		if ( !playerData.m_playerShip.HasFuel() )
		{
			return false;
		}

		// must be off cooldown
		if ( m_playerMissileCooldown > 0.0f )
		{
			return false;
		}

		// must have target
		if ( m_currentTargetIndex < 0 )
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// Fire player's laser at current target.
	/// </summary>
	public bool FirePlayerLaser()
	{
		if ( !CanFireLaser() )
		{
			return false;
		}

		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;

		// get target alien ship (it has to be a living ship in this encounter)
		var targetShip = GetValidTarget();

		if ( targetShip == null )
		{
			return false;
		}

		var targetVessel = gameData.m_vesselList[ targetShip.m_vesselId ];

		// check range
		var playerPosition = playerData.m_general.m_coordinates;
		var targetPosition = targetShip.m_coordinates;
		var distance = Vector3.Distance( playerPosition, targetPosition );

		if ( distance > c_laserRange )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Target out of laser range.</color>" );
			return false;
		}

		// firing on the aliens makes them hostile for the rest of the encounter (whether or not the shot does any damage)
		SpaceflightController.m_instance.m_encounter.PlayerAttacked();

		// and it shows them where the ship is, cloaked or not
		m_uncloakedTimer = c_uncloakedTime;

		// check if target is immune
		if ( targetVessel.m_immuneToLasers )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Target is immune to lasers!</color>" );
			// still fire visually but no damage
		}

		// where the shot goes: the navigator's aim can put it whole cells off the target, and then it misses (the original's laser hits only what is in the cell it is
		// aimed at)
		var aim = RollAimOffset( GetNavigatorSkill() );
		var onTarget = ( aim == Vector2Int.zero );
		var aimPosition = targetPosition + new Vector3( aim.x, 0.0f, aim.y ) * c_unitsPerCell;

		// fire the laser
		var laser = GetAvailableLaser();
		if ( laser != null )
		{
			// set color based on weapon class
			var laserColor = GetLaserColor( playerData.m_playerShip.m_laserCannonClass );
			laser.SetColor( laserColor );
			laser.Fire( playerPosition, aimPosition );
		}

		// apply damage if on target and not immune
		if ( onTarget && !targetVessel.m_immuneToLasers )
		{
			// the laser's damage falls off with the distance to the target
			int damage = GetLaserDamage( playerData.m_playerShip.m_laserCannonClass, Vector3.Distance( playerPosition, targetPosition ) );

			// a damaged laser cannon does damage in proportion to its health
			damage = Mathf.Max( 1, Mathf.RoundToInt( damage * playerData.m_playerShip.GetSystemFactor( PD_PlayerShip.ShipSystem.Lasers ) ) );

			ApplyDamageToAlien( m_currentTargetIndex, damage );
		}

		// the shot uses up a little endurium
		playerData.m_playerShip.UseUpFuel( c_laserFuelPerShot );

		// set cooldown
		m_playerLaserCooldown = c_baseLaserCooldown / ( 1.0f + playerData.m_playerShip.m_laserCannonClass * 0.2f );

		// play phaser sound
		SoundController.m_instance.PlaySound( SoundController.Sound.PhaserFire );

		return true;
	}

	/// <summary>
	/// Fire player's missile at current target.
	/// </summary>
	public bool FirePlayerMissile()
	{
		if ( !CanFireMissile() )
		{
			return false;
		}

		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;

		// get target (it has to be a living ship in this encounter)
		var encounter = SpaceflightController.m_instance.m_encounter;
		var targetShip = GetValidTarget();

		if ( targetShip == null )
		{
			return false;
		}

		var targetVessel = gameData.m_vesselList[ targetShip.m_vesselId ];

		// check range
		var playerPosition = playerData.m_general.m_coordinates;
		var targetPosition = targetShip.m_coordinates;
		var distance = Vector3.Distance( playerPosition, targetPosition );

		if ( distance > c_missileRange )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Target out of missile range.</color>" );
			return false;
		}

		// is there a missile to launch? (the aliens' missiles come out of the same pool, and all of them can be in the air)
		var missile = GetAvailableMissile();

		if ( missile == null )
		{
			// no - nothing is launched, so it costs no fuel and the aliens have nothing to take offence at
			return false;
		}

		// firing on the aliens makes them hostile for the rest of the encounter (whether or not the missile hits)
		encounter.PlayerAttacked();

		// and it shows them where the ship is, cloaked or not
		m_uncloakedTimer = c_uncloakedTime;

		// the launch uses up a little endurium
		playerData.m_playerShip.UseUpFuel( c_missileFuelPerShot );

		// check if target is immune
		bool isImmune = targetVessel.m_immuneToMissiles;
		if ( isImmune )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Target is immune to missiles!</color>" );
		}

		// where the missile goes: the navigator's aim can put it whole cells off the target, and then it flies to that place and misses (the original's missile flies
		// along the line to the place it is aimed at)
		var aim = RollAimOffset( GetNavigatorSkill() );
		var onTarget = ( aim == Vector2Int.zero );
		var aimPosition = targetPosition + new Vector3( aim.x, 0.0f, aim.y ) * c_unitsPerCell;

		// fire the missile - find target model (its model slot is not the same as the target index, so ask the encounter for it)
		var targetModel = encounter.GetAlienShipModel( m_currentTargetIndex );
		int targetIndex = m_currentTargetIndex;
		int damage = GetMissileDamage( Mathf.Min( playerData.m_playerShip.m_missileLauncherClass, c_plasmaBoltClass - 1 ) );

		// a damaged missile launcher's missiles do damage in proportion to its health
		damage = Mathf.Max( 1, Mathf.RoundToInt( damage * playerData.m_playerShip.GetSystemFactor( PD_PlayerShip.ShipSystem.Missiles ) ) );

		// this is called when the missile reaches the target
		System.Action<Vector3, bool> onMissileHit = ( hitPosition, didHit ) =>
		{
			if ( didHit && onTarget && !isImmune )
			{
				ApplyDamageToAlien( targetIndex, damage );
			}

			// play explosion
			var explosion = GetAvailableExplosion();
			if ( explosion != null )
			{
				explosion.SetScale( 0.5f );
				explosion.Play( hitPosition );
			}

			// play torpedo explosion sound
			SoundController.m_instance.PlaySound( SoundController.Sound.TorpedoExplosion );
		};

		if ( !onTarget )
		{
			// off target: it flies to where it was aimed
			missile.Fire( playerPosition, aimPosition, onMissileHit );
		}
		else if ( targetModel != null )
		{
			// home in on the target model
			missile.Fire( playerPosition, targetModel.transform, onMissileHit );
		}
		else
		{
			// the target has no model so fly to its current position instead
			missile.Fire( playerPosition, targetPosition, onMissileHit );
		}

		// set cooldown
		m_playerMissileCooldown = c_baseMissileCooldown / ( 1.0f + playerData.m_playerShip.m_missileLauncherClass * 0.15f );

		// play torpedo launch sound
		SoundController.m_instance.PlaySound( SoundController.Sound.TorpedoFire );

		// (the original says nothing of the player's own launches and hits: COMBAT-OV)

		return true;
	}

	/// <summary>
	/// Apply damage to an alien ship.
	/// </summary>
	void ApplyDamageToAlien( int alienIndex, int damage )
	{
		var encounter = SpaceflightController.m_instance.m_encounter;
		var alienShipList = encounter.m_pdEncounter.GetAlienShipList();

		if ( alienIndex >= alienShipList.Length )
		{
			return;
		}

		var targetShip = alienShipList[ alienIndex ];

		// already dead?
		if ( targetShip.m_isDead )
		{
			return;
		}

		var gameData = DataController.m_instance.m_gameData;
		var vessel = gameData.m_vesselList[ targetShip.m_vesselId ];

		// make sure the ship has its hit points (ships from save files made before alien ships had hit points don't)
		targetShip.EnsureHitPoints( vessel );

		// the shields take the whole hit if they have points left, are up and the fight is not in a nebula, and the armor takes it otherwise: never both, and the
		// shields do not go below 0 (DAMAGE-OV WF162: WEB40 @ 0= ?A-SHIELDS-UP 0= OR ?NEB @ OR)
		var shieldsTakeTheHit = ( targetShip.m_shieldPoints > 0 ) && encounter.AliensHaveShieldsUp() && !SpaceflightController.m_instance.ShipIsInsideANebula();

		if ( shieldsTakeTheHit )
		{
			targetShip.m_shieldPoints = Mathf.Max( 0, targetShip.m_shieldPoints - damage );
		}
		else
		{
			targetShip.m_armorPoints -= damage;
		}

		// show hit effect
		if ( targetShip.m_armorPoints > 0 )
		{
			var hullHit = GetAvailableHullHit();
			if ( hullHit != null )
			{
				// the player's shot came from the player ship, so the sparks fly back that way
				hullHit.Play( targetShip.m_coordinates, targetShip.m_coordinates - DataController.m_instance.m_playerData.m_general.m_coordinates );
			}

			// (the original says nothing of the player's own hits: the hit effects show whether the shields took it)
		}
		else
		{
			// ship destroyed!
			targetShip.m_armorPoints = 0;
			targetShip.m_isDead = true;

			// show explosion
			var explosion = GetAvailableExplosion();
			if ( explosion != null )
			{
				explosion.Play( targetShip.m_coordinates );
			}

			// play ship explosion sound
			SoundController.m_instance.PlaySound( SoundController.Sound.ShipExplosion );

			// replace ship model with debris
			encounter.SpawnDebrisForShip( alienIndex, targetShip.m_vesselId, targetShip.m_coordinates );

			SpaceflightController.m_instance.m_messages.AddText( $"<color=#00FF00>{vessel.m_name} destroyed!</color>" );

			// clear target
			m_currentTargetIndex = -1;
		}
	}

	// a hit on the player ship, as the original's DAMAGE-OV (WF3A4): it lands in chunks, each a random share of what is left of it and at least 10 points (WF358),
	// and each chunk on one thing (WF378): on the shields while they are up (raised, charged and outside a nebula, WEC8A), else on the armor while it has points,
	// and once the armor is gone on one of the ship's working parts, chosen at random (DamageAWorkingPart). Better shields and armor lose less of a chunk, (6 -
	// class) / 5 of it (WF248, WF26A). The ship is destroyed when its hull's integrity reaches 0 ("!!HULL DESTROYED!!"), not when its armor does
	public void ApplyDamageToPlayer( int damage, Vector3 hitDirection )
	{
		// a ship that has been destroyed cannot be hit again (it would explode once more and call the game over screen a second time)
		if ( m_playerIsDestroyed || ( damage <= 0 ) )
		{
			return;
		}

		var playerData = DataController.m_instance.m_playerData;
		var playerPosition = playerData.m_general.m_coordinates;
		var ship = playerData.m_playerShip;

		var shieldsWereHit = false;
		var shipWasHit = false;

		// chunk by chunk until the hit is spent, the hull is gone or the game is lost (a chunk takes at least 10 points off what is left, so this ends - the limit is
		// a guard)
		var remaining = damage;

		for ( var chunks = 0; ( remaining > 0 ) && !ship.IsDestroyed() && !m_playerIsDestroyed && ( chunks < c_maximumChunksPerHit ); chunks++ )
		{
			// a random share of what is left, at least 10 points (WF358: RRND from 1 to all of it, MAX 10)
			var chunk = Mathf.Max( UnityEngine.Random.Range( 1, remaining + 1 ), c_leastChunk );

			remaining = Mathf.Max( remaining - chunk, 0 );

			if ( ship.m_shieldsAreUp && ( ship.m_shieldPoints > 0 ) && !SpaceflightController.m_instance.ShipIsInsideANebula() )
			{
				// the shields take it (lowered shields keep their charge, so they have to be up to take anything)
				ship.m_shieldPoints = Mathf.Max( ship.m_shieldPoints - chunk * ( 6 - ship.m_shieldingClass ) / 5, 0 );

				shieldsWereHit = true;
			}
			else if ( ship.m_armorPoints > 0 )
			{
				// the armor takes it (a ship without armor plating has the armor of its bare hull, class 0)
				ship.m_armorPoints = Mathf.Max( ship.m_armorPoints - chunk * ( 6 - ship.m_armorClass ) / 5, 0 );

				shipWasHit = true;
			}
			else
			{
				// the armor is gone: the hull, a system or the crew takes it
				DamageAWorkingPart( chunk );

				shipWasHit = true;
			}
		}

		// the whole crew has died (the game is over already, PD_CrewAssignment.InjureCrewMember has ended it)
		if ( m_playerIsDestroyed )
		{
			return;
		}

		if ( shieldsWereHit )
		{
			// show shield effect
			var shieldHit = GetAvailableShieldHit();
			if ( shieldHit != null )
			{
				shieldHit.Play( playerPosition, hitDirection );
			}

			// play shield hit sound
			SoundController.m_instance.PlaySound( SoundController.Sound.ShieldHit );
		}

		if ( !shipWasHit )
		{
			return;
		}

		// show hull hit
		var hullHit = GetAvailableHullHit();
		if ( hullHit != null )
		{
			hullHit.Play( playerPosition, hitDirection );
		}

		// play red alert if armor is critically low (below 25% of the most this ship can have)
		int maxArmor = ship.GetMaximumArmorPoints();
		if ( ship.m_armorPoints > 0 && ship.m_armorPoints * 4 < maxArmor )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.RedAlert );
			SpaceflightController.m_instance.m_messages.AddText( "<color=#FFA500>WARNING: Hull breach imminent!</color>" );
		}

		// a critically wounded ship is warped out of danger by the Crystal Pearl
		if ( WarpOutWithTheCrystalPearl() )
		{
			return;
		}

		// check for destruction
		if ( ship.IsDestroyed() )
		{
			DestroyPlayer( playerPosition );
		}
	}

	// the least a chunk of a hit is, and the most chunks a hit can have (a guard: a chunk is at least 10 points, so even the plasma bolt's 4000 is 400 chunks)
	const int c_leastChunk = 10;
	const int c_maximumChunksPerHit = 1000;

	// the parts of the ship a chunk can land on once the armor is gone, in the original's order (DAMAGE-OV #>?FIEL and #>DAMAG: the hull, the engines, the
	// sensors, the comm, the missile launcher, the laser cannon, the crew - ShipSystem.Count here - and the shield generator)
	static readonly PD_PlayerShip.ShipSystem[] c_damageableParts =
	{
		PD_PlayerShip.ShipSystem.Hull,
		PD_PlayerShip.ShipSystem.Engines,
		PD_PlayerShip.ShipSystem.Sensors,
		PD_PlayerShip.ShipSystem.Comm,
		PD_PlayerShip.ShipSystem.Missiles,
		PD_PlayerShip.ShipSystem.Lasers,
		PD_PlayerShip.ShipSystem.Count,
		PD_PlayerShip.ShipSystem.Shields
	};

	// a chunk that got past the shields and the armor lands on one of the ship's working parts, chosen at random from those that still work (DAMAGE-OV WEF3C and
	// WEF76): the hull or a system loses 10 times the chunk over the hull's integrity (PD_PlayerShip.DamageSystem), and the crew is wounded by as much (WF0E0)
	void DamageAWorkingPart( int chunk )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var crewIsAlive = GetLivingCrew().Count > 0;

		// which parts still work
		var working = 0;

		foreach ( var part in c_damageableParts )
		{
			if ( ( part == PD_PlayerShip.ShipSystem.Count ) ? crewIsAlive : ship.CanBeDamaged( part ) )
			{
				working++;
			}
		}

		if ( working == 0 )
		{
			return;
		}

		// pick one of them
		var pick = UnityEngine.Random.Range( 0, working );

		foreach ( var part in c_damageableParts )
		{
			if ( !( ( part == PD_PlayerShip.ShipSystem.Count ) ? crewIsAlive : ship.CanBeDamaged( part ) ) )
			{
				continue;
			}

			if ( pick-- > 0 )
			{
				continue;
			}

			if ( part == PD_PlayerShip.ShipSystem.Count )
			{
				WoundCrew( ship.GetSystemDamage( chunk ) );
			}
			else
			{
				ship.DamageSystem( part, chunk );
			}

			return;
		}
	}

	// the Crystal Pearl is "an automatic warp-out device for when a ship is heavily damaged" (the Thrynn; the Spemin: it "automatically warps a critically wounded
	// ship out of danger") - with it in the hold a ship in an encounter that has less than a tenth of its armor left, or whose hull would be destroyed, is warped
	// out of the encounter with what it has left (the hull at least 1 point of its integrity). The tenth is the port's choice - returns true if the ship was warped
	// out
	bool WarpOutWithTheCrystalPearl()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;

		if ( ( playerData.m_general.m_location != PD_General.Location.Encounter ) || !ship.HasArtifact( "Crystal Pearl" ) )
		{
			return false;
		}

		// critically wounded?
		if ( ( ship.m_armorPoints * 10 >= ship.GetMaximumArmorPoints() ) && !ship.IsDestroyed() )
		{
			return false;
		}

		// the hit that wounded it did not destroy it
		if ( ship.IsDestroyed() )
		{
			ship.SetSystemHealth( PD_PlayerShip.ShipSystem.Hull, 1 );
		}

		// nothing that is still in the air follows the ship out
		ClearMissiles();

		SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>The Crystal Pearl has warped the ship out of danger!</color>" );

		SpaceflightController.m_instance.m_encounter.WarpOut();

		return true;
	}

	// call this when something other than damage destroys the player ship (a star that flares with the ship in its system) - the cause is what the game over
	// screen says (every way to lose the game ends in the same game over)
	public void DestroyPlayerShip( string cause )
	{
		// a ship that has been destroyed cannot be destroyed again
		if ( m_playerIsDestroyed )
		{
			return;
		}

		var playerData = DataController.m_instance.m_playerData;

		playerData.m_playerShip.m_shieldPoints = 0;

		m_gameOverCause = cause;

		DestroyPlayer( playerData.m_general.m_coordinates );
	}

	// the game is lost without the ship being destroyed (the whole crew has died): the game over screen with its cause, and the ship's hull at 0, so that the
	// lost game is never saved (DataController.SavePlayerData refuses a destroyed ship, and the game over reloads the last save)
	public void EndGame( string cause )
	{
		if ( m_playerIsDestroyed )
		{
			return;
		}

		m_playerIsDestroyed = true;
		m_gameOverCause = cause;

		DataController.m_instance.m_playerData.m_playerShip.DestroyHull();

		ClearMissiles();

		ShowGameOver();
	}

	// the player ship is destroyed: it explodes, and then the game is over
	void DestroyPlayer( Vector3 playerPosition )
	{
		DataController.m_instance.m_playerData.m_playerShip.DestroyHull();

		// this only happens once
		m_playerIsDestroyed = true;

		// nothing that is still in the air has a ship left to hit
		ClearMissiles();

		// play ship explosion sound
		SoundController.m_instance.PlaySound( SoundController.Sound.ShipExplosion );

		// spawn player debris
		SpawnPlayerDebris( playerPosition );

		// show explosion
		var explosion = GetAvailableExplosion();
		if ( explosion != null )
		{
			explosion.Play( playerPosition, () =>
			{
				ShowGameOver();
			} );
		}
		else
		{
			// no explosion available, show game over immediately
			ShowGameOver();
		}
	}

	/// <summary>
	/// Spawn debris at the player's position when destroyed.
	/// </summary>
	void SpawnPlayerDebris( Vector3 position )
	{
		if ( m_playerDebrisTemplate == null )
		{
			Debug.Log( "SpawnPlayerDebris: No debris template assigned!" );
			return;
		}

		// get the player ship
		var playerShip = SpaceflightController.m_instance.m_playerShip;

		// the ship's materials, for its wreck (the debris model was imported with blank ones)
		var shipMaterials = ShipWreck.GatherShipMaterials( ( playerShip.m_ship != null ) ? playerShip.m_ship.gameObject : null );

		// hide the player ship model
		if ( playerShip.m_ship != null )
		{
			playerShip.m_ship.gameObject.SetActive( false );
		}

		// spawn debris at the player's position with proper scale
		var debrisInstance = Instantiate( m_playerDebrisTemplate, position, Quaternion.identity );
		
		// use the template's local scale directly - adjust in Unity Inspector if needed
		debrisInstance.transform.localScale = m_playerDebrisTemplate.transform.localScale;
		debrisInstance.SetActive( true );

		// drawn with the ship's own materials, scorched, with embers that cool
		ShipWreck.MakeWreck( debrisInstance, shipMaterials );

		Debug.Log( $"SpawnPlayerDebris: Spawned debris at {position} with scale {debrisInstance.transform.localScale}" );

		// add slow tumbling rotation
		var tumble = debrisInstance.AddComponent<DebrisTumble>();
		tumble.m_rotationSpeed = new Vector3( Random.Range( -10f, 10f ), Random.Range( -10f, 10f ), Random.Range( -10f, 10f ) );
	}

	/// <summary>
	/// Shows the game over screen using the button system.
	/// </summary>
	void ShowGameOver()
	{
		Debug.Log( "ShowGameOver called!" );
		
		// clear messages so game over text is visible
		SpaceflightController.m_instance.m_messages.Clear();

		// game over! (with what caused it, if it was not the damage the ship took - such a cause says game over itself, as STRINFO's flare text does)
		if ( string.IsNullOrEmpty( m_gameOverCause ) )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=#FF0000>Hull destroyed!</color>" );
			SpaceflightController.m_instance.m_messages.AddText( "<color=#FFFF00>GAME OVER</color>" );
		}
		else
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=#FF0000>" + m_gameOverCause + "</color>" );
		}

		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Press ESC to return to title screen.</color>" );

		// pause the game
		SpaceflightController.m_instance.m_gameIsPaused = true;
		
		// set a flag so pressing ESC will restart
		SpaceflightController.m_instance.m_gameOver = true;
	}

	/// <summary>
	/// Called by aliens to fire at the player.
	/// </summary>
	public void AlienFiresAtPlayer( PD_AlienShip alienShip, GD_Vessel vessel )
	{
		// there is nothing left to fire at once the player ship has been destroyed (a missile launched now would still be in the air when the game over screen pauses the game)
		if ( m_playerIsDestroyed )
		{
			return;
		}

		// nor at a ship the Shimmering Ball cloaks
		if ( PlayerIsCloaked() )
		{
			return;
		}

		var playerData = DataController.m_instance.m_playerData;
		var playerPosition = playerData.m_general.m_coordinates;
		var alienPosition = alienShip.m_coordinates;

		// calculate direction to player
		var direction = ( playerPosition - alienPosition ).normalized;

		// what weapon does the alien have?
		if ( vessel.m_hasPlasmaBolts )
		{
			// a plasma bolt is the original's missile of class 6, the one missile that follows the ship (COMBAT-OV WE929): it flies after the ship and does its
			// damage when it reaches it, so a ship that keeps away until it runs out of time escapes it; one of the aliens' missiles in the air (fewer than 10)
			var bolt = ( m_alienMissilesInFlight < c_maximumAlienMissilesInFlight ) ? GetAvailableMissile() : null;

			if ( bolt != null )
			{
				var playerShip = SpaceflightController.m_instance.m_playerShip;
				int damage = GetMissileDamage( c_plasmaBoltClass );

				m_alienMissilesInFlight++;

				bolt.Fire( alienPosition, playerShip.transform, ( hitPosition, reachedTarget ) =>
				{
					m_alienMissilesInFlight = Mathf.Max( 0, m_alienMissilesInFlight - 1 );

					if ( reachedTarget && !m_playerIsDestroyed )
					{
						// what hit the ship, in the original's words (DAMAGE-OV: PLASMA BOLT)
						SpaceflightController.m_instance.m_messages.AddText( "<color=red>Plasma bolt</color>" );

						ApplyDamageToPlayer( damage, direction );
					}

					// play explosion
					var explosion = GetAvailableExplosion();
					if ( explosion != null )
					{
						explosion.SetScale( 0.5f );
						explosion.Play( hitPosition );
					}

					// play torpedo explosion sound
					SoundController.m_instance.PlaySound( SoundController.Sound.TorpedoExplosion );
				} );

				// play torpedo launch sound
				SoundController.m_instance.PlaySound( SoundController.Sound.TorpedoFire );
			}
		}
		else if ( vessel.m_laserClass > 0 )
		{
			// fire laser
			var laser = GetAvailableLaser();
			if ( laser != null )
			{
				laser.SetColor( new Color( 1.0f, 0.5f, 0.5f ) ); // red laser
				laser.Fire( alienPosition, playerPosition );
			}

			// apply damage: twice a laser's damage at that distance, and the Rod Device "projects an enhanced laser shield around a spaceship" (the Starport's
			// analysis): a quarter of it with the Rod aboard (DAMAGE-OV WF3EA and WED36: ?ROD @ IF 4 /)
			int damage = GetAlienLaserDamage( vessel.m_laserClass, Vector3.Distance( alienPosition, playerPosition ), playerData.m_playerShip.HasArtifact( "Rod Device" ) );

			// what hit the ship, in the original's words (COMBAT-OV WECEA: LASER CLASS n, or LASER PARTIALLY DEFLECTED with the Rod Device)
			SpaceflightController.m_instance.m_messages.AddText( playerData.m_playerShip.HasArtifact( "Rod Device" ) ? "<color=red>Laser partially deflected</color>" : "<color=red>Laser class " + vessel.m_laserClass + "</color>" );

			ApplyDamageToPlayer( damage, direction );
		}

		// a ship with a missile launcher launches its missiles as well as firing its laser (COMBAT-OV <COMBAT> rolls the laser, WECEA, and the missile, WEC95, each
		// on its own), while fewer than 10 of the aliens' missiles are in the air (WDFD2)
		if ( !vessel.m_hasPlasmaBolts && ( vessel.m_missileClass > 0 ) && ( m_alienMissilesInFlight < c_maximumAlienMissilesInFlight ) )
		{
			// fire missile
			var missile = GetAvailableMissile();
			if ( missile != null )
			{
				var playerShip = SpaceflightController.m_instance.m_playerShip;
				int damage = GetMissileDamage( Mathf.Min( vessel.m_missileClass, c_plasmaBoltClass - 1 ) );

				m_alienMissilesInFlight++;

				// it flies to where the ship was when it was launched, and only the plasma bolt follows the ship (COMBAT-OV WE929): it hits if the ship is still
				// about in that place (within 100 units, a cell and a quarter of the original's combat map at c_unitsPerCell)
				missile.Fire( alienPosition, playerShip.transform.position, ( hitPosition, reachedTarget ) =>
				{
					m_alienMissilesInFlight = Mathf.Max( 0, m_alienMissilesInFlight - 1 );

					var didHit = reachedTarget && !m_playerIsDestroyed && ( Vector3.Distance( SpaceflightController.m_instance.m_playerShip.transform.position, hitPosition ) <= c_alienMissileHitRadius );

					if ( didHit )
					{
						// what hit the ship, in the original's words (DAMAGE-OV: MISSILE CLASS n)
						SpaceflightController.m_instance.m_messages.AddText( "<color=red>Missile class " + vessel.m_missileClass + "</color>" );

						ApplyDamageToPlayer( damage, direction );
					}

					// play explosion
					var explosion = GetAvailableExplosion();
					if ( explosion != null )
					{
						explosion.SetScale( 0.5f );
						explosion.Play( hitPosition );
					}

					// play torpedo explosion sound
					SoundController.m_instance.PlaySound( SoundController.Sound.TorpedoExplosion );
				} );

				// play torpedo launch sound
				SoundController.m_instance.PlaySound( SoundController.Sound.TorpedoFire );

			}
			else
			{
				// play energy weapon sound
				SoundController.m_instance.PlaySound( SoundController.Sound.EnergyWeapon );
			}
		}
	}

	// a hit that falls on the crew wounds each of them who is alive with a chance of 70 in 100, by the hit's share of their durability - 5 at least - and the text
	// window says how badly each is wounded (DAMAGE-OV WF0E0, WED5E, WED6A and WF02E: "<NAME> IS MODERATELY WOUNDED")
	void WoundCrew( int damage )
	{
		var gameData = DataController.m_instance.m_gameData;
		var crewAssignment = DataController.m_instance.m_playerData.m_crewAssignment;

		foreach ( var file in GetLivingCrew() )
		{
			if ( UnityEngine.Random.Range( 0, 100 ) >= c_woundChance )
			{
				continue;
			}

			var durability = 1;

			if ( ( file.m_crewRaceId >= 0 ) && ( file.m_crewRaceId < gameData.m_crewRaceList.Length ) )
			{
				durability = Mathf.Max( 1, gameData.m_crewRaceList[ file.m_crewRaceId ].m_durability );
			}

			var wound = Mathf.Max( c_leastWound, UnityEngine.Random.Range( 1, Mathf.Max( 1, damage ) + 1 ) / durability / 10 );

			// (at 0 the crew member dies, and with nobody left the game is lost: PD_CrewAssignment.InjureCrewMember says so)
			if ( !crewAssignment.InjureCrewMember( file, wound ) )
			{
				SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + file.m_name + " is " + ExamineButton.GetWoundWord( Mathf.CeilToInt( file.m_vitality ) ) + " wounded.</color>" );
			}
		}
	}

	// the chance of each crew member to be wounded by a hit on the crew, in 100, and the least wound (DAMAGE-OV WED5E: 70; WED6A: 5)
	const int c_woundChance = 70;

	// the living crew members at their posts, each once (one can hold several posts)
	static List<PD_Personnel.PD_PersonnelFile> GetLivingCrew()
	{
		var crewAssignment = DataController.m_instance.m_playerData.m_crewAssignment;
		var crew = new List<PD_Personnel.PD_PersonnelFile>();

		for ( var role = PD_CrewAssignment.Role.First; role < PD_CrewAssignment.Role.Count; role++ )
		{
			if ( !crewAssignment.IsAssigned( role ) )
			{
				continue;
			}

			var file = crewAssignment.GetPersonnelFile( role );

			if ( ( file != null ) && !file.m_isDead && ( file.m_vitality > 0.0f ) && !crew.Contains( file ) )
			{
				crew.Add( file );
			}
		}

		return crew;
	}
	const int c_leastWound = 5;

	// get available laser from pool
	LaserBeam GetAvailableLaser()
	{
		foreach ( var laser in m_laserPool )
		{
			if ( !laser.IsActive() )
			{
				return laser;
			}
		}
		return null;
	}

	// get available missile from pool
	MissileProjectile GetAvailableMissile()
	{
		foreach ( var missile in m_missilePool )
		{
			if ( !missile.IsActive() )
			{
				return missile;
			}
		}
		return null;
	}

	// get available explosion from pool
	ExplosionEffect GetAvailableExplosion()
	{
		EnsurePoolsInitialized();
		
		if ( m_explosionPool == null )
		{
			return null;
		}

		foreach ( var explosion in m_explosionPool )
		{
			if ( !explosion.IsPlaying() )
			{
				// back to full size - whoever used it last may have made it smaller (a missile hit plays it at half size)
				explosion.SetScale( 1.0f );

				return explosion;
			}
		}
		return null;
	}

	// get available shield hit from pool
	ShieldHitEffect GetAvailableShieldHit()
	{
		foreach ( var shieldHit in m_shieldHitPool )
		{
			if ( !shieldHit.IsPlaying() )
			{
				return shieldHit;
			}
		}
		return null;
	}

	// get available hull hit from pool
	HullHitEffect GetAvailableHullHit()
	{
		foreach ( var hullHit in m_hullHitPool )
		{
			if ( !hullHit.IsPlaying() )
			{
				return hullHit;
			}
		}
		return null;
	}

	// get laser color based on weapon class
	Color GetLaserColor( int weaponClass )
	{
		switch ( weaponClass )
		{
			case 1: return new Color( 1.0f, 0.3f, 0.3f ); // red
			case 2: return new Color( 1.0f, 0.5f, 0.2f ); // orange
			case 3: return new Color( 1.0f, 1.0f, 0.3f ); // yellow
			case 4: return new Color( 0.3f, 1.0f, 0.3f ); // green
			case 5: return new Color( 0.3f, 0.5f, 1.0f ); // blue
			default: return new Color( 1.0f, 0.3f, 0.3f );
		}
	}
}
