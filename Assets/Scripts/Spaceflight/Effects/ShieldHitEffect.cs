
using UnityEngine;

/// <summary>
/// Shield hit effect - the shield bubble around the ship lights up where the shot struck it, a ripple runs across it, and blue sparks fly off.
/// </summary>
public class ShieldHitEffect : MonoBehaviour
{
	// the flash and the sparks where the shot strikes the bubble
	ParticleSystem m_flashParticles;
	ParticleSystem m_sparkParticles;

	// where the shot strikes the bubble (the particles are under it)
	Transform m_impactPoint;

	// shield sphere mesh
	GameObject m_shieldSphere;
	Material m_shieldMaterial;

	// effect parameters
	float m_duration = 0.45f;
	float m_timer;
	bool m_isPlaying;

	// how big the bubble is across (the player ship is about 51 long), and how much it swells as the ripple runs
	const float c_bubbleSize = 70.0f;
	const float c_bubbleSwell = 0.04f;

	// how far above the plane the ships fly in the shot strikes the bubble (as a share of the way to the top)
	const float c_impactLift = 0.7f;

	// the shield bubble shader's properties
	static readonly int m_impactDirectionId = Shader.PropertyToID( "_ImpactDirection" );
	static readonly int m_progressId = Shader.PropertyToID( "_Progress" );

	// unity awake
	void Awake()
	{
		EffectMaterials.Initialize();

		CreateShieldSphere();
		CreateShieldParticles();
		gameObject.SetActive( false );
	}

	// create the shield sphere
	void CreateShieldSphere()
	{
		m_shieldSphere = GameObject.CreatePrimitive( PrimitiveType.Sphere );
		m_shieldSphere.name = "Shield Bubble";
		m_shieldSphere.transform.SetParent( transform );
		m_shieldSphere.transform.localPosition = Vector3.zero;
		m_shieldSphere.transform.localRotation = Quaternion.identity;
		m_shieldSphere.transform.localScale = Vector3.one * c_bubbleSize;

		// remove collider
		var collider = m_shieldSphere.GetComponent<Collider>();
		if ( collider != null )
		{
			Destroy( collider );
		}

		// the bubble shader is in a Resources folder, so it is in every build
		var shader = Resources.Load<Shader>( "Shaders/Shield Bubble" );

		if ( shader == null )
		{
			Debug.LogWarning( "ShieldHitEffect: the shield bubble shader was not found, the shield hit shows only its sparks" );

			m_shieldSphere.SetActive( false );
			return;
		}

		// one material for each effect, since each one is at its own point of its ripple
		m_shieldMaterial = new Material( shader );
		m_shieldMaterial.name = "Shield Bubble";
		m_shieldMaterial.renderQueue = 3000;

		var renderer = m_shieldSphere.GetComponent<Renderer>();
		renderer.sharedMaterial = m_shieldMaterial;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;

		m_shieldSphere.SetActive( false );
	}

	// create the flash and the sparks where the shot strikes
	void CreateShieldParticles()
	{
		var impactObject = new GameObject( "Impact" );
		impactObject.transform.SetParent( transform );
		m_impactPoint = impactObject.transform;

		// the flash
		m_flashParticles = EffectMaterials.CreateParticleSystem( m_impactPoint, "Flash", EffectMaterials.m_glowMaterial, 5, 1 );

		var flashMain = m_flashParticles.main;
		flashMain.startLifetime = 0.15f;
		flashMain.startSpeed = 0.0f;
		flashMain.startSize = 26.0f;

		var flashEmission = m_flashParticles.emission;
		flashEmission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var flashShape = m_flashParticles.shape;
		flashShape.enabled = false;

		var flashSize = m_flashParticles.sizeOverLifetime;
		flashSize.enabled = true;
		flashSize.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 0.5f, 1.0f, 1.0f ) );

		var flashColour = m_flashParticles.colorOverLifetime;
		flashColour.enabled = true;
		flashColour.color = EffectMaterials.MakeGradient( new Color( 0.9f, 0.97f, 1.0f ), new Color( 0.5f, 0.8f, 1.0f ), new Color( 0.2f, 0.45f, 1.0f ), 1.0f, 0.7f, 0.0f );

		// the sparks: blue streaks that fly off the bubble
		m_sparkParticles = EffectMaterials.CreateParticleSystem( m_impactPoint, "Sparks", EffectMaterials.m_glowMaterial, 4, 40 );

		var main = m_sparkParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.15f, 0.35f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 60.0f, 160.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 0.8f, 1.6f );

		var emission = m_sparkParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 24 ) } );

		// off the bubble and along it
		var shape = m_sparkParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Cone;
		shape.angle = 75.0f;
		shape.radius = 3.0f;

		var velocityOverLifetime = m_sparkParticles.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.2f ) );

		var colorOverLifetime = m_sparkParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( new Color( 0.9f, 0.97f, 1.0f ), new Color( 0.5f, 0.8f, 1.0f ), new Color( 0.2f, 0.4f, 1.0f ), 1.0f, 1.0f, 0.0f );

		var renderer = m_sparkParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Stretch;
		renderer.velocityScale = 0.06f;
		renderer.lengthScale = 1.5f;
	}

	// unity update
	void Update()
	{
		if ( m_isPlaying )
		{
			m_timer += Time.deltaTime;
			float t = Mathf.Clamp01( m_timer / m_duration );

			// the ripple runs across the bubble as it fades, and the bubble swells a little
			if ( m_shieldMaterial != null )
			{
				m_shieldMaterial.SetFloat( m_progressId, t );
				m_shieldSphere.transform.localScale = Vector3.one * ( c_bubbleSize * ( 1.0f + c_bubbleSwell * t ) );
			}

			if ( m_timer >= m_duration )
			{
				m_isPlaying = false;
				m_shieldSphere.SetActive( false );
				gameObject.SetActive( false );
			}
		}
	}

	/// <summary>
	/// Play the shield hit effect at the specified position (struck on the front of the bubble).
	/// </summary>
	public void Play( Vector3 position )
	{
		Play( position, Vector3.back );
	}

	/// <summary>
	/// Play the shield hit effect at the specified position, struck by a shot travelling in the given direction.
	/// </summary>
	public void Play( Vector3 position, Vector3 hitDirection )
	{
		// the side of the bubble that faces the shot, in the plane the ships fly in (a shot from straight above or below strikes the front)
		var flatDirection = new Vector3( hitDirection.x, 0.0f, hitDirection.z );
		var towardsTheShot = ( flatDirection.sqrMagnitude > 0.01f ) ? -flatDirection.normalized : Vector3.forward;

		// struck a little above the plane, on the half of the bubble the camera looks down on: the ripple's circle is then seen at a slant and reads as a ring (struck on the rim, it was seen edge on, as a straight band)
		var impactDirection = ( towardsTheShot + Vector3.up * c_impactLift ).normalized;

		transform.position = position;
		transform.rotation = Quaternion.identity;

		// the flash and the sparks are on the bubble, the sparks fly off it towards the shot
		m_impactPoint.localPosition = impactDirection * ( c_bubbleSize * 0.5f );
		m_impactPoint.localRotation = Quaternion.LookRotation( impactDirection, Vector3.up );

		m_timer = 0.0f;
		m_isPlaying = true;

		if ( m_shieldMaterial != null )
		{
			m_shieldSphere.SetActive( true );
			m_shieldSphere.transform.localScale = Vector3.one * c_bubbleSize;
			m_shieldMaterial.SetVector( m_impactDirectionId, impactDirection );
			m_shieldMaterial.SetFloat( m_progressId, 0.0f );
		}

		gameObject.SetActive( true );

		// start every part from the beginning
		m_flashParticles.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		m_flashParticles.Play();
		m_sparkParticles.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		m_sparkParticles.Play();
	}

	/// <summary>
	/// Check if the effect is playing.
	/// </summary>
	public bool IsPlaying()
	{
		return m_isPlaying;
	}
}
