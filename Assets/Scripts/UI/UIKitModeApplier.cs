using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// gives what the scenes hold the look of the UI kit's mode, where UIKitStyle does not: the tints of the buttons, the colours the kit's tools copied into the
// scenes without a UIKitStyle, and the colour tags written into texts (the scenes are saved in the dark mode's colours, so in the dark mode only the texts' tags are looked at)
public static class UIKitModeApplier
{
	// shows the colour tags of a text in the kit's colours, in the light mode (the dark mode shows texts as they are written, as it always has)
	class TextColours : ITextPreprocessor
	{
		public string PreprocessText( string text )
		{
			return ( UIKitTheme.GetMode() == UIKitTheme.Mode.Light ) ? UIKitTheme.KitColors( text ) : text;
		}
	}

	// the one preprocessor every text shares
	static readonly TextColours s_textColours = new TextColours();

	// the mode the loaded scenes are in
	static UIKitTheme.Mode s_appliedMode = UIKitTheme.Mode.Dark;

	// the role of each colour copied into a scene, and the buttons tinted by the kit's tool, found the first time they were given another mode's colours
	static readonly System.Collections.Generic.Dictionary<Graphic, UIKitTheme.ColorRole> s_graphicRoles = new System.Collections.Generic.Dictionary<Graphic, UIKitTheme.ColorRole>();
	static readonly System.Collections.Generic.HashSet<Selectable> s_tintedButtons = new System.Collections.Generic.HashSet<Selectable>();

	// unity: before the first scene loads
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void Initialize()
	{
		SceneManager.sceneLoaded -= SceneLoaded;
		SceneManager.sceneLoaded += SceneLoaded;

		UIKitTheme.Changed -= ModeChanged;
		UIKitTheme.Changed += ModeChanged;

		s_appliedMode = UIKitTheme.GetMode();
	}

	// a scene was loaded: it is in the dark mode's colours (and so are the objects it gave to the scene that is never unloaded, such as the save panel of
	// the Persistent scene, which have moved there by now; those already in the mode's colours match no dark colour and are left as they are)
	static void SceneLoaded( Scene scene, LoadSceneMode loadSceneMode )
	{
		var dark = UIKitTheme.Load( UIKitTheme.Mode.Dark );

		Apply( scene, dark );
		Apply( GetDontDestroyOnLoadScene(), dark );
	}

	// the player changed the mode: every loaded scene goes from the old mode's colours to the new
	static void ModeChanged()
	{
		var from = UIKitTheme.Load( s_appliedMode );

		s_appliedMode = UIKitTheme.GetMode();

		for ( var i = 0; i < SceneManager.sceneCount; i++ )
		{
			var scene = SceneManager.GetSceneAt( i );

			if ( scene.isLoaded )
			{
				Apply( scene, from );
			}
		}

		// the scene that is never unloaded is not in the scene manager's list
		Apply( GetDontDestroyOnLoadScene(), from );
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

	// gives a scene the look of the mode in use, from the colours of the theme it is in now
	public static void Apply( Scene scene, UIKitTheme from )
	{
		var to = UIKitTheme.Get();

		if ( ( to == null ) || !scene.IsValid() )
		{
			return;
		}

		var recolour = ( from != null ) && ( from != to );

		// forget what has been destroyed since
		if ( recolour )
		{
			s_tintedButtons.RemoveWhere( selectable => selectable == null );

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
					Retint( selectable, from, to );
				}

				// the colours copied into the scene (an element with a UIKitStyle that sets its colour does it itself)
				foreach ( var graphic in rootObject.GetComponentsInChildren<Graphic>( true ) )
				{
					if ( graphic.TryGetComponent<UIKitStyle>( out var style ) && ( style.m_color != UIKitTheme.ColorRole.Keep ) )
					{
						continue;
					}

					// white is no colour of the kit's but a sprite's own (an image the button's tint colours), so it is never taken for a role
					if ( ( graphic.color.r > 0.996f ) && ( graphic.color.g > 0.996f ) && ( graphic.color.b > 0.996f ) )
					{
						continue;
					}

					// the role is found once, in the colours it was given (the light mode gives several roles one colour, so its colours cannot tell them apart)
					if ( !s_graphicRoles.TryGetValue( graphic, out var role ) )
					{
						role = UIKitTheme.FindRole( from, graphic.color );

						if ( role != UIKitTheme.ColorRole.Keep )
						{
							s_graphicRoles[ graphic ] = role;
						}
					}

					if ( role != UIKitTheme.ColorRole.Keep )
					{
						var color = to.GetColor( role );

						color.a = graphic.color.a;

						graphic.color = color;
					}
				}
			}

			// the texts' colour tags, and the texts shown again with them
			foreach ( var text in rootObject.GetComponentsInChildren<TMP_Text>( true ) )
			{
				if ( ( text.textPreprocessor == null ) || ( text.textPreprocessor == s_textColours ) )
				{
					text.textPreprocessor = s_textColours;

					if ( text.isActiveAndEnabled )
					{
						text.ForceMeshUpdate( true, true );
					}
				}
			}
		}
	}

	// gives a button the tints of the mode in use, if it has the tints of the other (as the kit's tool tints a button)
	static void Retint( Selectable selectable, UIKitTheme from, UIKitTheme to )
	{
		if ( selectable.transition != Selectable.Transition.ColorTint )
		{
			return;
		}

		var colours = selectable.colors;

		// a button is known for one of the kit's once, by its tint in the colours it was given (as for the graphics)
		if ( !s_tintedButtons.Contains( selectable ) )
		{
			if ( UIKitTheme.FindRole( from, colours.normalColor ) != UIKitTheme.ColorRole.BackgroundRaised )
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
}
