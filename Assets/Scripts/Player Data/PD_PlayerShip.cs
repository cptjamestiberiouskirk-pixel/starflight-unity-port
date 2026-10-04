
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
	public int GetMaximumArmorPoints()
	{
		return HasArmorPlating() ? GetArmor().m_points : c_bareHullArmorPoints;
	}

	// the most shield points this ship can have - the full charge of the shielding that is installed (zero if there is none)
	public int GetMaximumShieldPoints()
	{
		return Mathf.Max( 0, GetShielding().m_points );
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
