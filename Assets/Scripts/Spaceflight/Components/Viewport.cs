
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Viewport : MonoBehaviour
{
	// text elements
	public TextMeshProUGUI m_label;

	// the fade image
	public Image m_fadeImage;

	// the ui canvas
	public Canvas m_canvas;

	// the camera
	public Camera m_camera;

	// the cameras that need to use the render texture
	public Camera[] m_cameras;

	// the fade material
	Material m_fadeMaterial;

	// set to true to run the fade sequence
	bool m_isFading;

	// our fade timer
	float m_fadeTimer;

	// how long to fade
	float m_fadeDuration;

	// the original and target fade
	float m_originalFadeAmount;
	float m_currentFadeAmount;
	float m_targetFadeAmount;

	// the flash of the Black Egg's explosion: the original's main view runs through these colours, again and again, for 3 s (DPART-OV EXPLOSION, ten
	// FLASHCOLORs a turn)
	static readonly Color[] c_flashColors = { Color.black, new Color( 1.0f, 0.5f, 0.0f ), Color.red, new Color( 1.0f, 0.5f, 0.0f ), Color.yellow, new Color( 1.0f, 0.5f, 0.0f ), Color.yellow, Color.white, Color.yellow, Color.white };
	const float c_flashColorTime = 0.05f;
	bool m_isFlashing;
	float m_flashTimer;
	float m_flashDuration;
	Color m_fadeColor;

	// unity awake
	void Awake()
	{
	}

	// unity start
	void Start()
	{
		// make a copy of the fade material so the material file doesn't get updated (a ui image gives us the shared material, not a copy of it)
		m_fadeMaterial = new Material( m_fadeImage.material );
		m_fadeImage.material = m_fadeMaterial;

		// turn off the fade
		m_fadeImage.enabled = false;
	}

	// unity on destroy
	void OnDestroy()
	{
		// the fade material is our own copy so we have to clean it up
		if ( m_fadeMaterial != null )
		{
			Destroy( m_fadeMaterial );
		}
	}

	// unity update
	void Update()
	{
		// get the canvas width in pixels
		var canvasWidth = m_canvas.pixelRect.width;

		// calculate the new rect for a viewport that fits exactly in the hole
		var x = 12.0f / canvasWidth * m_canvas.scaleFactor;
		var y = 12.0f / 1080.0f;
		var w = 1.0f - ( ( 12.0f + 512.0f ) / canvasWidth * m_canvas.scaleFactor );
		var h = 1.0f - ( ( 12.0f + 32.0f + 12.0f + 12.0f ) / 1080.0f );

		var newCameraRect = new Rect( x, y, w, h );

		// update the rect on the player camera
		m_camera.rect = newCameraRect;

		// update all of the encounter cameras
		foreach ( var camera in m_cameras )
		{
			camera.rect = newCameraRect;
		}

		// the explosion's flash
		UpdateFlash();

		// are we fading the viewport?
		if ( m_isFading && !m_isFlashing )
		{
			// is the game paused?
			if ( !SpaceflightController.m_instance.m_gameIsPaused )
			{
				// no - ok update the timer
				m_fadeTimer += Time.deltaTime;

				// are we done?
				if ( m_fadeTimer >= m_fadeDuration )
				{
					// yes - stop fading
					m_isFading = false;

					// turn off the fade object if necessary
					if ( m_targetFadeAmount == 1.0f )
					{
						m_fadeImage.enabled = false;
					}
				}

				// update the current fade amount
				m_currentFadeAmount = m_isFading ? Mathf.SmoothStep( m_originalFadeAmount, m_targetFadeAmount, m_fadeTimer / m_fadeDuration ) : m_targetFadeAmount;

				// update the opacity of the material
				Tools.SetOpacity( m_fadeMaterial, 1.0f - m_currentFadeAmount );
			}
		}
	}

	// call this to flash the main view through the explosion's colours for this long
	public void StartFlash( float duration )
	{
		if ( m_fadeMaterial == null )
		{
			return;
		}

		if ( !m_isFlashing )
		{
			m_fadeColor = m_fadeMaterial.GetColor( "SF_AlbedoColor" );
		}

		m_isFlashing = true;
		m_flashTimer = 0.0f;
		m_flashDuration = duration;

		m_fadeImage.enabled = true;
	}

	// whether the main view is flashing
	public bool IsFlashing()
	{
		return m_isFlashing;
	}

	// the flash, a colour at a time, and the fade as it was afterwards
	void UpdateFlash()
	{
		if ( !m_isFlashing || SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		m_flashTimer += Time.deltaTime;

		if ( m_flashTimer >= m_flashDuration )
		{
			m_isFlashing = false;

			m_fadeMaterial.SetColor( "SF_AlbedoColor", m_fadeColor );

			Tools.SetOpacity( m_fadeMaterial, 1.0f - m_currentFadeAmount );

			m_fadeImage.enabled = m_isFading || ( m_currentFadeAmount < 1.0f );

			return;
		}

		var color = c_flashColors[ Mathf.FloorToInt( m_flashTimer / c_flashColorTime ) % c_flashColors.Length ];

		color.a = 1.0f;

		m_fadeMaterial.SetColor( "SF_AlbedoColor", color );
	}

	// call this to fade in or out the viewport
	public void StartFade( float targetFadeAmount, float fadeDuration )
	{
		// do we want to set it instantly?
		if ( fadeDuration == 0.0f )
		{
			// yes - make it so
			m_currentFadeAmount = targetFadeAmount;

			// update the opacity of the material
			Tools.SetOpacity( m_fadeMaterial, 1.0f - m_currentFadeAmount );

			// make the fade object active if necessary
			m_fadeImage.enabled = targetFadeAmount < 1.0f;

			// if we were previously fading - stop it
			m_isFading = false;
		}
		else
		{
			if ( ( ( m_isFading == false ) && ( targetFadeAmount != m_currentFadeAmount ) ) || ( targetFadeAmount != m_targetFadeAmount ) )
			{
				// no - set up a smooth fade transition
				m_isFading = true;
				m_fadeTimer = 0.0f;
				m_fadeDuration = fadeDuration;
				m_originalFadeAmount = m_currentFadeAmount;
				m_targetFadeAmount = targetFadeAmount;

				// make the fade object active
				m_fadeImage.enabled = true;
			}
		}
	}

	// check whether or not the viewport is still fading (in or out)
	public bool IsFading()
	{
		return m_isFading;
	}

	// call this to get the current viewport fade amount
	public float GetCurrentFadeAmount()
	{
		// return the current fade amount
		return m_currentFadeAmount;
	}

	// call this to update the label
	public void UpdateLabel( string text )
	{
		m_label.text = text;
	}

	// the coordinates shown while the bearings are lost, and when the next ones are picked
	string m_lostCoordinates = "";
	float m_nextLostCoordinatesTime;

	// call this to update the coordinates
	public void UpdateCoordinates()
	{
		var playerData = DataController.m_instance.m_playerData;

		string text;

		if ( playerData.m_general.m_location == PD_General.Location.Disembarked )
		{
			Tools.WorldToLatLongCoordinates( playerData.m_general.m_lastDisembarkedCoordinates, out var x, out var z );

			// north-south first, as the original's coordinates above the main view read ("15N x 30W") - x is the east-west place and z the north-south one
			var eastWest = Mathf.RoundToInt( x );
			var northSouth = Mathf.RoundToInt( z );

			text = Mathf.Abs( northSouth ).ToString() + ( ( northSouth < 0 ) ? "S" : "N" ) + " x " + Mathf.Abs( eastWest ).ToString() + ( ( eastWest < 0 ) ? "W" : "E" );
		}
		else if ( ( SpaceflightController.m_instance != null ) && SpaceflightController.m_instance.HasLostBearings() )
		{
			// the navigator has lost the ship's bearings after a flux jump: the coordinates are anything, new ones a few times a second (the original's HYPER-OV
			// .COORDINATES shows two random numbers at every step while ?CAN-NAVIGATE is false)
			if ( Time.time >= m_nextLostCoordinatesTime )
			{
				m_lostCoordinates = ( ( Random.Range( 0, 1758 ) + 3 ) / 8 ).ToString() + "   " + ( ( Random.Range( 0, 1998 ) + 3 ) / 8 ).ToString();
				m_nextLostCoordinatesTime = Time.time + 0.25f;
			}

			text = m_lostCoordinates;
		}
		else
		{
			var gameCoordinates = Tools.WorldToGameCoordinates( playerData.m_general.m_lastHyperspaceCoordinates );

			var x = Mathf.RoundToInt( gameCoordinates.x );
			var y = Mathf.RoundToInt( gameCoordinates.z );

			text = x.ToString() + "   " + y.ToString();
		}

		UpdateLabel( text );
	}
}
