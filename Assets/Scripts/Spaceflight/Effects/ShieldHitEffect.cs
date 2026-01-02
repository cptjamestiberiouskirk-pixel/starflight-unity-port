
using UnityEngine;

/// <summary>
/// Shield hit effect - expanding energy sphere.
/// </summary>
public class ShieldHitEffect : MonoBehaviour
{
	// particle system for shield impact
	ParticleSystem m_shieldParticles;

	// shield sphere mesh
	GameObject m_shieldSphere;
	Material m_shieldMaterial;

	// effect parameters
	float m_duration = 0.4f;
	float m_timer;
	bool m_isPlaying;
	float m_maxScale = 80.0f;

	// unity awake
	void Awake()
	{
		CreateShieldSphere();
		CreateShieldParticles();
		gameObject.SetActive( false );
	}

	// create the shield sphere
	void CreateShieldSphere()
	{
		m_shieldSphere = GameObject.CreatePrimitive( PrimitiveType.Sphere );
		m_shieldSphere.transform.SetParent( transform );
		m_shieldSphere.transform.localPosition = Vector3.zero;
		m_shieldSphere.transform.localScale = Vector3.one * m_maxScale;

		// remove collider
		var collider = m_shieldSphere.GetComponent<Collider>();
		if ( collider != null )
		{
			Destroy( collider );
		}

		// create transparent additive material
		var renderer = m_shieldSphere.GetComponent<Renderer>();
		m_shieldMaterial = new Material( Shader.Find( "Standard" ) );
		m_shieldMaterial.SetFloat( "_Mode", 3 ); // transparent
		m_shieldMaterial.SetInt( "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha );
		m_shieldMaterial.SetInt( "_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha );
		m_shieldMaterial.SetInt( "_ZWrite", 0 );
		m_shieldMaterial.DisableKeyword( "_ALPHATEST_ON" );
		m_shieldMaterial.EnableKeyword( "_ALPHABLEND_ON" );
		m_shieldMaterial.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
		m_shieldMaterial.renderQueue = 3000;
		m_shieldMaterial.SetColor( "_Color", new Color( 0.3f, 0.6f, 1.0f, 0.5f ) );
		m_shieldMaterial.SetColor( "_EmissionColor", new Color( 0.3f, 0.6f, 1.0f ) * 2.0f );
		m_shieldMaterial.EnableKeyword( "_EMISSION" );
		renderer.material = m_shieldMaterial;

		m_shieldSphere.SetActive( false );
	}

	// create particle ripples
	void CreateShieldParticles()
	{
		var particleObject = new GameObject( "Particles" );
		particleObject.transform.SetParent( transform );
		particleObject.transform.localPosition = Vector3.zero;

		m_shieldParticles = particleObject.AddComponent<ParticleSystem>();

		var main = m_shieldParticles.main;
		main.startLifetime = 0.5f;
		main.startSpeed = 50.0f;
		main.startSize = 5.0f;
		main.startColor = new Color( 0.5f, 0.8f, 1.0f, 1.0f );
		main.maxParticles = 50;
		main.playOnAwake = false;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		var emission = m_shieldParticles.emission;
		emission.rateOverTime = 0;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 30 ) } );

		var shape = m_shieldParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 30.0f;

		var colorOverLifetime = m_shieldParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 0.5f, 0.8f, 1.0f ), 0.0f ), new GradientColorKey( new Color( 0.2f, 0.4f, 1.0f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		// create material
		var renderer = m_shieldParticles.GetComponent<ParticleSystemRenderer>();
		var material = new Material( Shader.Find( "Particles/Standard Unlit" ) );
		material.SetColor( "_Color", Color.white );
		renderer.material = material;
	}

	// unity update
	void Update()
	{
		if ( m_isPlaying )
		{
			m_timer += Time.deltaTime;
			float t = m_timer / m_duration;

			// animate shield sphere
			float scale = Mathf.Lerp( m_maxScale * 0.5f, m_maxScale, t );
			m_shieldSphere.transform.localScale = Vector3.one * scale;

			// fade out
			float alpha = Mathf.Lerp( 0.5f, 0.0f, t );
			m_shieldMaterial.SetColor( "_Color", new Color( 0.3f, 0.6f, 1.0f, alpha ) );

			if ( m_timer >= m_duration )
			{
				m_isPlaying = false;
				m_shieldSphere.SetActive( false );
				gameObject.SetActive( false );
			}
		}
	}

	/// <summary>
	/// Play the shield hit effect at the specified position.
	/// </summary>
	public void Play( Vector3 position )
	{
		transform.position = position;
		m_timer = 0.0f;
		m_isPlaying = true;

		m_shieldSphere.SetActive( true );
		m_shieldSphere.transform.localScale = Vector3.one * m_maxScale * 0.5f;
		m_shieldMaterial.SetColor( "_Color", new Color( 0.3f, 0.6f, 1.0f, 0.5f ) );

		gameObject.SetActive( true );
		m_shieldParticles.Play();
	}

	/// <summary>
	/// Check if the effect is playing.
	/// </summary>
	public bool IsPlaying()
	{
		return m_isPlaying;
	}
}
