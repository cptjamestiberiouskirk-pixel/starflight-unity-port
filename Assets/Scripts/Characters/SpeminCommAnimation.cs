using UnityEngine;

// the Spemin's comm portrait moves as the Genesis version's does: its eyes blink, its discs spin on their stalks, its body wobbles,
// and slime drips from the ceiling. It finds the parts of the Spemin model by their names (Eyeball, Disc, the body mesh Spemin and
// the floor Goo), makes the drips when it starts, and draws its random numbers from a generator of its own so that it leaves
// Random.state alone. The eyes keep glancing about and the stalks keep swaying with their RandomRotation components.
public class SpeminCommAnimation : MonoBehaviour
{
	// how often each eye blinks (seconds between blinks) and how long a blink takes
	public float m_minimumBlinkInterval = 1.2f;
	public float m_maximumBlinkInterval = 4.5f;
	public float m_blinkDuration = 0.2f;

	// how far an eye shrinks into its socket at the middle of a blink
	public float m_blinkDepth = 0.2f;

	// how fast the discs spin (degrees a second; each disc picks its speed and direction)
	public float m_minimumDiscSpeed = 40.0f;
	public float m_maximumDiscSpeed = 160.0f;

	// the body's wobble: how much its width and height change, and how fast
	public float m_wobbleAmount = 0.08f;
	public float m_wobbleSpeed = 1.6f;

	// the body's breathing in depth (what SpeminBreathe did): how much and how fast
	public float m_breatheAmount = 0.03f;
	public float m_breatheSpeed = 1.0f;

	// the drips of slime: how many, where they hang (in this model's frame, around the body), how long they swell before they fall
	public int m_dripCount = 10;
	public float m_dripSpread = 14.0f;
	public float m_dripHeight = 26.0f;
	public float m_minimumSwellTime = 1.0f;
	public float m_maximumSwellTime = 3.0f;
	public float m_dripGravity = 30.0f;

	// the size of a drip (in this model's frame), and how long it lies splashed on the floor before it starts again
	public float m_dripSize = 1.6f;
	public float m_splatTime = 0.25f;

	// the parts
	Transform[] m_eyes;
	Vector3[] m_eyeScales;
	float[] m_nextBlink;
	float[] m_blinkTimer;

	Transform[] m_discs;
	Quaternion[] m_discRotations;
	float[] m_discSpeeds;
	float[] m_discAngles;

	Transform m_body;
	Vector3 m_bodyScale;
	float m_wobbleTimer;
	float m_breatheTimer;

	Transform[] m_drips;
	Vector3[] m_dripHomes;
	float[] m_dripTimer;
	float[] m_dripSwellTime;
	float[] m_dripSpeed;
	float[] m_dripSplat;
	float m_floorHeight;

	// random numbers of its own
	System.Random m_random;

	// unity start
	void Start()
	{
		m_random = new System.Random();

		var eyes = new System.Collections.Generic.List<Transform>();
		var discs = new System.Collections.Generic.List<Transform>();
		Renderer goo = null;

		foreach ( var part in GetComponentsInChildren<Transform>( true ) )
		{
			if ( part.name.StartsWith( "Eyeball" ) )
			{
				eyes.Add( part );
			}
			else if ( part.name.StartsWith( "Disc" ) )
			{
				discs.Add( part );
			}
			else if ( ( part.name == "Spemin" ) && ( part.GetComponent<MeshRenderer>() != null ) )
			{
				m_body = part;
			}
			else if ( part.name == "Goo" )
			{
				goo = part.GetComponent<Renderer>();
			}
		}

		// the eyes: each blinks on a clock of its own
		m_eyes = eyes.ToArray();
		m_eyeScales = new Vector3[ m_eyes.Length ];
		m_nextBlink = new float[ m_eyes.Length ];
		m_blinkTimer = new float[ m_eyes.Length ];

		for ( var i = 0; i < m_eyes.Length; i++ )
		{
			m_eyeScales[ i ] = m_eyes[ i ].localScale;
			m_nextBlink[ i ] = Range( m_minimumBlinkInterval, m_maximumBlinkInterval );
			m_blinkTimer[ i ] = -1.0f;
		}

		// the discs: each spins about its own axis (their meshes are flat in their local XY plane), at a speed and in a direction of its own
		m_discs = discs.ToArray();
		m_discRotations = new Quaternion[ m_discs.Length ];
		m_discSpeeds = new float[ m_discs.Length ];
		m_discAngles = new float[ m_discs.Length ];

		for ( var i = 0; i < m_discs.Length; i++ )
		{
			m_discRotations[ i ] = m_discs[ i ].localRotation;
			m_discSpeeds[ i ] = Range( m_minimumDiscSpeed, m_maximumDiscSpeed ) * ( ( m_random.Next( 2 ) == 0 ) ? -1.0f : 1.0f );
			m_discAngles[ i ] = Range( 0.0f, 360.0f );
		}

		// the body: this takes over SpeminBreathe's breathing, since both would set its scale
		if ( m_body != null )
		{
			m_bodyScale = m_body.localScale;

			var breathe = m_body.GetComponent<SpeminBreathe>();

			if ( breathe != null )
			{
				breathe.enabled = false;
			}
		}

		m_wobbleTimer = Range( 0.0f, 10.0f );
		m_breatheTimer = 0.0f;

		// the drips hang from above the body and fall to the goo on the floor
		m_floorHeight = ( goo != null ) ? transform.InverseTransformPoint( goo.bounds.center ).y : 0.0f;

		MakeDrips( goo );
	}

	// unity update
	void Update()
	{
		var deltaTime = Time.deltaTime;

		UpdateEyes( deltaTime );
		UpdateDiscs( deltaTime );
		UpdateBody( deltaTime );
		UpdateDrips( deltaTime );
	}

	// a random number between minimum and maximum
	float Range( float minimum, float maximum )
	{
		return minimum + (float) m_random.NextDouble() * ( maximum - minimum );
	}

	// make the drips: small balls of the floor's slime, hidden until they swell
	void MakeDrips( Renderer goo )
	{
		m_drips = new Transform[ Mathf.Max( 0, m_dripCount ) ];
		m_dripHomes = new Vector3[ m_drips.Length ];
		m_dripTimer = new float[ m_drips.Length ];
		m_dripSwellTime = new float[ m_drips.Length ];
		m_dripSpeed = new float[ m_drips.Length ];
		m_dripSplat = new float[ m_drips.Length ];

		var centre = ( m_body != null ) ? transform.InverseTransformPoint( m_body.position ) : Vector3.zero;

		for ( var i = 0; i < m_drips.Length; i++ )
		{
			var drip = GameObject.CreatePrimitive( PrimitiveType.Sphere );

			// the drips are drawn only: no collider
			var dripCollider = drip.GetComponent<Collider>();

			if ( dripCollider != null )
			{
				Destroy( dripCollider );
			}

			drip.name = "Drip " + ( i + 1 );
			drip.layer = gameObject.layer;
			drip.transform.SetParent( transform, false );

			if ( goo != null )
			{
				drip.GetComponent<Renderer>().sharedMaterial = goo.sharedMaterial;
			}

			m_drips[ i ] = drip.transform;
			m_dripHomes[ i ] = new Vector3( centre.x + Range( -m_dripSpread, m_dripSpread ), m_floorHeight + m_dripHeight, centre.z + Range( -m_dripSpread * 0.5f, m_dripSpread * 0.8f ) );

			// they start at different points of their cycle
			ResetDrip( i );
			m_dripTimer[ i ] = Range( 0.0f, m_dripSwellTime[ i ] );
		}
	}

	// a drip goes back to the ceiling and starts to swell again
	void ResetDrip( int i )
	{
		m_dripTimer[ i ] = 0.0f;
		m_dripSwellTime[ i ] = Range( m_minimumSwellTime, m_maximumSwellTime );
		m_dripSpeed[ i ] = 0.0f;
		m_dripSplat[ i ] = -1.0f;
		m_drips[ i ].localPosition = m_dripHomes[ i ];
		m_drips[ i ].localScale = Vector3.zero;
	}

	// each eye blinks now and then: it shrinks into its socket and comes back out
	void UpdateEyes( float deltaTime )
	{
		for ( var i = 0; i < m_eyes.Length; i++ )
		{
			if ( m_eyes[ i ] == null )
			{
				continue;
			}

			if ( m_blinkTimer[ i ] < 0.0f )
			{
				// waiting for the next blink
				m_nextBlink[ i ] -= deltaTime;

				if ( m_nextBlink[ i ] <= 0.0f )
				{
					m_blinkTimer[ i ] = 0.0f;
				}

				continue;
			}

			// in a blink: down and back up again
			m_blinkTimer[ i ] += deltaTime;

			var phase = Mathf.Clamp01( m_blinkTimer[ i ] / Mathf.Max( 0.01f, m_blinkDuration ) );
			var closed = Mathf.Sin( phase * Mathf.PI );

			m_eyes[ i ].localScale = m_eyeScales[ i ] * Mathf.Lerp( 1.0f, m_blinkDepth, closed );

			if ( phase >= 1.0f )
			{
				m_eyes[ i ].localScale = m_eyeScales[ i ];
				m_blinkTimer[ i ] = -1.0f;
				m_nextBlink[ i ] = Range( m_minimumBlinkInterval, m_maximumBlinkInterval );
			}
		}
	}

	// each disc spins about its own axis
	void UpdateDiscs( float deltaTime )
	{
		for ( var i = 0; i < m_discs.Length; i++ )
		{
			if ( m_discs[ i ] == null )
			{
				continue;
			}

			m_discAngles[ i ] = Mathf.Repeat( m_discAngles[ i ] + m_discSpeeds[ i ] * deltaTime, 360.0f );
			m_discs[ i ].localRotation = m_discRotations[ i ] * Quaternion.Euler( 0.0f, 0.0f, m_discAngles[ i ] );
		}
	}

	// the body wobbles: its width and height swing against each other (so it keeps about the same bulk) and it breathes in depth
	void UpdateBody( float deltaTime )
	{
		if ( m_body == null )
		{
			return;
		}

		m_wobbleTimer += deltaTime * m_wobbleSpeed;
		m_breatheTimer = Mathf.Repeat( m_breatheTimer + deltaTime * m_breatheSpeed, Mathf.PI * 2.0f );

		var wobble = Mathf.Sin( m_wobbleTimer * 2.0f ) * 0.7f + Mathf.Sin( m_wobbleTimer * 3.3f + 1.7f ) * 0.3f;
		var breathe = Mathf.Sin( m_breatheTimer );

		m_body.localScale = new Vector3( m_bodyScale.x * ( 1.0f + m_wobbleAmount * wobble ), m_bodyScale.y * ( 1.0f - m_wobbleAmount * wobble ), m_bodyScale.z * ( 1.0f + m_breatheAmount * breathe ) );
	}

	// the drips swell on the ceiling, stretch, fall to the floor and start again
	void UpdateDrips( float deltaTime )
	{
		for ( var i = 0; i < m_drips.Length; i++ )
		{
			if ( m_drips[ i ] == null )
			{
				continue;
			}

			m_dripTimer[ i ] += deltaTime;

			// splashed on the floor: it spreads flat for a moment, then starts again
			if ( m_dripSplat[ i ] >= 0.0f )
			{
				m_dripSplat[ i ] += deltaTime;

				var spread = Mathf.Clamp01( m_dripSplat[ i ] / Mathf.Max( 0.01f, m_splatTime ) );

				m_drips[ i ].localScale = new Vector3( m_dripSize * ( 1.0f + spread ), m_dripSize * 0.2f * ( 1.0f - spread ), m_dripSize * ( 1.0f + spread ) );

				if ( spread >= 1.0f )
				{
					ResetDrip( i );
				}

				continue;
			}

			if ( m_dripTimer[ i ] < m_dripSwellTime[ i ] )
			{
				// swelling: it grows and hangs a little lower as it does
				var swell = m_dripTimer[ i ] / m_dripSwellTime[ i ];

				m_drips[ i ].localScale = new Vector3( m_dripSize * swell, m_dripSize * swell * ( 1.0f + swell ), m_dripSize * swell );
				m_drips[ i ].localPosition = m_dripHomes[ i ] - new Vector3( 0.0f, m_dripSize * swell, 0.0f );
				continue;
			}

			// falling: it speeds up and stretches with its speed
			m_dripSpeed[ i ] += m_dripGravity * deltaTime;

			var position = m_drips[ i ].localPosition - new Vector3( 0.0f, m_dripSpeed[ i ] * deltaTime, 0.0f );
			var stretch = 1.0f + Mathf.Min( 2.0f, m_dripSpeed[ i ] * 0.08f );

			m_drips[ i ].localScale = new Vector3( m_dripSize / Mathf.Sqrt( stretch ), m_dripSize * stretch, m_dripSize / Mathf.Sqrt( stretch ) );
			m_drips[ i ].localPosition = position;

			// it reaches the goo on the floor and splashes
			if ( position.y <= m_floorHeight )
			{
				m_drips[ i ].localPosition = new Vector3( position.x, m_floorHeight, position.z );
				m_dripSplat[ i ] = 0.0f;
			}
		}
	}
}
