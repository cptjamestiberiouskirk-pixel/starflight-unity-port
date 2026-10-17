// the crew's answer to "Return to the ship?", asked when the terrain vehicle stops within MoveButton.c_boardingDistanceKm of the ship on its way back: Yes takes
// the vehicle back aboard, as a stop on the ship's own cell does
public class TVBoardYesButton : ShipButton
{
	// true while the map fades before the vehicle goes in
	bool m_isTransitioning;

	public override string GetLabel()
	{
		return "Yes";
	}

	public override bool Execute()
	{
		MoveButton.StartBoarding();

		m_isTransitioning = true;

		// keep the button active while the map fades
		return true;
	}

	public override bool Update()
	{
		if ( m_isTransitioning && !SpaceflightController.m_instance.m_viewport.IsFading() )
		{
			m_isTransitioning = false;

			SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Planetside );
			SpaceflightController.m_instance.m_buttonController.ClearCurrentButton();
		}

		// the console does nothing else meanwhile
		return true;
	}
}
