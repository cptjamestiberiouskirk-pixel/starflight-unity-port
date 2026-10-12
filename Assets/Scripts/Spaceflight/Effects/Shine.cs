
using UnityEngine;

public class Shine : MonoBehaviour
{
	const int c_numVertices = 256;

	// now and then a small flare rises from the rim and falls back (the look of the sun only: the flares of the game are
	// StarSystem's, and the size of the shine is still what SetSize gives)
	const float c_flareDuration = 2.5f;
	const float c_flareShortestWait = 6.0f;
	const float c_flareLongestWait = 14.0f;
	const float c_firstFlareWait = 3.0f;

	// how high a flare rises (a fraction of the size of the shine) and how wide it is (radians)
	const float c_flareHeight = 0.22f;
	const float c_flareWidth = 0.55f;

	static readonly int c_flareId = Shader.PropertyToID( "_Flare" );

	public float m_speed = 1.0f;

	Vector3[] m_positions;
	Vector2[] m_uv;
	int[] m_triangles;

	Mesh m_mesh;
	MeshFilter m_meshFilter;
	MeshRenderer m_meshRenderer;
	Material m_material;

	float m_lerpTime;

	// random numbers of its own, so that the look of the sun takes none from the game's Random
	System.Random m_random = new System.Random( 1986 );

	// the flare: how long the current one has been going, when the next one comes, where it is and how strong
	float m_flareTime;
	float m_nextFlare;
	float m_flareAngle;
	float m_flareStrength;

	// the smallest size of the shine (SetSize)
	float m_minimumSize;

	// unity awake
	void Awake()
	{
		m_mesh = new Mesh();
		m_mesh.MarkDynamic();

		m_meshFilter = GetComponent<MeshFilter>();
		m_meshFilter.mesh = m_mesh;

		m_meshRenderer = GetComponent<MeshRenderer>();
		m_material = m_meshRenderer.material;

		m_positions = new Vector3[ c_numVertices ];
		m_uv = new Vector2[ c_numVertices ];
		m_triangles = new int[ ( c_numVertices - 1 ) * 3 ];

		InitializeMesh();

		m_mesh.vertices = m_positions;
		m_mesh.uv = m_uv;
		m_mesh.triangles = m_triangles;

		SetColor( new Color( 1.0f, 1.0f, 1.0f ) );
		SetSize( 5.0f, 10.0f );
	}

	// unity start
	void Start()
	{
		m_lerpTime = 0.0f;

		// the first flare comes soon
		m_flareTime = 0.0f;
		m_nextFlare = c_firstFlareWait;
		m_flareStrength = 0.0f;
	}

	// unity update
	void Update()
	{
		m_lerpTime += Time.deltaTime * m_speed;

		if ( m_lerpTime >= Mathf.PI * 2.0f )
		{
			m_lerpTime -= Mathf.PI * 2.0f;
		}

		m_material.SetFloat( "_LerpTime", m_lerpTime );

		// is it time for the next flare?
		m_flareTime += Time.deltaTime;

		if ( m_flareTime >= m_nextFlare )
		{
			m_flareTime = 0.0f;
			m_flareAngle = (float) m_random.NextDouble() * Mathf.PI * 2.0f;
			m_flareStrength = 0.6f + 0.4f * (float) m_random.NextDouble();
			m_nextFlare = c_flareDuration + Mathf.Lerp( c_flareShortestWait, c_flareLongestWait, (float) m_random.NextDouble() );
		}

		// the flare rises and falls back
		var rise = ( m_flareTime < c_flareDuration ) ? Mathf.Sin( Mathf.PI * m_flareTime / c_flareDuration ) : 0.0f;
		var strength = m_flareStrength * rise * rise;

		m_material.SetVector( c_flareId, new Vector4( m_flareAngle, c_flareHeight * m_minimumSize * strength, c_flareWidth, strength ) );
	}

	void InitializeMesh()
	{
		m_positions[ 0 ] = new Vector3( 0.0f, 0.0f, 0.0f );
		m_uv[ 0 ] = new Vector2( 0.0f, 0.0f );

		for ( int i = 1; i < c_numVertices - 1; i++ )
		{
			int offset = ( i - 1 ) % ( c_numVertices - 2 );

			float angle = ( ( Mathf.PI * 2.0f ) / (float) ( c_numVertices - 2 ) ) * (float) offset;

			m_positions[ i ] = new Vector3( angle, Random.Range( 0.0f, Mathf.PI * 2.0f ), 1.0f );
			m_uv[ i ] = new Vector2( 1.0f, 0.0f );
		}

		m_positions[ c_numVertices - 1 ] = m_positions[ 1 ];
		m_uv[ c_numVertices - 1 ] = m_uv[ 1 ];

		int vertexIndex = 0;

		for ( int i = 1; i < c_numVertices; i++ )
		{
			m_triangles[ vertexIndex++ ] = 0;
			m_triangles[ vertexIndex++ ] = i;
			m_triangles[ vertexIndex++ ] = ( i + 1 ) % c_numVertices;
		}
	}

	public void SetColor( Color newColor )
	{
		m_material.SetColor( "_Color", newColor );
	}

	public void SetSize( float newMinSize, float newMaxSize )
	{
		m_minimumSize = newMinSize;

		m_material.SetVector( "_Size", new Vector4( newMinSize, newMaxSize - newMinSize, 0.0f, 0.0f ) );

		m_mesh.bounds = new Bounds( new Vector3( 0.0f, 0.0f, 0.0f ), new Vector3( newMaxSize * 2.0f, newMaxSize * 2.0f, 0.0f ) );
	}
}
