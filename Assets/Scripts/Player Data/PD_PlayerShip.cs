
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
	public int m_missilesRemaining;

	// how many seconds it takes the shields to regain one percent of their full charge
	public const float c_shieldRechargeInterval = 5.0f;

	// true once the shield charge is kept when the shields are lowered (false in save files from before that - see ValidateShieldCharge)
	public bool m_shieldChargeIsKept;

	// how long it has been since the shields last regained some charge
	public float m_shieldRechargeTimer;

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
				m_shieldPoints = GetShielding().m_points;
			}
		}

		// never less than nothing and never more than the installed shielding can hold
		m_shieldPoints = Mathf.Clamp( m_shieldPoints, 0, GetShielding().m_points );
	}

	// put the shields back at full charge (starport does this while the ship is docked)
	public void RechargeShieldsFully()
	{
		m_shieldChargeIsKept = true;
		m_shieldPoints = GetShielding().m_points;
		m_shieldRechargeTimer = 0.0f;
	}

	// call this every frame during spaceflight - the shields slowly regain their charge whether they are up or down
	public void UpdateShields( float deltaTime )
	{
		ValidateShieldCharge();

		// are the shields fully charged already?
		var maximumPoints = GetShielding().m_points;

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
