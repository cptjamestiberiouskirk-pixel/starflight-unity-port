
using UnityEngine;
using TMPro;

// the planets' orbits beside them in the star system, as in the UI kit's Star System mockup ("III · Liquid", in the kit's small label style): a label for each
// orbit, placed just right of its planet's edge every frame while the planet is in view; the texts are made when the star system changes, not every frame
// (it lives in the viewport, made by Starflight Remake > UI Kit > Apply to the Spaceflight Console)
public class PlanetLabels : MonoBehaviour
{
	// the label the others are cloned from (hidden)
	public TextMeshProUGUI m_template;

	// the space between a planet's edge and its label
	const float c_gap = 12.0f;

	// a label for each orbit, the planet it was made for, and the renderer that gives the planet's size
	TextMeshProUGUI[] m_labels;
	int[] m_labelPlanetId;
	Renderer[] m_renderers;

	// the area the labels are placed in (the viewport) and the camera that draws the canvas
	RectTransform m_area;
	Camera m_uiCamera;

	// the orbits' numerals
	static readonly string[] c_numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

	// unity start
	void Start()
	{
		m_area = transform as RectTransform;

		var canvas = GetComponentInParent<Canvas>();

		m_uiCamera = ( ( canvas != null ) && ( canvas.renderMode != RenderMode.ScreenSpaceOverlay ) ) ? canvas.worldCamera : null;

		m_labels = new TextMeshProUGUI[ GD_Star.c_maxNumPlanets ];
		m_labelPlanetId = new int[ GD_Star.c_maxNumPlanets ];
		m_renderers = new Renderer[ GD_Star.c_maxNumPlanets ];

		if ( m_template != null )
		{
			for ( var i = 0; i < m_labels.Length; i++ )
			{
				m_labels[ i ] = Instantiate( m_template, transform );
				m_labels[ i ].gameObject.SetActive( true );
				m_labels[ i ].enabled = false;
				m_labelPlanetId[ i ] = -1;
			}

			m_template.gameObject.SetActive( false );
		}
	}

	// unity late update (after the planets have turned in their orbits and the camera has moved)
	void LateUpdate()
	{
		var controller = SpaceflightController.m_instance;

		if ( ( m_labels == null ) || ( m_template == null ) || ( controller == null ) || ( m_area == null ) || ( controller.m_starSystem == null ) )
		{
			return;
		}

		var location = DataController.m_instance.m_playerData.m_general.m_location;
		var showing = ( location == PD_General.Location.StarSystem ) || ( location == PD_General.Location.JustLaunched );
		var camera = controller.m_playerCamera.m_camera;
		var planets = controller.m_starSystem.m_planetController;

		for ( var i = 0; i < m_labels.Length; i++ )
		{
			var label = m_labels[ i ];

			if ( label == null )
			{
				continue;
			}

			var planetController = ( planets != null ) && ( i < planets.Length ) ? planets[ i ] : null;

			if ( !showing || ( camera == null ) || ( planetController == null ) || ( planetController.m_planet == null ) || !planetController.gameObject.activeInHierarchy || ( planetController.m_planetModel == null ) )
			{
				label.enabled = false;
				continue;
			}

			// the text and the renderer change only when the orbit has another planet (another star system)
			var planet = planetController.m_planet;

			if ( m_labelPlanetId[ i ] != planet.m_id )
			{
				m_labelPlanetId[ i ] = planet.m_id;
				m_renderers[ i ] = planetController.m_planetModel.GetComponentInChildren<Renderer>();

				var orbit = planet.m_orbitPosition - 1;

				label.text = ( ( ( orbit >= 0 ) && ( orbit < c_numerals.Length ) ) ? c_numerals[ orbit ] : "?" ) + " · " + planet.GetSurfaceText();
			}

			// the planet's middle and its edge on the screen
			var centre = planetController.m_planetModel.transform.position;
			var radius = ( m_renderers[ i ] != null ) ? m_renderers[ i ].bounds.extents.x : planetController.m_planetModel.transform.lossyScale.x * 0.5f;
			var screenCentre = camera.WorldToScreenPoint( centre );
			var screenEdge = camera.WorldToScreenPoint( centre + camera.transform.right * radius );

			if ( ( screenCentre.z <= 0.0f ) || !camera.pixelRect.Contains( screenCentre ) )
			{
				label.enabled = false;
				continue;
			}

			RectTransformUtility.ScreenPointToLocalPointInRectangle( m_area, screenCentre, m_uiCamera, out var localCentre );
			RectTransformUtility.ScreenPointToLocalPointInRectangle( m_area, screenEdge, m_uiCamera, out var localEdge );

			label.rectTransform.localPosition = new Vector3( localCentre.x + Mathf.Abs( localEdge.x - localCentre.x ) + c_gap, localCentre.y, 0.0f );
			label.enabled = true;
		}
	}
}
