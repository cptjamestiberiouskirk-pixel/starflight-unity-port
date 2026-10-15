
using UnityEngine;
using UnityEngine.SceneManagement;

// gives the Starport station (the concourse the astronaut walks in, and the station the ship launches from) and the docking bay (the hangar the ship waits
// in before launch) the colours of the UI kit's scheme in use, in its dark mode, since they are 3D views: their materials keep the classic dark theme's
// colours (UIKitConcourseTool and UIKitDockingBayTool give them those), and every renderer that draws with one of them is given the scheme's colours in a
// property block, so the material assets are left as they are; the concourse's floor and the bay's doors hold the weights of a palette of the theme's
// colours in their albedo maps, which their material (SF - Standard with SF_PALETTEMAP_ON) mixes; in the schemes of the original games the station has the
// 1986 Starport's colours instead, and the bay its colours from before the kit
public static class StarportColours
{
	// the colours of one of the materials: the theme's role for its albedo (mixed with a second role if it has one, Keep if not), the role of its glow
	// (Keep for none) and how bright it is, whether the glow tints the emissive colour its animations give it rather than being that colour (the bay's
	// sirens, whose lights shine in the glow's colour too), and its colour and glow in 1986 (a glow of black for none)
	public struct MaterialColours
	{
		public string m_name;
		public UIKitTheme.ColorRole m_albedo;
		public UIKitTheme.ColorRole m_albedoMix;
		public float m_mix;
		public UIKitTheme.ColorRole m_glow;
		public float m_glowStrength;
		public bool m_glowTints;
		public Color32 m_original;
		public Color32 m_originalGlow;
	}

	// the materials with one colour that take the theme's (the original's black, dark grey and light grey hull, its blue wall, red doors and brown pad edge
	// become the kit's navy, border and secondary text colours, its sensor window's steel blue, a dim cyan that glows softly, and its amber, glowing; the
	// bay's white walls a light blue grey between the kit's two text colours, and its orange sirens on grey bases the kit's amber on its border blue);
	// Interstel's logo, the transporter's particles and the bay's particles keep their own. The 1986 colours are the original's screens'
	// (Research/Screenshots/Starport), which the port's materials had before the kit: the hull's black is a dark grey, so that its shape shows, and its
	// edging keeps the grey of its own texture; the original has no docking bay of this kind, so the bay has the port's colours from before the kit
	public static readonly MaterialColours[] c_materials =
	{
		new MaterialColours { m_name = "Starport Hull - Black", m_albedo = UIKitTheme.ColorRole.BackgroundPanel, m_original = new Color32( 0x26, 0x26, 0x26, 0xFF ) },
		new MaterialColours { m_name = "Starport Hull - Dark", m_albedo = UIKitTheme.ColorRole.BorderDefault, m_original = new Color32( 0x55, 0x55, 0x55, 0xFF ) },
		new MaterialColours { m_name = "Starport Hull - Light", m_albedo = UIKitTheme.ColorRole.TextSecondary, m_original = new Color32( 0xAA, 0xAA, 0xAA, 0xFF ) },
		new MaterialColours { m_name = "Starport Hull - Edge", m_albedo = UIKitTheme.ColorRole.TextSecondary, m_original = new Color32( 0xFF, 0xFF, 0xFF, 0xFF ) },
		new MaterialColours { m_name = "Starport Wall", m_albedo = UIKitTheme.ColorRole.SensorWindow, m_original = new Color32( 0x00, 0x00, 0xAA, 0xFF ) },
		new MaterialColours { m_name = "Starport Doors", m_albedo = UIKitTheme.ColorRole.AccentPrimaryDim, m_glow = UIKitTheme.ColorRole.AccentPrimary, m_glowStrength = 0.3f, m_original = new Color32( 0xAA, 0x00, 0x00, 0xFF ) },
		new MaterialColours { m_name = "Starport Pad - Inside", m_albedo = UIKitTheme.ColorRole.BackgroundPanel, m_original = new Color32( 0x55, 0x55, 0x55, 0xFF ) },
		new MaterialColours { m_name = "Starport Pad - Outside", m_albedo = UIKitTheme.ColorRole.BorderDefault, m_original = new Color32( 0xAA, 0xAA, 0xAA, 0xFF ) },
		new MaterialColours { m_name = "Starport Pad - Edge", m_albedo = UIKitTheme.ColorRole.AccentSecondary, m_glow = UIKitTheme.ColorRole.AccentSecondary, m_glowStrength = 0.5f, m_original = new Color32( 0xAA, 0x55, 0x00, 0xFF ), m_originalGlow = new Color32( 0xAA, 0x55, 0x00, 0xFF ) },
		new MaterialColours { m_name = "Docking Bay Walls", m_albedo = UIKitTheme.ColorRole.TextPrimary, m_albedoMix = UIKitTheme.ColorRole.TextSecondary, m_mix = 0.5f, m_original = new Color32( 0xFF, 0xFF, 0xFF, 0xFF ) },
		new MaterialColours { m_name = "Docking Bay Siren Dome", m_albedo = UIKitTheme.ColorRole.AccentSecondary, m_glow = UIKitTheme.ColorRole.AccentSecondary, m_glowStrength = 1.0f, m_glowTints = true, m_original = new Color32( 0xFF, 0x95, 0x00, 0xFF ), m_originalGlow = new Color32( 0xFF, 0x95, 0x00, 0xFF ) },
		new MaterialColours { m_name = "Docking Bay Siren Base", m_albedo = UIKitTheme.ColorRole.BorderDefault, m_original = new Color32( 0x33, 0x33, 0x33, 0xFF ) }
	};

	// the colours of a material drawn from a palette: the theme's roles of its five colours, in the order of the albedo map's weights (red, green, blue,
	// alpha, and the rest; Keep for black), how much each is scaled, its five colours in 1986, the role of its glow and how bright it is, and its 1986 glow
	public struct PaletteColours
	{
		public string m_name;
		public UIKitTheme.ColorRole[] m_roles;
		public float[] m_scales;
		public Color32[] m_originals;
		public UIKitTheme.ColorRole m_glow;
		public float m_glowStrength;
		public Color32 m_originalGlow;
	}

	// the concourse's floor: the floor, the cyan band round the star (which glows in the accent), the dark behind the door mats, the door mats, and the star;
	// in 1986 light cyan, with no band of its own (the band is floor) and nothing glowing, door mats of dark grey, and a light blue star. The bay's doors:
	// the metal (its grey times the kit's blue grey, lifted so that the doors stay about as light as they were) and Interstel's logo (the kit's amber,
	// glowing); before the kit the white metal of their texture and a yellow logo that did not glow
	public static readonly PaletteColours[] c_palettes =
	{
		new PaletteColours
		{
			m_name = "Starport Floor",
			m_roles = new[] { UIKitTheme.ColorRole.BackgroundHover, UIKitTheme.ColorRole.AccentPrimary, UIKitTheme.ColorRole.BackgroundApp, UIKitTheme.ColorRole.BorderDefault, UIKitTheme.ColorRole.BackgroundPanel },
			m_scales = new[] { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f },
			m_originals = new[] { new Color32( 0x55, 0xFF, 0xFF, 0xFF ), new Color32( 0x55, 0xFF, 0xFF, 0xFF ), new Color32( 0x00, 0x00, 0x00, 0xFF ), new Color32( 0x55, 0x55, 0x55, 0xFF ), new Color32( 0x55, 0x55, 0xFF, 0xFF ) },
			m_glow = UIKitTheme.ColorRole.AccentPrimary,
			m_glowStrength = 0.9f
		},
		new PaletteColours
		{
			m_name = "Docking Bay Doors",
			m_roles = new[] { UIKitTheme.ColorRole.TextSecondary, UIKitTheme.ColorRole.AccentSecondary, UIKitTheme.ColorRole.Keep, UIKitTheme.ColorRole.Keep, UIKitTheme.ColorRole.Keep },
			m_scales = new[] { 1.35f, 1.0f, 1.0f, 1.0f, 1.0f },
			m_originals = new[] { new Color32( 0xFF, 0xFF, 0xFF, 0xFF ), new Color32( 0xFF, 0xDE, 0x00, 0xFF ), new Color32( 0x00, 0x00, 0x00, 0xFF ), new Color32( 0x00, 0x00, 0x00, 0xFF ), new Color32( 0x00, 0x00, 0x00, 0xFF ) },
			m_glow = UIKitTheme.ColorRole.AccentSecondary,
			m_glowStrength = 0.6f
		}
	};

	// the shader's properties and keywords
	public const string c_albedoColour = "SF_AlbedoColor";
	public const string c_emissiveColour = "SF_EmissiveColor";
	public const string c_emissiveTint = "SF_EmissiveTint";
	public const string c_emissiveColourTint = "SF_EmissiveColorTint";
	public const string c_paletteColour = "SF_PaletteColor";
	public const string c_paletteKeyword = "SF_PALETTEMAP_ON";
	public const string c_emissiveColourTintKeyword = "SF_EMISSIVECOLORTINT_ON";

	// the schemes of the original games, in which the station and the bay have their 1986 colours rather than their kit's roles
	public static readonly string[] c_originalSchemes = { "Starflight", "Starflight 2" };

	// the renderers that draw with the materials, in the scenes that are loaded
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

	// finds the renderers of a scene that has loaded that draw with one of the materials (hidden ones too), and gives them the scheme's colours
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

	// whether a renderer draws with one of the materials
	static bool DrawsTheStation( Renderer stationRenderer )
	{
		foreach ( var material in stationRenderer.sharedMaterials )
		{
			if ( ( material != null ) && ( ( FindPalette( material.name ) >= 0 ) || ( Find( material.name ) >= 0 ) ) )
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

	// the index in c_palettes of a material's palette, or -1
	public static int FindPalette( string materialName )
	{
		for ( var i = 0; i < c_palettes.Length; i++ )
		{
			if ( c_palettes[ i ].m_name == materialName )
			{
				return i;
			}
		}

		return -1;
	}

	// the albedo and the glow of a material in a theme, or in 1986
	public static void GetColours( MaterialColours colours, UIKitTheme theme, bool original, out Color albedo, out Color glow )
	{
		if ( original )
		{
			albedo = colours.m_original;
			glow = colours.m_originalGlow;
			glow.a = 1.0f;

			return;
		}

		albedo = theme.GetColor( colours.m_albedo );

		if ( colours.m_albedoMix != UIKitTheme.ColorRole.Keep )
		{
			albedo = Color.Lerp( albedo, theme.GetColor( colours.m_albedoMix ), colours.m_mix );
		}

		glow = ( colours.m_glow == UIKitTheme.ColorRole.Keep ) ? Color.black : theme.GetColor( colours.m_glow ) * colours.m_glowStrength;
	}

	// one of a palette's five colours in a theme, or in 1986
	public static Color GetPaletteColour( PaletteColours palette, int index, UIKitTheme theme, bool original )
	{
		if ( original )
		{
			return palette.m_originals[ index ];
		}

		var colour = ( palette.m_roles[ index ] == UIKitTheme.ColorRole.Keep ) ? Color.black : theme.GetColor( palette.m_roles[ index ] ) * palette.m_scales[ index ];

		colour.a = 1.0f;

		return colour;
	}

	// a palette's glow in a theme, or in 1986
	public static Color GetPaletteGlow( PaletteColours palette, UIKitTheme theme, bool original )
	{
		if ( original )
		{
			Color glow = palette.m_originalGlow;

			glow.a = 1.0f;

			return glow;
		}

		return ( palette.m_glow == UIKitTheme.ColorRole.Keep ) ? Color.black : theme.GetColor( palette.m_glow ) * palette.m_glowStrength;
	}

	// whether the scheme in use is one of the original games', in which the station and the bay have their 1986 colours
	public static bool UsesOriginalColours()
	{
		var scheme = UIKitTheme.GetScheme();

		if ( ( scheme < 0 ) || ( scheme >= UIKitTheme.c_schemes.Length ) )
		{
			return false;
		}

		return System.Array.IndexOf( c_originalSchemes, UIKitTheme.c_schemes[ scheme ].m_name ) >= 0;
	}

	// gives the renderers the colours of the scheme in use, in its dark mode (or their 1986 colours, in the original games' schemes)
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

				var paletteIndex = FindPalette( materials[ i ].name );
				var materialIndex = ( paletteIndex < 0 ) ? Find( materials[ i ].name ) : -1;

				if ( ( paletteIndex < 0 ) && ( materialIndex < 0 ) )
				{
					continue;
				}

				// a renderer whose emissive colour an animation sets (the sirens) takes its colours in the renderer's own block, the one the animation sets the
				// colour in: a block for one of its materials would hide the animated colour; the others in the block for the material
				var animated = ( materialIndex >= 0 ) && c_materials[ materialIndex ].m_glowTints;

				// keep what else the block holds
				if ( animated )
				{
					stationRenderer.GetPropertyBlock( s_propertyBlock );
				}
				else
				{
					stationRenderer.GetPropertyBlock( s_propertyBlock, i );
				}

				if ( paletteIndex >= 0 )
				{
					// the palette its weights are of, and its glow
					var palette = c_palettes[ paletteIndex ];

					for ( var p = 0; p < palette.m_roles.Length; p++ )
					{
						s_propertyBlock.SetColor( c_paletteColour + p, GetPaletteColour( palette, p, theme, original ) );
					}

					s_propertyBlock.SetColor( c_emissiveTint, GetPaletteGlow( palette, theme, original ) );
				}
				else
				{
					var colours = c_materials[ materialIndex ];

					GetColours( colours, theme, original, out var albedo, out var glow );

					// (the material's own opacity stays)
					albedo.a = materials[ i ].GetColor( c_albedoColour ).a;

					s_propertyBlock.SetColor( c_albedoColour, albedo );

					// a glow that tints the emissive colour its animations give it, or the emissive colour itself
					s_propertyBlock.SetColor( colours.m_glowTints ? c_emissiveColourTint : c_emissiveColour, glow );

					// the lights of a siren shine in its glow's colour (its animations change only how bright they are)
					if ( colours.m_glowTints )
					{
						glow.a = 1.0f;

						foreach ( var sirenLight in stationRenderer.GetComponentsInChildren<Light>( true ) )
						{
							sirenLight.color = glow;
						}
					}
				}

				if ( animated )
				{
					stationRenderer.SetPropertyBlock( s_propertyBlock );
				}
				else
				{
					stationRenderer.SetPropertyBlock( s_propertyBlock, i );
				}
			}
		}
	}
}
