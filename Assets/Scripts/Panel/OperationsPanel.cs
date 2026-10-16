
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OperationsPanel : Panel
{
	// the screens
	public GameObject m_welcomeGameObject;
	public GameObject m_noticesGameObject;
	public GameObject m_evaluationGameObject;

	// main buttons
	public Button m_noticesButton;
	public Button m_evaluationButton;
	public Button m_exitButton;

	// notices buttons
	public Button m_moreButton;
	public Button m_previousButton;
	public Button m_nextButton;
	public Button m_quitButton;

	// notice screen components
	public TextMeshProUGUI m_stardateText;
	public TextMeshProUGUI m_messageText;
	public float m_baseOffset;
	public float m_smoothScrollSpeed;

	// notices stuff
	int m_latestNoticeId;
	int m_currentNoticeId;
	int m_currentLine;
	float m_currentOffset;
	bool m_endOfMessageReached;

	// true while the screen asks whether to repeat the last notice (the original's "NO NEW NOTICES" and "REPEAT LAST NOTICE? [N Y]", OP-OV in the decompiled
	// original), the line said before the notice shown ("***REPEATING LAST NOTICE***"), and the labels of More and Quit, which answer Yes and No meanwhile
	bool m_askingToRepeat;
	string m_noticePreface;
	string m_moreLabel;
	string m_quitLabel;

	// the original types a notice out a paragraph at a time, pausing after every paragraph but the last for 300 ms a byte of the paragraph's encoded text (OP-OV
	// WE912: the STRING record's byte 0x0b times 300 ms), from 5.7 s for a short line to 42 s for the longest, so there is time to read it; the spacebar
	// (More) shows the next one at once, and the abort key (Quit) ends the notice with "***NOTICE ABORTED***"
	const float c_secondsPerEncodedByte = 0.3f;

	// the pause after a paragraph of a notice without the original's lengths
	const float c_paragraphDelay = 0.3f;
	float m_paragraphTimer;

	// the text of the evaluation screen, and what it says in the scene (that no colony world recommendations have been filed)
	TextMeshProUGUI m_evaluationText;
	string m_evaluationSceneText;

	// the evaluation screen's pages, one evaluation to a page as in the original, and the page shown
	readonly System.Collections.Generic.List<string> m_evaluationPages = new System.Collections.Generic.List<string>();
	int m_evaluationPage;

	// the starport controller
	public AstronautController m_astronautController;

	// a crew that has to report to Operations gets its evaluation by itself, a second after the room opens (OP-OV (U-OP): 1000 ms, then WF47A); 0 when none is due
	const float c_evaluationDelay = 1.0f;
	float m_evaluationTimer;

	// panel open
	public override bool Open()
	{
		// base panel open
		base.Open();

		// show the welcome screen
		ShowWelcome();

		// the evaluation comes up by itself for a crew that has to report here
		m_evaluationTimer = DataController.m_instance.m_playerData.m_general.MustReportToOperations() ? c_evaluationDelay : 0.0f;

		// panel was opened
		return true;
	}

	// panel closed
	public override void Closed()
	{
		// base panel closed
		base.Closed();

		// let the starport controller know
		m_astronautController.PanelWasClosed();
	}

	// panel tick
	public override void Tick()
	{
		// the evaluation a crew has to report for
		if ( m_evaluationTimer > 0.0f )
		{
			m_evaluationTimer -= Time.deltaTime;

			if ( m_evaluationTimer <= 0.0f )
			{
				ShowEvaluations();
			}
		}

		// get the height of the message shown
		float newOffset = m_messageText.renderedHeight + m_baseOffset;

		// smooth scroll the position (y)
		m_currentOffset = Mathf.Lerp( m_currentOffset, newOffset, m_smoothScrollSpeed * Time.deltaTime );

		// move up the message text game object
		m_messageText.rectTransform.offsetMax = new Vector3( 0.0f, m_currentOffset, 0.0f );

		// type out the notice shown, a paragraph at a time
		if ( m_noticesGameObject.activeSelf && !m_askingToRepeat && !m_endOfMessageReached )
		{
			m_paragraphTimer += Time.deltaTime;

			if ( m_paragraphTimer >= GetPauseAfterParagraph( m_currentLine - 1 ) )
			{
				ShowNextLine();

				UpdateButtons();
			}
		}
	}

	// call this to enter the welcome screen
	public void ShowWelcome()
	{
		// a question left open ends with the screen
		if ( m_askingToRepeat )
		{
			StopAskingToRepeat();
		}

		// show and hide objects
		m_welcomeGameObject.SetActive( true );
		m_noticesGameObject.SetActive( false );
		m_evaluationGameObject.SetActive( false );

		// show the main buttons
		m_noticesButton.gameObject.SetActive( true );
		m_evaluationButton.gameObject.SetActive( true );
		m_exitButton.gameObject.SetActive( true );

		// hide the notices buttons
		m_moreButton.gameObject.SetActive( false );
		m_previousButton.gameObject.SetActive( false );
		m_nextButton.gameObject.SetActive( false );
		m_quitButton.gameObject.SetActive( false );

		// automatically select the "notices" button for the player
		m_noticesButton.Select();
	}

	// call this to enter the notices screen
	public void ShowNotices()
	{
		// show and hide objects
		m_welcomeGameObject.SetActive( false );
		m_noticesGameObject.SetActive( true );
		m_evaluationGameObject.SetActive( false );

		// hide the main buttons
		m_noticesButton.gameObject.SetActive( false );
		m_evaluationButton.gameObject.SetActive( false );
		m_exitButton.gameObject.SetActive( false );

		// show the notices buttons
		m_moreButton.gameObject.SetActive( true );
		m_previousButton.gameObject.SetActive( true );
		m_nextButton.gameObject.SetActive( true );
		m_quitButton.gameObject.SetActive( true );

		// reset some variables
		m_latestNoticeId = 0;
		m_currentNoticeId = 0;

		// get access to the player progress
		PlayerData playerData = DataController.m_instance.m_playerData;

		// get access to the game data
		GameData gameData = DataController.m_instance.m_gameData;

		// update the stardate text (the way the game shows stardates, not in the date format of the computer's region) - the heading beside it says NOTICES, and
		// the original's header is that word and today's date (OP-OV, WE735)
		m_stardateText.text = PD_General.GetDisplayStardate( playerData.m_general.m_currentStardateYMD );

		// figure out which notice we should be showing: the first one not read yet, or else the last one read (the original's WEA28)
		var newNoticeId = -1;
		var lastReadNoticeId = 0;

		for ( int noticeId = 0; noticeId < gameData.m_noticeList.Length; noticeId++ )
		{
			GD_Notice notice = gameData.m_noticeList[ noticeId ];

			if ( string.Compare( playerData.m_general.m_currentStardateYMD, notice.m_stardate ) >= 0 )
			{
				m_latestNoticeId = noticeId;

				if ( string.Compare( notice.m_stardate, playerData.m_starport.m_lastReadNoticeStardate ) > 0 )
				{
					if ( newNoticeId < 0 )
					{
						newNoticeId = noticeId;
					}
				}
				else
				{
					lastReadNoticeId = noticeId;
				}
			}
		}

		m_noticePreface = "";

		// a new notice: show it
		if ( newNoticeId >= 0 )
		{
			m_currentNoticeId = newNoticeId;

			ShowCurrentMessage();

			return;
		}

		// none: the original says so and asks whether to repeat the last one (Yes and No on the screen's More and Quit)
		m_currentNoticeId = lastReadNoticeId;

		AskToRepeat();
	}

	// "NO NEW NOTICES" and "REPEAT LAST NOTICE? [N Y]", with More as Yes and Quit as No
	void AskToRepeat()
	{
		m_askingToRepeat = true;
		m_currentOffset = 0;

		m_messageText.text = "No new notices\r\n\r\nRepeat last notice?";

		m_previousButton.gameObject.SetActive( false );
		m_nextButton.gameObject.SetActive( false );

		SetButtonLabel( m_moreButton, ref m_moreLabel, "Yes" );
		SetButtonLabel( m_quitButton, ref m_quitLabel, "No" );

		m_moreButton.interactable = true;

		m_quitButton.Select();
	}

	// the question is answered: More and Quit get their labels back, and the notice buttons come back
	void StopAskingToRepeat()
	{
		m_askingToRepeat = false;

		SetButtonLabel( m_moreButton, ref m_moreLabel, null );
		SetButtonLabel( m_quitButton, ref m_quitLabel, null );

		m_previousButton.gameObject.SetActive( true );
		m_nextButton.gameObject.SetActive( true );
	}

	// gives a button another label, keeping its own the first time (null puts its own back)
	static void SetButtonLabel( Button button, ref string ownLabel, string label )
	{
		var text = ( button == null ) ? null : button.GetComponentInChildren<TextMeshProUGUI>( true );

		if ( text == null )
		{
			return;
		}

		if ( ownLabel == null )
		{
			ownLabel = text.text;
		}

		text.text = label ?? ownLabel;
	}

	// true while the screen asks whether to repeat the last notice
	public bool IsAskingToRepeat()
	{
		return m_askingToRepeat;
	}

	// call this to enter the evaluations screen
	public void ShowEvaluations()
	{
		// show and hide objects
		m_welcomeGameObject.SetActive( false );
		m_noticesGameObject.SetActive( false );
		m_evaluationGameObject.SetActive( true );

		// the main buttons give way to the notices screen's More (the next page) and Quit, as the original's evaluation screen has "[ QUIT ]"
		m_noticesButton.gameObject.SetActive( false );
		m_evaluationButton.gameObject.SetActive( false );
		m_exitButton.gameObject.SetActive( false );

		m_moreButton.gameObject.SetActive( false );
		m_previousButton.gameObject.SetActive( false );
		m_nextButton.gameObject.SetActive( false );
		m_quitButton.gameObject.SetActive( true );

		m_quitButton.Select();

		MakeEvaluationPages();

		m_evaluationPage = 0;

		ShowEvaluationPage();
	}

	// the evaluation screen's pages, as the original's (OP-OV in the decompiled original): first Interstel's supplemental evaluations, each once (the towing
	// charges, the lost terrain vehicle, and the completion of the mission after the win, STRINFO 1.2), then the evaluations of the planets recommended for
	// colonization until the ship leaves with them read, the last of them followed by "END OF EVALUATIONS" - or, with none, the scene's "You have not filed any
	// colony world recommendations", with the original's NEW once recommendations have been filed (OP-OV WEBC1)
	void MakeEvaluationPages()
	{
		m_evaluationPages.Clear();

		if ( m_evaluationText == null )
		{
			var evaluationTransform = m_evaluationGameObject.transform.Find( "Display/Error Text Mask/Error Text" );

			if ( evaluationTransform == null )
			{
				return;
			}

			m_evaluationText = evaluationTransform.GetComponent<TextMeshProUGUI>();

			if ( m_evaluationText == null )
			{
				return;
			}

			m_evaluationSceneText = m_evaluationText.text;
		}

		var general = DataController.m_instance.m_playerData.m_general;

		// (this also moves the colony evaluations of a save file from before their list out of the supplemental ones)
		var colonyEvaluations = general.GetColonyEvaluationList();

		// the supplemental evaluations, shown now and then no more
		if ( general.m_supplementalEvaluationList != null )
		{
			m_evaluationPages.AddRange( general.m_supplementalEvaluationList );

			general.m_supplementalEvaluationList.Clear();
		}

		// and their fines booked as they are shown, as the original's (OP-OV WE78B: WF045 and WF12C, WF02B)
		DataController.m_instance.m_playerData.BookSupplementalFines();

		if ( general.m_gameWon && !general.m_missionCompleteShown )
		{
			var storyText = DataController.m_instance.m_gameData.FindStoryText( "MissionComplete" );

			if ( storyText != null )
			{
				m_evaluationPages.Add( storyText.m_text );
			}

			general.m_missionCompleteShown = true;
		}

		// the colony evaluations, until the ship leaves the Starport with them read (CHKFLIGHT-OV WF40A deletes them then)
		if ( colonyEvaluations.Count > 0 )
		{
			m_evaluationPages.AddRange( colonyEvaluations );

			m_evaluationPages[ m_evaluationPages.Count - 1 ] += "\n\n\nEND OF EVALUATIONS";
		}
		else if ( general.m_colonyRecommendationCount > 0 )
		{
			// recommendations have been filed, but none since the evaluations were last read: the original's NEW (OP-OV WEBC1)
			m_evaluationPages.Add( m_evaluationSceneText.Replace( "not filed any colony", "not filed any new colony" ) );
		}
		else
		{
			m_evaluationPages.Add( m_evaluationSceneText );
		}

		// read now
		general.m_colonyEvaluationUnread = false;
	}

	// shows the current page of the evaluation screen - More is there while there is another page to see
	void ShowEvaluationPage()
	{
		if ( ( m_evaluationText == null ) || ( m_evaluationPages.Count == 0 ) )
		{
			return;
		}

		m_evaluationPage = Mathf.Clamp( m_evaluationPage, 0, m_evaluationPages.Count - 1 );

		m_evaluationText.text = m_evaluationPages[ m_evaluationPage ];

		var morePages = ( m_evaluationPage < m_evaluationPages.Count - 1 );

		m_moreButton.gameObject.SetActive( morePages );

		if ( morePages )
		{
			m_moreButton.Select();
		}
		else
		{
			m_quitButton.Select();
		}
	}

	// this is called if we clicked on the notices button
	public void NoticesClicked()
	{
		InputController.m_instance.Debounce();

		// show the notices
		ShowNotices();

		// play a ui sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// this is called if we clicked on the evaluation button
	public void EvaluationClicked()
	{
		InputController.m_instance.Debounce();

		// show the evaluations screen
		ShowEvaluations();

		// play a ui sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// this is called if we clicked on the exit button
	public void ExitClicked()
	{
		InputController.m_instance.Debounce();

		// close this panel
		PanelController.m_instance.Close();
	}

	// this is called if we clicked on the more button
	public void MoreClicked()
	{
		InputController.m_instance.Debounce();

		// on the evaluation screen, the next evaluation (the original's "PRESS SPACEBAR FOR NEXT EVALUATION")
		if ( m_evaluationGameObject.activeSelf )
		{
			m_evaluationPage++;

			ShowEvaluationPage();

			SoundController.m_instance.PlaySound( SoundController.Sound.Update );

			return;
		}

		// Yes to "REPEAT LAST NOTICE?": the last notice again, after the original's "***REPEATING LAST NOTICE***"
		if ( m_askingToRepeat )
		{
			StopAskingToRepeat();

			m_noticePreface = "***Repeating last notice***\r\n\r\n";

			ShowCurrentMessage();

			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

			return;
		}

		// show the next line of the notice
		ShowNextLine();

		// update the buttons
		UpdateButtons();
	}

	// this is called if we clicked on the prev button
	public void PreviousClicked()
	{
		InputController.m_instance.Debounce();

		// show the previous notice
		if ( m_currentNoticeId > 0 )
		{
			m_currentNoticeId--;

			ShowCurrentMessage();
		}

		// play a ui sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Update );
	}

	// this is called if we clicked on the next button
	public void NextClicked()
	{
		InputController.m_instance.Debounce();

		// show the next notice
		if ( m_currentNoticeId < m_latestNoticeId )
		{
			m_currentNoticeId++;

			ShowCurrentMessage();
		}

		// play a ui sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Update );
	}

	// this is called if we clicked on the quit button
	public void QuitClicked()
	{
		InputController.m_instance.Debounce();

		// while a notice is being typed out, the abort key ends it (OP-OV WE912: "***NOTICE ABORTED***", and after the newest notice the end of the notices)
		if ( m_noticesGameObject.activeSelf && !m_askingToRepeat && !m_endOfMessageReached )
		{
			AbortNotice();

			UpdateButtons();

			SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

			return;
		}

		// No to "REPEAT LAST NOTICE?" (or Quit): back to the menu
		if ( m_askingToRepeat )
		{
			StopAskingToRepeat();
		}

		// show the welcome screen
		ShowWelcome();

		// play a ui sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
	}

	// call this to start displaying the current message
	void ShowCurrentMessage()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// reset some variables
		m_currentLine = 0;
		m_currentOffset = 0;
		m_endOfMessageReached = false;

		// show the first line of the current message
		ShowNextLine();

		// update the buttons
		UpdateButtons();

		// add the current notice to the ships log
		playerData.m_shipsLog.AddStarportNotice( m_currentNoticeId );
	}

	// update which buttons are enabled and selected
	void UpdateButtons()
	{
		// select the quit button by default
		m_quitButton.Select();

		// enable / disable the previous and next buttons based on what our current notice index is
		m_previousButton.interactable = ( m_currentNoticeId > 0 );
		m_nextButton.interactable = ( m_currentNoticeId < m_latestNoticeId );

		// check if we have reached the end of the current notice
		if ( m_endOfMessageReached )
		{
			m_moreButton.interactable = false;

			if ( m_currentNoticeId < m_latestNoticeId )
			{
				m_nextButton.Select();
			}
		}
		else
		{
			m_moreButton.interactable = true;

			m_moreButton.Select();
		}
	}

	// call this to show the next line of the current message
	// the seconds the original waits after a paragraph of the notice shown before it types the next (c_paragraphDelay for a notice without the lengths)
	float GetPauseAfterParagraph( int paragraph )
	{
		var noticeList = DataController.m_instance.m_gameData.m_noticeList;

		if ( ( m_currentNoticeId < 0 ) || ( m_currentNoticeId >= noticeList.Length ) )
		{
			return c_paragraphDelay;
		}

		var lengths = noticeList[ m_currentNoticeId ].m_paragraphLengths;

		if ( ( lengths == null ) || ( paragraph < 0 ) || ( paragraph >= lengths.Length ) )
		{
			return c_paragraphDelay;
		}

		return lengths[ paragraph ] * c_secondsPerEncodedByte;
	}

	void ShowNextLine()
	{
		// get access to the game data
		GameData gameData = DataController.m_instance.m_gameData;

		// get the current notice
		GD_Notice currentNotice = gameData.m_noticeList[ m_currentNoticeId ];

		// check if we are displaying the first line
		if ( m_currentLine == 0 )
		{
			// clear the text and add the date (a stardate too), after the line said before the notice if there is one
			m_messageText.text = m_noticePreface + PD_General.GetDisplayStardate( currentNotice.m_stardate );

			m_noticePreface = "";

			// remember the newest notice read
			if ( string.Compare( currentNotice.m_stardate, DataController.m_instance.m_playerData.m_starport.m_lastReadNoticeStardate ) > 0 )
			{
				DataController.m_instance.m_playerData.m_starport.m_lastReadNoticeStardate = currentNotice.m_stardate;
			}
		}

		// the next paragraph comes after the full delay again
		m_paragraphTimer = 0.0f;

		// get the current line of the message
		string[] subStrings = currentNotice.m_message.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );

		if ( m_currentLine < subStrings.Length )
		{
			m_messageText.text += "\r\n\r\n" + subStrings[ m_currentLine ];
			m_currentLine++;

			// check if we have reached the end of this message
			if ( m_currentLine == subStrings.Length )
			{
				m_endOfMessageReached = true;

				// after the newest notice the original says there are no more (OP-OV, WE912)
				AddEndOfNotices();
			}
		}
	}

	// the rest of the notice shown is not typed: the original's "***NOTICE ABORTED***", then the end of the notices if it was the newest
	void AbortNotice()
	{
		m_endOfMessageReached = true;

		m_messageText.text += "\r\n\r\n***Notice aborted***";

		AddEndOfNotices();
	}

	// after the newest notice the original says there are no more (OP-OV, WE912)
	void AddEndOfNotices()
	{
		if ( m_currentNoticeId >= m_latestNoticeId )
		{
			m_messageText.text += "\r\n\r\n***End of notices***";
		}
	}
}
