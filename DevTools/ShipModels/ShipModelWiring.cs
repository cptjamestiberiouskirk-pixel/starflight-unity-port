// Puts alien ship models into Spaceflight.unity from a manifest that build_<race>.py writes (or one written by hand).
//
// wire.ps1 copies this file into Assets/, runs Unity in batch mode with
//   -executeMethod ShipModelWiring.Run -shipManifest <manifest.json>
// and removes it again. For every material of the manifest it makes (or updates) an SF - Standard material next to
// the models; for every vessel it sets up the import of its model and debris FBX (scale 1, materials remapped to those
// materials), puts an inactive instance of the model under the other model templates of the Encounter location
// (layer Encounter), and points the vessel's slots of Encounter.m_alienShipModelTemplate and m_alienShipDebrisTemplate
// at the instance and the debris model. Running it again replaces what an earlier run made.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShipModelWiring
{
	[Serializable]
	public class MaterialEntry
	{
		public string name;
		public string folder;
		public float[] albedo;
		public float[] specular;
		public float smoothness;
		public float[] emissive;
	}

	[Serializable]
	public class VesselEntry
	{
		public int id;
		public string name;
		public string model;
		public string debris;
		public float scale;
		public string[] materials;
	}

	[Serializable]
	public class Manifest
	{
		public string race;
		public VesselEntry[] vessels;
		public MaterialEntry[] materials;
	}

	const string c_scenePath = "Assets/Scenes/Spaceflight.unity";

	// a ship material of the project whose settings (shader, keywords, render state) the new materials start from
	const string c_templateMaterialPath = "Assets/Game Objects/Ships/Spemin/Spemin Ship Hull.mat";

	const string c_placeholderName = "Not Modeled Yet";

	static void Log( string text )
	{
		Debug.Log( "[ShipModelWiring] " + text );
	}

	public static void Run()
	{
		var exitCode = 0;

		try
		{
			var manifestPath = ArgumentAfter( "-shipManifest" );

			if ( string.IsNullOrEmpty( manifestPath ) || !File.Exists( manifestPath ) )
			{
				throw new Exception( "no manifest: pass -shipManifest <path>" );
			}

			var manifest = JsonUtility.FromJson<Manifest>( File.ReadAllText( manifestPath ) );

			Log( "manifest " + manifestPath + " race=" + manifest.race + " vessels=" + manifest.vessels.Length );

			AssetDatabase.Refresh();

			var materials = MakeMaterials( manifest );

			foreach ( var vessel in manifest.vessels )
			{
				SetUpImport( vessel.model, vessel, materials );

				if ( !string.IsNullOrEmpty( vessel.debris ) )
				{
					SetUpImport( vessel.debris, vessel, materials );
				}
			}

			WireScene( manifest );
		}
		catch ( Exception exception )
		{
			Log( "FAILED " + exception );
			exitCode = 1;
		}

		EditorApplication.Exit( exitCode );
	}

	static string ArgumentAfter( string name )
	{
		var arguments = Environment.GetCommandLineArgs();

		for ( var i = 0; i < arguments.Length - 1; i++ )
		{
			if ( arguments[ i ] == name )
			{
				return arguments[ i + 1 ];
			}
		}

		return null;
	}

	static Color ToColor( float[] values )
	{
		if ( ( values == null ) || ( values.Length < 3 ) )
		{
			return Color.black;
		}

		return new Color( values[ 0 ], values[ 1 ], values[ 2 ], 1.0f );
	}

	static Dictionary<string, Material> MakeMaterials( Manifest manifest )
	{
		var materials = new Dictionary<string, Material>();

		if ( manifest.materials == null )
		{
			return materials;
		}

		var template = AssetDatabase.LoadAssetAtPath<Material>( c_templateMaterialPath );

		if ( template == null )
		{
			throw new Exception( "template material missing: " + c_templateMaterialPath );
		}

		foreach ( var entry in manifest.materials )
		{
			var path = entry.folder + "/" + entry.name + ".mat";
			var material = AssetDatabase.LoadAssetAtPath<Material>( path );
			var isNew = ( material == null );

			if ( isNew )
			{
				material = new Material( template );
			}
			else
			{
				material.CopyPropertiesFromMaterial( template );
			}

			material.name = entry.name;

			// no maps: flat colours only
			foreach ( var textureName in material.GetTexturePropertyNames() )
			{
				material.SetTexture( textureName, null );
			}

			foreach ( var keyword in material.shaderKeywords )
			{
				if ( keyword.EndsWith( "MAP_ON" ) || keyword.EndsWith( "MAP_ISCOMPRESSED" ) || ( keyword == "SF_ALBEDOOCCLUSION_ON" ) )
				{
					material.DisableKeyword( keyword );
				}
			}

			material.SetColor( "SF_AlbedoColor", ToColor( entry.albedo ) );
			material.SetColor( "SF_SpecularColor", ToColor( entry.specular ) );
			material.SetFloat( "SF_Smoothness", entry.smoothness );
			material.SetColor( "SF_EmissiveColor", ToColor( entry.emissive ) );

			if ( isNew )
			{
				Directory.CreateDirectory( entry.folder );
				AssetDatabase.CreateAsset( material, path );
			}
			else
			{
				EditorUtility.SetDirty( material );
			}

			materials[ entry.name ] = material;

			Log( "material " + path + ( isNew ? " (new)" : " (updated)" ) + " keywords=" + string.Join( " ", material.shaderKeywords ) );
		}

		AssetDatabase.SaveAssets();

		return materials;
	}

	static void SetUpImport( string assetPath, VesselEntry vessel, Dictionary<string, Material> materials )
	{
		var importer = AssetImporter.GetAtPath( assetPath ) as ModelImporter;

		if ( importer == null )
		{
			throw new Exception( "not a model: " + assetPath );
		}

		// models of the project that were tuned by hand are left as they are; only a vessel with materials of its own is set up
		if ( ( vessel.materials == null ) || ( vessel.materials.Length == 0 ) )
		{
			Log( "import of " + assetPath + " left as it is" );
			return;
		}

		importer.globalScale = 1.0f;
		importer.useFileScale = true;
		importer.importCameras = false;
		importer.importLights = false;
		importer.importAnimation = false;
		importer.animationType = ModelImporterAnimationType.None;
		importer.importBlendShapes = false;
		importer.isReadable = false;
		importer.importNormals = ModelImporterNormals.Import;
		importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
		importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

		foreach ( var name in vessel.materials )
		{
			Material material;

			if ( materials.TryGetValue( name, out material ) )
			{
				importer.AddRemap( new AssetImporter.SourceAssetIdentifier( typeof( Material ), name ), material );
			}
			else
			{
				throw new Exception( "material " + name + " of " + vessel.name + " is not in the manifest" );
			}
		}

		importer.SaveAndReimport();

		Log( "import of " + assetPath + " set up with " + vessel.materials.Length + " materials" );
	}

	static void SetLayer( GameObject gameObject, int layer )
	{
		gameObject.layer = layer;

		foreach ( Transform child in gameObject.transform )
		{
			SetLayer( child.gameObject, layer );
		}
	}

	static void WireScene( Manifest manifest )
	{
		var scene = EditorSceneManager.OpenScene( c_scenePath, OpenSceneMode.Single );
		var encounter = UnityEngine.Object.FindFirstObjectByType<Encounter>( FindObjectsInactive.Include );

		if ( encounter == null )
		{
			throw new Exception( "no Encounter in " + c_scenePath );
		}

		// the new templates go where the placeholder is, under the other model templates
		Transform parent = null;

		foreach ( var template in encounter.m_alienShipModelTemplate )
		{
			if ( ( template != null ) && ( template.name == c_placeholderName ) )
			{
				parent = template.transform.parent;
				break;
			}
		}

		if ( parent == null )
		{
			throw new Exception( "the placeholder model template was not found" );
		}

		var layer = LayerMask.NameToLayer( "Encounter" );

		foreach ( var vessel in manifest.vessels )
		{
			if ( ( vessel.id <= 0 ) || ( vessel.id >= encounter.m_alienShipModelTemplate.Length ) )
			{
				throw new Exception( "vessel id out of range: " + vessel.id );
			}

			var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>( vessel.model );

			if ( modelAsset == null )
			{
				throw new Exception( "model missing: " + vessel.model );
			}

			// replace what an earlier run put there: an instance of the same model with the vessel's name. Anything else of that
			// name was made by hand and stays (Spaceflight.unity had a hand-made Mechan Scout template that no slot used)
			var previous = parent.Find( vessel.name );

			if ( previous != null )
			{
				if ( PrefabUtility.GetCorrespondingObjectFromOriginalSource( previous.gameObject ) != modelAsset )
				{
					throw new Exception( "a hand-made object named " + vessel.name + " is already under the model templates; rename it or wire it by hand" );
				}

				UnityEngine.Object.DestroyImmediate( previous.gameObject );
			}

			var instance = (GameObject) PrefabUtility.InstantiatePrefab( modelAsset, scene );

			instance.transform.SetParent( parent, false );
			instance.name = vessel.name;
			instance.transform.localPosition = Vector3.zero;

			var scale = ( vessel.scale > 0.0f ) ? vessel.scale : 1.0f;

			instance.transform.localScale = modelAsset.transform.localScale * scale;

			SetLayer( instance, layer );
			instance.SetActive( false );

			encounter.m_alienShipModelTemplate[ vessel.id ] = instance;

			if ( !string.IsNullOrEmpty( vessel.debris ) )
			{
				var debrisAsset = AssetDatabase.LoadAssetAtPath<GameObject>( vessel.debris );

				if ( debrisAsset == null )
				{
					throw new Exception( "debris missing: " + vessel.debris );
				}

				encounter.m_alienShipDebrisTemplate[ vessel.id ] = debrisAsset;
			}

			Log( "vessel " + vessel.id + " " + vessel.name + ": template " + instance.name + " scale " + instance.transform.localScale.x + " rotation " + instance.transform.localEulerAngles + ( string.IsNullOrEmpty( vessel.debris ) ? "" : ", debris " + vessel.debris ) );
		}

		EditorUtility.SetDirty( encounter );
		EditorSceneManager.MarkSceneDirty( scene );

		if ( !EditorSceneManager.SaveScene( scene ) )
		{
			throw new Exception( "the scene could not be saved" );
		}

		Log( "saved " + c_scenePath );
	}
}
