
// captures the lifeform Cargo offered and puts it in stasis (planet.txt: "If the response is capture then there is a suspenseful noise. Capturing takes about 6
// seconds") - a stunned one "will not run away or attack"
public class TVCaptureButton : ShipButton
{
	public override string GetLabel()
	{
		return "Capture";
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
		if ( ( lifeform == null ) || ( lifeform.m_status == TerrainLifeform.Status.Dead ) )
		{
			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>It got away.</color>" );

			return false;
		}

		lifeforms.StartCapture( lifeform );

		return false;
	}
}
