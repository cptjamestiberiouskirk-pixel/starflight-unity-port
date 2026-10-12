
using UnityEngine;
using System.Collections.Generic;

// This component creates a Star Trek-style transporter dematerialization effect.
// The object shrinks away in a shimmer of blue light: sparkles rising through it, a pad of light on the ground under it, and a soft blue glow on what is around it.
// It is drawn with the materials the combat effects share (EffectMaterials), which are made in code and so are in every build.
public class TransporterEffect : MonoBehaviour
{
	// duration of the effect in seconds
	const float c_effectDuration = 1.5f;

	// how many sparkles rise each second, and for how much of the effect they keep coming
	const int c_sparklesPerSecond = 150;
	const float c_sparkleTime = 1.25f;

	// the smallest size the effect is made for (a pebble still gets a shimmer that can be seen)
	const float c_minimumSize = 0.5f;

	// how big the pad of light on the ground under the object is (the terrain camera looks down on it)
	const float c_padSize = 3.0f;

	// how bright the light gets and how far it reaches (as a share of the object's size)
	const float c_lightIntensity = 2.0f;
	const float c_lightRange = 6.0f;

	// the colour of the transporter (the pale blue of the docking bay's)
	static readonly Color c_transporterColor = new Color( 0.6f, 0.82f, 1.0f, 1.0f );

	// effect state
	float m_effectTimer;
	bool m_isPlaying;

	// the renderers of the object (hidden at the very end)
	List<Renderer> m_renderers = new List<Renderer>();

	// original scale for shrink effect
	Vector3 m_originalScale;

	// the shimmer: its own object in the world, so it does not shrink with the object and can fade out after the object is gone
	GameObject m_effectObject;
	ParticleSystem m_particleSystem;
	ParticleSystem m_padParticles;
	Light m_light;

	// how big the object is across
	float m_size;

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

		// gather all renderers in this object and children (not the ones of an effect)
		m_renderers.Clear();

		foreach ( var renderer in GetComponentsInChildren<Renderer>() )
		{
			if ( ( renderer is ParticleSystemRenderer ) || ( renderer is LineRenderer ) || ( renderer is TrailRenderer ) )
			{
				continue;
			}

			m_renderers.Add( renderer );
		}

		// how big the object is, and where its middle is
		var bounds = new Bounds( transform.position, Vector3.zero );

		if ( m_renderers.Count > 0 )
		{
			bounds = m_renderers[ 0 ].bounds;

			foreach ( var renderer in m_renderers )
			{
				bounds.Encapsulate( renderer.bounds );
			}
		}

		m_size = Mathf.Max( c_minimumSize, Mathf.Max( bounds.size.x, bounds.size.z ) );

		CreateShimmer( bounds );
	}

	// create the sparkles, the pad of light and the glow
	void CreateShimmer( Bounds bounds )
	{
		EffectMaterials.Initialize();

		m_effectObject = new GameObject( "Transporter Shimmer" );
		m_effectObject.transform.position = bounds.center;

		// the ground under the object (a quarter of the way up it: an object on uneven ground is partly sunk into it, and a pad at its very bottom would be under the surface)
		var groundPoint = new Vector3( bounds.center.x, bounds.min.y + bounds.size.y * 0.25f, bounds.center.z );

		// the sparkles: rising through the object and twinkling
		m_particleSystem = EffectMaterials.CreateParticleSystem( m_effectObject.transform, "Sparkles", EffectMaterials.m_glowMaterial, 2, 300 );

		var main = m_particleSystem.main;
		main.duration = c_sparkleTime;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.35f, 0.75f );
		main.startSpeed = 0.0f;
		main.startSize = new ParticleSystem.MinMaxCurve( m_size * 0.1f, m_size * 0.25f );
		main.startColor = c_transporterColor;

		var emission = m_particleSystem.emission;
		emission.rateOverTime = c_sparklesPerSecond;

		// from the whole of the object
		var shape = m_particleSystem.shape;
		shape.shapeType = ParticleSystemShapeType.Box;
		shape.scale = new Vector3( Mathf.Max( bounds.size.x, c_minimumSize ), Mathf.Max( bounds.size.y, c_minimumSize ), Mathf.Max( bounds.size.z, c_minimumSize ) ) * 1.1f;

		// rising (all three axes in the same mode, two constants: Unity logs "Particle Velocity curves must all be in the same mode" every frame otherwise, and does not use them)
		var velocityOverLifetime = m_particleSystem.velocityOverLifetime;
		velocityOverLifetime.enabled = true;
		velocityOverLifetime.x = new ParticleSystem.MinMaxCurve( 0.0f, 0.0f );
		velocityOverLifetime.y = new ParticleSystem.MinMaxCurve( m_size * 0.6f, m_size * 1.4f );
		velocityOverLifetime.z = new ParticleSystem.MinMaxCurve( 0.0f, 0.0f );

		// twinkling: each sparkle flares up, dims and flares again before it goes out
		var sizeOverLifetime = m_particleSystem.sizeOverLifetime;
		sizeOverLifetime.enabled = true;
		sizeOverLifetime.size = new ParticleSystem.MinMaxCurve( 1.0f, new AnimationCurve(
			new Keyframe( 0.0f, 0.0f ),
			new Keyframe( 0.2f, 1.0f ),
			new Keyframe( 0.45f, 0.35f ),
			new Keyframe( 0.7f, 0.9f ),
			new Keyframe( 1.0f, 0.0f )
		) );

		var colorOverLifetime = m_particleSystem.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( Color.white, new Color( 0.85f, 0.93f, 1.0f ), c_transporterColor, 1.0f, 1.0f, 0.0f );

		m_particleSystem.Play();

		// the pad of light on the ground under the object: it lies flat, so the terrain camera, which looks down on it, sees all of it
		m_padParticles = EffectMaterials.CreateParticleSystem( m_effectObject.transform, "Pad", EffectMaterials.m_glowMaterial, 0, 1 );
		m_padParticles.transform.position = groundPoint;

		var padMain = m_padParticles.main;
		padMain.startLifetime = c_effectDuration;
		padMain.startSpeed = 0.0f;
		padMain.startSize = m_size * c_padSize;

		// pale blue, brighter than the sparkles
		padMain.startColor = new Color( 0.75f, 0.9f, 1.0f, 1.0f );

		var padEmission = m_padParticles.emission;
		padEmission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var padShape = m_padParticles.shape;
		padShape.enabled = false;

		// it swells as it comes up and fades as the object goes
		var padSize = m_padParticles.sizeOverLifetime;
		padSize.enabled = true;
		padSize.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 0.5f, 1.0f, 1.0f ) );

		var padColour = m_padParticles.colorOverLifetime;
		padColour.enabled = true;
		var padGradient = new Gradient();
		padGradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( Color.white, 0.0f ), new GradientColorKey( c_transporterColor, 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 0.0f, 0.0f ), new GradientAlphaKey( 0.85f, 0.15f ), new GradientAlphaKey( 0.6f, 0.7f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		padColour.color = padGradient;

		var padRenderer = m_padParticles.GetComponent<ParticleSystemRenderer>();
		padRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;

		m_padParticles.Play();

		// the glow it casts on the ground and on what is around it
		m_light = m_effectObject.AddComponent<Light>();
		m_light.type = LightType.Point;
		m_light.color = c_transporterColor;
		m_light.range = m_size * c_lightRange;
		m_light.intensity = 0.0f;
		m_light.shadows = LightShadows.None;
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

		// the glow comes up quickly, shimmers, and dies away as the object goes
		var strength = Mathf.Clamp01( progress / 0.15f ) * Mathf.Clamp01( ( 1.0f - progress ) / 0.3f );
		var shimmer = 0.85f + 0.15f * Mathf.Sin( m_effectTimer * 40.0f );

		if ( m_light != null )
		{
			m_light.intensity = c_lightIntensity * strength * shimmer;
		}

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
		// the shimmer is not part of the object: its last sparkles fade out on their own, then it goes too
		if ( m_effectObject != null )
		{
			if ( m_particleSystem != null )
			{
				m_particleSystem.Stop( true, ParticleSystemStopBehavior.StopEmitting );
			}

			if ( m_light != null )
			{
				m_light.enabled = false;
			}

			Destroy( m_effectObject, 1.0f );
		}
	}
}
