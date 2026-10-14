
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

	// the two modes every colour scheme has (the classic scheme's are the dark look the kit was drawn with and a light one in steel grey); the player
	// picks the scheme and the mode in the save panel's Colors screen
	public enum Mode
	{
		Dark, Light
	}

	// where the player's choice is kept: a setting of this computer, not of a saved game
	public const string c_modeKey = "UI Kit Mode";

	// a colour scheme of the kit: its name as the player sees it, a line about its look, and the names of its dark and light themes in Resources
	public class Scheme
	{
		public readonly string m_name;
		public readonly string m_description;
		public readonly string m_darkResourceName;
		public readonly string m_lightResourceName;

		public Scheme( string name, string description, string darkResourceName, string lightResourceName )
		{
			m_name = name;
			m_description = description;
			m_darkResourceName = darkResourceName;
			m_lightResourceName = lightResourceName;
		}

		// a scheme whose two themes are in the schemes' folder, named after it (made by DevTools/UIKitSchemes/make_themes.py)
		public Scheme( string name, string description ) : this( name, description, c_schemeFolder + name + " Dark", c_schemeFolder + name + " Light" )
		{
		}
	}

	// the folder in Resources that holds the themes of every scheme but the classic one
	public const string c_schemeFolder = "UI Kit Themes/";

	// the kit's colour schemes, in the order the save panel's Colors screen shows them: the kit as it was drawn (the default), the two original
	// games' own colours, and the twelve palettes the owner kept on 2026-10-14 (the player picks one at any time; each has a dark and a light mode)
	public static readonly Scheme[] c_schemes =
	{
		new Scheme( "Classic", "The kit as it was drawn: ice cyan on navy, and steel gray", c_resourceName, c_lightResourceName ),
		new Scheme( "Starflight", "The 1986 original's EGA colors: cyan and white on black, in blue frames" ),
		new Scheme( "Starflight 2", "The 1989 sequel's EGA colors: white on black in blue frames, on a light gray screen" ),
		new Scheme( "Endurium", "Mint-teal crystal glow over an abyssal teal hull" ),
		new Scheme( "Interstel Amber", "Holographic orange on cool gunmetal, cyan shields" ),
		new Scheme( "Phosphor", "Retro-futurist green CRT with amber warnings" ),
		new Scheme( "Titanium", "Monochrome platinum with a single ice-blue accent" ),
		new Scheme( "Heritage EGA", "The original's EGA colors, rebuilt in modern glass" ),
		new Scheme( "Solar Gold", "Gold and brass on warm obsidian, steel-blue shields" ),
		new Scheme( "Spec-Ops", "Tactical lime on olive gunmetal, orange call-outs" ),
		new Scheme( "Azure Drive", "Electric azure on blue graphite, peach highlights" ),
		new Scheme( "Arctic Glass", "Ice cyan re-cut on neutral slate" ),
		new Scheme( "Starliner", "Peach, apricot and sky-blue blocks on pure black" ),
		new Scheme( "Night Ops", "Red-running bridge lighting that keeps night vision" ),
		new Scheme( "Hi-Vis", "Signal yellow and white on true black: maximum legibility" ),
	};

	// where the player's choice of scheme is kept, by its name (a setting of this computer, as the mode is)
	public const string c_schemeKey = "UI Kit Scheme";

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

	[Header( "Buttons (white, tinted: the sci-fi button's fill with two corners cut off, and its frame)" )]
	public Sprite m_buttonSprite;
	public Sprite m_buttonFrameSprite;

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
		None, Panel, PanelOutline, Glow, Bar, Diamond, DiamondOutline, Button, ButtonFrame
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
			case SpriteRole.Button: return m_buttonSprite;
			case SpriteRole.ButtonFrame: return m_buttonFrameSprite;
			default: return null;
		}
	}

	// the loaded themes, by their names in Resources (a missing one is kept as null, so that it is looked for once)
	static readonly System.Collections.Generic.Dictionary<string, UIKitTheme> s_themes = new System.Collections.Generic.Dictionary<string, UIKitTheme>();

	// the mode in use, and whether it has been read from the player's setting yet
	static Mode s_mode;
	static bool s_modeKnown;

	// the scheme in use (an index into c_schemes), and whether it has been read from the player's setting yet
	static int s_scheme;
	static bool s_schemeKnown;

	// returns the theme of the scheme and the mode in use (loaded from Resources the first time, null if the asset is missing)
	public static UIKitTheme Get()
	{
		var theme = LoadScheme( GetScheme(), GetMode() );

		// without the scheme's theme the classic one of the mode stands in, and without the light mode's asset the kit stays dark
		if ( theme == null )
		{
			theme = Load( GetMode() );
		}

		if ( ( theme == null ) && ( GetMode() != Mode.Dark ) )
		{
			theme = Load( Mode.Dark );
		}

		return theme;
	}

	// returns the classic theme of a mode (the dark one has the colours the scenes are saved in)
	public static UIKitTheme Load( Mode mode )
	{
		return LoadScheme( 0, mode );
	}

	// returns the theme of a scheme in a mode (loaded from Resources the first time, null if the asset is missing)
	public static UIKitTheme LoadScheme( int scheme, Mode mode )
	{
		if ( ( scheme < 0 ) || ( scheme >= c_schemes.Length ) )
		{
			scheme = 0;
		}

		var name = ( mode == Mode.Light ) ? c_schemes[ scheme ].m_lightResourceName : c_schemes[ scheme ].m_darkResourceName;

		if ( !s_themes.TryGetValue( name, out var theme ) )
		{
			theme = Resources.Load<UIKitTheme>( name );

			if ( theme == null )
			{
				Debug.LogWarning( "The UI kit theme (Resources/" + name + ") is missing" );
			}

			s_themes[ name ] = theme;
		}

		return theme;
	}

	// returns the scheme in use (the player's setting, the classic one if there is none, it cannot be read or it names no scheme)
	public static int GetScheme()
	{
		if ( !s_schemeKnown )
		{
			s_scheme = 0;

			try
			{
				var name = PlayerPrefs.GetString( c_schemeKey, c_schemes[ 0 ].m_name );

				for ( var i = 0; i < c_schemes.Length; i++ )
				{
					if ( c_schemes[ i ].m_name == name )
					{
						s_scheme = i;
					}
				}
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The UI kit's scheme could not be read: " + exception.Message );
			}

			s_schemeKnown = true;
		}

		return s_scheme;
	}

	// changes the scheme and lets everything on screen know (remember false leaves the player's setting as it is, for a test)
	public static void SetScheme( int scheme, bool remember = true )
	{
		if ( ( scheme < 0 ) || ( scheme >= c_schemes.Length ) )
		{
			return;
		}

		var previousScheme = GetScheme();

		if ( remember )
		{
			try
			{
				PlayerPrefs.SetString( c_schemeKey, c_schemes[ scheme ].m_name );
				PlayerPrefs.Save();
			}
			catch ( System.Exception exception )
			{
				Debug.LogWarning( "The UI kit's scheme could not be saved: " + exception.Message );
			}
		}

		if ( scheme == previousScheme )
		{
			return;
		}

		s_scheme = scheme;

		ThemeChanged();
	}

	// whether the theme in use shows the colour tags written into texts as they are written: the classic dark one does, as the kit always has (every
	// other theme shows them in its own colours, through KitColors)
	public static bool ShowsTagsAsWritten()
	{
		return Get() == Load( Mode.Dark );
	}

	// returns the theme of what is drawn over the 3D views (the main viewport: its frame, labels and target bracket, the starmap and the ship's log, and
	// the labels on the terrain): the dark theme of the scheme in use, in either mode, since space and the planets' surfaces are dark
	public static UIKitTheme GetViewTheme()
	{
		var theme = LoadScheme( GetScheme(), Mode.Dark );

		return ( theme != null ) ? theme : Load( Mode.Dark );
	}

	// whether this is drawn over a 3D view: it is in the main viewport (under the object that has the Viewport)
	public static bool IsOverView( Component component )
	{
		return ( component != null ) && ( component.GetComponentInParent<Viewport>( true ) != null );
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

		ThemeChanged();
	}

	// the theme in use changed (its scheme or its mode): everything on screen is told
	static void ThemeChanged()
	{
		// the message colours are made from the theme, so they are made again
		s_messageColourTags = null;
		s_viewColourTags = null;

		Changed?.Invoke();
	}

	// the console's bullets: off, on (the selected button) and active (the running button)
	public enum Bullet
	{
		Off, On, Active
	}

	// the bullets made from this theme's colours the first time they are asked for (every theme but the classic dark one, whose bullets the kit's
	// rebuild drew into the sprites above)
	[System.NonSerialized]
	Sprite[] m_madeBullets;

	// returns the console's bullet of a state in this theme's colours
	public Sprite GetBullet( Bullet bullet )
	{
		var index = Mathf.Clamp( (int) bullet, 0, 2 );

		if ( this == Load( Mode.Dark ) )
		{
			return ( index == 0 ) ? m_bulletOffSprite : ( ( index == 1 ) ? m_bulletOnSprite : m_bulletActiveSprite );
		}

		// (made again after play mode in the editor has destroyed the ones made before)
		if ( ( m_madeBullets == null ) || ( m_madeBullets[ index ] == null ) )
		{
			var pixelsPerUnit = ( m_bulletOnSprite != null ) ? m_bulletOnSprite.pixelsPerUnit : 100.0f;

			m_madeBullets = new Sprite[]
			{
				MakeBullet( m_textMuted, 0.0f, pixelsPerUnit ),
				MakeBullet( m_accentPrimary, 1.0f, pixelsPerUnit ),
				MakeBullet( m_accentPrimary, 2.0f, pixelsPerUnit )
			};
		}

		return m_madeBullets[ index ];
	}

	// the width and height of a bullet's picture in pixels
	public const int c_bulletSize = 48;

	// draws a console bullet: a diamond 15 pixels from its centre to a corner (at 2x), in this colour (fill 0 is an outline 3 pixels wide, 1 is filled,
	// 2 is filled with a glow around it); the kit's rebuild (UIKitBuilder) and the bullets made at run time both draw them here
	public static Color32[] DrawBullet( Color color, float fill )
	{
		const float centre = c_bulletSize / 2.0f;
		const float radius = 15.0f;
		var pixels = new Color32[ c_bulletSize * c_bulletSize ];

		for ( var y = 0; y < c_bulletSize; y++ )
		{
			for ( var x = 0; x < c_bulletSize; x++ )
			{
				var coverage = 0.0f;
				var glow = 0.0f;

				// 4 x 4 samples per pixel
				for ( var sy = 0; sy < 4; sy++ )
				{
					for ( var sx = 0; sx < 4; sx++ )
					{
						var distance = Mathf.Abs( x + ( sx + 0.5f ) / 4.0f - centre ) + Mathf.Abs( y + ( sy + 0.5f ) / 4.0f - centre );
						var inside = ( fill > 0.0f ) ? ( distance <= radius ) : ( ( distance <= radius ) && ( distance > radius - 3.0f ) );

						coverage += inside ? 1.0f : 0.0f;

						if ( ( fill > 1.0f ) && ( distance > radius ) )
						{
							glow += Mathf.Pow( Mathf.Clamp01( 1.0f - ( distance - radius ) / 8.0f ), 2.0f ) * 0.55f;
						}
					}
				}

				coverage /= 16.0f;
				glow /= 16.0f;

				// the running bullet is brighter at the centre
				var c = ( fill > 1.0f ) ? Color.Lerp( color, Color.white, 0.35f ) : color;

				c.a = Mathf.Clamp01( coverage + glow * ( 1.0f - coverage ) );

				pixels[ y * c_bulletSize + x ] = c;
			}
		}

		return pixels;
	}

	// makes a console bullet's sprite at run time
	static Sprite MakeBullet( Color color, float fill, float pixelsPerUnit )
	{
		var texture = new Texture2D( c_bulletSize, c_bulletSize, TextureFormat.RGBA32, false );

		texture.wrapMode = TextureWrapMode.Clamp;
		texture.SetPixels32( DrawBullet( color, fill ) );
		texture.Apply();

		return Sprite.Create( texture, new Rect( 0, 0, c_bulletSize, c_bulletSize ), new Vector2( 0.5f, 0.5f ), pixelsPerUnit );
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
		// the terrain scan's count of lifeforms (it was pink), in the colour of the label the scan puts on a lifeform
		( "#ff80ff", ColorRole.TextAlert ),
	};

	// the tags as they are written (<color=green> and <color="green">) and what they become in the theme in use, made the first time
	static (string from, string to)[] s_messageColourTags;

	// the same for the theme of the 3D views, when it is another one
	static (string from, string to)[] s_viewColourTags;

	// returns the text with the colours of its colour tags turned into the kit's (the message box shows its messages through this, so the game's code keeps its tags)
	public static string KitColors( string text )
	{
		return KitColors( text, false );
	}

	// returns the text with its colour tags in the colours of the theme in use, or of the theme of the 3D views for a text drawn over one
	public static string KitColors( string text, bool overView )
	{
		var theme = overView ? GetViewTheme() : Get();

		if ( string.IsNullOrEmpty( text ) || ( theme == null ) || ( text.IndexOf( "<color=", System.StringComparison.OrdinalIgnoreCase ) < 0 ) )
		{
			return text;
		}

		var tags = overView ? s_viewColourTags : s_messageColourTags;

		if ( tags == null )
		{
			var list = new System.Collections.Generic.List<(string, string)>();

			foreach ( var ( tag, role ) in c_messageColours )
			{
				var to = "<color=#" + ColorUtility.ToHtmlStringRGB( theme.GetColor( role ) ) + ">";

				list.Add( ( "<color=" + tag + ">", to ) );
				list.Add( ( "<color=\"" + tag + "\">", to ) );
			}

			tags = list.ToArray();

			if ( overView )
			{
				s_viewColourTags = tags;
			}
			else
			{
				s_messageColourTags = tags;
			}
		}

		foreach ( var ( from, to ) in tags )
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
