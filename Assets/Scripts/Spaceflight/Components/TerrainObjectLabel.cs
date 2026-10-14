
using UnityEngine;
using TMPro;

// This component creates a floating label above terrain objects when scanned.
// It is TextMesh Pro world-space text in the UI kit's label style (Rajdhani, capitals, spaced), with a dark outline so that it reads on any ground.
public class TerrainObjectLabel : MonoBehaviour
{
	// the label text object
	GameObject m_labelObject;
	TextMeshPro m_textMesh;

	// the label's own copy of its font material (for the outline), destroyed with it
	Material m_material;

	// how high above the object to place the label
	const float c_labelHeight = 8.0f;

	// the size of the label's text in world units (a little larger than the legacy label's 72 point Arial at a character size of 0.2, as the kit's capitals are narrower), and the width of its outline
	const float c_fontSize = 20.0f;
	const float c_outlineWidth = 0.3f;

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

		// the text, in the UI kit's small label style (its font, capitals and spacing; the size is the label's own, in world units)
		m_textMesh = m_labelObject.AddComponent<TextMeshPro>();

		// (over the terrain, so in the theme of the 3D views: dark in either mode)
		var theme = UIKitTheme.GetViewTheme();

		if ( theme != null )
		{
			theme.m_labelS.Apply( m_textMesh );
		}

		m_textMesh.fontSize = c_fontSize;
		m_textMesh.alignment = TextAlignmentOptions.Center;
		m_textMesh.textWrappingMode = TextWrappingModes.NoWrap;
		m_textMesh.rectTransform.sizeDelta = new Vector2( 40.0f, 8.0f );

		// a dark outline (on the label's own copy of the font material, which it destroys)
		m_material = m_textMesh.fontMaterial;
		m_textMesh.outlineWidth = c_outlineWidth;
		m_textMesh.outlineColor = ( theme != null ) ? (Color32) theme.m_backgroundApp : new Color32( 0, 0, 0, 255 );

		// configure the mesh renderer
		var renderer = m_labelObject.GetComponent<MeshRenderer>();

		if ( renderer != null )
		{
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

	void OnDisable()
	{
		// the label is not a child of this object, so it does not go away with it - hide it when this object is hidden
		// (labels used to stay in the air after the player had left the terrain vehicle, and nothing counted their time down any more)
		HideLabel();
	}

	void OnDestroy()
	{
		// clean up the label when the object is destroyed
		if ( m_labelObject != null )
		{
			Destroy( m_labelObject );
		}

		// and its copy of the font material
		if ( m_material != null )
		{
			Destroy( m_material );
		}
	}
}
