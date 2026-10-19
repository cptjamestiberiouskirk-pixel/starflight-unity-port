
using System;
using System.Collections.Generic;

// what has been taken from the surfaces of the planets - the objects of a planet are placed again from the planet's seed every time the terrain vehicle goes out,
// so without this a deposit that was picked up would be back the next time; the minerals, lifeforms and curios come back when the ship leaves the orbit, as the
// original's do (RefillVisitedPlanets)
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

	// what has been taken from the random ruins (by TerrainRuin.GetRandomItemKey: the planet, the ruin and the item - null in older save files)
	public List<int> m_takenRandomRuinItemList;

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

		// a Black Egg the crew chose not to arm when it was dropped ("ARM THIS DEVICE?", ITEMS-OV WE9AA) - false for an egg in a save from before the question,
		// which was armed by being dropped
		public bool m_leftUnarmed;

		// the wreck of a terrain vehicle that ran out of fuel here (MOVE-OV WEF48 leaves it as an item of 300 cubic meters, too big for any hold) - it holds
		// nothing (its cargo was dropped beside it) and cannot be picked up
		public bool m_isVehicleWreck;
	}

	// what the terrain vehicle has dropped on the planets ("when an object is dropped on a planet's surface it can be picked up again" - the manual, page 21)
	public List<DroppedCargo> m_droppedCargoList;
	public int m_nextDroppedCargoId;

	// the planets a Black Egg has destroyed (ids into the game data's planet list)
	public List<int> m_destroyedPlanetList;

	[Serializable]

	public class LostLifeform
	{
		// the planet, and the lifeform by the order it is placed in (the same every time, since the placing is seeded from the planet)
		public int m_planetId;
		public int m_placeIndex;

		// dead (it lies where it is placed), or gone (captured or eaten)
		public bool m_isDead;
	}

	// the lifeforms of the planets visited in this orbit that have been killed, captured or eaten: they stay so until the ship leaves the orbit (null in older
	// save files)
	public List<LostLifeform> m_lostLifeformList;

	// the planets the terrain vehicle has gone out on since the ship went into orbit (the original's ?LANDED, which SHIPBUTTONS WF095 sets on the first run):
	// what was taken from them comes back when the ship leaves the orbit (null in older save files)
	public List<int> m_visitedPlanetList;

	public void Reset()
	{
		m_depositList = new List<Deposit>();
		m_takenArtifactSiteList = new List<int>();
		m_takenRandomRuinItemList = new List<int>();
		m_droppedCargoList = new List<DroppedCargo>();
		m_nextDroppedCargoId = 0;
		m_destroyedPlanetList = new List<int>();
		m_lostLifeformList = new List<LostLifeform>();
		m_visitedPlanetList = new List<int>();
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

		if ( m_takenRandomRuinItemList == null )
		{
			m_takenRandomRuinItemList = new List<int>();
		}

		if ( m_droppedCargoList == null )
		{
			m_droppedCargoList = new List<DroppedCargo>();
		}

		if ( m_destroyedPlanetList == null )
		{
			m_destroyedPlanetList = new List<int>();
		}

		if ( m_lostLifeformList == null )
		{
			m_lostLifeformList = new List<LostLifeform>();
		}

		if ( m_visitedPlanetList == null )
		{
			m_visitedPlanetList = new List<int>();
		}
	}

	// true if a Black Egg has destroyed this planet
	public bool IsPlanetDestroyed( int planetId )
	{
		Validate();

		return m_destroyedPlanetList.Contains( planetId );
	}

	// call this when a Black Egg destroys a planet - what was dropped on it goes with it
	public void DestroyPlanet( int planetId )
	{
		Validate();

		if ( !m_destroyedPlanetList.Contains( planetId ) )
		{
			m_destroyedPlanetList.Add( planetId );
		}

		m_droppedCargoList.RemoveAll( droppedCargo => droppedCargo.m_planetId == planetId );
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

	// call this when the artifact of a site is back at its site (the Crystal Orb the Veloxi take: COMMSPEC-OV WEB3F)
	public void ReturnArtifactSite( int artifactSiteId )
	{
		Validate();

		m_takenArtifactSiteList.Remove( artifactSiteId );
	}

	// true if this item of a random ruin has been taken
	public bool IsRandomRuinItemTaken( int itemKey )
	{
		Validate();

		return m_takenRandomRuinItemList.Contains( itemKey );
	}

	// call this when an item of a random ruin has been taken
	public void TakeRandomRuinItem( int itemKey )
	{
		Validate();

		if ( !m_takenRandomRuinItemList.Contains( itemKey ) )
		{
			m_takenRandomRuinItemList.Add( itemKey );
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

	// call this when a lifeform of a visited planet dies (isDead) or is captured or eaten
	public void LoseLifeform( int planetId, int placeIndex, bool isDead )
	{
		Validate();

		foreach ( var lostLifeform in m_lostLifeformList )
		{
			if ( ( lostLifeform.m_planetId == planetId ) && ( lostLifeform.m_placeIndex == placeIndex ) )
			{
				// (a dead lifeform that is eaten is gone)
				lostLifeform.m_isDead = lostLifeform.m_isDead && isDead;

				return;
			}
		}

		m_lostLifeformList.Add( new LostLifeform { m_planetId = planetId, m_placeIndex = placeIndex, m_isDead = isDead } );
	}

	// what has become of a lifeform of a visited planet (null if it is as it was placed)
	public LostLifeform FindLostLifeform( int planetId, int placeIndex )
	{
		Validate();

		foreach ( var lostLifeform in m_lostLifeformList )
		{
			if ( ( lostLifeform.m_planetId == planetId ) && ( lostLifeform.m_placeIndex == placeIndex ) )
			{
				return lostLifeform;
			}
		}

		return null;
	}

	// call this when the terrain vehicle goes out on a planet
	public void VisitPlanet( int planetId )
	{
		Validate();

		if ( ( planetId >= 0 ) && !m_visitedPlanetList.Contains( planetId ) )
		{
			m_visitedPlanetList.Add( planetId );
		}
	}

	// call this when the ship leaves an orbit: the planets visited in it get back their minerals, lifeforms and curios, as the original's DEPART does when the
	// ship leaves the orbit of a planet it has landed on (DPART-OV WF166 deletes them from the surface, and each region is seeded again from its place when the
	// terrain vehicle comes back: MOVE-OV ?POPULA, WED34)
	public void RefillVisitedPlanets( bool gameWon )
	{
		Validate();

		foreach ( var planetId in m_visitedPlanetList )
		{
			RefillPlanet( planetId, gameWon );
		}

		m_visitedPlanetList.Clear();
	}

	// what the original deletes (DPART-OV WF0D6): the elements (the deposits and what was dropped), the lifeforms, the curios (the artifacts that are not special:
	// in the random ruins and what was dropped) and a dropped Black Egg once the game is won - the special artifacts, the artifact sites, the ancient ruins whose
	// Endurium STRINFO gives and the wreck of a terrain vehicle stay as they are
	void RefillPlanet( int planetId, bool gameWon )
	{
		m_depositList.RemoveAll( deposit => deposit.m_planetId == planetId );
		m_lostLifeformList.RemoveAll( lostLifeform => lostLifeform.m_planetId == planetId );
		m_takenRandomRuinItemList.RemoveAll( itemKey => ( TerrainRuin.GetRandomItemPlanetId( itemKey ) == planetId ) && !TerrainRuins.IsSiteRuinIndex( TerrainRuin.GetRandomItemRuinIndex( itemKey ) ) );
		m_droppedCargoList.RemoveAll( droppedCargo => ( droppedCargo.m_planetId == planetId ) && IsDeletedOnDeparture( droppedCargo, gameWon ) );
	}

	// true if the original deletes this when the ship leaves (DPART-OV WF0D6: class 26, the elements; class 28 species 16 to 50, the curios; species 3, the
	// Black Egg, once the game is won - the wreck of a terrain vehicle is species 51, MOVE-OV WEF48)
	static bool IsDeletedOnDeparture( DroppedCargo droppedCargo, bool gameWon )
	{
		if ( droppedCargo.m_isVehicleWreck )
		{
			return false;
		}

		if ( droppedCargo.m_elementId >= 0 )
		{
			return true;
		}

		var gameData = DataController.m_instance.m_gameData;

		if ( ( droppedCargo.m_artifactId < 0 ) || ( droppedCargo.m_artifactId >= gameData.m_artifactList.Length ) )
		{
			return false;
		}

		var artifact = gameData.m_artifactList[ droppedCargo.m_artifactId ];

		if ( artifact.m_name == "Black Egg" )
		{
			return gameWon;
		}

		return !artifact.IsSpecial();
	}
}
