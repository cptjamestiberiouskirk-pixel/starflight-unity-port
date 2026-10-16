
using UnityEngine;
using System;

[Serializable]

public class PD_TerrainVehicle
{
	public int m_volumeUsed;

	public float m_fuelRemaining;

	public PD_ArtifactStorage m_artifactStorage;
	public PD_ElementStorage m_elementStorage;

	// the lifeforms captured and put in stasis (planet.txt) - null in older save files
	public PD_SpecimenStorage m_specimenStorage;

	// set when the terrain vehicle has been destroyed, until the Starport installs a new one (the original's kernel: "?TV is TVEHICLE available from ship?" -
	// false in older save files)
	public bool m_isLost;

	// whether the stunner is mounted rather than the laser (the manual, page 21: "Your Terrain Vehicle is equipped with a laser and a stunner") - the laser in older
	// save files
	public bool m_stunnerMounted;

	// the fine for a lost terrain vehicle (STRINFO 1.2: "you have been fined 10,000 MU")
	public const int c_lossFine = 10000;

	public void Reset()
	{
		// refuel the terrain vehicle
		Refuel();

		// create and reset the cargo hold to initial game state
		m_artifactStorage = new PD_ArtifactStorage();
		m_elementStorage = new PD_ElementStorage();

		m_artifactStorage.Reset();
		m_elementStorage.Reset();

		m_specimenStorage = new PD_SpecimenStorage();

		// recalculate the used up space in the cargo hold
		RecalculateVolumeUsed();
	}

	public void Refuel()
	{
		// reset fuel remaining to maximum
		m_fuelRemaining = 1.0f;
	}

	public int GetRemainingVolume()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// calculate and return the amount of space remaining in the cargo hold
		return gameData.m_misc.m_terrainVehicleVolume - m_volumeUsed;
	}

	public int GetPercentRemainingVolume()
	{
		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get the remaining volume in cubic meters
		var remainingVolume = GetRemainingVolume();

		// return it as a percentage
		return Mathf.FloorToInt( (float) remainingVolume * 100.0f / (float) gameData.m_misc.m_terrainVehicleVolume );
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

	// true if this artifact is in the terrain vehicle's hold
	public bool HasArtifact( string artifactName )
	{
		var artifactId = DataController.m_instance.m_gameData.FindArtifactId( artifactName );

		return ( artifactId >= 0 ) && ( m_artifactStorage != null ) && ( m_artifactStorage.Find( artifactId ) != null );
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

	public void UseUpFuel( float amount )
	{
		// add to the running amount of fuel remaining
		m_fuelRemaining -= amount;
	}

	public int GetPercentFuelRemaining()
	{
		return Mathf.CeilToInt( m_fuelRemaining * 100.0f );
	}

	// the reserve below the display's 0%: the original's tank holds 32767, its 0% is at 2000 of them and it is empty at none (TV-OV, MOVE-OV), so the reserve is
	// 2000 / 30767 of the full tank, 6.5%
	public const float c_reserve = 2000.0f / 30767.0f;

	// true when the vehicle has used its reserve too
	public bool IsOutOfFuel()
	{
		return m_fuelRemaining <= -c_reserve;
	}
}
