
using UnityEngine;
using System;

[Serializable]

public class PD_PlayerShip
{
	// the armor points of a ship with no armor plating installed (the bare hull)
	public const int c_bareHullArmorPoints = 250;

	// the acceleration is the engines' class times this over the ship's mass in tons (SHIPGRPH-OV (.ACC): 0x01f4)
	const int c_accelerationPerEngineClassTon = 500;

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

	// the lifeforms captured and put in stasis (planet.txt) - null in older save files
	public PD_SpecimenStorage m_specimenStorage;

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

	// the ship's systems (the original's damage screen has a bar for the hull, engines, sensors, comm, shield, missile and laser - SS Damage) - the order is
	// saved, append and never reorder: the hull came last (its integrity, the original's ship field 0x11, DAMAGE-OV WEB53), so in save files from before it the
	// hull is whole
	public enum ShipSystem
	{
		Engines,
		Sensors,
		Comm,
		Shields,
		Missiles,
		Lasers,
		Hull,
		Count
	}

	// how whole each system is, from 0 (destroyed) to 100 (undamaged), by ShipSystem (null in save files from before the systems could be damaged, one short in
	// those from before the hull had an integrity of its own)
	public int[] m_systemHealth;

	// what the engineer is repairing, in the order of the original's Repair (REPAIR-OV >FIELD): the hull, or a system by its ShipSystem plus 1 (0, the hull, in
	// save files from before the systems could be damaged). The hull is its integrity: the engineer never repairs the armor, which only the Starport restores, as
	// in the original (REPAIR-OV has the armor's field and never uses it)
	public int m_repairItem;
	public const int c_hullRepairItem = 0;
	public const int c_repairItemCount = (int) ShipSystem.Count;

	// the repair mineral of the hull and of each system, by its name in the game data, and how much of it the engineer needs: a cubic meter for every tenth
	// repaired (decided 2026-10-11 for roadmap 1.2 - the original's engineer asks for "2 cubic meters of Molybdenum", "5 cubic meters of Aluminum",
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

		m_specimenStorage = new PD_SpecimenStorage();

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
	// what the Starport charges to repair the ship, as the original's (CONFIG-OV WF226): 4 M.U. a missing armor point; for the engines, shields, missile launcher
	// and laser cannon the price of the installed class in hundreds of M.U. times the damage in percent, plus 5% ((100 - health) * 105 / 100); and 100 M.U. a
	// point of damage to the sensors and the comm, and 250 M.U. a point of the hull's integrity
	public int GetStarportRepairCost()
	{
		var gameData = DataController.m_instance.m_gameData;
		long cost = 0;

		var missingPoints = GetMaximumArmorPoints() - m_armorPoints;

		if ( missingPoints > 0 )
		{
			cost += (long) missingPoints * c_repairPricePerArmorPoint;
		}

		cost += (long) ( 100 - GetHullIntegrity() ) * c_repairPricePerHullPoint;

		cost += GetPartRepairCost( gameData.m_enginesList, m_enginesClass, ShipSystem.Engines );
		cost += GetPartRepairCost( gameData.m_shieldingList, m_shieldingClass, ShipSystem.Shields );
		cost += GetPartRepairCost( gameData.m_missileLauncherList, m_missileLauncherClass, ShipSystem.Missiles );
		cost += GetPartRepairCost( gameData.m_laserCannonList, m_laserCannonClass, ShipSystem.Lasers );

		cost += (long) ( 200 - GetSystemHealth( ShipSystem.Sensors ) - GetSystemHealth( ShipSystem.Comm ) ) * c_repairPricePerSensorAndCommPoint;

		return (int) Mathf.Min( cost, int.MaxValue );
	}

	// what the Starport charges for one armor point, for one point of the hull's integrity, and for one point of damage to the sensors or the comm (CONFIG-OV WF226)
	const int c_repairPricePerArmorPoint = 4;
	const int c_repairPricePerHullPoint = 250;
	const int c_repairPricePerSensorAndCommPoint = 100;

	// the repair of an installed part: its class's price in hundreds of M.U. times its damage in percent plus 5% (CONFIG-OV WF1D6), nothing for a part that is not installed
	long GetPartRepairCost( GD_ShipPart[] partList, int partClass, ShipSystem system )
	{
		if ( ( partList == null ) || ( partClass < 1 ) || ( partClass >= partList.Length ) )
		{
			return 0;
		}

		var damage = 100 - Mathf.Clamp( GetSystemHealth( system ), 0, 100 );

		return (long) ( partList[ partClass ].m_buyPrice / 100 ) * ( damage * 105 / 100 );
	}

	// what the Starport pays for an installed part, as the original's (CONFIG-OV WECB0): its class's price in hundreds of M.U. times its condition over 105, in
	// hundreds of M.U. again, at least 5 - the game data's sell price when the part is whole. The armor has no condition of its own (its damage is the armor
	// points), so it sells whole. The parts are numbered as the ship configuration numbers them: 1 engines, 2 shields, 3 armor, 4 missiles, 5 lasers
	public int GetPartResaleValue( int partIndex, GD_ShipPart part )
	{
		if ( part == null )
		{
			return 0;
		}

		var condition = 100;

		switch ( partIndex )
		{
			case 1: condition = GetSystemHealth( ShipSystem.Engines ); break;
			case 2: condition = GetSystemHealth( ShipSystem.Shields ); break;
			case 4: condition = GetSystemHealth( ShipSystem.Missiles ); break;
			case 5: condition = GetSystemHealth( ShipSystem.Lasers ); break;
		}

		condition = Mathf.Clamp( condition, 0, 100 );

		var value = (long) ( part.m_buyPrice / 100 ) * condition / 105 * 100;

		return (int) Mathf.Max( value, 5 );
	}

	// a part just bought is whole (the parts are numbered as the ship configuration numbers them; the armor has no condition of its own)
	public void InstallNewPart( int partIndex )
	{
		switch ( partIndex )
		{
			case 1: SetSystemHealth( ShipSystem.Engines, 100 ); break;
			case 2: SetSystemHealth( ShipSystem.Shields, 100 ); break;
			case 4: SetSystemHealth( ShipSystem.Missiles, 100 ); break;
			case 5: SetSystemHealth( ShipSystem.Lasers, 100 ); break;
		}
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

	// how much of its work a system does, from 0 to 1: a damaged system works in proportion to its health (decided 2026-10-11 for roadmap 1.2)
	public float GetSystemFactor( ShipSystem system )
	{
		return GetSystemHealth( system ) / 100.0f;
	}

	// how whole the hull is, from 0 to 100 (the original's hull integrity) - the ship is destroyed when it reaches 0, whatever is left of its armor (DAMAGE-OV
	// WF3A4: "!!HULL DESTROYED!!")
	public int GetHullIntegrity()
	{
		return GetSystemHealth( ShipSystem.Hull );
	}

	// true once the ship has been destroyed (a destroyed ship is never saved, and nothing may make its hull whole again)
	public bool IsDestroyed()
	{
		return GetHullIntegrity() <= 0;
	}

	// call this when the ship is destroyed (the game is lost and is never saved)
	public void DestroyHull()
	{
		SetSystemHealth( ShipSystem.Hull, 0 );
	}

	// what is left of the ship as the status display's DAMAGE reads it, in percent (SCIENCE WE760): the average health of the hull and of the systems the ship has,
	// its armor left out, and nothing at all once the hull's integrity is down to 19 or less
	public int GetOverallIntegrity()
	{
		var total = 0;
		var counted = 0;

		for ( var system = 0; system < (int) ShipSystem.Count; system++ )
		{
			if ( HasSystem( (ShipSystem) system ) )
			{
				total += GetSystemHealth( (ShipSystem) system );
				counted++;
			}
		}

		return ( GetHullIntegrity() > 19 ) ? total / Mathf.Max( 1, counted ) : 0;
	}

	// the health below which the sensors give no readings (decided 2026-10-11)
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
			case ShipSystem.Hull: return "Hull";
			default: return "?";
		}
	}

	// how many of the systems the ship has
	public int GetInstalledSystemCount()
	{
		var installed = 0;

		for ( var system = 0; system < (int) ShipSystem.Count; system++ )
		{
			if ( HasSystem( (ShipSystem) system ) )
			{
				installed++;
			}
		}

		return installed;
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

	// true if a chunk of damage can land on this system: it is installed and still works (DAMAGE-OV #>?FIEL)
	public bool CanBeDamaged( ShipSystem system )
	{
		return HasSystem( system ) && ( GetSystemHealth( system ) > 0 );
	}

	// a chunk of damage that got past the shields and the armor lands on a system: it loses the chunk times 10 over the hull's integrity, so the weaker the hull
	// the more it loses (DAMAGE-OV WF378 and WEC46, never below 0) - returns what the system lost
	public int DamageSystem( ShipSystem system, int chunk )
	{
		var before = GetSystemHealth( system );

		SetSystemHealth( system, before - GetSystemDamage( chunk ) );

		return before - GetSystemHealth( system );
	}

	// what a chunk of damage takes off a system once the armor is gone (DAMAGE-OV WF378: 10 times the chunk over the hull's integrity)
	public int GetSystemDamage( int chunk )
	{
		return chunk * 10 / Mathf.Max( 1, GetHullIntegrity() );
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

		// never more than the most this ship can have, and never below 0 (the old damage took armor points below 0, and a ship is destroyed by its hull, not its
		// armor: this never adds a usable point)
		m_armorPoints = Mathf.Clamp( m_armorPoints, 0, GetMaximumArmorPoints() );
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
		// the original's: the engines' class times 500 over the ship's mass, in whole G (SHIPGRPH-OV (.ACC)) - the port's curve before this gave 6 G where the
		// original reads 7 (starflt_076), and went wrong above 500 tons
		m_acceleration = ( m_mass > 0 ) ? ( m_enginesClass * c_accelerationPerEngineClassTon / m_mass ) : 0;
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

	// takes this artifact (by its name in the game data) out of the cargo hold - false if it is not there
	public bool RemoveArtifact( string artifactName )
	{
		if ( !HasArtifact( artifactName ) )
		{
			return false;
		}

		m_artifactStorage.Remove( DataController.m_instance.m_gameData.FindArtifactId( artifactName ) );

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();

		return true;
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
		m_volumeUsed = artifactVolume + elementVolume + ( ( m_specimenStorage != null ) ? m_specimenStorage.GetVolumeUsed() : 0 );
	}

	// the specimens in the hold (made in an older save file that has none)
	public PD_SpecimenStorage GetSpecimenStorage()
	{
		if ( m_specimenStorage == null )
		{
			m_specimenStorage = new PD_SpecimenStorage();
		}

		return m_specimenStorage;
	}

	public void AddSpecimen( PD_Specimen specimen )
	{
		GetSpecimenStorage().Add( specimen );

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	public void RemoveSpecimen( PD_Specimen specimen )
	{
		GetSpecimenStorage().Remove( specimen );

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	public void RaiseShields()
	{
		if ( !m_shieldsAreUp )
		{
			// the shields come up with whatever charge they have (raising them does not recharge them)
			ValidateShieldCharge();

			m_shieldsAreUp = true;

			// in the original's words (NAVIGATR WF359: SHIELDS ARE NOW UP)
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Shields are now up.</color>" );
		}
	}

	public void DropShields()
	{
		if ( m_shieldsAreUp )
		{
			// the shields keep their charge while they are down
			m_shieldsAreUp = false;

			// in the original's words (NAVIGATR >DOWN-SHIELD: SHIELDS ARE DOWN)
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Shields are down.</color>" );
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

	// the number of seconds it will take to finish the repair that is under way (zero if there is nothing to repair or nobody to do it) - every repair item is
	// repaired at the same pace, the same share of the whole in the same time as the bare hull's armor at the engineer's rate
	public float GetRepairTimeRemaining()
	{
		var repairRate = GetRepairRate();
		var maximumArmor = GetMaximumArmorPoints();
		var pointsToRepair = 100 - GetRepairItemHealth( m_repairItem );

		if ( ( repairRate <= 0.0f ) || ( maximumArmor <= 0 ) || ( pointsToRepair <= 0 ) )
		{
			return 0.0f;
		}

		return Mathf.Max( 0.0f, pointsToRepair - m_repairProgress ) / ( repairRate * 100.0f / maximumArmor );
	}

	// the system a repair item is (the hull first, then the systems in their order)
	public static ShipSystem GetRepairItemSystem( int item )
	{
		return ( item == c_hullRepairItem ) ? ShipSystem.Hull : (ShipSystem) ( item - 1 );
	}

	// how whole a repair item is, from 0 to 100 (100 for one the ship cannot have)
	public int GetRepairItemHealth( int item )
	{
		if ( ( item < 0 ) || ( item >= c_repairItemCount ) )
		{
			return 100;
		}

		return GetSystemHealth( GetRepairItemSystem( item ) );
	}

	// true if a repair item works at all (the original's Repair shows "CURRENT FUNCTIONALITY: OPERATIVE" or "INOPERATIVE"): a system works in proportion to its
	// health, and not at all at 0, and the sensors give no readings below c_sensorsNeedHealth (decided 2026-10-11); the hull while it has any
	// integrity
	public bool IsRepairItemOperative( int item )
	{
		if ( ( item < 0 ) || ( item >= c_repairItemCount ) )
		{
			return false;
		}

		var system = GetRepairItemSystem( item );
		var health = GetSystemHealth( system );

		return ( system == ShipSystem.Sensors ) ? ( health >= c_sensorsNeedHealth ) : ( health > 0 );
	}

	// call this when the engineer stops repairing (the original's "CEASING REPAIRS ON ...")
	public void StopRepairs()
	{
		m_repairsAreUnderWay = false;
		m_repairProgress = 0.0f;
	}

	// true if the ship has this repair item (the hull always)
	public bool HasRepairItem( int item )
	{
		return ( item >= 0 ) && ( item < c_repairItemCount ) && HasSystem( GetRepairItemSystem( item ) );
	}

	// the name of a repair item, as the engineer says it
	public string GetRepairItemName( int item )
	{
		return GetSystemName( GetRepairItemSystem( item ) ).ToLower();
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

	// call this every frame during spaceflight - the engineer repairs the hull's integrity or a system a little at a time
	public void UpdateRepairs( float deltaTime )
	{
		// nothing to do unless the engineer has been told to repair something
		if ( !m_repairsAreUnderWay )
		{
			return;
		}

		// a destroyed ship is beyond repair (and it has to stay destroyed, a destroyed ship is never saved)
		if ( IsDestroyed() || ( m_repairItem < 0 ) || ( m_repairItem >= c_repairItemCount ) )
		{
			m_repairsAreUnderWay = false;

			return;
		}

		UpdateSystemRepairs( deltaTime );
	}

	// the engineer repairs the hull's integrity or a system a little at a time, at the same pace for each (the same share of the whole in the same time as the
	// armor points of the ship's armor at the engineer's rate)
	void UpdateSystemRepairs( float deltaTime )
	{
		var system = GetRepairItemSystem( m_repairItem );

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

			// the original's line (REPAIR-OV: "REPAIR COMPLETED", and the engineer waits to be told what to repair next)
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Repair completed.</color>" );
		}
	}

	// arms the weapons that work, saying which (NAVIGATR WF3FB: ARMING MISSILES, ARMING LASERS) - a ship whose weapons are all destroyed arms nothing
	public void ArmWeapons()
	{
		if ( m_weaponsAreArmed )
		{
			return;
		}

		var missiles = ( m_missileLauncherClass > 0 ) && ( GetSystemHealth( ShipSystem.Missiles ) > 0 );
		var lasers = ( m_laserCannonClass > 0 ) && ( GetSystemHealth( ShipSystem.Lasers ) > 0 );

		if ( missiles )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Arming missiles.</color>" );
		}

		if ( lasers )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Arming lasers.</color>" );
		}

		m_weaponsAreArmed = missiles || lasers;
	}

	public void DisarmWeapons()
	{
		if ( m_weaponsAreArmed )
		{
			m_weaponsAreArmed = false;

			// in the original's words (NAVIGATR >DISARM: WEAPONS ARE DISARMED)
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Weapons are disarmed.</color>" );
		}
	}

	// the planet's gravity as the landing computer counts it, in hundredths of a G: above 8 G it grows by the square root (LAND-OV WF147: SQRT(G - 800) * 10 + 800)
	public static int GetLandingGravity( GD_Planet planet )
	{
		var gravity = Mathf.Max( 0, planet.m_gravity );

		if ( gravity > c_landingGravityKnee )
		{
			gravity = Mathf.FloorToInt( Mathf.Sqrt( gravity - c_landingGravityKnee ) ) * 10 + c_landingGravityKnee;
		}

		return gravity;
	}

	const int c_landingGravityKnee = 800;

	// how much Endurium a landing or a launch burns, in tenths of a cubic meter: the gravity over 40 (LAND-OV WF177)
	public static int GetLandingFuel( GD_Planet planet )
	{
		return GetLandingGravity( planet ) / 40;
	}

	// whether the planet's gravity crushes the hull of a ship that lands (LAND-OV WF3D6: more than 7.99 G)
	public static bool GravityCrushesHull( GD_Planet planet )
	{
		return GetLandingGravity( planet ) > 799;
	}

	// the Endurium on board in tenths of a cubic meter, counted as the landing computer counts it (at most 320, LAND-OV WF32C and WF37B)
	int GetLandingFuelOnBoard()
	{
		var endurium = ( m_elementStorage != null ) ? m_elementStorage.Find( 5 ) : null;

		return ( endurium == null ) ? 0 : Mathf.Min( endurium.m_volume, 320 );
	}

	// whether there is too little Endurium to land safely (LAND-OV WF32C: the fuel times 40 below the gravity) - a ship that goes down anyway crashes
	public bool HasTooLittleFuelToLand( GD_Planet planet )
	{
		return GetLandingFuelOnBoard() * 40 < GetLandingGravity( planet );
	}

	// whether there is too little Endurium to launch again (LAND-OV WF37B): on the surface for the launch, in orbit for a landing and the launch after it
	public bool HasTooLittleFuelToRelaunch( GD_Planet planet, bool onTheSurface )
	{
		var fuel = GetLandingFuelOnBoard();

		return ( fuel + ( onTheSurface ? fuel : 0 ) ) * 20 < GetLandingGravity( planet );
	}

	// burns the Endurium of a landing or a launch (LAND-OV WF177: the gravity over 40, in tenths, never below none)
	public void BurnLandingFuel( GD_Planet planet )
	{
		var endurium = ( m_elementStorage != null ) ? m_elementStorage.Find( 5 ) : null;

		if ( endurium == null )
		{
			return;
		}

		var tenths = Mathf.Min( GetLandingFuel( planet ), endurium.m_volume );

		if ( tenths <= 0 )
		{
			return;
		}

		m_elementStorage.Remove( 5, tenths );

		RecalculateVolumeUsed();

		if ( m_elementStorage.Find( 5 ) == null )
		{
			DropShields();
		}
	}

	// returns true if there is any endurium on board (the engines, the shields and the weapons all run on it)
	public bool HasFuel()
	{
		return ( m_elementStorage != null ) && ( m_elementStorage.Find( 5 ) != null );
	}

	// what a float may fall short of a tenth of a cubic meter and still count as one
	const float c_fuelAllowance = 0.0001f;

	public void UseUpFuel( float amount )
	{
		// add to the running amount of fuel used up
		m_fuelUsed += amount;

		// deduct every 0.1 unit used up (a missile uses up five at once; the little allowance keeps a float's 0.0999... from holding one back, and the count is a
		// guard)
		for ( var tenths = 0; ( m_fuelUsed >= 0.1f - c_fuelAllowance ) && ( tenths < 10000 ); tenths++ )
		{
			// deduct 0.1 unit from storage (if there is any left)
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
