// Sets up the console's system map in Spaceflight.unity for the shaders of Assets/Shaders/System Map *.shader and the
// textures that build_system_map_art.py writes.
//
// wire.ps1 copies this file into Assets/, runs Unity in batch mode with
//   -executeMethod SystemMapWiring.Run
// and removes it again. It sets up the import of the two textures, makes the four materials, and in the scene: puts the
// star field (a RawImage) and the ecosphere band behind the map, gives each orbit a ring image in place of its ring mesh,
// gives the star, the planets and the ship marker their materials and sprite, puts the ship's sensor ping, wake and engine
// glow under the ship marker, clips the map to the display, and points SystemDisplay at the new objects.
// It then deletes the assets only the old map used (the ring mesh and its materials). Running it again gives the same scene.

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SystemMapWiring
{
	const string c_scenePath = "Assets/Scenes/Spaceflight.unity";
	const string c_folder = "Assets/Game Objects/Display/System Map";

	const string c_starFieldPath = c_folder + "/System Map Star Field.png";
	const string c_shipPath = c_folder + "/System Map Ship.png";
	const string c_glowPath = c_folder + "/System Map Glow.png";
	const string c_wakePath = c_folder + "/System Map Wake.png";

	// how many segments the ship's wake has
	const int c_wakeSegments = 12;

	// the ship sprite is 20 x 60 map units at 4 texels per unit, with the middle of the chevron 10 units above its bottom
	const float c_shipWidth = 20.0f;
	const float c_shipHeight = 60.0f;
	const float c_shipPivotY = 10.0f / 60.0f;

	// the assets only the old map used
	static readonly string[] c_oldAssets =
	{
		c_folder + "/System Map Orbit Ring.mat",
		c_folder + "/System Map Player.mat",
		c_folder + "/System Map.FBX",
	};

	const int c_uiLayer = 5;

	// the star field stops this many map units inside the display, so that the rounded corners and the soft edge of the console's screen stay
	const float c_starFieldInset = 2.0f;

	static void Log( string text )
	{
		Debug.Log( "[SystemMapWiring] " + text );
	}

	public static void Run()
	{
		var exitCode = 0;

		try
		{
			AssetDatabase.Refresh();

			SetUpTextures();

			var planetMaterial = MakeMaterial( "System Map Planet", "Custom/UI/System Map Planet" );
			var starMaterial = MakeMaterial( "System Map Star", "Custom/UI/System Map Star" );
			var orbitMaterial = MakeMaterial( "System Map Orbit", "Custom/UI/System Map Orbit" );
			var ecosphereMaterial = MakeMaterial( "System Map Ecosphere", "Custom/UI/System Map Ecosphere" );

			WireScene( planetMaterial, starMaterial, orbitMaterial, ecosphereMaterial );

			DeleteOldAssets();

			AssetDatabase.SaveAssets();
		}
		catch ( Exception exception )
		{
			Log( "FAILED " + exception );
			exitCode = 1;
		}

		EditorApplication.Exit( exitCode );
	}

	static void SetUpTextures()
	{
		// the star field tiles: each star shows its own part of it
		var starField = (TextureImporter) AssetImporter.GetAtPath( c_starFieldPath );

		if ( starField == null )
		{
			throw new Exception( "no star field texture at " + c_starFieldPath + " (run build_system_map_art.py first)" );
		}

		starField.textureType = TextureImporterType.Default;
		starField.sRGBTexture = true;
		starField.alphaSource = TextureImporterAlphaSource.None;
		starField.wrapMode = TextureWrapMode.Repeat;
		starField.filterMode = FilterMode.Trilinear;
		starField.mipmapEnabled = true;
		starField.maxTextureSize = 1024;
		starField.textureCompression = TextureImporterCompression.CompressedHQ;
		starField.SaveAndReimport();

		// the ship marker is a sprite that turns about the middle of its chevron
		var ship = (TextureImporter) AssetImporter.GetAtPath( c_shipPath );

		if ( ship == null )
		{
			throw new Exception( "no ship texture at " + c_shipPath + " (run build_system_map_art.py first)" );
		}

		ship.textureType = TextureImporterType.Sprite;
		ship.spriteImportMode = SpriteImportMode.Single;
		ship.sRGBTexture = true;
		ship.alphaIsTransparency = true;
		ship.wrapMode = TextureWrapMode.Clamp;
		ship.filterMode = FilterMode.Bilinear;
		ship.mipmapEnabled = true;
		ship.textureCompression = TextureImporterCompression.Uncompressed;

		var settings = new TextureImporterSettings();
		ship.ReadTextureSettings( settings );
		settings.spriteAlignment = (int) SpriteAlignment.Custom;
		settings.spritePivot = new Vector2( 0.5f, c_shipPivotY );
		settings.spritePixelsPerUnit = 4.0f;
		ship.SetTextureSettings( settings );
		ship.SaveAndReimport();

		// the engine glow and the segments of the wake are plain sprites, tinted in the game
		SetUpPlainSprite( c_glowPath );
		SetUpPlainSprite( c_wakePath );

		Log( "textures set up" );
	}

	// a white sprite, tinted in the game
	static void SetUpPlainSprite( string path )
	{
		var importer = (TextureImporter) AssetImporter.GetAtPath( path );

		if ( importer == null )
		{
			throw new Exception( "no texture at " + path + " (run build_system_map_art.py first)" );
		}

		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		importer.sRGBTexture = true;
		importer.alphaIsTransparency = true;
		importer.wrapMode = TextureWrapMode.Clamp;
		importer.filterMode = FilterMode.Bilinear;
		importer.mipmapEnabled = true;
		importer.textureCompression = TextureImporterCompression.Uncompressed;
		importer.SaveAndReimport();
	}

	static Material MakeMaterial( string name, string shaderName )
	{
		var shader = Shader.Find( shaderName );

		if ( shader == null )
		{
			throw new Exception( "no shader " + shaderName );
		}

		var path = c_folder + "/" + name + ".mat";
		var material = AssetDatabase.LoadAssetAtPath<Material>( path );

		// a material of an earlier map, made for another shader, is made again so that none of its old properties stay
		if ( ( material != null ) && ( material.shader != shader ) )
		{
			AssetDatabase.DeleteAsset( path );
			material = null;
		}

		if ( material == null )
		{
			material = new Material( shader );
			AssetDatabase.CreateAsset( material, path );
		}

		Log( "material " + path );

		return material;
	}

	static void WireScene( Material planetMaterial, Material starMaterial, Material orbitMaterial, Material ecosphereMaterial )
	{
		var scene = EditorSceneManager.OpenScene( c_scenePath, OpenSceneMode.Single );

		SystemDisplay systemDisplay = null;

		foreach ( var display in Resources.FindObjectsOfTypeAll<SystemDisplay>() )
		{
			if ( display.gameObject.scene == scene )
			{
				systemDisplay = display;
			}
		}

		if ( systemDisplay == null )
		{
			throw new Exception( "no SystemDisplay in " + c_scenePath );
		}

		var root = systemDisplay.transform;

		// everything on the map stays inside the display (a sensor ping that goes out at the edge of the system would spill over the console)
		if ( root.GetComponent<RectMask2D>() == null )
		{
			root.gameObject.AddComponent<RectMask2D>();
		}

		// the star field covers the display (inside the edge of the console's screen), behind everything
		var background = FindOrMake( root, "Star Field" ).GetComponent<RawImage>();

		if ( background == null )
		{
			background = FindOrMake( root, "Star Field" ).AddComponent<RawImage>();
		}

		background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>( c_starFieldPath );
		background.raycastTarget = false;
		background.color = Color.white;
		Stretch( background.rectTransform, c_starFieldInset );
		background.transform.SetSiblingIndex( 0 );

		// the ecosphere band, behind the star and the orbits
		var ecosphere = FindOrMake( root, "Ecosphere" ).GetComponent<Image>();

		if ( ecosphere == null )
		{
			ecosphere = FindOrMake( root, "Ecosphere" ).AddComponent<Image>();
		}

		ecosphere.sprite = null;
		ecosphere.material = ecosphereMaterial;
		ecosphere.raycastTarget = false;
		ecosphere.color = Color.white;
		Center( ecosphere.rectTransform, 450.0f );
		ecosphere.transform.SetSiblingIndex( 1 );

		// the star
		systemDisplay.m_star.sprite = null;
		systemDisplay.m_star.material = starMaterial;
		systemDisplay.m_star.color = Color.white;
		systemDisplay.m_star.raycastTarget = false;
		systemDisplay.m_star.rectTransform.sizeDelta = new Vector2( 340.0f, 340.0f );

		// a ring image on each orbit, in place of the ring mesh, behind the orbit's planet
		var rings = new Image[ systemDisplay.m_orbitList.Length ];

		for ( var i = 0; i < systemDisplay.m_orbitList.Length; i++ )
		{
			var orbit = systemDisplay.m_orbitList[ i ].transform;
			var oldRing = orbit.Find( "Ring" );

			if ( ( oldRing != null ) && ( oldRing.GetComponent<MeshRenderer>() != null ) )
			{
				UnityEngine.Object.DestroyImmediate( oldRing.gameObject );
			}

			var ring = FindOrMake( orbit, "Ring" ).GetComponent<Image>();

			if ( ring == null )
			{
				ring = FindOrMake( orbit, "Ring" ).AddComponent<Image>();
			}

			var halfSize = SystemDisplay.GetOrbitRadius( i + 1 ) + 4.0f;

			ring.sprite = null;
			ring.material = orbitMaterial;
			ring.raycastTarget = false;
			ring.color = Color.white;
			Center( ring.rectTransform, halfSize * 2.0f );
			ring.transform.SetSiblingIndex( 0 );

			rings[ i ] = ring;
		}

		// the planets
		foreach ( var planet in systemDisplay.m_planetList )
		{
			planet.sprite = null;
			planet.material = planetMaterial;
			planet.raycastTarget = false;
			planet.color = Color.white;
		}

		// the ship marker
		var player = systemDisplay.m_player;

		player.sprite = AssetDatabase.LoadAssetAtPath<Sprite>( c_shipPath );
		player.material = null;
		player.color = Color.white;
		player.raycastTarget = false;
		player.rectTransform.sizeDelta = new Vector2( c_shipWidth, c_shipHeight );
		player.rectTransform.pivot = new Vector2( 0.5f, c_shipPivotY );

		// the sensor ping: a ring drawn by the orbit shader (SystemDisplay takes the trail off it)
		var ping = FindOrMake( root, "Ping" ).GetComponent<Image>();

		if ( ping == null )
		{
			ping = FindOrMake( root, "Ping" ).AddComponent<Image>();
		}

		ping.sprite = null;
		ping.material = orbitMaterial;
		ping.raycastTarget = false;
		ping.color = Color.white;
		Center( ping.rectTransform, 100.0f );

		// the wake: segments that SystemDisplay stretches between the ship's last positions
		var wakeRoot = FindOrMake( root, "Wake" );

		Center( (RectTransform) wakeRoot.transform, 0.0f );

		var wakeSprite = AssetDatabase.LoadAssetAtPath<Sprite>( c_wakePath );
		var wake = new Image[ c_wakeSegments ];

		for ( var i = 0; i < c_wakeSegments; i++ )
		{
			var segment = FindOrMake( wakeRoot.transform, "Segment " + ( i + 1 ) ).GetComponent<Image>();

			if ( segment == null )
			{
				segment = FindOrMake( wakeRoot.transform, "Segment " + ( i + 1 ) ).AddComponent<Image>();
			}

			segment.sprite = wakeSprite;
			segment.material = null;
			segment.raycastTarget = false;
			segment.color = Color.white;
			Center( segment.rectTransform, 0.0f );
			segment.rectTransform.sizeDelta = new Vector2( 4.0f, 2.4f );

			wake[ i ] = segment;
		}

		// the engine glow, at the back of the chevron
		var engineGlow = FindOrMake( root, "Engine Glow" ).GetComponent<Image>();

		if ( engineGlow == null )
		{
			engineGlow = FindOrMake( root, "Engine Glow" ).AddComponent<Image>();
		}

		engineGlow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>( c_glowPath );
		engineGlow.material = null;
		engineGlow.raycastTarget = false;
		engineGlow.color = Color.white;
		Center( engineGlow.rectTransform, 10.0f );

		// drawn in this order, under the ship marker
		ping.transform.SetAsLastSibling();
		wakeRoot.transform.SetAsLastSibling();
		engineGlow.transform.SetAsLastSibling();
		player.transform.SetAsLastSibling();

		// point the display at the new objects
		var serializedDisplay = new SerializedObject( systemDisplay );

		var ringList = serializedDisplay.FindProperty( "m_ringList" );

		ringList.arraySize = rings.Length;

		for ( var i = 0; i < rings.Length; i++ )
		{
			ringList.GetArrayElementAtIndex( i ).objectReferenceValue = rings[ i ];
		}

		serializedDisplay.FindProperty( "m_ecosphere" ).objectReferenceValue = ecosphere;
		serializedDisplay.FindProperty( "m_background" ).objectReferenceValue = background;
		serializedDisplay.FindProperty( "m_engineGlow" ).objectReferenceValue = engineGlow;
		serializedDisplay.FindProperty( "m_ping" ).objectReferenceValue = ping;

		var wakeList = serializedDisplay.FindProperty( "m_wakeList" );

		wakeList.arraySize = wake.Length;

		for ( var i = 0; i < wake.Length; i++ )
		{
			wakeList.GetArrayElementAtIndex( i ).objectReferenceValue = wake[ i ];
		}

		serializedDisplay.ApplyModifiedPropertiesWithoutUndo();

		EditorSceneManager.MarkSceneDirty( scene );

		if ( !EditorSceneManager.SaveScene( scene ) )
		{
			throw new Exception( "could not save " + c_scenePath );
		}

		Log( "scene wired: " + rings.Length + " rings, star field, ecosphere, star, " + systemDisplay.m_planetList.Length + " planets, ship marker, sensor ping, " + wake.Length + " wake segments, engine glow" );
	}

	// the child of that name, or a new one on the UI layer
	static GameObject FindOrMake( Transform parent, string name )
	{
		var child = parent.Find( name );

		if ( child != null )
		{
			return child.gameObject;
		}

		var gameObject = new GameObject( name, typeof( RectTransform ) );

		gameObject.layer = c_uiLayer;
		gameObject.transform.SetParent( parent, false );

		return gameObject;
	}

	// fill the parent, the given number of units inside its edges
	static void Stretch( RectTransform rectTransform, float inset )
	{
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.pivot = new Vector2( 0.5f, 0.5f );
		rectTransform.anchoredPosition3D = Vector3.zero;
		rectTransform.offsetMin = new Vector2( inset, inset );
		rectTransform.offsetMax = new Vector2( -inset, -inset );
		rectTransform.localRotation = Quaternion.identity;
		rectTransform.localScale = Vector3.one;
	}

	// a square in the middle of the parent
	static void Center( RectTransform rectTransform, float size )
	{
		rectTransform.anchorMin = new Vector2( 0.5f, 0.5f );
		rectTransform.anchorMax = new Vector2( 0.5f, 0.5f );
		rectTransform.pivot = new Vector2( 0.5f, 0.5f );
		rectTransform.anchoredPosition3D = Vector3.zero;
		rectTransform.sizeDelta = new Vector2( size, size );
		rectTransform.localRotation = Quaternion.identity;
		rectTransform.localScale = Vector3.one;
	}

	static void DeleteOldAssets()
	{
		foreach ( var path in c_oldAssets )
		{
			if ( AssetDatabase.LoadMainAssetAtPath( path ) != null )
			{
				AssetDatabase.DeleteAsset( path );

				Log( "deleted " + path );
			}
		}
	}
}
