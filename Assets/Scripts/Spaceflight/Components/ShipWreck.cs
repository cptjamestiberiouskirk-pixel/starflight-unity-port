
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The wreck of a destroyed ship: drawn with the materials of the ship it was (the debris models are the ship's own meshes with faces knocked out,
/// but they were imported with blank white materials), scorched dark, its lights out, with an ember glow that cools to a smoulder.
/// </summary>
public class ShipWreck : MonoBehaviour
{
	// how dark the scorched hull is next to the ship's own colour
	const float c_scorchedShade = 0.45f;

	// the ember glow when the ship has just blown up, how long it takes to cool, and the smoulder it cools to (which keeps a wreck in the dark of hyperspace in sight)
	static readonly Color c_emberColor = new Color( 1.0f, 0.35f, 0.08f, 1.0f );
	const float c_emberStrength = 0.6f;
	const float c_coolingTime = 6.0f;
	const float c_smoulder = 0.12f;

	// the properties of the ship shader (SF - Standard) the wreck sets for each of its materials (the emissive tint switches off the ship's lights, which its emissive map draws)
	static readonly int m_albedoColorId = Shader.PropertyToID( "SF_AlbedoColor" );
	static readonly int m_emissiveColorId = Shader.PropertyToID( "SF_EmissiveColor" );
	static readonly int m_emissiveTintId = Shader.PropertyToID( "SF_EmissiveTint" );

	// the wreck's renderers, and for each of their materials its scorched colour (or nothing for a material without the ship shader's colours)
	List<Renderer> m_renderers = new List<Renderer>();
	List<Color[]> m_scorchedColors = new List<Color[]>();

	// one block for every material, filled again as the embers cool
	MaterialPropertyBlock m_propertyBlock;

	// how long the wreck has been cooling
	float m_timer;
	bool m_isCooling;

	/// <summary>
	/// The materials of a ship's renderers, by renderer name, to be given to its wreck (gather them before the ship's model is removed).
	/// </summary>
	public static Dictionary<string, Material[]> GatherShipMaterials( GameObject ship )
	{
		var materials = new Dictionary<string, Material[]>();

		if ( ship == null )
		{
			return materials;
		}

		foreach ( var renderer in ship.GetComponentsInChildren<Renderer>( true ) )
		{
			// only the hull: not the particles, lines or trails of an engine or an effect
			if ( ( renderer is ParticleSystemRenderer ) || ( renderer is LineRenderer ) || ( renderer is TrailRenderer ) )
			{
				continue;
			}

			// the debris model's renderers are named after the model, without the (Clone) of an instance
			var name = renderer.name.Replace( "(Clone)", "" ).Trim();

			if ( !materials.ContainsKey( name ) )
			{
				materials.Add( name, renderer.sharedMaterials );
			}
		}

		return materials;
	}

	/// <summary>
	/// Make a wreck of the debris model: give it the materials of the ship it was, scorched, and start its embers cooling.
	/// </summary>
	public static ShipWreck MakeWreck( GameObject debris, Dictionary<string, Material[]> shipMaterials )
	{
		var wreck = debris.GetComponent<ShipWreck>();

		if ( wreck == null )
		{
			wreck = debris.AddComponent<ShipWreck>();
		}

		wreck.Initialize( shipMaterials );

		return wreck;
	}

	// give the debris the ship's materials and work out their scorched colours
	void Initialize( Dictionary<string, Material[]> shipMaterials )
	{
		m_renderers.Clear();
		m_scorchedColors.Clear();

		if ( m_propertyBlock == null )
		{
			m_propertyBlock = new MaterialPropertyBlock();
		}

		// the material for a debris renderer that has no namesake in the ship (the first of the ship's)
		Material[] fallback = null;

		foreach ( var pair in shipMaterials )
		{
			if ( ( pair.Value != null ) && ( pair.Value.Length > 0 ) && ( pair.Value[ 0 ] != null ) )
			{
				fallback = pair.Value;
				break;
			}
		}

		foreach ( var renderer in GetComponentsInChildren<Renderer>( true ) )
		{
			if ( ( renderer is ParticleSystemRenderer ) || ( renderer is LineRenderer ) || ( renderer is TrailRenderer ) )
			{
				continue;
			}

			var name = renderer.name.Replace( "(Clone)", "" ).Trim();

			Material[] source;

			if ( !shipMaterials.TryGetValue( name, out source ) || ( source == null ) || ( source.Length == 0 ) )
			{
				source = fallback;
			}

			// nothing to give it: it keeps what it was imported with
			if ( source == null )
			{
				continue;
			}

			// one material for each of the debris mesh's material slots (the debris meshes are the ship's, so the slots match; a longer list repeats its last)
			var slots = Mathf.Max( 1, renderer.sharedMaterials.Length );
			var materials = new Material[ slots ];

			for ( var i = 0; i < slots; i++ )
			{
				materials[ i ] = source[ Mathf.Min( i, source.Length - 1 ) ];
			}

			renderer.sharedMaterials = materials;

			// the scorched colour of each material (a clear colour for a material without the ship shader's albedo colour, which is left alone)
			var scorched = new Color[ slots ];

			for ( var i = 0; i < slots; i++ )
			{
				var material = materials[ i ];

				if ( ( material != null ) && material.HasProperty( m_albedoColorId ) )
				{
					var color = material.GetColor( m_albedoColorId );

					scorched[ i ] = new Color( color.r * c_scorchedShade, color.g * c_scorchedShade * 0.92f, color.b * c_scorchedShade * 0.85f, color.a );
				}
				else
				{
					scorched[ i ] = Color.clear;
				}
			}

			m_renderers.Add( renderer );
			m_scorchedColors.Add( scorched );
		}

		m_timer = 0.0f;
		m_isCooling = true;

		ApplyColors( 1.0f );
	}

	// unity update
	void Update()
	{
		if ( !m_isCooling )
		{
			return;
		}

		m_timer += Time.deltaTime;

		// the embers cool quickly at first, then slowly, to a smoulder
		var heat = 1.0f - Mathf.Clamp01( m_timer / c_coolingTime );

		ApplyColors( Mathf.Lerp( c_smoulder, 1.0f, heat * heat ) );

		// cooled: the scorched colour and the smoulder stay
		if ( m_timer >= c_coolingTime )
		{
			m_isCooling = false;
		}
	}

	// set the scorched colour and the ember glow of every material of the wreck
	void ApplyColors( float heat )
	{
		var ember = c_emberColor * ( c_emberStrength * heat );

		ember.a = 1.0f;

		for ( var r = 0; r < m_renderers.Count; r++ )
		{
			var renderer = m_renderers[ r ];

			if ( renderer == null )
			{
				continue;
			}

			var scorched = m_scorchedColors[ r ];

			for ( var i = 0; i < scorched.Length; i++ )
			{
				// a material without the ship shader's colours is left as it is
				if ( scorched[ i ].a <= 0.0f )
				{
					continue;
				}

				m_propertyBlock.Clear();
				m_propertyBlock.SetColor( m_albedoColorId, scorched[ i ] );
				m_propertyBlock.SetColor( m_emissiveTintId, Color.black );
				m_propertyBlock.SetColor( m_emissiveColorId, ember );

				renderer.SetPropertyBlock( m_propertyBlock, i );
			}
		}
	}
}
