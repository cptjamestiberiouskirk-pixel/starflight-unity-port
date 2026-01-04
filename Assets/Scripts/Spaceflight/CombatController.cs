
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

	// current target
	int m_currentTargetIndex = -1;

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
	}

	/// <summary>
	/// Set the current target for combat.
	/// </summary>
	public void SetTarget( int targetIndex )
	{
		m_currentTargetIndex = targetIndex;
	}

	/// <summary>
	/// Get the current target index.
	/// </summary>
	public int GetTargetIndex()
	{
		return m_currentTargetIndex;
	}

	/// <summary>
	/// Check if player can fire laser.
	/// </summary>
	public bool CanFireLaser()
	{
		var playerData = DataController.m_instance.m_playerData;

		// must have laser cannon
		if ( playerData.m_playerShip.m_laserCannonClass <= 0 )
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

		// must have missile launcher
		if ( playerData.m_playerShip.m_missileLauncherClass <= 0 )
		{
			return false;
		}

		// must have missiles
		if ( playerData.m_playerShip.m_missilesRemaining <= 0 )
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

		// get target alien ship
		var encounter = SpaceflightController.m_instance.m_encounter;
		var alienShipList = encounter.m_pdEncounter.GetAlienShipList();

		if ( m_currentTargetIndex >= alienShipList.Length )
		{
			return false;
		}

		var targetShip = alienShipList[ m_currentTargetIndex ];
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
			ApplyDamageToAlien( m_currentTargetIndex, damage );
		}

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

		// get target
		var encounter = SpaceflightController.m_instance.m_encounter;
		var alienShipList = encounter.m_pdEncounter.GetAlienShipList();

		if ( m_currentTargetIndex >= alienShipList.Length )
		{
			return false;
		}

		var targetShip = alienShipList[ m_currentTargetIndex ];
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

		// use a missile
		playerData.m_playerShip.m_missilesRemaining--;

		// check if target is immune
		bool isImmune = targetVessel.m_immuneToMissiles;
		if ( isImmune )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Target is immune to missiles!</color>" );
		}

		// fire the missile
		var missile = GetAvailableMissile();
		if ( missile != null )
		{
			// find target model transform
			var targetModel = encounter.m_alienShipModelList[ m_currentTargetIndex ];
			int targetIndex = m_currentTargetIndex;
			int damage = c_missileDamage[ Mathf.Clamp( playerData.m_playerShip.m_missileLauncherClass, 0, c_missileDamage.Length - 1 ) ];

			missile.Fire( playerPosition, targetModel.transform, ( hitPosition, didHit ) =>
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
			} );
		}

		// set cooldown
		m_playerMissileCooldown = c_baseMissileCooldown / ( 1.0f + playerData.m_playerShip.m_missileLauncherClass * 0.15f );

		// play torpedo launch sound
		SoundController.m_instance.PlaySound( SoundController.Sound.TorpedoFire );

		SpaceflightController.m_instance.m_messages.AddText( $"<color=white>Missile launched! ({playerData.m_playerShip.m_missilesRemaining} remaining)</color>" );

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
		var targetModel = encounter.m_alienShipModelList[ alienIndex ];

		// already dead?
		if ( targetShip.m_isDead )
		{
			return;
		}

		var gameData = DataController.m_instance.m_gameData;
		var vessel = gameData.m_vesselList[ targetShip.m_vesselId ];

		// apply damage (for aliens we use armor class as hit points)
		float armorRemaining = vessel.m_armorClass - ( damage / 10.0f );

		// show hit effect
		if ( armorRemaining > 0.0f )
		{
			var hullHit = GetAvailableHullHit();
			if ( hullHit != null )
			{
				hullHit.Play( targetShip.m_coordinates );
			}

			SpaceflightController.m_instance.m_messages.AddText( $"<color=#00FF00>Hit! Target damaged.</color>" );
		}
		else
		{
			// ship destroyed!
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
		var playerData = DataController.m_instance.m_playerData;
		var playerPosition = playerData.m_general.m_coordinates;

		// first absorb with shields
		if ( playerData.m_playerShip.m_shieldPoints > 0 )
		{
			int shieldAbsorb = Mathf.Min( damage, playerData.m_playerShip.m_shieldPoints );
			playerData.m_playerShip.m_shieldPoints -= shieldAbsorb;
			damage -= shieldAbsorb;

			// show shield effect
			var shieldHit = GetAvailableShieldHit();
			if ( shieldHit != null )
			{
				shieldHit.Play( playerPosition );
			}

			// play shield hit sound
			SoundController.m_instance.PlaySound( SoundController.Sound.ShieldHit );

			if ( shieldAbsorb > 0 )
			{
				SpaceflightController.m_instance.m_messages.AddText( $"<color=cyan>Shields absorb {shieldAbsorb} damage!</color>" );
			}
		}

		// remaining damage to armor
		if ( damage > 0 )
		{
			playerData.m_playerShip.m_armorPoints -= damage;

			// show hull hit
			var hullHit = GetAvailableHullHit();
			if ( hullHit != null )
			{
				hullHit.Play( playerPosition, hitDirection );
			}

			SpaceflightController.m_instance.m_messages.AddText( $"<color=red>Hull takes {damage} damage!</color>" );

			// play red alert if armor is critically low (below 25%)
			int maxArmor = playerData.m_playerShip.m_armorPoints + damage; // approximate original value
			if ( playerData.m_playerShip.m_armorPoints > 0 && playerData.m_playerShip.m_armorPoints < 250 )
			{
				SoundController.m_instance.PlaySound( SoundController.Sound.RedAlert );
				SpaceflightController.m_instance.m_messages.AddText( "<color=#FFA500>WARNING: Hull breach imminent!</color>" );
			}

			// check for destruction
			if ( playerData.m_playerShip.m_armorPoints <= 0 )
			{
				playerData.m_playerShip.m_armorPoints = 0;

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

		// game over!
		SpaceflightController.m_instance.m_messages.AddText( "<color=#FF0000>Ship destroyed!</color>" );
		SpaceflightController.m_instance.m_messages.AddText( "<color=#FFFF00>GAME OVER</color>" );
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

			SpaceflightController.m_instance.m_messages.AddText( $"<color=red>Plasma bolt incoming!</color>" );
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
			ApplyDamageToPlayer( damage, direction );

			SpaceflightController.m_instance.m_messages.AddText( $"<color=red>Enemy laser fire!</color>" );
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

				SpaceflightController.m_instance.m_messages.AddText( $"<color=red>Incoming torpedo!</color>" );
			}
			else
			{
				// play energy weapon sound
				SoundController.m_instance.PlaySound( SoundController.Sound.EnergyWeapon );
			}
		}
	}

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
