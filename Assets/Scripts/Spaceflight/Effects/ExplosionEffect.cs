
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

	// the size of the textures that are made for the explosions
	const int c_textureSize = 128;

	// true once whoever is waiting for the explosion has been told that it is over
	bool m_isComplete;

	// callback when explosion finishes
	System.Action m_onComplete;

	// the materials and the debris meshes, made once and shared by every explosion (they are made in code, so nothing in the build has to refer to them)
	static Material m_glowMaterial;
	static Material m_fireMaterial;
	static Material m_smokeMaterial;
	static Material m_ringMaterial;
	static Material m_debrisMaterial;
	static Mesh[] m_debrisMeshes;

	// unity awake
	void Awake()
	{
		CreateSharedAssets();

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

	// make a particle system that bursts once when the explosion plays
	ParticleSystem CreateParticleSystem( string name, Material material, int sortingOrder, int maxParticles )
	{
		var particleObject = new GameObject( name );
		particleObject.transform.SetParent( transform );
		particleObject.transform.localPosition = Vector3.zero;

		var particleSystem = particleObject.AddComponent<ParticleSystem>();

		// a new particle system is playing, and its duration can only be set while it is stopped
		particleSystem.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		var main = particleSystem.main;
		main.duration = 2.0f;
		main.loop = false;
		main.playOnAwake = false;
		main.maxParticles = maxParticles;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		// follow the scale of the explosion (a missile hit is half the size of a ship blowing up)
		main.scalingMode = ParticleSystemScalingMode.Hierarchy;

		// every particle comes from the burst
		var emission = particleSystem.emission;
		emission.rateOverTime = 0;

		var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
		renderer.sharedMaterial = material;
		renderer.sortingOrder = sortingOrder;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;

		return particleSystem;
	}

	// create the white hot flash at the start
	void CreateFlashParticles()
	{
		m_flashParticles = CreateParticleSystem( "Flash", m_glowMaterial, 5, 1 );

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
		colorOverLifetime.color = MakeGradient( new Color( 1.0f, 1.0f, 0.95f ), new Color( 1.0f, 0.85f, 0.4f ), new Color( 1.0f, 0.6f, 0.2f ), 1.0f, 0.8f, 0.0f );
	}

	// create the fireball: puffs of fire that billow out, cool from yellow to deep red and burn out
	void CreateFireParticles()
	{
		m_fireParticles = CreateParticleSystem( "Fire", m_fireMaterial, 2, 24 );

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
		m_ringParticles = CreateParticleSystem( "Shockwave", m_ringMaterial, 3, 1 );

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
		colorOverLifetime.color = MakeGradient( new Color( 1.0f, 0.9f, 0.7f ), new Color( 1.0f, 0.65f, 0.3f ), new Color( 1.0f, 0.45f, 0.15f ), 0.3f, 0.12f, 0.0f );

		// the camera looks down on the plane the ships fly in, so the ring lies in that plane
		var renderer = m_ringParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
	}

	// create spark particles: streaks that fly out fast and burn out
	void CreateSparkParticles()
	{
		m_sparkParticles = CreateParticleSystem( "Sparks", m_glowMaterial, 4, 100 );

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
		colorOverLifetime.color = MakeGradient( new Color( 1.0f, 1.0f, 0.85f ), new Color( 1.0f, 0.7f, 0.25f ), new Color( 1.0f, 0.3f, 0.05f ), 1.0f, 1.0f, 0.0f );

		// stretched along their flight, so they read as streaks
		var renderer = m_sparkParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Stretch;
		renderer.velocityScale = 0.08f;
		renderer.lengthScale = 2.0f;
	}

	// create debris particles: tumbling shards that glow white hot, cool to dark metal, and leave short glowing trails
	void CreateDebrisParticles()
	{
		m_debrisParticles = CreateParticleSystem( "Debris", m_debrisMaterial, 1, 30 );

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
		trails.colorOverTrail = MakeGradient( Color.white, Color.white, Color.white, 1.0f, 0.5f, 0.0f );

		var renderer = m_debrisParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Mesh;
		renderer.SetMeshes( m_debrisMeshes );
		renderer.trailMaterial = m_glowMaterial;
	}

	// create smoke particles: dark billows that rise out of the fire, spread and thin out
	void CreateSmokeParticles()
	{
		m_smokeParticles = CreateParticleSystem( "Smoke", m_smokeMaterial, 0, 20 );

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

	// a gradient from a start colour through a middle colour to an end colour, with the alpha going from start through middle to end
	static Gradient MakeGradient( Color startColor, Color middleColor, Color endColor, float startAlpha, float middleAlpha, float endAlpha )
	{
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( startColor, 0.0f ), new GradientColorKey( middleColor, 0.3f ), new GradientColorKey( endColor, 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( startAlpha, 0.0f ), new GradientAlphaKey( middleAlpha, 0.6f ), new GradientAlphaKey( endAlpha, 1.0f ) }
		);
		return gradient;
	}

	// make the materials, textures and debris meshes the explosions share (once: they are kept when a scene unloads)
	static void CreateSharedAssets()
	{
		// already made (a destroyed material compares equal to null, so they are made again if anything destroyed them)
		if ( ( m_glowMaterial != null ) && ( m_fireMaterial != null ) && ( m_smokeMaterial != null ) && ( m_ringMaterial != null ) && ( m_debrisMaterial != null ) && ( m_debrisMeshes != null ) )
		{
			return;
		}

		// the shader is in a Resources folder, so it is in every build
		var shader = Resources.Load<Shader>( "Shaders/Explosion Particle" );

		if ( shader == null )
		{
			Debug.LogWarning( "ExplosionEffect: the explosion particle shader was not found, using the sprite shader" );

			shader = Shader.Find( "Sprites/Default" );
		}

		// the flash, the sparks and the shockwave only add light, the fire also hides some of what is behind it (so it stays orange over a blue planet), smoke and debris cover it
		m_glowMaterial = CreateMaterial( "Explosion Glow", shader, MakeTexture( "Explosion Glow", 64, GlowAlpha ), 0.0f );
		m_fireMaterial = CreateMaterial( "Explosion Fire", shader, MakeTexture( "Explosion Fire", c_textureSize, FireAlpha ), 0.6f );
		m_ringMaterial = CreateMaterial( "Explosion Shockwave", shader, MakeTexture( "Explosion Shockwave", c_textureSize, RingAlpha ), 0.0f );
		m_smokeMaterial = CreateMaterial( "Explosion Smoke", shader, MakeTexture( "Explosion Smoke", c_textureSize, SmokeAlpha ), 1.0f );
		m_debrisMaterial = CreateMaterial( "Explosion Debris", shader, MakeShadeTexture(), 1.0f );

		m_debrisMeshes = new Mesh[] { MakeShardMesh(), MakeSliverMesh() };
	}

	// make a material for the explosion particles (occlusion: how much of what is behind a particle it hides, from 0 for pure glow to 1 for ordinary transparency)
	static Material CreateMaterial( string name, Shader shader, Texture2D texture, float occlusion )
	{
		var material = new Material( shader );
		material.name = name;
		material.mainTexture = texture;
		material.SetFloat( "_Occlusion", occlusion );
		material.renderQueue = 3000;

		// kept for the whole session, like the textures (Resources.UnloadUnusedAssets would otherwise free them between scenes)
		material.hideFlags = HideFlags.DontUnloadUnusedAsset;

		return material;
	}

	// make a white texture whose alpha is the shape of a particle (alphaAt gets the distance from the middle, 0 to 1 at the edge, and the position from -1 to 1)
	static Texture2D MakeTexture( string name, int size, System.Func<float, float, float, float> alphaAt )
	{
		var texture = new Texture2D( size, size, TextureFormat.RGBA32, true );
		texture.name = name;
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Bilinear;
		texture.hideFlags = HideFlags.DontUnloadUnusedAsset;

		var pixels = new Color32[ size * size ];

		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				// position from -1 to 1, through the middle of the pixel
				var u = ( ( x + 0.5f ) / size ) * 2.0f - 1.0f;
				var v = ( ( y + 0.5f ) / size ) * 2.0f - 1.0f;

				var distance = Mathf.Sqrt( u * u + v * v );

				// nothing outside the circle, so no particle shows the corners of its square
				var alpha = ( distance >= 1.0f ) ? 0.0f : Mathf.Clamp01( alphaAt( distance, u, v ) );

				pixels[ y * size + x ] = new Color32( 255, 255, 255, (byte) Mathf.RoundToInt( alpha * 255.0f ) );
			}
		}

		texture.SetPixels32( pixels );
		texture.Apply( true, true );

		return texture;
	}

	// a soft glow with a hot middle
	static float GlowAlpha( float distance, float u, float v )
	{
		var falloff = 1.0f - distance;

		return falloff * falloff * 0.6f + Mathf.Pow( falloff, 6.0f ) * 0.4f;
	}

	// a puff of fire: a round glow with ragged, billowing edges and brighter and darker wisps inside
	static float FireAlpha( float distance, float u, float v )
	{
		var edgeNoise = FractalNoise( u * 3.0f, v * 3.0f, 11 );
		var innerNoise = FractalNoise( u * 6.0f + 17.0f, v * 6.0f + 5.0f, 23 );

		var ragged = distance + ( edgeNoise - 0.5f ) * 0.75f;
		var shape = 1.0f - Mathf.SmoothStep( 0.25f, 0.9f, ragged );

		return shape * ( 0.3f + 0.7f * innerNoise * innerNoise ) * Mathf.Clamp01( ( 1.0f - distance ) * 4.0f );
	}

	// a puff of smoke: softer and lumpier than the fire
	static float SmokeAlpha( float distance, float u, float v )
	{
		var edgeNoise = FractalNoise( u * 2.0f + 31.0f, v * 2.0f + 7.0f, 37 );
		var innerNoise = FractalNoise( u * 4.0f + 3.0f, v * 4.0f + 41.0f, 53 );

		var ragged = distance + ( edgeNoise - 0.5f ) * 0.6f;
		var shape = 1.0f - Mathf.SmoothStep( 0.15f, 1.0f, ragged );

		return shape * ( 0.45f + 0.55f * innerNoise ) * Mathf.Clamp01( ( 1.0f - distance ) * 4.0f );
	}

	// the shockwave: a bright thin ring near the edge with a faint haze inside it
	static float RingAlpha( float distance, float u, float v )
	{
		var ringOffset = ( distance - 0.85f ) / 0.035f;
		var ring = Mathf.Exp( -ringOffset * ringOffset );
		var haze = 0.06f * Mathf.SmoothStep( 0.0f, 0.85f, distance ) * ( ( distance < 0.85f ) ? 1.0f : 0.0f );

		return ( ring + haze ) * Mathf.Clamp01( ( 1.0f - distance ) * 8.0f );
	}

	// the debris texture: a strip of greys, so that each face of a shard, which takes its whole colour from one place in the strip, is shaded differently
	static Texture2D MakeShadeTexture()
	{
		var texture = new Texture2D( c_shadeCount, 1, TextureFormat.RGBA32, false );
		texture.name = "Explosion Debris";
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Point;
		texture.hideFlags = HideFlags.DontUnloadUnusedAsset;

		var pixels = new Color32[ c_shadeCount ];

		for ( int i = 0; i < c_shadeCount; i++ )
		{
			var shade = (byte) Mathf.RoundToInt( Mathf.Lerp( 0.35f, 1.0f, i / (float) ( c_shadeCount - 1 ) ) * 255.0f );

			pixels[ i ] = new Color32( shade, shade, shade, 255 );
		}

		texture.SetPixels32( pixels );
		texture.Apply( false, true );

		return texture;
	}

	// how many shades the debris texture has
	const int c_shadeCount = 8;

	// a shard of hull: an uneven tetrahedron
	static Mesh MakeShardMesh()
	{
		var a = new Vector3( 0.0f, 0.6f, 0.0f );
		var b = new Vector3( -0.5f, -0.3f, 0.35f );
		var c = new Vector3( 0.55f, -0.25f, 0.3f );
		var d = new Vector3( 0.05f, -0.2f, -0.6f );

		return MakeFacetedMesh( "Explosion Shard", new Vector3[] { a, b, c, a, c, d, a, d, b, b, d, c }, new int[] { 7, 4, 2, 0 } );
	}

	// a sliver of plating: a thin triangular slab
	static Mesh MakeSliverMesh()
	{
		var top0 = new Vector3( 0.0f, 0.12f, 0.7f );
		var top1 = new Vector3( -0.45f, 0.12f, -0.4f );
		var top2 = new Vector3( 0.5f, 0.12f, -0.3f );
		var bottom0 = new Vector3( 0.0f, -0.12f, 0.7f );
		var bottom1 = new Vector3( -0.45f, -0.12f, -0.4f );
		var bottom2 = new Vector3( 0.5f, -0.12f, -0.3f );

		var corners = new Vector3[]
		{
			// the two flat faces
			top0, top2, top1,
			bottom0, bottom1, bottom2,

			// the three edges, two triangles each
			top0, bottom0, bottom2, top0, bottom2, top2,
			top2, bottom2, bottom1, top2, bottom1, top1,
			top1, bottom1, bottom0, top1, bottom0, top0
		};

		return MakeFacetedMesh( "Explosion Sliver", corners, new int[] { 7, 1, 5, 5, 3, 3, 6, 6 } );
	}

	// make a mesh whose every triangle has corners of its own, all at the same place in the shade texture, so each face is flat and has its own shade
	static Mesh MakeFacetedMesh( string name, Vector3[] corners, int[] shadeOfTriangle )
	{
		var triangleCount = corners.Length / 3;

		var uvs = new Vector2[ corners.Length ];
		var triangles = new int[ corners.Length ];

		for ( int i = 0; i < corners.Length; i++ )
		{
			// guard against a shade list shorter than the triangle list
			var triangle = i / 3;
			var shade = ( triangle < shadeOfTriangle.Length ) ? shadeOfTriangle[ triangle ] : 0;

			// the middle of the shade's pixel
			uvs[ i ] = new Vector2( ( Mathf.Clamp( shade, 0, c_shadeCount - 1 ) + 0.5f ) / c_shadeCount, 0.5f );
			triangles[ i ] = i;
		}

		var mesh = new Mesh();
		mesh.name = name;
		mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
		mesh.vertices = corners;
		mesh.uv = uvs;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();

		Debug.Assert( triangleCount == shadeOfTriangle.Length, "ExplosionEffect: " + name + " has " + triangleCount + " triangles and " + shadeOfTriangle.Length + " shades" );

		return mesh;
	}

	// fractal value noise from 0 to 1, from a hash of the lattice points (not from Random: the explosions are made while the Spaceflight scene starts, and the planets need the game's random numbers to stay where they are)
	static float FractalNoise( float x, float y, int seed )
	{
		var total = 0.0f;
		var amplitude = 0.5f;
		var amplitudeSum = 0.0f;

		for ( int octave = 0; octave < 4; octave++ )
		{
			total += ValueNoise( x, y, seed + octave * 101 ) * amplitude;
			amplitudeSum += amplitude;

			x *= 2.0f;
			y *= 2.0f;
			amplitude *= 0.5f;
		}

		return total / amplitudeSum;
	}

	// value noise from 0 to 1: the hashes of the four lattice points around the position, blended smoothly
	static float ValueNoise( float x, float y, int seed )
	{
		var x0 = Mathf.FloorToInt( x );
		var y0 = Mathf.FloorToInt( y );

		var fx = x - x0;
		var fy = y - y0;

		// smoothstep, so the lattice does not show
		fx = fx * fx * ( 3.0f - 2.0f * fx );
		fy = fy * fy * ( 3.0f - 2.0f * fy );

		var bottom = Mathf.Lerp( Hash( x0, y0, seed ), Hash( x0 + 1, y0, seed ), fx );
		var top = Mathf.Lerp( Hash( x0, y0 + 1, seed ), Hash( x0 + 1, y0 + 1, seed ), fx );

		return Mathf.Lerp( bottom, top, fy );
	}

	// a number from 0 to 1 that depends only on the lattice point and the seed
	static float Hash( int x, int y, int seed )
	{
		unchecked
		{
			var h = (uint) ( x * 374761393 + y * 668265263 + seed * 2147483647 );

			h = ( h ^ ( h >> 13 ) ) * 1274126177u;
			h ^= h >> 16;

			return ( h & 0xFFFFFF ) / (float) 0x1000000;
		}
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
