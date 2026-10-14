
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// gives a UI element the look of a role of the UI kit (UIKitTheme): a text style for a text, a sprite for an image, and a colour
// (applied when the element is enabled and, in the editor, whenever a value here changes, so the scene shows what the game shows)
[ExecuteAlways]
[DisallowMultipleComponent]
public class UIKitStyle : MonoBehaviour
{
	// the text style (texts only)
	public UIKitTheme.TextStyleRole m_textStyle = UIKitTheme.TextStyleRole.None;

	// the font size, if not the text style's own (0 keeps the style's size)
	public float m_fontSize;

	// the height of a line in pixels, if the text's lines have to line up with something (0 keeps the font's own line height)
	public float m_lineHeight;

	// the sprite (images only)
	public UIKitTheme.SpriteRole m_sprite = UIKitTheme.SpriteRole.None;

	// the colour
	public UIKitTheme.ColorRole m_color = UIKitTheme.ColorRole.Keep;

	// how opaque the colour is
	[Range( 0.0f, 1.0f )]
	public float m_alpha = 1.0f;

	// unity on enable
	void OnEnable()
	{
		// the element takes the new look when the player changes the kit's mode
		UIKitTheme.Changed -= Apply;
		UIKitTheme.Changed += Apply;

		Apply();
	}

	// unity on disable
	void OnDisable()
	{
		UIKitTheme.Changed -= Apply;
	}

#if UNITY_EDITOR
	// unity on validate (a value changed in the inspector)
	void OnValidate()
	{
		Apply();
	}
#endif

	// gives the element its look (in the theme of the 3D views when it is drawn over one: dark in either mode)
	public void Apply()
	{
		var theme = UIKitTheme.IsOverView( this ) ? UIKitTheme.GetViewTheme() : UIKitTheme.Get();

		if ( theme == null )
		{
			return;
		}

		// a text gets its style and size
		if ( TryGetComponent<TMP_Text>( out var text ) )
		{
			var style = theme.GetTextStyle( m_textStyle );

			if ( style != null )
			{
				style.Apply( text );

				if ( m_fontSize > 0.0f )
				{
					text.fontSize = m_fontSize;
				}

				// TextMesh Pro adds the line spacing in hundredths of an em to the font's own line height, so work out the spacing that gives this line height
				if ( ( m_lineHeight > 0.0f ) && ( text.font != null ) && ( text.font.faceInfo.pointSize > 0 ) && ( text.fontSize > 0.0f ) )
				{
					var naturalLineHeight = text.font.faceInfo.lineHeight / text.font.faceInfo.pointSize * text.fontSize;

					text.lineSpacing = ( m_lineHeight - naturalLineHeight ) / ( 0.01f * text.fontSize );
				}
			}
		}

		// an image gets its sprite, sliced unless it is a diamond
		if ( TryGetComponent<Image>( out var image ) )
		{
			var sprite = theme.GetSprite( m_sprite );

			if ( sprite != null )
			{
				image.sprite = sprite;
				image.type = ( ( m_sprite == UIKitTheme.SpriteRole.Diamond ) || ( m_sprite == UIKitTheme.SpriteRole.DiamondOutline ) ) ? Image.Type.Simple : Image.Type.Sliced;
				image.pixelsPerUnitMultiplier = 1.0f;
			}
		}

		// any graphic gets its colour
		if ( ( m_color != UIKitTheme.ColorRole.Keep ) && TryGetComponent<Graphic>( out var graphic ) )
		{
			var color = theme.GetColor( m_color );

			color.a = m_alpha;

			graphic.color = color;
		}
	}
}
