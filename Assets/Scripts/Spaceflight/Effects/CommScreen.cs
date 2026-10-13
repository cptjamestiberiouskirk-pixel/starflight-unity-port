using UnityEngine;
using MorePPEffects;

// the comm link's picture comes in through a burst of interference that clears as the link settles, after which the alien's portrait is shown clean: the original
// shows the portrait clean and garbles only the words, by the communications officer's skill (Encounter.AddComm), so the interference is the port's, and how strong
// it is at the start is the only part the officer's skill decides
public class CommScreen : MonoBehaviour
{
	// how long the interference takes to clear
	public float m_duration = 5.0f;

	// how long to fade in
	public float m_fadeInDuration = 1.0f;

	// camera fx components we will be controlling
	ColoredRays m_coloredRays;
	Waves m_waves;
	Headache m_headache;
	LowResolution m_lowResolution;

	// our fade in timer
	float m_timer;

	// are we fading in?
	bool m_fadingIn;

	// how strong the coloured rays and how low the resolution are when the link comes up
	float m_startingRays;
	float m_startingResolution;

	// the full resolution the low resolution effect clears to
	const float c_fullResolution = 512.0f;

	// unity awake
	void Awake()
	{
		// create the components
		m_waves = gameObject.AddComponent<Waves>();
		m_headache = gameObject.AddComponent<Headache>();
		m_coloredRays = gameObject.AddComponent<ColoredRays>();
		m_lowResolution = gameObject.AddComponent<LowResolution>();
	}

	// unity update
	void Update()
	{
		// is the interference still clearing?
		if ( !m_fadingIn )
		{
			return;
		}

		// update the timer
		m_timer += Time.deltaTime;

		// calculate new strength of effects
		var strength = ( m_duration > 0.0f ) ? Mathf.Clamp( m_timer / m_duration, 0.0f, 1.0f ) : 1.0f;

		// update strength of effects
		m_waves.strengthX = Mathf.SmoothStep( 3.0f, 0.0f, strength );
		m_headache.strength = Mathf.SmoothStep( 10.0f, 0.0f, strength );
		m_coloredRays.strength = Mathf.SmoothStep( m_startingRays, 0.0f, strength );

		// the resolution comes up to the full one
		if ( m_lowResolution.enabled )
		{
			m_lowResolution.resolutionX = Mathf.FloorToInt( Mathf.SmoothStep( m_startingResolution, c_fullResolution, strength ) );
			m_lowResolution.resolutionY = m_lowResolution.resolutionX;
		}

		// are we done?
		if ( m_timer >= m_duration )
		{
			// yes - the picture is clean from now on
			m_fadingIn = false;

			m_waves.strengthX = 0.0f;
			m_headache.strength = 0.0f;
			m_coloredRays.strength = 0.0f;
			m_lowResolution.enabled = false;
		}
	}

	// unity onenable
	void OnEnable()
	{
		// reset our timer
		m_timer = 0;

		// we are fading in
		m_fadingIn = true;

		// do we have a spaceflight controller (we could be in the test level where there isn't one)
		if ( SpaceflightController.m_instance != null )
		{
			// yes - fade in the viewer
			SpaceflightController.m_instance.m_viewport.StartFade( 1.0f, m_fadeInDuration );
		}

		// play some sounds
		PlaySound( SoundController.Sound.WarbleLong );
		PlaySound( SoundController.Sound.StaticLong );

		// the training level for the test scene
		var trainingLevel = 0.5f;

		// do we have a player (are we not in test scene?)
		if ( DataController.m_instance != null )
		{
			// get to the player data
			var playerData = DataController.m_instance.m_playerData;

			// configure the video effects based on the training level of the comm officer
			var personnelFile = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer );

			// convert the communications training level to a 1 to 0 range
			if ( personnelFile != null )
			{
				trainingLevel = personnelFile.m_communications / 250.0f;
			}
		}

		// re-range tranining level
		trainingLevel = Mathf.Lerp( 0.1f, 1.0f, Mathf.Clamp01( trainingLevel ) );

		// colored rays
		m_startingRays = 0.5f + 4.5f * ( 1.0f - trainingLevel );
		m_coloredRays.strength = m_startingRays;

		// waves
		m_waves.strengthY = 0.0f;
		m_waves.frequencyX = 100.0f;

		// low resolution
		if ( trainingLevel < 0.75f )
		{
			m_lowResolution.enabled = true;

			m_startingResolution = Mathf.Floor( c_fullResolution * trainingLevel );

			m_lowResolution.resolutionX = Mathf.FloorToInt( m_startingResolution );
			m_lowResolution.resolutionY = m_lowResolution.resolutionX;
		}
		else
		{
			m_lowResolution.enabled = false;
		}
	}

	// play a sound if we have a sound controller
	void PlaySound( SoundController.Sound sound )
	{
		if ( SoundController.m_instance != null )
		{
			SoundController.m_instance.PlaySound( sound );
		}
	}
}
