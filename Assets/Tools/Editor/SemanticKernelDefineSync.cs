
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// switches the STARFLIGHT_SEMANTIC_KERNEL scripting define on when NuGetForUnity has put Semantic Kernel into
// Assets/Packages, and off when it is gone
//
// the Starflight.AI assembly (Assets/Scripts/AI) only compiles with that define, so the project keeps compiling on a
// machine (or in CI) where the NuGet packages have not been restored yet; without it, the missing DLLs would stop the
// whole project from compiling, NuGetForUnity included, and so the restore that would bring them could never run
[InitializeOnLoad]
public class SemanticKernelDefineSync : AssetPostprocessor
{
	// the define the Starflight.AI assemblies require
	public const string c_defineSymbol = "STARFLIGHT_SEMANTIC_KERNEL";

	// the DLLs that have to be there (NuGetForUnity puts packages in Assets/Packages by default)
	static readonly string[] c_requiredAssemblies = { "Microsoft.SemanticKernel.Core.dll", "Microsoft.SemanticKernel.Abstractions.dll" };

	// check once the editor has loaded
	static SemanticKernelDefineSync()
	{
		EditorApplication.delayCall += Sync;
	}

	// check again when NuGetForUnity adds or removes packages
	static void OnPostprocessAllAssets( string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths )
	{
		if ( TouchesSemanticKernel( importedAssets ) || TouchesSemanticKernel( deletedAssets ) || TouchesSemanticKernel( movedAssets ) || TouchesSemanticKernel( movedFromAssetPaths ) )
		{
			EditorApplication.delayCall -= Sync;
			EditorApplication.delayCall += Sync;
		}
	}

	// true if one of the paths is part of a semantic kernel package
	static bool TouchesSemanticKernel( string[] assetPaths )
	{
		if ( assetPaths == null )
		{
			return false;
		}

		foreach ( var assetPath in assetPaths )
		{
			if ( ( assetPath != null ) && assetPath.StartsWith( "Assets/Packages/Microsoft.SemanticKernel", StringComparison.OrdinalIgnoreCase ) )
			{
				return true;
			}
		}

		return false;
	}

	// true if every required DLL is somewhere under Assets/Packages
	public static bool IsSemanticKernelInstalled()
	{
		var packagesFolder = Path.Combine( Application.dataPath, "Packages" );

		if ( !Directory.Exists( packagesFolder ) )
		{
			return false;
		}

		foreach ( var assemblyName in c_requiredAssemblies )
		{
			if ( Directory.GetFiles( packagesFolder, assemblyName, SearchOption.AllDirectories ).Length == 0 )
			{
				return false;
			}
		}

		return true;
	}

	// adds or removes the define for the build target that is selected
	[MenuItem( "Starflight Remake/AI/Sync Semantic Kernel Define" )]
	public static void Sync()
	{
		// changing defines recompiles, which must not happen while entering play mode
		if ( EditorApplication.isPlayingOrWillChangePlaymode )
		{
			return;
		}

		var installed = IsSemanticKernelInstalled();

		var buildTarget = NamedBuildTarget.FromBuildTargetGroup( EditorUserBuildSettings.selectedBuildTargetGroup );

		PlayerSettings.GetScriptingDefineSymbols( buildTarget, out string[] defines );

		var defineList = new List<string>( defines );

		var hasDefine = defineList.Contains( c_defineSymbol );

		if ( installed == hasDefine )
		{
			return;
		}

		if ( installed )
		{
			defineList.Add( c_defineSymbol );
		}
		else
		{
			defineList.Remove( c_defineSymbol );
		}

		PlayerSettings.SetScriptingDefineSymbols( buildTarget, defineList.ToArray() );

		Debug.Log( "SemanticKernelDefineSync: " + ( installed ? "Semantic Kernel is in Assets/Packages, added " : "Semantic Kernel is not in Assets/Packages, removed " ) + c_defineSymbol + " for " + buildTarget.TargetName + "." );
	}
}
