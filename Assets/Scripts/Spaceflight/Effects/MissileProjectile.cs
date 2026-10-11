
using UnityEngine;

/// <summary>
/// Missile projectile that travels toward a target: a glowing warhead with a hot exhaust streak and a smoke trail behind it.
/// </summary>
public class MissileProjectile : MonoBehaviour
{
	// missile speed
	public float m_speed = 500.0f;

	// missile turn rate
	public float m_turnRate = 180.0f;

	// explosion radius
	public float m_explosionRadius = 50.0f;

	// the target position
	Vector3 m_targetPosition;

	// the target transform (for homing)
	Transform m_targetTransform;

	// callback when missile hits
	System.Action<Vector3, bool> m_onHitCallback;

	// missile state
	bool m_isActive;
	float m_lifetime;
	float m_maxLifetime = 5.0f;

	// the glowing warhead, the hot exhaust streak behind it, and the smoke trail it leaves
	ParticleSystem m_headParticles;
	TrailRenderer m_exhaustTrail;
	ParticleSystem m_smokeParticles;

	// true while the particles are paused with the game
	bool m_particlesArePaused;

	// how far behind the middle of the missile its exhaust comes out, how long its streak lasts and how wide it starts
	const float c_exhaustOffset = 5.0f;
	const float c_exhaustTime = 0.12f;
	const float c_exhaustWidth = 5.0f;

	// how many smoke puffs the missile leaves for each unit it flies (so they overlap at any speed)
	const float c_smokePerUnit = 0.25f;

	// unity awake
	void Awake()
	{
		EffectMaterials.Initialize();

		CreateMissileVisual();
		CreateTrailParticles();
		gameObject.SetActive( false );
	}

	// create the missile: a glowing warhead that travels with it (one particle that lives as long as any flight)
	void CreateMissileVisual()
	{
		m_headParticles = EffectMaterials.CreateParticleSystem( transform, "Warhead", EffectMaterials.m_glowMaterial, 3, 1 );

		var main = m_headParticles.main;
		main.startLifetime = m_maxLifetime + 1.0f;
		main.startSpeed = 0.0f;
		main.startSize = 9.0f;
		main.startColor = new Color( 1.0f, 0.85f, 0.6f, 1.0f );

		// it moves with the missile
		main.simulationSpace = ParticleSystemSimulationSpace.Local;

		var emission = m_headParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var shape = m_headParticles.shape;
		shape.enabled = false;
	}

	// create the exhaust and the smoke trail
	void CreateTrailParticles()
	{
		var exhaustObject = new GameObject( "Exhaust" );
		exhaustObject.transform.SetParent( transform );
		exhaustObject.transform.localPosition = new Vector3( 0.0f, 0.0f, -c_exhaustOffset );

		// the exhaust and the smoke come out backwards
		exhaustObject.transform.localRotation = Quaternion.Euler( 0.0f, 180.0f, 0.0f );

		// the hot exhaust: a streak of flame behind the missile that tapers and cools from white to red (a trail, so it is unbroken at any speed)
		m_exhaustTrail = exhaustObject.AddComponent<TrailRenderer>();
		m_exhaustTrail.sharedMaterial = EffectMaterials.m_beamMaterial;
		m_exhaustTrail.time = c_exhaustTime;
		m_exhaustTrail.minVertexDistance = 3.0f;
		m_exhaustTrail.widthCurve = AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.0f );
		m_exhaustTrail.widthMultiplier = c_exhaustWidth;
		m_exhaustTrail.colorGradient = EffectMaterials.MakeGradient( new Color( 1.0f, 0.95f, 0.75f ), new Color( 1.0f, 0.6f, 0.2f ), new Color( 0.8f, 0.2f, 0.05f ), 1.0f, 0.7f, 0.0f );
		m_exhaustTrail.textureMode = LineTextureMode.Stretch;
		m_exhaustTrail.numCapVertices = 2;
		m_exhaustTrail.sortingOrder = 2;
		m_exhaustTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		m_exhaustTrail.receiveShadows = false;
		m_exhaustTrail.emitting = false;

		// the smoke: puffs that spread and thin out behind the missile
		m_smokeParticles = EffectMaterials.CreateParticleSystem( exhaustObject.transform, "Smoke", EffectMaterials.m_smokeMaterial, 1, 150 );

		var smokeMain = m_smokeParticles.main;
		smokeMain.loop = true;
		smokeMain.startLifetime = new ParticleSystem.MinMaxCurve( 0.6f, 0.9f );
		smokeMain.startSpeed = new ParticleSystem.MinMaxCurve( 2.0f, 8.0f );
		smokeMain.startSize = new ParticleSystem.MinMaxCurve( 3.0f, 5.0f );
		smokeMain.startRotation = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );

		var smokeEmission = m_smokeParticles.emission;
		smokeEmission.rateOverTime = 0;
		smokeEmission.rateOverDistance = c_smokePerUnit;

		var smokeShape = m_smokeParticles.shape;
		smokeShape.shapeType = ParticleSystemShapeType.Sphere;
		smokeShape.radius = 1.0f;

		var smokeSize = m_smokeParticles.sizeOverLifetime;
		smokeSize.enabled = true;
		smokeSize.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 3.0f ) );

		var smokeColour = m_smokeParticles.colorOverLifetime;
		smokeColour.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 0.45f, 0.35f, 0.28f ), 0.0f ), new GradientColorKey( new Color( 0.3f, 0.3f, 0.3f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 0.0f, 0.0f ), new GradientAlphaKey( 0.4f, 0.15f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		smokeColour.color = gradient;
	}

	// start the warhead, the exhaust and the smoke from the beginning
	void StartParticles()
	{
		m_particlesArePaused = false;

		m_headParticles.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		m_headParticles.Play();
		m_exhaustTrail.Clear();
		m_exhaustTrail.emitting = true;
		m_smokeParticles.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		m_smokeParticles.Play();
	}

	// unity update
	void Update()
	{
		if ( !m_isActive )
		{
			return;
		}

		// a missile does not fly on while the game is paused (it used to arrive and do its damage behind the save panel)
		if ( ( SpaceflightController.m_instance != null ) && SpaceflightController.m_instance.m_gameIsPaused )
		{
			// nor does its smoke (it would pile up where the missile stands; the exhaust streak dies away behind it)
			if ( !m_particlesArePaused )
			{
				m_headParticles.Pause();
				m_smokeParticles.Pause();
				m_particlesArePaused = true;
			}

			return;
		}

		// and they go on where they were when the game goes on
		if ( m_particlesArePaused )
		{
			m_headParticles.Play();
			m_smokeParticles.Play();
			m_particlesArePaused = false;
		}

		m_lifetime += Time.deltaTime;

		// check for timeout
		if ( m_lifetime >= m_maxLifetime )
		{
			// a missile that runs out of time has missed - tell whoever launched it (they show the explosion where the missile was and do no damage)
			m_onHitCallback?.Invoke( transform.position, false );
			Deactivate();
			return;
		}

		// update target position if we have a transform
		if ( m_targetTransform != null )
		{
			m_targetPosition = m_targetTransform.position;
		}

		// calculate direction to target
		var direction = ( m_targetPosition - transform.position ).normalized;

		// smoothly rotate toward target
		var targetRotation = Quaternion.LookRotation( direction, Vector3.up );
		transform.rotation = Quaternion.RotateTowards( transform.rotation, targetRotation, m_turnRate * Time.deltaTime );

		// move forward
		transform.position += transform.forward * m_speed * Time.deltaTime;

		// check for hit
		var distanceToTarget = Vector3.Distance( transform.position, m_targetPosition );
		if ( distanceToTarget <= m_explosionRadius )
		{
			// hit!
			m_onHitCallback?.Invoke( transform.position, true );
			Deactivate();
		}
	}

	/// <summary>
	/// Fire the missile at a target position.
	/// </summary>
	public void Fire( Vector3 startPosition, Vector3 targetPosition, System.Action<Vector3, bool> onHitCallback )
	{
		transform.position = startPosition;
		m_targetPosition = targetPosition;
		m_targetTransform = null;
		m_onHitCallback = onHitCallback;
		m_lifetime = 0.0f;
		m_isActive = true;

		// face the target
		var direction = ( targetPosition - startPosition ).normalized;
		transform.rotation = Quaternion.LookRotation( direction, Vector3.up );

		gameObject.SetActive( true );
		StartParticles();
	}

	/// <summary>
	/// Fire the missile at a target transform (homing).
	/// </summary>
	public void Fire( Vector3 startPosition, Transform targetTransform, System.Action<Vector3, bool> onHitCallback )
	{
		transform.position = startPosition;
		m_targetTransform = targetTransform;
		m_targetPosition = targetTransform.position;
		m_onHitCallback = onHitCallback;
		m_lifetime = 0.0f;
		m_isActive = true;

		// face the target
		var direction = ( m_targetPosition - startPosition ).normalized;
		transform.rotation = Quaternion.LookRotation( direction, Vector3.up );

		gameObject.SetActive( true );
		StartParticles();
	}

	/// <summary>
	/// Deactivate the missile.
	/// </summary>
	void Deactivate()
	{
		m_isActive = false;
		m_headParticles.Stop();
		m_exhaustTrail.emitting = false;
		m_smokeParticles.Stop();
		gameObject.SetActive( false );
	}

	/// <summary>
	/// Take the missile out of the air without it hitting anything (the hit callback is not called).
	/// </summary>
	public void Cancel()
	{
		if ( m_isActive )
		{
			// forget who was waiting for this missile to arrive
			m_onHitCallback = null;

			Deactivate();
		}
	}

	/// <summary>
	/// Check if the missile is active.
	/// </summary>
	public bool IsActive()
	{
		return m_isActive;
	}
}
