
using UnityEngine;

// This component creates a floating label above terrain objects when scanned.
// It uses Unity's legacy TextMesh for simple 3D world-space text.
public class TerrainObjectLabel : MonoBehaviour
{
	// the label text object
	GameObject m_labelObject;
	TextMesh m_textMesh;

	// how high above the object to place the label
	const float c_labelHeight = 8.0f;

	// how long the label stays visible (in seconds)
	const float c_labelDuration = 30.0f;

	// timer for auto-hide
	float m_hideTimer;

	// is the label currently visible
	bool m_isVisible;

	// cached camera reference
	Camera m_mainCamera;

	// create and show a label with the given text and color
	public void ShowLabel( string text, Color color )
	{
		// create the label object if it doesn't exist
		if ( m_labelObject == null )
		{
			CreateLabel();
		}

		// set the text and color
		m_textMesh.text = text;
		m_textMesh.color = color;

		// show the label
		m_labelObject.SetActive( true );
		m_isVisible = true;

		// reset the hide timer
		m_hideTimer = c_labelDuration;

		// cache camera
		m_mainCamera = Camera.main;
	}

	// hide the label
	public void HideLabel()
	{
		if ( m_labelObject != null )
		{
			m_labelObject.SetActive( false );
		}

		m_isVisible = false;
	}

	// create the label game object
	void CreateLabel()
	{
		// create a new game object for the label
		m_labelObject = new GameObject( "Label" );

		// don't parent to the object - place at world position instead
		// this avoids issues with object scale/rotation affecting label position
		m_labelObject.transform.position = transform.position + new Vector3( 0, c_labelHeight, 0 );

		// add legacy TextMesh component (simpler, no font asset needed)
		m_textMesh = m_labelObject.AddComponent<TextMesh>();
		m_textMesh.fontSize = 72;
		m_textMesh.characterSize = 0.2f;
		m_textMesh.anchor = TextAnchor.MiddleCenter;
		m_textMesh.alignment = TextAlignment.Center;
		m_textMesh.fontStyle = FontStyle.Bold;

		// try to use Arial font which is built into Unity
		m_textMesh.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );

		// if that fails, try Arial
		if ( m_textMesh.font == null )
		{
			m_textMesh.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		}

		// configure the mesh renderer
		var renderer = m_labelObject.GetComponent<MeshRenderer>();
		if ( renderer != null )
		{
			// use the font's material if available, otherwise create one
			if ( m_textMesh.font != null && m_textMesh.font.material != null )
			{
				renderer.material = m_textMesh.font.material;
			}
			else
			{
				renderer.material = new Material( Shader.Find( "GUI/Text Shader" ) );
			}

			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}
	}

	void Update()
	{
		if ( !m_isVisible || m_labelObject == null )
		{
			return;
		}

		// keep label positioned above the object (since it's not parented)
		m_labelObject.transform.position = transform.position + new Vector3( 0, c_labelHeight, 0 );

		// make the label face the camera (billboard)
		if ( m_mainCamera == null )
		{
			m_mainCamera = Camera.main;
		}

		if ( m_mainCamera != null )
		{
			// billboard - make the label face the camera
			m_labelObject.transform.LookAt( m_mainCamera.transform );
			// flip 180 degrees so text isn't mirrored
			m_labelObject.transform.Rotate( 0, 180, 0 );
		}

		// update hide timer
		m_hideTimer -= Time.deltaTime;

		if ( m_hideTimer <= 0 )
		{
			HideLabel();
		}
	}

	void OnDestroy()
	{
		// clean up the label when the object is destroyed
		if ( m_labelObject != null )
		{
			Destroy( m_labelObject );
		}
	}
}
