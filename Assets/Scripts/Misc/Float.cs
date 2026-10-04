
using UnityEngine;

public class Float : MonoBehaviour
{
	public float m_speed = 1.0f;
	public float m_range = 45.0f;

	Vector3 m_originalPosition;
	float m_timer;

	void Start()
	{
		m_originalPosition = transform.localPosition;
	}

	void Update()
	{
		m_timer += Time.deltaTime * m_speed;

		// the timer goes into Mathf.Sin, so it is an angle in radians and a full turn is two pi (it started again at 360,
		// which is not a whole number of turns, so the object jumped every time the timer got there)
		if ( m_timer > Mathf.PI * 2.0f )
		{
			m_timer -= Mathf.PI * 2.0f;
		}

		var offset = Mathf.Sin( m_timer ) * m_range;

		transform.localPosition = m_originalPosition + new Vector3( 0.0f, offset, 0.0f );
	}
}
