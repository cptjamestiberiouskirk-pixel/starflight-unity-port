using UnityEngine;

// the Elowan's comm portrait moves as the Genesis version's does: its vines wave and curl, its stalk sways, its leaves flutter and its
// eyes blink. It finds the parts of the rigged Elowan model by their names (the stalk's bones, every other bone a vine's, the Leaf
// meshes and the two eyes), moves each from the pose it had when the portrait started, and draws its random numbers from a generator of
// its own so that it leaves Random.state alone. It takes over the RandomRotation of the vines' bones, since both would set their
// rotations; the head's bones keep theirs, so the head still glances about.
public class ElowanCommAnimation : MonoBehaviour
{
	// the bones of the stalk from the root up to the head: every other bone belongs to a vine
	public string[] m_stalkBones = { "Bone001", "Bone002", "Bone003", "Bone004", "Bone005", "Bone006", "Bone007" };

	// the stalk's bones that sway (below the head, whose bones glance with their RandomRotation), how far (degrees) and how fast
	public string[] m_swayBones = { "Bone002", "Bone003" };
	public float m_swayAmount = 3.0f;
	public float m_swaySpeed = 0.7f;

	// the vines: how far each bone curls (degrees) and twists, how fast the wave runs, and how many bones make one wave
	public float m_vineCurl = 5.0f;
	public float m_vineTwist = 2.5f;
	public float m_vineSpeed = 1.2f;
	public float m_vineWaveLength = 5.0f;

	// the leaves: how far each turns about its midrib (degrees), and how fast (each picks its pace between the two)
	public float m_leafAmount = 9.0f;
	public float m_minimumLeafSpeed = 1.6f;
	public float m_maximumLeafSpeed = 3.2f;

	// the eyes: how often they blink (seconds between blinks), how long a blink takes, and how far an eye closes
	public float m_minimumBlinkInterval = 2.0f;
	public float m_maximumBlinkInterval = 6.0f;
	public float m_blinkDuration = 0.3f;
	public float m_blinkDepth = 0.12f;

	// the parts and their poses when the portrait started
	Transform[] m_sway;
	Quaternion[] m_swayRotations;
	float[] m_swayPhases;

	Transform[] m_vines;
	Quaternion[] m_vineRotations;
	float[] m_vinePhases;

	Transform[] m_leaves;
	Quaternion[] m_leafRotations;
	float[] m_leafSpeeds;
	float[] m_leafPhases;

	Transform[] m_eyes;
	Vector3[] m_eyeScales;
	int[] m_eyeAxes;
	float m_nextBlink;
	float m_blinkTimer;

	// the clock all the waves run on
	float m_timer;

	// random numbers of its own
	System.Random m_random;

	// unity start
	void Start()
	{
		m_random = new System.Random();
		m_timer = Range( 0.0f, 10.0f );

		var stalk = new System.Collections.Generic.HashSet<string>( m_stalkBones );
		var swayNames = new System.Collections.Generic.HashSet<string>( m_swayBones );
		var sway = new System.Collections.Generic.List<Transform>();
		var vines = new System.Collections.Generic.List<Transform>();
		var leaves = new System.Collections.Generic.List<Transform>();
		var eyes = new System.Collections.Generic.List<Transform>();

		foreach ( var part in GetComponentsInChildren<Transform>( true ) )
		{
			if ( part.name.StartsWith( "Bone" ) )
			{
				if ( swayNames.Contains( part.name ) )
				{
					sway.Add( part );
				}
				else if ( !stalk.Contains( part.name ) )
				{
					vines.Add( part );
				}
			}
			else if ( part.name.StartsWith( "Leaf" ) )
			{
				leaves.Add( part );
			}
			else if ( part.name.EndsWith( " Eye" ) )
			{
				eyes.Add( part );
			}
		}

		// the stalk sways a little, each of its bones a little out of step with the one below
		m_sway = sway.ToArray();
		m_swayRotations = new Quaternion[ m_sway.Length ];
		m_swayPhases = new float[ m_sway.Length ];

		for ( var i = 0; i < m_sway.Length; i++ )
		{
			m_swayRotations[ i ] = m_sway[ i ].localRotation;
			m_swayPhases[ i ] = i * 0.6f;
		}

		// the vines: each bone is a step further along its vine's wave than the bone it hangs from, and each vine has a wave of its own
		m_vines = vines.ToArray();
		m_vineRotations = new Quaternion[ m_vines.Length ];
		m_vinePhases = new float[ m_vines.Length ];

		var vinePhase = new System.Collections.Generic.Dictionary<Transform, float>();

		for ( var i = 0; i < m_vines.Length; i++ )
		{
			var bone = m_vines[ i ];

			m_vineRotations[ i ] = bone.localRotation;

			// they take over from RandomRotation, which would set the same rotations
			var randomRotation = bone.GetComponent<RandomRotation>();

			if ( randomRotation != null )
			{
				randomRotation.enabled = false;
			}

			// a bone that hangs from the stalk starts a vine at a point of the wave of its own; every other bone is a step on from its parent
			float parentPhase;

			if ( ( bone.parent != null ) && vinePhase.TryGetValue( bone.parent, out parentPhase ) )
			{
				m_vinePhases[ i ] = parentPhase + Mathf.PI * 2.0f / Mathf.Max( 1.0f, m_vineWaveLength );
			}
			else
			{
				m_vinePhases[ i ] = Range( 0.0f, Mathf.PI * 2.0f );
			}

			vinePhase[ bone ] = m_vinePhases[ i ];
		}

		// the leaves flutter, each at a pace and from a point of its own
		m_leaves = leaves.ToArray();
		m_leafRotations = new Quaternion[ m_leaves.Length ];
		m_leafSpeeds = new float[ m_leaves.Length ];
		m_leafPhases = new float[ m_leaves.Length ];

		for ( var i = 0; i < m_leaves.Length; i++ )
		{
			m_leafRotations[ i ] = m_leaves[ i ].localRotation;
			m_leafSpeeds[ i ] = Range( m_minimumLeafSpeed, m_maximumLeafSpeed );
			m_leafPhases[ i ] = Range( 0.0f, Mathf.PI * 2.0f );
		}

		// the eyes close along the axis of theirs that points most nearly up, as a lid would
		m_eyes = eyes.ToArray();
		m_eyeScales = new Vector3[ m_eyes.Length ];
		m_eyeAxes = new int[ m_eyes.Length ];

		for ( var i = 0; i < m_eyes.Length; i++ )
		{
			m_eyeScales[ i ] = m_eyes[ i ].localScale;
			m_eyeAxes[ i ] = UpAxis( m_eyes[ i ] );
		}

		m_nextBlink = Range( m_minimumBlinkInterval, m_maximumBlinkInterval );
		m_blinkTimer = -1.0f;
	}

	// unity update
	void Update()
	{
		var deltaTime = Time.deltaTime;

		m_timer += deltaTime;

		UpdateSway();
		UpdateVines();
		UpdateLeaves();
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

	// the stalk sways: two slow waves of different paces, so that it never quite repeats
	void UpdateSway()
	{
		for ( var i = 0; i < m_sway.Length; i++ )
		{
			if ( m_sway[ i ] == null )
			{
				continue;
			}

			var t = m_timer * m_swaySpeed + m_swayPhases[ i ];
			var roll = Mathf.Sin( t ) * m_swayAmount;
			var pitch = Mathf.Sin( t * 0.63f + 1.3f ) * m_swayAmount * 0.6f;

			m_sway[ i ].localRotation = m_swayRotations[ i ] * Quaternion.Euler( pitch, 0.0f, roll );
		}
	}

	// the vines wave: a curl that runs along each vine from the stalk out to its tip, with a gentle twist that runs at another pace
	void UpdateVines()
	{
		var t = m_timer * m_vineSpeed;

		for ( var i = 0; i < m_vines.Length; i++ )
		{
			if ( m_vines[ i ] == null )
			{
				continue;
			}

			var curl = Mathf.Sin( m_vinePhases[ i ] - t ) * m_vineCurl;
			var twist = Mathf.Sin( m_vinePhases[ i ] * 0.7f - t * 0.55f ) * m_vineTwist;

			m_vines[ i ].localRotation = m_vineRotations[ i ] * Quaternion.Euler( 0.0f, twist, curl );
		}
	}

	// each leaf turns a little about its midrib
	void UpdateLeaves()
	{
		for ( var i = 0; i < m_leaves.Length; i++ )
		{
			if ( m_leaves[ i ] == null )
			{
				continue;
			}

			var turn = Mathf.Sin( m_timer * m_leafSpeeds[ i ] + m_leafPhases[ i ] ) * m_leafAmount;

			m_leaves[ i ].localRotation = m_leafRotations[ i ] * Quaternion.Euler( 0.0f, turn, 0.0f );
		}
	}

	// the eyes blink together now and then: they close along their upward axis and open again, easing in and out so that no frame jumps
	void UpdateEyes( float deltaTime )
	{
		if ( m_blinkTimer < 0.0f )
		{
			// waiting for the next blink
			m_nextBlink -= deltaTime;

			if ( m_nextBlink <= 0.0f )
			{
				m_blinkTimer = 0.0f;
			}

			return;
		}

		m_blinkTimer += deltaTime;

		var phase = Mathf.Clamp01( m_blinkTimer / Mathf.Max( 0.01f, m_blinkDuration ) );
		var closed = ( 1.0f - Mathf.Cos( phase * Mathf.PI * 2.0f ) ) * 0.5f;
		var squash = Mathf.Lerp( 1.0f, m_blinkDepth, closed );

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
