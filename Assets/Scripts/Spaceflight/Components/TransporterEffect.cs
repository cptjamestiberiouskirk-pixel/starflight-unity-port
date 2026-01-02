
using UnityEngine;
using System.Collections.Generic;

// This component creates a Star Trek-style transporter dematerialization effect.
// It makes the object shimmer and fade out with sparkle particles.
// This effect is designed to match the transporter effect used in the docking bay.
public class TransporterEffect : MonoBehaviour
{
	// duration of the effect in seconds
	const float c_effectDuration = 1.5f;

	// particle settings (used as fallback if prefab not found)
	const int c_particleCount = 50;
	const float c_particleSpeed = 2.0f;
	const float c_particleSize = 0.15f;

	// path to the starport transporter prefab
	const string c_transporterPrefabPath = "Starport Transporter Light";

	// effect state
	float m_effectTimer;
	bool m_isPlaying;

	// original renderers and their materials
	List<Renderer> m_renderers = new List<Renderer>();
	List<Material[]> m_originalMaterials = new List<Material[]>();
	List<Color[]> m_originalColors = new List<Color[]>();

	// original scale for shrink effect
	Vector3 m_originalScale;

	// particle system for sparkles (either from prefab or created programmatically)
	ParticleSystem m_particleSystem;

	// the instantiated prefab (if using prefab)
	GameObject m_particlePrefabInstance;

	// callback when effect completes
	System.Action m_onComplete;

	// start the transporter effect
	public void Play( System.Action onComplete = null )
	{
		if ( m_isPlaying )
		{
			return;
		}

		m_onComplete = onComplete;
		m_isPlaying = true;
		m_effectTimer = 0;

		// store original scale for shrink effect
		m_originalScale = transform.localScale;

		// gather all renderers in this object and children
		m_renderers.Clear();
		m_originalMaterials.Clear();
		m_originalColors.Clear();

		var allRenderers = GetComponentsInChildren<Renderer>();

		foreach ( var renderer in allRenderers )
		{
			// skip particle system renderers
			if ( renderer is ParticleSystemRenderer )
			{
				continue;
			}

			m_renderers.Add( renderer );

			// store original materials
			m_originalMaterials.Add( renderer.materials );

			// store original colors
			var colors = new Color[ renderer.materials.Length ];

			for ( int i = 0; i < renderer.materials.Length; i++ )
			{
				if ( renderer.materials[ i ].HasProperty( "_Color" ) )
				{
					colors[ i ] = renderer.materials[ i ].color;
				}
				else
				{
					colors[ i ] = Color.white;
				}
			}

			m_originalColors.Add( colors );

			// switch materials to transparent rendering mode so alpha changes work
			SetMaterialsToTransparent( renderer );
		}

		// try to use the starport transporter prefab, fall back to programmatic particles
		if ( !TryLoadTransporterPrefab() )
		{
			// create particle system for sparkles as fallback
			CreateParticleSystem();
		}
	}

	// try to load and instantiate the starport transporter prefab
	// Note: To use the same prefab as the docking bay, move "Starport Transporter Light.prefab"
	// to a Resources folder, or set up a reference through a singleton like SpaceflightController.
	// Currently falls back to programmatic particles which provide similar visual effect.
	bool TryLoadTransporterPrefab()
	{
		// try to load the prefab from Resources folder
		var prefab = Resources.Load<GameObject>( c_transporterPrefabPath );

		if ( prefab != null )
		{
			// calculate the center of the object
			Vector3 center = transform.position;

			if ( m_renderers.Count > 0 )
			{
				var bounds = m_renderers[ 0 ].bounds;

				foreach ( var renderer in m_renderers )
				{
					bounds.Encapsulate( renderer.bounds );
				}

				center = bounds.center;
			}

			// instantiate the prefab at the object's center
			m_particlePrefabInstance = Instantiate( prefab, center, Quaternion.identity );

			// get the particle system from the prefab
			m_particleSystem = m_particlePrefabInstance.GetComponent<ParticleSystem>();

			if ( m_particleSystem == null )
			{
				m_particleSystem = m_particlePrefabInstance.GetComponentInChildren<ParticleSystem>();
			}

			if ( m_particleSystem != null )
			{
				// start playing the particle system
				m_particleSystem.Play();
				return true;
			}
			else
			{
				// prefab didn't have a particle system, clean it up
				Destroy( m_particlePrefabInstance );
				m_particlePrefabInstance = null;
			}
		}

		return false;
	}

	// switch a renderer's materials to transparent rendering mode
	void SetMaterialsToTransparent( Renderer renderer )
	{
		foreach ( var material in renderer.materials )
		{
			// for Standard shader and URP Lit shader
			if ( material.HasProperty( "_Surface" ) )
			{
				// URP Lit shader - set surface type to transparent
				material.SetFloat( "_Surface", 1 ); // 0 = Opaque, 1 = Transparent
				material.SetFloat( "_Blend", 0 ); // 0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
			}

			if ( material.HasProperty( "_Mode" ) )
			{
				// Standard shader - set rendering mode to Fade
				material.SetFloat( "_Mode", 2 ); // 0 = Opaque, 1 = Cutout, 2 = Fade, 3 = Transparent
			}

			// set up blend modes for transparency
			material.SetInt( "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha );
			material.SetInt( "_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha );
			material.SetInt( "_ZWrite", 0 );
			material.DisableKeyword( "_ALPHATEST_ON" );
			material.EnableKeyword( "_ALPHABLEND_ON" );
			material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
			material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
		}
	}

	void CreateParticleSystem()
	{
		// create a child object for the particle system
		var particleObj = new GameObject( "TransporterParticles" );
		particleObj.transform.SetParent( transform );
		particleObj.transform.localPosition = Vector3.zero;

		m_particleSystem = particleObj.AddComponent<ParticleSystem>();

		// stop the particle system before configuring (Unity auto-plays on AddComponent)
		m_particleSystem.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		// configure main module
		var main = m_particleSystem.main;
		main.duration = c_effectDuration;
		main.loop = false;
		main.startLifetime = 0.5f;
		main.startSpeed = c_particleSpeed;
		main.startSize = c_particleSize;
		main.startColor = new Color( 0.7f, 0.85f, 1.0f, 1.0f ); // light blue
		main.maxParticles = c_particleCount * 3;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.playOnAwake = false;

		// configure emission
		var emission = m_particleSystem.emission;
		emission.rateOverTime = c_particleCount;

		// configure shape - emit from the object's bounds
		var shape = m_particleSystem.shape;
		shape.shapeType = ParticleSystemShapeType.Box;

		// try to get bounds from renderers
		if ( m_renderers.Count > 0 )
		{
			var bounds = m_renderers[ 0 ].bounds;

			foreach ( var renderer in m_renderers )
			{
				bounds.Encapsulate( renderer.bounds );
			}

			shape.scale = bounds.size;
			particleObj.transform.position = bounds.center;
		}
		else
		{
			shape.scale = Vector3.one;
		}

		// configure velocity over lifetime - particles float upward
		// all axes must use the same curve mode to avoid Unity errors
		var velocityOverLifetime = m_particleSystem.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.x = new ParticleSystem.MinMaxCurve( 0.0f );
		velocityOverLifetime.y = new ParticleSystem.MinMaxCurve( 2.0f );
		velocityOverLifetime.z = new ParticleSystem.MinMaxCurve( 0.0f );

		// configure color over lifetime - fade out
		var colorOverLifetime = m_particleSystem.colorOverLifetime;
		colorOverLifetime.enabled = true;

		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] {
				new GradientColorKey( new Color( 0.7f, 0.85f, 1.0f ), 0.0f ),
				new GradientColorKey( new Color( 1.0f, 1.0f, 1.0f ), 0.5f ),
				new GradientColorKey( new Color( 0.7f, 0.85f, 1.0f ), 1.0f )
			},
			new GradientAlphaKey[] {
				new GradientAlphaKey( 1.0f, 0.0f ),
				new GradientAlphaKey( 1.0f, 0.5f ),
				new GradientAlphaKey( 0.0f, 1.0f )
			}
		);
		colorOverLifetime.color = new ParticleSystem.MinMaxGradient( gradient );

		// configure size over lifetime - shrink
		var sizeOverLifetime = m_particleSystem.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, new AnimationCurve(
			new Keyframe( 0, 1 ),
			new Keyframe( 1, 0 )
		) );

		// configure renderer
		var particleRenderer = particleObj.GetComponent<ParticleSystemRenderer>();

		// try to find a particle shader, fall back to alternatives if not found
		var shader = Shader.Find( "Particles/Standard Unlit" );
		if ( shader == null )
		{
			shader = Shader.Find( "Legacy Shaders/Particles/Additive" );
		}
		if ( shader == null )
		{
			shader = Shader.Find( "Universal Render Pipeline/Particles/Unlit" );
		}
		if ( shader == null )
		{
			shader = Shader.Find( "Unlit/Color" );
		}

		if ( shader != null )
		{
			particleRenderer.material = new Material( shader );
			particleRenderer.material.color = new Color( 0.7f, 0.85f, 1.0f, 1.0f );
		}

		// start playing
		m_particleSystem.Play();
	}

	void Update()
	{
		if ( !m_isPlaying )
		{
			return;
		}

		m_effectTimer += Time.deltaTime;
		float progress = Mathf.Clamp01( m_effectTimer / c_effectDuration );

		// scale down the object as it dematerializes (this works with any shader)
		// use smooth easing curve for nicer shrink effect
		float scaleProgress = progress * progress; // quadratic ease-in for smoother shrink
		float scaleMultiplier = 1.0f - scaleProgress;
		transform.localScale = m_originalScale * scaleMultiplier;

		// hide renderers at the very end
		if ( progress > 0.95f )
		{
			for ( int i = 0; i < m_renderers.Count; i++ )
			{
				if ( m_renderers[ i ] != null )
				{
					m_renderers[ i ].enabled = false;
				}
			}
		}

		// check if effect is complete
		if ( progress >= 1.0f )
		{
			CompleteEffect();
		}
	}

	void CompleteEffect()
	{
		m_isPlaying = false;

		// invoke callback
		if ( m_onComplete != null )
		{
			m_onComplete.Invoke();
		}

		// destroy the object
		Destroy( gameObject );
	}

	void OnDestroy()
	{
		// clean up prefab instance if it exists
		if ( m_particlePrefabInstance != null )
		{
			Destroy( m_particlePrefabInstance );
		}
		// clean up programmatically created particle system if it exists
		else if ( m_particleSystem != null )
		{
			Destroy( m_particleSystem.gameObject );
		}
	}
}
