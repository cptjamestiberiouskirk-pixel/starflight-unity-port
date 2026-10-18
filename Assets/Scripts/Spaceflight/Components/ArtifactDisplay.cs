
using UnityEngine;

// an artifact shown beside the ruin that holds it: it hovers over the ground, rising and sinking a little, and turns slowly, so that it catches the eye
// (it stands still while the game is paused)
public class ArtifactDisplay : MonoBehaviour
{
	// the artifact (an id into the game data's artifact list)
	public int m_artifactId = -1;

	// how fast it turns (degrees a second), and how far and how slowly it rises and sinks (units, seconds for a rise and a fall)
	const float c_turnSpeed = 30.0f;
	const float c_bobHeight = 0.6f;
	const float c_bobSeconds = 3.0f;

	// where it hovers when it is neither up nor down, and how long it has been shown
	Vector3 m_restPosition;
	float m_time;

	// its hum (SoundController.Sound.ArtifactHum, on an audio source of its own): at full loudness within c_humNear of the terrain vehicle, fading out by
	// c_humFar (it follows the vehicle, not the camera, which hangs high above the ground), and paused while the game is
	public const float c_humVolume = 0.5f;
	public const float c_humNear = 10.0f;
	public const float c_humFar = 40.0f;
	AudioSource m_hum;
	bool m_humPaused;

	// how loud the hum is at this distance from the terrain vehicle (along the ground)
	public static float HumVolumeAt( float distance )
	{
		return c_humVolume * Mathf.Clamp01( ( c_humFar - distance ) / ( c_humFar - c_humNear ) );
	}

	// unity start
	void Start()
	{
		m_restPosition = transform.localPosition;

		var clip = ( SoundController.m_instance != null ) ? SoundController.m_instance.GetClip( SoundController.Sound.ArtifactHum ) : null;

		if ( clip != null )
		{
			m_hum = gameObject.AddComponent<AudioSource>();
			m_hum.clip = clip;
			m_hum.loop = true;
			m_hum.playOnAwake = false;
			m_hum.spatialBlend = 0.0f;
			m_hum.volume = HumVolumeAt( DistanceToTerrainVehicle() );
			m_hum.Play();
		}
	}

	// how far the terrain vehicle is, along the ground (as far as the hum reaches when there is none)
	float DistanceToTerrainVehicle()
	{
		var controller = SpaceflightController.m_instance;

		if ( ( controller == null ) || ( controller.m_terrainVehicle == null ) )
		{
			return c_humFar;
		}

		var delta = controller.m_terrainVehicle.transform.position - transform.position;

		delta.y = 0.0f;

		return delta.magnitude;
	}

	// unity update
	void Update()
	{
		var paused = ( SpaceflightController.m_instance != null ) && SpaceflightController.m_instance.m_gameIsPaused;

		if ( ( m_hum != null ) && ( paused != m_humPaused ) )
		{
			if ( paused )
			{
				m_hum.Pause();
			}
			else
			{
				m_hum.UnPause();
			}

			m_humPaused = paused;
		}

		if ( paused )
		{
			return;
		}

		if ( m_hum != null )
		{
			m_hum.volume = HumVolumeAt( DistanceToTerrainVehicle() );
		}

		m_time += Time.deltaTime;

		transform.localRotation = Quaternion.Euler( 0.0f, m_time * c_turnSpeed, 0.0f );
		transform.localPosition = m_restPosition + Vector3.up * ( Mathf.Sin( m_time * 2.0f * Mathf.PI / c_bobSeconds ) * c_bobHeight );
	}
}
