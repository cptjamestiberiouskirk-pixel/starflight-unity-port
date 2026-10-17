// captures the lifeform beside the terrain vehicle into stasis, from Tools (since 2026-10-17; Cargo offered it before) and only once it is stunned: the 1984
// notes (planet.txt) have it that a stunned lifeform "will not run away or attack", and "Capturing takes about 6 seconds" - a capture beam holds it meanwhile
// (TerrainLifeforms). A dead lifeform is recorded with Cargo instead
public class TVCaptureButton : ShipButton
{
	public override string GetLabel()
	{
		return "Capture";
	}

	public override bool Execute()
	{
		var controller = SpaceflightController.m_instance;
		var terrainGrid = controller.m_disembarked.m_terrainGrid;
		var lifeforms = ( terrainGrid != null ) ? terrainGrid.m_terrainLifeforms : null;

		if ( lifeforms == null )
		{
			return Refuse( "There is no lifeform beside the vehicle." );
		}

		// one recording or capture at a time
		if ( lifeforms.IsRecording() )
		{
			controller.m_messages.Clear();
			controller.m_messages.AddText( lifeforms.IsCapturing() ? "<color=white>Capturing...</color>" : "<color=white>Recording...</color>" );

			return false;
		}

		var lifeform = lifeforms.FindLifeformBeside( controller.m_terrainVehicle.transform.position );

		if ( lifeform == null )
		{
			return Refuse( "There is no lifeform beside the vehicle." );
		}

		if ( lifeform.m_status == TerrainLifeform.Status.Dead )
		{
			return Refuse( "A dead lifeform cannot be captured. Cargo records its data." );
		}

		if ( lifeform.m_status != TerrainLifeform.Status.Stunned )
		{
			return Refuse( "The lifeform must be stunned first." );
		}

		lifeforms.StartCapture( lifeform );

		return false;
	}

	// the capture cannot start
	static bool Refuse( string reason )
	{
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + reason + "</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Error );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
