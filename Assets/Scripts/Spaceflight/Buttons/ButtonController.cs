
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ButtonController : MonoBehaviour
{
	public enum ButtonSet
	{
		Bridge,
		CommandA,
		CommandB,
		CommunicationsA,
		CommunicationsB,
		Engineering,
		Medical,
		StopTreatment,
		NavigationA,
		NavigationB,
		NavigationC,
		NavigationD,
		Science,
		Land,
		Launch,
		Hail,
		Respond,
		Comm,
		AskQuestion,
		AnswerQuestion,
		Posture,
		TerrainVehicle,
		ShipsLog,
		AlienComms,
		Combat,
		TerrainVehicleCargo,
		RecommendPlanet,
		RepairMineral,
		TerrainVehicleWeapon,
		TerrainVehicleLifeform,
		TerrainVehicleDeadLifeform,
		Distress,
		Count
	};

	// constants
	const int c_numButtons = 6;

	// public stuff we want to set using the editor
	public Sprite m_buttonOffSprite;
	public Sprite m_buttonOnSprite;
	public Sprite m_buttonActiveSprite;
	public Image[] m_buttonImageList;
	public TextMeshProUGUI[] m_buttonLabelList;
	public TextMeshProUGUI m_currentOfficer;

	// the icon beside the current officer's name (the scene's logo stands for the ship; the officers and the terrain vehicle have the UI kit's icons)
	public Image m_officerIcon;

	// the logo the icon shows on the bridge
	Sprite m_shipIcon;

	// the highlight behind the selected button's label and the bar on its left edge (the UI kit's selected and running states; without them only the bullets show the selection)
	public Image m_selectionHighlight;
	public Image m_selectionBar;

	// buttons
	private ShipButton[] m_buttonList;
	private ShipButton m_currentButton;

	// the button the fire button was pressed on (it is activated a moment later, and the buttons can change in between)
	private ShipButton m_buttonBeingActivated;

	// private stuff we don't want the editor to see
	int m_selectedButtonIndex;
	bool m_activatingButton;
	float m_activatingButtonTimer;
	float m_ignoreControllerTimer;

	// button sets
	ShipButton[][] m_buttonSets;

	// the current button set
	ButtonSet m_currentButtonSet;

	// unity awake
	void Awake()
	{
		// create the six ship buttons
		m_buttonList = new ShipButton[ c_numButtons ];

		// reset the ignore controller timer
		m_ignoreControllerTimer = 0.0f;

		// remember the ship's logo, which the officer icon shows on the bridge
		if ( m_officerIcon != null )
		{
			m_shipIcon = m_officerIcon.sprite;
		}

		// common bridge button
		var bridgeButton = new ChangeMenuButton( "Bridge", ButtonSet.Bridge );

		// allocate memory for button sets
		m_buttonSets = new ShipButton[ (int) ButtonSet.Count ][];

		m_buttonSets[ (int) ButtonSet.Bridge ] = new ShipButton[] { new CommandButton(), new ScienceButton(), new NavigationButton(), new EngineeringButton(), new CommunicationsButton(), new MedicalButton() };
		m_buttonSets[ (int) ButtonSet.CommandA ] = new ShipButton[] { new LaunchButton(), new DisembarkButton(), new ShipCargoButton(), new LogPlanetButton(), new ShipsLogButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.CommandB ] = new ShipButton[] { new LandButton(), new DisembarkButton(), new ShipCargoButton(), new LogPlanetButton(), new ShipsLogButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.CommunicationsA ] = new ShipButton[] { new HailButton(), new DistressButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.CommunicationsB ] = new ShipButton[] { new RespondButton(), new DistressButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.Engineering ] = new ShipButton[] { new DamageButton(), new RepairButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.Medical ] = new ShipButton[] { new ExamineButton(), new TreatButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.StopTreatment ] = new ShipButton[] { new StopTreatmentNoButton(), new StopTreatmentYesButton() };
		m_buttonSets[ (int) ButtonSet.NavigationA ] = new ShipButton[] { new ManeuverButton(), new StarmapButton(), new RaiseShieldsButton(), new ArmWeaponButton(), new CombatButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.NavigationB ] = new ShipButton[] { new ManeuverButton(), new StarmapButton(), new DropShieldsButton(), new ArmWeaponButton(), new CombatButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.NavigationC ] = new ShipButton[] { new ManeuverButton(), new StarmapButton(), new RaiseShieldsButton(), new DisarmWeaponButton(), new CombatButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.NavigationD ] = new ShipButton[] { new ManeuverButton(), new StarmapButton(), new DropShieldsButton(), new DisarmWeaponButton(), new CombatButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.Science ] = new ShipButton[] { new SensorsButton(), new AnalysisButton(), new StatusButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.Land ] = new ShipButton[] { new SelectSiteButton(), new DescendButton(), new AbortButton() };
		m_buttonSets[ (int) ButtonSet.Launch ] = new ShipButton[] { new LaunchNoButton(), new LaunchYesButton() };
		m_buttonSets[ (int) ButtonSet.Hail ] = new ShipButton[] { new FriendlyButton(), new HostileButton(), new ObsequiousButton() };
		m_buttonSets[ (int) ButtonSet.Respond ] = new ShipButton[] { new FriendlyButton(), new HostileButton(), new ObsequiousButton() };
		m_buttonSets[ (int) ButtonSet.Comm ] = new ShipButton[] { new StatementButton(), new QuestionButton(), new PostureButton(), new TerminateButton() };
		m_buttonSets[ (int) ButtonSet.AskQuestion ] = new ShipButton[] { new QThemselvesButton(), new QOtherRacesButton(), new QOldEmpireButton(), new QTheAncientsButton(), new QGeneralInfoButton() };
		m_buttonSets[ (int) ButtonSet.AnswerQuestion ] = new ShipButton[] { new AnswerYesButton(), new AnswerNoButton(), new TerminateButton() };
		m_buttonSets[ (int) ButtonSet.Posture ] = new ShipButton[] { new FriendlyButton(), new HostileButton(), new ObsequiousButton() };
		m_buttonSets[ (int) ButtonSet.TerrainVehicle ] = new ShipButton[] { new MapButton(), new MoveButton(), new TVCargoButton(), new LookButton(), new ScanButton(), new WeaponButton() };
		m_buttonSets[ (int) ButtonSet.TerrainVehicleCargo ] = new ShipButton[] { new CargoNextButton(), new CargoDropButton(), new CargoBackButton() };
		m_buttonSets[ (int) ButtonSet.TerrainVehicleWeapon ] = new ShipButton[] { new TVFireButton(), new TVSwitchWeaponButton(), new TVWeaponBackButton() };
		m_buttonSets[ (int) ButtonSet.TerrainVehicleLifeform ] = new ShipButton[] { new TVCaptureButton(), new TVRecordButton(), new TVIgnoreButton() };
		m_buttonSets[ (int) ButtonSet.TerrainVehicleDeadLifeform ] = new ShipButton[] { new TVRecordButton(), new TVIgnoreButton() };
		m_buttonSets[ (int) ButtonSet.RecommendPlanet ] = new ShipButton[] { new RecommendNoButton(), new RecommendYesButton() };
		m_buttonSets[ (int) ButtonSet.RepairMineral ] = new ShipButton[] { new RepairNoButton(), new RepairYesButton() };
		m_buttonSets[ (int) ButtonSet.Distress ] = new ShipButton[] { new DistressNoButton(), new DistressYesButton() };
		m_buttonSets[ (int) ButtonSet.ShipsLog ] = new ShipButton[] { new StarportNoticesButton(), new AlienCommsButton(), new MessagesButton(), bridgeButton };
		m_buttonSets[ (int) ButtonSet.AlienComms ] = new ShipButton[] { new ACThemselvesButton(), new ACOtherRacesButton(), new ACOldEmpireButton(), new ACTheAncientsButton(), new ACGeneralInfoButton(), new ChangeMenuButton( "Cancel", ButtonSet.ShipsLog ) };
		m_buttonSets[ (int) ButtonSet.Combat ] = new ShipButton[] { new TargetButton(), new FireLaserButton(), new FireMissileButton(), new StatusButton(), new NavigationButton() };
	}

	// unity start
	void Start()
	{
		// reset everything
		m_activatingButton = false;
		m_activatingButtonTimer = 0.0f;

		// the rows as the kit's sci-fi buttons
		MakeRowButtons();

		ShowButtonStates( m_currentButton != null );
	}

	// unity update
	void Update()
	{
		// don't do anything if the game is paused
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// check if we are activating the currently selected button
		if ( m_activatingButton )
		{
			// yes - so update the timer
			m_activatingButtonTimer += Time.deltaTime;

			// after a certain amount of time, execute the button
			if ( m_activatingButtonTimer >= 0.35f )
			{
				// reset the activate button flag and timer
				m_activatingButton = false;
				m_activatingButtonTimer = 0.0f;

				// the buttons may have changed since the fire button was pressed (the aliens asked a question, an encounter began) - the press
				// was for a button that is not there any more, and it must not activate whatever has taken its place (a press on Statement
				// used to answer Yes to a question the player had not seen yet)
				if ( m_buttonList[ m_selectedButtonIndex ] == m_buttonBeingActivated )
				{
					ActivateButton();
				}

				m_buttonBeingActivated = null;
			}

			return;
		}

		// check if we have a current funciton
		if ( m_currentButton != null )
		{
			// call update on it
			if ( m_currentButton.Update() )
			{
				// the button did the update - don't let this update do anything more
				return;
			}
		}

		// check if we moved the stick down
		if ( InputController.m_instance.m_south )
		{
			if ( m_ignoreControllerTimer == 0.0f )
			{
				m_ignoreControllerTimer = 0.3f;

				if ( m_selectedButtonIndex < ( m_buttonList.Length - 1 ) )
				{
					if ( m_buttonList[ m_selectedButtonIndex + 1 ] != null )
					{
						m_selectedButtonIndex++;

						UpdateButtonSprites();

						SoundController.m_instance.PlaySound( SoundController.Sound.Click );
					}
				}
			}
		}
		else if ( InputController.m_instance.m_north ) // check if we have moved the stick up
		{
			if ( m_ignoreControllerTimer == 0.0f )
			{
				m_ignoreControllerTimer = 0.3f;

				if ( m_selectedButtonIndex > 0 )
				{
					m_selectedButtonIndex--;

					UpdateButtonSprites();

					SoundController.m_instance.PlaySound( SoundController.Sound.Click );
				}
			}
		}
		else // we have centered the stick
		{
			m_ignoreControllerTimer = 0.0f;
		}

		// check if we have pressed the fire button
		if ( InputController.m_instance.m_submit )
		{
			InputController.m_instance.Debounce();

			if ( m_buttonList[ m_selectedButtonIndex ] == null )
			{
				SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			}
			else
			{
				// set the activate button flag and reset the timer
				m_activatingButton = true;
				m_activatingButtonTimer = 0.0f;

				// remember which button this press is for
				m_buttonBeingActivated = m_buttonList[ m_selectedButtonIndex ];

				// show the currently selected button as running
				ShowButtonStates( true );

				// play the activate sound
				SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
			}
		}
	}

	// call this to change the current officer: the name and the icon beside it
	public void ChangeOfficerText( string newOfficer, UIKitTheme.OfficerIcon officerIcon )
	{
		m_currentOfficer.text = newOfficer;

		if ( m_officerIcon == null )
		{
			return;
		}

		// the officer's icon from the UI kit (the ship, or a theme without the icon, shows the ship's logo)
		var theme = UIKitTheme.Get();
		var sprite = ( theme != null ) ? theme.GetOfficerIcon( officerIcon ) : null;

		m_officerIcon.sprite = ( sprite != null ) ? sprite : m_shipIcon;
	}

	// set the bridge buttons
	public void SetBridgeButtons()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// there is no current funciton
		ClearCurrentButton();

		// restore the bridge buttons
		ChangeButtonSet( ButtonSet.Bridge );

		// change the current officer label to the name of the ship
		ChangeOfficerText( "ISS " + playerData.m_playerShip.m_name, UIKitTheme.OfficerIcon.Ship );

		// update the message
		switch ( playerData.m_general.m_location )
		{
			case PD_General.Location.DockingBay:
				SpaceflightController.m_instance.m_messages.Clear();
				SpaceflightController.m_instance.m_messages.AddText( "<color=white>Ship computer activated.\nPre-launch procedures complete.\nStanding by to initiate launch.</color>" );
				break;

			// (out of the Starport the window keeps the launch's last lines, which the switch to the location has said)
		}
	}

	// go back to the communications officer's menu (after a hail, the original's console keeps HAIL, DISTRESS, BRIDGE: starflt_044, starflt_047)
	public void SetCommunicationsButtons()
	{
		ActivateBridgeButton<CommunicationsButton>();
	}

	// go to the captain's menu (after the launch from the Starport the original's console shows the captain's: starflt_139)
	public void SetCaptainButtons()
	{
		ActivateBridgeButton<CommandButton>();
	}

	// run one of the bridge's buttons as if it had been pressed, and make it the current button (the communications officer's keeps its menu up to date)
	void ActivateBridgeButton<T>() where T : ShipButton
	{
		// there is no current function
		ClearCurrentButton();

		foreach ( var button in m_buttonSets[ (int) ButtonSet.Bridge ] )
		{
			if ( button is T )
			{
				if ( button.Execute() )
				{
					m_currentButton = button;

					m_currentButton.Update();
				}

				return;
			}
		}

		// (every bridge button is in the bridge's set, so this is not reached)
		SetBridgeButtons();
	}

	// set the maneuver buttons
	public void SetManeuverButtons()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// button set to use depends on whether we have shields up and/or weapons armed
		if ( playerData.m_playerShip.m_shieldsAreUp && playerData.m_playerShip.m_weaponsAreArmed )
		{
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonSet.NavigationD );
		}
		else if ( playerData.m_playerShip.m_weaponsAreArmed )
		{
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonSet.NavigationC );
		}
		else if ( playerData.m_playerShip.m_shieldsAreUp )
		{
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonSet.NavigationB );
		}
		else
		{
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonSet.NavigationA );
		}

		// get the personnel file on this officer
		var personnelFile = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Navigator );

		// set the name of the officer
		SpaceflightController.m_instance.m_buttonController.ChangeOfficerText( "Officer " + personnelFile.m_name, UIKitTheme.OfficerIcon.Navigator );
	}

	// update the buttons and change the current button index
	public void ChangeButtonSet( ButtonSet buttonSet )
	{
		// there is no current funciton
		ClearCurrentButton();

		// get the desired button set list
		var buttonList = m_buttonSets[ (int) buttonSet ];

		// go through all 6 buttons
		for ( var i = 0; i < c_numButtons; i++ )
		{
			if ( i < buttonList.Length )
			{
				m_buttonList[ i ] = buttonList[ i ];

				m_buttonLabelList[ i ].text = m_buttonList[ i ].GetLabel();
			}
			else
			{
				m_buttonList[ i ] = null;

				m_buttonLabelList[ i ].text = "";
			}
		}

		// update the current button set
		m_currentButtonSet = buttonSet;

		// reset the current button index
		m_selectedButtonIndex = 0;

		// update the button sprites
		UpdateButtonSprites();
	}

	// the sci-fi buttons behind the console's rows (made at the start, one a row)
	Image[] m_rowButtons;

	// the space left between two rows' buttons, and how far the text of a row stands in from its button's left edge
	const float c_rowGap = 4.0f;
	const float c_rowTextInset = 14.0f;

	// how far the middle of a row's capitals is above the middle of its label's box (measured once a label has text, the labels share one style)
	float m_textMiddleOffset;
	bool m_textMiddleMeasured;

	// measures the middle of the capitals against the middle of the box, from a label with text in it (TextMesh Pro leaves room below the line)
	void MeasureTextMiddle()
	{
		if ( m_textMiddleMeasured || ( m_buttonLabelList == null ) )
		{
			return;
		}

		foreach ( var label in m_buttonLabelList )
		{
			if ( ( label != null ) && !string.IsNullOrEmpty( label.text ) )
			{
				label.ForceMeshUpdate();

				m_textMiddleOffset = label.textBounds.center.y - label.rectTransform.rect.center.y;
				m_textMiddleMeasured = true;

				return;
			}
		}
	}

	// unity late update: the rows' buttons and the highlight follow the labels, which the console's layout moves when the screen's size changes
	void LateUpdate()
	{
		if ( m_rowButtons == null )
		{
			return;
		}

		MeasureTextMiddle();

		for ( var i = 0; i < m_rowButtons.Length; i++ )
		{
			if ( ( m_rowButtons[ i ] == null ) || !m_rowButtons[ i ].gameObject.activeSelf )
			{
				continue;
			}

			PlaceRow( m_rowButtons[ i ].rectTransform, i );
		}

		if ( ( m_selectionHighlight != null ) && m_selectionHighlight.enabled )
		{
			PlaceRow( m_selectionHighlight.rectTransform, m_selectedButtonIndex );
		}
	}

	// puts a row's button (or the highlight) in its row's place, anchored in proportions of its parent's height as the console's layout places the labels,
	// so that it stays with its label at any size of the screen (a picture rendered at another size than the screen's lays the console out again)
	void PlaceRow( RectTransform rowTransform, int row )
	{
		var area = rowTransform.parent as RectTransform;

		if ( ( area == null ) || ( area.rect.height <= 0.0f ) || !GetRowPlace( row, area, out var centreY, out var height ) )
		{
			return;
		}

		var centre = centreY + area.rect.center.y;
		var bottom = ( centre - height * 0.5f - area.rect.yMin ) / area.rect.height;
		var top = ( centre + height * 0.5f - area.rect.yMin ) / area.rect.height;

		rowTransform.anchorMin = new Vector2( rowTransform.anchorMin.x, bottom );
		rowTransform.anchorMax = new Vector2( rowTransform.anchorMax.x, top );
		rowTransform.offsetMin = new Vector2( rowTransform.offsetMin.x, 0.0f );
		rowTransform.offsetMax = new Vector2( rowTransform.offsetMax.x, 0.0f );
	}

	// where a row's button goes in the highlight's parent: centred on the row's text (on the middle of its capitals, not of the label's box, which has room
	// below the line), and as high as the rows are apart less a gap, so that the text sits inside its button
	bool GetRowPlace( int row, RectTransform area, out float centreY, out float height )
	{
		centreY = 0.0f;
		height = 0.0f;

		if ( ( area == null ) || ( m_buttonLabelList == null ) || ( row < 0 ) || ( row >= m_buttonLabelList.Length ) || ( m_buttonLabelList[ row ] == null ) )
		{
			return false;
		}

		var label = m_buttonLabelList[ row ];
		var labelTransform = label.rectTransform;

		// the rows are as far apart as the first two labels
		var spacing = labelTransform.rect.height;

		if ( ( m_buttonLabelList.Length > 1 ) && ( m_buttonLabelList[ 0 ] != null ) && ( m_buttonLabelList[ 1 ] != null ) )
		{
			var first = area.InverseTransformPoint( m_buttonLabelList[ 0 ].rectTransform.TransformPoint( m_buttonLabelList[ 0 ].rectTransform.rect.center ) );
			var second = area.InverseTransformPoint( m_buttonLabelList[ 1 ].rectTransform.TransformPoint( m_buttonLabelList[ 1 ].rectTransform.rect.center ) );

			spacing = Mathf.Abs( first.y - second.y );
		}

		height = Mathf.Max( 0.0f, spacing - c_rowGap );

		// the middle of the text as it is drawn: the middle of the label's box, moved up to the middle of its capitals
		var middle = labelTransform.rect.center + new Vector2( 0.0f, m_textMiddleOffset );

		var centre = area.InverseTransformPoint( labelTransform.TransformPoint( middle ) );

		centreY = centre.y - area.rect.center.y;

		return true;
	}

	// makes the console's rows look like the kit's sci-fi buttons: a button behind each row, and the selected row's highlight lit in the same shape
	void MakeRowButtons()
	{
		var theme = UIKitTheme.Get();

		if ( ( m_selectionHighlight == null ) || ( theme == null ) || ( theme.m_buttonSprite == null ) || ( m_buttonLabelList == null ) )
		{
			return;
		}

		var highlight = m_selectionHighlight.rectTransform;
		var area = highlight.parent as RectTransform;

		if ( area == null )
		{
			return;
		}

		// the highlight is the selected row's button, lit (its frame comes with it)
		UIKitModeApplier.MakeButton( m_selectionHighlight );

		// the frame already marks the selected row, so the bar beside it is not needed
		if ( m_selectionBar != null )
		{
			m_selectionBar.gameObject.SetActive( false );
		}

		Canvas.ForceUpdateCanvases();

		MeasureTextMiddle();

		m_rowButtons = new Image[ m_buttonLabelList.Length ];

		for ( var i = 0; i < m_buttonLabelList.Length; i++ )
		{
			var rowObject = new GameObject( "UI Kit Row Button " + i, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( LayoutElement ) );
			var row = rowObject.GetComponent<RectTransform>();

			row.SetParent( area, false );

			// behind the highlight (and so behind the labels)
			row.SetSiblingIndex( highlight.GetSiblingIndex() );

			row.anchorMin = highlight.anchorMin;
			row.anchorMax = highlight.anchorMax;
			row.pivot = highlight.pivot;

			// the text stands in from the button's left edge
			m_buttonLabelList[ i ].margin = new Vector4( c_rowTextInset, 0.0f, 0.0f, 0.0f );

			row.sizeDelta = new Vector2( highlight.sizeDelta.x, 0.0f );

			rowObject.GetComponent<LayoutElement>().ignoreLayout = true;

			var image = rowObject.GetComponent<Image>();

			image.raycastTarget = false;

			var style = rowObject.AddComponent<UIKitStyle>();

			style.m_sprite = UIKitTheme.SpriteRole.Button;
			style.m_color = UIKitTheme.ColorRole.BackgroundRaised;
			style.Apply();

			UIKitModeApplier.MakeButton( image );

			// an unselected row's frame is fainter than the selected one's
			var frame = row.Find( UIKitModeApplier.c_buttonFrameName );

			if ( ( frame != null ) && frame.TryGetComponent<UIKitStyle>( out var frameStyle ) )
			{
				frameStyle.m_alpha = 0.35f;
				frameStyle.Apply();
			}

			m_rowButtons[ i ] = image;
		}
	}

	// unity on enable
	void OnEnable()
	{
		// the labels and the highlight take the colours of the kit's mode, so they are shown again when it changes
		UIKitTheme.Changed -= ModeChanged;
		UIKitTheme.Changed += ModeChanged;
	}

	// unity on disable
	void OnDisable()
	{
		UIKitTheme.Changed -= ModeChanged;
	}

	// the kit's mode changed: show the buttons' states in its colours
	void ModeChanged()
	{
		if ( m_buttonImageList != null )
		{
			ShowButtonStates( m_currentButton != null );
		}
	}

	// go through each button and show it as selected or not (none of them running)
	public void UpdateButtonSprites()
	{
		ShowButtonStates( false );
	}

	// shows which button is selected and whether it is running: the bullets (off, on, active), the labels' colours and the highlight behind the selected label
	// (the UI kit's console button states: a raised row with a cyan bar for the selected button, a cyan row with dark text for the running one)
	void ShowButtonStates( bool selectedIsRunning )
	{
		for ( var i = 0; i < c_numButtons; i++ )
		{
			var selected = ( m_selectedButtonIndex == i );

			m_buttonImageList[ i ].sprite = selected ? ( selectedIsRunning ? m_buttonActiveSprite : m_buttonOnSprite ) : m_buttonOffSprite;
		}

		var theme = UIKitTheme.Get();

		// a scene without the highlight (or without the theme) shows the selection with the bullets alone
		if ( ( m_selectionHighlight == null ) || ( theme == null ) )
		{
			return;
		}

		for ( var i = 0; i < c_numButtons; i++ )
		{
			var selected = ( m_selectedButtonIndex == i );

			m_buttonLabelList[ i ].color = selected ? ( selectedIsRunning ? theme.m_textOnAccent : theme.m_textPrimary ) : theme.m_textSecondary;
		}

		// no highlight on an empty row (the selected index is always on a button, but the buttons may not have been set yet)
		var hasButton = ( m_selectedButtonIndex >= 0 ) && ( m_selectedButtonIndex < c_numButtons ) && ( m_buttonList != null ) && ( m_buttonList[ m_selectedButtonIndex ] != null );

		// a row's button shows only where the row has a button of the console, in its row's place
		if ( m_rowButtons != null )
		{
			for ( var i = 0; i < m_rowButtons.Length; i++ )
			{
				if ( m_rowButtons[ i ] == null )
				{
					continue;
				}

				m_rowButtons[ i ].gameObject.SetActive( ( m_buttonList != null ) && ( i < m_buttonList.Length ) && ( m_buttonList[ i ] != null ) );

				MeasureTextMiddle();
				PlaceRow( m_rowButtons[ i ].rectTransform, i );
			}
		}

		m_selectionHighlight.enabled = hasButton;

		if ( m_selectionBar != null )
		{
			m_selectionBar.enabled = hasButton && !selectedIsRunning;
			m_selectionBar.color = theme.m_accentPrimary;
		}

		if ( !hasButton )
		{
			return;
		}

		m_selectionHighlight.color = selectedIsRunning ? theme.m_accentPrimary : theme.m_backgroundHover;

		// the highlight goes behind the selected label: its centre and its height, in the highlight's parent (it spans the parent's width)
		var label = m_buttonLabelList[ m_selectedButtonIndex ].rectTransform;
		var highlight = m_selectionHighlight.rectTransform;
		var area = highlight.parent as RectTransform;

		if ( area == null )
		{
			return;
		}

		var centre = area.InverseTransformPoint( label.TransformPoint( label.rect.center ) );

		highlight.anchoredPosition = new Vector2( highlight.anchoredPosition.x, centre.y - area.rect.center.y );
		highlight.sizeDelta = new Vector2( highlight.sizeDelta.x, label.rect.height );

		// with the rows as buttons, the highlight is the selected row's button: in the same place
		if ( m_rowButtons != null )
		{
			MeasureTextMiddle();
			PlaceRow( highlight, m_selectedButtonIndex );
		}
	}

	// call this to stop calling update on the current button
	public void ClearCurrentButton()
	{
		m_currentButton = null;
	}

	// call this to change the button that is selected
	public void SetSelectedButton( int buttonIndex )
	{
		ClearCurrentButton();
		
		m_selectedButtonIndex = buttonIndex;

		// show it as running
		ShowButtonStates( true );
	}

	// call this to activate the selected button
	public void ActivateButton()
	{
		// get the activated button (execute might change this so grab it now)
		var activatedButton = m_buttonList[ m_selectedButtonIndex ];

		// execute the current button and check if it returned true
		if ( activatedButton.Execute() )
		{
			// update the current button
			m_currentButton = activatedButton;

			// do the first update
			m_currentButton.Update();
		}
	}

	// call this to deactivate the current button
	public void DeactivateButton()
	{
		// we no longer have a current button
		ClearCurrentButton();

		// remove the "active" dot from the current button
		UpdateButtonSprites();

		// play the deactivate sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
	}

	// gets the current button set
	public ButtonSet GetCurrentButtonSet()
	{
		return m_currentButtonSet;
	}
}