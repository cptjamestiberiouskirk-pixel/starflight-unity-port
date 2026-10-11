
public class DistressButton : ShipButton
{
	public override string GetLabel()
	{
		return "Distress";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		SpaceflightController.m_instance.m_messages.Clear();

		// check location - distress makes no sense when docked
		if ( playerData.m_general.m_location == PD_General.Location.DockingBay )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>You're safely docked. No need for a distress signal.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// check if we're already in an encounter
		if ( playerData.m_general.m_location == PD_General.Location.Encounter )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>We're already in contact with another vessel!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// after the flare of Arth's sun there is no Starport to answer (STRINFO 2.14)
		if ( playerData.m_general.m_starportDestroyed )
		{
			var storyText = DataController.m_instance.m_gameData.FindStoryText( "DistressNoResponse" );

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>" + ( ( storyText != null ) ? storyText.m_text : "THERE'S NO RESPONSE!" ) + "</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// the distress call is answered: Interstel tows the ship back to the Starport, and the crew pays for the rescue (STRINFO 1.2, the supplemental evaluation
		// on towing charges)
		var fine = playerData.GetTowingFine();

		playerData.m_bank.m_currentBalance -= fine;
		playerData.m_bank.m_transactionList.Add( new PD_Bank.Transaction( playerData.m_general.m_currentStardateYMD, "Towing charges", fine + "-" ) );

		var towingCharges = DataController.m_instance.m_gameData.FindStoryText( "TowingCharges" );

		if ( towingCharges != null )
		{
			playerData.m_general.AddSupplementalEvaluation( towingCharges.m_text.Replace( "[AMOUNT]", fine.ToString() ) );
		}

		SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>Distress signal sent.</color>" );
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Interstel has towed the ship back to Starport. Towing charges: " + fine + " M.U.</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		// back in Arth's docking bay
		var gameData = DataController.m_instance.m_gameData;

		playerData.m_general.m_currentStarId = gameData.m_misc.m_arthStarId;
		playerData.m_general.m_currentPlanetId = gameData.m_misc.m_arthPlanetId;

		SpaceflightController.m_instance.SwitchLocation( PD_General.Location.DockingBay );

		return false;
	}
}
