
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

			// the ship's name and the officer's logo
			new Entry( c_right + "C/GameObject/A/GameObject/A/GameObject/Image", UIText.None, 0, UISprite.None, UIColor.TextMuted ),
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

			// the launch countdown keeps its colour
			new Entry( c_viewport + "Countdown", UIText.DisplayXL, 240, UISprite.None, UIColor.Keep ),

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
			table.Add( new Entry( c_buttons + "B/GameObject/" + i, UIText.LabelM, 26, UISprite.None, UIColor.TextPrimary ) );
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

		var styled = 0;

		foreach ( var entry in GetTable() )
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

			var outline = panel.Find( "UI Kit Outline" );

			if ( outline == null )
			{
				var go = new GameObject( "UI Kit Outline", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( LayoutElement ) );

				Undo.RegisterCreatedObjectUndo( go, "UI kit" );

				outline = go.transform;
				outline.SetParent( panel, false );
			}

			// it covers its panel, is drawn last and takes no part in a layout or in clicks
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

		EditorSceneManager.MarkSceneDirty( canvas.gameObject.scene );

		return report.ToString();
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
