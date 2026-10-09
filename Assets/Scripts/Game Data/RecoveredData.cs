
using System;

// what the original game had and Starflight Game Data.json does not, recovered from the other sources of the original (Research/Data/STRINFO.DOC)
// it is kept in its own file so that the game data file stays the original data - each record names its source in an m_source key, which is only read by people
[Serializable]

public class RecoveredData
{
	public GD_Comm[] m_commList;
}
