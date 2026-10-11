// leaves the lifeform Cargo offered alone (planet.txt: "<-- IGNORE CAPTURE RECORD -->")
public class TVIgnoreButton : ShipButton
{
	public override string GetLabel()
	{
		return "Ignore";
	}

	public override bool Execute()
	{
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;

		if ( ( terrainGrid != null ) && ( terrainGrid.m_terrainLifeforms != null ) )
		{
			terrainGrid.m_terrainLifeforms.m_offeredLifeform = null;
		}

		SpaceflightController.m_instance.m_messages.Clear();

		TVCargoButton.BackToTerrainVehicle();

		return false;
	}
}
