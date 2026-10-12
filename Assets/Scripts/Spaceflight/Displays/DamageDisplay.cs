
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// the original's DAMAGE display (ENGINEER in the decompiled original: (/DAMAGE) and DBARS): a row for each of the hull, the engines, the sensors, the comm, the
// shield, the missiles and the laser, with its label always and, for what the ship has, a bar whose green part is how whole it is and whose red part is what
// has been lost - the scene has no object for it, so DisplayController makes it at run time, with the UI kit's styles
public class DamageDisplay : ShipDisplay
{
	// the label of each row, as the original writes them, by repair item (the hull, then each PD_PlayerShip.ShipSystem)
	static readonly string[] c_rowLabels = { "Hull", "Engines", "Sensors", "Comm.", "Shield", "Missile", "Laser" };

	// where the rows are, in pixels (the status display's labels start 48 pixels from the side, and its lines are 41 pixels apart)
	const float c_topMargin = 24.0f;
	const float c_rowHeight = 41.0f;
	const float c_sideMargin = 48.0f;
	const float c_labelWidth = 200.0f;
	const float c_barHeight = 16.0f;
	const float c_framePadding = 8.0f;

	// the bar of each row (its red background) and the green part of it
	GameObject[] m_bars;
	RectTransform[] m_barFills;

	// how whole each row was when its bar was drawn (-1 for a row with no bar, int.MinValue before it is drawn)
	int[] m_shownHealth;

	// makes the display, hidden, under the other displays
	public static DamageDisplay Create( Transform parent )
	{
		var displayObject = new GameObject( "Damage", typeof( RectTransform ) );

		// hidden until it is shown (the UI kit styles its parts when they are enabled)
		displayObject.SetActive( false );

		var rectTransform = (RectTransform) displayObject.transform;

		rectTransform.SetParent( parent, false );

		// it fills the display's area, as the other displays do
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;

		var display = displayObject.AddComponent<DamageDisplay>();

		display.Build();

		return display;
	}

	// the display label
	public override string GetLabel()
	{
		return "Damage";
	}

	public override void Show()
	{
		base.Show();

		// draw every bar again
		for ( var item = 0; item < m_shownHealth.Length; item++ )
		{
			m_shownHealth[ item ] = int.MinValue;
		}

		UpdateBars();
	}

	public override void Update()
	{
		// the bars follow the repairs under way
		UpdateBars();
	}

	// how whole the row of a repair item shows it, from 0 to 100 (-1 if the row has no bar: the ship does not have it)
	public int GetShownHealth( int item )
	{
		if ( ( m_shownHealth == null ) || ( item < 0 ) || ( item >= m_shownHealth.Length ) )
		{
			return -1;
		}

		return m_shownHealth[ item ];
	}

	// the rows as the display shows them, for the probe ("HULL 100, ENGINES 80, ..., MISSILE -")
	public string Describe()
	{
		var text = "";

		for ( var item = 0; item < c_rowLabels.Length; item++ )
		{
			var health = GetShownHealth( item );

			text += ( ( item > 0 ) ? ", " : "" ) + c_rowLabels[ item ].ToUpper() + " " + ( ( health < 0 ) ? "-" : health.ToString() );
		}

		return text;
	}

	// draws the bars again where the ship has changed since they were drawn
	void UpdateBars()
	{
		if ( ( m_bars == null ) || ( DataController.m_instance == null ) )
		{
			return;
		}

		var ship = DataController.m_instance.m_playerData.m_playerShip;

		for ( var item = 0; item < m_bars.Length; item++ )
		{
			// the original draws a bar only for what the ship has (the hull always)
			var health = ship.HasRepairItem( item ) ? Mathf.Clamp( ship.GetRepairItemHealth( item ), 0, 100 ) : -1;

			if ( health == m_shownHealth[ item ] )
			{
				continue;
			}

			m_shownHealth[ item ] = health;

			m_bars[ item ].SetActive( health >= 0 );

			if ( health >= 0 )
			{
				// the green part is how whole it is, and the red background shows through for the rest
				m_barFills[ item ].anchorMax = new Vector2( health / 100.0f, 1.0f );
				m_barFills[ item ].gameObject.SetActive( health > 0 );
			}
		}
	}

	// makes the rows
	void Build()
	{
		var rowCount = c_rowLabels.Length;

		m_bars = new GameObject[ rowCount ];
		m_barFills = new RectTransform[ rowCount ];
		m_shownHealth = new int[ rowCount ];

		var barLeft = c_sideMargin + c_labelWidth;

		// the original's box round the bars
		var frame = MakeImage( "Frame", transform, UIKitTheme.SpriteRole.PanelOutline, UIKitTheme.ColorRole.BorderDefault );

		Place( frame.rectTransform, barLeft - c_framePadding, -1.0f, c_sideMargin - c_framePadding, c_topMargin - c_framePadding, rowCount * c_rowHeight + 2.0f * c_framePadding );

		for ( var item = 0; item < rowCount; item++ )
		{
			var top = c_topMargin + item * c_rowHeight;

			// the label, always (the original writes every one, whether the ship has the item or not)
			var label = MakeText( c_rowLabels[ item ], transform );

			Place( label.rectTransform, c_sideMargin, c_labelWidth, 0.0f, top, c_rowHeight );

			// the bar, in the middle of the row
			var bar = MakeImage( "Bar " + c_rowLabels[ item ], transform, UIKitTheme.SpriteRole.Bar, UIKitTheme.ColorRole.StatusDanger );

			Place( bar.rectTransform, barLeft, -1.0f, c_sideMargin, top + ( c_rowHeight - c_barHeight ) * 0.5f, c_barHeight );

			var fill = MakeImage( "Fill", bar.transform, UIKitTheme.SpriteRole.Bar, UIKitTheme.ColorRole.StatusFuel );

			fill.rectTransform.anchorMin = Vector2.zero;
			fill.rectTransform.anchorMax = Vector2.one;
			fill.rectTransform.offsetMin = Vector2.zero;
			fill.rectTransform.offsetMax = Vector2.zero;

			m_bars[ item ] = bar.gameObject;
			m_barFills[ item ] = fill.rectTransform;
			m_shownHealth[ item ] = int.MinValue;
		}
	}

	// puts a part of the display in place from the top left: a width, or a negative width to stretch it to the given distance from the right side
	static void Place( RectTransform rectTransform, float left, float width, float right, float top, float height )
	{
		var stretch = width < 0.0f;

		rectTransform.anchorMin = new Vector2( 0.0f, 1.0f );
		rectTransform.anchorMax = new Vector2( stretch ? 1.0f : 0.0f, 1.0f );
		rectTransform.pivot = new Vector2( 0.0f, 1.0f );
		rectTransform.offsetMin = new Vector2( left, -( top + height ) );
		rectTransform.offsetMax = new Vector2( stretch ? -right : ( left + width ), -top );
	}

	// makes a label of the display, in the style of the status display's labels
	static TextMeshProUGUI MakeText( string text, Transform parent )
	{
		var textObject = new GameObject( text, typeof( RectTransform ) );

		textObject.transform.SetParent( parent, false );

		var textMesh = textObject.AddComponent<TextMeshProUGUI>();

		textMesh.text = text;
		textMesh.alignment = TextAlignmentOptions.MidlineLeft;
		textMesh.textWrappingMode = TextWrappingModes.NoWrap;
		textMesh.raycastTarget = false;

		var style = textObject.AddComponent<UIKitStyle>();

		style.m_textStyle = UIKitTheme.TextStyleRole.LabelM;
		style.m_fontSize = 24.0f;
		style.m_color = UIKitTheme.ColorRole.TextSecondary;

		return textMesh;
	}

	// makes an image of the display, with a sprite and a colour of the UI kit
	static Image MakeImage( string name, Transform parent, UIKitTheme.SpriteRole sprite, UIKitTheme.ColorRole color )
	{
		var imageObject = new GameObject( name, typeof( RectTransform ) );

		imageObject.transform.SetParent( parent, false );

		var image = imageObject.AddComponent<Image>();

		image.raycastTarget = false;

		var style = imageObject.AddComponent<UIKitStyle>();

		style.m_sprite = sprite;
		style.m_color = color;

		return image;
	}
}
