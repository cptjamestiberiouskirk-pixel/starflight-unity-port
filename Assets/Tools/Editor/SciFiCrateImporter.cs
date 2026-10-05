
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// imports the crate that DevTools/Blender/make_scifi_crate.py exports, makes a prefab of it, and places it in the active scene
public static class SciFiCrateImporter
{
	// where the blender script writes the model
	public const string c_modelPath = "Assets/Models/SciFiCrate.fbx";

	// the prefab made from it (a variant of the model, so a new export of the model updates it)
	public const string c_prefabPath = "Assets/Models/SciFiCrate.prefab";

	[MenuItem( "Starflight Remake/AI/Import SciFi Crate Into Active Scene" )]
	public static void ImportIntoActiveScene()
	{
		// a change made in play mode is lost when play mode ends
		if ( EditorApplication.isPlayingOrWillChangePlaymode )
		{
			Debug.LogWarning( "SciFiCrateImporter: leave play mode first, a change made in play mode is not saved." );

			return;
		}

		// pick up files that were written outside the editor (the blender export)
		AssetDatabase.Refresh( ImportAssetOptions.ForceSynchronousImport );

		if ( !File.Exists( c_modelPath ) )
		{
			Debug.LogError( "SciFiCrateImporter: " + c_modelPath + " does not exist. Run DevTools/Blender/make_scifi_crate.py in Blender first." );

			return;
		}

		// import the model again, in case it was exported over an older one
		AssetDatabase.ImportAsset( c_modelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport );

		var model = AssetDatabase.LoadAssetAtPath<GameObject>( c_modelPath );

		if ( model == null )
		{
			Debug.LogError( "SciFiCrateImporter: Unity could not import " + c_modelPath + " as a model (see the import errors in the console)." );

			return;
		}

		// make the prefab once
		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>( c_prefabPath );

		if ( prefab == null )
		{
			var modelInstance = (GameObject) PrefabUtility.InstantiatePrefab( model );

			modelInstance.name = "SciFiCrate";

			prefab = PrefabUtility.SaveAsPrefabAsset( modelInstance, c_prefabPath, out bool saved );

			Object.DestroyImmediate( modelInstance );

			if ( !saved || ( prefab == null ) )
			{
				Debug.LogError( "SciFiCrateImporter: could not save " + c_prefabPath + "." );

				return;
			}
		}

		// place it at the origin of the active scene
		var scene = SceneManager.GetActiveScene();

		var crate = (GameObject) PrefabUtility.InstantiatePrefab( prefab, scene );

		crate.transform.SetPositionAndRotation( Vector3.zero, Quaternion.identity );

		Undo.RegisterCreatedObjectUndo( crate, "Place SciFi Crate" );

		// the scene has to be saved to keep it
		EditorSceneManager.MarkSceneDirty( scene );

		Selection.activeGameObject = crate;

		Debug.Log( "SciFiCrateImporter: placed " + c_prefabPath + " at (0, 0, 0) in scene " + scene.name + " (save the scene to keep it)." );
	}
}
