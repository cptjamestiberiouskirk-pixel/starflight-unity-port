
using UnityEngine;

/// <summary>
/// Explosion visual effect using particle systems: a flash, a fireball, a shockwave ring, sparks, glowing debris, smoke, and a flash of light.
/// </summary>
public class ExplosionEffect : MonoBehaviour
{
	// particle systems
	ParticleSystem m_flashParticles;
	ParticleSystem m_fireParticles;
	ParticleSystem m_ringParticles;
	ParticleSystem m_sparkParticles;
	ParticleSystem m_debrisParticles;
	ParticleSystem m_smokeParticles;

	// all of them, to start them together
	ParticleSystem[] m_particleSystems;

	// the flash of light that lights up the ships and the wreck nearby
	Light m_light;

	// explosion parameters
	float m_duration = 1.5f;
	float m_timer;
	bool m_isPlaying;

	// how long the explosion stays switched on: the debris and the smoke live for at most two seconds, and the smoke starts a tenth of a second in
	const float c_particleTime = 2.2f;

	// the light: how bright it gets, how far it reaches at full size, how long it takes to come up and how fast it dies away
	const float c_lightIntensity = 4.0f;
	const float c_lightRange = 500.0f;
	const float c_lightRiseTime = 0.04f;
	const float c_lightDecay = 7.0f;

	// true once whoever is waiting for the explosion has been told that it is over
	bool m_isComplete;

	// callback when explosion finishes
	System.Action m_onComplete;

	// unity awake
	void Awake()
	{
		EffectMaterials.Initialize();

		// the smoke is drawn first, under the debris, then the fire, the ring, the sparks and the flash on top
		CreateSmokeParticles();
		CreateDebrisParticles();
		CreateFireParticles();
		CreateRingParticles();
		CreateSparkParticles();
		CreateFlashParticles();

		m_particleSystems = new ParticleSystem[] { m_smokeParticles, m_debrisParticles, m_fireParticles, m_ringParticles, m_sparkParticles, m_flashParticles };

		CreateLight();

		gameObject.SetActive( false );
	}

	// create the white hot flash at the start
	void CreateFlashParticles()
	{
		m_flashParticles = EffectMaterials.CreateParticleSystem( transform, "Flash", EffectMaterials.m_glowMaterial, 5, 1 );

		var main = m_flashParticles.main;
		main.startLifetime = 0.25f;
		main.startSpeed = 0.0f;
		main.startSize = 160.0f;

		var emission = m_flashParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var shape = m_flashParticles.shape;
		shape.enabled = false;

		// it swells as it fades
		var sizeOverLifetime = m_flashParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 0.3f, 1.0f, 1.0f ) );

		var colorOverLifetime = m_flashParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( new Color( 1.0f, 1.0f, 0.95f ), new Color( 1.0f, 0.85f, 0.4f ), new Color( 1.0f, 0.6f, 0.2f ), 1.0f, 0.8f, 0.0f );
	}

	// create the fireball: puffs of fire that billow out, cool from yellow to deep red and burn out
	void CreateFireParticles()
	{
		m_fireParticles = EffectMaterials.CreateParticleSystem( transform, "Fire", EffectMaterials.m_fireMaterial, 2, 24 );

		var main = m_fireParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.7f, 1.2f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 20.0f, 60.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 45.0f, 90.0f );
		main.startRotation = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.flipRotation = 0.5f;

		var emission = m_fireParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 16 ) } );

		var shape = m_fireParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 10.0f;

		// they slow down as they spread
		var velocityOverLifetime = m_fireParticles.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.1f ) );

		var sizeOverLifetime = m_fireParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, new AnimationCurve( new Keyframe( 0.0f, 0.5f, 3.0f, 3.0f ), new Keyframe( 0.3f, 1.0f, 0.8f, 0.8f ), new Keyframe( 1.0f, 1.4f, 0.4f, 0.4f ) ) );

		var rotationOverLifetime = m_fireParticles.rotationOverLifetime;
		rotationOverLifetime.enabled = true;
		rotationOverLifetime.z = new ParticleSystem.MinMaxCurve( -1.0f, 1.0f );

		var colorOverLifetime = m_fireParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 1.0f, 0.9f, 0.6f ), 0.0f ), new GradientColorKey( new Color( 1.0f, 0.6f, 0.2f ), 0.15f ), new GradientColorKey( new Color( 0.9f, 0.3f, 0.06f ), 0.45f ), new GradientColorKey( new Color( 0.35f, 0.07f, 0.03f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 0.8f, 0.0f ), new GradientAlphaKey( 0.7f, 0.4f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;
	}

	// create the shockwave: a faint ring that races out flat and fades
	void CreateRingParticles()
	{
		m_ringParticles = EffectMaterials.CreateParticleSystem( transform, "Shockwave", EffectMaterials.m_ringMaterial, 3, 1 );

		var main = m_ringParticles.main;
		main.startLifetime = 0.45f;
		main.startSpeed = 0.0f;
		main.startSize = 300.0f;

		var emission = m_ringParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var shape = m_ringParticles.shape;
		shape.enabled = false;

		// fast at first, then slowing down
		var sizeOverLifetime = m_ringParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, new AnimationCurve( new Keyframe( 0.0f, 0.08f, 3.5f, 3.5f ), new Keyframe( 1.0f, 1.0f, 0.2f, 0.2f ) ) );

		var colorOverLifetime = m_ringParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( new Color( 1.0f, 0.9f, 0.7f ), new Color( 1.0f, 0.65f, 0.3f ), new Color( 1.0f, 0.45f, 0.15f ), 0.3f, 0.12f, 0.0f );

		// the camera looks down on the plane the ships fly in, so the ring lies in that plane
		var renderer = m_ringParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
	}

	// create spark particles: streaks that fly out fast and burn out
	void CreateSparkParticles()
	{
		m_sparkParticles = EffectMaterials.CreateParticleSystem( transform, "Sparks", EffectMaterials.m_glowMaterial, 4, 100 );

		var main = m_sparkParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.4f, 1.0f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 150.0f, 360.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 1.5f, 3.5f );

		var emission = m_sparkParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 70 ) } );

		// mostly out along the plane the ships fly in, where they are seen from above
		var shape = m_sparkParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 6.0f;
		shape.scale = new Vector3( 1.0f, 0.35f, 1.0f );

		var velocityOverLifetime = m_sparkParticles.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.15f ) );

		var colorOverLifetime = m_sparkParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( new Color( 1.0f, 1.0f, 0.85f ), new Color( 1.0f, 0.7f, 0.25f ), new Color( 1.0f, 0.3f, 0.05f ), 1.0f, 1.0f, 0.0f );

		// stretched along their flight, so they read as streaks
		var renderer = m_sparkParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Stretch;
		renderer.velocityScale = 0.08f;
		renderer.lengthScale = 2.0f;
	}

	// create debris particles: tumbling shards that glow white hot, cool to dark metal, and leave short glowing trails
	void CreateDebrisParticles()
	{
		m_debrisParticles = EffectMaterials.CreateParticleSystem( transform, "Debris", EffectMaterials.m_debrisMaterial, 1, 30 );

		var main = m_debrisParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 1.5f, 2.0f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 35.0f, 110.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 4.0f, 10.0f );
		main.startRotation3D = true;
		main.startRotationX = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.startRotationY = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.startRotationZ = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );

		var emission = m_debrisParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 16 ) } );

		var shape = m_debrisParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 8.0f;
		shape.scale = new Vector3( 1.0f, 0.4f, 1.0f );

		var velocityOverLifetime = m_debrisParticles.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.35f ) );

		var rotationOverLifetime = m_debrisParticles.rotationOverLifetime;
		rotationOverLifetime.enabled = true;
		rotationOverLifetime.separateAxes = true;
		rotationOverLifetime.x = new ParticleSystem.MinMaxCurve( -4.0f, 4.0f );
		rotationOverLifetime.y = new ParticleSystem.MinMaxCurve( -4.0f, 4.0f );
		rotationOverLifetime.z = new ParticleSystem.MinMaxCurve( -4.0f, 4.0f );

		var colorOverLifetime = m_debrisParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 1.0f, 0.9f, 0.6f ), 0.0f ), new GradientColorKey( new Color( 1.0f, 0.5f, 0.12f ), 0.2f ), new GradientColorKey( new Color( 0.45f, 0.14f, 0.06f ), 0.55f ), new GradientColorKey( new Color( 0.16f, 0.13f, 0.12f ), 0.85f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 1.0f, 0.85f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;

		// short glowing trails in the colour of the shard (they fade out once the shard has cooled)
		var trails = m_debrisParticles.trails;
		trails.enabled = true;
		trails.ratio = 1.0f;
		trails.lifetime = 0.3f;
		trails.minVertexDistance = 2.0f;
		trails.inheritParticleColor = true;
		trails.dieWithParticles = true;
		trails.widthOverTrail = new ParticleSystem.MinMaxCurve( 0.6f, AnimationCurve.Linear( 0.0f, 1.0f, 1.0f, 0.0f ) );
		trails.colorOverTrail = EffectMaterials.MakeGradient( Color.white, Color.white, Color.white, 1.0f, 0.5f, 0.0f );

		var renderer = m_debrisParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Mesh;
		renderer.SetMeshes( EffectMaterials.m_debrisMeshes );
		renderer.trailMaterial = EffectMaterials.m_glowMaterial;
	}

	// create smoke particles: dark billows that rise out of the fire, spread and thin out
	void CreateSmokeParticles()
	{
		m_smokeParticles = EffectMaterials.CreateParticleSystem( transform, "Smoke", EffectMaterials.m_smokeMaterial, 0, 20 );

		var main = m_smokeParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 1.3f, 1.9f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 12.0f, 35.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 55.0f, 95.0f );
		main.startRotation = new ParticleSystem.MinMaxCurve( 0.0f, Mathf.PI * 2.0f );
		main.flipRotation = 0.5f;

		// a little after the flash, out of the fire
		var emission = m_smokeParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.1f, 12 ) } );

		var shape = m_smokeParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 20.0f;

		var velocityOverLifetime = m_smokeParticles.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 1.0f, 1.0f, 0.3f ) );

		var sizeOverLifetime = m_smokeParticles.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, new AnimationCurve( new Keyframe( 0.0f, 0.6f, 2.0f, 2.0f ), new Keyframe( 1.0f, 1.8f, 0.6f, 0.6f ) ) );

		var rotationOverLifetime = m_smokeParticles.rotationOverLifetime;
		rotationOverLifetime.enabled = true;
		rotationOverLifetime.z = new ParticleSystem.MinMaxCurve( -0.5f, 0.5f );

		var colorOverLifetime = m_smokeParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( new Color( 0.4f, 0.26f, 0.17f ), 0.0f ), new GradientColorKey( new Color( 0.2f, 0.17f, 0.15f ), 0.4f ), new GradientColorKey( new Color( 0.1f, 0.1f, 0.1f ), 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 0.0f, 0.0f ), new GradientAlphaKey( 0.6f, 0.12f ), new GradientAlphaKey( 0.35f, 0.5f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		colorOverLifetime.color = gradient;
	}

	// create the light that flashes with the explosion
	void CreateLight()
	{
		m_light = gameObject.AddComponent<Light>();
		m_light.type = LightType.Point;
		m_light.color = new Color( 1.0f, 0.6f, 0.25f );
		m_light.intensity = 0.0f;
		m_light.range = c_lightRange;
		m_light.shadows = LightShadows.None;
	}

	// unity update
	void Update()
	{
		if ( m_isPlaying )
		{
			// use unscaled delta time so explosion finishes even when game is paused
			m_timer += Time.unscaledDeltaTime;

			// the light comes up at once and dies away
			if ( m_timer < c_lightRiseTime )
			{
				m_light.intensity = c_lightIntensity * ( m_timer / c_lightRiseTime );
			}
			else
			{
				m_light.intensity = c_lightIntensity * Mathf.Exp( -( m_timer - c_lightRiseTime ) * c_lightDecay );
			}

			// tell whoever is waiting that the explosion is over (once, and at the same time as before)
			if ( !m_isComplete && ( m_timer >= m_duration ) )
			{
				m_isComplete = true;
				m_onComplete?.Invoke();
			}

			// switch the explosion off once its last particles are gone (it was switched off with the callback, which cut the smoke and the debris off in mid air)
			if ( m_timer >= c_particleTime )
			{
				m_isPlaying = false;
				m_light.intensity = 0.0f;
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
		m_isComplete = false;
		m_onComplete = onComplete;

		// the light reaches as far as the explosion is big
		m_light.range = c_lightRange * transform.localScale.x;
		m_light.intensity = 0.0f;

		gameObject.SetActive( true );

		// start every part from the beginning (one that was switched off half way would otherwise carry on where it was)
		foreach ( var particleSystem in m_particleSystems )
		{
			particleSystem.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
			particleSystem.Play();
		}
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
