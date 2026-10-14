
using UnityEngine;
using UnityEngine.SceneManagement;

// gives the Starport station (the concourse the astronaut walks in, and the station the ship launches from in the Spaceflight scene) the colours of the
// UI kit's scheme in use, in its dark mode, since the station is a 3D view: its materials keep the classic dark theme's colours (UIKitConcourseTool gives
// them those), and every renderer that draws with one of them is given the scheme's colours in a property block, so the material assets are left as they are;
// the floor's albedo map holds the weights of five of the theme's colours, which its material (SF - Standard with SF_PALETTEMAP_ON) mixes; in the schemes
// of the original games the station has the 1986 Starport's colours instead
public static class StarportColours
{
	// the colours of one of the station's materials: the theme's role for its albedo, the role of its glow and how bright it is, and its colours in the 1986
	// Starport (the original game's EGA colours) and whether it glowed there in its own colour
	public struct MaterialColours
	{
		public string m_name;
		public UIKitTheme.ColorRole m_albedo;
		public UIKitTheme.ColorRole m_glow;
		public float m_glowStrength;
		public Color32 m_original;
		public bool m_originalGlows;

		public MaterialColours( string name, UIKitTheme.ColorRole albedo, Color32 original, UIKitTheme.ColorRole glow = UIKitTheme.ColorRole.Keep, float glowStrength = 0.0f, bool originalGlows = false )
		{
			m_name = name;
			m_albedo = albedo;
			m_glow = glow;
			m_glowStrength = glowStrength;
			m_original = original;
			m_originalGlows = originalGlows;
		}
	}

	// the station's materials that take the theme's colours (the original's black, dark grey and light grey hull, its blue wall, red doors and brown pad edge
	// become the kit's navy, border and secondary text colours, its sensor window's steel blue, a dim cyan that glows softly, and its amber, glowing);
	// Interstel's logo and the transporter's particles keep their own. The 1986 colours are the original's screens' (Research/Screenshots/Starport), which
	// the port's materials had before the kit: the hull's black is a dark grey, so that its shape shows, and its edging keeps the grey of its own texture
	public static readonly MaterialColours[] c_materials =
	{
		new MaterialColours( "Starport Hull - Black", UIKitTheme.ColorRole.BackgroundPanel, new Color32( 0x26, 0x26, 0x26, 0xFF ) ),
		new MaterialColours( "Starport Hull - Dark", UIKitTheme.ColorRole.BorderDefault, new Color32( 0x55, 0x55, 0x55, 0xFF ) ),
		new MaterialColours( "Starport Hull - Light", UIKitTheme.ColorRole.TextSecondary, new Color32( 0xAA, 0xAA, 0xAA, 0xFF ) ),
		new MaterialColours( "Starport Hull - Edge", UIKitTheme.ColorRole.TextSecondary, new Color32( 0xFF, 0xFF, 0xFF, 0xFF ) ),
		new MaterialColours( "Starport Wall", UIKitTheme.ColorRole.SensorWindow, new Color32( 0x00, 0x00, 0xAA, 0xFF ) ),
		new MaterialColours( "Starport Doors", UIKitTheme.ColorRole.AccentPrimaryDim, new Color32( 0xAA, 0x00, 0x00, 0xFF ), UIKitTheme.ColorRole.AccentPrimary, 0.3f ),
		new MaterialColours( "Starport Pad - Inside", UIKitTheme.ColorRole.BackgroundPanel, new Color32( 0x55, 0x55, 0x55, 0xFF ) ),
		new MaterialColours( "Starport Pad - Outside", UIKitTheme.ColorRole.BorderDefault, new Color32( 0xAA, 0xAA, 0xAA, 0xFF ) ),
		new MaterialColours( "Starport Pad - Edge", UIKitTheme.ColorRole.AccentSecondary, new Color32( 0xAA, 0x55, 0x00, 0xFF ), UIKitTheme.ColorRole.AccentSecondary, 0.5f, true )
	};

	// the floor's material, and the theme's colours its weights are of, in the order of the palette's colours (the albedo map's red, green, blue and alpha,
	// and the rest): the floor, the cyan band round the star, the dark behind the door mats, the door mats, and the star; the band also glows in the accent
	public const string c_floorMaterial = "Starport Floor";

	public static readonly UIKitTheme.ColorRole[] c_floorPalette =
	{
		UIKitTheme.ColorRole.BackgroundHover,
		UIKitTheme.ColorRole.AccentPrimary,
		UIKitTheme.ColorRole.BackgroundApp,
		UIKitTheme.ColorRole.BorderDefault,
		UIKitTheme.ColorRole.BackgroundPanel
	};

	public const UIKitTheme.ColorRole c_floorGlow = UIKitTheme.ColorRole.AccentPrimary;
	public const float c_floorGlowStrength = 0.9f;

	// the 1986 floor in the same order: light cyan, with no band of its own (the band is floor) and nothing glowing, door mats of dark grey, and a light blue star
	public static readonly Color32[] c_originalFloorPalette =
	{
		new Color32( 0x55, 0xFF, 0xFF, 0xFF ),
		new Color32( 0x55, 0xFF, 0xFF, 0xFF ),
		new Color32( 0x00, 0x00, 0x00, 0xFF ),
		new Color32( 0x55, 0x55, 0x55, 0xFF ),
		new Color32( 0x55, 0x55, 0xFF, 0xFF )
	};

	// the schemes of the original games, in which the station has the 1986 Starport's colours rather than their kit's roles
	public static readonly string[] c_originalSchemes = { "Starflight", "Starflight 2" };

	// the shader's properties
	public const string c_albedoColour = "SF_AlbedoColor";
	public const string c_emissiveColour = "SF_EmissiveColor";
	public const string c_emissiveTint = "SF_EmissiveTint";
	public const string c_paletteColour = "SF_PaletteColor";
	public const string c_paletteKeyword = "SF_PALETTEMAP_ON";

	// the renderers that draw with the station's materials, in the scenes that are loaded
	static readonly System.Collections.Generic.List<Renderer> s_renderers = new System.Collections.Generic.List<Renderer>();

	// the property block the colours are set in (one for all, filled from each renderer's own before it is changed)
	static MaterialPropertyBlock s_propertyBlock;

	// unity: before the first scene loads
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void Initialize()
	{
		SceneManager.sceneLoaded -= SceneLoaded;
		SceneManager.sceneLoaded += SceneLoaded;

		UIKitTheme.Changed -= Apply;
		UIKitTheme.Changed += Apply;

		s_renderers.Clear();
	}

	// finds the renderers of a scene that has loaded that draw with the station's materials (hidden ones too), and gives them the scheme's colours
	static void SceneLoaded( Scene scene, LoadSceneMode mode )
	{
		// the renderers of the scenes that have gone
		s_renderers.RemoveAll( stationRenderer => stationRenderer == null );

		foreach ( var root in scene.GetRootGameObjects() )
		{
			foreach ( var stationRenderer in root.GetComponentsInChildren<Renderer>( true ) )
			{
				if ( DrawsTheStation( stationRenderer ) && !s_renderers.Contains( stationRenderer ) )
				{
					s_renderers.Add( stationRenderer );
				}
			}
		}

		Apply();
	}

	// whether a renderer draws with one of the station's materials
	static bool DrawsTheStation( Renderer stationRenderer )
	{
		foreach ( var material in stationRenderer.sharedMaterials )
		{
			if ( ( material != null ) && ( ( material.name == c_floorMaterial ) || ( Find( material.name ) >= 0 ) ) )
			{
				return true;
			}
		}

		return false;
	}

	// the index in c_materials of a material's colours, or -1
	public static int Find( string materialName )
	{
		for ( var i = 0; i < c_materials.Length; i++ )
		{
			if ( c_materials[ i ].m_name == materialName )
			{
				return i;
			}
		}

		return -1;
	}

	// the albedo and the glow of a material in a theme
	public static void GetColours( MaterialColours colours, UIKitTheme theme, out Color albedo, out Color emissive )
	{
		albedo = theme.GetColor( colours.m_albedo );
		emissive = ( colours.m_glow == UIKitTheme.ColorRole.Keep ) ? Color.black : theme.GetColor( colours.m_glow ) * colours.m_glowStrength;
	}

	// the albedo and the glow of a material in the 1986 Starport
	public static void GetOriginalColours( MaterialColours colours, out Color albedo, out Color emissive )
	{
		albedo = colours.m_original;
		emissive = colours.m_originalGlows ? albedo : Color.black;
	}

	// whether the scheme in use is one of the original games', in which the station has the 1986 Starport's colours
	public static bool UsesOriginalColours()
	{
		var scheme = UIKitTheme.GetScheme();

		if ( ( scheme < 0 ) || ( scheme >= UIKitTheme.c_schemes.Length ) )
		{
			return false;
		}

		return System.Array.IndexOf( c_originalSchemes, UIKitTheme.c_schemes[ scheme ].m_name ) >= 0;
	}

	// gives the renderers the colours of the scheme in use, in its dark mode (or the 1986 Starport's, in the original games' schemes)
	public static void Apply()
	{
		var theme = UIKitTheme.GetViewTheme();

		if ( theme == null )
		{
			return;
		}

		var original = UsesOriginalColours();

		if ( s_propertyBlock == null )
		{
			s_propertyBlock = new MaterialPropertyBlock();
		}

		foreach ( var stationRenderer in s_renderers )
		{
			if ( stationRenderer == null )
			{
				continue;
			}

			var materials = stationRenderer.sharedMaterials;

			for ( var i = 0; i < materials.Length; i++ )
			{
				if ( materials[ i ] == null )
				{
					continue;
				}

				// keep what else the renderer's block for this material holds
				stationRenderer.GetPropertyBlock( s_propertyBlock, i );

				if ( materials[ i ].name == c_floorMaterial )
				{
					// the floor: the palette its weights are of, and the glow of its band (the 1986 floor has none)
					for ( var p = 0; p < c_floorPalette.Length; p++ )
					{
						s_propertyBlock.SetColor( c_paletteColour + p, original ? (Color) c_originalFloorPalette[ p ] : theme.GetColor( c_floorPalette[ p ] ) );
					}

					s_propertyBlock.SetColor( c_emissiveTint, original ? Color.black : theme.GetColor( c_floorGlow ) * c_floorGlowStrength );
				}
				else
				{
					var index = Find( materials[ i ].name );

					if ( index < 0 )
					{
						continue;
					}

					Color albedo;
					Color emissive;

					if ( original )
					{
						GetOriginalColours( c_materials[ index ], out albedo, out emissive );
					}
					else
					{
						GetColours( c_materials[ index ], theme, out albedo, out emissive );
					}

					// (the material's own opacity stays)
					albedo.a = materials[ i ].GetColor( c_albedoColour ).a;

					s_propertyBlock.SetColor( c_albedoColour, albedo );
					s_propertyBlock.SetColor( c_emissiveColour, emissive );
				}

				stationRenderer.SetPropertyBlock( s_propertyBlock, i );
			}
		}
	}
}
