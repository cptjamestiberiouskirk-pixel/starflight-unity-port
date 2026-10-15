
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UIColor = UIKitTheme.ColorRole;
using UIText = UIKitTheme.TextStyleRole;
using UISprite = UIKitTheme.SpriteRole;

// gives the Spaceflight scene's console the look of the UI kit: a UIKitStyle with a role on each element, an outline on the panels,
// and the kit's bullets on the console buttons (run it again after changing the table: it only sets what the table says)
public static class UIKitSceneTool
{
	const string c_scenePath = "Assets/Scenes/Spaceflight.unity";

	// what each element of the console becomes: its path under the UI canvas, its text style and size, its sprite, its colour and opacity
	struct Entry
	{
		public string m_path;
		public UIText m_text;
		public float m_size;
		public UISprite m_sprite;
		public UIColor m_color;
		public float m_alpha;
		public float m_lineHeight;

		public Entry( string path, UIText text, float size, UISprite sprite, UIColor color, float alpha = 1.0f, float lineHeight = 0.0f )
		{
			m_path = path; m_text = text; m_size = size; m_sprite = sprite; m_color = color; m_alpha = alpha; m_lineHeight = lineHeight;
		}
	}

	const string c_left = "Main/Left/";
	const string c_viewport = "Main/Left/GameObject/GameObject/Main Viewport/";
	const string c_right = "Main/Right/GameObject/";
	const string c_display = "Main/Right/GameObject/B/Display/";
	const string c_buttons = "Main/Right/GameObject/C/GameObject/B/Buttons/";
	const string c_officerIcon = "Main/Right/GameObject/C/GameObject/A/GameObject/A/GameObject/Image";

	// the selection highlight's margins from the sides of the labels' panel (the labels start 10 px in: the bar on the left stays clear of them), and the width of the bar (as in the kit's Console Button)
	const float c_selectionInsetLeft = 1.0f;
	const float c_selectionInsetRight = 6.0f;
	const float c_selectionBarWidth = 3.0f;

	static Entry[] GetTable()
	{
		var table = new System.Collections.Generic.List<Entry>
		{
			// the frame around the viewport and the right column: the background of the app, so the panels stand on it
			new Entry( c_left + "Background - Top", UIText.None, 0, UISprite.None, UIColor.BackgroundApp ),
			new Entry( c_left + "Background - Left", UIText.None, 0, UISprite.None, UIColor.BackgroundApp ),
			new Entry( c_left + "Background - Bottom", UIText.None, 0, UISprite.None, UIColor.BackgroundApp ),
			new Entry( "Main/Right/Background", UIText.None, 0, UISprite.None, UIColor.BackgroundApp ),

			// the top bars: the coordinates as a readout, the display's name as a heading
			new Entry( c_left + "GameObject/Top Bar/GameObject/Label", UIText.ReadoutM, 28, UISprite.None, UIColor.TextPrimary ),
			new Entry( c_right + "Top Bar/GameObject/Display Label", UIText.HeadingM, 22, UISprite.None, UIColor.TextAccent ),

			// the display panel
			new Entry( "Main/Right/GameObject/B", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),

			// the status display
			new Entry( c_display + "Status/A/Labels", UIText.LabelM, 24, UISprite.None, UIColor.TextSecondary , 1.0f, 41.0f ),
			new Entry( c_display + "Status/A/Values", UIText.ReadoutM, 24, UISprite.None, UIColor.TextPrimary , 1.0f, 41.0f ),
			new Entry( c_display + "Status/B/Shield Gauge", UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised ),
			new Entry( c_display + "Status/B/Shield Gauge/Shield Level", UIText.None, 0, UISprite.Panel, UIColor.StatusShield ),
			new Entry( c_display + "Status/B/Shield Gauge/Shields Text", UIText.LabelS, 18, UISprite.None, UIColor.TextSecondary ),
			new Entry( c_display + "Status/B/Armor Gauge", UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised ),
			new Entry( c_display + "Status/B/Armor Gauge/Armor Level", UIText.None, 0, UISprite.Panel, UIColor.StatusArmor ),
			new Entry( c_display + "Status/B/Armor Gauge/Armor Text", UIText.LabelS, 18, UISprite.None, UIColor.TextSecondary ),

			// the sensors display (its window takes the theme's sensor window colour instead of the original's magenta, keeping its sprite, whose edge the sensors display measures; its readouts are not in capitals, which would write the mass as 2X10)
			new Entry( c_display + "Sensors/B/Panel", UIText.None, 0, UISprite.None, UIColor.SensorWindow ),
			new Entry( c_display + "Sensors/A/Mass", UIText.BodyM, 28, UISprite.None, UIColor.TextSecondary ),
			new Entry( c_display + "Sensors/C/Bio \\/ Min", UIText.BodyM, 28, UISprite.None, UIColor.TextSecondary ),
			new Entry( c_display + "Sensors/B/Instructions", UIText.BodyM, 30, UISprite.None, UIColor.TextAccent ),

			// the terrain map: the display's background, the frames around the map and its legend, the crosshairs of the site selection
			new Entry( c_display + "Terrain Map", UIText.None, 0, UISprite.None, UIColor.BackgroundPanel ),
			new Entry( c_display + "Terrain Map/GameObject/Map Frame", UIText.None, 0, UISprite.None, UIColor.BorderDefault ),
			new Entry( c_display + "Terrain Map/GameObject/Legend Frame", UIText.None, 0, UISprite.None, UIColor.BorderDefault ),
			new Entry( c_display + "Terrain Map/Select Site/Horizontal Crosshair/Outline", UIText.None, 0, UISprite.None, UIColor.BackgroundApp ),
			new Entry( c_display + "Terrain Map/Select Site/Horizontal Crosshair/Center", UIText.None, 0, UISprite.None, UIColor.AccentPrimary ),
			new Entry( c_display + "Terrain Map/Select Site/Vertical Crosshair/Outline", UIText.None, 0, UISprite.None, UIColor.BackgroundApp ),
			new Entry( c_display + "Terrain Map/Select Site/Vertical Crosshair/Center", UIText.None, 0, UISprite.None, UIColor.AccentPrimary ),

			// the terrain map's site selection
			new Entry( c_display + "Terrain Map/Select Site/Label", UIText.HeadingM, 26, UISprite.None, UIColor.TextAccent ),
			new Entry( c_display + "Terrain Map/Select Site/Coordinates/Latitude Label", UIText.LabelM, 26, UISprite.None, UIColor.TextSecondary ),
			new Entry( c_display + "Terrain Map/Select Site/Coordinates/Latitude Text", UIText.ReadoutM, 28, UISprite.None, UIColor.TextAccent ),
			new Entry( c_display + "Terrain Map/Select Site/Coordinates/Longitude Label", UIText.LabelM, 26, UISprite.None, UIColor.TextSecondary ),
			new Entry( c_display + "Terrain Map/Select Site/Coordinates/Longitude Text", UIText.ReadoutM, 28, UISprite.None, UIColor.TextAccent ),

			// the terrain vehicle displays
			new Entry( c_display + "Terrain Vehicle/A/Labels", UIText.LabelM, 24, UISprite.None, UIColor.TextSecondary , 1.0f, 41.0f ),
			new Entry( c_display + "Terrain Vehicle/A/Values", UIText.ReadoutM, 24, UISprite.None, UIColor.TextPrimary , 1.0f, 41.0f ),
			new Entry( c_display + "Terrain Vehicle/B/Labels", UIText.LabelM, 24, UISprite.None, UIColor.TextSecondary , 1.0f, 41.0f ),
			new Entry( c_display + "Terrain Vehicle/B/Values", UIText.ReadoutM, 24, UISprite.None, UIColor.TextPrimary , 1.0f, 41.0f ),
			new Entry( c_display + "Terrain Vehicle Cargo/A/Labels", UIText.LabelM, 24, UISprite.None, UIColor.TextSecondary , 1.0f, 41.0f ),
			new Entry( c_display + "Terrain Vehicle Cargo/A/Values", UIText.ReadoutM, 24, UISprite.None, UIColor.TextPrimary , 1.0f, 41.0f ),

			// the ship's name and the officer's icon (the ship's logo on the bridge, the kit's icon of each officer, set by the button controller)
			new Entry( c_officerIcon, UIText.None, 0, UISprite.None, UIColor.AccentPrimary ),
			new Entry( c_right + "C/GameObject/A/GameObject/B/GameObject/Current Officer", UIText.HeadingL, 28, UISprite.None, UIColor.TextPrimary ),

			// the console buttons' panel
			new Entry( c_buttons + "B", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),

			// the message box
			new Entry( c_right + "D/Frame", UIText.None, 0, UISprite.PanelOutline, UIColor.BorderDefault ),
			new Entry( c_right + "D/Frame/Background", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),
			new Entry( c_right + "D/Frame/Background/Messages", UIText.BodyM, 24, UISprite.None, UIColor.TextAccent ),

			// the ship's log
			new Entry( c_viewport + "Ships Log/GameObject/GameObject/Entries Mask/Entries", UIText.BodyM, 30, UISprite.None, UIColor.TextPrimary ),
			new Entry( c_viewport + "Ships Log/GameObject/GameObject/Entries Mask/Selection Xform/Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundHover ),
			new Entry( c_viewport + "Ships Log/GameObject/Divider", UIText.None, 0, UISprite.None, UIColor.BorderDefault ),
			new Entry( c_viewport + "Ships Log/GameObject/Message", UIText.BodyM, 30, UISprite.None, UIColor.TextSecondary ),
			new Entry( c_viewport + "Ships Log/GameObject/GameObject/Entries Mask/Selection Xform/Up Arrow Image", UIText.None, 0, UISprite.None, UIColor.AccentPrimary ),
			new Entry( c_viewport + "Ships Log/GameObject/GameObject/Entries Mask/Selection Xform/Down Arrow Image", UIText.None, 0, UISprite.None, UIColor.AccentPrimary ),

			// the launch countdown in the kit's amber, as the docking bay's sirens (it was the original's yellow; its animation changes only its size and opacity,
			// and it starts invisible, as it was, until its animation shows it)
			new Entry( c_viewport + "Countdown", UIText.DisplayXL, 240, UISprite.None, UIColor.TextWarning, 0.0f ),

			// the popup ("Commencing System Penetration")
			new Entry( "Popup/Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),
			new Entry( "Popup/Panel/Logo", UIText.None, 0, UISprite.None, UIColor.AccentPrimary ),
			new Entry( "Popup/Panel/Right/Message", UIText.HeadingM, 30, UISprite.None, UIColor.TextPrimary ),
			new Entry( "Popup/Panel/Right/Progress Bar", UIText.None, 0, UISprite.Bar, UIColor.BackgroundRaised ),
			new Entry( "Popup/Panel/Right/Progress Bar/Fill", UIText.None, 0, UISprite.Bar, UIColor.AccentPrimary ),
		};

		// the strips behind the status and terrain vehicle readouts are transparent: the kit's readouts stand on the panel
		for ( var i = 1; i <= 6; i++ )
		{
			table.Add( new Entry( c_display + "Status/A/Bars/Bar (" + i + ")", UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised, 0.0f ) );
			table.Add( new Entry( c_display + "Terrain Vehicle/B/Bars/Bar (" + i + ")", UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised, 0.0f ) );
			// (the console buttons' labels take their colours from the button controller, which shows the selected and the running button)
			table.Add( new Entry( c_buttons + "B/GameObject/" + i, UIText.LabelM, 26, UISprite.None, UIColor.Keep ) );
		}

		for ( var i = 1; i <= 5; i++ )
		{
			table.Add( new Entry( c_display + "Terrain Vehicle/A/Bars/Bar (" + i + ")", UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised, 0.0f ) );
		}

		// the message table's columns
		for ( var i = 1; i <= 6; i++ )
		{
			table.Add( new Entry( c_right + "D/Frame/Background/Messages/MessagesTable/Column" + i, UIText.BodyM, 22, UISprite.None, UIColor.TextPrimary ) );
		}

		return table.ToArray();
	}

	// the panels that get an outline on top of them: path, colour
	static readonly (string path, UIColor color)[] c_outlines =
	{
		( "Main/Right/GameObject/B", UIColor.BorderDefault ),
		( c_buttons + "B", UIColor.BorderDefault ),
		( "Popup/Panel", UIColor.BorderAccent ),
	};

	[MenuItem( "Starflight Remake/UI Kit/Apply to the Spaceflight Console" )]
	public static void ApplyFromMenu()
	{
		if ( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
		{
			return;
		}

		EditorSceneManager.OpenScene( c_scenePath, OpenSceneMode.Single );

		Debug.Log( Apply() );
	}

	// for batch mode: open the scene, apply the kit, save the scene
	public static void ApplyAndSave()
	{
		EditorSceneManager.OpenScene( c_scenePath, OpenSceneMode.Single );

		Debug.Log( Apply() );

		EditorSceneManager.SaveScene( EditorSceneManager.GetActiveScene() );
	}

	// gives the open Spaceflight scene's console the kit's look and returns a report
	public static string Apply()
	{
		var report = new System.Text.StringBuilder( "UI kit applied to the Spaceflight console\n" );
		var theme = UIKitTheme.Get();
		var canvas = FindCanvas();

		if ( ( theme == null ) || ( canvas == null ) )
		{
			return "missing " + ( ( theme == null ) ? "the UI kit theme" : "the UI canvas" );
		}

		var styled = ApplyEntries( canvas, GetTable(), report );

		report.AppendLine( styled + " elements styled" );

		// an outline drawn over each panel
		foreach ( var (path, color) in c_outlines )
		{
			var panel = canvas.Find( path );

			if ( panel == null )
			{
				report.AppendLine( "not found: " + path );
				continue;
			}

			AddOutline( panel, color );
		}

		// the console buttons' bullets
		var buttons = canvas.Find( "Main/Right/GameObject/C/GameObject/B/Buttons" );
		var controller = ( buttons != null ) ? buttons.GetComponent<ButtonController>() : null;

		if ( ( controller != null ) && ( theme.m_bulletOffSprite != null ) )
		{
			Undo.RecordObject( controller, "UI kit" );

			controller.m_buttonOffSprite = theme.m_bulletOffSprite;
			controller.m_buttonOnSprite = theme.m_bulletOnSprite;
			controller.m_buttonActiveSprite = theme.m_bulletActiveSprite;

			EditorUtility.SetDirty( controller );

			// the bullets as the scene starts: the first button selected
			for ( var i = 1; i <= 6; i++ )
			{
				var bullet = buttons.Find( "A/GameObject/" + i );
				var image = ( bullet != null ) ? bullet.GetComponent<Image>() : null;

				if ( image != null )
				{
					Undo.RecordObject( image, "UI kit" );

					image.sprite = ( i == 1 ) ? theme.m_bulletOnSprite : theme.m_bulletOffSprite;
					image.preserveAspect = true;
					image.color = Color.white;

					EditorUtility.SetDirty( image );
				}
			}

			report.AppendLine( "console bullets set" );
		}
		else
		{
			report.AppendLine( "console bullets not set (no button controller or no bullets in the theme)" );
		}

		// the officer's icon: the button controller changes it with the officer, and the icons are square where the ship's logo is wide, so it keeps its aspect
		var officerIcon = canvas.Find( c_officerIcon );
		var officerImage = ( officerIcon != null ) ? officerIcon.GetComponent<Image>() : null;

		if ( ( controller != null ) && ( officerImage != null ) )
		{
			Undo.RecordObject( controller, "UI kit" );
			controller.m_officerIcon = officerImage;
			EditorUtility.SetDirty( controller );

			Undo.RecordObject( officerImage, "UI kit" );
			officerImage.preserveAspect = true;
			EditorUtility.SetDirty( officerImage );

			report.AppendLine( "officer icon set" );
		}
		else
		{
			report.AppendLine( "officer icon not set (no button controller or no image at " + c_officerIcon + ")" );
		}

		// the highlight behind the selected console button: the first child of the labels' panel (above the panel, below the labels), the width of the panel,
		// moved and coloured by the button controller; a bar on its left edge marks the selected button that is not running
		var labelPanel = canvas.Find( c_buttons + "B" );

		if ( ( controller != null ) && ( labelPanel != null ) )
		{
			var highlight = labelPanel.Find( "UI Kit Selection" );

			if ( highlight == null )
			{
				var go = new GameObject( "UI Kit Selection", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( LayoutElement ) );

				Undo.RegisterCreatedObjectUndo( go, "UI kit" );

				highlight = go.transform;
				highlight.SetParent( labelPanel, false );
			}

			highlight.SetAsFirstSibling();

			var highlightRect = (RectTransform) highlight;

			highlightRect.anchorMin = new Vector2( 0.0f, 0.5f );
			highlightRect.anchorMax = new Vector2( 1.0f, 0.5f );
			highlightRect.pivot = new Vector2( 0.5f, 0.5f );
			highlightRect.anchoredPosition = new Vector2( ( c_selectionInsetLeft - c_selectionInsetRight ) * 0.5f, 0.0f );
			highlightRect.sizeDelta = new Vector2( -( c_selectionInsetLeft + c_selectionInsetRight ), 40.0f );
			highlight.GetComponent<LayoutElement>().ignoreLayout = true;

			var highlightImage = highlight.GetComponent<Image>();

			highlightImage.raycastTarget = false;
			highlightImage.sprite = theme.m_panelSprite;
			highlightImage.type = Image.Type.Sliced;
			highlightImage.color = theme.m_backgroundHover;

			var bar = highlight.Find( "Bar" );

			if ( bar == null )
			{
				var go = new GameObject( "Bar", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );

				Undo.RegisterCreatedObjectUndo( go, "UI kit" );

				bar = go.transform;
				bar.SetParent( highlight, false );
			}

			var barRect = (RectTransform) bar;

			barRect.anchorMin = new Vector2( 0.0f, 0.0f );
			barRect.anchorMax = new Vector2( 0.0f, 1.0f );
			barRect.pivot = new Vector2( 0.0f, 0.5f );
			barRect.anchoredPosition = Vector2.zero;
			barRect.sizeDelta = new Vector2( c_selectionBarWidth, 0.0f );

			var barImage = bar.GetComponent<Image>();

			barImage.raycastTarget = false;
			barImage.sprite = null;
			barImage.color = theme.m_accentPrimary;

			Undo.RecordObject( controller, "UI kit" );
			controller.m_selectionHighlight = highlightImage;
			controller.m_selectionBar = barImage;

			EditorUtility.SetDirty( controller );
			EditorUtility.SetDirty( highlightImage );
			EditorUtility.SetDirty( barImage );

			report.AppendLine( "selection highlight set" );
		}
		else
		{
			report.AppendLine( "selection highlight not set (no button controller or no label panel at " + c_buttons + "B)" );
		}

		// the docking bay sirens' lights shine in the kit's amber, as their domes do (the launch and landing animations change only their intensity)
		var amberLights = 0;

		foreach ( var light in Object.FindObjectsByType<Light>( FindObjectsInactive.Include, FindObjectsSortMode.None ) )
		{
			if ( light.name.StartsWith( "Amber Light" ) )
			{
				Undo.RecordObject( light, "UI kit" );
				light.color = theme.m_accentSecondary;
				EditorUtility.SetDirty( light );
				amberLights++;
			}
		}

		report.AppendLine( amberLights + " siren lights in the kit's amber" );

		// the target bracket in the viewport (the Figma file's Encounter and Combat mockups)
		report.AppendLine( MakeTargetBracket( canvas ) );

		// the stars' coordinates in hyperspace, and the frame on the viewport (the Hyperspace mockup)
		report.AppendLine( MakeLabelLayer<StarLabels>( canvas, "UI Kit Star Labels", "123 · 101" ) );
		report.AppendLine( MakeLabelLayer<PlanetLabels>( canvas, "UI Kit Planet Labels", "III · Liquid" ) );
		report.AppendLine( MakeViewportFrame( canvas ) );

		EditorSceneManager.MarkSceneDirty( canvas.gameObject.scene );

		return report.ToString();
	}

	// the length and thickness of a target bracket corner's arms, and the label's size under it
	const float c_bracketArm = 16.0f;
	const float c_bracketThickness = 2.0f;
	const float c_bracketLabelSize = 16.0f;

	// makes (or remakes) the viewport's target bracket: a TargetBracket that covers the viewport, a bracket of four corners (two bars each) that it places
	// around the target, and a label under it; drawn under the viewport's fade, so that a fade covers it too
	static string MakeTargetBracket( Transform canvas )
	{
		var viewport = canvas.Find( c_viewport.TrimEnd( '/' ) );

		if ( viewport == null )
		{
			return "target bracket not made (no viewport at " + c_viewport + ")";
		}

		var root = viewport.Find( "UI Kit Target" );

		if ( root != null )
		{
			Undo.DestroyObjectImmediate( root.gameObject );
		}

		// (the mask keeps a bracket and its label near the edge inside the viewport, off the console)
		var rootObject = new GameObject( "UI Kit Target", typeof( RectTransform ), typeof( RectMask2D ), typeof( TargetBracket ) );

		Undo.RegisterCreatedObjectUndo( rootObject, "UI kit" );

		var rootRect = (RectTransform) rootObject.transform;

		rootRect.SetParent( viewport, false );
		rootRect.anchorMin = Vector2.zero;
		rootRect.anchorMax = Vector2.one;
		rootRect.offsetMin = Vector2.zero;
		rootRect.offsetMax = Vector2.zero;

		// under the fade (the last thing the viewport draws when it fades out)
		var fade = viewport.Find( "Fade" );

		if ( fade != null )
		{
			rootRect.SetSiblingIndex( fade.GetSiblingIndex() );
		}

		// the bracket, placed and sized by the TargetBracket
		var bracket = new GameObject( "Bracket", typeof( RectTransform ) );
		var bracketRect = (RectTransform) bracket.transform;

		bracketRect.SetParent( rootRect, false );
		bracketRect.anchorMin = new Vector2( 0.5f, 0.5f );
		bracketRect.anchorMax = new Vector2( 0.5f, 0.5f );
		bracketRect.pivot = new Vector2( 0.5f, 0.5f );
		bracketRect.sizeDelta = new Vector2( 120.0f, 80.0f );

		// four corners, each a horizontal and a vertical bar along the bracket's edges
		var bars = new System.Collections.Generic.List<Graphic>();

		foreach ( var corner in new[] { new Vector2( 0, 1 ), new Vector2( 1, 1 ), new Vector2( 0, 0 ), new Vector2( 1, 0 ) } )
		{
			bars.Add( MakeBar( bracketRect, corner, new Vector2( c_bracketArm, c_bracketThickness ) ) );
			bars.Add( MakeBar( bracketRect, corner, new Vector2( c_bracketThickness, c_bracketArm ) ) );
		}

		// the label under the bracket, in the kit's small label style (its colour is the TargetBracket's)
		var label = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( TMPro.TextMeshProUGUI ) );
		var labelRect = (RectTransform) label.transform;
		var labelText = label.GetComponent<TMPro.TextMeshProUGUI>();

		labelRect.SetParent( bracketRect, false );
		labelRect.anchorMin = new Vector2( 0.5f, 0.0f );
		labelRect.anchorMax = new Vector2( 0.5f, 0.0f );
		labelRect.pivot = new Vector2( 0.5f, 1.0f );
		labelRect.anchoredPosition = new Vector2( 0.0f, -6.0f );
		labelRect.sizeDelta = new Vector2( 600.0f, 24.0f );
		labelText.alignment = TMPro.TextAlignmentOptions.Top;
		labelText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
		labelText.raycastTarget = false;
		labelText.text = "Vessel · Range 0";

		Style( labelText, UIText.LabelS, c_bracketLabelSize, UISprite.None, UIColor.Keep );

		var targetBracket = rootObject.GetComponent<TargetBracket>();

		targetBracket.m_bracket = bracketRect;
		targetBracket.m_bars = bars.ToArray();
		targetBracket.m_label = labelText;

		// hidden until there is a target
		bracket.SetActive( false );

		EditorUtility.SetDirty( targetBracket );

		return "target bracket made";
	}

	// the size of a star's coordinates in hyperspace, the frame's corner arms, its margin from the viewport's edges, and the size of the location's name
	const float c_starLabelSize = 14.0f;
	const float c_frameArm = 20.0f;
	const float c_frameMargin = 6.0f;
	const float c_frameLabelSize = 14.0f;

	// makes (or remakes) a layer of labels in the viewport (the stars' coordinates in hyperspace, the planets' orbits in a star system): a component that covers
	// the viewport (masked to it, under the target bracket and the fade) and the hidden label it clones its labels from
	static string MakeLabelLayer<T>( Transform canvas, string name, string sample ) where T : MonoBehaviour
	{
		var viewport = canvas.Find( c_viewport.TrimEnd( '/' ) );

		if ( viewport == null )
		{
			return name + " not made (no viewport at " + c_viewport + ")";
		}

		var old = viewport.Find( name );

		if ( old != null )
		{
			Undo.DestroyObjectImmediate( old.gameObject );
		}

		var rootObject = new GameObject( name, typeof( RectTransform ), typeof( RectMask2D ), typeof( T ) );

		Undo.RegisterCreatedObjectUndo( rootObject, "UI kit" );

		var rootRect = (RectTransform) rootObject.transform;

		rootRect.SetParent( viewport, false );
		rootRect.anchorMin = Vector2.zero;
		rootRect.anchorMax = Vector2.one;
		rootRect.offsetMin = Vector2.zero;
		rootRect.offsetMax = Vector2.zero;

		// under the target bracket (and so under the fade)
		var target = viewport.Find( "UI Kit Target" );
		var fade = viewport.Find( "Fade" );

		if ( target != null )
		{
			rootRect.SetSiblingIndex( target.GetSiblingIndex() );
		}
		else if ( fade != null )
		{
			rootRect.SetSiblingIndex( fade.GetSiblingIndex() );
		}

		// the label the pool is cloned from: placed by its left edge, beside the star
		var label = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( TMPro.TextMeshProUGUI ) );
		var labelRect = (RectTransform) label.transform;
		var labelText = label.GetComponent<TMPro.TextMeshProUGUI>();

		labelRect.SetParent( rootRect, false );
		labelRect.anchorMin = new Vector2( 0.5f, 0.5f );
		labelRect.anchorMax = new Vector2( 0.5f, 0.5f );
		labelRect.pivot = new Vector2( 0.0f, 0.5f );
		labelRect.sizeDelta = new Vector2( 200.0f, 24.0f );
		labelText.alignment = TMPro.TextAlignmentOptions.Left;
		labelText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
		labelText.raycastTarget = false;
		labelText.text = sample;

		Style( labelText, UIText.LabelS, c_starLabelSize, UISprite.None, UIColor.TextSecondary );

		// the component's template (both kinds of label layer have one)
		var layer = rootObject.GetComponent<T>();
		var field = typeof( T ).GetField( "m_template" );

		if ( field != null )
		{
			field.SetValue( layer, labelText );
		}

		EditorUtility.SetDirty( layer );

		return name + " made";
	}

	// makes (or remakes) the viewport's frame: a ViewportFrame a little inside the viewport's edges, with a corner mark (two bars) at each corner and the
	// location's name in the top left; drawn over everything else in the viewport
	static string MakeViewportFrame( Transform canvas )
	{
		var viewport = canvas.Find( c_viewport.TrimEnd( '/' ) );

		if ( viewport == null )
		{
			return "viewport frame not made (no viewport at " + c_viewport + ")";
		}

		var old = viewport.Find( "UI Kit Frame" );

		if ( old != null )
		{
			Undo.DestroyObjectImmediate( old.gameObject );
		}

		var rootObject = new GameObject( "UI Kit Frame", typeof( RectTransform ), typeof( ViewportFrame ) );

		Undo.RegisterCreatedObjectUndo( rootObject, "UI kit" );

		var rootRect = (RectTransform) rootObject.transform;

		rootRect.SetParent( viewport, false );
		rootRect.anchorMin = Vector2.zero;
		rootRect.anchorMax = Vector2.one;
		rootRect.offsetMin = new Vector2( c_frameMargin, c_frameMargin );
		rootRect.offsetMax = new Vector2( -c_frameMargin, -c_frameMargin );
		rootRect.SetAsLastSibling();

		foreach ( var corner in new[] { new Vector2( 0, 1 ), new Vector2( 1, 1 ), new Vector2( 0, 0 ), new Vector2( 1, 0 ) } )
		{
			Style( MakeBar( rootRect, corner, new Vector2( c_frameArm, c_bracketThickness ) ), UIText.None, 0, UISprite.None, UIColor.AccentPrimary );
			Style( MakeBar( rootRect, corner, new Vector2( c_bracketThickness, c_frameArm ) ), UIText.None, 0, UISprite.None, UIColor.AccentPrimary );
		}

		// the location's name, inside the top left corner
		var label = new GameObject( "Location", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( TMPro.TextMeshProUGUI ) );
		var labelRect = (RectTransform) label.transform;
		var labelText = label.GetComponent<TMPro.TextMeshProUGUI>();

		labelRect.SetParent( rootRect, false );
		labelRect.anchorMin = new Vector2( 0.0f, 1.0f );
		labelRect.anchorMax = new Vector2( 0.0f, 1.0f );
		labelRect.pivot = new Vector2( 0.0f, 1.0f );
		labelRect.anchoredPosition = new Vector2( c_frameArm + 8.0f, -6.0f );
		labelRect.sizeDelta = new Vector2( 400.0f, 24.0f );
		labelText.alignment = TMPro.TextAlignmentOptions.TopLeft;
		labelText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
		labelText.raycastTarget = false;
		labelText.text = "Hyperspace";

		Style( labelText, UIText.LabelS, c_frameLabelSize, UISprite.None, UIColor.TextAccent );

		// the status, inside the top right corner (the planet in orbit, the orbit to go into, the comm link, the armed weapons)
		var status = new GameObject( "Status", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( TMPro.TextMeshProUGUI ) );
		var statusRect = (RectTransform) status.transform;
		var statusText = status.GetComponent<TMPro.TextMeshProUGUI>();

		statusRect.SetParent( rootRect, false );
		statusRect.anchorMin = new Vector2( 1.0f, 1.0f );
		statusRect.anchorMax = new Vector2( 1.0f, 1.0f );
		statusRect.pivot = new Vector2( 1.0f, 1.0f );
		statusRect.anchoredPosition = new Vector2( -( c_frameArm + 8.0f ), -6.0f );
		statusRect.sizeDelta = new Vector2( 600.0f, 24.0f );
		statusText.alignment = TMPro.TextAlignmentOptions.TopRight;
		statusText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
		statusText.raycastTarget = false;
		statusText.text = "";

		Style( statusText, UIText.LabelS, c_frameLabelSize, UISprite.None, UIColor.TextSecondary );

		var frame = rootObject.GetComponent<ViewportFrame>();

		frame.m_label = labelText;
		frame.m_status = statusText;

		EditorUtility.SetDirty( frame );

		return "viewport frame made";
	}

	// one bar of a target bracket corner: anchored at the corner, reaching into the bracket
	static Graphic MakeBar( RectTransform bracket, Vector2 corner, Vector2 size )
	{
		var bar = new GameObject( "Bar", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		var rect = (RectTransform) bar.transform;
		var image = bar.GetComponent<Image>();

		rect.SetParent( bracket, false );
		rect.anchorMin = corner;
		rect.anchorMax = corner;
		rect.pivot = corner;
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = size;
		image.raycastTarget = false;

		return image;
	}

	// gives each element of a table its UIKitStyle (a new one, or the one it has) and returns how many were styled; what is not found goes into the report
	static int ApplyEntries( Transform canvas, System.Collections.Generic.IEnumerable<Entry> entries, System.Text.StringBuilder report )
	{
		var styled = 0;

		foreach ( var entry in entries )
		{
			// a name with a slash in it (Bio / Min, written "Bio \/ Min" in the table) cannot be found by path, so it is looked for under its parent
			var target = entry.m_path.Contains( "\\/" ) ? null : canvas.Find( entry.m_path );

			if ( ( target == null ) && entry.m_path.Contains( "\\/" ) )
			{
				var split = entry.m_path.LastIndexOf( '/', entry.m_path.IndexOf( "\\/" ) );
				var parent = canvas.Find( entry.m_path.Substring( 0, split ) );
				var name = entry.m_path.Substring( split + 1 ).Replace( "\\/", "/" );

				target = ( parent != null ) ? FindChild( parent, name ) : null;
			}

			if ( target == null )
			{
				report.AppendLine( "not found: " + entry.m_path );
				continue;
			}

			var style = target.GetComponent<UIKitStyle>();

			if ( style == null )
			{
				style = Undo.AddComponent<UIKitStyle>( target.gameObject );
			}

			Undo.RecordObject( style, "UI kit" );

			style.m_textStyle = entry.m_text;
			style.m_fontSize = entry.m_size;
			style.m_sprite = entry.m_sprite;
			style.m_color = entry.m_color;
			style.m_alpha = entry.m_alpha;
			style.m_lineHeight = entry.m_lineHeight;

			Apply( style );

			styled++;
		}

		return styled;
	}

	// draws the kit's outline over a panel: a child that covers it, is drawn last and takes no part in a layout or in clicks
	static void AddOutline( Transform panel, UIColor color )
	{
		var outline = panel.Find( "UI Kit Outline" );

		if ( outline == null )
		{
			var go = new GameObject( "UI Kit Outline", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( LayoutElement ) );

			Undo.RegisterCreatedObjectUndo( go, "UI kit" );

			outline = go.transform;
			outline.SetParent( panel, false );
		}

		var rect = (RectTransform) outline;

		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		outline.SetAsLastSibling();
		outline.GetComponent<LayoutElement>().ignoreLayout = true;
		outline.GetComponent<Image>().raycastTarget = false;

		var style = outline.GetComponent<UIKitStyle>();

		if ( style == null )
		{
			style = outline.gameObject.AddComponent<UIKitStyle>();
		}

		style.m_sprite = UISprite.PanelOutline;
		style.m_color = color;

		Apply( style );
	}

	// ---------------------------------------------------------------- the Starport rooms

	const string c_starportScenePath = "Assets/Scenes/Starport.unity";

	[MenuItem( "Starflight Remake/UI Kit/Apply to the Starport Rooms" )]
	public static void ApplyStarportFromMenu()
	{
		if ( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
		{
			return;
		}

		EditorSceneManager.OpenScene( c_starportScenePath, OpenSceneMode.Single );

		Debug.Log( ApplyToStarport() );
	}

	// for batch mode: open the Starport scene, apply the kit, save the scene
	public static void ApplyStarportAndSave()
	{
		EditorSceneManager.OpenScene( c_starportScenePath, OpenSceneMode.Single );

		Debug.Log( ApplyToStarport() );

		EditorSceneManager.SaveScene( EditorSceneManager.GetActiveScene() );
	}

	// the Starport rooms are built alike, so their elements get their roles by rule from what they draw (a sprite and its colour, a font and its colour)
	// rather than from a table of paths; an element that already has a UIKitStyle keeps it, so running this again styles only what is new
	public static string ApplyToStarport()
	{
		var report = new System.Text.StringBuilder( "UI kit applied to the Starport rooms\n" );
		var theme = UIKitTheme.Get();
		var canvas = FindCanvas();

		if ( ( theme == null ) || ( canvas == null ) )
		{
			return "missing " + ( ( theme == null ) ? "the UI kit theme" : "the UI canvas" );
		}

		var counts = new System.Collections.Generic.SortedDictionary<string, int>();
		void Count( string rule ) { counts[ rule ] = counts.TryGetValue( rule, out var n ) ? n + 1 : 1; }

		// the buttons' images are styled as buttons
		var buttons = canvas.GetComponentsInChildren<Button>( true );
		var buttonImages = new System.Collections.Generic.HashSet<Graphic>();

		foreach ( var button in buttons )
		{
			if ( button.targetGraphic != null )
			{
				buttonImages.Add( button.targetGraphic );
			}
		}

		foreach ( var image in canvas.GetComponentsInChildren<Image>( true ) )
		{
			if ( image.GetComponent<UIKitStyle>() != null )
			{
				continue;
			}

			var sprite = ( image.sprite != null ) ? image.sprite.name : "";
			var colour = ColorUtility.ToHtmlStringRGB( image.color );
			var alpha = image.color.a;

			if ( buttonImages.Contains( image ) )
			{
				// a button: the kit's panel in white, its colours come from the button's tint (set below)
				Style( image, UIText.None, 0, UISprite.Panel, UIColor.Keep );
				Undo.RecordObject( image, "UI kit" );
				image.color = Color.white;
				Count( "button" );
			}
			else if ( sprite == "Panel - Background" )
			{
				if ( ( ( colour == "5455FF" ) || ( colour == "5355FF" ) ) && ( alpha > 0.9f ) )
				{
					// a room or a dialog
					Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel );
					AddOutline( image.transform, UIColor.BorderDefault );
					Count( "room or dialog panel" );
				}
				else if ( ( colour == "5355FF" ) || ( colour == "5455FF" ) )
				{
					Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised );
					Count( "sub-panel" );
				}
				else if ( colour == "FFFFFF" && ( alpha < 0.9f ) )
				{
					// the inner area of a room
					Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundApp, 0.6f );
					Count( "inner area" );
				}
				else if ( colour == "FFFFFF" )
				{
					// a field to type in
					Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundApp );
					AddOutline( image.transform, UIColor.BorderAccent );
					Count( "input field" );
				}
				else if ( colour == "6F0000" )
				{
					Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel );
					AddOutline( image.transform, UIColor.BorderAlert );
					Count( "alert dialog" );
				}
				else if ( colour == "A35514" )
				{
					Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel );
					AddOutline( image.transform, UIColor.AccentSecondary );
					Count( "caution panel" );
				}
				else if ( colour == "262A38" )
				{
					// the shade over a room behind a dialog
					Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundApp, 0.85f );
					Count( "overlay" );
				}
				else
				{
					report.AppendLine( "left as it is: panel " + colour + " at " + PathOf( image.transform ) );
				}
			}
			else if ( sprite == "Small-Corners" )
			{
				// a selection bar
				Style( image, UIText.None, 0, UISprite.Panel, UIColor.AccentPrimaryDim, ( alpha > 0.9f ) ? 0.55f : 0.35f );
				Count( "selection bar" );
			}
			else if ( sprite == "Panel Button - Right Arrow" )
			{
				Style( image, UIText.None, 0, UISprite.None, ( colour == "404040" ) ? UIColor.TextMuted : UIColor.AccentPrimary );
				Count( "arrow" );
			}
			else if ( sprite.StartsWith( "Interstel Logo" ) )
			{
				Style( image, UIText.None, 0, UISprite.None, UIColor.AccentSecondary );
				Count( "logo" );
			}
			else if ( sprite == "Panel - Horizontal Line" )
			{
				Style( image, UIText.None, 0, UISprite.None, UIColor.BorderDefault );
				Count( "line" );
			}
			else if ( sprite == "UISprite" )
			{
				Style( image, UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised );
				Count( "bar" );
			}
			else if ( sprite.StartsWith( "Personnel - " ) && ( image.transform.parent != null ) && ( image.transform.parent.Find( "UI Kit Backdrop" ) == null ) )
			{
				// a race's portrait is a black silhouette: it stands on the sensor window's colour, as the vessels' sensor pictures do
				AddBackdrop( image.transform.parent, UIColor.SensorWindow );
				Count( "portrait backdrop" );
			}
		}

		// the buttons' tints: navy, dim cyan when the cursor is on them
		foreach ( var button in buttons )
		{
			if ( TintButton( button, theme ) )
			{
				Count( "button tint" );
			}
		}

		foreach ( var text in canvas.GetComponentsInChildren<TMPro.TMP_Text>( true ) )
		{
			if ( ( text.GetComponent<UIKitStyle>() != null ) || ( text.font == null ) )
			{
				continue;
			}

			var font = text.font.name;
			var colour = ColorUtility.ToHtmlStringRGB( text.color );
			var size = text.fontSize;
			var inButton = text.GetComponentInParent<Button>( true ) != null;

			// a text of several lines keeps its line height, so that columns of names and values stay side by side
			var lineHeight = text.text.Contains( "\n" ) ? NaturalLineHeight( text ) : 0.0f;

			if ( font.StartsWith( "Space Age" ) && ( ( colour == "4FF9ED" ) || ( colour == "4FF8ED" ) ) )
			{
				// a button's label, or the question of a dialog
				Style( text, UIText.LabelM, Mathf.Round( size * ( inButton ? 0.6f : 0.7f ) ), UISprite.None, inButton ? UIColor.TextPrimary : UIColor.TextAccent, 1.0f, lineHeight );
				Count( inButton ? "button label" : "dialog label" );
			}
			else if ( font.StartsWith( "Space Age" ) && ( size >= 90.0f ) )
			{
				Style( text, UIText.DisplayXL, Mathf.Round( size * 0.75f ), UISprite.None, UIColor.TextPrimary, 1.0f, lineHeight );
				Count( "title" );
			}
			else if ( font.StartsWith( "Space Age" ) && ( size >= 44.0f ) )
			{
				Style( text, UIText.HeadingL, Mathf.Round( size * 0.72f ), UISprite.None, UIColor.TextPrimary, 1.0f, lineHeight );
				Count( "heading" );
			}
			else if ( font.StartsWith( "Space Age" ) )
			{
				Style( text, UIText.LabelM, Mathf.Round( size * 0.85f ), UISprite.None, UIColor.TextSecondary, 1.0f, lineHeight );
				Count( "info line" );
			}
			else if ( font.StartsWith( "Audiowide" ) )
			{
				var role = ( ( colour == "4FF8ED" ) || ( colour == "4FF9ED" ) ) ? UIColor.TextAccent : ( colour == "211104" ) ? UIColor.TextWarning : UIColor.TextPrimary;

				Style( text, UIText.BodyM, Mathf.Round( size ), UISprite.None, role, 1.0f, lineHeight );
				Count( "body text" );
			}
		}

		// the legacy texts (the name field) only change colour
		foreach ( var text in canvas.GetComponentsInChildren<Text>( true ) )
		{
			if ( text.GetComponent<UIKitStyle>() == null )
			{
				Style( text, UIText.None, 0, UISprite.None, UIColor.TextPrimary );
				Count( "legacy text" );
			}
		}

		foreach ( var pair in counts )
		{
			report.AppendLine( pair.Key + ": " + pair.Value );
		}

		EditorSceneManager.MarkSceneDirty( canvas.gameObject.scene );

		return report.ToString();
	}

	// ---------------------------------------------------------------- the intro

	const string c_introScenePath = "Assets/Scenes/Intro.unity";
	const string c_introLogoFolder = "Assets/Game Objects/Intro/";

	// what each element of the intro's pages becomes (chosen 2026-10-12: all of it in the kit, the logos too; Electronic Arts' logo stays theirs)
	static Entry[] GetIntroTable()
	{
		var entries = new System.Collections.Generic.List<Entry>
		{
			// page 2: the Starflight logo in the kit's cyan (it is white), the copyright line on a navy strip
			new Entry( "Page 2/Starflight Image", UIText.None, 0, UISprite.None, UIColor.AccentPrimary ),
			new Entry( "Page 2/Panel", UIText.None, 0, UISprite.None, UIColor.BackgroundPanel, 0.85f ),
			new Entry( "Page 2/Panel/Copyright Text", UIText.LabelM, 36, UISprite.None, UIColor.TextSecondary ),
			new Entry( "Page 2/Panel/Electronic Arts Text", UIText.LabelM, 36, UISprite.None, UIColor.TextSecondary ),

			// page 3: the remake's credits on a kit panel (the roles in its cyan, the names in its primary text colour, set below)
			new Entry( "Page 3/Xform/Heading", UIText.HeadingL, 46, UISprite.None, UIColor.TextPrimary ),
			new Entry( "Page 3/Xform/Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel, 0.92f ),
			new Entry( "Page 3/Xform/Panel/Top Credits", UIText.BodyM, 40, UISprite.None, UIColor.TextAccent ),
			new Entry( "Page 3/Xform/Panel/GameObject/Left Credits", UIText.BodyM, 40, UISprite.None, UIColor.TextAccent ),
			new Entry( "Page 3/Xform/Panel/GameObject/Right Credits", UIText.BodyM, 40, UISprite.None, UIColor.TextAccent ),
			new Entry( "Page 3/Xform/Panel/Bottom Credits", UIText.BodyM, 40, UISprite.None, UIColor.TextAccent ),
			new Entry( "Page 3/Xform/Footer", UIText.LabelM, 40, UISprite.None, UIColor.TextAccent ),

			// page 5: Interstel's warning on a kit panel with the alert outline (it was a red panel)
			new Entry( "Page 5/Dialog", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),
			new Entry( "Page 5/Dialog/Panel/Heading Text", UIText.HeadingM, 30, UISprite.None, UIColor.TextAlert ),
			new Entry( "Page 5/Dialog/Panel/Body Text", UIText.BodyM, 38, UISprite.None, UIColor.TextPrimary ),
		};

		// page 1: the original team's names in the kit's heading style
		foreach ( var name in new[] { "Row 1 Panel/Rod McConnell", "Row 1 Panel/Greg Johnson", "Row 2 Panel/T. C. Lee", "Row 3 Panel/Alec Kercso", "Row 3 Panel/Bob Gonsalves" } )
		{
			entries.Add( new Entry( "Page 1/Credits Panel/" + name, UIText.HeadingL, 50, UISprite.None, UIColor.TextPrimary ) );
		}

		return entries.ToArray();
	}

	[MenuItem( "Starflight Remake/UI Kit/Apply to the Intro" )]
	public static void ApplyIntroFromMenu()
	{
		if ( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
		{
			return;
		}

		EditorSceneManager.OpenScene( c_introScenePath, OpenSceneMode.Single );

		Debug.Log( ApplyToIntro() );
	}

	// for batch mode: open the Intro scene, apply the kit, save the scene
	public static void ApplyIntroAndSave()
	{
		EditorSceneManager.OpenScene( c_introScenePath, OpenSceneMode.Single );

		Debug.Log( ApplyToIntro() );

		EditorSceneManager.SaveScene( EditorSceneManager.GetActiveScene() );
	}

	// gives the open Intro scene's pages the kit's look and returns a report
	public static string ApplyToIntro()
	{
		var report = new System.Text.StringBuilder( "UI kit applied to the intro\n" );
		var theme = UIKitTheme.Get();
		var canvas = FindCanvas();

		if ( ( theme == null ) || ( canvas == null ) )
		{
			return "missing " + ( ( theme == null ) ? "the UI kit theme" : "the UI canvas" );
		}

		report.AppendLine( ApplyEntries( canvas, GetIntroTable(), report ) + " elements styled" );

		// outlines on the two panels
		foreach ( var (path, color) in new[] { ( "Page 3/Xform/Panel", UIColor.BorderDefault ), ( "Page 5/Dialog", UIColor.BorderAlert ) } )
		{
			var panel = canvas.Find( path );

			if ( panel != null )
			{
				AddOutline( panel, color );
			}
			else
			{
				report.AppendLine( "not found: " + path );
			}
		}

		// the credits' names were green (colour tags in the text): the kit's primary text colour
		var nameTag = "<color=#" + ColorUtility.ToHtmlStringRGB( theme.m_textPrimary ) + ">";

		foreach ( var path in new[] { "Page 3/Xform/Panel/Top Credits", "Page 3/Xform/Panel/GameObject/Left Credits", "Page 3/Xform/Panel/GameObject/Right Credits", "Page 3/Xform/Panel/Bottom Credits" } )
		{
			var credits = canvas.Find( path );

			if ( ( credits != null ) && credits.TryGetComponent<TMPro.TMP_Text>( out var text ) && text.text.Contains( "<color=\"green\">" ) )
			{
				Undo.RecordObject( text, "UI kit" );
				text.text = text.text.Replace( "<color=\"green\">", nameTag );
				EditorUtility.SetDirty( text );
			}
		}

		// the Binary Systems logo's red line in the kit's cyan (a copy of each picture, made from the original, which stays)
		foreach ( var part in new[] { "Left", "Middle", "Right" } )
		{
			var image = canvas.Find( "Page 1/Binary Systems/" + part );
			var sprite = MakeKitLogo( "Intro Binary Systems " + part, theme.m_accentPrimary, report );

			if ( ( image != null ) && image.TryGetComponent<Image>( out var logo ) && ( sprite != null ) )
			{
				Undo.RecordObject( logo, "UI kit" );
				logo.sprite = sprite;
				EditorUtility.SetDirty( logo );
			}
		}

		EditorSceneManager.MarkSceneDirty( canvas.gameObject.scene );

		return report.ToString();
	}

	// makes the kit's copy of one of the intro's logo pictures: its red pixels in this colour (as bright as they were), everything else as it was; the copy
	// gets the original's import settings
	static Sprite MakeKitLogo( string name, Color colour, System.Text.StringBuilder report )
	{
		var sourcePath = c_introLogoFolder + name + ".png";
		var kitPath = c_introLogoFolder + name + " Kit.png";

		if ( !System.IO.File.Exists( sourcePath ) )
		{
			report.AppendLine( "missing " + sourcePath );
			return null;
		}

		var source = new Texture2D( 2, 2, TextureFormat.RGBA32, false );

		if ( !source.LoadImage( System.IO.File.ReadAllBytes( sourcePath ) ) )
		{
			report.AppendLine( "could not read " + sourcePath );
			return null;
		}

		var pixels = source.GetPixels32();
		var changed = 0;

		for ( var i = 0; i < pixels.Length; i++ )
		{
			Color c = pixels[ i ];

			// red: well above green and blue
			var redness = Mathf.Clamp01( ( c.r - Mathf.Max( c.g, c.b ) ) / 0.4f );

			if ( redness <= 0.0f )
			{
				continue;
			}

			// the kit's colour, as bright as the red was (dark red edges stay visible), blended by how red the pixel is
			var mixed = Color.Lerp( c, new Color( Mathf.Clamp01( colour.r * Mathf.Max( c.r, 0.6f ) ), Mathf.Clamp01( colour.g * Mathf.Max( c.r, 0.6f ) ), Mathf.Clamp01( colour.b * Mathf.Max( c.r, 0.6f ) ), c.a ), redness );

			mixed.a = c.a;
			pixels[ i ] = mixed;
			changed++;
		}

		var copy = new Texture2D( source.width, source.height, TextureFormat.RGBA32, false );

		copy.SetPixels32( pixels );
		copy.Apply();

		System.IO.File.WriteAllBytes( kitPath, copy.EncodeToPNG() );

		Object.DestroyImmediate( source );
		Object.DestroyImmediate( copy );

		AssetDatabase.ImportAsset( kitPath, ImportAssetOptions.ForceSynchronousImport );

		var importer = AssetImporter.GetAtPath( kitPath ) as TextureImporter;
		var original = AssetImporter.GetAtPath( sourcePath ) as TextureImporter;

		if ( ( importer != null ) && ( original != null ) )
		{
			importer.textureType = original.textureType;
			importer.spriteImportMode = original.spriteImportMode;
			importer.spritePixelsPerUnit = original.spritePixelsPerUnit;
			importer.spriteBorder = original.spriteBorder;
			importer.spritePivot = original.spritePivot;
			importer.alphaIsTransparency = original.alphaIsTransparency;
			importer.mipmapEnabled = original.mipmapEnabled;
			importer.wrapMode = original.wrapMode;
			importer.filterMode = original.filterMode;
			importer.textureCompression = original.textureCompression;
			importer.maxTextureSize = original.maxTextureSize;
			importer.SaveAndReimport();
		}

		report.AppendLine( name + " Kit: " + changed + " red pixels in the kit's colour" );

		return AssetDatabase.LoadAssetAtPath<Sprite>( kitPath );
	}

	// ---------------------------------------------------------------- the save game panel

	const string c_persistentScenePath = "Assets/Scenes/Persistent.unity";
	const string c_savePanel = "Save Game Panel/Root/";
	const string c_saveContent = "Save Game Panel/Root/Xform/";

	// the largest size of a slot's description (it sizes itself down to fit its well: Rajdhani is narrower than the font it had, so it can be larger)
	const float c_saveDescriptionSize = 30.0f;

	// what each element of the save game panel becomes (its buttons are styled by rule, as the Starport's are)
	static Entry[] GetSavePanelTable()
	{
		var entries = new System.Collections.Generic.List<Entry>
		{
			// the panel, Interstel's logo in amber as in the Starport, the heading
			new Entry( "Save Game Panel/Root", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),
			new Entry( c_saveContent + "Heading/Logo Image", UIText.None, 0, UISprite.None, UIColor.AccentSecondary ),
			new Entry( c_saveContent + "Heading/Heading Text", UIText.HeadingL, 35, UISprite.None, UIColor.TextPrimary ),

			// the shade over the panel behind a dialog, and the dialogs (their questions as the Starport's dialogs have them)
			new Entry( c_saveContent + "Overlay Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundApp, 0.85f ),
			new Entry( c_saveContent + "Switch Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),
			new Entry( c_saveContent + "Switch Panel/Xform/Label Text", UIText.LabelM, 28, UISprite.None, UIColor.TextAccent ),
			new Entry( c_saveContent + "Copy Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),
			new Entry( c_saveContent + "Copy Panel/Xform/Label Text", UIText.LabelM, 28, UISprite.None, UIColor.TextAccent ),
			new Entry( c_saveContent + "Reset Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundPanel ),
			new Entry( c_saveContent + "Reset Panel/Xform/Label Text", UIText.LabelM, 28, UISprite.None, UIColor.TextAlert ),
		};

		// the five slots: a raised card, its name as a heading, "Active" in cyan, the description in a darker well
		foreach ( var slot in new[] { "A", "B", "C", "D", "E" } )
		{
			var path = c_saveContent + "Xform/Slot " + slot;

			entries.Add( new Entry( path, UIText.None, 0, UISprite.Panel, UIColor.BackgroundRaised ) );
			entries.Add( new Entry( path + "/Xform/A/Xform/ID", UIText.HeadingL, 30, UISprite.None, UIColor.TextPrimary ) );
			entries.Add( new Entry( path + "/Xform/A/Xform/Active", UIText.LabelM, 24, UISprite.None, UIColor.TextAccent ) );
			entries.Add( new Entry( path + "/Xform/B/Panel", UIText.None, 0, UISprite.Panel, UIColor.BackgroundApp, 0.6f ) );
			entries.Add( new Entry( path + "/Xform/B/Panel/Description", UIText.BodyM, c_saveDescriptionSize, UISprite.None, UIColor.TextSecondary ) );
		}

		return entries.ToArray();
	}

	// the panels of the save game panel that get an outline: path, colour (the reset dialog was red: its outline is the alert colour)
	static readonly (string path, UIColor color)[] c_savePanelOutlines =
	{
		( "Save Game Panel/Root", UIColor.BorderDefault ),
		( c_saveContent + "Xform/Slot A", UIColor.BorderSubtle ),
		( c_saveContent + "Xform/Slot B", UIColor.BorderSubtle ),
		( c_saveContent + "Xform/Slot C", UIColor.BorderSubtle ),
		( c_saveContent + "Xform/Slot D", UIColor.BorderSubtle ),
		( c_saveContent + "Xform/Slot E", UIColor.BorderSubtle ),
		( c_saveContent + "Switch Panel", UIColor.BorderAccent ),
		( c_saveContent + "Copy Panel", UIColor.BorderAccent ),
		( c_saveContent + "Reset Panel", UIColor.BorderAlert ),
	};

	[MenuItem( "Starflight Remake/UI Kit/Apply to the Save Game Panel" )]
	public static void ApplySavePanelFromMenu()
	{
		if ( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
		{
			return;
		}

		EditorSceneManager.OpenScene( c_persistentScenePath, OpenSceneMode.Single );

		Debug.Log( ApplyToSavePanel() );
	}

	// for batch mode: open the Persistent scene, apply the kit to the save game panel, save the scene
	public static void ApplySavePanelAndSave()
	{
		EditorSceneManager.OpenScene( c_persistentScenePath, OpenSceneMode.Single );

		Debug.Log( ApplyToSavePanel() );

		EditorSceneManager.SaveScene( EditorSceneManager.GetActiveScene() );
	}

	// gives the save game panel (in the Persistent scene, shown over the Starport and the Spaceflight scenes) the kit's look and returns a report
	public static string ApplyToSavePanel()
	{
		var report = new System.Text.StringBuilder( "UI kit applied to the save game panel\n" );
		var theme = UIKitTheme.Get();
		var canvas = FindCanvas();
		var panel = ( canvas != null ) ? canvas.Find( "Save Game Panel" ) : null;

		if ( ( theme == null ) || ( panel == null ) )
		{
			return "missing " + ( ( theme == null ) ? "the UI kit theme" : "the save game panel" );
		}

		// the buttons keep their heights: those in a row are sized by their layout from their sprite, and the kit's panel sprite is smaller than theirs
		foreach ( var button in panel.GetComponentsInChildren<Button>( true ) )
		{
			var rect = (RectTransform) button.transform;

			if ( ( button.transform.parent != null ) && ( button.transform.parent.GetComponent<LayoutGroup>() != null ) && ( button.GetComponent<UIKitStyle>() == null ) && ( rect.rect.height > 0.0f ) )
			{
				var element = button.GetComponent<LayoutElement>();

				if ( element == null )
				{
					element = Undo.AddComponent<LayoutElement>( button.gameObject );
				}

				Undo.RecordObject( element, "UI kit" );
				element.minHeight = rect.rect.height;
				EditorUtility.SetDirty( element );
			}
		}

		report.AppendLine( ApplyEntries( canvas, GetSavePanelTable(), report ) + " elements styled" );

		// the descriptions size themselves to fit, up to the kit's size
		foreach ( var slot in new[] { "A", "B", "C", "D", "E" } )
		{
			var description = canvas.Find( c_saveContent + "Xform/Slot " + slot + "/Xform/B/Panel/Description" );

			if ( ( description != null ) && description.TryGetComponent<TMPro.TMP_Text>( out var text ) )
			{
				Undo.RecordObject( text, "UI kit" );
				text.fontSizeMax = c_saveDescriptionSize;
				EditorUtility.SetDirty( text );
			}
		}

		foreach ( var (path, color) in c_savePanelOutlines )
		{
			var target = canvas.Find( path );

			if ( target == null )
			{
				report.AppendLine( "not found: " + path );
				continue;
			}

			AddOutline( target, color );
		}

		// the buttons, as in the Starport: the kit's panel in white, their colours from their tints, their labels in the kit's label style
		var buttons = 0;

		foreach ( var button in panel.GetComponentsInChildren<Button>( true ) )
		{
			if ( ( button.targetGraphic is Image image ) && ( image.GetComponent<UIKitStyle>() == null ) )
			{
				Style( image, UIText.None, 0, UISprite.Panel, UIColor.Keep );
				Undo.RecordObject( image, "UI kit" );
				image.color = Color.white;
				EditorUtility.SetDirty( image );
			}

			TintButton( button, theme );

			var label = button.GetComponentInChildren<TMPro.TMP_Text>( true );

			// the label keeps its size in proportion (the menu's 48 and the dialogs' 40 become 29 and 24); a label that has its style already keeps it
			if ( ( label != null ) && ( label.GetComponent<UIKitStyle>() == null ) )
			{
				Style( label, UIText.LabelM, Mathf.Round( label.fontSize * 0.6f ), UISprite.None, UIColor.TextPrimary );
			}

			buttons++;
		}

		report.AppendLine( buttons + " buttons styled" );

		EditorSceneManager.MarkSceneDirty( canvas.gameObject.scene );

		return report.ToString();
	}

	// gives a button the kit's tints: navy, dim cyan when the cursor is on it or it is selected (returns false for a button that does not tint)
	static bool TintButton( Button button, UIKitTheme theme )
	{
		if ( button.transition != Selectable.Transition.ColorTint )
		{
			return false;
		}

		Undo.RecordObject( button, "UI kit" );

		var colours = button.colors;

		colours.normalColor = theme.m_backgroundRaised;
		colours.highlightedColor = theme.m_accentPrimaryDim;
		colours.selectedColor = theme.m_accentPrimaryDim;
		colours.pressedColor = theme.m_accentPrimaryStrong;
		colours.disabledColor = new Color( theme.m_backgroundHover.r, theme.m_backgroundHover.g, theme.m_backgroundHover.b, 0.5f );
		colours.colorMultiplier = 1.0f;

		button.colors = colours;

		EditorUtility.SetDirty( button );

		return true;
	}

	// puts a panel of this colour behind everything else in a container: a first child that covers it and takes no part in a layout or in clicks
	static void AddBackdrop( Transform parent, UIColor color )
	{
		var go = new GameObject( "UI Kit Backdrop", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( LayoutElement ) );

		Undo.RegisterCreatedObjectUndo( go, "UI kit" );

		var rect = (RectTransform) go.transform;

		rect.SetParent( parent, false );
		rect.SetAsFirstSibling();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		go.GetComponent<LayoutElement>().ignoreLayout = true;
		go.GetComponent<Image>().raycastTarget = false;

		Style( go.GetComponent<Image>(), UIText.None, 0, UISprite.Panel, color );
	}

	// gives an element a UIKitStyle with these settings and applies it
	static void Style( Component target, UIText text, float size, UISprite sprite, UIColor color, float alpha = 1.0f, float lineHeight = 0.0f )
	{
		var style = Undo.AddComponent<UIKitStyle>( target.gameObject );

		style.m_textStyle = text;
		style.m_fontSize = size;
		style.m_sprite = sprite;
		style.m_color = color;
		style.m_alpha = alpha;
		style.m_lineHeight = lineHeight;

		Apply( style );
	}

	// the height of a line of this text as it is now (its font's line height at its size, plus its line spacing in hundredths of an em)
	static float NaturalLineHeight( TMPro.TMP_Text text )
	{
		if ( ( text.font == null ) || ( text.font.faceInfo.pointSize <= 0 ) )
		{
			return 0.0f;
		}

		return text.font.faceInfo.lineHeight / text.font.faceInfo.pointSize * text.fontSize + text.lineSpacing * 0.01f * text.fontSize;
	}

	// the path of a transform, for the report
	static string PathOf( Transform t )
	{
		return ( t.parent == null ) ? t.name : PathOf( t.parent ) + "/" + t.name;
	}

	// applies a style and marks what it changed, so the scene saves it
	static void Apply( UIKitStyle style )
	{
		foreach ( var component in style.GetComponents<Component>() )
		{
			if ( component is Graphic )
			{
				Undo.RecordObject( component, "UI kit" );
			}
		}

		style.Apply();

		foreach ( var component in style.GetComponents<Component>() )
		{
			if ( ( component is Graphic ) || ( component is UIKitStyle ) )
			{
				EditorUtility.SetDirty( component );
			}
		}
	}

	// the transform of the UI canvas
	static Transform FindCanvas()
	{
		foreach ( var canvas in Object.FindObjectsByType<Canvas>( FindObjectsInactive.Include, FindObjectsSortMode.None ) )
		{
			if ( ( canvas.name == "UI" ) && ( canvas.transform.parent == null ) )
			{
				return canvas.transform;
			}
		}

		return null;
	}

	// a direct child by name
	static Transform FindChild( Transform parent, string name )
	{
		foreach ( Transform child in parent )
		{
			if ( child.name == name )
			{
				return child;
			}
		}

		return null;
	}
}
