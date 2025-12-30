
using UnityEngine;

/// <summary>
/// Missile projectile that travels toward a target with a particle trail.
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

	// trail particle system
	ParticleSystem m_trailParticles;

	// missile visual
	GameObject m_missileVisual;

	// unity awake
	void Awake()
	{
		CreateMissileVisual();
		CreateTrailParticles();
		gameObject.SetActive( false );
	}

	// create simple missile visual
	void CreateMissileVisual()
	{
		// create a simple elongated capsule for the missile body
		m_missileVisual = GameObject.CreatePrimitive( PrimitiveType.Capsule );
		m_missileVisual.transform.SetParent( transform );
		m_missileVisual.transform.localPosition = Vector3.zero;
		m_missileVisual.transform.localRotation = Quaternion.Euler( 90.0f, 0.0f, 0.0f );
		m_missileVisual.transform.localScale = new Vector3( 3.0f, 8.0f, 3.0f );

		// remove collider
		var collider = m_missileVisual.GetComponent<Collider>();
		if ( collider != null )
		{
			Destroy( collider );
		}

		// set emissive material
		var renderer = m_missileVisual.GetComponent<Renderer>();
		if ( renderer != null )
		{
			var material = new Material( Shader.Find( "Standard" ) );
			material.SetColor( "_Color", new Color( 0.8f, 0.8f, 0.8f ) );
			material.SetColor( "_EmissionColor", new Color( 1.0f, 0.5f, 0.2f ) * 2.0f );
			material.EnableKeyword( "_EMISSION" );
			renderer.material = material;
		}
	}

	// create trail particle system
	void CreateTrailParticles()
	{
		var trailObject = new GameObject( "Trail" );
		trailObject.transform.SetParent( transform );
		trailObject.transform.localPosition = new Vector3( 0.0f, 0.0f, -8.0f );

		m_trailParticles = trailObject.AddComponent<ParticleSystem>();

		var main = m_trailParticles.main;
		main.startLifetime = 0.5f;
		main.startSpeed = 0.0f;
		main.startSize = 8.0f;
		main.startColor = new Color( 1.0f, 0.6f, 0.2f, 0.8f );
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.maxParticles = 100;

		var emission = m_trailParticles.emission;
		emission.rateOverTime = 50;

		var shape = m_trailParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 1.0f;

		var colorOverLifetime = m_trailParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 1.0f, 0.6f, 0.2f ), 0.0f ), new GradientColorKey( new Color( 0.5f, 0.2f, 0.1f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 0.8f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		var sizeOverLifetime = m_trailParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.Linear( 0.0f, 1.0f, 1.0f, 0.0f ) );

		// create material
		var renderer = m_trailParticles.GetComponent<ParticleSystemRenderer>();
		var material = new Material( Shader.Find( "Particles/Standard Unlit" ) );
		material.SetColor( "_Color", Color.white );
		renderer.material = material;
	}

	// unity update
	void Update()
	{
		if ( !m_isActive )
		{
			return;
		}

		m_lifetime += Time.deltaTime;

		// check for timeout
		if ( m_lifetime >= m_maxLifetime )
		{
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
		m_trailParticles.Play();
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
		m_trailParticles.Play();
	}

	/// <summary>
	/// Deactivate the missile.
	/// </summary>
	void Deactivate()
	{
		m_isActive = false;
		m_trailParticles.Stop();
		gameObject.SetActive( false );
	}

	/// <summary>
	/// Check if the missile is active.
	/// </summary>
	public bool IsActive()
	{
		return m_isActive;
	}
}
