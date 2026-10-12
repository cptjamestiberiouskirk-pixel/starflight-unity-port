
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

// builds the UI kit's assets: TextMesh Pro font assets from the kit's fonts, the sprites' import settings, and the theme asset that ties them together
public static class UIKitBuilder
{
	const string c_fontFolder = "Assets/Fonts";
	const string c_spriteFolder = "Assets/UI Kit/Sprites";
	const string c_themePath = "Assets/Resources/" + UIKitTheme.c_resourceName + ".asset";

	// the characters every font asset holds: ASCII, Latin-1, and the signs the game shows (middle dot, times, superscripts for the masses, arrows)
	static string GetCharacters()
	{
		var characters = new System.Text.StringBuilder();

		for ( var c = 32; c < 127; c++ )
		{
			characters.Append( (char) c );
		}

		for ( var c = 160; c < 256; c++ )
		{
			characters.Append( (char) c );
		}

		characters.Append( "·×²³¹⁰⁴⁵⁶⁷⁸⁹‘’“”…•←↑→↓" );

		return characters.ToString();
	}

	// true while Remake Font Assets runs
	static bool s_remakeFonts;

	[MenuItem( "Starflight Remake/UI Kit/Remake Font Assets" )]
	public static void RemakeFontAssets()
	{
		s_remakeFonts = true;

		try
		{
			Rebuild();
		}
		finally
		{
			s_remakeFonts = false;
		}
	}

	[MenuItem( "Starflight Remake/UI Kit/Rebuild" )]
	public static void Rebuild()
	{
		var report = new System.Text.StringBuilder();

		// the font assets
		var orbitronBold = MakeFontAsset( c_fontFolder + "/Orbitron/Orbitron-Bold.ttf", report );
		var orbitronMedium = MakeFontAsset( c_fontFolder + "/Orbitron/Orbitron-Medium.ttf", report );
		var rajdhaniSemiBold = MakeFontAsset( c_fontFolder + "/Rajdhani/Rajdhani-SemiBold.ttf", report );
		var rajdhaniMedium = MakeFontAsset( c_fontFolder + "/Rajdhani/Rajdhani-Medium.ttf", report );
		var shareTechMono = MakeFontAsset( c_fontFolder + "/Share Tech Mono/ShareTechMono-Regular.ttf", report );

		// the sprites
		var panel = ImportSprite( "UI Kit - Panel.png", new Vector4( 10, 10, 10, 10 ), report );
		var panelOutline = ImportSprite( "UI Kit - Panel Outline.png", new Vector4( 10, 10, 10, 10 ), report );
		var glow = ImportSprite( "UI Kit - Glow.png", new Vector4( 60, 60, 60, 60 ), report );
		var bar = ImportSprite( "UI Kit - Bar.png", new Vector4( 8, 0, 8, 0 ), report );
		var diamond = ImportSprite( "UI Kit - Diamond.png", Vector4.zero, report );
		var diamondOutline = ImportSprite( "UI Kit - Diamond Outline.png", Vector4.zero, report );

		// the theme (kept if it exists, so colours tuned in the inspector stay)
		var theme = AssetDatabase.LoadAssetAtPath<UIKitTheme>( c_themePath );

		if ( theme == null )
		{
			theme = ScriptableObject.CreateInstance<UIKitTheme>();
			AssetDatabase.CreateAsset( theme, c_themePath );
		}

		// Orbitron for titles (the kit's SemiBold heading uses Bold: Orbitron has no static SemiBold), Rajdhani for labels and text, Share Tech Mono for figures
		theme.m_displayXL.m_font = orbitronBold;
		theme.m_headingL.m_font = orbitronBold;
		theme.m_headingM.m_font = orbitronMedium;
		theme.m_labelM.m_font = rajdhaniSemiBold;
		theme.m_labelS.m_font = rajdhaniSemiBold;
		theme.m_bodyM.m_font = rajdhaniMedium;
		theme.m_readoutL.m_font = shareTechMono;
		theme.m_readoutM.m_font = shareTechMono;

		theme.m_panelSprite = panel;
		theme.m_panelOutlineSprite = panelOutline;
		theme.m_glowSprite = glow;
		theme.m_barSprite = bar;
		theme.m_diamondSprite = diamond;
		theme.m_diamondOutlineSprite = diamondOutline;

		// the console button bullets take their colours from the theme: an outline for a button that is off, filled for the selected one, glowing for the one that runs
		theme.m_bulletOffSprite = MakeBullet( "UI Kit - Bullet Off.png", theme.m_textMuted, 0.0f, report );
		theme.m_bulletOnSprite = MakeBullet( "UI Kit - Bullet On.png", theme.m_accentPrimary, 1.0f, report );
		theme.m_bulletActiveSprite = MakeBullet( "UI Kit - Bullet Active.png", theme.m_accentPrimary, 2.0f, report );

		// the officer icons (the Figma file's Icon components, exported at 4x and made white: the console tints them)
		theme.m_officerCaptainSprite = ImportSprite( "UI Kit - Officer Captain.png", Vector4.zero, report );
		theme.m_officerScienceSprite = ImportSprite( "UI Kit - Officer Science.png", Vector4.zero, report );
		theme.m_officerNavigatorSprite = ImportSprite( "UI Kit - Officer Navigator.png", Vector4.zero, report );
		theme.m_officerEngineerSprite = ImportSprite( "UI Kit - Officer Engineer.png", Vector4.zero, report );
		theme.m_officerCommunicationsSprite = ImportSprite( "UI Kit - Officer Communications.png", Vector4.zero, report );
		theme.m_officerDoctorSprite = ImportSprite( "UI Kit - Officer Doctor.png", Vector4.zero, report );
		theme.m_officerTerrainVehicleSprite = ImportSprite( "UI Kit - Officer Terrain Vehicle.png", Vector4.zero, report );

		EditorUtility.SetDirty( theme );
		AssetDatabase.SaveAssets();

		Debug.Log( "UI kit rebuilt\n" + report );
	}

	// draws a console button bullet: a diamond 15 pixels from its centre to a corner (at 2x), in this colour
	// (fill 0 is an outline 3 pixels wide, 1 is filled, 2 is filled with a glow around it)
	static Sprite MakeBullet( string fileName, Color color, float fill, System.Text.StringBuilder report )
	{
		const int size = 48;
		const float centre = size / 2.0f;
		const float radius = 15.0f;
		var pixels = new Color32[ size * size ];

		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
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

				pixels[ y * size + x ] = c;
			}
		}

		var texture = new Texture2D( size, size, TextureFormat.RGBA32, false );

		texture.SetPixels32( pixels );
		texture.Apply();

		File.WriteAllBytes( c_spriteFolder + "/" + fileName, texture.EncodeToPNG() );
		Object.DestroyImmediate( texture );

		AssetDatabase.ImportAsset( c_spriteFolder + "/" + fileName );

		return ImportSprite( fileName, Vector4.zero, report );
	}

	// makes (or remakes) the TextMesh Pro font asset next to a font file, holding the kit's characters, and fixes it so that it never changes at runtime
	static TMP_FontAsset MakeFontAsset( string fontPath, System.Text.StringBuilder report )
	{
		var font = AssetDatabase.LoadAssetAtPath<Font>( fontPath );

		if ( font == null )
		{
			report.AppendLine( "missing font " + fontPath );
			return null;
		}

		var assetPath = Path.ChangeExtension( fontPath, null ) + " SDF.asset";
		var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>( assetPath );

		// an existing font asset is kept (remaking it rewrites 2 MB of atlas), unless the fonts are being remade on purpose
		if ( ( existing != null ) && !s_remakeFonts )
		{
			report.AppendLine( existing.name + ": kept" );
			return existing;
		}

		if ( existing != null )
		{
			AssetDatabase.DeleteAsset( assetPath );
		}

		// 56 point samples with 6 pixels of padding in a 1024 x 1024 atlas hold the whole set (the size of the project's other font assets)
		var fontAsset = TMP_FontAsset.CreateFontAsset( font, 56, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false );
		fontAsset.name = Path.GetFileNameWithoutExtension( assetPath );

		AssetDatabase.CreateAsset( fontAsset, assetPath );

		fontAsset.material.name = fontAsset.name + " Material";
		AssetDatabase.AddObjectToAsset( fontAsset.material, fontAsset );

		fontAsset.atlasTextures[ 0 ].name = fontAsset.name + " Atlas";
		AssetDatabase.AddObjectToAsset( fontAsset.atlasTextures[ 0 ], fontAsset );

		fontAsset.TryAddCharacters( GetCharacters(), out var missing );

		// static from now on: the atlas is complete, and a dynamic asset would be written again in every play session
		fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

		EditorUtility.SetDirty( fontAsset );
		AssetDatabase.SaveAssets();

		report.AppendLine( fontAsset.name + ": " + fontAsset.characterTable.Count + " characters" + ( string.IsNullOrEmpty( missing ) ? "" : ", not in the font: " + missing ) );

		return fontAsset;
	}

	// gives one of the kit's sprites its import settings: a sprite at 200 pixels per unit (drawn at twice the size), sliced by this border
	static Sprite ImportSprite( string fileName, Vector4 border, System.Text.StringBuilder report )
	{
		var path = c_spriteFolder + "/" + fileName;
		var importer = AssetImporter.GetAtPath( path ) as TextureImporter;

		if ( importer == null )
		{
			report.AppendLine( "missing sprite " + path );
			return null;
		}

		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		importer.spritePixelsPerUnit = 200.0f;
		importer.spriteBorder = border;
		importer.mipmapEnabled = false;
		importer.alphaIsTransparency = true;
		importer.wrapMode = TextureWrapMode.Clamp;
		importer.filterMode = FilterMode.Bilinear;
		importer.textureCompression = TextureImporterCompression.Uncompressed;
		importer.SaveAndReimport();

		var sprite = AssetDatabase.LoadAssetAtPath<Sprite>( path );

		report.AppendLine( fileName + ": " + ( ( sprite != null ) ? ( sprite.rect.width + " x " + sprite.rect.height + ", border " + sprite.border ) : "no sprite" ) );

		return sprite;
	}
}
