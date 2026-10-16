
using System;

[Serializable]

public class PD_Starport
{
	public string m_lastReadNoticeStardate;
	public PD_ArtifactStorage m_artifactStorage;

	// the price of a cubic meter of Endurium, fixed when the ship last left the Starport (the original sets it only at launch, PORTMENU-OV WEBA2); 0 before the
	// first launch and in a save from before it was kept, when the price of the day is used (GD_Element)
	public int m_enduriumPrice;

	// call this when the ship leaves the Starport: the Endurium price of the day holds until the next launch
	public void FixEnduriumPrice( string today )
	{
		m_enduriumPrice = GD_Element.GetEnduriumPriceOn( today );
	}

	// ensure artifact storage exists (lazy initialization)
	void EnsureArtifactStorage()
	{
		if ( m_artifactStorage == null )
		{
			m_artifactStorage = new PD_ArtifactStorage();
			m_artifactStorage.Reset();
		}
	}

	public void AddArtifact( int artifactId )
	{
		EnsureArtifactStorage();
		m_artifactStorage.Add( artifactId );
	}

	public void RemoveArtifact( int artifactId )
	{
		if ( m_artifactStorage == null )
		{
			return; // nothing to remove if storage doesn't exist
		}
		m_artifactStorage.Remove( artifactId );
	}

	public void Reset()
	{
		// reset the last read notice stardate
		m_lastReadNoticeStardate = "0000-00-00";

		// no launch yet: the Endurium price is the price of the day
		m_enduriumPrice = 0;

		// create and reset the starport artifact storage to initial game state
		m_artifactStorage = new PD_ArtifactStorage();

		m_artifactStorage.Reset();

		// get access to the game data
		var gameData = DataController.m_instance.m_gameData;

		// add the artifacts that are available to buy in starport at the beginning of the game
		for ( var artifactId = 0; artifactId < gameData.m_artifactList.Length; artifactId++ )
		{
			var artifactGameData = gameData.m_artifactList[ artifactId ];

			if ( artifactGameData.m_availableInStarport )
			{
				m_artifactStorage.Add( artifactId );
			}
		}
	}
}
