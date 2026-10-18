
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

	// unity start
	void Start()
	{
		m_restPosition = transform.localPosition;
	}

	// unity update
	void Update()
	{
		if ( ( SpaceflightController.m_instance != null ) && SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		m_time += Time.deltaTime;

		transform.localRotation = Quaternion.Euler( 0.0f, m_time * c_turnSpeed, 0.0f );
		transform.localPosition = m_restPosition + Vector3.up * ( Mathf.Sin( m_time * 2.0f * Mathf.PI / c_bobSeconds ) * c_bobHeight );
	}
}
