// Makes the lifeform models of manifest-lifeforms.json ready for the game.
//
// wire.ps1 copies this file into Assets/, runs Unity in batch mode with
//   -executeMethod LifeformModelWiring.Run -lifeformManifest <manifest-lifeforms.json>
// and removes it again. For every model it makes (or updates) its two SF - Standard materials next to the models, in
// Materials/ ("<model> Skin", which the game tints with the species colour, and "<model> Detail", which it never tints),
// both with the model's maps, sets up the import of its FBX (scale 1, its materials remapped to those) and saves a prefab
// of it as Assets/Resources/Lifeforms/<model>.prefab, which TerrainLifeforms loads. Running it again replaces what an
// earlier run made.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class LifeformModelWiring
{
	[Serializable]
	public class Textures
	{
		public string albedo;
		public string normal;
		public string occlusion;
		public string specular;
		public string emissive;
	}

	[Serializable]
	public class Entry
	{
		public string name;
		public string kind;
		public string model;
		public string[] materials;
		public Textures textures;
	}

	[Serializable]
	public class Manifest
	{
		public Entry[] lifeforms;
	}

	// a material of the project whose settings (shader, render state) the new materials start from
	const string c_templateMaterialPath = "Assets/Game Objects/Ships/Spemin/Spemin Ship Hull.mat";

	const string c_prefabFolder = "Assets/Resources/Lifeforms";

	static void Log( string text )
	{
		Debug.Log( "[LifeformModelWiring] " + text );
	}

	public static void Run()
	{
		var exitCode = 0;

		try
		{
			var arguments = Environment.GetCommandLineArgs();
			var manifestPath = "";

			for ( var i = 0; i < arguments.Length - 1; i++ )
			{
				if ( arguments[ i ] == "-lifeformManifest" )
				{
					manifestPath = arguments[ i + 1 ];
				}
			}

			var manifest = JsonUtility.FromJson<Manifest>( File.ReadAllText( manifestPath ) );

			if ( ( manifest == null ) || ( manifest.lifeforms == null ) || ( manifest.lifeforms.Length == 0 ) )
			{
				throw new Exception( "no lifeforms in " + manifestPath );
			}

			var template = AssetDatabase.LoadAssetAtPath<Material>( c_templateMaterialPath );

			if ( template == null )
			{
				throw new Exception( "no template material at " + c_templateMaterialPath );
			}

			// the material Inspector's own keyword logic, so that the materials come out as if they were made by hand
			var shaderGuiType = Type.GetType( "SFShaderGUI, Assembly-CSharp-Editor" );
			var shaderGui = ( shaderGuiType == null ) ? null : Activator.CreateInstance( shaderGuiType, true );
			var materialChanged = ( shaderGuiType == null ) ? null : shaderGuiType.GetMethod( "MaterialChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public );

			if ( ( shaderGui == null ) || ( materialChanged == null ) )
			{
				throw new Exception( "SFShaderGUI.MaterialChanged not found" );
			}

			if ( !AssetDatabase.IsValidFolder( c_prefabFolder ) )
			{
				AssetDatabase.CreateFolder( "Assets/Resources", "Lifeforms" );
			}

			foreach ( var entry in manifest.lifeforms )
			{
				WireLifeform( entry, template, shaderGui, materialChanged );
			}

			AssetDatabase.SaveAssets();

			Log( "done: " + manifest.lifeforms.Length + " lifeforms" );
		}
		catch ( Exception exception )
		{
			Log( "FAILED: " + exception );

			exitCode = 1;
		}

		EditorApplication.Exit( exitCode );
	}

	static void WireLifeform( Entry entry, Material template, object shaderGui, System.Reflection.MethodInfo materialChanged )
	{
		var folder = Path.GetDirectoryName( entry.model ).Replace( '\\', '/' );
		var materialFolder = folder + "/Materials";

		if ( !AssetDatabase.IsValidFolder( materialFolder ) )
		{
			AssetDatabase.CreateFolder( folder, "Materials" );
		}

		// the two materials: the skin and the detail share the model's maps
		var importer = AssetImporter.GetAtPath( entry.model ) as ModelImporter;

		if ( importer == null )
		{
			throw new Exception( "not a model: " + entry.model );
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

		foreach ( var materialName in entry.materials )
		{
			var path = materialFolder + "/" + materialName + ".mat";
			var material = AssetDatabase.LoadAssetAtPath<Material>( path );
			var isNew = material == null;

			if ( isNew )
			{
				material = new Material( template );
			}
			else
			{
				material.CopyPropertiesFromMaterial( template );
			}

			material.name = materialName;

			material.SetTexture( "_MainTex", LoadTexture( folder, entry.textures.albedo ) );
			material.SetTexture( "SF_SpecularMap", LoadTexture( folder, entry.textures.specular ) );
			material.SetTexture( "SF_NormalMap", LoadTexture( folder, entry.textures.normal ) );
			material.SetTexture( "SF_OcclusionMap", LoadTexture( folder, entry.textures.occlusion ) );
			material.SetTexture( "SF_EmissiveMap", LoadTexture( folder, entry.textures.emissive ) );
			material.SetTexture( "SF_DetailNormalMap", null );
			material.SetTexture( "_DetailAlbedoMap", null );
			material.SetTexture( "SF_ReflectionMap", null );

			// the maps carry the colour, the shine and the glow: white multipliers let them through (the skin's colour is the species', set by the game)
			material.SetColor( "SF_AlbedoColor", Color.white );
			material.SetColor( "SF_SpecularColor", Color.white );
			material.SetFloat( "SF_Smoothness", 1.0f );
			material.SetColor( "SF_EmissiveTint", Color.white );
			material.SetColor( "SF_EmissiveColor", Color.black );
			material.SetFloat( "SF_NormalMapStrength", 1.0f );
			material.SetFloat( "SF_OcclusionPower", 1.0f );

			materialChanged.Invoke( shaderGui, new object[] { material } );

			if ( isNew )
			{
				AssetDatabase.CreateAsset( material, path );
			}
			else
			{
				EditorUtility.SetDirty( material );
			}

			importer.AddRemap( new AssetImporter.SourceAssetIdentifier( typeof( Material ), materialName ), material );

			Log( "material " + path + ( isNew ? " (new)" : " (updated)" ) + " keywords=" + string.Join( " ", material.shaderKeywords ) );
		}

		importer.SaveAndReimport();

		// the prefab the game loads: the model under a root of its own, so that the game can scale and turn the root
		var model = AssetDatabase.LoadAssetAtPath<GameObject>( entry.model );

		if ( model == null )
		{
			throw new Exception( "the model did not import: " + entry.model );
		}

		var root = new GameObject( entry.name );
		var instance = (GameObject) PrefabUtility.InstantiatePrefab( model );

		instance.transform.SetParent( root.transform, false );

		var prefabPath = c_prefabFolder + "/" + entry.name + ".prefab";

		PrefabUtility.SaveAsPrefabAsset( root, prefabPath );

		UnityEngine.Object.DestroyImmediate( root );

		Log( "prefab " + prefabPath + " (" + entry.kind + ")" );
	}

	// a map next to the model, or null if it has none
	static Texture2D LoadTexture( string folder, string fileName )
	{
		if ( string.IsNullOrEmpty( fileName ) )
		{
			return null;
		}

		var texture = AssetDatabase.LoadAssetAtPath<Texture2D>( folder + "/" + fileName );

		if ( texture == null )
		{
			throw new Exception( "no texture " + folder + "/" + fileName );
		}

		return texture;
	}
}
