
using System.IO;
using UnityEditor;
using UnityEngine;

// gives the docking bay (the hangar the ship waits in before launch, in the Spaceflight scene) the colours of the UI kit, as the Starport concourse has them
// (the kit's colours with a soft glow on the same geometry, chosen on 2026-10-12): blue grey metal instead of grey, Interstel's logo on the doors in the
// kit's amber and glowing, and amber sirens; the materials are given the classic dark theme's colours, which the scenes are saved in, and StarportColours
// gives the bay the colours of the scheme in use at run time (its table of which colour goes where is the one used here)
public static class UIKitDockingBayTool
{
	const string c_folder = "Assets/Game Objects/Docking Bay/";

	// the original door texture (kept: the kit's textures are made from it) and the two the kit's doors use: the weights of the palette's metal and logo
	// (their lightness, as the original texture has it) and the glow of the logo
	const string c_doorsSource = c_folder + "Docking Bay Doors - Albedo.png";
	const string c_doorsWeights = c_folder + "Docking Bay Doors - Kit Weights.png";
	const string c_doorsEmissive = c_folder + "Docking Bay Doors - Kit Emissive.png";

	// the animations that light the sirens up
	static readonly string[] c_sirenClips = { "Docking Bay Prelaunch", "Docking Bay Launch", "Docking Bay Landing" };

	[MenuItem( "Starflight Remake/UI Kit/Apply to the Docking Bay" )]
	public static void Apply()
	{
		Debug.Log( ApplyToDockingBay() );
	}

	// gives the docking bay's materials the classic dark theme's colours and makes the kit's door textures, and returns a report
	public static string ApplyToDockingBay()
	{
		var report = new System.Text.StringBuilder( "UI kit applied to the docking bay\n" );
		var theme = UIKitTheme.Load( UIKitTheme.Mode.Dark );

		if ( theme == null )
		{
			return "missing the UI kit theme";
		}

		// the walls (white, shaded by their occlusion map, become a light blue grey between the kit's two text colours: the secondary alone made the bay much
		// darker than it was), and the sirens (an amber dome that glows on a base in the kit's border blue)
		foreach ( var colours in StarportColours.c_materials )
		{
			if ( !colours.m_name.StartsWith( "Docking Bay" ) )
			{
				continue;
			}

			StarportColours.GetColours( colours, theme, false, out var albedo, out var glow );

			SetColours( colours, albedo, glow, report );
		}

		// the launch and the landing light the sirens up: their keys say how lit, and the dome's tint gives the colour
		foreach ( var clipName in c_sirenClips )
		{
			MakeSirenKeysGrey( clipName, report );
		}

		// the doors
		var palette = StarportColours.c_palettes[ StarportColours.FindPalette( "Docking Bay Doors" ) ];

		if ( MakeDoorTextures( report ) )
		{
			var doors = AssetDatabase.LoadAssetAtPath<Material>( c_folder + palette.m_name + ".mat" );
			var weights = AssetDatabase.LoadAssetAtPath<Texture2D>( c_doorsWeights );
			var emissive = AssetDatabase.LoadAssetAtPath<Texture2D>( c_doorsEmissive );

			if ( ( doors != null ) && ( weights != null ) && ( emissive != null ) )
			{
				Undo.RecordObject( doors, "UI kit" );

				// the albedo map holds the palette's weights, and the palette is the theme's colours
				doors.SetTexture( "_MainTex", weights );
				doors.EnableKeyword( StarportColours.c_paletteKeyword );

				for ( var p = 0; p < palette.m_roles.Length; p++ )
				{
					doors.SetColor( StarportColours.c_paletteColour + p, StarportColours.GetPaletteColour( palette, p, theme, false ) );
				}

				doors.SetTexture( "SF_EmissiveMap", emissive );
				doors.SetColor( StarportColours.c_emissiveTint, StarportColours.GetPaletteGlow( palette, theme, false ) );
				doors.SetColor( StarportColours.c_emissiveColour, Color.black );
				doors.EnableKeyword( "SF_EMISSIVEMAP_ON" );

				EditorUtility.SetDirty( doors );

				report.AppendLine( "Docking Bay Doors: the palette's weights and the theme's colours, the logo glowing" );
			}
			else
			{
				report.AppendLine( "Docking Bay Doors: not set (material or textures missing)" );
			}
		}

		AssetDatabase.SaveAssets();

		return report.ToString();
	}

	// makes the sirens' emissive colour keys in an animation grey: at each key the red channel tells how lit the siren is (0 off, 1 lit), and the green and
	// blue keys become as lit, so that the dome's emissive colour tint (SF_EMISSIVECOLORTINT_ON) gives the colour (the alpha stays)
	static void MakeSirenKeysGrey( string clipName, System.Text.StringBuilder report )
	{
		var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>( c_folder + clipName + ".anim" );

		if ( clip == null )
		{
			report.AppendLine( clipName + ": not found" );
			return;
		}

		Undo.RecordObject( clip, "UI kit" );

		var retargeted = 0;

		foreach ( var binding in AnimationUtility.GetCurveBindings( clip ) )
		{
			var green = binding.propertyName == "material.SF_EmissiveColor.g";
			var blue = binding.propertyName == "material.SF_EmissiveColor.b";

			if ( !green && !blue )
			{
				continue;
			}

			var redBinding = binding;

			redBinding.propertyName = "material.SF_EmissiveColor.r";

			var red = AnimationUtility.GetEditorCurve( clip, redBinding );
			var curve = AnimationUtility.GetEditorCurve( clip, binding );

			if ( ( red == null ) || ( curve == null ) )
			{
				continue;
			}

			var keys = curve.keys;

			for ( var i = 0; i < keys.Length; i++ )
			{
				keys[ i ].value = Mathf.Clamp01( red.Evaluate( keys[ i ].time ) );
			}

			curve.keys = keys;

			AnimationUtility.SetEditorCurve( clip, binding, curve );

			retargeted++;
		}

		EditorUtility.SetDirty( clip );

		report.AppendLine( clipName + ": " + retargeted + " siren colour curves grey" );
	}

	// sets a material's albedo (and the standard colour, which the editor shows) and its glow: its emissive colour, or for a material whose glow tints the
	// emissive colour its animations give it, a lit emissive colour (white) and the glow as its tint
	static void SetColours( StarportColours.MaterialColours colours, Color albedo, Color glow, System.Text.StringBuilder report )
	{
		var material = AssetDatabase.LoadAssetAtPath<Material>( c_folder + colours.m_name + ".mat" );

		if ( material == null )
		{
			report.AppendLine( colours.m_name + ": not found" );
			return;
		}

		Undo.RecordObject( material, "UI kit" );

		material.SetColor( StarportColours.c_albedoColour, albedo );
		material.SetColor( "_Color", albedo );

		if ( colours.m_glowTints )
		{
			material.SetColor( StarportColours.c_emissiveColour, Color.white );
			material.SetColor( StarportColours.c_emissiveColourTint, glow );
			material.EnableKeyword( StarportColours.c_emissiveColourTintKeyword );
		}
		else
		{
			material.SetColor( StarportColours.c_emissiveColour, glow );
		}

		EditorUtility.SetDirty( material );

		report.AppendLine( colours.m_name + ": #" + ColorUtility.ToHtmlStringRGB( albedo ) + ( ( glow.maxColorComponent > 0.0f ) ? ( colours.m_glowTints ? ", glowing as lit" : ", glowing" ) : "" ) );
	}

	// makes the kit's door textures from the original: the weights of the palette's metal (the grey metal and its dark stencils, which keep their shading
	// in the kit's blue grey) and logo (the yellow, which becomes the kit's amber and glows), each as light as the original was, so that a pixel between
	// the two keeps its blend; the weights are lightnesses as the original texture has them, so the texture is imported as the original is
	static bool MakeDoorTextures( System.Text.StringBuilder report )
	{
		if ( !File.Exists( c_doorsSource ) )
		{
			report.AppendLine( "missing " + c_doorsSource );
			return false;
		}

		var source = new Texture2D( 2, 2, TextureFormat.RGBA32, false );

		if ( !source.LoadImage( File.ReadAllBytes( c_doorsSource ) ) )
		{
			report.AppendLine( "could not read " + c_doorsSource );
			return false;
		}

		var width = source.width;
		var height = source.height;
		var pixels = source.GetPixels32();

		Object.DestroyImmediate( source );

		var count = width * height;
		var weights = new Color32[ count ];
		var emissive = new Color32[ count ];
		var logoPixels = 0;

		for ( var i = 0; i < count; i++ )
		{
			Color c = pixels[ i ];

			// how yellow the pixel is: red and green above blue (0 for grey, 1 for the logo's yellow)
			var yellow = Mathf.Clamp01( ( Mathf.Min( c.r, c.g ) - c.b ) / 0.4f );
			var lightness = Mathf.Max( c.r, Mathf.Max( c.g, c.b ) );

			// the metal as grey as the original, the logo as light as its yellow (the palette's red and green weights; the others are not used)
			weights[ i ] = new Color( ( 1.0f - yellow ) * c.grayscale, yellow * lightness, 0.0f, 0.0f );
			emissive[ i ] = Color.Lerp( Color.black, Color.white, yellow * lightness );

			if ( yellow > 0.5f )
			{
				logoPixels++;
			}
		}

		WriteTexture( c_doorsWeights, weights, width, height, true );
		WriteTexture( c_doorsEmissive, emissive, width, height, false );

		report.AppendLine( "door textures made: " + width + " x " + height + " (logo " + logoPixels + " pixels)" );

		return true;
	}

	// writes a texture as a PNG, imports it, and gives it the import settings of the original door texture (a texture of weights is compressed at high
	// quality, since an error in a weight shows as a colour that is not the scheme's)
	static void WriteTexture( string path, Color32[] pixels, int width, int height, bool weights )
	{
		var texture = new Texture2D( width, height, TextureFormat.RGBA32, false );

		texture.SetPixels32( pixels );
		texture.Apply();

		File.WriteAllBytes( path, texture.EncodeToPNG() );

		Object.DestroyImmediate( texture );

		AssetDatabase.ImportAsset( path, ImportAssetOptions.ForceSynchronousImport );

		var importer = AssetImporter.GetAtPath( path ) as TextureImporter;
		var original = AssetImporter.GetAtPath( c_doorsSource ) as TextureImporter;

		if ( ( importer != null ) && ( original != null ) )
		{
			importer.sRGBTexture = original.sRGBTexture;
			importer.mipmapEnabled = original.mipmapEnabled;
			importer.wrapMode = original.wrapMode;
			importer.filterMode = original.filterMode;
			importer.anisoLevel = original.anisoLevel;
			importer.maxTextureSize = original.maxTextureSize;
			importer.textureCompression = weights ? TextureImporterCompression.CompressedHQ : original.textureCompression;
			importer.SaveAndReimport();
		}
	}
}
