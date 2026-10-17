// the crew's answer to "Return to the ship?", asked when the terrain vehicle stops within MoveButton.c_boardingDistanceKm of the ship on its way back: No keeps
// the vehicle out, stopped where it is
public class TVBoardNoButton : ShipButton
{
	public override string GetLabel()
	{
		return "No";
	}

	public override bool Execute()
	{
		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.TerrainVehicle );
		SpaceflightController.m_instance.m_messages.Clear();

		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

		return false;
	}
}
