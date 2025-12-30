
using UnityEngine;

/// <summary>
/// Hull hit effect - sparks and debris for armor damage.
/// </summary>
public class HullHitEffect : MonoBehaviour
{
	// particle systems
	ParticleSystem m_sparkParticles;
	ParticleSystem m_debrisParticles;

	// effect parameters
	float m_duration = 0.6f;
	float m_timer;
	bool m_isPlaying;

	// unity awake
	void Awake()
	{
		CreateSparkParticles();
		CreateDebrisParticles();
		gameObject.SetActive( false );
	}

	// create spark particles
	void CreateSparkParticles()
	{
		var sparkObject = new GameObject( "Sparks" );
		sparkObject.transform.SetParent( transform );
		sparkObject.transform.localPosition = Vector3.zero;

		m_sparkParticles = sparkObject.AddComponent<ParticleSystem>();

		var main = m_sparkParticles.main;
		main.startLifetime = 0.4f;
		main.startSpeed = 150.0f;
		main.startSize = 2.0f;
		main.startColor = new Color( 1.0f, 0.8f, 0.3f, 1.0f );
		main.maxParticles = 50;
		main.playOnAwake = false;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		var emission = m_sparkParticles.emission;
		emission.rateOverTime = 0;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 30 ) } );

		var shape = m_sparkParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Hemisphere;
		shape.radius = 5.0f;

		var colorOverLifetime = m_sparkParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 1.0f, 0.9f, 0.5f ), 0.0f ), new GradientColorKey( new Color( 1.0f, 0.3f, 0.1f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

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
		main.startLifetime = 1.0f;
		main.startSpeed = 80.0f;
		main.startSize = new ParticleSystem.MinMaxCurve( 1.0f, 4.0f );
		main.startColor = new Color( 0.5f, 0.5f, 0.5f, 1.0f );
		main.startRotation = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.maxParticles = 20;
		main.playOnAwake = false;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		var emission = m_debrisParticles.emission;
		emission.rateOverTime = 0;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 10 ) } );

		var shape = m_debrisParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Hemisphere;
		shape.radius = 5.0f;

		var rotationOverLifetime = m_debrisParticles.rotationOverLifetime;
		rotationOverLifetime.enabled = true;
		rotationOverLifetime.z = new ParticleSystem.MinMaxCurve( -3.0f, 3.0f );

		var colorOverLifetime = m_debrisParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 0.6f, 0.6f, 0.6f ), 0.0f ), new GradientColorKey( new Color( 0.3f, 0.3f, 0.3f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		// create material
		var renderer = m_debrisParticles.GetComponent<ParticleSystemRenderer>();
		var material = new Material( Shader.Find( "Standard" ) );
		material.SetColor( "_Color", new Color( 0.4f, 0.4f, 0.4f ) );
		renderer.material = material;
	}

	// unity update
	void Update()
	{
		if ( m_isPlaying )
		{
			m_timer += Time.deltaTime;
			if ( m_timer >= m_duration )
			{
				m_isPlaying = false;
				gameObject.SetActive( false );
			}
		}
	}

	/// <summary>
	/// Play the hull hit effect at the specified position.
	/// </summary>
	public void Play( Vector3 position )
	{
		transform.position = position;
		m_timer = 0.0f;
		m_isPlaying = true;

		gameObject.SetActive( true );
		m_sparkParticles.Play();
		m_debrisParticles.Play();
	}

	/// <summary>
	/// Play with direction for directional debris.
	/// </summary>
	public void Play( Vector3 position, Vector3 hitDirection )
	{
		transform.position = position;
		transform.rotation = Quaternion.LookRotation( hitDirection, Vector3.up );
		m_timer = 0.0f;
		m_isPlaying = true;

		gameObject.SetActive( true );
		m_sparkParticles.Play();
		m_debrisParticles.Play();
	}

	/// <summary>
	/// Check if the effect is playing.
	/// </summary>
	public bool IsPlaying()
	{
		return m_isPlaying;
	}
}
