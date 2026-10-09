
using System;
using System.Collections.Generic;

// what has been taken from the surfaces of the planets - the objects of a planet are placed again from the planet's seed every time the terrain vehicle goes out,
// so without this a deposit that was picked up would be back the next time
[Serializable]

public class PD_PlanetSurfaces
{
	[Serializable]

	public class Deposit
	{
		// the planet, and the deposit by the order it is placed in (the same every time, since the placing is seeded from the planet)
		public int m_planetId;
		public int m_depositIndex;

		// what is left of it, in tenths of a cubic meter (0 once it has all been picked up)
		public int m_volumeLeft;
	}

	public List<Deposit> m_depositList;

	// the artifact sites (ids into the game data's artifact site list) whose artifact has been taken
	public List<int> m_takenArtifactSiteList;

	[Serializable]

	public class DroppedCargo
	{
		public int m_id;

		// where it lies (world x, y and z on the planet's surface - y is where the terrain vehicle was, which floats on water)
		public int m_planetId;
		public float m_x;
		public float m_y;
		public float m_z;

		// what it is: an element and its volume in tenths of a cubic meter, or an artifact (-1 for the one it is not)
		public int m_elementId = -1;
		public int m_volume;
		public int m_artifactId = -1;
	}

	// what the terrain vehicle has dropped on the planets ("when an object is dropped on a planet's surface it can be picked up again" - the manual, page 21)
	public List<DroppedCargo> m_droppedCargoList;
	public int m_nextDroppedCargoId;

	public void Reset()
	{
		m_depositList = new List<Deposit>();
		m_takenArtifactSiteList = new List<int>();
		m_droppedCargoList = new List<DroppedCargo>();
		m_nextDroppedCargoId = 0;
	}

	// make sure there are lists (save files from before them have none)
	public void Validate()
	{
		if ( m_depositList == null )
		{
			m_depositList = new List<Deposit>();
		}

		if ( m_takenArtifactSiteList == null )
		{
			m_takenArtifactSiteList = new List<int>();
		}

		if ( m_droppedCargoList == null )
		{
			m_droppedCargoList = new List<DroppedCargo>();
		}
	}

	// call this when the terrain vehicle drops something - returns what lies on the ground now
	public DroppedCargo AddDroppedCargo( int planetId, float x, float y, float z, int elementId, int volume, int artifactId )
	{
		Validate();

		var droppedCargo = new DroppedCargo { m_id = m_nextDroppedCargoId, m_planetId = planetId, m_x = x, m_y = y, m_z = z, m_elementId = elementId, m_volume = volume, m_artifactId = artifactId };

		m_nextDroppedCargoId++;

		m_droppedCargoList.Add( droppedCargo );

		return droppedCargo;
	}

	// what was dropped with this id (null if it has been picked up again)
	public DroppedCargo FindDroppedCargo( int droppedCargoId )
	{
		Validate();

		foreach ( var droppedCargo in m_droppedCargoList )
		{
			if ( droppedCargo.m_id == droppedCargoId )
			{
				return droppedCargo;
			}
		}

		return null;
	}

	// call this when something that was dropped has been picked up again
	public void RemoveDroppedCargo( int droppedCargoId )
	{
		var droppedCargo = FindDroppedCargo( droppedCargoId );

		if ( droppedCargo != null )
		{
			m_droppedCargoList.Remove( droppedCargo );
		}
	}

	// true if the artifact of this site has been taken
	public bool IsArtifactSiteTaken( int artifactSiteId )
	{
		Validate();

		return m_takenArtifactSiteList.Contains( artifactSiteId );
	}

	// call this when the artifact of a site has been taken
	public void TakeArtifactSite( int artifactSiteId )
	{
		Validate();

		if ( !m_takenArtifactSiteList.Contains( artifactSiteId ) )
		{
			m_takenArtifactSiteList.Add( artifactSiteId );
		}
	}

	// what is left of a deposit that has been picked up from - returns -1 for a deposit nothing has been taken from
	public int GetDepositVolumeLeft( int planetId, int depositIndex )
	{
		Validate();

		foreach ( var deposit in m_depositList )
		{
			if ( ( deposit.m_planetId == planetId ) && ( deposit.m_depositIndex == depositIndex ) )
			{
				return deposit.m_volumeLeft;
			}
		}

		return -1;
	}

	// call this when something has been picked up from a deposit - volumeLeft is what is left of it (0 once it is all gone)
	public void SetDepositVolumeLeft( int planetId, int depositIndex, int volumeLeft )
	{
		Validate();

		foreach ( var deposit in m_depositList )
		{
			if ( ( deposit.m_planetId == planetId ) && ( deposit.m_depositIndex == depositIndex ) )
			{
				deposit.m_volumeLeft = volumeLeft;

				return;
			}
		}

		m_depositList.Add( new Deposit { m_planetId = planetId, m_depositIndex = depositIndex, m_volumeLeft = volumeLeft } );
	}
}
