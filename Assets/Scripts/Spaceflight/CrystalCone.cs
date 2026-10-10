// the Crystal Cone, a device of the Ancients: "this is needed to identify from orbit the location of the control nexus" of the Crystal Planet (STRINFO 3.1, the research
// council's recommendation). The sources do not say how the original showed it - the port reports the nexus in the messages when the ship goes into orbit around the
// Crystal Planet and when Land opens the map to pick the landing site
public static class CrystalCone
{
	// reports where the control nexus is, if the ship is in orbit around the Crystal Planet with the Crystal Cone in its hold - returns true if it did
	public static bool ReportNexus()
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		// only in orbit
		if ( playerData.m_general.m_location != PD_General.Location.InOrbit )
		{
			return false;
		}

		// only around the Crystal Planet (guard against an id that is not in the list)
		var planetId = playerData.m_general.m_currentPlanetId;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) || !gameData.m_planetList[ planetId ].IsCrystalPlanet() )
		{
			return false;
		}

		// only with the Crystal Cone in the ship's hold
		var coneId = gameData.FindArtifactId( "Crystal Cone" );
		var artifactStorage = playerData.m_playerShip.m_artifactStorage;

		if ( ( coneId < 0 ) || ( artifactStorage == null ) || ( artifactStorage.Find( coneId ) == null ) )
		{
			return false;
		}

		// the nexus, in the form the terrain map shows its coordinates
		var latitude = (int) BlackEgg.c_nexusLatitude;
		var longitude = (int) BlackEgg.c_nexusLongitude;

		var nexus = System.Math.Abs( latitude ) + ( ( latitude < 0 ) ? "S" : "N" ) + " x " + System.Math.Abs( longitude ) + ( ( longitude < 0 ) ? "W" : "E" );

		SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>The Crystal Cone has located the control nexus at " + nexus + ".</color>" );

		return true;
	}
}
