using UnityEngine;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public class InfiniteStarfield : MonoBehaviour
{
	[System.Serializable]
	public struct Starfield
	{
		public Material m_mat;
		public Color m_color;
		public int m_maxParticles;
		public float m_startSize;
		public float m_height;
		public float m_heightBounds;
		public float m_2dBounds;
	}

	[SerializeField] bool m_yIsUp;
	[SerializeField] Starfield[] m_starfields;
	
	ParticleSystem[] m_particleSystems;
	ParticleSystem.Particle[][] m_particles;
	Vector3 m_lastPosition;

	// the materials made for the layers (one each), destroyed when the starfield is made again or goes
	List<Material> m_starMaterials = new List<Material>();

	// the star shader (in a Resources folder, so it is in every build: the materials are made in code)
	static Shader m_starShader;

	// the faintest star next to the brightest, and how much bigger the brightest are than the smallest
	const float c_faintest = 0.16f;
	const float c_smallest = 0.6f;
	const float c_largest = 1.5f;

	// the share of stars bright enough for a glint, and how much bigger their quad is (the glint reaches out of the halo)
	const float c_glintShare = 0.035f;
	const float c_glintRoom = 2.0f;

	// how much bigger the quad of a star is than the star sprite was: the core is drawn in its middle, with room for the halo
	const float c_quadRoom = 1.5f;

	// how much of the layer's tint each star takes (the rest is the star's own colour)
	const float c_layerTint = 0.5f;

	// the colours of stars, from blue white to orange, and how often each is picked (they add up to 1)
	static readonly Color[] c_starColors = new Color[]
	{
		new Color( 0.72f, 0.82f, 1.0f ),
		new Color( 0.9f, 0.94f, 1.0f ),
		new Color( 1.0f, 1.0f, 1.0f ),
		new Color( 1.0f, 0.95f, 0.84f ),
		new Color( 1.0f, 0.85f, 0.62f ),
		new Color( 1.0f, 0.7f, 0.45f )
	};
	static readonly float[] c_starColorShares = new float[] { 0.15f, 0.2f, 0.3f, 0.18f, 0.11f, 0.06f };

	void Start()
	{
		InitStarField();
	}

	public void InitFromUnityButton()
	{
		InitStarField();
	}
	
	public void InitStarField(params Starfield[] starfields)
	{
		foreach ( Transform child in transform )
			Destroy( child.gameObject );

		// the materials of the starfield this one replaces
		foreach ( var material in m_starMaterials )
		{
			if ( material != null )
			{
				Destroy( material );
			}
		}

		m_starMaterials.Clear();

		if ( m_starShader == null )
		{
			m_starShader = Resources.Load<Shader>( "Shaders/Starfield Star" );
		}

		if ( starfields.Length > 0 )
			m_starfields = starfields;

		var cameraPos = transform.position;
		var aspect = Camera.main.aspect;
		
		int count = m_starfields.Length;
		m_particleSystems = new ParticleSystem[ count ];
		m_particles = new ParticleSystem.Particle[ count ][];

		for ( int i = 0; i < count; i++ )
		{
			var starfield = m_starfields[ i ];
			var psHolder = new GameObject($"starfield {i}");
			psHolder.transform.SetParent(transform, false);
			var ps = m_particleSystems[ i ] = psHolder.AddComponent<ParticleSystem>();
			var emission = ps.emission;
			emission.enabled = false;
			var shape = ps.shape;
			shape.enabled = false;
			var main = ps.main;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
			main.playOnAwake = false;
			main.loop = false;
			main.maxParticles = starfield.m_maxParticles;
			main.startSize = starfield.m_startSize;
			main.startColor = starfield.m_color;
			var partRend = ps.GetComponent<ParticleSystemRenderer>();

			if ( m_starShader != null )
			{
				// each star drawn by the star shader: a core, a halo, a glint for a bright one, and a twinkle (which needs the particle's stable random numbers)
				var material = new Material( m_starShader );
				material.name = "Starfield Star " + i;
				m_starMaterials.Add( material );

				partRend.sharedMaterial = material;
				partRend.SetActiveVertexStreams( new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomXY } );
			}
			else
			{
				// the layer's own sprite material, as before
				partRend.material = starfield.m_mat;
			}

			// the size, brightness, colour and glint of each star come from a random sequence of their own (the game's random numbers are drawn exactly as before)
			var starRandom = new System.Random( 1986 + i * 7919 );

			Vector3 startingPosition, bounds;
			if (m_yIsUp)
			{
				startingPosition = new Vector3( cameraPos.x, starfield.m_height, cameraPos.z );
				bounds = new Vector3( starfield.m_2dBounds * aspect, starfield.m_heightBounds, starfield.m_2dBounds );
			}
			else
			{
				startingPosition = new Vector3( cameraPos.x, cameraPos.y, starfield.m_height );
				bounds = new Vector3( starfield.m_2dBounds * aspect, starfield.m_2dBounds, starfield.m_heightBounds );
			}
			
			m_particles[ i ] = new ParticleSystem.Particle[ ( int )( starfield.m_maxParticles * aspect ) ];
			for ( var j = 0; j < m_particles[ i ].Length; j++ )
			{
				m_particles[ i ][ j ].position = startingPosition + Vector3.Scale( new Vector3( Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f ), bounds );
				m_particles[ i ][ j ].startColor = main.startColor.Evaluate( 0.0f );
				m_particles[ i ][ j ].startSize = main.startSize.Evaluate( 0.0f );
				m_particles[ i ][ j ].rotation = Random.value * 360f;
				m_particles[ i ][ j ].startLifetime = m_particles[ i ][ j ].remainingLifetime = float.MaxValue;

				if ( m_starShader != null )
				{
					MakeStar( ref m_particles[ i ][ j ], starfield, starRandom );
				}
			}
			
			ps.SetParticles( m_particles[ i ],  m_particles[ i ].Length );
		}
	}
	
	// give a star its own size, brightness, colour and glint: many faint stars and few bright ones, as in a real sky
	static void MakeStar( ref ParticleSystem.Particle particle, Starfield starfield, System.Random starRandom )
	{
		var magnitude = ( float ) starRandom.NextDouble();

		// brightness and size rise steeply towards the brightest few
		var brightness = Mathf.Lerp( c_faintest, 1.0f, Mathf.Pow( magnitude, 3.0f ) );
		var size = Mathf.Lerp( c_smallest, c_largest, Mathf.Pow( magnitude, 2.0f ) );

		// the brightest few have a glint, stronger the brighter they are, and a bigger quad for it
		var glint = 0.0f;

		if ( magnitude > 1.0f - c_glintShare )
		{
			glint = Mathf.Clamp01( ( magnitude - ( 1.0f - c_glintShare ) ) / c_glintShare * 0.7f + 0.3f );
			size *= c_glintRoom;
		}

		// the star's own colour, tinted by its layer
		var pick = ( float ) starRandom.NextDouble();
		var color = c_starColors[ c_starColors.Length - 1 ];

		for ( var k = 0; k < c_starColors.Length; k++ )
		{
			if ( pick < c_starColorShares[ k ] )
			{
				color = c_starColors[ k ];
				break;
			}

			pick -= c_starColorShares[ k ];
		}

		color = color * Color.Lerp( Color.white, starfield.m_color, c_layerTint ) * brightness;
		color.a = glint;

		particle.startColor = color;
		particle.startSize = starfield.m_startSize * size * c_quadRoom;

		// a glint lies along the screen's axes, so the star is not turned
		particle.rotation = 0.0f;

		// its stable random numbers, which the shader uses for its twinkle
		particle.randomSeed = ( uint ) starRandom.Next();
	}

	void OnDestroy()
	{
		foreach ( var material in m_starMaterials )
		{
			if ( material != null )
			{
				Destroy( material );
			}
		}

		m_starMaterials.Clear();
	}

	void LateUpdate()
	{
		if ( m_lastPosition != transform.position )
		{
			var aspect = Camera.main.aspect;
			var cameraPos = transform.position;
			m_lastPosition = cameraPos;

			for ( int i = 0; i < m_particleSystems.Length; i++ )
			{
				var starfield = m_starfields[ i ];
				var ps = m_particleSystems[ i ];

				Vector3 startingPosition, bounds;
				if (m_yIsUp)
				{
					startingPosition = new Vector3( cameraPos.x, starfield.m_height, cameraPos.z );
					bounds = new Vector3( starfield.m_2dBounds * aspect, starfield.m_heightBounds, starfield.m_2dBounds );
				}
				else
				{
					startingPosition = new Vector3( cameraPos.x, cameraPos.y, starfield.m_height );
					bounds = new Vector3( starfield.m_2dBounds * aspect, starfield.m_2dBounds, starfield.m_heightBounds );
				}

				var minCorner = startingPosition + Vector3.Scale( new Vector3( -0.5f, -0.5f, -0.5f ), bounds );
				var maxCorner = startingPosition + Vector3.Scale( new Vector3( 0.5f, 0.5f, 0.5f ), bounds );
				
				for ( var j = 0; j < m_particles[ i ].Length; j++ )
				{
					var position = m_particles[ i ][ j ].position;

					if ( position.x < minCorner.x )
						position.x += bounds.x;
					else if ( position.x > maxCorner.x )
						position.x -= bounds.x;

					if ( !m_yIsUp )
					{
						if ( position.y < minCorner.y )
							position.y += bounds.y;
						else if ( position.y > maxCorner.y )
							position.y -= bounds.y;
					}
					else
					{
						if ( position.z < minCorner.z )
							position.z += bounds.z;
						else if ( position.z > maxCorner.z )
							position.z -= bounds.z;
					}
					
					m_particles[ i ][ j ].position = position;
				}
				
				ps.SetParticles( m_particles[ i ], m_particles[ i ].Length );
			}
		}
	}
}
