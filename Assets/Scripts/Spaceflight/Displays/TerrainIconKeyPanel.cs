using UnityEngine;
using UnityEngine.UI;
using TMPro;

// the key to the surface icons on the auxiliary display while the terrain vehicle's Map is up, as the original's Icons button shows it (LISTICONS ICONS:
// roadmap 8.27): "Icon key", then the pictures with their names on two pages, icons 0 to 14 and 15 to 26, the second with the elevation bar of the planet's
// surface ("LO.....ELEVATION.....HI") - made at run time over the auxiliary display, in the UI kit's colours
public class TerrainIconKeyPanel : MonoBehaviour
{
	// the number of pages, and the most rows a page has
	public const int c_pageCount = 2;
	const int c_rows = TerrainIconKey.c_firstPageCount;

	// the share of the panel's height the title and the page line take, at the top and the bottom
	const float c_titleHeight = 0.09f;
	const float c_footerHeight = 0.07f;

	// the share of a row's height the text takes
	const float c_textPerRow = 0.7f;

	// the parts of the panel
	TextMeshProUGUI m_title;
	TextMeshProUGUI m_footer;
	readonly RawImage[] m_pictures = new RawImage[ c_rows ];
	readonly TextMeshProUGUI[] m_names = new TextMeshProUGUI[ c_rows ];
	Image m_elevationBar;
	TextMeshProUGUI m_elevationLabel;

	// the page shown
	int m_page;

	// makes the panel over this display
	public static TerrainIconKeyPanel Create( RectTransform display )
	{
		var panelObject = new GameObject( "Icon key", typeof( RectTransform ) );
		var rectTransform = (RectTransform) panelObject.transform;

		rectTransform.SetParent( display, false );
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;
		rectTransform.SetAsLastSibling();

		// an opaque panel over what the display shows
		var background = panelObject.AddComponent<Image>();

		background.raycastTarget = false;

		var backgroundStyle = panelObject.AddComponent<UIKitStyle>();

		backgroundStyle.m_sprite = UIKitTheme.SpriteRole.Panel;
		backgroundStyle.m_color = UIKitTheme.ColorRole.BackgroundPanel;

		// (its OnEnable ran before these were set)
		backgroundStyle.Apply();

		var panel = panelObject.AddComponent<TerrainIconKeyPanel>();

		panel.Build( display.rect.height );

		return panel;
	}

	// the parts of the panel, sized for a display this high
	void Build( float height )
	{
		var rowHeight = height * ( 1.0f - c_titleHeight - c_footerHeight ) / ( c_rows + 1 );
		var fontSize = Mathf.Max( 8.0f, rowHeight * c_textPerRow );

		m_title = MakeText( "Title", new Vector2( 0.04f, 1.0f - c_titleHeight ), new Vector2( 0.96f, 1.0f ), UIKitTheme.TextStyleRole.HeadingM, fontSize * 1.3f, UIKitTheme.ColorRole.TextAccent, TextAlignmentOptions.Center );
		m_title.text = "Icon key";

		m_footer = MakeText( "Page", new Vector2( 0.04f, 0.0f ), new Vector2( 0.96f, c_footerHeight ), UIKitTheme.TextStyleRole.LabelS, fontSize, UIKitTheme.ColorRole.TextMuted, TextAlignmentOptions.Center );

		for ( var row = 0; row < c_rows; row++ )
		{
			var top = 1.0f - c_titleHeight - ( 1.0f - c_titleHeight - c_footerHeight ) * row / ( c_rows + 1 );
			var bottom = 1.0f - c_titleHeight - ( 1.0f - c_titleHeight - c_footerHeight ) * ( row + 1 ) / ( c_rows + 1 );

			// the picture, square, at the left of the row
			var pictureObject = new GameObject( "Picture " + row, typeof( RectTransform ) );
			var pictureTransform = (RectTransform) pictureObject.transform;

			pictureTransform.SetParent( transform, false );
			pictureTransform.anchorMin = new Vector2( 0.06f, bottom );
			pictureTransform.anchorMax = new Vector2( 0.06f, top );
			pictureTransform.pivot = new Vector2( 0.0f, 0.5f );
			pictureTransform.sizeDelta = new Vector2( rowHeight * 0.9f, -rowHeight * 0.1f );
			pictureTransform.anchoredPosition = Vector2.zero;

			m_pictures[ row ] = pictureObject.AddComponent<RawImage>();
			m_pictures[ row ].raycastTarget = false;

			// the name beside it
			m_names[ row ] = MakeText( "Name " + row, new Vector2( 0.06f, bottom ), new Vector2( 0.96f, top ), UIKitTheme.TextStyleRole.BodyM, fontSize, UIKitTheme.ColorRole.TextPrimary, TextAlignmentOptions.MidlineLeft );
			m_names[ row ].rectTransform.offsetMin = new Vector2( rowHeight * 1.3f, 0.0f );
		}

		// the elevation bar and its words, in the second page's last rows
		var barTop = 1.0f - c_titleHeight - ( 1.0f - c_titleHeight - c_footerHeight ) * ( c_rows - 2 ) / ( c_rows + 1 );
		var barBottom = 1.0f - c_titleHeight - ( 1.0f - c_titleHeight - c_footerHeight ) * ( c_rows - 1 ) / ( c_rows + 1 );

		var barObject = new GameObject( "Elevation bar", typeof( RectTransform ) );
		var barTransform = (RectTransform) barObject.transform;

		barTransform.SetParent( transform, false );
		barTransform.anchorMin = new Vector2( 0.1f, barBottom );
		barTransform.anchorMax = new Vector2( 0.9f, barTop );
		barTransform.offsetMin = Vector2.zero;
		barTransform.offsetMax = Vector2.zero;

		m_elevationBar = barObject.AddComponent<Image>();
		m_elevationBar.raycastTarget = false;
		m_elevationBar.preserveAspect = false;

		var labelBottom = 1.0f - c_titleHeight - ( 1.0f - c_titleHeight - c_footerHeight ) * c_rows / ( c_rows + 1 );

		m_elevationLabel = MakeText( "Elevation", new Vector2( 0.04f, labelBottom ), new Vector2( 0.96f, barBottom ), UIKitTheme.TextStyleRole.LabelS, fontSize, UIKitTheme.ColorRole.TextSecondary, TextAlignmentOptions.Center );
		m_elevationLabel.text = "Lo.....elevation.....hi";

		ShowPage( 0 );
	}

	// shows a page of the key (0 or 1)
	public void ShowPage( int page )
	{
		m_page = Mathf.Clamp( page, 0, c_pageCount - 1 );

		var first = ( m_page == 0 ) ? 0 : TerrainIconKey.c_firstPageCount;
		var count = ( m_page == 0 ) ? TerrainIconKey.c_firstPageCount : ( TerrainIconKey.c_iconCount - TerrainIconKey.c_firstPageCount );

		for ( var row = 0; row < c_rows; row++ )
		{
			var shown = row < count;

			m_pictures[ row ].enabled = shown;
			m_names[ row ].enabled = shown;

			if ( shown )
			{
				m_pictures[ row ].texture = TerrainIconKey.GetTexture( first + row );
				m_names[ row ].text = TerrainIconKey.GetName( first + row );
			}
		}

		// the second page has the elevation bar of the planet's surface (the landing map's legend for it)
		var elevationSprite = ( m_page == c_pageCount - 1 ) ? GetElevationSprite() : null;

		m_elevationBar.enabled = elevationSprite != null;
		m_elevationBar.sprite = elevationSprite;
		m_elevationLabel.enabled = m_page == c_pageCount - 1;

		m_footer.text = "Page " + ( m_page + 1 ) + " of " + c_pageCount + ", left and right to turn";
	}

	// the page shown
	public int GetPage()
	{
		return m_page;
	}

	// the names on the page shown, one a line, and whether the elevation bar is shown - for the probe
	public string GetPageText()
	{
		var text = m_title.text;

		for ( var row = 0; row < c_rows; row++ )
		{
			if ( m_names[ row ].enabled )
			{
				text += "\n" + m_names[ row ].text;
			}
		}

		if ( m_elevationLabel.enabled )
		{
			text += "\n" + m_elevationLabel.text + ( m_elevationBar.enabled ? " (bar)" : "" );
		}

		return text;
	}

	// the elevation legend of the planet's surface from the landing map (null if there is none)
	static Sprite GetElevationSprite()
	{
		var displayController = SpaceflightController.m_instance.m_displayController;
		var playerData = DataController.m_instance.m_playerData;
		var planetController = ( SpaceflightController.m_instance.m_starSystem != null ) ? SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId ) : null;

		if ( ( displayController == null ) || ( displayController.m_terrainMapDisplay == null ) || ( planetController == null ) )
		{
			return null;
		}

		var legendImages = displayController.m_terrainMapDisplay.m_legendImages;
		var surfaceId = planetController.GetSurfaceId();

		if ( ( legendImages == null ) || ( surfaceId < 0 ) || ( surfaceId >= legendImages.Length ) || ( legendImages[ surfaceId ] == null ) )
		{
			return null;
		}

		return legendImages[ surfaceId ].sprite;
	}

	// a text of the panel in a style of the UI kit
	TextMeshProUGUI MakeText( string name, Vector2 anchorMin, Vector2 anchorMax, UIKitTheme.TextStyleRole textStyle, float fontSize, UIKitTheme.ColorRole color, TextAlignmentOptions alignment )
	{
		var textObject = new GameObject( name, typeof( RectTransform ) );
		var textTransform = (RectTransform) textObject.transform;

		textTransform.SetParent( transform, false );
		textTransform.anchorMin = anchorMin;
		textTransform.anchorMax = anchorMax;
		textTransform.offsetMin = Vector2.zero;
		textTransform.offsetMax = Vector2.zero;

		var textMesh = textObject.AddComponent<TextMeshProUGUI>();

		textMesh.alignment = alignment;
		textMesh.textWrappingMode = TextWrappingModes.NoWrap;
		textMesh.overflowMode = TextOverflowModes.Ellipsis;
		textMesh.raycastTarget = false;

		var style = textObject.AddComponent<UIKitStyle>();

		style.m_textStyle = textStyle;
		style.m_fontSize = fontSize;
		style.m_color = color;

		// (its OnEnable ran before these were set)
		style.Apply();

		return textMesh;
	}
}
