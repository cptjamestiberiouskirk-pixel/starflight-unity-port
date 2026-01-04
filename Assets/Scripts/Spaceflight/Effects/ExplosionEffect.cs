
using UnityEngine;

/// <summary>
/// Explosion visual effect using particle systems.
/// </summary>
public class ExplosionEffect : MonoBehaviour
{
	// particle systems
	ParticleSystem m_coreExplosion;
	ParticleSystem m_sparkParticles;
	ParticleSystem m_debrisParticles;
	ParticleSystem m_smokeParticles;

	// explosion parameters
	float m_duration = 1.5f;
	float m_timer;
	bool m_isPlaying;

	// callback when explosion finishes
	System.Action m_onComplete;

	// unity awake
	void Awake()
	{
		CreateCoreExplosion();
		CreateSparkParticles();
		CreateDebrisParticles();
		CreateSmokeParticles();
		gameObject.SetActive( false );
	}

	// create the core explosion flash
	void CreateCoreExplosion()
	{
		var coreObject = new GameObject( "Core" );
		coreObject.transform.SetParent( transform );
		coreObject.transform.localPosition = Vector3.zero;

		m_coreExplosion = coreObject.AddComponent<ParticleSystem>();

		var main = m_coreExplosion.main;
		main.startLifetime = 0.3f;
		main.startSpeed = 0.0f;
		main.startSize = 100.0f;
		main.startColor = new Color( 1.0f, 0.8f, 0.3f, 1.0f );
		main.maxParticles = 1;
		main.playOnAwake = false;

		var emission = m_coreExplosion.emission;
		emission.rateOverTime = 0;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var shape = m_coreExplosion.shape;
		shape.enabled = false;

		var sizeOverLifetime = m_coreExplosion.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 0.5f, 1.0f, 2.0f ) );

		var colorOverLifetime = m_coreExplosion.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 1.0f, 1.0f, 0.8f ), 0.0f ), new GradientColorKey( new Color( 1.0f, 0.5f, 0.1f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		// create material
		var renderer = m_coreExplosion.GetComponent<ParticleSystemRenderer>();
		var material = new Material( Shader.Find( "Particles/Standard Unlit" ) );
		material.SetColor( "_Color", Color.white );
		renderer.material = material;
	}

	// create spark particles
	void CreateSparkParticles()
	{
		var sparkObject = new GameObject( "Sparks" );
		sparkObject.transform.SetParent( transform );
		sparkObject.transform.localPosition = Vector3.zero;

		m_sparkParticles = sparkObject.AddComponent<ParticleSystem>();

		var main = m_sparkParticles.main;
		main.startLifetime = 0.8f;
		main.startSpeed = 200.0f;
		main.startSize = 3.0f;
		main.startColor = new Color( 1.0f, 0.8f, 0.3f, 1.0f );
		main.maxParticles = 100;
		main.playOnAwake = false;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		var emission = m_sparkParticles.emission;
		emission.rateOverTime = 0;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 50 ) } );

		var shape = m_sparkParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 10.0f;

		var colorOverLifetime = m_sparkParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 1.0f, 0.9f, 0.5f ), 0.0f ), new GradientColorKey( new Color( 1.0f, 0.3f, 0.1f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		var velocityOverLifetime = m_sparkParticles.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.2f ) );

		// create material
		var renderer = m_sparkParticles.GetComponent<ParticleSystemRenderer>();
		var material = new Material( Shader.Find( "Particles/Standard Unlit" ) );
		material.SetColor( "_Color", Color.white );
		renderer.material = material;
	}

	// create debris particles
	void CreateDebrisParticles()
	{
		var debrisObject = new GameObject( "Debris" );
		debrisObject.transform.SetParent( transform );
		debrisObject.transform.localPosition = Vector3.zero;

		m_debrisParticles = debrisObject.AddComponent<ParticleSystem>();

		var main = m_debrisParticles.main;
		main.startLifetime = 2.0f;
		main.startSpeed = 100.0f;
		main.startSize = new ParticleSystem.MinMaxCurve( 2.0f, 8.0f );
		main.startColor = new Color( 0.4f, 0.4f, 0.4f, 1.0f );
		main.startRotation = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.maxParticles = 30;
		main.playOnAwake = false;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		var emission = m_debrisParticles.emission;
		emission.rateOverTime = 0;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 20 ) } );

		var shape = m_debrisParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 15.0f;

		var rotationOverLifetime = m_debrisParticles.rotationOverLifetime;
		rotationOverLifetime.enabled = true;
		rotationOverLifetime.z = new ParticleSystem.MinMaxCurve( -2.0f, 2.0f );

		var colorOverLifetime = m_debrisParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 0.5f, 0.5f, 0.5f ), 0.0f ), new GradientColorKey( new Color( 0.2f, 0.2f, 0.2f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		// create material
		var renderer = m_debrisParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Mesh;
		renderer.mesh = Resources.GetBuiltinResource<Mesh>( "Cube.fbx" );
		var material = new Material( Shader.Find( "Standard" ) );
		material.SetColor( "_Color", new Color( 0.3f, 0.3f, 0.3f ) );
		renderer.material = material;
	}

	// create smoke particles
	void CreateSmokeParticles()
	{
		var smokeObject = new GameObject( "Smoke" );
		smokeObject.transform.SetParent( transform );
		smokeObject.transform.localPosition = Vector3.zero;

		m_smokeParticles = smokeObject.AddComponent<ParticleSystem>();

		var main = m_smokeParticles.main;
		main.startLifetime = 2.0f;
		main.startSpeed = 30.0f;
		main.startSize = 50.0f;
		main.startColor = new Color( 0.3f, 0.3f, 0.3f, 0.5f );
		main.maxParticles = 20;
		main.playOnAwake = false;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		var emission = m_smokeParticles.emission;
		emission.rateOverTime = 0;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.1f, 10 ) } );

		var shape = m_smokeParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 20.0f;

		var sizeOverLifetime = m_smokeParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.Linear( 0.0f, 1.0f, 1.0f, 3.0f ) );

		var colorOverLifetime = m_smokeParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 0.4f, 0.4f, 0.4f ), 0.0f ), new GradientColorKey( new Color( 0.2f, 0.2f, 0.2f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 0.6f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		// create material
		var renderer = m_smokeParticles.GetComponent<ParticleSystemRenderer>();
		var material = new Material( Shader.Find( "Particles/Standard Unlit" ) );
		material.SetColor( "_Color", Color.white );
		renderer.material = material;
	}

	// unity update
	void Update()
	{
		if ( m_isPlaying )
		{
			// use unscaled delta time so explosion finishes even when game is paused
			m_timer += Time.unscaledDeltaTime;
			if ( m_timer >= m_duration )
			{
				m_isPlaying = false;
				m_onComplete?.Invoke();
				gameObject.SetActive( false );
			}
		}
	}

	/// <summary>
	/// Play the explosion at the specified position.
	/// </summary>
	public void Play( Vector3 position, System.Action onComplete = null )
	{
		transform.position = position;
		m_timer = 0.0f;
		m_isPlaying = true;
		m_onComplete = onComplete;

		gameObject.SetActive( true );
		m_coreExplosion.Play();
		m_sparkParticles.Play();
		m_debrisParticles.Play();
		m_smokeParticles.Play();
	}

	/// <summary>
	/// Set the scale of the explosion.
	/// </summary>
	public void SetScale( float scale )
	{
		transform.localScale = Vector3.one * scale;
	}

	/// <summary>
	/// Check if the explosion is playing.
	/// </summary>
	public bool IsPlaying()
	{
		return m_isPlaying;
	}
}
