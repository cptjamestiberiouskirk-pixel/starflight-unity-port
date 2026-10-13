
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

	// damage values per weapon class
	static readonly int[] c_laserDamage = { 0, 20, 35, 50, 70, 100 };
	static readonly int[] c_missileDamage = { 0, 40, 70, 100, 140, 200 };

	// weapon ranges
	const float c_laserRange = 800.0f;
	const float c_missileRange = 1500.0f;

	// endurium used up by every shot, in cubic meters (a missile does twice the damage of a laser of the same class and uses twice the fuel)
	const float c_laserFuelPerShot = 0.01f;
	const float c_missileFuelPerShot = 0.02f;

	// current target
	int m_currentTargetIndex = -1;

	// true once the player ship has been destroyed (it can only be destroyed once)
	bool m_playerIsDestroyed;

	// what destroyed the player ship, when it was not the damage it took (null for that) - the game over screen says it
	string m_gameOverCause;

	// the Shimmering Ball cloaks the ship in combat, and a shot of the player drops the cloak for a while (the owner's ruling of 2026-10-10; how long is the port's
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
	}

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

		// fire the laser
		var laser = GetAvailableLaser();
		if ( laser != null )
		{
			// set color based on weapon class
			var laserColor = GetLaserColor( playerData.m_playerShip.m_laserCannonClass );
			laser.SetColor( laserColor );
			laser.Fire( playerPosition, targetPosition );
		}

		// apply damage if not immune
		if ( !targetVessel.m_immuneToLasers )
		{
			int damage = c_laserDamage[ Mathf.Clamp( playerData.m_playerShip.m_laserCannonClass, 0, c_laserDamage.Length - 1 ) ];

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

		// fire the missile - find target model (its model slot is not the same as the target index, so ask the encounter for it)
		var targetModel = encounter.GetAlienShipModel( m_currentTargetIndex );
		int targetIndex = m_currentTargetIndex;
		int damage = c_missileDamage[ Mathf.Clamp( playerData.m_playerShip.m_missileLauncherClass, 0, c_missileDamage.Length - 1 ) ];

		// a damaged missile launcher's missiles do damage in proportion to its health
		damage = Mathf.Max( 1, Mathf.RoundToInt( damage * playerData.m_playerShip.GetSystemFactor( PD_PlayerShip.ShipSystem.Missiles ) ) );

		// this is called when the missile reaches the target
		System.Action<Vector3, bool> onMissileHit = ( hitPosition, didHit ) =>
		{
			if ( didHit && !isImmune )
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

		if ( targetModel != null )
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

		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Missile launched!</color>" );

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

		// the shields absorb what they can first
		int shieldAbsorb = Mathf.Min( damage, targetShip.m_shieldPoints );

		targetShip.m_shieldPoints -= shieldAbsorb;

		// whatever gets through the shields comes off the armor
		targetShip.m_armorPoints -= ( damage - shieldAbsorb );

		// show hit effect
		if ( targetShip.m_armorPoints > 0 )
		{
			var hullHit = GetAvailableHullHit();
			if ( hullHit != null )
			{
				// the player's shot came from the player ship, so the sparks fly back that way
				hullHit.Play( targetShip.m_coordinates, targetShip.m_coordinates - DataController.m_instance.m_playerData.m_general.m_coordinates );
			}

			// let the player know whether the hit got through the shields
			if ( shieldAbsorb == damage )
			{
				SpaceflightController.m_instance.m_messages.AddText( $"<color=#00FF00>Hit! Target's shields absorbed it.</color>" );
			}
			else
			{
				SpaceflightController.m_instance.m_messages.AddText( $"<color=#00FF00>Hit! Target damaged.</color>" );
			}
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

	/// <summary>
	/// Apply damage to the player ship.
	/// </summary>
	public void ApplyDamageToPlayer( int damage, Vector3 hitDirection )
	{
		// a ship that has been destroyed cannot be hit again (it would explode once more and call the game over screen a second time)
		if ( m_playerIsDestroyed )
		{
			return;
		}

		var playerData = DataController.m_instance.m_playerData;
		var playerPosition = playerData.m_general.m_coordinates;

		// first absorb with shields (lowered shields keep their charge now, so they have to be up to absorb anything)
		if ( playerData.m_playerShip.m_shieldsAreUp && ( playerData.m_playerShip.m_shieldPoints > 0 ) )
		{
			int shieldAbsorb = Mathf.Min( damage, playerData.m_playerShip.m_shieldPoints );
			playerData.m_playerShip.m_shieldPoints -= shieldAbsorb;
			damage -= shieldAbsorb;

			// show shield effect
			var shieldHit = GetAvailableShieldHit();
			if ( shieldHit != null )
			{
				shieldHit.Play( playerPosition, hitDirection );
			}

			// play shield hit sound
			SoundController.m_instance.PlaySound( SoundController.Sound.ShieldHit );

		}

		// remaining damage to armor
		if ( damage > 0 )
		{
			playerData.m_playerShip.m_armorPoints -= damage;

			// and one of the ship's other fields takes its share: one of its systems, or the crew (the original's damage picks one of eight fields for a hit, the
			// seventh the crew, DAMAGE-OV #>?FIEL and #>DAMAG)
			if ( UnityEngine.Random.Range( 0, playerData.m_playerShip.GetInstalledSystemCount() + 1 ) == 0 )
			{
				WoundCrew( damage );
			}
			else
			{
				playerData.m_playerShip.DamageRandomSystem( damage );
			}

			// show hull hit
			var hullHit = GetAvailableHullHit();
			if ( hullHit != null )
			{
				hullHit.Play( playerPosition, hitDirection );
			}

			// play red alert if armor is critically low (below 25% of the most this ship can have)
			int maxArmor = playerData.m_playerShip.GetMaximumArmorPoints();
			if ( playerData.m_playerShip.m_armorPoints > 0 && playerData.m_playerShip.m_armorPoints * 4 < maxArmor )
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
			if ( playerData.m_playerShip.m_armorPoints <= 0 )
			{
				DestroyPlayer( playerPosition );
			}
		}
	}

	// the Crystal Pearl is "an automatic warp-out device for when a ship is heavily damaged" (the Thrynn; the Spemin: it "automatically warps a critically wounded
	// ship out of danger") - with it in the hold a ship in an encounter that has less than a tenth of its armor left, or would be destroyed, is warped out of the
	// encounter with what armor it has (at least 1 point). The tenth is the port's choice - returns true if the ship was warped out
	bool WarpOutWithTheCrystalPearl()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;

		if ( ( playerData.m_general.m_location != PD_General.Location.Encounter ) || !ship.HasArtifact( "Crystal Pearl" ) )
		{
			return false;
		}

		// critically wounded?
		if ( ship.m_armorPoints * 10 >= ship.GetMaximumArmorPoints() )
		{
			return false;
		}

		// the hit that wounded it did not destroy it
		ship.m_armorPoints = Mathf.Max( ship.m_armorPoints, 1 );

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

	// the game is lost without the ship being destroyed (the whole crew has died): the game over screen with its cause, and the ship's armor at 0, so that the
	// lost game is never saved (DataController.SavePlayerData refuses a destroyed ship, and the game over reloads the last save)
	public void EndGame( string cause )
	{
		if ( m_playerIsDestroyed )
		{
			return;
		}

		m_playerIsDestroyed = true;
		m_gameOverCause = cause;

		DataController.m_instance.m_playerData.m_playerShip.m_armorPoints = 0;

		ClearMissiles();

		ShowGameOver();
	}

	// the player ship is destroyed: it explodes, and then the game is over
	void DestroyPlayer( Vector3 playerPosition )
	{
		DataController.m_instance.m_playerData.m_playerShip.m_armorPoints = 0;

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
			SpaceflightController.m_instance.m_messages.AddText( "<color=#FF0000>Ship destroyed!</color>" );
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
			// fire plasma bolt (use laser visual with different color)
			var laser = GetAvailableLaser();
			if ( laser != null )
			{
				laser.SetColor( new Color( 0.5f, 1.0f, 0.5f ) ); // green plasma
				laser.Fire( alienPosition, playerPosition );
			}

			// apply damage
			int damage = 30 + vessel.m_shieldClass * 10;
			ApplyDamageToPlayer( damage, direction );

			// what hit the ship, in the original's words (DAMAGE-OV: PLASMA BOLT)
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Plasma bolt</color>" );
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

			// apply damage
			int damage = c_laserDamage[ Mathf.Clamp( vessel.m_laserClass, 0, c_laserDamage.Length - 1 ) ];

			// the Rod Device "projects an enhanced laser shield around a spaceship" (the Starport's analysis; the original's combat code keeps whether the ship has it,
			// disys.txt ?ROD) - how much it takes off is the port's choice: half of every laser hit
			if ( playerData.m_playerShip.HasArtifact( "Rod Device" ) )
			{
				damage /= 2;
			}

			ApplyDamageToPlayer( damage, direction );
		}
		else if ( vessel.m_missileClass > 0 )
		{
			// fire missile
			var missile = GetAvailableMissile();
			if ( missile != null )
			{
				var playerShip = SpaceflightController.m_instance.m_playerShip;
				int damage = c_missileDamage[ Mathf.Clamp( vessel.m_missileClass, 0, c_missileDamage.Length - 1 ) ];

				missile.Fire( alienPosition, playerShip.transform, ( hitPosition, didHit ) =>
				{
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
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;
		var crewAssignment = playerData.m_crewAssignment;
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

		foreach ( var file in crew )
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
