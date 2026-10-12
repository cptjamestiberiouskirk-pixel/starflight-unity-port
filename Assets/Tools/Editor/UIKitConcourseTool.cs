
using System.IO;
using UnityEditor;
using UnityEngine;

// gives the Starport concourse (the 3D hall the astronaut walks in, and the station seen from outside at launch) the colours of the UI kit:
// the materials take the theme's colours instead of the original's EGA colours, and the floor gets a navy texture with a glowing cyan outline
// around its star, made from the original floor texture (the owner's choice of 2026-10-12: the kit's colours with a soft glow, the same geometry)
public static class UIKitConcourseTool
{
	const string c_folder = "Assets/Game Objects/Starport/";

	// the original floor texture (kept: the kit's textures are made from it) and the two the kit's floor uses
	const string c_floorSource = c_folder + "Starport Floor - Albedo.png";
	const string c_floorAlbedo = c_folder + "Starport Floor - Kit Albedo.png";
	const string c_floorEmissive = c_folder + "Starport Floor - Kit Emissive.png";

	// how bright the glows are (the emissive colour is the kit's colour times this)
	const float c_outlineGlow = 0.9f;
	const float c_doorGlow = 0.3f;
	const float c_padEdgeGlow = 0.5f;

	[MenuItem( "Starflight Remake/UI Kit/Apply to the Starport Concourse" )]
	public static void Apply()
	{
		Debug.Log( ApplyToConcourse() );
	}

	// gives the concourse's materials the kit's colours and makes the kit's floor textures, and returns a report
	public static string ApplyToConcourse()
	{
		var report = new System.Text.StringBuilder( "UI kit applied to the Starport concourse\n" );
		var theme = UIKitTheme.Get();

		if ( theme == null )
		{
			return "missing the UI kit theme";
		}

		// the hull: the original's black, dark grey and light grey become the kit's navy, its default border and its secondary text colour
		SetColours( "Starport Hull - Black", theme.m_backgroundPanel, Color.black, report );
		SetColours( "Starport Hull - Dark", theme.m_borderDefault, Color.black, report );
		SetColours( "Starport Hull - Light", theme.m_textSecondary, Color.black, report );

		// the hull's edging is a grey texture: the kit's blue grey tints it like the rest of the hull (Interstel's logo keeps its colours)
		SetColours( "Starport Hull - Edge", theme.m_textSecondary, Color.black, report );

		// the wall inside the ring: the original's blue becomes the steel blue of the sensor window
		SetColours( "Starport Wall", theme.m_sensorWindow, Color.black, report );

		// the doors: the original's red becomes a dim cyan that glows softly
		SetColours( "Starport Doors", theme.m_accentPrimaryDim, theme.m_accentPrimary * c_doorGlow, report );

		// the transporter pad: navy inside, the kit's border around it, its edge amber and glowing (it glowed brown)
		SetColours( "Starport Pad - Inside", theme.m_backgroundPanel, Color.black, report );
		SetColours( "Starport Pad - Outside", theme.m_borderDefault, Color.black, report );
		SetColours( "Starport Pad - Edge", theme.m_accentSecondary, theme.m_accentSecondary * c_padEdgeGlow, report );

		// the floor
		if ( MakeFloorTextures( theme, report ) )
		{
			var floor = AssetDatabase.LoadAssetAtPath<Material>( c_folder + "Starport Floor.mat" );
			var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>( c_floorAlbedo );
			var emissive = AssetDatabase.LoadAssetAtPath<Texture2D>( c_floorEmissive );

			if ( ( floor != null ) && ( albedo != null ) && ( emissive != null ) )
			{
				Undo.RecordObject( floor, "UI kit" );

				floor.SetTexture( "_MainTex", albedo );
				floor.SetTexture( "SF_EmissiveMap", emissive );
				floor.SetColor( "SF_EmissiveTint", theme.m_accentPrimary * c_outlineGlow );
				floor.SetColor( "SF_EmissiveColor", Color.black );
				floor.EnableKeyword( "SF_EMISSIVEMAP_ON" );

				EditorUtility.SetDirty( floor );

				report.AppendLine( "Starport Floor: the kit's textures, the star's outline glowing" );
			}
			else
			{
				report.AppendLine( "Starport Floor: not set (material or textures missing)" );
			}
		}

		AssetDatabase.SaveAssets();

		return report.ToString();
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

	// makes the kit's floor textures from the original: its teal floor becomes navy, its blue star a darker navy, the teal band around the star (the teal that
	// is not joined to the floor) cyan and glowing, and the grey door mats the kit's border colour; the edges between them keep their blend
	static bool MakeFloorTextures( UIKitTheme theme, System.Text.StringBuilder report )
	{
		if ( !File.Exists( c_floorSource ) )
		{
			report.AppendLine( "missing " + c_floorSource );
			return false;
		}

		var source = new Texture2D( 2, 2, TextureFormat.RGBA32, false );

		if ( !source.LoadImage( File.ReadAllBytes( c_floorSource ) ) )
		{
			report.AppendLine( "could not read " + c_floorSource );
			return false;
		}

		var width = source.width;
		var height = source.height;
		var pixels = source.GetPixels32();

		Object.DestroyImmediate( source );

		// the original's two colours: the floor (a corner) and the star (the centre)
		Color teal = pixels[ 0 ];
		Color blue = pixels[ ( height / 2 ) * width + ( width / 2 ) ];
		var span = new Vector3( blue.r - teal.r, blue.g - teal.g, blue.b - teal.b );
		var spanLength = Vector3.Dot( span, span );

		if ( spanLength <= 0.0f )
		{
			report.AppendLine( "the floor texture's corner and centre are the same colour" );
			return false;
		}

		// how far each pixel is from the teal towards the blue (0 teal, 1 blue), and which pixels are grey (the door mats)
		var count = width * height;
		var towardsBlue = new float[ count ];
		var grey = new bool[ count ];

		for ( var i = 0; i < count; i++ )
		{
			Color c = pixels[ i ];

			grey[ i ] = ( Mathf.Max( c.r, Mathf.Max( c.g, c.b ) ) - Mathf.Min( c.r, Mathf.Min( c.g, c.b ) ) ) < 0.1f;
			towardsBlue[ i ] = Mathf.Clamp01( Vector3.Dot( new Vector3( c.r - teal.r, c.g - teal.g, c.b - teal.b ), span ) / spanLength );
		}

		// the floor: the teal joined to the corner (a flood fill); the teal that is not joined to it is the band around the star
		var floor = new bool[ count ];
		var queue = new int[ count ];
		var head = 0;
		var tail = 0;

		floor[ 0 ] = true;
		queue[ tail++ ] = 0;

		while ( head < tail )
		{
			var p = queue[ head++ ];
			var x = p % width;
			var y = p / width;

			for ( var n = 0; n < 4; n++ )
			{
				var nx = x + ( ( n == 0 ) ? 1 : ( n == 1 ) ? -1 : 0 );
				var ny = y + ( ( n == 2 ) ? 1 : ( n == 3 ) ? -1 : 0 );

				if ( ( nx < 0 ) || ( ny < 0 ) || ( nx >= width ) || ( ny >= height ) )
				{
					continue;
				}

				var q = ny * width + nx;

				if ( !floor[ q ] && !grey[ q ] && ( towardsBlue[ q ] < 0.5f ) )
				{
					floor[ q ] = true;
					queue[ tail++ ] = q;
				}
			}
		}

		// the band: teal that is not floor, and the pixels within 2 of it (so that its blended edges are cyan on their teal side)
		var band = new bool[ count ];

		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				var i = y * width + x;

				if ( floor[ i ] || grey[ i ] || ( towardsBlue[ i ] >= 0.5f ) )
				{
					continue;
				}

				for ( var dy = -2; dy <= 2; dy++ )
				{
					for ( var dx = -2; dx <= 2; dx++ )
					{
						var nx = x + dx;
						var ny = y + dy;

						if ( ( nx >= 0 ) && ( ny >= 0 ) && ( nx < width ) && ( ny < height ) && !floor[ ny * width + nx ] )
						{
							band[ ny * width + nx ] = true;
						}
					}
				}
			}
		}

		var floorColour = theme.m_backgroundHover;
		var starColour = theme.m_backgroundPanel;
		var bandColour = theme.m_accentPrimary;
		var matColour = theme.m_borderDefault;
		var albedo = new Color32[ count ];
		var emissive = new Color32[ count ];

		for ( var i = 0; i < count; i++ )
		{
			if ( grey[ i ] )
			{
				// a door mat: its lightness from black to the original's grey becomes the app's background to the kit's border
				Color c = pixels[ i ];

				albedo[ i ] = Color.Lerp( theme.m_backgroundApp, matColour, Mathf.Clamp01( c.grayscale / 0.333f ) );
				emissive[ i ] = Color.black;
				continue;
			}

			var tealSide = band[ i ] ? bandColour : floorColour;

			albedo[ i ] = Color.Lerp( tealSide, starColour, towardsBlue[ i ] );
			emissive[ i ] = band[ i ] ? Color.Lerp( Color.white, Color.black, towardsBlue[ i ] ) : Color.black;
		}

		WriteTexture( c_floorAlbedo, albedo, width, height, true );
		WriteTexture( c_floorEmissive, emissive, width, height, true );

		report.AppendLine( "floor textures made: " + width + " x " + height + " (floor " + tail + " pixels)" );

		return true;
	}

	// writes a texture as a PNG, imports it, and gives it the import settings of the original floor texture
	static void WriteTexture( string path, Color32[] pixels, int width, int height, bool sRGB )
	{
		var texture = new Texture2D( width, height, TextureFormat.RGBA32, false );

		texture.SetPixels32( pixels );
		texture.Apply();

		File.WriteAllBytes( path, texture.EncodeToPNG() );

		Object.DestroyImmediate( texture );

		AssetDatabase.ImportAsset( path, ImportAssetOptions.ForceSynchronousImport );

		var importer = AssetImporter.GetAtPath( path ) as TextureImporter;
		var original = AssetImporter.GetAtPath( c_floorSource ) as TextureImporter;

		if ( ( importer != null ) && ( original != null ) )
		{
			importer.sRGBTexture = sRGB;
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
