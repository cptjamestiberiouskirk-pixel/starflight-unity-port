using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// gives what the scenes hold the look of the UI kit's theme in use (its colour scheme in its mode), where UIKitStyle does not: the tints of the buttons,
// the colours the kit's tools copied into the scenes without a UIKitStyle, and the colour tags written into texts (the scenes are saved in the classic
// dark theme's colours, so in that theme only the texts' tags are looked at)
public static class UIKitModeApplier
{
	// shows the colour tags of a text in the kit's colours, in every theme but the classic dark one (which shows texts as they are written, as it always has);
	// a text drawn over a 3D view takes the colours of the views' theme
	class TextColours : ITextPreprocessor
	{
		readonly bool m_overView;

		public TextColours( bool overView )
		{
			m_overView = overView;
		}

		public string PreprocessText( string text )
		{
			if ( m_overView )
			{
				return ( UIKitTheme.GetViewTheme() == UIKitTheme.Load( UIKitTheme.Mode.Dark ) ) ? text : UIKitTheme.KitColors( text, true );
			}

			return UIKitTheme.ShowsTagsAsWritten() ? text : UIKitTheme.KitColors( text );
		}
	}

	// the preprocessor every text shares, and the one of the texts drawn over a 3D view
	static readonly TextColours s_textColours = new TextColours( false );
	static readonly TextColours s_viewTextColours = new TextColours( true );

	// the theme the loaded scenes are in, and the theme of what they draw over the 3D views
	static UIKitTheme s_appliedTheme;
	static UIKitTheme s_appliedViewTheme;

	// the role of each colour copied into a scene, and the buttons tinted by the kit's tool, found the first time they were given another theme's colours
	static readonly System.Collections.Generic.Dictionary<Graphic, UIKitTheme.ColorRole> s_graphicRoles = new System.Collections.Generic.Dictionary<Graphic, UIKitTheme.ColorRole>();
	static readonly System.Collections.Generic.HashSet<Selectable> s_tintedButtons = new System.Collections.Generic.HashSet<Selectable>();

	// graphics that show colours of their own and are never given the theme's (the colour swatches of the save panel's Colors screen)
	static readonly System.Collections.Generic.HashSet<Graphic> s_ownColours = new System.Collections.Generic.HashSet<Graphic>();

	// unity: before the first scene loads
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void Initialize()
	{
		SceneManager.sceneLoaded -= SceneLoaded;
		SceneManager.sceneLoaded += SceneLoaded;

		UIKitTheme.Changed -= ThemeChanged;
		UIKitTheme.Changed += ThemeChanged;

		s_appliedTheme = UIKitTheme.Get();
		s_appliedViewTheme = UIKitTheme.GetViewTheme();
	}

	// leaves this graphic's colour alone when the theme changes (it shows a colour of its own)
	public static void KeepColour( Graphic graphic )
	{
		if ( graphic != null )
		{
			s_ownColours.Add( graphic );
		}
	}

	// a scene was loaded: it is in the dark mode's colours (and so are the objects it gave to the scene that is never unloaded, such as the save panel of
	// the Persistent scene, which have moved there by now; those already in the mode's colours match no dark colour and are left as they are)
	static void SceneLoaded( Scene scene, LoadSceneMode loadSceneMode )
	{
		var dark = UIKitTheme.Load( UIKitTheme.Mode.Dark );

		// the kit's buttons take the sci-fi button's shape (found by their tint, which is still the dark mode's)
		MakeButtons( scene, dark );
		MakeButtons( GetDontDestroyOnLoadScene(), dark );

		Apply( scene, dark );
		Apply( GetDontDestroyOnLoadScene(), dark );
	}

	// the name of the frame a button is given (and how a button that has its frame is known)
	public const string c_buttonFrameName = "UI Kit Button Frame";

	// gives every button the kit's tool tinted the sci-fi button's look: its fill with two corners cut off, and a thin frame in the kit's accent colour
	// (the frame is a child with its own UIKitStyle, so it takes the colours of each mode by itself)
	static void MakeButtons( Scene scene, UIKitTheme dark )
	{
		var theme = UIKitTheme.Get();

		if ( !scene.IsValid() || ( theme == null ) || ( dark == null ) || ( theme.m_buttonSprite == null ) || ( theme.m_buttonFrameSprite == null ) )
		{
			return;
		}

		foreach ( var rootObject in scene.GetRootGameObjects() )
		{
			foreach ( var button in rootObject.GetComponentsInChildren<Button>( true ) )
			{
				if ( ( button.transition != Selectable.Transition.ColorTint ) || !( button.targetGraphic is Image image ) || ( button.transform.Find( c_buttonFrameName ) != null ) )
				{
					continue;
				}

				// only the kit's buttons (an arrow or an icon keeps its own picture)
				if ( !s_tintedButtons.Contains( button ) && ( UIKitTheme.FindRole( dark, button.colors.normalColor ) != UIKitTheme.ColorRole.BackgroundRaised ) && ( UIKitTheme.FindRole( theme, button.colors.normalColor ) != UIKitTheme.ColorRole.BackgroundRaised ) )
				{
					continue;
				}

				MakeButton( image );
			}
		}
	}

	// gives one image the sci-fi button's fill, and a frame over it (under the button's label)
	public static void MakeButton( Image image )
	{
		var theme = UIKitTheme.Get();

		if ( ( image == null ) || ( theme == null ) || ( theme.m_buttonSprite == null ) )
		{
			return;
		}

		image.sprite = theme.m_buttonSprite;
		image.type = Image.Type.Sliced;
		image.pixelsPerUnitMultiplier = 1.0f;

		// a UIKitStyle on the image would give it its old sprite back when the mode changes
		if ( image.TryGetComponent<UIKitStyle>( out var imageStyle ) && ( imageStyle.m_sprite != UIKitTheme.SpriteRole.None ) )
		{
			imageStyle.m_sprite = UIKitTheme.SpriteRole.Button;
		}

		if ( image.transform.Find( c_buttonFrameName ) != null )
		{
			return;
		}

		var frameObject = new GameObject( c_buttonFrameName, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( LayoutElement ) );
		var frameTransform = frameObject.GetComponent<RectTransform>();

		frameTransform.SetParent( image.transform, false );
		frameTransform.SetAsFirstSibling();
		frameTransform.anchorMin = Vector2.zero;
		frameTransform.anchorMax = Vector2.one;
		frameTransform.offsetMin = Vector2.zero;
		frameTransform.offsetMax = Vector2.zero;

		frameObject.GetComponent<LayoutElement>().ignoreLayout = true;

		var frame = frameObject.GetComponent<Image>();

		frame.raycastTarget = false;

		var frameStyle = frameObject.AddComponent<UIKitStyle>();

		frameStyle.m_sprite = UIKitTheme.SpriteRole.ButtonFrame;
		frameStyle.m_color = UIKitTheme.ColorRole.BorderAccent;
		frameStyle.m_alpha = 0.7f;
		frameStyle.Apply();
	}

	// the player changed the scheme or the mode: every loaded scene goes from the old theme's colours to the new
	static void ThemeChanged()
	{
		var from = ( s_appliedTheme != null ) ? s_appliedTheme : UIKitTheme.Load( UIKitTheme.Mode.Dark );
		var fromView = ( s_appliedViewTheme != null ) ? s_appliedViewTheme : UIKitTheme.Load( UIKitTheme.Mode.Dark );

		s_appliedTheme = UIKitTheme.Get();
		s_appliedViewTheme = UIKitTheme.GetViewTheme();

		for ( var i = 0; i < SceneManager.sceneCount; i++ )
		{
			var scene = SceneManager.GetSceneAt( i );

			if ( scene.isLoaded )
			{
				Apply( scene, from, fromView );
			}
		}

		// the scene that is never unloaded is not in the scene manager's list
		Apply( GetDontDestroyOnLoadScene(), from, fromView );
	}

	// an object kept in the scene that is never unloaded, to find that scene by
	static GameObject s_dontDestroyOnLoadMarker;

	// returns the scene the objects that are never unloaded live in (the save panel and the other persistent objects)
	static Scene GetDontDestroyOnLoadScene()
	{
		if ( s_dontDestroyOnLoadMarker == null )
		{
			s_dontDestroyOnLoadMarker = new GameObject( "UI Kit Mode Marker" );
			s_dontDestroyOnLoadMarker.hideFlags = HideFlags.HideInHierarchy;

			Object.DontDestroyOnLoad( s_dontDestroyOnLoadMarker );
		}

		return s_dontDestroyOnLoadMarker.scene;
	}

	// gives a scene the look of the theme in use, from the colours of the theme it is in now (and what it draws over the 3D views the look of the views'
	// theme, from the colours of the views' theme before; the same as the other unless said)
	public static void Apply( Scene scene, UIKitTheme from, UIKitTheme fromView = null )
	{
		var to = UIKitTheme.Get();
		var toView = UIKitTheme.GetViewTheme();

		if ( ( to == null ) || ( toView == null ) || !scene.IsValid() )
		{
			return;
		}

		if ( fromView == null )
		{
			fromView = from;
		}

		var recolour = ( ( from != null ) && ( from != to ) ) || ( ( fromView != null ) && ( fromView != toView ) );

		// the colours the scenes are saved in: an object made after its scene was given another theme's colours (a row of a list, from its prefab) still has them
		var saved = UIKitTheme.Load( UIKitTheme.Mode.Dark );

		// forget what has been destroyed since
		if ( recolour )
		{
			s_tintedButtons.RemoveWhere( selectable => selectable == null );
			s_ownColours.RemoveWhere( graphic => graphic == null );

			var destroyed = new System.Collections.Generic.List<Graphic>();

			foreach ( var graphic in s_graphicRoles.Keys )
			{
				if ( graphic == null )
				{
					destroyed.Add( graphic );
				}
			}

			foreach ( var graphic in destroyed )
			{
				s_graphicRoles.Remove( graphic );
			}
		}

		foreach ( var rootObject in scene.GetRootGameObjects() )
		{
			if ( recolour )
			{
				// the buttons' tints (UIKitSceneTool.TintButton)
				foreach ( var selectable in rootObject.GetComponentsInChildren<Selectable>( true ) )
				{
					if ( UIKitTheme.IsOverView( selectable ) )
					{
						Retint( selectable, fromView, toView, saved );
					}
					else
					{
						Retint( selectable, from, to, saved );
					}
				}

				// the colours copied into the scene (an element with a UIKitStyle that sets its colour does it itself)
				foreach ( var graphic in rootObject.GetComponentsInChildren<Graphic>( true ) )
				{
					if ( ( graphic.TryGetComponent<UIKitStyle>( out var style ) && ( style.m_color != UIKitTheme.ColorRole.Keep ) ) || s_ownColours.Contains( graphic ) )
					{
						continue;
					}

					// white is no colour of the kit's but a sprite's own (an image the button's tint colours), so it is never taken for a role
					if ( ( graphic.color.r > 0.996f ) && ( graphic.color.g > 0.996f ) && ( graphic.color.b > 0.996f ) )
					{
						continue;
					}

					// a graphic over a 3D view goes from and to the views' theme
					var overView = UIKitTheme.IsOverView( graphic );
					var graphicFrom = overView ? fromView : from;
					var graphicTo = overView ? toView : to;

					// the role is found once, in the colours it was given (the light mode gives several roles one colour, so its colours cannot tell them apart)
					if ( !s_graphicRoles.TryGetValue( graphic, out var role ) )
					{
						role = UIKitTheme.FindRole( graphicFrom, graphic.color );

						if ( ( role == UIKitTheme.ColorRole.Keep ) && ( saved != graphicFrom ) )
						{
							role = UIKitTheme.FindRole( saved, graphic.color );
						}

						if ( role != UIKitTheme.ColorRole.Keep )
						{
							s_graphicRoles[ graphic ] = role;
						}
					}

					if ( role != UIKitTheme.ColorRole.Keep )
					{
						var color = graphicTo.GetColor( role );

						color.a = graphic.color.a;

						graphic.color = color;
					}
				}
			}

			// the texts' colour tags, and the texts shown again with them
			foreach ( var text in rootObject.GetComponentsInChildren<TMP_Text>( true ) )
			{
				if ( ( text.textPreprocessor == null ) || ( text.textPreprocessor == s_textColours ) || ( text.textPreprocessor == s_viewTextColours ) )
				{
					text.textPreprocessor = UIKitTheme.IsOverView( text ) ? s_viewTextColours : s_textColours;

					if ( text.isActiveAndEnabled )
					{
						text.ForceMeshUpdate( true, true );
					}
				}
			}
		}
	}

	// gives a button the tints of the theme in use, if it has the tints of the one before (as the kit's tool tints a button)
	static void Retint( Selectable selectable, UIKitTheme from, UIKitTheme to, UIKitTheme saved )
	{
		if ( selectable.transition != Selectable.Transition.ColorTint )
		{
			return;
		}

		var colours = selectable.colors;

		// a button is known for one of the kit's once, by its tint in the colours it was given (as for the graphics), or in the colours the scenes are
		// saved in (a button made after its scene was given another theme's colours)
		if ( !s_tintedButtons.Contains( selectable ) )
		{
			if ( !SameColour( colours.normalColor, from.m_backgroundRaised ) && ( ( saved == null ) || !SameColour( colours.normalColor, saved.m_backgroundRaised ) ) )
			{
				return;
			}

			s_tintedButtons.Add( selectable );
		}

		colours.normalColor = to.m_backgroundRaised;
		colours.highlightedColor = to.m_accentPrimaryDim;
		colours.selectedColor = to.m_accentPrimaryDim;
		colours.pressedColor = to.m_accentPrimaryStrong;
		colours.disabledColor = new Color( to.m_backgroundHover.r, to.m_backgroundHover.g, to.m_backgroundHover.b, 0.5f );

		selectable.colors = colours;
	}

	// whether two colours are the same colour of the kit (as FindRole tells them apart)
	static bool SameColour( Color a, Color b )
	{
		return ( Mathf.Abs( a.r - b.r ) < 0.004f ) && ( Mathf.Abs( a.g - b.g ) < 0.004f ) && ( Mathf.Abs( a.b - b.b ) < 0.004f );
	}
}
