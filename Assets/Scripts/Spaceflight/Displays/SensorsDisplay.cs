
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SensorsDisplay : ShipDisplay
{
	public enum ScanType
	{
		Player,
		SpeminTransport,
		SpeminScout,
		SpeminWarship,
		MechanScout,
		ElowanTransport,
		ElowanScout,
		ElowanWarship,
		ThrynnTransport,
		ThrynnScout,
		ThrynnWarship,
		VeloxiTransport,
		VeloxiScout,
		VeloxiWarship,
		GazurtoidScout,
		GazurtoidWarship,
		UhlekScout,
		UhlekWarship,
		VeloxDrone,
		NomadProbe,
		Mysterion,
		TheEnterprise,
		Minstrel,
		NoahTransport,
		Debris,
		Planet,
		Unknown
	};

	// below 10^22 tons the original draws a planet's disc this many pixels across, plus this many for each unit of the leading digit of its mass
	const float c_planetDiscBasePixels = 2.0f;
	const float c_planetDiscPixelsPerDigit = 4.0f;

	// from 10^22 tons up the original draws the disc this many pixels across
	const int c_fullPlanetDiscMassPower = 22;
	const float c_fullPlanetDiscPixels = 62.0f;

	// the original's window is 57 pixels high, and on a 4:3 screen its pixels are 1.2 times as high as they are wide
	const float c_originalWindowHeight = 57.0f * 1.2f;

	// the disc of the planet mask is 501 of its 512 rows high
	const float c_planetMaskDiscFraction = 501.0f / 512.0f;

	// the mass text
	public TextMeshProUGUI m_massText;

	// the bio / min text
	public TextMeshProUGUI m_bioMinText;

	// the instructions text
	public TextMeshProUGUI m_instructionsText;

	// the background image
	public Image m_backgroundImage;

	// the scan image (this should be using custom mask image shader)
	public Image m_maskImage;

	// the magenta panel of the sensor window
	public Image m_panelImage;

	// how wide the magenta border around an empty window is (the original's is 3 of the window's 90 pixels)
	public float m_emptyBorderWidth = 16.0f;

	// the transparent edge of the panel's sprite, in the sprite's pixels ("Panel - Background" is 128 x 128 and opaque from its 7th pixel in)
	public float m_panelSpriteMargin = 6.0f;

	// the background textures for the various scan types
	public Texture[] m_backgroundTextures;

	// the masks for the various scan types
	public Texture[] m_maskTextures;

	// the background textures for the debris each vessel leaves (indexed by vessel id)
	public Texture[] m_debrisBackgroundTextures;

	// the masks for the debris each vessel leaves (indexed by vessel id)
	public Texture[] m_debrisMaskTextures;

	// how fast to cycle the colors
	public float m_colorCycleSpeed;

	// minimum duration for scanning
	public float m_minDuration;

	// maximum duration for scanning
	public float m_maxDuration;

	// do we have sensor data to analyze?
	public bool m_hasSensorData;

	// what are we currently scanning
	public ScanType m_scanType;

	// the background material
	Material m_backgroundMaterial;

	// the mask material
	Material m_maskMaterial;

	// the inside of the window when a scan has no picture: black inside a magenta border, as the original shows an unidentified object
	// (a copy of the panel, made the first time the display is shown)
	Image m_emptyImage;

	// clips the pictures to the magenta panel (a picture is 640 units wide, the panel 480, so the noise of a wide picture would show beside the window)
	// (added the first time the display is shown; the backgrounds multiply what is under them, so only the noise needs it)
	RectMask2D m_windowClip;

	// are we running the cinematics?
	bool m_isDoingCinematics;

	// mass power base (18 for planets, 0 for ships and debris, whose mass in the game data is in tons)
	int m_massPowerBase;

	// mass of object we are scanning
	int m_mass;

	// bio density of object we are scanning
	int m_bioDensity;

	// mineral density of object we are scanning
	int m_mineralDensity;

	// have we stopped the sensor sound yet?
	bool m_soundStopped;

	// our timer
	float m_timer;

	// unity start
	public override void Start()
	{
	}

	// unity update
	public override void Update()
	{
		// are we doing the cinematics?
		if ( !m_isDoingCinematics )
		{
			// no - don't do anything here
			return;
		}

		// yes -update the timer
		m_timer += Time.deltaTime;

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// calculate the duration of the scan
		var scanDuration = Mathf.Lerp( m_minDuration, m_maxDuration, ( m_bioDensity + m_mineralDensity ) / 200.0f );

		// calculate alpha
		var alpha = Mathf.SmoothStep( 0.0f, 1.0f, m_timer / scanDuration );

		// have we stopped the scanning sound yet?
		if ( !m_soundStopped )
		{
			// no - is it time to stop the sound?
			if ( m_timer > ( scanDuration - 1.0f ) )
			{
				// yes - stop it now
				m_soundStopped = true;

				// stop the scanning sound
				SoundController.m_instance.StopSound( SoundController.Sound.Scanning );
			}
		}

		// is it time to stop the cinematics?
		if ( m_timer >= scanDuration )
		{
			// cap the timer
			m_timer = scanDuration;

			// the cinematics is over - set alpha to 1
			alpha = 1.0f;

			// turn off cinematics
			m_isDoingCinematics = false;

			// deactivate the sensor button
			SpaceflightController.m_instance.m_buttonController.DeactivateButton();

			// play the activate sound
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

			// we have something to analyze
			m_hasSensorData = true;
		}

		// calculate the new color multiplier for the noise
		var r = ( alpha + ( 1 - alpha ) * Mathf.Abs( Mathf.Sin( m_timer * m_colorCycleSpeed + ( 2.0f * Mathf.PI / 3.0f ) * 1.0f ) ) ) * alpha;
		var g = ( alpha + ( 1 - alpha ) * Mathf.Abs( Mathf.Sin( m_timer * m_colorCycleSpeed + ( 2.0f * Mathf.PI / 3.0f ) * 2.0f ) ) ) * alpha;
		var b = ( alpha + ( 1 - alpha ) * Mathf.Abs( Mathf.Sin( m_timer * m_colorCycleSpeed + ( 2.0f * Mathf.PI / 3.0f ) * 3.0f ) ) ) * alpha;

		// set the new image color
		m_maskImage.color = new Color( r, g, b );

		// give the main (noise) texture a random posititon
		m_maskMaterial.SetTextureOffset( "_MainTex", new Vector2( Random.Range( 0.0f, 1.0f ), Random.Range( 0.0f, 1.0f ) ) );

		// is the cinematics done?
		if ( !m_isDoingCinematics )
		{
			// what were we scanning?

			switch ( m_scanType )
			{
				case ScanType.Planet:
				{
					// get the current planet
					var planet = gameData.m_planetList[ playerData.m_general.m_currentPlanetId ];

					// get the planet info
					var atmosphere = planet.GetAtmosphereText();
					var hydrosphere = planet.GetHydrosphereText();
					var lithosphere = planet.GetLithosphereText();

					// update the messages text
					SpaceflightController.m_instance.m_messages.Clear();
					SpaceflightController.m_instance.m_messages.AddText( "Atmosphere:\n<color=white>" + atmosphere + "</color>\nHydrosphere:\n<color=white>" + hydrosphere + "</color>\nLithosphere:\n<color=white>" + lithosphere + "</color>" );

					break;
				}

				case ScanType.Debris:
				{
					// debris scanning shows salvageable materials
					SpaceflightController.m_instance.m_messages.Clear();

					string text = "<color=#FFFF00>Debris Analysis:</color>\n";
					text += "<color=white>Wreckage detected.</color>\n";
					text += "Salvage potential: <color=white>" + m_mineralDensity + "%</color>\n";
					text += "<color=#808080>Debris may contain recoverable materials.</color>";

					SpaceflightController.m_instance.m_messages.AddText( text );

					break;
				}

				case ScanType.Unknown:
				{
					SpaceflightController.m_instance.m_messages.Clear();
					SpaceflightController.m_instance.m_messages.AddText( "<color=#FFFF00>Scanners indicate unidentified object!</color>" );

					break;
				}

				default:
				{
					// the scan type is the vessel id - make sure it is one we have data for
					var vesselId = (int) m_scanType;

					if ( ( vesselId < 0 ) || ( vesselId >= gameData.m_vesselList.Length ) )
					{
						SpaceflightController.m_instance.m_messages.Clear();
						SpaceflightController.m_instance.m_messages.AddText( "<color=#FFFF00>Scanners indicate unidentified object!</color>" );

						break;
					}

					// display the ship information
					var vessel = gameData.m_vesselList[ vesselId ];

					string text = "Object Constituents:";

					if ( vessel.m_enduriumVolume > 0 )
					{
						var elementName = gameData.m_elementList[ gameData.m_misc.m_enduriumElementId ].m_name;

						text += "\n<color=white>" + elementName + "</color>";
					}

					if ( vessel.m_elementVolumeA > 0 )
					{
						var elementName = gameData.m_elementList[ vessel.m_elementIdA ].m_name;

						text += "\n<color=white>" + elementName + "</color>";
					}

					if ( vessel.m_elementVolumeB > 0 )
					{
						var elementName = gameData.m_elementList[ vessel.m_elementIdB ].m_name;

						text += "\n<color=white>" + elementName + "</color>";
					}

					if ( vessel.m_elementVolumeC > 0 )
					{
						var elementName = gameData.m_elementList[ vessel.m_elementIdC ].m_name;

						text += "\n<color=white>" + elementName + "</color>";
					}

					// update the messages text
					SpaceflightController.m_instance.m_messages.Clear();
					SpaceflightController.m_instance.m_messages.AddText( text );

					break;
				}
			}
		}

		// calculate the scanned amounts
		var scannedMass = Mathf.Lerp( 1.0f, m_mass, Mathf.Pow( m_timer / scanDuration, 4.0f ) );
		var scannedBio = Mathf.Lerp( 0.0f, m_bioDensity, m_timer / scanDuration );
		var scannedMinerals = Mathf.Lerp( 0.0f, m_mineralDensity, m_timer / scanDuration );

		// update the mass text
		var massLength = Mathf.RoundToInt( scannedMass ).ToString().Length;
		var massPower = m_massPowerBase + massLength - 1;
		var massBase = Mathf.FloorToInt( scannedMass / Mathf.Pow( 10.0f, massLength - 1 ) );

		// a mass under ten tons is shown as it is, with no power of ten (the original shows the minstrel as "2")
		if ( massPower <= 0 )
		{
			m_massText.text = "Mass: <color=\"white\">" + Mathf.RoundToInt( scannedMass ) + "</color> Tons";
		}
		else
		{
			m_massText.text = "Mass: <color=\"white\">" + massBase + "x10<sup>" + massPower + "</sup></color> Tons";
		}

		// update the bio text (the sensors read the minerals of a planet or of debris, and the energy of a vessel, as in the original)
		var secondLabel = IsVessel( m_scanType ) ? "Energy" : "Min";

		// the original shows plain numbers here, with no percent sign
		m_bioMinText.text = "Bio: <color=\"white\">" + Mathf.RoundToInt( scannedBio ) + "</color>   " + secondLabel + ": <color=\"white\">" + Mathf.RoundToInt( scannedMinerals ) + "</color>";
	}

	// returns true if this scan type is a vessel (and not a planet, debris or an unknown object)
	static bool IsVessel( ScanType scanType )
	{
		return ( scanType != ScanType.Planet ) && ( scanType != ScanType.Debris ) && ( scanType != ScanType.Unknown );
	}

	// the display label
	public override string GetLabel()
	{
		return "Sensors";
	}

	// show
	public override void Show()
	{
		// call base show
		base.Show();

		// make a copy of the background material so the file doesn't get updated
		if ( m_backgroundMaterial == null )
		{
			m_backgroundMaterial = new Material( m_backgroundImage.material );
			m_backgroundImage.material = m_backgroundMaterial;
		}

		// make a copy of the mask material so the file doesn't get updated
		if ( m_maskMaterial == null )
		{
			m_maskMaterial = new Material( m_maskImage.material );
			m_maskImage.material = m_maskMaterial;
		}

		// clip everything in the window to the panel (once)
		if ( ( m_windowClip == null ) && ( m_panelImage != null ) )
		{
			var window = m_panelImage.transform.parent.gameObject;

			if ( !window.TryGetComponent( out m_windowClip ) )
			{
				m_windowClip = window.AddComponent<RectMask2D>();
			}

			// the panel is inset in the window by its offsets (left, bottom, right, top), and its magenta starts inside the sprite's transparent edge
			var panelRect = m_panelImage.rectTransform;
			var margin = GetPanelMargin();

			m_windowClip.padding = new Vector4( panelRect.offsetMin.x + margin, panelRect.offsetMin.y + margin, -panelRect.offsetMax.x + margin, -panelRect.offsetMax.y + margin );
		}

		// make the black inside of an empty window (once)
		if ( ( m_emptyImage == null ) && ( m_panelImage != null ) )
		{
			// a copy of the panel, drawn right above it (and under the pictures)
			var emptyObject = Instantiate( m_panelImage.gameObject, m_panelImage.transform.parent );

			emptyObject.name = "Empty";
			emptyObject.transform.SetSiblingIndex( m_panelImage.transform.GetSiblingIndex() + 1 );

			m_emptyImage = emptyObject.GetComponent<Image>();
			m_emptyImage.color = Color.black;
			m_emptyImage.raycastTarget = false;

			// smaller than the panel by the width of the border on every side
			var panelRect = m_panelImage.rectTransform;
			var emptyRect = m_emptyImage.rectTransform;
			var border = new Vector2( m_emptyBorderWidth, m_emptyBorderWidth );

			emptyRect.offsetMin = panelRect.offsetMin + border;
			emptyRect.offsetMax = panelRect.offsetMax - border;
		}

		// hide the top and bottom text
		m_massText.gameObject.SetActive( false );
		m_bioMinText.gameObject.SetActive( false );

		// hide the background and mask
		m_backgroundImage.gameObject.SetActive( false );
		m_maskImage.gameObject.SetActive( false );

		// the window is magenta until something is scanned
		if ( m_emptyImage != null )
		{
			m_emptyImage.gameObject.SetActive( false );
		}

		// show the instructions text
		m_instructionsText.gameObject.SetActive( true );
	}

	// hide
	public override void Hide()
	{
		// call base hide
		base.Hide();

		// no more sensor data available for analysis
		m_hasSensorData = false;
	}

	// returns true if we have both a background and a mask texture for this scan type
	bool HasTextures( int scanTypeIndex )
	{
		if ( ( scanTypeIndex < 0 ) || ( scanTypeIndex >= m_maskTextures.Length ) || ( scanTypeIndex >= m_backgroundTextures.Length ) )
		{
			return false;
		}

		return ( m_maskTextures[ scanTypeIndex ] != null ) && ( m_backgroundTextures[ scanTypeIndex ] != null );
	}

	// returns the texture at this index of the list, or null if the list has none there
	static Texture GetTexture( Texture[] textureList, int index )
	{
		if ( ( textureList == null ) || ( index < 0 ) || ( index >= textureList.Length ) )
		{
			return null;
		}

		return textureList[ index ];
	}

	// how far the magenta of the panel starts inside the panel's rectangle, in canvas units
	// (a sliced sprite's edge is drawn at the canvas's reference pixels per unit over the sprite's pixels per unit)
	float GetPanelMargin()
	{
		if ( ( m_panelImage == null ) || ( m_panelImage.sprite == null ) || ( m_panelImage.canvas == null ) || ( m_panelImage.sprite.pixelsPerUnit <= 0.0f ) || ( m_panelImage.pixelsPerUnitMultiplier <= 0.0f ) )
		{
			return 0.0f;
		}

		return m_panelSpriteMargin * m_panelImage.canvas.referencePixelsPerUnit / ( m_panelImage.sprite.pixelsPerUnit * m_panelImage.pixelsPerUnitMultiplier );
	}

	// the scale of a planet's picture that makes its disc as large in the window as the original draws it
	// (measured in the original on nine planets on 2026-10-10: below 10^22 tons the disc is 2 + 4 x the leading digit of the mass pixels across,
	// from 2x10^20 to 6x10^21 tons, and from 10^22 tons up it is 62 pixels across, up to 5x10^23 tons; masses under 10^20 tons were not seen)
	public float GetPlanetPictureScale( int massPowerBase, int mass )
	{
		// the leading digit and the power of ten of the mass, as the readout shows them
		var massDigit = Mathf.Max( 1, mass );
		var massPower = massPowerBase;

		for ( var i = 0; ( massDigit >= 10 ) && ( i < 10 ); i++ )
		{
			massDigit /= 10;
			massPower++;
		}

		// how many of the original's pixels the disc is across
		var discPixels = ( massPower >= c_fullPlanetDiscMassPower ) ? c_fullPlanetDiscPixels : Mathf.Min( c_fullPlanetDiscPixels, c_planetDiscBasePixels + c_planetDiscPixelsPerDigit * massDigit );

		// the magenta inside the panel is the original's window, and the planet's disc is most of the picture's height at scale 1
		var windowHeight = ( m_panelImage != null ) ? m_panelImage.rectTransform.rect.height - 2.0f * GetPanelMargin() : 0.0f;
		var pictureDiscHeight = m_maskImage.rectTransform.rect.height * c_planetMaskDiscFraction;

		if ( ( windowHeight <= 0.0f ) || ( pictureDiscHeight <= 0.0f ) )
		{
			return 1.0f;
		}

		return discPixels / c_originalWindowHeight * windowHeight / pictureDiscHeight;
	}

	// call this to start the scanning cinematics (vessel id is the vessel that left the debris, for a debris scan)
	public void StartScanning( ScanType scanType, int massPowerBase, int mass, int bioDensity, int mineralDensity, int vesselId = -1 )
	{
		// the picture for this scan
		Texture backgroundTexture = null;
		Texture maskTexture = null;

		if ( scanType == ScanType.Debris )
		{
			// debris has the picture of what is left of the vessel that was destroyed (a vessel with no such picture leaves the window empty)
			backgroundTexture = GetTexture( m_debrisBackgroundTextures, vesselId );
			maskTexture = GetTexture( m_debrisMaskTextures, vesselId );
		}
		else
		{
			// if we don't have a mask or background for this scan type then show the unknown ones instead
			// (only the picture changes - the scan type stays what it is, so the readout and the analysis are for the real object)
			int textureIndex = (int) scanType;

			if ( !HasTextures( textureIndex ) )
			{
				textureIndex = (int) ScanType.Unknown;
			}

			if ( HasTextures( textureIndex ) )
			{
				backgroundTexture = m_backgroundTextures[ textureIndex ];
				maskTexture = m_maskTextures[ textureIndex ];
			}
		}

		// remember the scan type, mass, bio density, and mineral density
		m_scanType = scanType;
		m_massPowerBase = massPowerBase;
		m_mass = mass;
		m_bioDensity = bioDensity;
		m_mineralDensity = mineralDensity;

		// reset the cinematics timer
		m_timer = 0.0f;

		// start the cinematics
		m_isDoingCinematics = true;
		m_soundStopped = false;

		// is there a picture for this scan? (the unknown slot is empty, so an object we have no picture for leaves the window empty, as the original does for an unidentified object)
		var hasPicture = ( backgroundTexture != null ) && ( maskTexture != null );

		// set the background and mask textures of the picture
		if ( hasPicture )
		{
			m_backgroundMaterial.SetTexture( "_MainTex", backgroundTexture );

			m_maskMaterial.SetTexture( "_MaskTex", maskTexture );
		}

		// reset background and mask image scale
		m_maskImage.transform.localScale = m_backgroundImage.transform.localScale = Vector3.one;

		// if we are scanning a planet scale the picture so that its disc is as large in the window as the original draws it
		if ( m_scanType == ScanType.Planet )
		{
			m_backgroundImage.transform.localScale = m_maskImage.transform.localScale = Vector3.one * GetPlanetPictureScale( massPowerBase, mass );
		}
		else if ( m_scanType == ScanType.Debris )
		{
			// scale debris to fill the display area properly
			m_backgroundImage.transform.localScale = m_maskImage.transform.localScale = new Vector3( 1.0f, 1.0f, 1.0f );
		}

		// play the scanning sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Scanning );

		// show the top and bottom text (an object with no picture shows no readout, as the original shows an unidentified object)
		m_massText.gameObject.SetActive( hasPicture );
		m_bioMinText.gameObject.SetActive( hasPicture );

		// show the background and mask (only if we have a picture - otherwise the last scan's picture would still be there)
		m_backgroundImage.gameObject.SetActive( hasPicture );
		m_maskImage.gameObject.SetActive( hasPicture );

		// with no picture the window is black inside its magenta border
		if ( m_emptyImage != null )
		{
			m_emptyImage.gameObject.SetActive( !hasPicture );
		}

		// hide the instructions text
		m_instructionsText.gameObject.SetActive( false );
	}
}
