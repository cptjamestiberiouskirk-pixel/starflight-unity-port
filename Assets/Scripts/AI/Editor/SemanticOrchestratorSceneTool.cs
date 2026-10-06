
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starflight.AI.EditorTools
{
	// puts the semantic orchestrator into the scene that is open in the editor
	public static class SemanticOrchestratorSceneTool
	{
		// the name of the game object that holds the orchestrator
		public const string c_gameObjectName = "SemanticOrchestrator";

		[MenuItem( "Starflight Remake/AI/Add Semantic Orchestrator To Active Scene" )]
		public static void AddToActiveScene()
		{
			// a change made in play mode is lost when play mode ends
			if ( EditorApplication.isPlayingOrWillChangePlaymode )
			{
				Debug.LogWarning( "SemanticOrchestratorSceneTool: leave play mode first, a change made in play mode is not saved." );

				return;
			}

			var scene = SceneManager.GetActiveScene();

			// reuse the object if the scene already has one
			GameObject orchestrator = null;

			foreach ( var root in scene.GetRootGameObjects() )
			{
				if ( root.name == c_gameObjectName )
				{
					orchestrator = root;

					break;
				}
			}

			if ( orchestrator == null )
			{
				orchestrator = new GameObject( c_gameObjectName );

				SceneManager.MoveGameObjectToScene( orchestrator, scene );

				Undo.RegisterCreatedObjectUndo( orchestrator, "Add Semantic Orchestrator" );
			}

			// the dispatcher first, so it exists when the orchestrator starts
			if ( !orchestrator.TryGetComponent<MainThreadDispatcher>( out _ ) )
			{
				Undo.AddComponent<MainThreadDispatcher>( orchestrator );
			}

			if ( !orchestrator.TryGetComponent<MetagameOrchestrator>( out _ ) )
			{
				Undo.AddComponent<MetagameOrchestrator>( orchestrator );
			}

			// the scene has to be saved to keep it
			EditorSceneManager.MarkSceneDirty( scene );

			Selection.activeGameObject = orchestrator;

			Debug.Log( "SemanticOrchestratorSceneTool: " + c_gameObjectName + " is in scene " + scene.name + " with a MainThreadDispatcher and a MetagameOrchestrator (save the scene to keep it)." );
		}
	}
}
