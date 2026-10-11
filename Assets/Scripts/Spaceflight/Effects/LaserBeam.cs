
using UnityEngine;

/// <summary>
/// Visual effect for laser beam weapons: a soft glow in the laser's colour around a white hot core, a flash where it leaves
/// the gun, and a flare with sparks where it strikes.
/// </summary>
public class LaserBeam : MonoBehaviour
{
	// the line renderer component (the glow)
	LineRenderer m_lineRenderer;

	// the white hot core inside the glow
	LineRenderer m_coreRenderer;

	// optional custom material for the glow (can be set in editor or loaded at runtime)
	public Material m_customMaterial;

	// the flash at the gun, and the flare and the sparks where the beam strikes
	ParticleSystem m_muzzleParticles;
	ParticleSystem m_impactParticles;
	ParticleSystem m_sparkParticles;

	// start and end points
	Vector3 m_startPoint;
	Vector3 m_endPoint;

	// beam lifetime
	float m_lifetime;
	float m_timer;

	// beam colors
	Color m_startColor = new Color( 1.0f, 0.2f, 0.2f, 1.0f ); // red laser

	// beam width (the glow; the core is a share of it)
	float m_startWidth = 9.0f;
	float m_endWidth = 7.0f;

	// how wide the core is next to the glow, and how much of the way to white its colour is
	const float c_coreWidth = 0.22f;
	const float c_coreWhiteness = 0.75f;

	// how fast and how much the beam flickers while it fades
	const float c_flickerSpeed = 90.0f;
	const float c_flickerAmount = 0.15f;

	// unity awake
	void Awake()
	{
		EffectMaterials.Initialize();

		// create the line renderer
		m_lineRenderer = gameObject.AddComponent<LineRenderer>();
		SetUpLine( m_lineRenderer, ( m_customMaterial != null ) ? m_customMaterial : EffectMaterials.m_beamMaterial, 0 );

		// and the core, drawn over the glow
		var coreObject = new GameObject( "Core" );
		coreObject.transform.SetParent( transform );
		m_coreRenderer = coreObject.AddComponent<LineRenderer>();
		SetUpLine( m_coreRenderer, EffectMaterials.m_beamMaterial, 1 );

		CreateMuzzleParticles();
		CreateImpactParticles();

		SetColor( m_startColor );

		// disable by default
		m_lineRenderer.enabled = false;
		m_coreRenderer.enabled = false;
	}

	// set up a line of the beam
	void SetUpLine( LineRenderer line, Material material, int sortingOrder )
	{
		line.positionCount = 2;
		line.useWorldSpace = true;
		line.sharedMaterial = material;
		line.textureMode = LineTextureMode.Stretch;
		line.numCapVertices = 4;
		line.sortingOrder = sortingOrder;
		line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		line.receiveShadows = false;
	}

	// create the flash at the gun
	void CreateMuzzleParticles()
	{
		var muzzleObject = new GameObject( "Muzzle" );
		muzzleObject.transform.SetParent( transform );

		m_muzzleParticles = EffectMaterials.CreateParticleSystem( muzzleObject.transform, "Flash", EffectMaterials.m_glowMaterial, 2, 1 );

		var main = m_muzzleParticles.main;
		main.startLifetime = 0.12f;
		main.startSpeed = 0.0f;
		main.startSize = 18.0f;

		var emission = m_muzzleParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var shape = m_muzzleParticles.shape;
		shape.enabled = false;

		var colorOverLifetime = m_muzzleParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( Color.white, Color.white, Color.white, 1.0f, 0.6f, 0.0f );
	}

	// create the flare and the sparks where the beam strikes
	void CreateImpactParticles()
	{
		var impactObject = new GameObject( "Impact" );
		impactObject.transform.SetParent( transform );

		// the flare
		m_impactParticles = EffectMaterials.CreateParticleSystem( impactObject.transform, "Flare", EffectMaterials.m_glowMaterial, 3, 1 );

		var flareMain = m_impactParticles.main;
		flareMain.startLifetime = 0.2f;
		flareMain.startSpeed = 0.0f;
		flareMain.startSize = 32.0f;

		var flareEmission = m_impactParticles.emission;
		flareEmission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 1 ) } );

		var flareShape = m_impactParticles.shape;
		flareShape.enabled = false;

		var flareSize = m_impactParticles.sizeOverLifetime;
		flareSize.enabled = true;
		flareSize.size = new ParticleSystem.MinMaxCurve( 1.0f, AnimationCurve.EaseInOut( 0.0f, 0.6f, 1.0f, 1.0f ) );

		var flareColour = m_impactParticles.colorOverLifetime;
		flareColour.enabled = true;
		flareColour.color = EffectMaterials.MakeGradient( Color.white, Color.white, Color.white, 1.0f, 0.6f, 0.0f );

		// the sparks, thrown back towards the gun
		m_sparkParticles = EffectMaterials.CreateParticleSystem( impactObject.transform, "Sparks", EffectMaterials.m_glowMaterial, 4, 16 );

		var main = m_sparkParticles.main;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.1f, 0.25f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 60.0f, 160.0f );
		main.startSize = new ParticleSystem.MinMaxCurve( 0.6f, 1.2f );

		var emission = m_sparkParticles.emission;
		emission.SetBursts( new ParticleSystem.Burst[] { new ParticleSystem.Burst( 0.0f, 10 ) } );

		var shape = m_sparkParticles.shape;
		shape.shapeType = ParticleSystemShapeType.Cone;
		shape.angle = 60.0f;
		shape.radius = 1.0f;

		var colorOverLifetime = m_sparkParticles.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = EffectMaterials.MakeGradient( Color.white, Color.white, Color.white, 1.0f, 1.0f, 0.0f );

		var renderer = m_sparkParticles.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Stretch;
		renderer.velocityScale = 0.06f;
		renderer.lengthScale = 1.5f;
	}

	// unity update
	void Update()
	{
		if ( m_timer > 0.0f )
		{
			m_timer -= Time.deltaTime;

			// fade out the beam, with a flicker
			float alpha = Mathf.Clamp01( m_timer / m_lifetime );
			float flicker = 1.0f - c_flickerAmount * ( 0.5f + 0.5f * Mathf.Sin( m_timer * c_flickerSpeed ) );
			float width = alpha * flicker;

			m_lineRenderer.startWidth = m_startWidth * width;
			m_lineRenderer.endWidth = m_endWidth * width;
			m_coreRenderer.startWidth = m_startWidth * c_coreWidth * width;
			m_coreRenderer.endWidth = m_endWidth * c_coreWidth * width;

			if ( m_timer <= 0.0f )
			{
				m_lineRenderer.enabled = false;
				m_coreRenderer.enabled = false;
			}
		}
	}

	/// <summary>
	/// Fire the laser beam from start to end point.
	/// </summary>
	public void Fire( Vector3 startPoint, Vector3 endPoint, float lifetime = 0.3f )
	{
		m_startPoint = startPoint;
		m_endPoint = endPoint;
		m_lifetime = Mathf.Max( lifetime, 0.01f );
		m_timer = m_lifetime;

		m_lineRenderer.SetPosition( 0, m_startPoint );
		m_lineRenderer.SetPosition( 1, m_endPoint );
		m_lineRenderer.startWidth = m_startWidth;
		m_lineRenderer.endWidth = m_endWidth;
		m_lineRenderer.enabled = true;

		m_coreRenderer.SetPosition( 0, m_startPoint );
		m_coreRenderer.SetPosition( 1, m_endPoint );
		m_coreRenderer.startWidth = m_startWidth * c_coreWidth;
		m_coreRenderer.endWidth = m_endWidth * c_coreWidth;
		m_coreRenderer.enabled = true;

		// the flash at the gun, the flare where it strikes, and the sparks thrown back along the beam
		m_muzzleParticles.transform.parent.position = m_startPoint;
		m_impactParticles.transform.parent.position = m_endPoint;

		var backAlongTheBeam = m_startPoint - m_endPoint;

		if ( backAlongTheBeam.sqrMagnitude > 0.01f )
		{
			m_impactParticles.transform.parent.rotation = Quaternion.LookRotation( backAlongTheBeam, Vector3.up );
		}

		m_muzzleParticles.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		m_muzzleParticles.Play();
		m_impactParticles.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		m_impactParticles.Play();
		m_sparkParticles.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		m_sparkParticles.Play();
	}

	/// <summary>
	/// Set the beam color (for different weapon classes).
	/// </summary>
	public void SetColor( Color color )
	{
		m_startColor = color;

		// the glow in the laser's colour, a little fainter at the far end
		m_lineRenderer.startColor = new Color( color.r, color.g, color.b, 1.0f );
		m_lineRenderer.endColor = new Color( color.r, color.g, color.b, 0.75f );

		// the core most of the way to white
		var coreColor = Color.Lerp( color, Color.white, c_coreWhiteness );
		m_coreRenderer.startColor = new Color( coreColor.r, coreColor.g, coreColor.b, 1.0f );
		m_coreRenderer.endColor = new Color( coreColor.r, coreColor.g, coreColor.b, 0.85f );

		// the flash, the flare and the sparks half way to white
		var flashColor = Color.Lerp( color, Color.white, 0.5f );
		var muzzleMain = m_muzzleParticles.main;
		muzzleMain.startColor = flashColor;
		var impactMain = m_impactParticles.main;
		impactMain.startColor = flashColor;
		var sparkMain = m_sparkParticles.main;
		sparkMain.startColor = flashColor;
	}

	/// <summary>
	/// Check if the beam is currently active.
	/// </summary>
	public bool IsActive()
	{
		return m_timer > 0.0f;
	}
}
