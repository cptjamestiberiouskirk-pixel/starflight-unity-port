// records the data of the lifeform Cargo offered (planet.txt: "If the answer is "YES" there is a whiring noise while the word "Recording" is presented. Then it
// reads "Data collection completed, sir."")
public class TVRecordButton : ShipButton
{
	public override string GetLabel()
	{
		return "Record";
	}

	public override bool Execute()
	{
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var lifeforms = ( terrainGrid != null ) ? terrainGrid.m_terrainLifeforms : null;

		TVCargoButton.BackToTerrainVehicle();

		if ( lifeforms == null )
		{
			return false;
		}

		var lifeform = lifeforms.m_offeredLifeform;

		lifeforms.m_offeredLifeform = null;

		// eaten since it was offered
		if ( lifeform == null )
		{
			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>It got away.</color>" );

			return false;
		}

		lifeforms.StartRecording( lifeform );

		return false;
	}
}
