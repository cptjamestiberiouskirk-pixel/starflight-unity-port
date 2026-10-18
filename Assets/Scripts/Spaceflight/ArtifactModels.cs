
using System.Collections.Generic;
using UnityEngine;

// the models of the original's 15 special artifacts (Resources/Artifacts, made by DevTools/ShipModels/import_meshy_artifacts.py): one for each, named
// for its artifact, centred on its origin and one unit across at its widest; the curios have none, as the original showed no artifact at all
public static class ArtifactModels
{
	// the models loaded so far, by artifact name (null for an artifact without one)
	static readonly Dictionary<string, GameObject> s_models = new Dictionary<string, GameObject>();

	// the artifact's model, or null if it has none
	public static GameObject Get( GD_Artifact artifact )
	{
		if ( ( artifact == null ) || string.IsNullOrEmpty( artifact.m_name ) )
		{
			return null;
		}

		if ( !s_models.TryGetValue( artifact.m_name, out var model ) )
		{
			model = artifact.IsSpecial() ? Resources.Load<GameObject>( "Artifacts/" + artifact.m_name ) : null;
			s_models[ artifact.m_name ] = model;
		}

		return model;
	}

	// a copy of the artifact's model under this parent, this many units across at its widest, on this layer (null if it has none)
	public static GameObject Make( GD_Artifact artifact, Transform parent, float size, int layer )
	{
		var model = Get( artifact );

		if ( model == null )
		{
			return null;
		}

		var copy = Object.Instantiate( model, parent, false );

		copy.name = artifact.m_name;
		copy.transform.localScale = model.transform.localScale * size;

		foreach ( var child in copy.GetComponentsInChildren<Transform>( true ) )
		{
			child.gameObject.layer = layer;
		}

		return copy;
	}
}
