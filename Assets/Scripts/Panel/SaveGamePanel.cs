
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class SaveGamePanel : Panel
{
	// the description for each save game slot
	public TextMeshProUGUI[] m_slotDescriptionText;

	// the "active" text object array
	public GameObject[] m_slotActiveText;

	// the overlay panel
	public GameObject m_overlayPanel;

	// the switch panel
	public GameObject m_switchPanel;

	// the copy panel
	public GameObject m_copyPanel;

	// the reset panel
	public GameObject m_resetPanel;

	// the switch button
	public Button m_switchButton;

	// the copy button
	public Button m_copyButton;

	// the reset button
	public Button m_resetButton;

	// the resume button
	public Button m_resumeButton;

	// the quit button
	public Button m_quit;

	// the switch cancel button
	public Button m_switchCancelButton;

	// the copy cancel button
	public Button m_copyCancelButton;

	// the reset no button
	public Button m_resetNoButton;

	// the callback object
	object m_callbackObject;

	// the button that opens the Colors screen (made from the resume button the first time the panel opens, so the scene needs no new objects)
	Button m_colorsButton;

	// the Colors screen: the UI kit's colour schemes and its dark and light modes (made from the switch panel the first time it opens)
	GameObject m_colorsPanel;

	// the screen's button for each scheme, the diamond that marks the scheme in use, and the scheme's colour swatches
	Button[] m_schemeButtons;
	Image[] m_schemeMarkers;
	Image[][] m_schemeSwatches;

	// the screen's buttons for the two modes, and the diamonds that mark the mode in use
	Button[] m_modeButtons;
	Image[] m_modeMarkers;

	// the screen's heading, the schemes' names on their buttons, the line about the scheme in use, and the button that closes the screen
	TextMeshProUGUI m_colorsHeadingText;
	TMP_Text[] m_schemeLabels;
	TextMeshProUGUI m_schemeDescriptionText;
	Button m_colorsDoneButton;

	// the theme roles a scheme's swatches show, left to right: its screen, its panels, its text, its two accents, and its fuel and danger colours
	static readonly UIKitTheme.ColorRole[] c_swatchRoles =
	{
		UIKitTheme.ColorRole.BackgroundApp, UIKitTheme.ColorRole.BackgroundRaised, UIKitTheme.ColorRole.TextPrimary, UIKitTheme.ColorRole.AccentPrimary,
		UIKitTheme.ColorRole.AccentSecondary, UIKitTheme.ColorRole.StatusFuel, UIKitTheme.ColorRole.StatusDanger
	};

	// how many schemes the Colors screen shows in a row
	const int c_schemeColumns = 5;

	// call this to open this panel
	public override bool Open()
	{
		// base panel open
		base.Open();

		// hide all of the pop up panels
		m_overlayPanel.SetActive( false );
		m_switchPanel.SetActive( false );
		m_copyPanel.SetActive( false );
		m_resetPanel.SetActive( false );

		if ( m_colorsPanel != null )
		{
			m_colorsPanel.SetActive( false );
		}

		// the button for the Colors screen
		MakeColorsButton();

		// enable the main menu buttons
		EnableMenuButtons( true );

		// update the active slot indicators
		UpdateActive();

		// update the save game descriptions
		UpdateDescriptions();

		// select the resume button by default
		m_resumeButton.Select();

		// panel was opened
		return true;
	}

	// this is called when the closing animation is done
	public override void Closed()
	{
		// base planel closed
		base.Closed();

		// if we have a callback object let it know we have closed the panel
		if ( ( m_callbackObject != null ) && !m_callbackObject.Equals( null ) )
		{
			// get the object type
			var type = m_callbackObject.GetType();

			// get the method
			var method = type.GetMethod( "PanelWasClosed" );

			// invoke the method
			method.Invoke( m_callbackObject, null );
		}

		// forget who opened the panel (whoever opens it next says so first). This panel outlives the scenes, and holding
		// on to the spaceflight controller here kept the whole spaceflight scene in memory after the player had left it
		m_callbackObject = null;
	}

	// called when player clicks on switch
	public void SwitchClicked()
	{
		InputController.m_instance.Debounce();

		// deselect all the buttons
		EventSystem.current.SetSelectedGameObject( null );

		// disable the menu buttons
		EnableMenuButtons( false );

		// activate the overlay
		m_overlayPanel.SetActive( true );

		// activate the switch panel
		m_switchPanel.SetActive( true );

		// select the cancel button by default
		m_switchCancelButton.Select();

		// play the activate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// called when player clicks on copy
	public void CopyClicked()
	{
		InputController.m_instance.Debounce();

		// deselect all the buttons
		EventSystem.current.SetSelectedGameObject( null );

		// disable the menu buttons
		EnableMenuButtons( false );

		// activate the overlay
		m_overlayPanel.SetActive( true );

		// activate the copy panel
		m_copyPanel.SetActive( true );

		// select the cancel button by default
		m_copyCancelButton.Select();

		// play the activate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// called when player clicks on reset
	public void ResetClicked()
	{
		InputController.m_instance.Debounce();

		// deselect all the buttons
		EventSystem.current.SetSelectedGameObject( null );

		// disable the menu buttons
		EnableMenuButtons( false );

		// activate the overlay
		m_overlayPanel.SetActive( true );

		// activate the reset panel
		m_resetPanel.SetActive( true );

		// select the no button by default
		m_resetNoButton.Select();

		// play the activate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// called when player clicks on resume
	public void ResumeClicked()
	{
		InputController.m_instance.Debounce();

		// close this panel
		PanelController.m_instance.Close();
	}

	// called when the player clicks on colors: the screen of the UI kit's colour schemes and modes
	public void ColorsClicked()
	{
		InputController.m_instance.Debounce();

		MakeColorsPanel();

		if ( m_colorsPanel == null )
		{
			return;
		}

		// deselect all the buttons
		EventSystem.current.SetSelectedGameObject( null );

		// disable the menu buttons
		EnableMenuButtons( false );

		// activate the overlay and the colors panel
		m_overlayPanel.SetActive( true );
		m_colorsPanel.SetActive( true );

		UpdateColorsPanel();

		// select the scheme in use
		var scheme = UIKitTheme.GetScheme();

		if ( ( scheme >= 0 ) && ( scheme < m_schemeButtons.Length ) && ( m_schemeButtons[ scheme ] != null ) )
		{
			m_schemeButtons[ scheme ].Select();
		}

		// play the activate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// called when the player picks a colour scheme on the Colors screen: the whole game takes it at once
	public void SchemeClicked( int scheme )
	{
		InputController.m_instance.Debounce();

		UIKitTheme.SetScheme( scheme );

		// the descriptions are written in the new colours
		UpdateDescriptions();
		UpdateColorsPanel();

		// play the update sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Update );
	}

	// called when the player picks the dark or the light mode on the Colors screen
	public void ModeClicked( UIKitTheme.Mode mode )
	{
		InputController.m_instance.Debounce();

		UIKitTheme.SetMode( mode );

		// the descriptions are written in the new colours
		UpdateDescriptions();
		UpdateColorsPanel();

		// play the update sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Update );
	}

	// panel tick: the cancel key (escape) leaves the Colors screen
	public override void Tick()
	{
		if ( ( m_colorsPanel != null ) && m_colorsPanel.activeSelf && InputController.m_instance.m_cancel )
		{
			CancelClicked();
		}
	}

	// makes the button for the Colors screen: a copy of the resume button, in front of it
	void MakeColorsButton()
	{
		if ( ( m_colorsButton != null ) || ( m_resumeButton == null ) )
		{
			return;
		}

		m_colorsButton = CopyButton( m_resumeButton, m_resumeButton.transform.parent, "Colors Button", "Colors", ColorsClicked );

		m_colorsButton.transform.SetSiblingIndex( m_resumeButton.transform.GetSiblingIndex() );
	}

	// makes a copy of a button with its own label and its own click (the copy would have the original's), which the arrow keys reach by themselves
	Button CopyButton( Button original, Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick )
	{
		var button = Instantiate( original, parent );

		button.name = name;
		button.onClick = new Button.ButtonClickedEvent();
		button.onClick.AddListener( onClick );
		button.navigation = new Navigation { mode = Navigation.Mode.Automatic };

		var text = button.GetComponentInChildren<TMP_Text>( true );

		if ( text != null )
		{
			text.text = label;
		}
		else
		{
			var legacyText = button.GetComponentInChildren<Text>( true );

			if ( legacyText != null )
			{
				legacyText.text = label;
			}
		}

		return button;
	}

	// makes the Colors screen the first time it is needed: a copy of the switch panel (the kit's popup with its frame and a heading), made as large as
	// the slots, with a button for each colour scheme (its name and swatches of its colours), the two modes at the top and a done button at the bottom
	void MakeColorsPanel()
	{
		if ( ( m_colorsPanel != null ) || ( m_switchPanel == null ) || ( m_switchCancelButton == null ) )
		{
			return;
		}

		m_colorsPanel = Instantiate( m_switchPanel, m_switchPanel.transform.parent );
		m_colorsPanel.name = "Colors Panel";
		m_colorsPanel.SetActive( false );

		var panelTransform = (RectTransform) m_colorsPanel.transform;

		panelTransform.anchorMin = new Vector2( 0.02f, 0.02f );
		panelTransform.anchorMax = new Vector2( 0.98f, 0.98f );
		panelTransform.offsetMin = Vector2.zero;
		panelTransform.offsetMax = Vector2.zero;

		// the copy's contents are laid out here by hand: its layout group goes, and so does everything in it but its heading
		var layout = m_colorsPanel.GetComponentInChildren<VerticalLayoutGroup>( true );

		if ( layout == null )
		{
			Destroy( m_colorsPanel );
			m_colorsPanel = null;

			return;
		}

		var content = (RectTransform) layout.transform;
		var heading = content.GetComponentInChildren<TextMeshProUGUI>( true );

		DestroyImmediate( layout );

		for ( var i = content.childCount - 1; i >= 0; i-- )
		{
			var child = content.GetChild( i );

			if ( ( heading == null ) || ( child != heading.transform ) )
			{
				DestroyImmediate( child.gameObject );
			}
		}

		// the heading, at the top left
		if ( heading != null )
		{
			heading.text = "Color Scheme";

			m_colorsHeadingText = heading;

			PlaceAt( heading.rectTransform, new Vector2( 0.0f, 1.0f ), new Vector2( 0.6f, 1.0f ), new Vector2( 0.0f, -56.0f ), Vector2.zero );

			// the line about the scheme in use, under it, in the kit's body text
			m_schemeDescriptionText = Instantiate( heading, content );
			m_schemeDescriptionText.name = "Scheme Description";

			PlaceAt( m_schemeDescriptionText.rectTransform, new Vector2( 0.0f, 1.0f ), new Vector2( 1.0f, 1.0f ), new Vector2( 0.0f, -100.0f ), new Vector2( 0.0f, -60.0f ) );

			if ( m_schemeDescriptionText.TryGetComponent<UIKitStyle>( out var descriptionStyle ) )
			{
				descriptionStyle.m_textStyle = UIKitTheme.TextStyleRole.BodyM;
				descriptionStyle.m_color = UIKitTheme.ColorRole.TextSecondary;
				descriptionStyle.m_fontSize = 22.0f;
				descriptionStyle.Apply();
			}
		}

		// the two modes, at the top right
		m_modeButtons = new Button[ 2 ];
		m_modeMarkers = new Image[ 2 ];

		for ( var i = 0; i < 2; i++ )
		{
			var mode = ( i == 0 ) ? UIKitTheme.Mode.Dark : UIKitTheme.Mode.Light;
			var button = CopyButton( m_switchCancelButton, content, mode + " Mode Button", mode.ToString(), () => ModeClicked( mode ) );

			PlaceAt( (RectTransform) button.transform, new Vector2( 1.0f, 1.0f ), new Vector2( 1.0f, 1.0f ), new Vector2( -432.0f + i * 216.0f, -56.0f ), new Vector2( -232.0f + i * 216.0f, 0.0f ) );
			CenterLabel( button );

			m_modeButtons[ i ] = button;
			m_modeMarkers[ i ] = MakeMarker( button.transform );
		}

		// the done button, at the bottom right
		m_colorsDoneButton = CopyButton( m_switchCancelButton, content, "Done Button", "Done", CancelClicked );

		PlaceAt( (RectTransform) m_colorsDoneButton.transform, new Vector2( 1.0f, 0.0f ), new Vector2( 1.0f, 0.0f ), new Vector2( -240.0f, 0.0f ), new Vector2( 0.0f, 64.0f ) );
		CenterLabel( m_colorsDoneButton );

		// the schemes, in rows between them (the rows and their buttons share out the space, whatever the size of the screen)
		var listObject = new GameObject( "Schemes", typeof( RectTransform ), typeof( VerticalLayoutGroup ) );
		var listTransform = (RectTransform) listObject.transform;

		listTransform.SetParent( content, false );

		PlaceAt( listTransform, Vector2.zero, Vector2.one, new Vector2( 0.0f, 88.0f ), new Vector2( 0.0f, -120.0f ) );

		var list = listObject.GetComponent<VerticalLayoutGroup>();

		list.spacing = 16.0f;
		list.childAlignment = TextAnchor.UpperLeft;
		list.childControlWidth = true;
		list.childControlHeight = true;
		list.childForceExpandWidth = true;
		list.childForceExpandHeight = false;

		var schemes = UIKitTheme.c_schemes;

		m_schemeButtons = new Button[ schemes.Length ];
		m_schemeLabels = new TMP_Text[ schemes.Length ];
		m_schemeMarkers = new Image[ schemes.Length ];
		m_schemeSwatches = new Image[ schemes.Length ][];

		Transform row = null;

		for ( var i = 0; i < schemes.Length; i++ )
		{
			if ( ( i % c_schemeColumns ) == 0 )
			{
				row = MakeSchemeRow( listTransform );
			}

			var scheme = i;
			var button = CopyButton( m_switchCancelButton, row, schemes[ i ].m_name + " Button", schemes[ i ].m_name, () => SchemeClicked( scheme ) );

			// as wide as the other buttons of its row
			if ( !button.TryGetComponent<LayoutElement>( out var layoutElement ) )
			{
				layoutElement = button.gameObject.AddComponent<LayoutElement>();
			}

			layoutElement.ignoreLayout = false;
			layoutElement.minWidth = 0.0f;
			layoutElement.preferredWidth = 0.0f;
			layoutElement.flexibleWidth = 1.0f;

			var label = button.GetComponentInChildren<TMP_Text>( true );

			if ( label != null )
			{
				label.margin = new Vector4( 18.0f, 14.0f, 40.0f, 0.0f );
				label.textWrappingMode = TextWrappingModes.Normal;
			}

			m_schemeButtons[ i ] = button;
			m_schemeLabels[ i ] = label;
			m_schemeMarkers[ i ] = MakeMarker( button.transform );
			m_schemeSwatches[ i ] = MakeSwatches( button.transform );
		}

		// the last row filled up with empty cells, so that its buttons are as wide as the others
		for ( var i = schemes.Length; ( row != null ) && ( ( i % c_schemeColumns ) != 0 ); i++ )
		{
			var cell = new GameObject( "Empty", typeof( RectTransform ), typeof( LayoutElement ) );

			cell.transform.SetParent( row, false );
			cell.GetComponent<LayoutElement>().flexibleWidth = 1.0f;
		}
	}

	// makes a row of the Colors screen's scheme buttons
	static Transform MakeSchemeRow( Transform parent )
	{
		var rowObject = new GameObject( "Row", typeof( RectTransform ), typeof( HorizontalLayoutGroup ), typeof( LayoutElement ) );

		rowObject.transform.SetParent( parent, false );

		var layout = rowObject.GetComponent<HorizontalLayoutGroup>();

		layout.spacing = 16.0f;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = true;

		rowObject.GetComponent<LayoutElement>().preferredHeight = 136.0f;

		return rowObject.transform;
	}

	// places a rectangle by its anchors and the offsets of its corners from them
	static void PlaceAt( RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax )
	{
		rectTransform.anchorMin = anchorMin;
		rectTransform.anchorMax = anchorMax;
		rectTransform.pivot = new Vector2( 0.5f, 0.5f );
		rectTransform.offsetMin = offsetMin;
		rectTransform.offsetMax = offsetMax;

		// a layout element would ask a layout group for another size
		if ( rectTransform.TryGetComponent<LayoutElement>( out var layoutElement ) )
		{
			layoutElement.ignoreLayout = true;
		}
	}

	// puts a button's label in its middle
	static void CenterLabel( Button button )
	{
		SetAlignment( button.GetComponentInChildren<TMP_Text>( true ), HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Middle );
	}

	// aligns a text (TextMesh Pro draws by these two, not by the older alignment that combines them)
	static void SetAlignment( TMP_Text text, HorizontalAlignmentOptions horizontal, VerticalAlignmentOptions vertical )
	{
		if ( text != null )
		{
			text.horizontalAlignment = horizontal;
			text.verticalAlignment = vertical;
		}
	}

	// makes the diamond that marks the scheme or the mode in use, at the top right of its button, in the kit's accent colour
	static Image MakeMarker( Transform button )
	{
		var markerObject = new GameObject( "In Use", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		var markerTransform = (RectTransform) markerObject.transform;

		markerTransform.SetParent( button, false );
		markerTransform.anchorMin = new Vector2( 1.0f, 1.0f );
		markerTransform.anchorMax = new Vector2( 1.0f, 1.0f );
		markerTransform.pivot = new Vector2( 1.0f, 1.0f );
		markerTransform.sizeDelta = new Vector2( 18.0f, 18.0f );
		markerTransform.anchoredPosition = new Vector2( -14.0f, -14.0f );

		var marker = markerObject.GetComponent<Image>();

		marker.raycastTarget = false;

		var style = markerObject.AddComponent<UIKitStyle>();

		style.m_sprite = UIKitTheme.SpriteRole.Diamond;
		style.m_color = UIKitTheme.ColorRole.AccentPrimary;
		style.Apply();

		return marker;
	}

	// makes a row of swatches along the bottom of a scheme's button, which show the scheme's own colours whatever the theme in use
	static Image[] MakeSwatches( Transform button )
	{
		var rowObject = new GameObject( "Swatches", typeof( RectTransform ), typeof( HorizontalLayoutGroup ) );
		var rowTransform = (RectTransform) rowObject.transform;

		rowTransform.SetParent( button, false );
		rowTransform.anchorMin = new Vector2( 0.0f, 0.0f );
		rowTransform.anchorMax = new Vector2( 1.0f, 0.0f );
		rowTransform.pivot = new Vector2( 0.5f, 0.0f );
		rowTransform.offsetMin = new Vector2( 18.0f, 16.0f );
		rowTransform.offsetMax = new Vector2( -18.0f, 40.0f );

		var row = rowObject.GetComponent<HorizontalLayoutGroup>();

		row.spacing = 3.0f;
		row.childControlWidth = true;
		row.childControlHeight = true;
		row.childForceExpandWidth = true;
		row.childForceExpandHeight = true;

		var swatches = new Image[ c_swatchRoles.Length ];

		for ( var i = 0; i < swatches.Length; i++ )
		{
			var swatchObject = new GameObject( "Swatch", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );

			swatchObject.transform.SetParent( rowTransform, false );

			swatches[ i ] = swatchObject.GetComponent<Image>();
			swatches[ i ].raycastTarget = false;

			// a thin grey line round it, so that a swatch of the button's own colour still shows (on a dark button or a light one)
			var outline = swatchObject.AddComponent<Outline>();

			outline.effectColor = new Color( 0.5f, 0.5f, 0.5f, 0.6f );
			outline.effectDistance = new Vector2( 1.0f, -1.0f );

			UIKitModeApplier.KeepColour( swatches[ i ] );
		}

		return swatches;
	}

	// shows the scheme and the mode in use on the Colors screen, and each scheme's colours in the mode in use
	void UpdateColorsPanel()
	{
		if ( m_colorsPanel == null )
		{
			return;
		}

		var schemeInUse = UIKitTheme.GetScheme();
		var modeInUse = UIKitTheme.GetMode();

		// the texts' alignments (TextMesh Pro gives a copied text its own back when the text first wakes, so they are set whenever the screen is shown)
		SetAlignment( m_colorsHeadingText, HorizontalAlignmentOptions.Left, VerticalAlignmentOptions.Middle );
		SetAlignment( m_schemeDescriptionText, HorizontalAlignmentOptions.Left, VerticalAlignmentOptions.Middle );

		foreach ( var label in m_schemeLabels )
		{
			SetAlignment( label, HorizontalAlignmentOptions.Left, VerticalAlignmentOptions.Top );
		}

		for ( var i = 0; i < m_schemeButtons.Length; i++ )
		{
			if ( m_schemeMarkers[ i ] != null )
			{
				m_schemeMarkers[ i ].enabled = ( i == schemeInUse );
			}

			var theme = UIKitTheme.LoadScheme( i, modeInUse );

			for ( var j = 0; j < m_schemeSwatches[ i ].Length; j++ )
			{
				if ( m_schemeSwatches[ i ][ j ] != null )
				{
					m_schemeSwatches[ i ][ j ].color = ( theme != null ) ? theme.GetColor( c_swatchRoles[ j ] ) : Color.clear;
				}
			}
		}

		for ( var i = 0; i < m_modeButtons.Length; i++ )
		{
			if ( m_modeMarkers[ i ] != null )
			{
				m_modeMarkers[ i ].enabled = ( ( i == 0 ) ? UIKitTheme.Mode.Dark : UIKitTheme.Mode.Light ) == modeInUse;
			}
		}

		if ( m_schemeDescriptionText != null )
		{
			m_schemeDescriptionText.text = UIKitTheme.c_schemes[ schemeInUse ].m_name + ": " + UIKitTheme.c_schemes[ schemeInUse ].m_description;
		}
	}

	// called when the player clicks on quit
	public void QuitClicked()
	{
		InputController.m_instance.Debounce();

		// save the game
		DataController.m_instance.SaveActiveGame();

		// quit the game
		Application.Quit();
	}

	// called when player switches save game slots
	public void SwitchSaveGame( int i )
	{
		// do we want to switch to a different save game slot?
		if ( i != DataController.m_instance.m_activeSaveGameSlotNumber )
		{
			// yes - tell the data controller to switch
			DataController.m_instance.SetTargetSaveGameSlotNumber( i );

			// play the update sound
			SoundController.m_instance.PlaySound( SoundController.Sound.Update );

			// close this panel
			PanelController.m_instance.Close();
		}
		else
		{
			// no - return to the main menu
			CancelClicked();
		}
	}

	// called when player copies the save game
	public void CopySaveGame( int i )
	{
		// do we want to copy to a different save game slot?
		if ( i != DataController.m_instance.m_activeSaveGameSlotNumber )
		{
			// yes - tell the data controller to copy
			DataController.m_instance.CopyActiveSaveGameSlot( i );

			// update the save game descriptions
			UpdateDescriptions();

			// enable the main menu buttons
			EnableMenuButtons( true );

			// hide the overlay
			m_overlayPanel.SetActive( false );

			// hide the copy panel
			m_copyPanel.SetActive( false );

			// select the resume button by default
			m_resumeButton.Select();

			// play the update sound
			SoundController.m_instance.PlaySound( SoundController.Sound.Update );
		}
		else
		{
			// no - return to the main menu
			CancelClicked();
		}
	}

	// called when player wants to reset the current game
	public void ResetGame()
	{
		// tell the data controller to reset the game
		DataController.m_instance.ResetGame();

		// play the update sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Update );

		// close this panel
		PanelController.m_instance.Close();
	}

	// called when player clicks on cancel (or "no")
	public void CancelClicked()
	{
		InputController.m_instance.Debounce();

		// deselect all the buttons
		EventSystem.current.SetSelectedGameObject( null );

		// enable the menu buttons
		EnableMenuButtons( true );

		// hide the overlay
		m_overlayPanel.SetActive( false );

		// hide all of the pop up panels
		m_switchPanel.SetActive( false );
		m_copyPanel.SetActive( false );
		m_resetPanel.SetActive( false );

		if ( m_colorsPanel != null )
		{
			m_colorsPanel.SetActive( false );
		}

		// select the resume button by default
		m_resumeButton.Select();

		// play the deactivate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
	}

	// pass in the object you want to update when the panel has closed
	public void SetCallbackObject( object callbackObject )
	{
		m_callbackObject = callbackObject;
	}

	// call this to update the active slot indicators
	void UpdateActive()
	{
		for ( var i = 0; i < DataController.c_numSaveGameSlots; i++ )
		{
			m_slotActiveText[ i ].SetActive( i == DataController.m_instance.m_activeSaveGameSlotNumber );
		}
	}

	// call this to update the save game descriptions
	void UpdateDescriptions()
	{
		// go through each save game slot
		for ( var i = 0; i < DataController.c_numSaveGameSlots; i++ )
		{
			// get the player data for that save game slot
			var playerData = DataController.m_instance.m_playerDataList[ i ];

			// the date (stored year-month-day, shown day-month-year as everywhere else in the game) and time
			var description = "Date: <color=\"blue\">" + PD_General.GetDisplayStardate( playerData.m_general.m_currentStardateYMD ) + "</color>   Time: <color=\"blue\">" + playerData.m_general.m_hour.ToString( "D2" ) + ":" + playerData.m_general.m_minute.ToString( "D2" ) + "</color>\n";

			// the location
			description += "Location: <color=\"blue\">";

			Vector3 coordinates;

			switch ( playerData.m_general.m_location )
			{
				case PD_General.Location.Starport:
					description += "Starport</color>\n";
					break;
				case PD_General.Location.DockingBay:
					description += "Docking Bay</color>\n";
					break;
				case PD_General.Location.JustLaunched:
					description += "Just Launched</color>\n";
					break;
				case PD_General.Location.StarSystem:
					// the system's own place, as the coordinates above the main view give it (the ship's place inside the system is not a hyperspace place)
					coordinates = Tools.WorldToGameCoordinates( playerData.m_general.m_lastHyperspaceCoordinates );
					description += "Star System</color>   Coordinates: <color=\"blue\">" + Mathf.RoundToInt( coordinates.x ) + ", " + Mathf.RoundToInt( coordinates.z ) + "</color>\n";
					break;
				case PD_General.Location.Hyperspace:
					coordinates = Tools.WorldToGameCoordinates( playerData.m_general.m_lastHyperspaceCoordinates );
					description += "Hyperspace</color>   Coordinates: <color=\"blue\">" + Mathf.RoundToInt( coordinates.x ) + ", " + Mathf.RoundToInt( coordinates.z ) + "</color>\n";
					break;
				case PD_General.Location.InOrbit:
					description += "In Orbit</color>\n";
					break;
				case PD_General.Location.Planetside:
					description += "Planetside</color>\n";
					break;
				case PD_General.Location.Encounter:
					description += "In Encounter</color>\n";
					break;
				case PD_General.Location.Disembarked:
					description += "Terrain Vehicle</color>\n";
					break;
				default:
					// a location this list does not know yet - the colour tag still has to be closed, or the rest of the description runs on in this line
					description += playerData.m_general.m_location + "</color>\n";
					break;
			}

			// the ship name and cargo
			var shipName = ( playerData.m_playerShip.m_name == "" ) ? "No Name" : playerData.m_playerShip.m_name;
			var numCargoPods = ( playerData.m_playerShip.m_numCargoPods == 0 ) ? "None" : playerData.m_playerShip.m_numCargoPods.ToString();
			var cargoUsage = (float) playerData.m_playerShip.m_volumeUsed / (float) playerData.m_playerShip.m_volume * 100.0f;

			description += "Ship: <color=\"blue\">" + shipName + "</color>";
			description += "   Cargo Pods: <color=\"blue\">" + numCargoPods + "</color>";
			description += "   Cargo: <color=\"blue\">" + cargoUsage.ToString( "N1" ) + "% Full</color>\n";

			// ship part classes
			description += "Engines: <color=\"blue\">" + playerData.m_playerShip.GetEngines().m_name + "</color>";
			description += "   Shields: <color=\"blue\">" + playerData.m_playerShip.GetShielding().m_name + "</color>";
			description += "   Armor: <color=\"blue\">" + playerData.m_playerShip.GetArmor().m_name + "</color>";
			description += "   Missile Launcher: <color=\"blue\">" + playerData.m_playerShip.GetMissileLauncher().m_name + "</color>";
			description += "   Laser Cannon: <color=\"blue\">" + playerData.m_playerShip.GetLaserCannon().m_name + "</color>";

			// replace the description text for this save game slot (its values in the UI kit's colours)
			m_slotDescriptionText[ i ].text = UIKitTheme.KitColors( description );
		}
	}

	// call this to enable or disable the main menu buttons
	void EnableMenuButtons( bool enable )
	{
		m_switchButton.interactable = enable;
		m_copyButton.interactable = enable;
		m_resetButton.interactable = enable;
		m_resumeButton.interactable = enable;

		if ( m_colorsButton != null )
		{
			m_colorsButton.interactable = enable;
		}
	}
}
