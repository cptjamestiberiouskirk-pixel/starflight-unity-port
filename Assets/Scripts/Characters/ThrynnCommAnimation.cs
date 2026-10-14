using UnityEngine;

// the Thrynn's comm portrait moves as the Genesis version's does: its eyes glance about together and blink, and it breathes, its arms
// shifting a little as it does. It finds the parts of the rigged Thrynn model by their names, moves each from the pose
// it had when the portrait started, and draws its random numbers from a generator of its own so that it leaves Random.state alone. It
// takes over the left eye's RandomRotation (the right eye had none, so the eyes looked different ways); the neck keeps its
// RandomRotation and the mouth its SinusoidalRotation.
public class ThrynnCommAnimation : MonoBehaviour
{
	// the eyes glance together: how far (degrees), how often (seconds between glances) and how quickly an eye moves to its new direction
	public float m_glanceRange = 14.0f;
	public float m_minimumGlanceInterval = 1.2f;
	public float m_maximumGlanceInterval = 3.5f;
	public float m_glanceTime = 0.3f;

	// the eyes blink: how often (seconds between blinks), how long a blink takes, and how far an eye closes
	public float m_minimumBlinkInterval = 2.5f;
	public float m_maximumBlinkInterval = 6.0f;
	public float m_blinkDuration = 0.28f;
	public float m_blinkDepth = 0.1f;

	// the breathing: the bone that rises and falls, how far (degrees) and how fast (breaths a second)
	public string m_breathingBone = "Back Bone";
	public float m_breathingAmount = 1.8f;
	public float m_breathingSpeed = 0.28f;

	// the arms shift a little as it breathes: how far (degrees)
	public float m_armDrift = 2.5f;

	// the parts and their poses when the portrait started
	Transform[] m_eyes;
	Quaternion[] m_eyeRotations;
	Vector3[] m_eyeScales;
	int[] m_eyeAxes;

	Transform m_breathing;
	Quaternion m_breathingRotation;

	Transform[] m_arms;
	Quaternion[] m_armRotations;
	float[] m_armPhases;

	// the glance: where the eyes turned from, where they turn to, and how far through the turn they are
	Vector2 m_glanceFrom;
	Vector2 m_glanceTo;
	float m_glanceTimer;
	float m_nextGlance;

	// the blink timer (below 0: waiting for the next blink)
	float m_blinkTimer;
	float m_nextBlink;

	// the clock the waves run on
	float m_timer;

	// random numbers of its own
	System.Random m_random;

	// unity start
	void Start()
	{
		m_random = new System.Random();
		m_timer = Range( 0.0f, 10.0f );

		var eyes = new System.Collections.Generic.List<Transform>();
		var arms = new System.Collections.Generic.List<Transform>();

		foreach ( var part in GetComponentsInChildren<Transform>( true ) )
		{
			if ( part.name.EndsWith( "Eyeball" ) )
			{
				eyes.Add( part );
			}
			else if ( part.name.EndsWith( "Arm Bone" ) )
			{
				arms.Add( part );
			}
			else if ( part.name == m_breathingBone )
			{
				m_breathing = part;
			}
		}

		// the eyes: they take over from RandomRotation, which would set the same rotations; each closes along its axis that points most nearly up
		m_eyes = eyes.ToArray();
		m_eyeRotations = new Quaternion[ m_eyes.Length ];
		m_eyeScales = new Vector3[ m_eyes.Length ];
		m_eyeAxes = new int[ m_eyes.Length ];

		for ( var i = 0; i < m_eyes.Length; i++ )
		{
			m_eyeRotations[ i ] = m_eyes[ i ].localRotation;
			m_eyeScales[ i ] = m_eyes[ i ].localScale;
			m_eyeAxes[ i ] = UpAxis( m_eyes[ i ] );

			var randomRotation = m_eyes[ i ].GetComponent<RandomRotation>();

			if ( randomRotation != null )
			{
				randomRotation.enabled = false;
			}
		}

		if ( m_breathing != null )
		{
			m_breathingRotation = m_breathing.localRotation;
		}

		// the arms shift out of step with each other
		m_arms = arms.ToArray();
		m_armRotations = new Quaternion[ m_arms.Length ];
		m_armPhases = new float[ m_arms.Length ];

		for ( var i = 0; i < m_arms.Length; i++ )
		{
			m_armRotations[ i ] = m_arms[ i ].localRotation;
			m_armPhases[ i ] = Range( 0.0f, Mathf.PI * 2.0f );
		}

		m_glanceFrom = Vector2.zero;
		m_glanceTo = Vector2.zero;
		m_glanceTimer = -1.0f;
		m_nextGlance = Range( m_minimumGlanceInterval, m_maximumGlanceInterval );

		m_blinkTimer = -1.0f;
		m_nextBlink = Range( m_minimumBlinkInterval, m_maximumBlinkInterval );

	}

	// unity update
	void Update()
	{
		var deltaTime = Time.deltaTime;

		m_timer += deltaTime;

		UpdateEyes( deltaTime );
		UpdateBreathing();
		UpdateArms();
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

	// the eyes glance together, easing into each new direction, and blink together now and then
	void UpdateEyes( float deltaTime )
	{
		// the glance: wait, then turn to a new direction, starting and stopping gently
		if ( m_glanceTimer < 0.0f )
		{
			m_nextGlance -= deltaTime;

			if ( m_nextGlance <= 0.0f )
			{
				m_glanceFrom = m_glanceTo;
				m_glanceTo = new Vector2( Range( -m_glanceRange, m_glanceRange ), Range( -m_glanceRange, m_glanceRange ) * 0.6f );
				m_glanceTimer = 0.0f;
			}
		}
		else
		{
			m_glanceTimer += deltaTime;

			if ( m_glanceTimer >= m_glanceTime )
			{
				m_glanceTimer = -1.0f;
				m_nextGlance = Range( m_minimumGlanceInterval, m_maximumGlanceInterval );
			}
		}

		var turn = ( m_glanceTimer < 0.0f ) ? 1.0f : Mathf.SmoothStep( 0.0f, 1.0f, m_glanceTimer / Mathf.Max( 0.01f, m_glanceTime ) );
		var look = Vector2.Lerp( m_glanceFrom, m_glanceTo, turn );
		var glance = Quaternion.Euler( look.y, look.x, 0.0f );

		// the blink: wait, then close and open again, easing in and out so that no frame jumps
		var squash = 1.0f;

		if ( m_blinkTimer < 0.0f )
		{
			m_nextBlink -= deltaTime;

			if ( m_nextBlink <= 0.0f )
			{
				m_blinkTimer = 0.0f;
			}
		}
		else
		{
			m_blinkTimer += deltaTime;

			var phase = Mathf.Clamp01( m_blinkTimer / Mathf.Max( 0.01f, m_blinkDuration ) );

			squash = Mathf.Lerp( 1.0f, m_blinkDepth, ( 1.0f - Mathf.Cos( phase * Mathf.PI * 2.0f ) ) * 0.5f );

			if ( phase >= 1.0f )
			{
				squash = 1.0f;
				m_blinkTimer = -1.0f;
				m_nextBlink = Range( m_minimumBlinkInterval, m_maximumBlinkInterval );
			}
		}

		for ( var i = 0; i < m_eyes.Length; i++ )
		{
			if ( m_eyes[ i ] == null )
			{
				continue;
			}

			var scale = m_eyeScales[ i ];

			scale[ m_eyeAxes[ i ] ] *= squash;

			m_eyes[ i ].localRotation = m_eyeRotations[ i ] * glance;
			m_eyes[ i ].localScale = scale;
		}
	}

	// it breathes: the back rises and falls slowly
	void UpdateBreathing()
	{
		if ( m_breathing == null )
		{
			return;
		}

		var breath = Mathf.Sin( m_timer * m_breathingSpeed * Mathf.PI * 2.0f ) * m_breathingAmount;

		m_breathing.localRotation = m_breathingRotation * Quaternion.Euler( 0.0f, 0.0f, breath );
	}

	// the arms shift a little, slowly and out of step with each other (more would stretch the skin of the shoulders)
	void UpdateArms()
	{
		for ( var i = 0; i < m_arms.Length; i++ )
		{
			if ( m_arms[ i ] == null )
			{
				continue;
			}

			var drift = Mathf.Sin( m_timer * 0.5f + m_armPhases[ i ] ) * m_armDrift;

			m_arms[ i ].localRotation = m_armRotations[ i ] * Quaternion.Euler( 0.0f, 0.0f, drift );
		}
	}
}
