
using UnityEngine;
using TMPro;

// the modern sci-fi UI kit of the remake (the Figma file "Starflight Remake - UI Kit"): its colour roles, text styles and sprites
// (the asset is Resources/UI Kit Theme, made and filled by Starflight Remake > UI Kit > Rebuild in the editor)
public class UIKitTheme : ScriptableObject
{
	// the name of the asset in Resources
	public const string c_resourceName = "UI Kit Theme";

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
	public Color m_accentSecondary = Hex( 0xFF4FD8 );

	[Header( "Status" )]
	public Color m_statusShield = Hex( 0x3DE6FF );
	public Color m_statusArmor = Hex( 0xFFB84D );
	public Color m_statusFuel = Hex( 0x4DFFA6 );
	public Color m_statusDanger = Hex( 0xFF5A6A );

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

	// the loaded theme
	static UIKitTheme s_instance;

	// returns the theme (loaded from Resources the first time, null if the asset is missing)
	public static UIKitTheme Get()
	{
		if ( s_instance == null )
		{
			s_instance = Resources.Load<UIKitTheme>( c_resourceName );

			if ( s_instance == null )
			{
				Debug.LogWarning( "The UI kit theme (Resources/" + c_resourceName + ") is missing" );
			}
		}

		return s_instance;
	}

	// a colour from its 0xRRGGBB value (the kit's colours are written as in Figma, in sRGB)
	public static Color Hex( int rgb )
	{
		return new Color32( (byte) ( ( rgb >> 16 ) & 0xFF ), (byte) ( ( rgb >> 8 ) & 0xFF ), (byte) ( rgb & 0xFF ), 255 );
	}
}
