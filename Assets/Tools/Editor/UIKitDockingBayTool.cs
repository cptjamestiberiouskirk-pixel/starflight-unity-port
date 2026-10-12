
using System.IO;
using UnityEditor;
using UnityEngine;

// gives the docking bay (the hangar the ship waits in before launch, in the Spaceflight scene) the colours of the UI kit, as the Starport concourse has them
// (the owner's choice of 2026-10-12 for the concourse: the kit's colours with a soft glow, the same geometry): blue grey metal instead of grey,
// Interstel's logo on the doors in the kit's amber and glowing, and amber sirens
public static class UIKitDockingBayTool
{
	const string c_folder = "Assets/Game Objects/Docking Bay/";

	// the original door texture (kept: the kit's textures are made from it) and the two the kit's doors use
	const string c_doorsSource = c_folder + "Docking Bay Doors - Albedo.png";
	const string c_doorsAlbedo = c_folder + "Docking Bay Doors - Kit Albedo.png";
	const string c_doorsEmissive = c_folder + "Docking Bay Doors - Kit Emissive.png";

	// how bright the glows are (the emissive colour is the kit's colour times this)
	const float c_logoGlow = 0.6f;
	const float c_sirenGlow = 1.0f;

	// how much the door metal is lifted after its grey is multiplied by the kit's blue grey (which takes about a quarter of its light)
	const float c_metalLift = 1.35f;

	[MenuItem( "Starflight Remake/UI Kit/Apply to the Docking Bay" )]
	public static void Apply()
	{
		Debug.Log( ApplyToDockingBay() );
	}

	// gives the docking bay's materials the kit's colours and makes the kit's door textures, and returns a report
	public static string ApplyToDockingBay()
	{
		var report = new System.Text.StringBuilder( "UI kit applied to the docking bay\n" );
		var theme = UIKitTheme.Get();

		if ( theme == null )
		{
			return "missing the UI kit theme";
		}

		// the walls: white (shaded by their occlusion map) becomes a light blue grey, between the kit's primary and secondary text colours (the secondary alone made the bay much darker than it was)
		SetColours( "Docking Bay Walls", Color.Lerp( theme.m_textPrimary, theme.m_textSecondary, 0.5f ), Color.black, report );

		// the sirens: an amber dome that glows on a base in the kit's border blue
		SetColours( "Docking Bay Siren Dome", theme.m_accentSecondary, theme.m_accentSecondary * c_sirenGlow, report );
		SetColours( "Docking Bay Siren Base", theme.m_borderDefault, Color.black, report );

		// the launch and the landing light the sirens up in the original's orange: their keys take the kit's amber
		RetargetSirenKeys( "Docking Bay Launch", theme.m_accentSecondary, report );
		RetargetSirenKeys( "Docking Bay Landing", theme.m_accentSecondary, report );

		// the doors
		if ( MakeDoorTextures( theme, report ) )
		{
			var doors = AssetDatabase.LoadAssetAtPath<Material>( c_folder + "Docking Bay Doors.mat" );
			var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>( c_doorsAlbedo );
			var emissive = AssetDatabase.LoadAssetAtPath<Texture2D>( c_doorsEmissive );

			if ( ( doors != null ) && ( albedo != null ) && ( emissive != null ) )
			{
				Undo.RecordObject( doors, "UI kit" );

				doors.SetTexture( "_MainTex", albedo );
				doors.SetTexture( "SF_EmissiveMap", emissive );
				doors.SetColor( "SF_EmissiveTint", theme.m_accentSecondary * c_logoGlow );
				doors.SetColor( "SF_EmissiveColor", Color.black );
				doors.EnableKeyword( "SF_EMISSIVEMAP_ON" );

				EditorUtility.SetDirty( doors );

				report.AppendLine( "Docking Bay Doors: the kit's textures, the logo glowing" );
			}
			else
			{
				report.AppendLine( "Docking Bay Doors: not set (material or textures missing)" );
			}
		}

		AssetDatabase.SaveAssets();

		return report.ToString();
	}

	// gives the sirens' emissive colour keys in an animation this colour: at each key the red channel tells how lit the siren is (the original's orange is
	// full red, so 0 is off and 1 is lit), and the green and blue keys become the colour's green and blue that far lit (the red and the alpha stay)
	static void RetargetSirenKeys( string clipName, Color colour, System.Text.StringBuilder report )
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
				keys[ i ].value = ( green ? colour.g : colour.b ) * Mathf.Clamp01( red.Evaluate( keys[ i ].time ) );
			}

			curve.keys = keys;

			AnimationUtility.SetEditorCurve( clip, binding, curve );

			retargeted++;
		}

		EditorUtility.SetDirty( clip );

		report.AppendLine( clipName + ": " + retargeted + " siren colour curves in the kit's amber" );
	}

	// sets a material's albedo (and the standard colour, which the editor shows) and its emissive colour
	static void SetColours( string name, Color albedo, Color emissive, System.Text.StringBuilder report )
	{
		var material = AssetDatabase.LoadAssetAtPath<Material>( c_folder + name + ".mat" );

		if ( material == null )
		{
			report.AppendLine( name + ": not found" );
			return;
		}

		Undo.RecordObject( material, "UI kit" );

		material.SetColor( "SF_AlbedoColor", albedo );
		material.SetColor( "_Color", albedo );
		material.SetColor( "SF_EmissiveColor", emissive );

		EditorUtility.SetDirty( material );

		report.AppendLine( name + ": #" + ColorUtility.ToHtmlStringRGB( albedo ) + ( ( emissive.maxColorComponent > 0.0f ) ? ", glowing" : "" ) );
	}

	// makes the kit's door textures from the original: the grey metal (and its dark stencils) keeps its shading in the kit's blue grey, and the yellow logo
	// becomes the kit's amber, which glows; a pixel between the two keeps its blend
	static bool MakeDoorTextures( UIKitTheme theme, System.Text.StringBuilder report )
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
		var albedo = new Color32[ count ];
		var emissive = new Color32[ count ];
		var metal = theme.m_textSecondary;
		var logo = theme.m_accentSecondary;
		var logoPixels = 0;

		for ( var i = 0; i < count; i++ )
		{
			Color c = pixels[ i ];

			// how yellow the pixel is: red and green above blue (0 for grey, 1 for the logo's yellow)
			var yellow = Mathf.Clamp01( ( Mathf.Min( c.r, c.g ) - c.b ) / 0.4f );
			var lightness = Mathf.Max( c.r, Mathf.Max( c.g, c.b ) );

			// the metal: its grey times the kit's blue grey, lifted so that the doors stay about as light as they were
			var metalColour = new Color( Mathf.Clamp01( c.grayscale * metal.r * c_metalLift ), Mathf.Clamp01( c.grayscale * metal.g * c_metalLift ), Mathf.Clamp01( c.grayscale * metal.b * c_metalLift ), 1.0f );

			// the logo: the kit's amber, as light as the yellow was
			var logoColour = new Color( logo.r * lightness, logo.g * lightness, logo.b * lightness, 1.0f );

			albedo[ i ] = Color.Lerp( metalColour, logoColour, yellow );
			emissive[ i ] = Color.Lerp( Color.black, Color.white, yellow * lightness );

			if ( yellow > 0.5f )
			{
				logoPixels++;
			}
		}

		WriteTexture( c_doorsAlbedo, albedo, width, height );
		WriteTexture( c_doorsEmissive, emissive, width, height );

		report.AppendLine( "door textures made: " + width + " x " + height + " (logo " + logoPixels + " pixels)" );

		return true;
	}

	// writes a texture as a PNG, imports it, and gives it the import settings of the original door texture
	static void WriteTexture( string path, Color32[] pixels, int width, int height )
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
			importer.textureCompression = original.textureCompression;
			importer.SaveAndReimport();
		}
	}
}
