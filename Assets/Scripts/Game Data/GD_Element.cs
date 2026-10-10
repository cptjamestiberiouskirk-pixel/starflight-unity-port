
using System;

[Serializable]

public class GD_Element
{
	public int m_id;
	public string m_name;
	public int m_starportPrice;
	public int m_actualValue;
	public int m_valueRank;
	public int m_initialVolume;
	public bool m_availableInStarport;

	// the Starport buys Endurium for what it sells it for, and the price rises over time (STRINFO 1.4: "Endurium which is equally valued whether being bought or
	// sold", "1000, 1500, 2000 (price rises over time)"; the notices of 20-02-4620 and 15-05-4620 raise it to 1500 and then 2000 M.U. per cubic meter)
	const int c_enduriumPrice = 1000;
	const string c_firstEnduriumRise = "4620-02-20";
	const int c_firstEnduriumRisePrice = 1500;
	const string c_secondEnduriumRise = "4620-05-15";
	const int c_secondEnduriumRisePrice = 2000;

	// true if this is Endurium
	bool IsEndurium()
	{
		return m_id == DataController.m_instance.m_gameData.m_misc.m_enduriumElementId;
	}

	// the price of Endurium today
	static int GetEnduriumPrice()
	{
		var today = DataController.m_instance.m_playerData.m_general.m_currentStardateYMD;

		if ( string.CompareOrdinal( today, c_secondEnduriumRise ) >= 0 )
		{
			return c_secondEnduriumRisePrice;
		}

		if ( string.CompareOrdinal( today, c_firstEnduriumRise ) >= 0 )
		{
			return c_firstEnduriumRisePrice;
		}

		return c_enduriumPrice;
	}

	// what the Starport sells a cubic meter of this element for
	public int GetStarportPrice()
	{
		return IsEndurium() ? GetEnduriumPrice() : m_starportPrice;
	}

	// what the Starport pays for a cubic meter of this element
	public int GetActualValue()
	{
		return IsEndurium() ? GetEnduriumPrice() : m_actualValue;
	}
}
