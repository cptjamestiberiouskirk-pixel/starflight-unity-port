
using UnityEngine;

/// <summary>
/// Hull hit effect - a flash where the shot struck the hull, sparks and glowing shards thrown back the way it came, and a puff of smoke.
/// </summary>
public class HullHitEffect : MonoBehaviour
{
	// particle systems
	ParticleSystem m_flashParticles;
	ParticleSystem m_sparkParticles;
	ParticleSystem m_debrisParticles;
	ParticleSystem m_smokeParticles;

	// all of them, to start them together
	ParticleSystem[] m_particleSystems;

	// effect parameters (every particle is gone by 0.55 s, so nothing is cut off in mid air when the effect is switched off)
	float m_duration = 0.6f;
	float m_timer;
	bool m_isPlaying;

	// how far from the middle of a ship the shot strikes its hull, on the side it came from
	const float c_hullRadius = 12.0f;

	// how wide the cone is that the sparks and the shards are thrown back in
	const float c_sprayAngle = 55.0f;

	// unity awake
	void Awake()
	{
		EffectMaterials.Initialize();

		// the smoke is drawn first, then the shards, the sparks and the flash on top
		CreateSmokeParticles();
		CreateDebrisParticles();
		CreateSparkParticles();
		CreateFlashParticles();

		m_particleSystems = new ParticleSystem[] { m_smokeParticles, m_debrisParticles, m_sparkParticles, m_flashParticles };

		gameObject.SetActive( false );
	}

	// create the flash where the shot strikes
	void CreateFlashParticles()
	{
		m_flashParticles = EffectMaterials.CreateParticleSystem( transform, "Flash", EffectMaterials.m_glowMaterial, 5, 1 );

		var main = m_flashParticles.main;
		main.startLifetime = 0.12f;
		main.startSpeed = 0.0f;
		main.startSize = 28.0f;

		var emission = m_flashParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var shape = m_flashParticles.shape;
		shape.enabled = false;

		var sizeOverLifetime = m_flashParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 0.5f, 1.0f, 1.0f ) );

		var colorOverLifetime = m_flashParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( new Color( 1.0f, 1.0f, 0.9f ), new Color( 1.0f, 0.8f, 0.4f ), new Color( 1.0f, 0.5f, 0.15f ), 1.0f, 0.7f, 0.0f );
	}

	// create spark particles: hot streaks thrown back the way the shot came
	void CreateSparkParticles()
	{
		m_sparkParticles = EffectMaterials.CreateParticleSystem( transform, "Sparks", EffectMaterials.m_glowMaterial, 4, 50 );

		var main = m_sparkParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.18f, 0.45f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 80.0f, 240.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 0.8f, 1.8f );

		var emission = m_sparkParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 28 ) } );

		var shape = m_sparkParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Cone;
		shape.angle = c_sprayAngle;
		shape.radius = 2.0f;

		var velocityOverLifetime = m_sparkParticles.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.2f ) );

		var colorOverLifetime = m_sparkParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( new Color( 1.0f, 1.0f, 0.85f ), new Color( 1.0f, 0.7f, 0.25f ), new Color( 1.0f, 0.3f, 0.05f ), 1.0f, 1.0f, 0.0f );

		// stretched along their flight, so they read as streaks
		var renderer = m_sparkParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Stretch;
		renderer.velocityScale = 0.06f;
		renderer.lengthScale = 1.5f;
	}

	// create debris particles: a few shards of plating that glow, tumble and cool, with short glowing trails
	void CreateDebrisParticles()
	{
		m_debrisParticles = EffectMaterials.CreateParticleSystem( transform, "Debris", EffectMaterials.m_debrisMaterial, 1, 12 );

		var main = m_debrisParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.35f, 0.55f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 30.0f, 90.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 1.5f, 3.5f );
		main.startRotation3D = true;
		main.startRotationX = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.startRotationY = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.startRotationZ = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );

		var emission = m_debrisParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 6 ) } );

		var shape = m_debrisParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Cone;
		shape.angle = c_sprayAngle;
		shape.radius = 2.0f;

		var rotationOverLifetime = m_debrisParticles.rotationOverLifetime;
		rotationOverLifetime.enabled = true;
		rotationOverLifetime.separateAxes = true;
		rotationOverLifetime.x = new ParticleSystem.MinMaxCurve( -8.0f, 8.0f );
		rotationOverLifetime.y = new ParticleSystem.MinMaxCurve( -8.0f, 8.0f );
		rotationOverLifetime.z = new ParticleSystem.MinMaxCurve( -8.0f, 8.0f );

		var colorOverLifetime = m_debrisParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 1.0f, 0.85f, 0.5f ), 0.0f ), new GradientColorKey( new Color( 1.0f, 0.45f, 0.1f ), 0.3f ), new GradientColorKey( new Color( 0.3f, 0.12f, 0.07f ), 0.75f ), new GradientColorKey( new Color( 0.18f, 0.15f, 0.14f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 1.0f, 0.8f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		var trails = m_debrisParticles.trails;
		trails.enabled = true;
		trails.ratio = 1.0f;
		trails.lifetime = 0.12f;
		trails.minVertexDistance = 1.0f;
		trails.inheritParticleColor = true;
		trails.dieWithParticles = true;
		trails.widthOverTrail = new ParticleSystem.MinMaxCurve( 0.5f, AnimationCurve.Linear( 0.0f, 1.0f, 1.0f, 0.0f ) );
		trails.colorOverTrail = EffectMaterials.MakeGradient( Color.white, Color.white, Color.white, 1.0f, 0.5f, 0.0f );

		var renderer = m_debrisParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Mesh;
		renderer.SetMeshes( EffectMaterials.m_debrisMeshes );
		renderer.trailMaterial = EffectMaterials.m_glowMaterial;
	}

	// create smoke particles: a small puff from the breach that spreads and thins out
	void CreateSmokeParticles()
	{
		m_smokeParticles = EffectMaterials.CreateParticleSystem( transform, "Smoke", EffectMaterials.m_smokeMaterial, 0, 4 );

		var main = m_smokeParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.35f, 0.55f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 5.0f, 20.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 10.0f, 18.0f );
		main.startRotation = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.flipRotation = 0.5f;

		var emission = m_smokeParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 3 ) } );

		var shape = m_smokeParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Cone;
		shape.angle = c_sprayAngle;
		shape.radius = 3.0f;

		var sizeOverLifetime = m_smokeParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 0.6f, 1.0f, 1.6f ) );

		var colorOverLifetime = m_smokeParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 0.4f, 0.27f, 0.18f ), 0.0f ), new GradientColorKey( new Color( 0.2f, 0.18f, 0.16f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 0.0f, 0.0f ), new GradientAlphaKey( 0.45f, 0.2f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;
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
	/// Play the hull hit effect at the specified position (with no direction the sparks are thrown out all around).
	/// </summary>
	public void Play( Vector3 position )
	{
		StartEffect( position, Vector3.zero );
	}

	/// <summary>
	/// Play with direction for directional debris (the direction the shot was travelling in).
	/// </summary>
	public void Play( Vector3 position, Vector3 hitDirection )
	{
		StartEffect( position, hitDirection );
	}

	// start the effect: the shot strikes the hull on the side it came from, and the sparks and the shards are thrown back that way
	void StartEffect( Vector3 position, Vector3 hitDirection )
	{
		// the direction in the plane the ships fly in (a hit from straight above or below has none)
		var flatDirection = new Vector3( hitDirection.x, 0.0f, hitDirection.z );
		var hasDirection = flatDirection.sqrMagnitude > 0.01f;

		if ( hasDirection )
		{
			var backTowardsTheShot = -flatDirection.normalized;

			transform.position = position + backTowardsTheShot * c_hullRadius;
			transform.rotation = Quaternion.LookRotation( backTowardsTheShot, Vector3.up );
		}
		else
		{
			transform.position = position;
			transform.rotation = Quaternion.identity;
		}

		// a cone back towards the shot, or all around with no direction
		SetSprayShape( m_sparkParticles, hasDirection );
		SetSprayShape( m_debrisParticles, hasDirection );
		SetSprayShape( m_smokeParticles, hasDirection );

		m_timer = 0.0f;
		m_isPlaying = true;

		gameObject.SetActive( true );

		// start every part from the beginning
		foreach ( var particleSystem in m_particleSystems )
		{
			particleSystem.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
			particleSystem.Play();
		}
	}

	// throw the particles out in a cone, or all around in the plane the ships fly in
	void SetSprayShape( ParticleSystem particleSystem, bool cone )
	{
		var shape = particleSystem.shape;

		if ( cone )
		{
			shape.shapeType = ParticleSystemShapeType.Cone;
			shape.scale = Vector3.one;
		}
		else
		{
			shape.shapeType = ParticleSystemShapeType.Sphere;
			shape.scale = new Vector3( 1.0f, 0.35f, 1.0f );
		}
	}

	/// <summary>
	/// Check if the effect is playing.
	/// </summary>
	public bool IsPlaying()
	{
		return m_isPlaying;
	}
}
