
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// a small window with a turning view of artifact models, drawn into a texture that a RawImage in a panel shows (the Trade Depot's selected artifact, the
// special artifacts in a cargo hold): the models stand side by side on a stage far below every scene, on the Artifact Preview layer, which only the view's
// own camera and light see; the window is a 3D view, so it keeps the dark background of the scheme's views in the light modes too
public class ArtifactPreview : MonoBehaviour
{
	// the layer of the stages (ProjectSettings: "Artifact Preview"), which no scene camera draws
	public const int c_layer = 16;

	// where the stages are (each view has its own, one below the other)
	const float c_stageDepth = -20000.0f;
	const float c_stageSpacing = 100.0f;
	const int c_stageSlots = 64;

	// how far apart the models stand (each is one unit across), how fast they turn (degrees a second), and how far above the camera looks down (degrees)
	const float c_spacing = 1.35f;
	const float c_turnSpeed = 40.0f;
	const float c_lookDown = 20.0f;

	// how thick the window's frame is (canvas units)
	const float c_frameThickness = 3.0f;

	// the stages made so far (the next one goes below them)
	static int s_stageCount;

	// the window's picture, the texture it shows, the camera that draws it, and the stage the models stand on
	RawImage m_image;
	RenderTexture m_texture;
	Camera m_camera;
	Transform m_stage;

	// what is shown (artifact ids, in order), the models showing them, and how far they have turned
	readonly List<int> m_shownIds = new List<int>();
	readonly List<GameObject> m_models = new List<GameObject>();
	float m_turn;

	/// <summary>
	/// Makes a framed window at this rect of a panel (in the parent's own units, anchored at its middle), its texture this many pixels across and down. It
	/// shows nothing until Show is given an artifact with a model.
	/// </summary>
	public static ArtifactPreview Make( RectTransform parent, string name, Rect rect, int width, int height )
	{
		// the frame, in the kit's border colour (a control of the panel, so in the panel's own theme)
		var frameObject = new GameObject( name, typeof( RectTransform ) );
		var frameTransform = frameObject.GetComponent<RectTransform>();

		frameTransform.SetParent( parent, false );
		frameTransform.anchorMin = new Vector2( 0.5f, 0.5f );
		frameTransform.anchorMax = new Vector2( 0.5f, 0.5f );
		frameTransform.pivot = new Vector2( 0.5f, 0.5f );
		frameTransform.anchoredPosition = rect.center;
		frameTransform.sizeDelta = rect.size;

		var frameImage = frameObject.AddComponent<Image>();

		frameImage.raycastTarget = false;

		var frameStyle = frameObject.AddComponent<UIKitStyle>();

		frameStyle.m_color = UIKitTheme.ColorRole.BorderDefault;
		frameStyle.Apply();

		// the view inside it
		var viewObject = new GameObject( name + " View", typeof( RectTransform ) );
		var viewTransform = viewObject.GetComponent<RectTransform>();

		viewTransform.SetParent( frameTransform, false );
		viewTransform.anchorMin = Vector2.zero;
		viewTransform.anchorMax = Vector2.one;
		viewTransform.offsetMin = new Vector2( c_frameThickness, c_frameThickness );
		viewTransform.offsetMax = new Vector2( -c_frameThickness, -c_frameThickness );

		var preview = frameObject.AddComponent<ArtifactPreview>();

		preview.m_texture = new RenderTexture( width, height, 16, RenderTextureFormat.ARGB32 ) { name = name, antiAliasing = 4 };

		// (white: the texture has the colours)
		preview.m_image = viewObject.AddComponent<RawImage>();
		preview.m_image.texture = preview.m_texture;
		preview.m_image.color = Color.white;
		preview.m_image.raycastTarget = false;

		// the stage, its camera looking a little down at it, and its light
		var stageObject = new GameObject( name + " Stage" );

		stageObject.transform.position = new Vector3( 0.0f, c_stageDepth - ( s_stageCount % c_stageSlots ) * c_stageSpacing, 0.0f );
		s_stageCount++;
		preview.m_stage = stageObject.transform;

		var cameraObject = new GameObject( name + " Camera" );

		cameraObject.transform.SetParent( stageObject.transform, false );
		cameraObject.transform.localRotation = Quaternion.Euler( c_lookDown, 0.0f, 0.0f );
		cameraObject.transform.localPosition = cameraObject.transform.localRotation * new Vector3( 0.0f, 0.0f, -10.0f );

		// (forward, so that the texture keeps its smoothed edges: deferred shading draws without them)
		preview.m_camera = cameraObject.AddComponent<Camera>();
		preview.m_camera.renderingPath = RenderingPath.Forward;
		preview.m_camera.orthographic = true;
		preview.m_camera.orthographicSize = 0.75f;
		preview.m_camera.clearFlags = CameraClearFlags.SolidColor;
		preview.m_camera.cullingMask = 1 << c_layer;
		preview.m_camera.nearClipPlane = 1.0f;
		preview.m_camera.farClipPlane = 20.0f;
		preview.m_camera.targetTexture = preview.m_texture;
		preview.m_camera.enabled = false;

		var lightObject = new GameObject( name + " Light" );

		lightObject.transform.SetParent( stageObject.transform, false );
		lightObject.transform.localRotation = Quaternion.Euler( 40.0f, -35.0f, 0.0f );

		// (one light: the SF shaders have no pass for a second one in forward rendering)
		var light = lightObject.AddComponent<Light>();

		light.type = LightType.Directional;
		light.intensity = 1.6f;
		light.cullingMask = 1 << c_layer;

		preview.ApplyTheme();
		preview.UpdateVisibility();

		return preview;
	}

	/// <summary>
	/// Shows these artifacts (ids into the game data's artifact list) side by side, turning (those without a model are left out; with none to show, the
	/// window is hidden).
	/// </summary>
	public void Show( IList<int> artifactIds )
	{
		var gameData = DataController.m_instance.m_gameData;
		var ids = new List<int>();

		if ( artifactIds != null )
		{
			foreach ( var artifactId in artifactIds )
			{
				if ( ( artifactId >= 0 ) && ( artifactId < gameData.m_artifactList.Length ) && ( ArtifactModels.Get( gameData.m_artifactList[ artifactId ] ) != null ) )
				{
					ids.Add( artifactId );
				}
			}
		}

		// (made again only when what is shown changes)
		if ( !IsSameList( ids, m_shownIds ) )
		{
			foreach ( var model in m_models )
			{
				if ( model != null )
				{
					Destroy( model );
				}
			}

			m_models.Clear();
			m_shownIds.Clear();

			for ( var i = 0; i < ids.Count; i++ )
			{
				var artifact = gameData.m_artifactList[ ids[ i ] ];
				var holder = new GameObject( "Turning " + artifact.m_name );

				holder.layer = c_layer;
				holder.transform.SetParent( m_stage, false );
				holder.transform.localPosition = new Vector3( ( i - ( ids.Count - 1 ) * 0.5f ) * c_spacing, 0.0f, 0.0f );
				holder.transform.localRotation = Quaternion.Euler( 0.0f, m_turn, 0.0f );

				ArtifactModels.Make( artifact, holder.transform, 1.0f, c_layer );

				m_models.Add( holder );
				m_shownIds.Add( ids[ i ] );
			}

			// the camera takes in the row (each model a little smaller than its place)
			var aspect = ( m_texture.height > 0 ) ? (float) m_texture.width / m_texture.height : 1.0f;

			m_camera.orthographicSize = Mathf.Max( 0.75f, ( ids.Count * c_spacing * 0.5f ) / Mathf.Max( 0.01f, aspect ) );
		}

		UpdateVisibility();
	}

	// the artifacts shown (ids into the game data's artifact list)
	public IList<int> GetShownIds()
	{
		return m_shownIds;
	}

	// true while the window shows something
	public bool IsShowing()
	{
		return isActiveAndEnabled && ( m_image != null ) && m_image.enabled && ( m_camera != null ) && m_camera.enabled;
	}

	// two lists of ids with the same ids in the same order
	static bool IsSameList( List<int> a, List<int> b )
	{
		if ( a.Count != b.Count )
		{
			return false;
		}

		for ( var i = 0; i < a.Count; i++ )
		{
			if ( a[ i ] != b[ i ] )
			{
				return false;
			}
		}

		return true;
	}

	// the window and its camera are on only while there is something to show and the panel is up (the stage is not under the panel, so its camera would
	// go on drawing after the panel closed)
	void UpdateVisibility()
	{
		var showing = isActiveAndEnabled && ( m_models.Count > 0 );

		if ( m_camera != null )
		{
			m_camera.enabled = showing;
		}

		if ( m_image != null )
		{
			m_image.enabled = showing;
		}

		var frameImage = GetComponent<Image>();

		if ( frameImage != null )
		{
			frameImage.enabled = showing;
		}
	}

	// the background of the scheme's views (dark in either mode)
	void ApplyTheme()
	{
		var theme = UIKitTheme.GetViewTheme();

		if ( ( m_camera != null ) && ( theme != null ) )
		{
			var background = theme.m_backgroundApp;

			background.a = 1.0f;
			m_camera.backgroundColor = background;
		}
	}

	// unity on enable
	void OnEnable()
	{
		UIKitTheme.Changed -= ApplyTheme;
		UIKitTheme.Changed += ApplyTheme;

		ApplyTheme();
		UpdateVisibility();
	}

	// unity on disable
	void OnDisable()
	{
		UIKitTheme.Changed -= ApplyTheme;

		UpdateVisibility();
	}

	// unity update
	void Update()
	{
		if ( m_models.Count == 0 )
		{
			return;
		}

		// (real time: the Starport has no pause, and a paused game stops its clock)
		m_turn += Time.unscaledDeltaTime * c_turnSpeed;

		foreach ( var model in m_models )
		{
			if ( model != null )
			{
				model.transform.localRotation = Quaternion.Euler( 0.0f, m_turn, 0.0f );
			}
		}
	}

	// unity on destroy
	void OnDestroy()
	{
		if ( m_stage != null )
		{
			Destroy( m_stage.gameObject );
		}

		if ( m_texture != null )
		{
			m_texture.Release();
			Destroy( m_texture );
		}
	}
}
