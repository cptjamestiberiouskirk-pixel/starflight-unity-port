
using UnityEngine;
using TMPro;

// the modern sci-fi UI kit of the remake (the Figma file "Starflight Remake - UI Kit"): its colour roles, text styles and sprites
// (the asset is Resources/UI Kit Theme, made and filled by Starflight Remake > UI Kit > Rebuild in the editor)
public class UIKitTheme : ScriptableObject
{
	// the name of the asset in Resources
	public const string c_resourceName = "UI Kit Theme";

	// the name of the light mode's asset in Resources: the kit in steel grey (the owner's request of 2026-10-13)
	public const string c_lightResourceName = "UI Kit Theme Light";

	// the kit's two looks: the dark one it was drawn with, and a light one in steel grey, which the player picks in the save panel
	public enum Mode
	{
		Dark, Light
	}

	// where the player's choice is kept: a setting of this computer, not of a saved game
	public const string c_modeKey = "UI Kit Mode";

	// raised when the mode changes, so that everything on screen takes the new look
	public static event System.Action Changed;

	// a text style of the kit: a font, its size, the space between the letters (in hundredths of an em, as TextMesh Pro counts it) and whether it is all capitals
	[System.Serializable]
	public class TextStyle
	{
		public TMP_FontAsset m_font;
		public float m_size;
		public float m_characterSpacing;
		public bool m_uppercase;

		// gives this text the style
		public void Apply( TMP_Text text )
		{
			if ( text == null )
			{
				return;
			}

			if ( m_font != null )
			{
				text.font = m_font;
			}

			text.fontSize = m_size;
			text.characterSpacing = m_characterSpacing;
			text.fontStyle = m_uppercase ? ( text.fontStyle | FontStyles.UpperCase ) : ( text.fontStyle & ~FontStyles.UpperCase );
		}
	}

	[Header( "Backgrounds" )]
	public Color m_backgroundApp = Hex( 0x05070D );
	public Color m_backgroundPanel = Hex( 0x0A0F1C );
	public Color m_backgroundRaised = Hex( 0x111A2E );
	public Color m_backgroundHover = Hex( 0x1A2742 );

	[Header( "Borders" )]
	public Color m_borderSubtle = Hex( 0x1A2742 );
	public Color m_borderDefault = Hex( 0x24365A );
	public Color m_borderAccent = Hex( 0x3DE6FF );
	public Color m_borderAlert = Hex( 0xFF5A6A );

	[Header( "Text" )]
	public Color m_textPrimary = Hex( 0xE6F0FF );
	public Color m_textSecondary = Hex( 0x9FB3D9 );
	public Color m_textMuted = Hex( 0x4A6290 );
	public Color m_textAccent = Hex( 0x7FF3FF );
	public Color m_textOnAccent = Hex( 0x05070D );
	public Color m_textAlert = Hex( 0xFF5A6A );
	public Color m_textWarning = Hex( 0xFFB84D );

	[Header( "Accents" )]
	public Color m_accentPrimary = Hex( 0x3DE6FF );
	public Color m_accentPrimaryStrong = Hex( 0x00C8F0 );
	public Color m_accentPrimaryDim = Hex( 0x007A99 );
	public Color m_accentSecondary = Hex( 0xFFB84D );

	[Header( "Status" )]
	public Color m_statusShield = Hex( 0x3DE6FF );
	public Color m_statusArmor = Hex( 0xFFB84D );
	public Color m_statusFuel = Hex( 0x4DFFA6 );
	public Color m_statusDanger = Hex( 0xFF5A6A );

	[Header( "Sensor window" )]
	// the window the sensor pictures are drawn on: steel blue, light enough that a vessel's black silhouette stands out on it (the original's was magenta; the owner chose steel blue on 2026-10-11)
	public Color m_sensorWindow = Hex( 0x2E4A73 );

	[Header( "Text styles" )]
	public TextStyle m_displayXL = new TextStyle { m_size = 48.0f, m_characterSpacing = 4.0f };
	public TextStyle m_headingL = new TextStyle { m_size = 24.0f, m_characterSpacing = 6.0f, m_uppercase = true };
	public TextStyle m_headingM = new TextStyle { m_size = 15.0f, m_characterSpacing = 12.0f, m_uppercase = true };
	public TextStyle m_labelM = new TextStyle { m_size = 17.0f, m_characterSpacing = 8.0f, m_uppercase = true };
	public TextStyle m_labelS = new TextStyle { m_size = 13.0f, m_characterSpacing = 10.0f, m_uppercase = true };
	public TextStyle m_bodyM = new TextStyle { m_size = 18.0f, m_characterSpacing = 1.0f };
	public TextStyle m_readoutL = new TextStyle { m_size = 30.0f, m_characterSpacing = 2.0f };
	public TextStyle m_readoutM = new TextStyle { m_size = 18.0f, m_characterSpacing = 2.0f };

	[Header( "Sprites (white, tinted by the image colour)" )]
	public Sprite m_panelSprite;
	public Sprite m_panelOutlineSprite;
	public Sprite m_glowSprite;
	public Sprite m_barSprite;
	public Sprite m_diamondSprite;
	public Sprite m_diamondOutlineSprite;

	[Header( "Console button bullets (coloured, made from the colours above by the rebuild)" )]
	public Sprite m_bulletOffSprite;
	public Sprite m_bulletOnSprite;
	public Sprite m_bulletActiveSprite;

	[Header( "Officer icons (white line art, tinted by the image colour)" )]
	public Sprite m_officerCaptainSprite;
	public Sprite m_officerScienceSprite;
	public Sprite m_officerNavigatorSprite;
	public Sprite m_officerEngineerSprite;
	public Sprite m_officerCommunicationsSprite;
	public Sprite m_officerDoctorSprite;
	public Sprite m_officerTerrainVehicleSprite;

	// who has the console: the ship itself (on the bridge), one of the six officers, or the terrain vehicle
	public enum OfficerIcon
	{
		Ship, Captain, Science, Navigator, Engineer, Communications, Doctor, TerrainVehicle
	}

	// returns the icon of whoever has the console (null for the ship, which keeps the logo it has in the scene)
	public Sprite GetOfficerIcon( OfficerIcon officer )
	{
		switch ( officer )
		{
			case OfficerIcon.Captain: return m_officerCaptainSprite;
			case OfficerIcon.Science: return m_officerScienceSprite;
			case OfficerIcon.Navigator: return m_officerNavigatorSprite;
			case OfficerIcon.Engineer: return m_officerEngineerSprite;
			case OfficerIcon.Communications: return m_officerCommunicationsSprite;
			case OfficerIcon.Doctor: return m_officerDoctorSprite;
			case OfficerIcon.TerrainVehicle: return m_officerTerrainVehicleSprite;
			default: return null;
		}
	}

	// the kit's colour roles, by name (Keep leaves a colour as it is)
	public enum ColorRole
	{
		Keep,
		BackgroundApp, BackgroundPanel, BackgroundRaised, BackgroundHover,
		BorderSubtle, BorderDefault, BorderAccent, BorderAlert,
		TextPrimary, TextSecondary, TextMuted, TextAccent, TextOnAccent, TextAlert, TextWarning,
		AccentPrimary, AccentPrimaryStrong, AccentPrimaryDim, AccentSecondary,
		StatusShield, StatusArmor, StatusFuel, StatusDanger,
		SensorWindow
	}

	// the kit's text styles, by name
	public enum TextStyleRole
	{
		None, DisplayXL, HeadingL, HeadingM, LabelM, LabelS, BodyM, ReadoutL, ReadoutM
	}

	// the kit's sprites, by name
	public enum SpriteRole
	{
		None, Panel, PanelOutline, Glow, Bar, Diamond, DiamondOutline
	}

	// returns the colour of a role (white for Keep, which callers do not apply)
	public Color GetColor( ColorRole role )
	{
		switch ( role )
		{
			case ColorRole.BackgroundApp: return m_backgroundApp;
			case ColorRole.BackgroundPanel: return m_backgroundPanel;
			case ColorRole.BackgroundRaised: return m_backgroundRaised;
			case ColorRole.BackgroundHover: return m_backgroundHover;
			case ColorRole.BorderSubtle: return m_borderSubtle;
			case ColorRole.BorderDefault: return m_borderDefault;
			case ColorRole.BorderAccent: return m_borderAccent;
			case ColorRole.BorderAlert: return m_borderAlert;
			case ColorRole.TextPrimary: return m_textPrimary;
			case ColorRole.TextSecondary: return m_textSecondary;
			case ColorRole.TextMuted: return m_textMuted;
			case ColorRole.TextAccent: return m_textAccent;
			case ColorRole.TextOnAccent: return m_textOnAccent;
			case ColorRole.TextAlert: return m_textAlert;
			case ColorRole.TextWarning: return m_textWarning;
			case ColorRole.AccentPrimary: return m_accentPrimary;
			case ColorRole.AccentPrimaryStrong: return m_accentPrimaryStrong;
			case ColorRole.AccentPrimaryDim: return m_accentPrimaryDim;
			case ColorRole.AccentSecondary: return m_accentSecondary;
			case ColorRole.StatusShield: return m_statusShield;
			case ColorRole.StatusArmor: return m_statusArmor;
			case ColorRole.StatusFuel: return m_statusFuel;
			case ColorRole.StatusDanger: return m_statusDanger;
			case ColorRole.SensorWindow: return m_sensorWindow;
			default: return Color.white;
		}
	}

	// returns the text style of a role (null for None)
	public TextStyle GetTextStyle( TextStyleRole role )
	{
		switch ( role )
		{
			case TextStyleRole.DisplayXL: return m_displayXL;
			case TextStyleRole.HeadingL: return m_headingL;
			case TextStyleRole.HeadingM: return m_headingM;
			case TextStyleRole.LabelM: return m_labelM;
			case TextStyleRole.LabelS: return m_labelS;
			case TextStyleRole.BodyM: return m_bodyM;
			case TextStyleRole.ReadoutL: return m_readoutL;
			case TextStyleRole.ReadoutM: return m_readoutM;
			default: return null;
		}
	}

	// returns the sprite of a role (null for None)
	public Sprite GetSprite( SpriteRole role )
	{
		switch ( role )
		{
			case SpriteRole.Panel: return m_panelSprite;
			case SpriteRole.PanelOutline: return m_panelOutlineSprite;
			case SpriteRole.Glow: return m_glowSprite;
			case SpriteRole.Bar: return m_barSprite;
			case SpriteRole.Diamond: return m_diamondSprite;
			case SpriteRole.DiamondOutline: return m_diamondOutlineSprite;
			default: return null;
		}
	}

	// the loaded themes, one per mode
	static UIKitTheme[] s_themes = new UIKitTheme[ 2 ];

	// the mode in use, and whether it has been read from the player's setting yet
	static Mode s_mode;
	static bool s_modeKnown;

	// returns the theme of the mode in use (loaded from Resources the first time, null if the asset is missing)
	public static UIKitTheme Get()
	{
		var theme = Load( GetMode() );

		// without the light mode's asset the kit stays dark
		if ( ( theme == null ) && ( GetMode() != Mode.Dark ) )
		{
			theme = Load( Mode.Dark );
		}

		return theme;
	}

	// returns the theme of a mode (loaded from Resources the first time, null if the asset is missing)
	public static UIKitTheme Load( Mode mode )
	{
		var index = ( mode == Mode.Light ) ? 1 : 0;

		if ( s_themes[ index ] == null )
		{
			var name = ( mode == Mode.Light ) ? c_lightResourceName : c_resourceName;

			s_themes[ index ] = Resources.Load<UIKitTheme>( name );

			if ( s_themes[ index ] == null )
			{
				Debug.LogWarning( "The UI kit theme (Resources/" + name + ") is missing" );
			}
		}

		return s_themes[ index ];
	}

	// returns the mode in use (the player's setting, dark if there is none or it cannot be read)
	public static Mode GetMode()
	{
		if ( !s_modeKnown )
		{
			s_mode = Mode.Dark;

			try
			{
				s_mode = ( PlayerPrefs.GetInt( c_modeKey, (int) Mode.Dark ) == (int) Mode.Light ) ? Mode.Light : Mode.Dark;
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The UI kit's mode could not be read: " + exception.Message );
			}

			s_modeKnown = true;
		}

		return s_mode;
	}

	// changes the mode and lets everything on screen know (remember false leaves the player's setting as it is, for a test)
	public static void SetMode( Mode mode, bool remember = true )
	{
		var previousMode = GetMode();

		if ( remember )
		{
			try
			{
				PlayerPrefs.SetInt( c_modeKey, (int) mode );
				PlayerPrefs.Save();
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The UI kit's mode could not be saved: " + exception.Message );
			}
		}

		if ( mode == previousMode )
		{
			return;
		}

		s_mode = mode;

		// the message colours are made from the theme, so they are made again
		s_messageColourTags = null;

		Changed?.Invoke();
	}

	// returns the role whose colour this is in a theme (Keep if none): used to give colours copied into the scenes the other mode's
	public static ColorRole FindRole( UIKitTheme theme, Color color )
	{
		if ( theme == null )
		{
			return ColorRole.Keep;
		}

		// the roles in the order they are looked for: a colour shared by two roles is taken for the first (the backgrounds come before the borders that share them)
		for ( var role = ColorRole.BackgroundApp; role <= ColorRole.SensorWindow; role++ )
		{
			var roleColor = theme.GetColor( role );

			if ( ( Mathf.Abs( roleColor.r - color.r ) < 0.004f ) && ( Mathf.Abs( roleColor.g - color.g ) < 0.004f ) && ( Mathf.Abs( roleColor.b - color.b ) < 0.004f ) )
			{
				return role;
			}
		}

		return ColorRole.Keep;
	}

	// the colour tags the game's messages are written with, and the kit's colour role each of them shows as (each keeps its meaning: green stays green)
	static readonly (string tag, ColorRole role)[] c_messageColours =
	{
		( "white", ColorRole.TextPrimary ),
		( "green", ColorRole.StatusFuel ),
		( "#0a0", ColorRole.StatusFuel ),
		( "#00aa00", ColorRole.StatusFuel ),
		( "#0aa", ColorRole.TextAccent ),
		( "cyan", ColorRole.TextAccent ),
		( "lightblue", ColorRole.TextAccent ),
		( "blue", ColorRole.AccentPrimary ),
		( "red", ColorRole.TextAlert ),
		( "yellow", ColorRole.TextWarning ),
		( "#ffff00", ColorRole.TextWarning ),
		( "orange", ColorRole.TextWarning ),
		( "grey", ColorRole.TextSecondary ),
		( "#00ffff", ColorRole.TextAccent ),
		( "#0ff", ColorRole.TextAccent ),
		( "#00ff00", ColorRole.StatusFuel ),
		( "#808080", ColorRole.TextSecondary ),
		( "#a35514", ColorRole.AccentSecondary ),
		( "#ff0000", ColorRole.TextAlert ),
		( "#ffa500", ColorRole.TextWarning ),
		( "#e6f0ff", ColorRole.TextPrimary ),
	};

	// the tags as they are written (<color=green> and <color="green">) and what they become, made the first time
	static (string from, string to)[] s_messageColourTags;

	// returns the text with the colours of its colour tags turned into the kit's (the message box shows its messages through this, so the game's code keeps its tags)
	public static string KitColors( string text )
	{
		var theme = Get();

		if ( string.IsNullOrEmpty( text ) || ( theme == null ) || ( text.IndexOf( "<color=", System.StringComparison.OrdinalIgnoreCase ) < 0 ) )
		{
			return text;
		}

		if ( s_messageColourTags == null )
		{
			var tags = new System.Collections.Generic.List<(string, string)>();

			foreach ( var ( tag, role ) in c_messageColours )
			{
				var to = "<color=#" + ColorUtility.ToHtmlStringRGB( theme.GetColor( role ) ) + ">";

				tags.Add( ( "<color=" + tag + ">", to ) );
				tags.Add( ( "<color=\"" + tag + "\">", to ) );
			}

			s_messageColourTags = tags.ToArray();
		}

		foreach ( var ( from, to ) in s_messageColourTags )
		{
			text = ReplaceIgnoringCase( text, from, to );
		}

		return text;
	}

	// replaces every occurrence of a string, whatever its case
	static string ReplaceIgnoringCase( string text, string from, string to )
	{
		var index = text.IndexOf( from, System.StringComparison.OrdinalIgnoreCase );

		if ( index < 0 )
		{
			return text;
		}

		var result = new System.Text.StringBuilder( text.Length + 16 );
		var start = 0;

		for ( var i = 0; ( index >= 0 ) && ( i < 1000 ); i++ )
		{
			result.Append( text, start, index - start ).Append( to );
			start = index + from.Length;
			index = text.IndexOf( from, start, System.StringComparison.OrdinalIgnoreCase );
		}

		result.Append( text, start, text.Length - start );

		return result.ToString();
	}

	// a colour from its 0xRRGGBB value (the kit's colours are written as in Figma, in sRGB)
	public static Color Hex( int rgb )
	{
		return new Color32( (byte) ( ( rgb >> 16 ) & 0xFF ), (byte) ( ( rgb >> 8 ) & 0xFF ), (byte) ( rgb & 0xFF ), 255 );
	}
}
