
using UnityEngine;

/// <summary>
/// Visual effect for laser beam weapons. Uses LineRenderer for the beam
/// with a glowing additive material.
/// </summary>
public class LaserBeam : MonoBehaviour
{
	// the line renderer component
	LineRenderer m_lineRenderer;

	// optional custom material (can be set in editor or loaded at runtime)
	public Material m_customMaterial;

	// start and end points
	Vector3 m_startPoint;
	Vector3 m_endPoint;

	// beam lifetime
	float m_lifetime;
	float m_timer;

	// beam colors
	Color m_startColor = new Color( 1.0f, 0.2f, 0.2f, 1.0f ); // red laser
	Color m_endColor = new Color( 1.0f, 0.8f, 0.2f, 0.0f );

	// beam width
	float m_startWidth = 2.0f;
	float m_endWidth = 0.5f;

	// unity awake
	void Awake()
	{
		// create the line renderer
		m_lineRenderer = gameObject.AddComponent<LineRenderer>();
		m_lineRenderer.positionCount = 2;
		m_lineRenderer.startWidth = m_startWidth;
		m_lineRenderer.endWidth = m_endWidth;
		m_lineRenderer.useWorldSpace = true;

		// use custom material if provided, otherwise create a default one
		if ( m_customMaterial != null )
		{
			m_lineRenderer.material = m_customMaterial;
		}
		else
		{
			// try to load the laser material from Resources
			var loadedMaterial = Resources.Load<Material>( "LaserRed_Mat" );
			if ( loadedMaterial != null )
			{
				m_lineRenderer.material = loadedMaterial;
			}
			else
			{
				// create a simple additive material as fallback
				var material = new Material( Shader.Find( "Particles/Standard Unlit" ) );
				material.SetFloat( "_Mode", 1 ); // additive
				material.SetColor( "_Color", Color.white );
				material.renderQueue = 3000;
				m_lineRenderer.material = material;
			}
		}

		// set gradient
		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( m_startColor, 0.0f ), new GradientColorKey( m_endColor, 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		m_lineRenderer.colorGradient = gradient;

		// disable by default
		m_lineRenderer.enabled = false;
	}

	// unity update
	void Update()
	{
		if ( m_timer > 0.0f )
		{
			m_timer -= Time.deltaTime;

			// fade out the beam
			float alpha = Mathf.Clamp01( m_timer / m_lifetime );
			m_lineRenderer.startWidth = m_startWidth * alpha;
			m_lineRenderer.endWidth = m_endWidth * alpha;

			if ( m_timer <= 0.0f )
			{
				m_lineRenderer.enabled = false;
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
		m_lifetime = lifetime;
		m_timer = lifetime;

		m_lineRenderer.SetPosition( 0, m_startPoint );
		m_lineRenderer.SetPosition( 1, m_endPoint );
		m_lineRenderer.startWidth = m_startWidth;
		m_lineRenderer.endWidth = m_endWidth;
		m_lineRenderer.enabled = true;
	}

	/// <summary>
	/// Set the beam color (for different weapon classes).
	/// </summary>
	public void SetColor( Color color )
	{
		m_startColor = color;
		m_endColor = new Color( color.r, color.g, color.b, 0.0f );

		var gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] { new GradientColorKey( m_startColor, 0.0f ), new GradientColorKey( m_endColor, 1.0f ) },
			new GradientAlphaKey[] { new GradientAlphaKey( 1.0f, 0.0f ), new GradientAlphaKey( 0.0f, 1.0f ) }
		);
		m_lineRenderer.colorGradient = gradient;
	}

	/// <summary>
	/// Check if the beam is currently active.
	/// </summary>
	public bool IsActive()
	{
		return m_timer > 0.0f;
	}
}
