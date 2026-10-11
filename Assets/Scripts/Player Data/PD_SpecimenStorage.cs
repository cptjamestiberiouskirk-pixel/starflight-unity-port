
using System;
using System.Collections.Generic;

// the specimens in a cargo hold (the terrain vehicle's or the ship's)
[Serializable]

public class PD_SpecimenStorage
{
	public List<PD_Specimen> m_specimenList = new List<PD_Specimen>();

	// the volume they take up
	public int GetVolumeUsed()
	{
		var volume = 0;

		if ( m_specimenList != null )
		{
			foreach ( var specimen in m_specimenList )
			{
				volume += specimen.m_volume;
			}
		}

		return volume;
	}

	public void Add( PD_Specimen specimen )
	{
		if ( m_specimenList == null )
		{
			m_specimenList = new List<PD_Specimen>();
		}

		m_specimenList.Add( specimen );
	}

	public void Remove( PD_Specimen specimen )
	{
		if ( m_specimenList != null )
		{
			m_specimenList.Remove( specimen );
		}
	}

	// how many there are
	public int GetCount()
	{
		return ( m_specimenList == null ) ? 0 : m_specimenList.Count;
	}
}
