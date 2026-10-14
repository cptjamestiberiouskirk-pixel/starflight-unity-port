
public class DistressYesButton : ShipButton
{
	public override string GetLabel()
	{
		return "Yes";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		SpaceflightController.m_instance.m_messages.Clear();

		// at the Starport there is nobody to call (the original's TOW-US: "WE'RE ALREADY HOME!")
		if ( playerData.m_general.m_location == PD_General.Location.DockingBay )
		{
			return Refuse( "We're already home!" );
		}

		// in an encounter the call does not get through (TOW-US: "TRANSMISSION IS BEING JAMMED" while alien ships are there - the port says it in every encounter,
		// since a tow would leave the encounter some other way than Encounter.LeaveEncounter)
		if ( playerData.m_general.m_location == PD_General.Location.Encounter )
		{
			return Refuse( "Transmission is being jammed." );
		}

		// after the flare of Arth's sun there is no Starport to answer (STRINFO 2.14)
		if ( playerData.m_general.m_starportDestroyed )
		{
			var storyText = DataController.m_instance.m_gameData.FindStoryText( "DistressNoResponse" );

			return Refuse( ( storyText != null ) ? storyText.m_text : "THERE'S NO RESPONSE!" );
		}

		// the distress call is answered: Interstel tows the ship back to the Starport, and the crew pays for the rescue (STRINFO 1.2, the supplemental evaluation
		// on towing charges)
		var fine = playerData.GetTowingFine();

		// a FINE in the original's ledger, like every supplemental evaluation's, booked when the evaluation screen shows the towing charges (OP-OV WF12C, WF02B)
		playerData.m_general.AddSupplementalFine( fine );

		var towingCharges = DataController.m_instance.m_gameData.FindStoryText( "TowingCharges" );

		if ( towingCharges != null )
		{
			playerData.m_general.AddSupplementalEvaluation( towingCharges.m_text.Replace( "[AMOUNT]", fine.ToString() ) );
		}

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		// back in Arth's docking bay
		var gameData = DataController.m_instance.m_gameData;

		playerData.m_general.m_currentStarId = gameData.m_misc.m_arthStarId;
		playerData.m_general.m_currentPlanetId = gameData.m_misc.m_arthPlanetId;

		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.DockingBay );

		// the crew sleeps through the tow (TOW-US: "INITIATING CRYOGENIC STASIS", then "RETRIEVAL COMPLETE, CREW REVIVED") - said after the switch, since the docking
		// bay's bridge clears the text window, and put before what the docking bay has said (the crew wake before Interstel pays a bonus): inserted last first
		SpaceflightController.m_instance.m_messages.InsertText( "<color=white>Interstel has towed the ship back to Starport. Towing charges: " + fine + " M.U.</color>" );
		SpaceflightController.m_instance.m_messages.InsertText( "<color=white>Retrieval complete, crew revived.</color>" );
		SpaceflightController.m_instance.m_messages.InsertText( "<color=white>Initiating cryogenic stasis.</color>" );

		return false;
	}

	// the call is not sent: say why, and go back to the buttons it was asked from
	static bool Refuse( string reason )
	{
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>" + reason + "</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Error );

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( DistressButton.s_returnButtonSet );

		return false;
	}
}
