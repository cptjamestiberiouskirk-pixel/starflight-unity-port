
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TerrainMapDisplay : ShipDisplay
{
	// the map
	public Image m_map;

	// the legends
	public Image[] m_legendImages;

	// select site stuff
	public GameObject m_selectSite;

	// select site label
	public GameObject m_selectSiteLabel;

	// the crosshairs
	public GameObject m_horizontalCrosshair;
	public GameObject m_verticalCrosshair;

	// the longitude and latitude text
	public TextMeshProUGUI m_latitudeText;
	public TextMeshProUGUI m_longitudeText;

	// controls moving of the crosshair`
	public float m_maxCrosshairMoveVelocity;
	public float m_crosshairMoveAcceleration;

	// the map material
	Material m_mapMaterial;

	// crosshair move velocity
	float m_crosshairVelocity;

	// keep track of whether we have already initialized
	bool m_initialized;

	// the marks of the science officer's survey and of the Red Cylinder over the map (made when they are first shown), and what they were last drawn for
	// (they are drawn again only when one of these changes)
	RawImage m_surveyMarks;
	Texture2D m_surveyMarksTexture;
	PlanetSurvey m_drawnSurvey;
	int m_drawnPlanetId = -1;
	int m_drawnRuinCount = -1;
	UIKitTheme m_drawnTheme;

	// unity start
	public override void Start()
	{
	}

	// unity on enable
	void OnEnable()
	{
		// the marks take the colours of the kit's scheme, also when the player picks another one
		UIKitTheme.Changed -= ThemeChanged;
		UIKitTheme.Changed += ThemeChanged;
	}

	// unity on disable
	void OnDisable()
	{
		UIKitTheme.Changed -= ThemeChanged;
	}

	// unity on destroy
	void OnDestroy()
	{
		// the marks' texture goes with the scene
		if ( m_surveyMarksTexture != null )
		{
			Destroy( m_surveyMarksTexture );
		}
	}

	// unity update
	public override void Update()
	{
	}

	// the display label
	public override string GetLabel()
	{
		return "Terrain Map";
	}

	// show
	public override void Show()
	{
		// call base show
		base.Show();

		// initialize
		Initialize();

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get the planet controller
		var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

		// get to the planet controller's material
		var planetMaterial = planetController.GetMaterial();

		// copy some of the material maps over
		m_mapMaterial.SetTexture( "SF_WaterMaskMap", planetMaterial.GetTexture( "SF_WaterMaskMap" ) );
		m_mapMaterial.SetTexture( "_MainTex", planetMaterial.GetTexture( "_MainTex" ) );
		m_mapMaterial.SetTexture( "SF_SpecularMap", planetMaterial.GetTexture( "SF_SpecularMap" ) );
		m_mapMaterial.SetTexture( "SF_NormalMap", planetMaterial.GetTexture( "SF_NormalMap" ) );

		// hide all the legends
		foreach ( var legendImage in m_legendImages )
		{
			legendImage.enabled = false;
		}

		// get the surface id from the planet controller
		var surfaceId = planetController.GetSurfaceId();

		// enable the correct legend
		m_legendImages[ surfaceId ].enabled = true;

		// hide the select site stuff
		m_selectSite.SetActive( false );

		// mark what the science officer's survey and the Red Cylinder have found
		ShowSurveyMarks();
	}

	// marks the mineral fields and the lifeforms of the science officer's survey of the planet, and the ancient ruins the Red Cylinder finds on it, over the
	// map (decided 2026-10-17, ROADMAP D6): nothing before the planet has been analysed from orbit, and no ruins without the Red Cylinder
	void ShowSurveyMarks()
	{
		var controller = SpaceflightController.m_instance;

		if ( ( controller == null ) || ( m_map == null ) )
		{
			return;
		}

		var planetId = DataController.m_instance.m_playerData.m_general.m_currentPlanetId;

		// (the survey kept is of the planet analysed last, which may be another)
		var survey = ( ( controller.m_planetSurvey != null ) && ( controller.m_planetSurvey.m_planetId == planetId ) ) ? controller.m_planetSurvey : null;
		var ruins = RedCylinder.GetAncientRuinPlaces();
		var hasMarks = ( ( survey != null ) && ( survey.m_marks.Count > 0 ) ) || ( ruins.Count > 0 );

		if ( !hasMarks )
		{
			if ( m_surveyMarks != null )
			{
				m_surveyMarks.enabled = false;
			}

			return;
		}

		if ( m_surveyMarks == null )
		{
			MakeSurveyMarks();
		}

		m_surveyMarks.enabled = true;

		var theme = UIKitTheme.GetViewTheme();

		if ( ( survey == m_drawnSurvey ) && ( planetId == m_drawnPlanetId ) && ( ruins.Count == m_drawnRuinCount ) && ( theme == m_drawnTheme ) )
		{
			return;
		}

		// (the map shows the planet's ground, dark in either mode, so the marks take the colours drawn over the views)
		SurveyMarks.Draw( m_surveyMarksTexture, survey, ruins, theme );

		m_drawnSurvey = survey;
		m_drawnPlanetId = planetId;
		m_drawnRuinCount = ruins.Count;
		m_drawnTheme = theme;
	}

	// makes the image of the marks: a child of the map that covers it, drawn over it and under the crosshairs
	void MakeSurveyMarks()
	{
		var marksObject = new GameObject( "Survey Marks", typeof( RectTransform ) );

		marksObject.layer = m_map.gameObject.layer;

		var rectTransform = marksObject.GetComponent<RectTransform>();

		rectTransform.SetParent( m_map.rectTransform, false );
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;

		m_surveyMarksTexture = new Texture2D( SurveyMarks.c_width, SurveyMarks.c_height, TextureFormat.RGBA32, false );

		m_surveyMarksTexture.name = "Survey Marks";
		m_surveyMarksTexture.wrapMode = TextureWrapMode.Clamp;
		m_surveyMarksTexture.filterMode = FilterMode.Bilinear;

		// (white: the texture has the colours, and the kit's scheme never takes white for one of its own)
		m_surveyMarks = marksObject.AddComponent<RawImage>();

		m_surveyMarks.texture = m_surveyMarksTexture;
		m_surveyMarks.color = Color.white;
		m_surveyMarks.raycastTarget = false;
	}

	// another scheme or mode was picked: the marks are drawn again in its colours
	void ThemeChanged()
	{
		if ( ( m_surveyMarks != null ) && m_surveyMarks.enabled && gameObject.activeInHierarchy )
		{
			ShowSurveyMarks();
		}
	}

	// hide
	public override void Hide()
	{
		// call base hide
		base.Hide();
	}

	// initialize
	void Initialize()
	{
		if ( m_initialized )
		{
			return;
		}

		// make an instance of the material so we don't permanently change its settings
		m_mapMaterial = new Material( m_map.material );

		m_map.material = m_mapMaterial;

		// hide the crosshairs
		HideSelectSite();

		// remember that we have already initialized
		m_initialized = true;
	}

	// show the select site stuff
	public void ShowSelectSite()
	{
		m_selectSite.SetActive( true );

		m_selectSiteLabel.SetActive( true );
	}

	// hide the select site stuff
	public void HideSelectSite()
	{
		m_selectSiteLabel.SetActive( false );
	}

	// the crosshair velocity is in degrees per frame at this many frames per second (the values set in the editor were made for it)
	const float c_crosshairFrameRate = 60.0f;

	// move the crosshairs
	public void MoveCrosshairs()
	{
		var playerData = DataController.m_instance.m_playerData;

		var crosshairsMoved = false;

		var x = InputController.m_instance.m_x;
		var y = InputController.m_instance.m_y;

		// how far the crosshair moves in this frame (the velocity itself was added every frame, so the crosshair was faster the more frames there were in a second)
		var crosshairStep = m_crosshairVelocity * Time.deltaTime * c_crosshairFrameRate;

		if ( x < -0.5f )
		{
			crosshairsMoved = true;

			playerData.m_general.m_selectedLatitude -= crosshairStep;

			playerData.m_general.m_selectedLatitude = Mathf.Max( -180.0f, playerData.m_general.m_selectedLatitude );
		}
		else if ( x > 0.5f )
		{
			crosshairsMoved = true;

			playerData.m_general.m_selectedLatitude += crosshairStep;

			playerData.m_general.m_selectedLatitude = Mathf.Min( 179.0f, playerData.m_general.m_selectedLatitude );
		}

		if ( y < -0.5f )
		{
			crosshairsMoved = true;

			playerData.m_general.m_selectedLongitude -= crosshairStep;

			playerData.m_general.m_selectedLongitude = Mathf.Max( -90.0f, playerData.m_general.m_selectedLongitude );
		}
		else if ( y > 0.5f )
		{
			crosshairsMoved = true;

			playerData.m_general.m_selectedLongitude += crosshairStep;

			playerData.m_general.m_selectedLongitude = Mathf.Min( 90.0f, playerData.m_general.m_selectedLongitude );
		}

		if ( crosshairsMoved )
		{
			// speed up, but not past the top speed (which was set in the editor and never used, so the crosshair kept getting faster)
			m_crosshairVelocity = Mathf.Min( m_maxCrosshairMoveVelocity, m_crosshairVelocity + Time.deltaTime * m_crosshairMoveAcceleration );
		}
		else
		{
			m_crosshairVelocity = Time.deltaTime * m_crosshairMoveAcceleration;
		}

		// the Red Cylinder flashes near an ancient ruin
		RedCylinder.Look( playerData.m_general.m_selectedLatitude, playerData.m_general.m_selectedLongitude );

		// the latitude is the north-south place and the longitude the east-west one, as the original's map gives them ("LAT 15N  LONG 30W") - the port's
		// m_selectedLatitude holds the east-west place and its m_selectedLongitude the north-south one, so each text takes the other
		m_latitudeText.text = Mathf.RoundToInt( Mathf.Abs( playerData.m_general.m_selectedLongitude ) ).ToString() + ( ( playerData.m_general.m_selectedLongitude < 0.0f ) ? "S" : "N" );
		m_longitudeText.text = Mathf.FloorToInt( Mathf.Abs( playerData.m_general.m_selectedLatitude ) ).ToString() + ( ( playerData.m_general.m_selectedLatitude < 0.0f ) ? "W" : "E" );

		x = playerData.m_general.m_selectedLatitude / 180.0f * 224.0f;
		y = playerData.m_general.m_selectedLongitude / 90.0f * 112.0f;

		m_verticalCrosshair.transform.localPosition = new Vector3( x - 16.0f, 0.0f, 0.0f );
		m_horizontalCrosshair.transform.localPosition = new Vector3( -16.0f, y, 0.0f );
	}
}
