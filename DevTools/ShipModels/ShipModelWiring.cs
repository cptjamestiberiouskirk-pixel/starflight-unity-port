// Puts alien ship models into Spaceflight.unity from a manifest that build_<race>.py writes (or one written by hand).
//
// wire.ps1 copies this file into Assets/, runs Unity in batch mode with
//   -executeMethod ShipModelWiring.Run -shipManifest <manifest.json>
// and removes it again. For every material of the manifest it makes (or updates) an SF - Standard material next to
// the models, of flat colours or with the maps the manifest names (a textured model, import_meshy.py); for every vessel it sets up the import of its model and debris FBX (scale 1, materials remapped to those
// materials), puts an inactive instance of the model under the other model templates of the Encounter location
// (layer Encounter), and points the vessel's slots of Encounter.m_alienShipModelTemplate and m_alienShipDebrisTemplate
// at the instance and the debris model. The player's ship and the terrain vehicle keep their old node names (their manifests
// have a playerShip or a terrainVehicle section): their scene copies lose the old models' material overrides instead. The
// models of a models section (ground props) only have their import set up with their materials. Running it again replaces
// what an earlier run made.

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

		// asset paths of maps (import_meshy.py writes them for a textured model); empty for a material of flat colours
		public string albedoMap;
		public string specularMap;
		public string normalMap;
		public string emissiveMap;
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

	// the player's ship (import_meshy_arth.py): its model keeps the old node names the scenes reference, so only its materials,
	// the scene copies' material overrides and the contrails' places change
	[Serializable]
	public class PlayerShipEntry
	{
		public string model;
		public string debris;
		public string[] materials;

		// the two engine nozzles in the model's frame, x, y, z, x, y, z
		public float[] contrails;

		// the debris model's import scale: CombatController spawns the wreck at its own scale, not the ship's instance scale
		public float debrisScale;
	}

	// the terrain vehicle (import_meshy_tv.py): its model keeps the old node names the scene references (TerrainVehicle turns and
	// steers the wheels through them), so only its materials, the scene copies' material overrides and the capsule collider the
	// scene gives the model's root (sized for the old model) change
	[Serializable]
	public class TerrainVehicleEntry
	{
		public string model;
		public string[] materials;

		// the capsule for the new model, in the root's frame (x, y, z), its height and its radius
		public float[] colliderCentre;
		public float colliderHeight;
		public float colliderRadius;
	}

	// a model whose import alone takes the manifest's materials (import_meshy_props.py: the ancient ruins, the rocks and the deposits
	// with shapes of their own, which keep their old node names and are placed by the game as they were)
	[Serializable]
	public class ModelEntry
	{
		public string model;
		public string[] materials;
	}

	[Serializable]
	public class Manifest
	{
		public string race;
		public VesselEntry[] vessels;
		public MaterialEntry[] materials;
		public PlayerShipEntry playerShip;
		public TerrainVehicleEntry terrainVehicle;
		public ModelEntry[] models;
	}

	// the scenes with copies of the player ship's model, and of the terrain vehicle's
	static readonly string[] c_playerShipScenes = { "Assets/Scenes/Spaceflight.unity", "Assets/Scenes/Starport.unity", "Assets/Scenes/Test.unity" };
	static readonly string[] c_terrainVehicleScenes = { "Assets/Scenes/Spaceflight.unity", "Assets/Scenes/Test.unity" };

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

			Log( "manifest " + manifestPath + " race=" + manifest.race + " vessels=" + ( ( manifest.vessels != null ) ? manifest.vessels.Length : 0 ) );

			AssetDatabase.Refresh();

			var materials = MakeMaterials( manifest );

			foreach ( var vessel in manifest.vessels ?? new VesselEntry[ 0 ] )
			{
				SetUpImport( vessel.model, vessel, materials, 1.0f );

				if ( !string.IsNullOrEmpty( vessel.debris ) )
				{
					SetUpImport( vessel.debris, vessel, materials, 1.0f );
				}
			}

			if ( ( manifest.vessels != null ) && ( manifest.vessels.Length > 0 ) )
			{
				WireScene( manifest );
			}

			if ( ( manifest.playerShip != null ) && !string.IsNullOrEmpty( manifest.playerShip.model ) )
			{
				WirePlayerShip( manifest.playerShip, materials );
			}

			if ( ( manifest.terrainVehicle != null ) && !string.IsNullOrEmpty( manifest.terrainVehicle.model ) )
			{
				WireTerrainVehicle( manifest.terrainVehicle, materials );
			}

			foreach ( var model in manifest.models ?? new ModelEntry[ 0 ] )
			{
				SetUpImport( model.model, new VesselEntry { name = Path.GetFileNameWithoutExtension( model.model ), materials = model.materials }, materials, 1.0f );
			}
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

			// start from no maps (flat colours); the maps of a textured model are put on below
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

			// the maps of a textured model, each with its keyword (the colours above multiply them)
			SetMap( material, "_MainTex", "SF_ALBEDOMAP_ON", LoadMap( entry.albedoMap, false ) );
			SetMap( material, "SF_SpecularMap", "SF_SPECULARMAP_ON", LoadMap( entry.specularMap, false ) );
			SetMap( material, "SF_EmissiveMap", "SF_EMISSIVEMAP_ON", LoadMap( entry.emissiveMap, false ) );

			// a map imported as a normal map is stored compressed, which the shader has to be told
			var normalMap = LoadMap( entry.normalMap, true );

			SetMap( material, "SF_NormalMap", "SF_NORMALMAP_ON", normalMap );

			if ( normalMap != null )
			{
				material.EnableKeyword( "SF_NORMALMAP_ISCOMPRESSED" );
			}

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

	// a map of the manifest with its import set up (a normal map as a normal map, any other in sRGB) - null for none
	static Texture2D LoadMap( string assetPath, bool isNormalMap )
	{
		if ( string.IsNullOrEmpty( assetPath ) )
		{
			return null;
		}

		var importer = AssetImporter.GetAtPath( assetPath ) as TextureImporter;

		if ( importer == null )
		{
			throw new Exception( "not a texture: " + assetPath );
		}

		var textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;

		if ( ( importer.textureType != textureType ) || ( importer.sRGBTexture == isNormalMap ) )
		{
			importer.textureType = textureType;
			importer.sRGBTexture = !isNormalMap;
			importer.SaveAndReimport();
		}

		return AssetDatabase.LoadAssetAtPath<Texture2D>( assetPath );
	}

	// put a map on a material and switch its keyword on (or leave both off for no map)
	static void SetMap( Material material, string textureName, string keyword, Texture2D map )
	{
		if ( map == null )
		{
			return;
		}

		material.SetTexture( textureName, map );
		material.EnableKeyword( keyword );
	}

	// scale: the importer's global scale (1 for the models build_<race>.py and import_meshy.py write)
	static void SetUpImport( string assetPath, VesselEntry vessel, Dictionary<string, Material> materials, float scale )
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

		importer.globalScale = scale;
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

		// a model made again with other materials (a textured model in place of a procedural one) drops the remaps of the old ones
		var remaps = new List<AssetImporter.SourceAssetIdentifier>( importer.GetExternalObjectMap().Keys );

		foreach ( var remap in remaps )
		{
			if ( ( remap.type == typeof( Material ) ) && ( Array.IndexOf( vessel.materials, remap.name ) < 0 ) )
			{
				importer.RemoveRemap( remap );
			}
		}

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

	// the player ship: its import, then in every scene with a copy of it the copy's material overrides go (each copy set the old
	// model's materials by hand) and, under the flying ship, the contrails move to the new engine nozzles
	static void WirePlayerShip( PlayerShipEntry playerShip, Dictionary<string, Material> materials )
	{
		var entry = new VesselEntry { name = "Arth Ship", materials = playerShip.materials };

		SetUpImport( playerShip.model, entry, materials, 1.0f );

		if ( !string.IsNullOrEmpty( playerShip.debris ) )
		{
			SetUpImport( playerShip.debris, entry, materials, ( playerShip.debrisScale > 0.0f ) ? playerShip.debrisScale : 1.0f );
		}

		var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>( playerShip.model );

		if ( modelAsset == null )
		{
			throw new Exception( "model missing: " + playerShip.model );
		}

		foreach ( var scenePath in c_playerShipScenes )
		{
			if ( !File.Exists( scenePath ) )
			{
				continue;
			}

			var scene = EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Single );
			var copies = RemoveMaterialOverrides( scene, scenePath, modelAsset, out var removed );

			// the contrails of the flying ship, each to the nozzle on its own side
			var player = UnityEngine.Object.FindFirstObjectByType<PlayerShip>( FindObjectsInactive.Include );

			if ( ( player != null ) && ( player.m_contrailParticleSystem != null ) && ( playerShip.contrails != null ) && ( playerShip.contrails.Length >= 6 ) )
			{
				foreach ( var contrail in player.m_contrailParticleSystem )
				{
					if ( contrail == null )
					{
						continue;
					}

					var side = ( contrail.transform.localPosition.x >= 0.0f ) ? 0 : 3;

					if ( Mathf.Sign( playerShip.contrails[ side ] ) != Mathf.Sign( contrail.transform.localPosition.x ) )
					{
						side = 3 - side;
					}

					var before = contrail.transform.localPosition;

					contrail.transform.localPosition = new Vector3( playerShip.contrails[ side ], playerShip.contrails[ side + 1 ], playerShip.contrails[ side + 2 ] );

					Log( scenePath + ": contrail " + contrail.name + " moved from " + before + " to " + contrail.transform.localPosition );
				}
			}

			if ( copies > 0 )
			{
				EditorSceneManager.MarkSceneDirty( scene );

				if ( !EditorSceneManager.SaveScene( scene ) )
				{
					throw new Exception( "the scene could not be saved: " + scenePath );
				}
			}

			Log( scenePath + ": " + copies + " copies of the player ship, " + removed + " material overrides removed" );
		}
	}

	// takes the material overrides off every copy of the model in the scene (each copy set the old model's materials by hand);
	// returns how many copies there are
	static int RemoveMaterialOverrides( UnityEngine.SceneManagement.Scene scene, string scenePath, GameObject modelAsset, out int removed )
	{
		var copies = 0;

		removed = 0;

		foreach ( var root in scene.GetRootGameObjects() )
		{
			foreach ( var node in root.GetComponentsInChildren<Transform>( true ) )
			{
				var gameObject = node.gameObject;

				if ( !PrefabUtility.IsAnyPrefabInstanceRoot( gameObject ) || ( PrefabUtility.GetCorrespondingObjectFromSource( gameObject ) != modelAsset ) )
				{
					continue;
				}

				var modifications = PrefabUtility.GetPropertyModifications( gameObject );
				var kept = new List<PropertyModification>();

				foreach ( var modification in modifications )
				{
					if ( modification.propertyPath.StartsWith( "m_Materials" ) )
					{
						removed++;
					}
					else
					{
						kept.Add( modification );
					}
				}

				PrefabUtility.SetPropertyModifications( gameObject, kept.ToArray() );
				copies++;

				Log( scenePath + ": copy " + gameObject.name + " under " + ( ( node.parent != null ) ? node.parent.name : "the scene" ) + ", " + ( modifications.Length - kept.Count ) + " material overrides removed" );
			}
		}

		return copies;
	}

	// the terrain vehicle: its import, then in every scene with a copy of it the copy's material overrides go and the vehicle's
	// capsule collider (on the copy's root) takes the new model's size
	static void WireTerrainVehicle( TerrainVehicleEntry terrainVehicle, Dictionary<string, Material> materials )
	{
		SetUpImport( terrainVehicle.model, new VesselEntry { name = "Terrain Vehicle", materials = terrainVehicle.materials }, materials, 1.0f );

		var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>( terrainVehicle.model );

		if ( modelAsset == null )
		{
			throw new Exception( "model missing: " + terrainVehicle.model );
		}

		foreach ( var scenePath in c_terrainVehicleScenes )
		{
			if ( !File.Exists( scenePath ) )
			{
				continue;
			}

			var scene = EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Single );
			var copies = RemoveMaterialOverrides( scene, scenePath, modelAsset, out var removed );
			var colliders = 0;

			// the capsule on the root of the vehicle's own copy (the one under the TerrainVehicle; other copies have none)
			foreach ( var vehicle in UnityEngine.Object.FindObjectsByType<TerrainVehicle>( FindObjectsInactive.Include, FindObjectsSortMode.None ) )
			{
				var model = vehicle.m_terrainVehicleModel;

				if ( ( model == null ) || ( terrainVehicle.colliderCentre == null ) || ( terrainVehicle.colliderCentre.Length < 3 ) || ( terrainVehicle.colliderHeight <= 0.0f ) )
				{
					continue;
				}

				foreach ( var capsule in model.GetComponentsInChildren<CapsuleCollider>( true ) )
				{
					var before = "centre " + capsule.center + " height " + capsule.height + " radius " + capsule.radius;

					capsule.center = new Vector3( terrainVehicle.colliderCentre[ 0 ], terrainVehicle.colliderCentre[ 1 ], terrainVehicle.colliderCentre[ 2 ] );
					capsule.height = terrainVehicle.colliderHeight;
					capsule.radius = terrainVehicle.colliderRadius;
					EditorUtility.SetDirty( capsule );
					colliders++;

					Log( scenePath + ": capsule on " + capsule.gameObject.name + " from " + before + " to centre " + capsule.center + " height " + capsule.height + " radius " + capsule.radius );
				}
			}

			if ( ( copies > 0 ) || ( colliders > 0 ) )
			{
				EditorSceneManager.MarkSceneDirty( scene );

				if ( !EditorSceneManager.SaveScene( scene ) )
				{
					throw new Exception( "the scene could not be saved: " + scenePath );
				}
			}

			Log( scenePath + ": " + copies + " copies of the terrain vehicle, " + removed + " material overrides removed, " + colliders + " capsules sized" );
		}
	}
}
