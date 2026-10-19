
using UnityEngine;

public class Starmap : MonoBehaviour
{
	public float m_scrollVelocity;

	public GameObject m_starmap;
	public GameObject m_playerShip;

	public Vector3 m_originalPosition;

	Vector3 m_positionOffset;

	// what the viewport label shows while the starmap is open
	string m_label;

	// the colours given to the starmap's own parts (set per renderer, so that the materials themselves are left as they are)
	MaterialPropertyBlock m_propertyBlock;

	// the copies of the background material the starmap's background is given
	readonly System.Collections.Generic.List<Material> m_backgroundMaterials = new System.Collections.Generic.List<Material>();

	// the name of the starmap's background material (the ship's log is drawn on it too), and the name of the copies given in the scheme's colour
	const string c_backgroundMaterialName = "Starmap - Background";
	const string c_backgroundCopyName = c_backgroundMaterialName + " (Scheme)";

	// the colour of the starmap's materials (the SF - Standard shader's albedo colour)
	const string c_colourProperty = "SF_AlbedoColor";

	// the known fluxes, drawn as lines between their ends when the starmap opens (MAP-OV WEA70 and WEA4C: a grey line for every flux the ship has been through), in
	// a copy of the grid's material so that its shader is in every build
	const string c_gridMaterialName = "Starmap - Grid";
	const float c_fluxLineWidth = 0.75f;
	const float c_fluxLineDepth = 14.0f;

	GameObject m_knownFluxes;
	Mesh m_knownFluxMesh;
	Material m_knownFluxMaterial;

	// unity start
	void Start()
	{
	}

	// unity on enable
	void OnEnable()
	{
		// the starmap takes the colours of the kit's scheme, also when the player picks another one
		UIKitTheme.Changed -= UseSchemeColours;
		UIKitTheme.Changed += UseSchemeColours;

		UseSchemeColours();
	}

	// unity on disable
	void OnDisable()
	{
		UIKitTheme.Changed -= UseSchemeColours;
	}

	// unity on destroy
	void OnDestroy()
	{
		// the copies of the background material go with the scene
		DestroyBackgroundCopies( m_backgroundMaterials );

		// and so do the known fluxes' mesh and material
		if ( m_knownFluxMesh != null )
		{
			Destroy( m_knownFluxMesh );
		}

		if ( m_knownFluxMaterial != null )
		{
			Destroy( m_knownFluxMaterial );
		}
	}

	// draws the fluxes the ship has been through as lines between their ends (MAP-OV WEA70, WEA4C), in the starmap's own coordinates, where a place in hyperspace
	// is at ( -x, y ) as the ship's marker is
	void DrawKnownFluxes()
	{
		var fluxList = DataController.m_instance.m_gameData.m_fluxList;
		var known = DataController.m_instance.m_playerData.m_general.GetKnownFluxes();

		if ( m_knownFluxes == null )
		{
			// the grid's material, copied (null if the starmap has none: then nothing is drawn)
			Material gridMaterial = null;

			foreach ( var starmapRenderer in m_starmap.GetComponentsInChildren<Renderer>( true ) )
			{
				foreach ( var material in starmapRenderer.sharedMaterials )
				{
					if ( ( material != null ) && ( material.name == c_gridMaterialName ) )
					{
						gridMaterial = material;
					}
				}
			}

			if ( gridMaterial == null )
			{
				return;
			}

			m_knownFluxMaterial = new Material( gridMaterial ) { name = "Starmap - Known Fluxes" };
			m_knownFluxMesh = new Mesh { name = "Starmap - Known Fluxes" };

			m_knownFluxes = new GameObject( "Known Fluxes" );
			m_knownFluxes.transform.SetParent( m_starmap.transform, false );
			m_knownFluxes.layer = m_starmap.layer;
			m_knownFluxes.AddComponent<MeshFilter>().sharedMesh = m_knownFluxMesh;
			m_knownFluxes.AddComponent<MeshRenderer>().sharedMaterial = m_knownFluxMaterial;

			UseSchemeColours();
		}

		var vertices = new System.Collections.Generic.List<Vector3>();
		var triangles = new System.Collections.Generic.List<int>();

		foreach ( var flux in fluxList )
		{
			// each flux once (the list has it both ways), and only the known ones
			if ( !known.Contains( flux.m_id ) || ( ( flux.m_x1 > flux.m_x2 ) || ( ( flux.m_x1 == flux.m_x2 ) && ( flux.m_y1 > flux.m_y2 ) ) ) )
			{
				continue;
			}

			var from = new Vector3( -flux.m_x1, flux.m_y1, c_fluxLineDepth );
			var to = new Vector3( -flux.m_x2, flux.m_y2, c_fluxLineDepth );
			var along = to - from;

			if ( along.sqrMagnitude < 0.01f )
			{
				continue;
			}

			var across = new Vector3( -along.y, along.x, 0.0f ).normalized * ( c_fluxLineWidth * 0.5f );
			var first = vertices.Count;

			vertices.Add( from + across );
			vertices.Add( from - across );
			vertices.Add( to - across );
			vertices.Add( to + across );

			// both faces, so that the line shows whichever way the starmap faces the camera
			triangles.AddRange( new[] { first, first + 1, first + 2, first, first + 2, first + 3 } );
			triangles.AddRange( new[] { first, first + 2, first + 1, first, first + 3, first + 2 } );
		}

		m_knownFluxMesh.Clear();
		m_knownFluxMesh.SetVertices( vertices );
		m_knownFluxMesh.SetTriangles( triangles, 0 );
		m_knownFluxMesh.RecalculateBounds();

		m_knownFluxes.SetActive( vertices.Count > 0 );
	}

	// how many fluxes the starmap draws (for the probe)
	public int GetDrawnFluxCount()
	{
		return ( m_knownFluxMesh != null ) && ( m_knownFluxes != null ) && m_knownFluxes.activeSelf ? m_knownFluxMesh.vertexCount / 4 : 0;
	}

	// gives the starmap's background, grid, ship marker and crosshair the colours of the kit's scheme in use, in its dark mode (the starmap is a view of
	// space, dark in either mode); the stars, the nebulas and the rest keep their own colours
	void UseSchemeColours()
	{
		var theme = UIKitTheme.GetViewTheme();

		if ( theme == null )
		{
			return;
		}

		if ( m_propertyBlock == null )
		{
			m_propertyBlock = new MaterialPropertyBlock();
		}

		foreach ( var starmapRenderer in GetComponentsInChildren<Renderer>( true ) )
		{
			var materials = starmapRenderer.sharedMaterials;

			for ( var i = 0; i < materials.Length; i++ )
			{
				if ( ( materials[ i ] == null ) || !materials[ i ].HasProperty( c_colourProperty ) )
				{
					continue;
				}

				Color colour;

				switch ( materials[ i ].name )
				{
					case c_backgroundMaterialName: colour = theme.m_backgroundPanel; break;
					case "Starmap - Grid": colour = theme.m_accentPrimaryDim; break;
					case "Starmap - Player Ship": colour = theme.m_accentPrimary; break;
					case "Starmap - Crosshair": colour = theme.m_textPrimary; break;
					case "Starmap - Known Fluxes": colour = theme.m_textSecondary; break;
					default: continue;
				}

				// (the material's own opacity stays)
				colour.a = materials[ i ].GetColor( c_colourProperty ).a;

				starmapRenderer.GetPropertyBlock( m_propertyBlock, i );
				m_propertyBlock.SetColor( c_colourProperty, colour );
				starmapRenderer.SetPropertyBlock( m_propertyBlock, i );
			}
		}

		// the background image
		UseSchemeBackground( transform, m_backgroundMaterials );
	}

	// gives the images under a transform that are drawn with the starmap's background material (the starmap's and the ship's log's backgrounds) the
	// panel colour of the views' theme: an image takes no property block, so each is given a copy of the material the first time, kept in the list
	public static void UseSchemeBackground( Transform root, System.Collections.Generic.List<Material> copies )
	{
		var theme = UIKitTheme.GetViewTheme();

		if ( ( root == null ) || ( copies == null ) || ( theme == null ) )
		{
			return;
		}

		foreach ( var image in root.GetComponentsInChildren<UnityEngine.UI.Image>( true ) )
		{
			var material = image.material;

			if ( ( material == null ) || !material.HasProperty( c_colourProperty ) )
			{
				continue;
			}

			if ( material.name == c_backgroundMaterialName )
			{
				material = new Material( material );
				material.name = c_backgroundCopyName;

				copies.Add( material );

				image.material = material;
			}
			else if ( material.name != c_backgroundCopyName )
			{
				continue;
			}

			// (the material's own opacity stays)
			var colour = theme.m_backgroundPanel;

			colour.a = material.GetColor( c_colourProperty ).a;

			material.SetColor( c_colourProperty, colour );
		}
	}

	// destroys the copies of the background material (when their owner goes with the scene)
	public static void DestroyBackgroundCopies( System.Collections.Generic.List<Material> copies )
	{
		if ( copies == null )
		{
			return;
		}

		foreach ( var material in copies )
		{
			if ( material != null )
			{
				Destroy( material );
			}
		}

		copies.Clear();
	}

	// unity update
	void Update()
	{
		// if the player hits cancel (esc) or submit (enter) close the starmap
		if ( InputController.m_instance.m_cancel || InputController.m_instance.m_submit )
		{
			Hide();

			InputController.m_instance.Debounce();

			SpaceflightController.m_instance.m_buttonController.DeactivateButton();

			// show the radar (we are in hyperspace)
			SpaceflightController.m_instance.m_radar.Show();
		}
		else
		{
			// get the controller stick position
			float controllerX = InputController.m_instance.m_x;
			float controllerY = InputController.m_instance.m_y;

			// create our 3d move vector from the controller position
			Vector3 scrollVector = new Vector3( controllerX, controllerY, 0.0f );

			// check if the move vector will actually move the starmap (that the controller is not centered)
			if ( scrollVector.magnitude > 0.5f )
			{
				// normalize the move vector to a length of 1.0
				scrollVector.Normalize();

				// apply the desired scroll velocity
				scrollVector *= m_scrollVelocity * Time.deltaTime;

				// update the position offset
				m_positionOffset += scrollVector;

				// make sure we don't go out of bounds
				if ( m_positionOffset.x < 0.0f )
				{
					m_positionOffset.x = 0.0f;
				}
				else if ( m_positionOffset.x > 250.0f * m_starmap.transform.localScale.x )
				{
					m_positionOffset.x = 250.0f * m_starmap.transform.localScale.x;
				}

				if ( m_positionOffset.y < 0.0f )
				{
					m_positionOffset.y = 0.0f;
				}
				else if ( m_positionOffset.y > 220.0f * m_starmap.transform.localScale.y )
				{
					m_positionOffset.y = 220.0f * m_starmap.transform.localScale.y;
				}

				// move the starmap
				m_starmap.transform.localPosition = m_originalPosition - m_positionOffset;

				// update the viewport label
				UpdateLabel();
			}
		}
	}

	// shows the original's bar (MAP-OV WE72B: POSITION, DESTINATION, DISTANCE, FUEL): the ship's place in hyperspace, the cursor's, the distance between them in
	// whole coordinates, and the fuel estimate, the Endurium the engines would use to fly there ("STARMAP - The fuel estimate will be working", alpha.txt - its
	// tenths are the port's)
	void UpdateLabel()
	{
		var x = Mathf.RoundToInt( m_positionOffset.x / m_starmap.transform.localScale.x );
		var y = Mathf.RoundToInt( m_positionOffset.y / m_starmap.transform.localScale.y );

		var playerData = DataController.m_instance.m_playerData;
		var ship = Tools.WorldToGameCoordinates( playerData.m_general.m_lastHyperspaceCoordinates );
		var distance = Vector2.Distance( new Vector2( x, y ), new Vector2( ship.x, ship.z ) );
		var fuel = playerData.m_playerShip.GetFuelForDistance( distance );

		// (the words shortened, so that the four fit on the one line above the main view)
		m_label = "Pos " + Mathf.RoundToInt( ship.x ) + " " + Mathf.RoundToInt( ship.z ) + "   Dest " + x + " " + y + "   Dist " + Mathf.RoundToInt( distance ) + "   Fuel " + fuel.ToString( "0.0", System.Globalization.CultureInfo.InvariantCulture );

		SpaceflightController.m_instance.m_viewport.UpdateLabel( m_label );
	}

	// the label shown while the starmap is open (for the probe and anything else that wants it)
	public string GetLabel()
	{
		return m_label;
	}

	// hide the starmap
	public void Hide()
	{
		// unpause the game
		SpaceflightController.m_instance.m_gameIsPaused = false;

		// make this game object not active
		gameObject.SetActive( false );
	}

	// show the starmap
	public void Show()
	{
		// pause the game
		SpaceflightController.m_instance.m_gameIsPaused = true;

		// make this game object active
		gameObject.SetActive( true );

		// hide the radar
		SpaceflightController.m_instance.m_radar.Hide();

		// play a sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Beep );

		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		// get the current hyperspace coordinates of the player
		var worldHyperspaceCoordinates = playerData.m_general.m_lastHyperspaceCoordinates;

		// convert to game coordinates
		var gameHyperspaceCoordinates = Tools.WorldToGameCoordinates( worldHyperspaceCoordinates );

		// scale the coordinates (and move z to y)
		m_positionOffset = new Vector3( gameHyperspaceCoordinates.x * m_starmap.transform.localScale.x, gameHyperspaceCoordinates.z * m_starmap.transform.localScale.y, 0.0f );

		// update the starmap position
		m_starmap.transform.localPosition = m_originalPosition - m_positionOffset;

		// update the player ship position
		m_playerShip.transform.localPosition = new Vector3( -gameHyperspaceCoordinates.x, gameHyperspaceCoordinates.z, 15.0f );

		// and draw the fluxes the ship has been through
		DrawKnownFluxes();

		// the cursor is on the ship to begin with
		UpdateLabel();
	}

	// returns true if the starmap is open
	public bool IsOpen()
	{
		return gameObject.activeInHierarchy;
	}
}
