
using System.Collections.Generic;
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

	// the image of the marks of the science officer's survey and of the Red Cylinder over the map (made when they are first shown)
	RawImage m_surveyMarks;

	// the image of the sites the Red Cylinder flashes over the map, its texture (made when it first flashes), and the sites it was drawn for
	RawImage m_cylinderSites;
	Texture2D m_cylinderSitesTexture;
	List<Vector2> m_drawnCylinderSites;

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

	// unity update
	public override void Update()
	{
		UpdateCylinderSites();
	}

	// unity on destroy
	void OnDestroy()
	{
		// (a texture made at run time is never freed with the scene)
		if ( m_cylinderSitesTexture != null )
		{
			Destroy( m_cylinderSitesTexture );
		}
	}

	// the sites the Red Cylinder flashes blink over the map for a while after it opens (SITE-OV WF290 cycles their colours), drawn once each time
	void UpdateCylinderSites()
	{
		if ( !RedCylinder.IsFlashing() || ( m_map == null ) )
		{
			if ( ( m_cylinderSites != null ) && m_cylinderSites.enabled )
			{
				m_cylinderSites.enabled = false;
			}

			return;
		}

		if ( m_cylinderSites == null )
		{
			m_cylinderSites = MakeOverlay( "Red Cylinder Sites" );
		}

		var sites = RedCylinder.GetSites();

		if ( ( m_cylinderSitesTexture == null ) || ( sites != m_drawnCylinderSites ) )
		{
			if ( m_cylinderSitesTexture == null )
			{
				m_cylinderSitesTexture = new Texture2D( SurveyMarks.c_width, SurveyMarks.c_height, TextureFormat.RGBA32, true );
				m_cylinderSitesTexture.name = "Red Cylinder Sites";
				m_cylinderSitesTexture.wrapModeU = TextureWrapMode.Repeat;
				m_cylinderSitesTexture.wrapModeV = TextureWrapMode.Clamp;
				m_cylinderSitesTexture.filterMode = FilterMode.Trilinear;
			}

			// the sites as the survey marks draw the ancient ruins, boxes in the danger colour of the scheme drawn over the views
			SurveyMarks.Draw( m_cylinderSitesTexture, null, sites, UIKitTheme.GetViewTheme() );

			m_cylinderSites.texture = m_cylinderSitesTexture;
			m_drawnCylinderSites = sites;
		}

		// on and off four times a second
		m_cylinderSites.enabled = ( Mathf.FloorToInt( Time.time * 8.0f ) % 2 ) == 0;
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
		if ( m_map == null )
		{
			return;
		}

		// (the marks the sensor picture shows too: SurveyMarks keeps them)
		var texture = SurveyMarks.GetTexture();

		if ( texture == null )
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

		m_surveyMarks.texture = texture;
		m_surveyMarks.enabled = true;
	}

	// makes the image of the marks
	void MakeSurveyMarks()
	{
		m_surveyMarks = MakeOverlay( "Survey Marks" );
	}

	// makes an image over the map: a child of the map that covers it, drawn over it and under the crosshairs
	RawImage MakeOverlay( string name )
	{
		var marksObject = new GameObject( name, typeof( RectTransform ) );

		marksObject.layer = m_map.gameObject.layer;

		var rectTransform = marksObject.GetComponent<RectTransform>();

		rectTransform.SetParent( m_map.rectTransform, false );
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;

		// (white: the texture has the colours, and the kit's scheme never takes white for one of its own)
		var image = marksObject.AddComponent<RawImage>();

		image.color = Color.white;
		image.raycastTarget = false;

		return image;
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
