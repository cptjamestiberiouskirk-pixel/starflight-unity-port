using UnityEngine;

// the Gazurtoid's comm portrait moves as the Genesis version's does: as well as its writhing tentacles (GazurtoidTentacle), its float
// (Float), its glancing eyes (RandomRotation) and the water's caustics and bubbles, which it keeps, its eyes blink and its body swells
// and settles as it breathes. The swell scales the whole model a little wider and a little shorter, so that the brows, ears and eye stalks,
// which hang from the body's transform and not from its bones, move with the skin under them. It finds the eyes by their names, works from
// the pose the model had when the portrait started, and draws its random numbers from a generator of its own so that it leaves
// Random.state alone.
public class GazurtoidCommAnimation : MonoBehaviour
{
	// the breathing: how much wider the body gets at the top of a breath (and half that much shorter), and how fast (breaths a second)
	public float m_breathingAmount = 0.035f;
	public float m_breathingSpeed = 0.3f;

	// the eyes blink: how often (seconds between blinks), how long a blink takes, and how far an eye closes
	public float m_minimumBlinkInterval = 2.5f;
	public float m_maximumBlinkInterval = 6.5f;
	public float m_blinkDuration = 0.32f;
	public float m_blinkDepth = 0.12f;

	// the model's scale and the eyes when the portrait started
	Vector3 m_scale;
	Transform[] m_eyes;
	Vector3[] m_eyeScales;
	int[] m_eyeAxes;

	// the blink timer (below 0: waiting for the next blink) and the breathing clock
	float m_blinkTimer;
	float m_nextBlink;
	float m_timer;

	// random numbers of its own
	System.Random m_random;

	// unity start
	void Start()
	{
		m_random = new System.Random();
		m_timer = Range( 0.0f, 10.0f );
		m_scale = transform.localScale;

		var eyes = new System.Collections.Generic.List<Transform>();

		foreach ( var part in GetComponentsInChildren<Transform>( true ) )
		{
			if ( part.name.EndsWith( "Eyeball" ) )
			{
				eyes.Add( part );
			}
		}

		// each eye closes along its axis that points most nearly up, as a lid would
		m_eyes = eyes.ToArray();
		m_eyeScales = new Vector3[ m_eyes.Length ];
		m_eyeAxes = new int[ m_eyes.Length ];

		for ( var i = 0; i < m_eyes.Length; i++ )
		{
			m_eyeScales[ i ] = m_eyes[ i ].localScale;
			m_eyeAxes[ i ] = UpAxis( m_eyes[ i ] );
		}

		m_blinkTimer = -1.0f;
		m_nextBlink = Range( m_minimumBlinkInterval, m_maximumBlinkInterval );
	}

	// unity update
	void Update()
	{
		var deltaTime = Time.deltaTime;

		m_timer += deltaTime;

		UpdateBreathing();
		UpdateEyes( deltaTime );
	}

	// a random number between minimum and maximum
	float Range( float minimum, float maximum )
	{
		return minimum + (float) m_random.NextDouble() * ( maximum - minimum );
	}

	// which of a transform's local axes (0 x, 1 y, 2 z) points most nearly up in the world
	static int UpAxis( Transform part )
	{
		var best = 0;
		var bestDot = -1.0f;

		for ( var axis = 0; axis < 3; axis++ )
		{
			var direction = ( axis == 0 ) ? part.right : ( ( axis == 1 ) ? part.up : part.forward );
			var dot = Mathf.Abs( Vector3.Dot( direction.normalized, Vector3.up ) );

			if ( dot > bestDot )
			{
				bestDot = dot;
				best = axis;
			}
		}

		return best;
	}

	// it breathes: the whole model swells a little wider and settles back, a little shorter as it swells, slowly
	void UpdateBreathing()
	{
		var breath = Mathf.Sin( m_timer * m_breathingSpeed * Mathf.PI * 2.0f ) * m_breathingAmount;

		transform.localScale = new Vector3( m_scale.x * ( 1.0f + breath ), m_scale.y * ( 1.0f - breath * 0.5f ), m_scale.z * ( 1.0f + breath ) );
	}

	// the eyes blink together now and then: they close along their upward axis and open again, easing in and out so that no frame jumps
	void UpdateEyes( float deltaTime )
	{
		if ( m_blinkTimer < 0.0f )
		{
			m_nextBlink -= deltaTime;

			if ( m_nextBlink <= 0.0f )
			{
				m_blinkTimer = 0.0f;
			}

			return;
		}

		m_blinkTimer += deltaTime;

		var phase = Mathf.Clamp01( m_blinkTimer / Mathf.Max( 0.01f, m_blinkDuration ) );
		var squash = Mathf.Lerp( 1.0f, m_blinkDepth, ( 1.0f - Mathf.Cos( phase * Mathf.PI * 2.0f ) ) * 0.5f );

		for ( var i = 0; i < m_eyes.Length; i++ )
		{
			if ( m_eyes[ i ] == null )
			{
				continue;
			}

			var scale = m_eyeScales[ i ];

			scale[ m_eyeAxes[ i ] ] *= squash;

			m_eyes[ i ].localScale = scale;
		}

		if ( phase >= 1.0f )
		{
			for ( var i = 0; i < m_eyes.Length; i++ )
			{
				if ( m_eyes[ i ] != null )
				{
					m_eyes[ i ].localScale = m_eyeScales[ i ];
				}
			}

			m_blinkTimer = -1.0f;
			m_nextBlink = Range( m_minimumBlinkInterval, m_maximumBlinkInterval );
		}
	}
}
