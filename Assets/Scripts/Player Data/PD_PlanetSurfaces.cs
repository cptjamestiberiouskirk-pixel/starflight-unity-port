
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

	public void Reset()
	{
		m_depositList = new List<Deposit>();
	}

	// make sure there is a list (save files from before this have none)
	public void Validate()
	{
		if ( m_depositList == null )
		{
			m_depositList = new List<Deposit>();
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
