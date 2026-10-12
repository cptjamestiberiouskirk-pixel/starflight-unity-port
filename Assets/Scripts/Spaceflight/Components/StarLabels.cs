
using UnityEngine;
using TMPro;

// the coordinates beside the star systems in hyperspace, as in the UI kit's Hyperspace mockup ("119 · 104" in the kit's small label style): a pool of labels
// that this places beside the stars in view every frame; the texts are made once, so moving the ship makes no new strings
// (it lives in the viewport, made by Starflight Remake > UI Kit > Apply to the Spaceflight Console)
public class StarLabels : MonoBehaviour
{
	// the label the pool is cloned from (hidden)
	public TextMeshProUGUI m_template;

	// how many stars can be labelled at once, and how far right of a star its label starts
	const int c_poolSize = 32;
	const float c_offset = 18.0f;

	// the pool, and the star each label shows (-1 for none)
	TextMeshProUGUI[] m_labels;
	int[] m_labelStar;

	// each star's position and text
	Vector3[] m_starPositions;
	string[] m_starTexts;

	// the area the labels are placed in (the viewport) and the camera that draws the canvas
	RectTransform m_area;
	Camera m_uiCamera;

	// how many labels are showing
	int m_showing;

	// unity start (the game data is loaded by then)
	void Start()
	{
		m_area = transform as RectTransform;

		var canvas = GetComponentInParent<Canvas>();

		m_uiCamera = ( ( canvas != null ) && ( canvas.renderMode != RenderMode.ScreenSpaceOverlay ) ) ? canvas.worldCamera : null;

		// the stars' positions and texts
		var starList = DataController.m_instance.m_gameData.m_starList;

		m_starPositions = new Vector3[ starList.Length ];
		m_starTexts = new string[ starList.Length ];

		for ( var i = 0; i < starList.Length; i++ )
		{
			m_starPositions[ i ] = starList[ i ].GetWorldCoordinates();
			m_starTexts[ i ] = starList[ i ].m_xCoordinate + " · " + starList[ i ].m_yCoordinate;
		}

		// the pool
		m_labels = new TextMeshProUGUI[ c_poolSize ];
		m_labelStar = new int[ c_poolSize ];

		if ( m_template != null )
		{
			for ( var i = 0; i < c_poolSize; i++ )
			{
				m_labels[ i ] = Instantiate( m_template, transform );
				m_labels[ i ].gameObject.SetActive( true );
				m_labels[ i ].enabled = false;
				m_labelStar[ i ] = -1;
			}

			m_template.gameObject.SetActive( false );
		}

		m_showing = 0;
	}

	// unity late update (after the ship and the camera have moved)
	void LateUpdate()
	{
		var controller = SpaceflightController.m_instance;

		if ( ( m_labels == null ) || ( m_template == null ) || ( controller == null ) || ( m_area == null ) || ( DataController.m_instance.m_playerData.m_general.m_location != PD_General.Location.Hyperspace ) )
		{
			HideFrom( 0 );
			return;
		}

		var camera = controller.m_playerCamera.m_camera;

		if ( camera == null )
		{
			HideFrom( 0 );
			return;
		}

		var view = camera.pixelRect;
		var used = 0;

		for ( var star = 0; ( star < m_starPositions.Length ) && ( used < c_poolSize ); star++ )
		{
			var screen = camera.WorldToScreenPoint( m_starPositions[ star ] );

			if ( ( screen.z <= 0.0f ) || !view.Contains( screen ) )
			{
				continue;
			}

			var label = m_labels[ used ];

			// the text only changes when the label is given another star
			if ( m_labelStar[ used ] != star )
			{
				m_labelStar[ used ] = star;
				label.text = m_starTexts[ star ];
			}

			RectTransformUtility.ScreenPointToLocalPointInRectangle( m_area, screen, m_uiCamera, out var local );

			label.rectTransform.localPosition = new Vector3( local.x + c_offset, local.y, 0.0f );
			label.enabled = true;

			used++;
		}

		HideFrom( used );

		m_showing = used;
	}

	// hides the labels from this one on
	void HideFrom( int first )
	{
		if ( m_labels == null )
		{
			return;
		}

		for ( var i = first; i < m_showing; i++ )
		{
			if ( m_labels[ i ] != null )
			{
				m_labels[ i ].enabled = false;
			}
		}

		m_showing = Mathf.Min( m_showing, first );
	}
}
