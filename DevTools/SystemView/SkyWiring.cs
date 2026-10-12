// Sets up the new sky of the spaceflight views: the Milky Way that build_sky_art.py writes.
//
// wire.ps1 copies this file into Assets/, runs Unity in batch mode with
//   -executeMethod SkyWiring.Run
// and removes it again. It imports the Milky Way picture as a cubemap (from its latitude and longitude layout), puts it on the
// skybox material with no strength (the Starport uses the same material and shows none of it), gives the Spaceflight scene's
// skybox controller the strength the star system view shows it with. Running it again gives the same project.

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SkyWiring
{
	const string c_scenePath = "Assets/Scenes/Spaceflight.unity";
	const string c_milkyWayPath = "Assets/Game Objects/Skyboxes/Milky Way/milky_way.png";
	const string c_skyboxMaterialPath = "Assets/Game Objects/Skyboxes/Skybox - Starflight.mat";

	// how strong the Milky Way is in the spaceflight views (a faint band: the stars of the particle starfield are in front of it)
	const float c_milkyWayStrength = 0.035f;

	static void Log( string text )
	{
		Debug.Log( "[SkyWiring] " + text );
	}

	public static void Run()
	{
		var exitCode = 0;

		try
		{
			AssetDatabase.Refresh();

			var milkyWay = ImportMilkyWay();

			SetUpMaterial( milkyWay );

			WireScene();

			AssetDatabase.SaveAssets();
		}
		catch ( Exception exception )
		{
			Log( "FAILED " + exception );
			exitCode = 1;
		}

		EditorApplication.Exit( exitCode );
	}

	static Cubemap ImportMilkyWay()
	{
		var importer = AssetImporter.GetAtPath( c_milkyWayPath ) as TextureImporter;

		if ( importer == null )
		{
			throw new Exception( "no texture at " + c_milkyWayPath + " (run build_sky_art.py first)" );
		}

		importer.textureType = TextureImporterType.Default;
		importer.textureShape = TextureImporterShape.TextureCube;
		importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
		importer.sRGBTexture = true;
		importer.mipmapEnabled = true;
		importer.wrapMode = TextureWrapMode.Clamp;
		importer.filterMode = FilterMode.Trilinear;
		importer.maxTextureSize = 2048;
		importer.textureCompression = TextureImporterCompression.CompressedHQ;
		importer.SaveAndReimport();

		var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>( c_milkyWayPath );

		if ( cubemap == null )
		{
			throw new Exception( c_milkyWayPath + " did not import as a cubemap" );
		}

		Log( "the Milky Way imports as a cubemap of " + cubemap.width + " x " + cubemap.height + " per face" );

		return cubemap;
	}

	static void SetUpMaterial( Cubemap milkyWay )
	{
		var material = AssetDatabase.LoadAssetAtPath<Material>( c_skyboxMaterialPath );

		if ( material == null )
		{
			throw new Exception( "no material at " + c_skyboxMaterialPath );
		}

		if ( !material.HasProperty( "_MilkyWay" ) )
		{
			throw new Exception( "the skybox shader has no _MilkyWay (" + material.shader.name + ")" );
		}

		material.SetTexture( "_MilkyWay", milkyWay );
		material.SetFloat( "_MilkyWayStrength", 0.0f );

		EditorUtility.SetDirty( material );

		Log( "the skybox material has the Milky Way, with no strength of its own" );
	}

	static void WireScene()
	{
		var scene = EditorSceneManager.OpenScene( c_scenePath, OpenSceneMode.Single );

		// the strength of the Milky Way in the spaceflight views
		var skybox = UnityEngine.Object.FindFirstObjectByType<StarflightSkybox>( FindObjectsInactive.Include );

		if ( skybox == null )
		{
			throw new Exception( "no StarflightSkybox in " + c_scenePath );
		}

		var serializedSkybox = new SerializedObject( skybox );

		serializedSkybox.FindProperty( "m_milkyWayStrength" ).floatValue = c_milkyWayStrength;
		serializedSkybox.ApplyModifiedPropertiesWithoutUndo();

		Log( "the Spaceflight scene's skybox shows the Milky Way at " + c_milkyWayStrength );

		EditorSceneManager.MarkSceneDirty( scene );
		EditorSceneManager.SaveScene( scene );
	}
}
