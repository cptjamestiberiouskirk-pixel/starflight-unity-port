
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

	// from 10^21 to 10^22 tons the original draws a planet's disc this many pixels across, plus this many for each unit of the leading digit of its mass
	const int c_planetDiscDigitMassPower = 21;
	const float c_planetDiscBasePixels = 2.0f;
	const float c_planetDiscPixelsPerDigit = 4.0f;

	// and never smaller than this many pixels across, which is also the size of every planet under 10^21 tons
	const float c_smallestPlanetDiscPixels = 10.0f;

	// from 10^22 tons up the original draws the disc this many pixels across
	const int c_fullPlanetDiscMassPower = 22;
	const float c_fullPlanetDiscPixels = 62.0f;

	// the original's window is 57 pixels high, and on a 4:3 screen its pixels are 1.2 times as high as they are wide
	const float c_originalWindowHeight = 57.0f * 1.2f;

	// the disc of the planet mask is 501 of its 512 rows high
	const float c_planetMaskDiscFraction = 501.0f / 512.0f;

	// how fast the planet in the window turns (turns per second: once in two minutes, as the planet in the orbit view turns at 3 degrees a second)
	const float c_planetTurnsPerSecond = 1.0f / 120.0f;

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

	// counts the sensor readings taken (each one can be analyzed once, see AnalysisButton)
	public int m_readingCount;

	// the vessel whose wreck the sensors read (-1 for any other reading)
	public int m_debrisVesselId = -1;

	// what are we currently scanning
	public ScanType m_scanType;

	// the background material
	Material m_backgroundMaterial;

	// the mask material
	Material m_maskMaterial;

	// the material that draws a planet's own surface in the disc of the planet mask (the Sensor Planet shader)
	public Material m_planetMaterial;

	// our copy of it (made the first time the display is shown, so the file doesn't get updated)
	Material m_planetSurfaceMaterial;

	// the material the picture is drawn with for this scan (the mask material, or the planet surface material for a planet that has its maps)
	Material m_pictureMaterial;

	// the albedo map of the planet in the picture (the planet's generator destroys it when the maps are no longer needed, and the picture goes back to the noise then)
	Texture m_planetAlbedoTexture;

	// how far the planet in the picture has turned (in turns)
	float m_planetSpin;

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
		// the planet in the picture keeps turning once the scan is over
		UpdatePlanetSurface();

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
		m_pictureMaterial.SetTextureOffset( "_MainTex", new Vector2( Random.Range( 0.0f, 1.0f ), Random.Range( 0.0f, 1.0f ) ) );

		// the noise fades into the planet's surface as the scan goes on (slowly at first, so the scan starts as the noise)
		if ( m_pictureMaterial == m_planetSurfaceMaterial )
		{
			m_planetSurfaceMaterial.SetFloat( "_Reveal", alpha * alpha );
		}

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
					// a wreck shows what it is made of: the debris of its vessel (the original's vessel records)
					SpaceflightController.m_instance.m_messages.Clear();

					string text = "Object Constituents:";

					if ( ( m_debrisVesselId >= 0 ) && ( m_debrisVesselId < gameData.m_vesselList.Length ) )
					{
						var wreck = gameData.m_vesselList[ m_debrisVesselId ];

						if ( wreck.m_enduriumVolume > 0 )
						{
							text += "\n<color=white>" + gameData.m_elementList[ gameData.m_misc.m_enduriumElementId ].m_name + "</color>";
						}

						foreach ( var debris in new[] { new[] { wreck.m_elementIdA, wreck.m_elementVolumeA }, new[] { wreck.m_elementIdB, wreck.m_elementVolumeB }, new[] { wreck.m_elementIdC, wreck.m_elementVolumeC } } )
						{
							if ( ( debris[ 1 ] > 0 ) && ( debris[ 0 ] >= 0 ) && ( debris[ 0 ] < gameData.m_elementList.Length ) )
							{
								text += "\n<color=white>" + gameData.m_elementList[ debris[ 0 ] ].m_name + "</color>";
							}
						}
					}

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

		// make a copy of the planet surface material so the file doesn't get updated
		if ( ( m_planetSurfaceMaterial == null ) && ( m_planetMaterial != null ) )
		{
			m_planetSurfaceMaterial = new Material( m_planetMaterial );
		}

		// until something is scanned the picture is drawn with the mask material
		if ( m_pictureMaterial == null )
		{
			m_pictureMaterial = m_maskMaterial;
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
	// (measured in the original on fifteen planets on 2026-10-10 and 2026-10-11: under 10^21 tons the disc is 10 pixels across (6x10^19, 2x10^20, 6x10^20),
	// from 10^21 to 10^22 tons it is 2 + 4 x the leading digit of the mass pixels across but at least 10 (1x10^21 10 pixels, 3, 4, 5, 6, 7 and 9x10^21
	// 14 to 38), and from 10^22 tons up it is 62 pixels across, up to 5x10^23 tons; 2 and 8x10^21 were not seen)
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
		var discPixels = c_smallestPlanetDiscPixels;

		if ( massPower >= c_fullPlanetDiscMassPower )
		{
			discPixels = c_fullPlanetDiscPixels;
		}
		else if ( massPower >= c_planetDiscDigitMassPower )
		{
			discPixels = Mathf.Clamp( c_planetDiscBasePixels + c_planetDiscPixelsPerDigit * massDigit, c_smallestPlanetDiscPixels, c_fullPlanetDiscPixels );
		}

		// the magenta inside the panel is the original's window, and the planet's disc is most of the picture's height at scale 1
		var windowHeight = ( m_panelImage != null ) ? m_panelImage.rectTransform.rect.height - 2.0f * GetPanelMargin() : 0.0f;
		var pictureDiscHeight = m_maskImage.rectTransform.rect.height * c_planetMaskDiscFraction;

		if ( ( windowHeight <= 0.0f ) || ( pictureDiscHeight <= 0.0f ) )
		{
			return 1.0f;
		}

		return discPixels / c_originalWindowHeight * windowHeight / pictureDiscHeight;
	}

	// puts the surface of the planet the ship is at into the picture, if that planet has its maps (otherwise the picture stays the noise)
	void ShowPlanetSurface()
	{
		if ( ( m_planetSurfaceMaterial == null ) || ( SpaceflightController.m_instance == null ) || ( SpaceflightController.m_instance.m_starSystem == null ) )
		{
			return;
		}

		// the planet the ship is at
		var planetId = DataController.m_instance.m_playerData.m_general.m_currentPlanetId;
		var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( planetId );

		// a planet whose maps are still being made, or could not be made, has no surface to show (the grey ball of a planet that could not be mapped is not its surface)
		if ( ( planetController == null ) || !planetController.HasMaps() )
		{
			return;
		}

		var planetGenerator = planetController.GetPlanetGenerator();

		if ( ( planetGenerator.m_albedoTexture == null ) || ( planetGenerator.m_waterMaskTexture == null ) || ( planetGenerator.m_normalTexture == null ) )
		{
			return;
		}

		// the planet's maps
		m_planetSurfaceMaterial.SetTexture( "_PlanetTex", planetGenerator.m_albedoTexture );
		m_planetSurfaceMaterial.SetTexture( "_WaterTex", planetGenerator.m_waterMaskTexture );
		m_planetSurfaceMaterial.SetTexture( "_NormalTex", planetGenerator.m_normalTexture );

		// the rim of its atmosphere (a gas giant has none)
		var planet = planetController.m_planet;

		m_planetSurfaceMaterial.SetColor( "_AtmosphereColor", planet.HasAtmosphere() ? planet.GetAtmosphereColor() : Color.black );

		// the scan starts as the noise
		m_planetSurfaceMaterial.SetFloat( "_Reveal", 0.0f );
		m_planetSurfaceMaterial.SetFloat( "_Spin", m_planetSpin );

		m_planetAlbedoTexture = planetGenerator.m_albedoTexture;
		m_pictureMaterial = m_planetSurfaceMaterial;
	}

	// turns the planet in the picture, and goes back to the noise if its maps have been destroyed (the ship has left the star system)
	void UpdatePlanetSurface()
	{
		if ( ( m_pictureMaterial == null ) || ( m_pictureMaterial != m_planetSurfaceMaterial ) )
		{
			return;
		}

		// a destroyed texture compares equal to null
		if ( m_planetAlbedoTexture == null )
		{
			m_pictureMaterial = m_maskMaterial;
			m_maskImage.material = m_maskMaterial;

			return;
		}

		// the planet stands still while the game is paused
		if ( ( SpaceflightController.m_instance != null ) && SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		m_planetSpin += Time.deltaTime * c_planetTurnsPerSecond;

		// wrap the spin around to avoid FP issues
		if ( m_planetSpin >= 1.0f )
		{
			m_planetSpin -= 1.0f;
		}

		m_planetSurfaceMaterial.SetFloat( "_Spin", m_planetSpin );
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

		// a new reading
		m_readingCount++;

		// the vessel of a wreck
		m_debrisVesselId = ( scanType == ScanType.Debris ) ? vesselId : -1;

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

		// a planet that has its maps shows its own surface in the disc, anything else the noise
		m_pictureMaterial = m_maskMaterial;
		m_planetAlbedoTexture = null;

		if ( hasPicture && ( scanType == ScanType.Planet ) )
		{
			ShowPlanetSurface();
		}

		m_maskImage.material = m_pictureMaterial;

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
