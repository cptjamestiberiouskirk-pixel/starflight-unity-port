using UnityEngine.InputSystem;

public class RecommendYesButton : ShipButton
{
	// the planet being named, the name typed so far, and the keyboard it is typed on (null while no planet is being named)
	int m_planetId = -1;
	string m_name = "";
	Keyboard m_keyboard;

	public override string GetLabel()
	{
		return "Yes";
	}

	// the planet that has just been logged is recommended for colonization - Interstel evaluates it when the ship is back at the Starport (STRINFO 1.2) - and,
	// as in the original (CAPTAIN overlay, (/LOG-PLAN) in the decompiled original), the player names it ("NAME THIS PLANET: ", up to 15 characters) before
	// "RECOMMENDATION LOGGED."; the name is on Interstel's evaluation
	public override bool Execute()
	{
		var playerData = DataController.m_instance.m_playerData;

		// is this a planet not recommended before? (only a new recommendation goes on to the bank)
		var recommendations = playerData.m_general.m_colonyRecommendationList;
		var isNew = ( recommendations == null ) || !recommendations.Contains( playerData.m_general.m_currentPlanetId );

		playerData.m_general.AddColonyRecommendation( playerData.m_general.m_currentPlanetId );

		// the bank pays its interest up to today and counts again from today, as the original's does on a new recommendation (CAPTAIN: I-TRANS, then PORTDAT)
		if ( isNew )
		{
			playerData.PayInterest( true );
		}

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		// with no keyboard there is nothing to type the name on: the planet goes unnamed
		StopNaming();

		m_keyboard = Keyboard.current;

		if ( m_keyboard == null )
		{
			FinishRecommendation();

			return false;
		}

		m_planetId = playerData.m_general.m_currentPlanetId;
		m_name = "";

		m_keyboard.onTextInput += OnTextInput;

		ShowName();

		// the running button until the name is typed (the stick and the fire button stay away from the console meanwhile)
		return true;
	}

	public override bool Update()
	{
		if ( m_keyboard == null )
		{
			return false;
		}

		// the save panel (the Escape key) pauses the game: no typing behind it
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return true;
		}

		// Enter ends the name
		if ( m_keyboard.enterKey.wasPressedThisFrame || m_keyboard.numpadEnterKey.wasPressedThisFrame )
		{
			InputController.m_instance.Debounce();

			DataController.m_instance.m_playerData.m_general.NameColonyRecommendation( m_planetId, m_name );

			StopNaming();

			FinishRecommendation();
		}

		return true;
	}

	// a character typed on the keyboard: a letter, a digit or a sign is added to the name (up to the original's 15), a backspace takes the last one away
	void OnTextInput( char character )
	{
		if ( m_keyboard == null )
		{
			return;
		}

		// something else has taken the console (the scene has gone, the buttons have changed): the name is left as it was
		var spaceflightController = SpaceflightController.m_instance;

		if ( ( spaceflightController == null ) || ( spaceflightController.m_buttonController.GetCurrentButtonSet() != ButtonController.ButtonSet.RecommendPlanet ) )
		{
			StopNaming();

			return;
		}

		// not while the save panel is open (what is typed there is not the planet's name)
		if ( spaceflightController.m_gameIsPaused )
		{
			return;
		}

		if ( character == '\b' )
		{
			if ( m_name.Length > 0 )
			{
				m_name = m_name.Substring( 0, m_name.Length - 1 );
			}
		}
		else if ( ( character >= ' ' ) && ( character != '\u007f' ) && ( m_name.Length < PD_General.c_maximumPlanetNameLength ) )
		{
			m_name += character;
		}

		ShowName();
	}

	// the question and the name typed so far, with a cursor
	void ShowName()
	{
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();
		messages.AddText( "<color=white>Name this planet: " + m_name + "_</color>" );
	}

	// no more typing
	void StopNaming()
	{
		if ( m_keyboard != null )
		{
			m_keyboard.onTextInput -= OnTextInput;
		}

		m_keyboard = null;
		m_planetId = -1;
	}

	// the original's "RECOMMENDATION LOGGED.", and back to the command buttons
	void FinishRecommendation()
	{
		SpaceflightController.m_instance.m_messages.Clear();
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Recommendation logged.</color>" );

		LogPlanetButton.BackToCommandButtons();
	}
}
