
using UnityEngine;
using System;

[Serializable]

public class PD_PlayerShip
{
	// the armor points of a ship with no armor plating installed (the bare hull)
	public const int c_bareHullArmorPoints = 250;

	public string m_name;

	public int m_numCargoPods;
	public int m_enginesClass;
	public int m_shieldingClass;
	public int m_armorClass;
	public int m_missileLauncherClass;
	public int m_laserCannonClass;
	public int m_mass;
	public int m_volume;
	public int m_volumeUsed;
	public int m_acceleration;

	public float m_fuelUsed;

	public PD_ArtifactStorage m_artifactStorage;
	public PD_ElementStorage m_elementStorage;

	public bool m_shieldsAreUp;
	public bool m_weaponsAreArmed;

	public int m_shieldPoints;
	public int m_armorPoints;

	// how many seconds it takes the shields to regain one percent of their full charge
	public const float c_shieldRechargeInterval = 5.0f;

	// true once the shield charge is kept when the shields are lowered (false in save files from before that - see ValidateShieldCharge)
	public bool m_shieldChargeIsKept;

	// how long it has been since the shields last regained some charge
	public float m_shieldRechargeTimer;

	// how many armor points the engineer repairs each second for every point of engineering skill, and the slowest an engineer ever works
	public const float c_repairRatePerSkillPoint = 0.02f;
	public const float c_minimumRepairRate = 0.5f;

	// true while the engineer is repairing the armor (false in save files from before repairs took time)
	public bool m_repairsAreUnderWay;

	// the part of an armor point that has been repaired so far
	public float m_repairProgress;

	// the ship's systems other than the hull, whose damage is the armor points (the original's damage screen has a bar for the hull, engines, sensors, comm,
	// shield, missile and laser - SS Damage) - the order is saved, append and never reorder
	public enum ShipSystem
	{
		Engines,
		Sensors,
		Comm,
		Shields,
		Missiles,
		Lasers,
		Count
	}

	// how whole each system is, from 0 (destroyed) to 100 (undamaged), by ShipSystem (null in save files from before the systems could be damaged)
	public int[] m_systemHealth;

	// what the engineer is repairing: the hull (the armor points), or a system by its ShipSystem plus 1 (0, the hull, in save files from before the systems could
	// be damaged, when the hull was all the engineer repaired)
	public int m_repairItem;
	public const int c_hullRepairItem = 0;
	public const int c_repairItemCount = (int) ShipSystem.Count + 1;

	// the repair mineral of the hull and of each system, by its name in the game data, and how much of it the engineer needs: a cubic meter for every tenth
	// repaired (the owner's ruling of 2026-10-11 for roadmap 1.2 - the original's engineer asks for "2 cubic meters of Molybdenum", "5 cubic meters of Aluminum",
	// SS Repair; which mineral goes with which system is the port's choice)
	static readonly string[] c_repairMinerals = { "Aluminum", "Molybdenum", "Copper", "Silver", "Titanium", "Iron", "Platinum" };
	public const int c_repairMineralPerTenth = 10;

	public void Reset()
	{
		// reset the ship name
		m_name = "";

		// reset the number of cargo pods
		m_numCargoPods = 0;

		// reset the levels of each ship component
		m_enginesClass = 1;
		m_shieldingClass = 0;
		m_armorClass = 0;
		m_missileLauncherClass = 0;
		m_laserCannonClass = 0;

		// reset fuel used to zero
		m_fuelUsed = 0.0f;

		// every system is whole
		m_systemHealth = null;

		EnsureSystemHealth();

		// reset other stuff
		m_shieldsAreUp = false;
		m_weaponsAreArmed = false;

		m_shieldPoints = 0;
		m_armorPoints = c_bareHullArmorPoints;

		m_shieldChargeIsKept = true;
		m_shieldRechargeTimer = 0.0f;

		m_repairsAreUnderWay = false;
		m_repairProgress = 0.0f;

		// recalculate the mass of the ship
		RecalculateMass();

		// recalculate the volume of the ship
		RecalculateVolume();

		// recalculate the acceleration of the ship
		RecalculateAcceleration();

		// create and reset the cargo hold to initial game state
		m_artifactStorage = new PD_ArtifactStorage();
		m_elementStorage = new PD_ElementStorage();

		m_artifactStorage.Reset();
		m_elementStorage.Reset();

		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// fill the ship cargo with all of the elements the player gets at the start of the game
		for ( var elementId = 0; elementId < gameData.m_elementList.Length; elementId++ )
		{
			var elementGameData = gameData.m_elementList[ elementId ];

			if ( elementGameData.m_initialVolume > 0 )
			{
				m_elementStorage.Add( elementId, elementGameData.m_initialVolume );
			}
		}

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	public GD_Engines GetEngines()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// return the engines this ship has
		return gameData.m_enginesList[ m_enginesClass ];
	}

	public GD_Shielding GetShielding()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// return the shields this ship has
		return gameData.m_shieldingList[ m_shieldingClass ];
	}

	public GD_Armor GetArmor()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// return the armor this ship has
		return gameData.m_armorList[ m_armorClass ];
	}

	// true if armor plating is installed (a ship without any still has the armor points of its bare hull)
	public bool HasArmorPlating()
	{
		return GetArmor().m_points > 0;
	}

	// the most armor points this ship can have - those of the armor plating that is installed, or those of the bare hull if there is none (so this is never zero)
	// what the Starport charges to repair the ship: the missing armor points at the price of the ship's armor class (a bare hull at the price of class 1), the
	// shields for nothing (the owner's ruling of 2026-10-10 for roadmap 1.1 - no source gives the price)
	public int GetStarportRepairCost()
	{
		var gameData = DataController.m_instance.m_gameData;
		var missingPoints = GetMaximumArmorPoints() - m_armorPoints;

		if ( missingPoints <= 0 )
		{
			return 0;
		}

		var armorClass = HasArmorPlating() ? m_armorClass : 1;

		if ( ( armorClass < 1 ) || ( armorClass >= gameData.m_armorList.Length ) || ( gameData.m_armorList[ armorClass ].m_points <= 0 ) )
		{
			return 0;
		}

		var armor = gameData.m_armorList[ armorClass ];

		// rounded up, so that a repair is never free
		return (int) ( ( (long) missingPoints * armor.m_buyPrice + armor.m_points - 1 ) / armor.m_points );
	}

	// make sure there is a health for every system (save files from before the systems could be damaged have none: their systems are whole)
	void EnsureSystemHealth()
	{
		if ( ( m_systemHealth != null ) && ( m_systemHealth.Length == (int) ShipSystem.Count ) )
		{
			return;
		}

		var health = new int[ (int) ShipSystem.Count ];

		for ( var system = 0; system < health.Length; system++ )
		{
			health[ system ] = ( ( m_systemHealth != null ) && ( system < m_systemHealth.Length ) ) ? m_systemHealth[ system ] : 100;
		}

		m_systemHealth = health;
	}

	// how whole a system is, from 0 to 100
	public int GetSystemHealth( ShipSystem system )
	{
		EnsureSystemHealth();

		return m_systemHealth[ (int) system ];
	}

	// set how whole a system is (kept between 0 and 100)
	public void SetSystemHealth( ShipSystem system, int health )
	{
		EnsureSystemHealth();

		m_systemHealth[ (int) system ] = Mathf.Clamp( health, 0, 100 );
	}

	// how much of its work a system does, from 0 to 1: a damaged system works in proportion to its health (the owner's ruling of 2026-10-11 for roadmap 1.2)
	public float GetSystemFactor( ShipSystem system )
	{
		return GetSystemHealth( system ) / 100.0f;
	}

	// the health below which the sensors give no readings (the owner's ruling of 2026-10-11)
	public const int c_sensorsNeedHealth = 25;

	// true if the ship has this system (the engines, the sensors and the comm always; the shields, the missiles and the lasers once they are installed)
	public bool HasSystem( ShipSystem system )
	{
		switch ( system )
		{
			case ShipSystem.Shields: return m_shieldingClass > 0;
			case ShipSystem.Missiles: return m_missileLauncherClass > 0;
			case ShipSystem.Lasers: return m_laserCannonClass > 0;
			case ShipSystem.Count: return false;
			default: return true;
		}
	}

	// the name of a system, as the damage report shows it (the shields, the missiles and the lasers by their equipment, as the report also shows the shields' charge)
	public static string GetSystemName( ShipSystem system )
	{
		switch ( system )
		{
			case ShipSystem.Engines: return "Engines";
			case ShipSystem.Sensors: return "Sensors";
			case ShipSystem.Comm: return "Comm";
			case ShipSystem.Shields: return "Shield generator";
			case ShipSystem.Missiles: return "Missile launcher";
			case ShipSystem.Lasers: return "Laser cannon";
			default: return "?";
		}
	}

	// true if every system is whole
	public bool AllSystemsAreWhole()
	{
		for ( var system = 0; system < (int) ShipSystem.Count; system++ )
		{
			if ( GetSystemHealth( (ShipSystem) system ) < 100 )
			{
				return false;
			}
		}

		return true;
	}

	// make every system whole again
	public void RestoreSystems()
	{
		for ( var system = 0; system < (int) ShipSystem.Count; system++ )
		{
			SetSystemHealth( (ShipSystem) system, 100 );
		}
	}

	// a hit that has got through the shields damages the hull (the armor points) and one of the ship's other systems, chosen at random, by the same share of it as of
	// the hull (the owner's ruling of 2026-10-11 for roadmap 1.2; the share is the port's choice) - returns the system it damaged (Count if none)
	public ShipSystem DamageRandomSystem( int damage )
	{
		var maximumArmor = GetMaximumArmorPoints();

		if ( ( damage <= 0 ) || ( maximumArmor <= 0 ) )
		{
			return ShipSystem.Count;
		}

		// how many systems the ship has
		var installed = 0;

		for ( var system = 0; system < (int) ShipSystem.Count; system++ )
		{
			if ( HasSystem( (ShipSystem) system ) )
			{
				installed++;
			}
		}

		if ( installed == 0 )
		{
			return ShipSystem.Count;
		}

		// pick one of them
		var pick = UnityEngine.Random.Range( 0, installed );

		for ( var system = 0; system < (int) ShipSystem.Count; system++ )
		{
			if ( !HasSystem( (ShipSystem) system ) )
			{
				continue;
			}

			if ( pick-- == 0 )
			{
				// the same share as of the hull, rounded up so that every hit leaves a mark
				var share = Mathf.CeilToInt( damage * 100.0f / maximumArmor );

				SetSystemHealth( (ShipSystem) system, GetSystemHealth( (ShipSystem) system ) - share );

				return (ShipSystem) system;
			}
		}

		return ShipSystem.Count;
	}

	public int GetMaximumArmorPoints()
	{
		return HasArmorPlating() ? GetArmor().m_points : c_bareHullArmorPoints;
	}

	// call this when a save file has been loaded - one from before selling the armor took its points away can hold more armor points than this ship can have
	public void ValidateArmorPoints()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// leave a save file alone whose armor class the game data does not have (there is no maximum to cut back to)
		if ( ( m_armorClass < 0 ) || ( m_armorClass >= gameData.m_armorList.Length ) )
		{
			return;
		}

		// never more than the most this ship can have (this never adds points, so a destroyed ship stays destroyed)
		m_armorPoints = Mathf.Min( m_armorPoints, GetMaximumArmorPoints() );
	}

	// the most shield points this ship can have - the full charge of the shielding that is installed (zero if there is none)
	public int GetMaximumShieldPoints()
	{
		// a damaged shield generator holds less charge
		return Mathf.Max( 0, Mathf.RoundToInt( GetShielding().m_points * GetSystemFactor( ShipSystem.Shields ) ) );
	}

	// what the armor points belong to, for the messages - the armor plating, or the hull itself if no plating is installed
	public string GetArmorName()
	{
		return HasArmorPlating() ? "armor" : "hull";
	}

	public GD_MissileLauncher GetMissileLauncher()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// return the missile launcher this ship has
		return gameData.m_missileLauncherList[ m_missileLauncherClass ];
	}

	public GD_LaserCannon GetLaserCannon()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// return the laser cannon this ship has
		return gameData.m_laserCannonList[ m_laserCannonClass ];
	}

	public void RecalculateMass()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// start with the base ship mass
		m_mass = gameData.m_misc.m_baseShipMass;

		// add in the mass of all the cargo pods
		m_mass += m_numCargoPods * gameData.m_misc.m_cargoPodMass;

		// add in the mass of all the add on ship components
		m_mass += GetEngines().m_mass;
		m_mass += GetShielding().m_mass;
		m_mass += GetArmor().m_mass;
		m_mass += GetMissileLauncher().m_mass;
		m_mass += GetLaserCannon().m_mass;
	}

	public void RecalculateVolume()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// start with the base ship mass
		m_volume = gameData.m_misc.m_baseShipVolume;

		// add in the volume of each cargo pod
		m_volume += gameData.m_misc.m_cargoPodVolume * m_numCargoPods;
	}

	public void RecalculateAcceleration()
	{
		// get our engines
		var engines = GetEngines();

		// this formula closely matches the original starflight game
		m_acceleration = Mathf.RoundToInt( Mathf.Pow( ( 500.0f - m_mass ) / 500.0f, engines.m_powerCurve ) * engines.m_powerScale * engines.m_maximumAcceleration + engines.m_minimumAcceleration );
	}

	public void AddCargoPod()
	{
		// add one cargo pod
		m_numCargoPods++;

		// recalculate ship metrics
		RecalculateMass();
		RecalculateVolume();
		RecalculateAcceleration();
	}

	public void RemoveCargoPod()
	{
		// remove one cargo pod
		m_numCargoPods--;

		// recalculate ship metrics
		RecalculateMass();
		RecalculateVolume();
		RecalculateAcceleration();
	}

	public int GetRemainingVolume()
	{
		// calculate and return the amount of space remaining in the cargo hold
		return m_volume - m_volumeUsed;
	}

	public void AddElement( int elementId, int volume )
	{
		// ensure storage exists
		if ( m_elementStorage == null )
		{
			m_elementStorage = new PD_ElementStorage();
			m_elementStorage.Reset();
		}

		// add the element to storage
		m_elementStorage.Add( elementId, volume );

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	public void RemoveElement( int elementId, int volume )
	{
		// ensure storage exists
		if ( m_elementStorage == null )
		{
			return; // nothing to remove if storage doesn't exist
		}

		// remove the element from storage
		m_elementStorage.Remove( elementId, volume );

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	public void AddArtifact( int artifactId )
	{
		// ensure storage exists
		if ( m_artifactStorage == null )
		{
			m_artifactStorage = new PD_ArtifactStorage();
			m_artifactStorage.Reset();
		}

		// add the artifact to storage
		m_artifactStorage.Add( artifactId );

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	// true if this artifact (by its name in the game data) is in the cargo hold
	public bool HasArtifact( string artifactName )
	{
		var artifactId = DataController.m_instance.m_gameData.FindArtifactId( artifactName );

		return ( artifactId >= 0 ) && ( m_artifactStorage != null ) && ( m_artifactStorage.Find( artifactId ) != null );
	}

	// the Endurium (in cubic meters) the engines use to fly this many starmap coordinates in hyperspace (the engines use their class's fuel for every coordinate,
	// as the ship's update takes it, and half of it with the Tesseract)
	public float GetFuelForDistance( float coordinates )
	{
		return Mathf.Max( 0.0f, coordinates ) * GetEngines().m_fuelUsedPerCoordinate * GetEngineFuelFactor();
	}

	// how much of the fuel the engines would use they do use: the Tesseract "projects a field which doubles the energy efficiency of a ship's engines" (the Starport's
	// analysis)
	public float GetEngineFuelFactor()
	{
		return HasArtifact( "Tesseract" ) ? 0.5f : 1.0f;
	}

	public void RemoveArtifact( int artifactId )
	{
		// ensure storage exists
		if ( m_artifactStorage == null )
		{
			return; // nothing to remove if storage doesn't exist
		}

		// remove the artifact from storage
		m_artifactStorage.Remove( artifactId );

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	public void RecalculateVolumeUsed()
	{
		// get total volume used up for artifacts and elements (with null checks)
		int artifactVolume = ( m_artifactStorage != null ) ? m_artifactStorage.m_volumeUsed : 0;
		int elementVolume = ( m_elementStorage != null ) ? m_elementStorage.m_volumeUsed : 0;
		m_volumeUsed = artifactVolume + elementVolume;
	}

	public void RaiseShields()
	{
		if ( !m_shieldsAreUp )
		{
			// the shields come up with whatever charge they have (raising them does not recharge them)
			ValidateShieldCharge();

			m_shieldsAreUp = true;

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Shields raised.</color>" );
		}
	}

	public void DropShields()
	{
		if ( m_shieldsAreUp )
		{
			// the shields keep their charge while they are down
			m_shieldsAreUp = false;

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Shields dropped.</color>" );
		}
	}

	// call this before using the shield charge - it makes sure the charge is one the installed shielding can have
	public void ValidateShieldCharge()
	{
		// lowering the shields used to throw the charge away, so a save file from back then has none while its shields are down - give it a full charge once
		if ( !m_shieldChargeIsKept )
		{
			m_shieldChargeIsKept = true;

			if ( !m_shieldsAreUp )
			{
				m_shieldPoints = GetMaximumShieldPoints();
			}
		}

		// never less than nothing and never more than the installed shielding can hold
		m_shieldPoints = Mathf.Clamp( m_shieldPoints, 0, GetMaximumShieldPoints() );
	}

	// put the shields back at full charge (starport does this while the ship is docked)
	public void RechargeShieldsFully()
	{
		m_shieldChargeIsKept = true;
		m_shieldPoints = GetMaximumShieldPoints();
		m_shieldRechargeTimer = 0.0f;
	}

	// call this every frame during spaceflight - the shields slowly regain their charge whether they are up or down
	public void UpdateShields( float deltaTime )
	{
		ValidateShieldCharge();

		// are the shields fully charged already?
		var maximumPoints = GetMaximumShieldPoints();

		if ( m_shieldPoints >= maximumPoints )
		{
			// yes - nothing to do
			m_shieldRechargeTimer = 0.0f;

			return;
		}

		// no - update the timer
		m_shieldRechargeTimer += deltaTime;

		// is it time for the shields to regain some charge?
		if ( m_shieldRechargeTimer >= c_shieldRechargeInterval )
		{
			// yes - one percent of the full charge for every interval that has passed
			var intervals = Mathf.FloorToInt( m_shieldRechargeTimer / c_shieldRechargeInterval );

			m_shieldRechargeTimer -= intervals * c_shieldRechargeInterval;

			m_shieldPoints = Mathf.Min( maximumPoints, m_shieldPoints + intervals * Mathf.Max( 1, maximumPoints / 100 ) );
		}
	}

	// the number of armor points the engineer repairs each second (zero if there is no engineer who can work)
	public float GetRepairRate()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// nothing gets repaired without an engineer
		if ( !playerData.m_crewAssignment.IsAssigned( PD_CrewAssignment.Role.Engineer ) )
		{
			return 0.0f;
		}

		var engineer = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Engineer );

		// or with one who is incapacitated
		if ( engineer.m_vitality <= 0 )
		{
			return 0.0f;
		}

		// the better the engineer the faster the repairs
		return Mathf.Max( c_minimumRepairRate, engineer.m_engineering * c_repairRatePerSkillPoint );
	}

	// the number of seconds it will take to finish repairing the armor (zero if there is nothing to repair or nobody to do it)
	public float GetRepairTimeRemaining()
	{
		var repairRate = GetRepairRate();
		var pointsToRepair = GetMaximumArmorPoints() - m_armorPoints;

		if ( ( repairRate <= 0.0f ) || ( pointsToRepair <= 0 ) )
		{
			return 0.0f;
		}

		return Mathf.Max( 0.0f, pointsToRepair - m_repairProgress ) / repairRate;
	}

	// call this to have the engineer start repairing the armor
	// how whole a repair item is, from 0 to 100 (the hull by its armor points)
	public int GetRepairItemHealth( int item )
	{
		if ( item == c_hullRepairItem )
		{
			var maximum = GetMaximumArmorPoints();

			return ( maximum > 0 ) ? Mathf.Clamp( Mathf.FloorToInt( m_armorPoints * 100.0f / maximum ), 0, 100 ) : 100;
		}

		return GetSystemHealth( (ShipSystem) ( item - 1 ) );
	}

	// true if the ship has this repair item (the hull always)
	public bool HasRepairItem( int item )
	{
		return ( item == c_hullRepairItem ) || ( ( item > 0 ) && ( item < c_repairItemCount ) && HasSystem( (ShipSystem) ( item - 1 ) ) );
	}

	// the name of a repair item, as the engineer says it
	public string GetRepairItemName( int item )
	{
		return ( item == c_hullRepairItem ) ? GetArmorName() : GetSystemName( (ShipSystem) ( item - 1 ) ).ToLower();
	}

	// the most damaged item the ship has (the engineer repairs the worst first) - -1 if everything is whole
	public int FindWorstRepairItem()
	{
		var worst = -1;
		var worstHealth = 100;

		for ( var item = 0; item < c_repairItemCount; item++ )
		{
			if ( !HasRepairItem( item ) )
			{
				continue;
			}

			var health = GetRepairItemHealth( item );

			if ( health < worstHealth )
			{
				worst = item;
				worstHealth = health;
			}
		}

		return worst;
	}

	// the element id of a repair item's mineral (-1 if the game data has no such element)
	public int GetRepairMineralId( int item )
	{
		var gameData = DataController.m_instance.m_gameData;

		if ( ( item < 0 ) || ( item >= c_repairMinerals.Length ) )
		{
			return -1;
		}

		for ( var elementId = 0; elementId < gameData.m_elementList.Length; elementId++ )
		{
			if ( gameData.m_elementList[ elementId ].m_name == c_repairMinerals[ item ] )
			{
				return elementId;
			}
		}

		return -1;
	}

	// how much of its mineral a repair item needs to be whole again, in tenths of a cubic meter (a cubic meter for every tenth missing, rounded up)
	public int GetRepairMineralNeeded( int item )
	{
		var missing = 100 - GetRepairItemHealth( item );

		return ( missing <= 0 ) ? 0 : ( ( missing + 9 ) / 10 ) * c_repairMineralPerTenth;
	}

	// call this when the engineer starts on a repair item
	public void StartRepairs( int item )
	{
		m_repairItem = item;

		StartRepairs();
	}

	public void StartRepairs()
	{
		m_repairsAreUnderWay = true;
		m_repairProgress = 0.0f;
	}

	// call this every frame during spaceflight - the engineer repairs the armor a little at a time
	public void UpdateRepairs( float deltaTime )
	{
		// nothing to do unless the engineer has been told to repair the armor
		if ( !m_repairsAreUnderWay )
		{
			return;
		}

		// a system rather than the hull?
		if ( ( m_repairItem > c_hullRepairItem ) && ( m_repairItem < c_repairItemCount ) )
		{
			UpdateSystemRepairs( deltaTime );

			return;
		}

		// a destroyed ship is beyond repair (and it has to stay destroyed, a destroyed ship is never saved)
		if ( m_armorPoints <= 0 )
		{
			m_repairsAreUnderWay = false;

			return;
		}

		// is there anything left to repair? (the armor may have been replaced or sold at starport in the meantime - a ship with no armor plating is repaired up to the points of its bare hull)
		var maximumPoints = GetMaximumArmorPoints();

		if ( m_armorPoints >= maximumPoints )
		{
			// no - the engineer is done
			m_repairsAreUnderWay = false;
			m_repairProgress = 0.0f;

			return;
		}

		// the repairs stop if the engineer is gone or incapacitated
		var repairRate = GetRepairRate();

		if ( repairRate <= 0.0f )
		{
			m_repairsAreUnderWay = false;

			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Repairs have stopped. No engineer is available!</color>" );

			return;
		}

		// repair a little more
		m_repairProgress += repairRate * deltaTime;

		// move the whole armor points that have been repaired over to the armor
		var pointsRepaired = Mathf.FloorToInt( m_repairProgress );

		m_repairProgress -= pointsRepaired;

		m_armorPoints = Mathf.Min( maximumPoints, m_armorPoints + pointsRepaired );

		// is the armor whole again?
		if ( m_armorPoints >= maximumPoints )
		{
			// yes - the engineer is done
			m_repairsAreUnderWay = false;
			m_repairProgress = 0.0f;

			SpaceflightController.m_instance.m_messages.AddText( "<color=green>Repairs on the " + GetArmorName() + " all completed, sir.</color>" );

			// and on to the next
			RepairNext();
		}
	}

	// the engineer repairs a system a little at a time, at the pace the hull is repaired at (the same share of the whole in the same time)
	void UpdateSystemRepairs( float deltaTime )
	{
		var system = (ShipSystem) ( m_repairItem - 1 );

		// is there anything left to repair?
		if ( GetSystemHealth( system ) >= 100 )
		{
			m_repairsAreUnderWay = false;
			m_repairProgress = 0.0f;

			return;
		}

		var repairRate = GetRepairRate();
		var maximumArmor = GetMaximumArmorPoints();

		if ( ( repairRate <= 0.0f ) || ( maximumArmor <= 0 ) )
		{
			m_repairsAreUnderWay = false;

			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Repairs have stopped. No engineer is available!</color>" );

			return;
		}

		// repair a little more (in whole points of health)
		m_repairProgress += repairRate * 100.0f / maximumArmor * deltaTime;

		var pointsRepaired = Mathf.FloorToInt( m_repairProgress );

		m_repairProgress -= pointsRepaired;

		SetSystemHealth( system, GetSystemHealth( system ) + pointsRepaired );

		if ( GetSystemHealth( system ) >= 100 )
		{
			m_repairsAreUnderWay = false;
			m_repairProgress = 0.0f;

			SpaceflightController.m_instance.m_messages.AddText( "<color=green>Repairs on the " + GetRepairItemName( m_repairItem ) + " all completed, sir.</color>" );

			RepairNext();
		}
	}

	// once a repair is done the engineer moves on to the most damaged item left (and asks for its mineral)
	void RepairNext()
	{
		if ( ( SpaceflightController.m_instance != null ) && ( FindWorstRepairItem() >= 0 ) )
		{
			RepairButton.AskForRepair( FindWorstRepairItem() );
		}
	}

	public void ArmWeapons()
	{
		if ( !m_weaponsAreArmed )
		{
			m_weaponsAreArmed = true;

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Weapons armed.</color>" );
		}
	}

	public void DisarmWeapons()
	{
		if ( m_weaponsAreArmed )
		{
			m_weaponsAreArmed = false;

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Weapons disarmed.</color>" );
		}
	}

	// returns true if there is any endurium on board (the engines, the shields and the weapons all run on it)
	public bool HasFuel()
	{
		return ( m_elementStorage != null ) && ( m_elementStorage.Find( 5 ) != null );
	}

	public void UseUpFuel( float amount )
	{
		// add to the running amount of fuel used up
		m_fuelUsed += amount;

		// have we used up more than 0.1 units?
		if ( m_fuelUsed >= 0.1f )
		{
			// yes - deduct 0.1 unit from storage (if there is any left)
			if ( m_elementStorage.Find( 5 ) != null )
			{
				m_elementStorage.Remove( 5, 1 );
			}

			// recalculate the volume used up in the cargo bays
			RecalculateVolumeUsed();

			// adjust running amount of fuel used up
			m_fuelUsed -= 0.1f;

			// get the amount of enduruium remaining in storage
			var elementReference = m_elementStorage.Find( 5 );

			// are we out of fuel?
			if ( elementReference == null )
			{
				// yes - lower the shields
				DropShields();
			}
		}
	}
}
