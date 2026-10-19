using System.Collections.Generic;
using UnityEngine;

// the shipped game's icons over the surface while the terrain vehicle's Map is up (the original draws its surface map with them, MAPS and LISTICONS ICONS: roadmap
// 8.27): the ship, the terrain vehicle, the deposits, the artifacts lying in ruins, the ruins, the dropped cargo and the lifeforms, each a small picture above it
// that keeps its size on the screen whatever the Map's height, and turns with the camera
public class TerrainIconMarkers : MonoBehaviour
{
	// a marker and what it marks (a lifeform's picture changes when it dies)
	class Marker
	{
		public Transform m_target;
		public TerrainLifeform m_lifeform;
		public SpriteRenderer m_renderer;
		public int m_icon;
	}

	// the markers
	readonly List<Marker> m_markers = new List<Marker>();

	// the size of a marker, as a share of the Map's height above the vehicle (the camera's follow distance), and how far above its object it floats, as a share of
	// its size
	const float c_sizePerHeight = 0.03f;
	const float c_heightPerSize = 1.0f;

	// the markers are drawn over the ground and the models
	const int c_sortingOrder = 100;

	// the Map's height above the vehicle
	float m_followDistance = 192.0f;

	// the camera the markers turn with
	Camera m_camera;

	// the Map button that made the markers, and the key it shows with them - both go once the Map is no longer the button running (whatever ended it)
	[System.NonSerialized] public ShipButton m_owner;
	[System.NonSerialized] public TerrainIconKeyPanel m_keyPanel;

	// marks everything on the surface around the terrain vehicle, for a Map at this height
	public void Show( float followDistance )
	{
		Hide();

		m_followDistance = followDistance;
		m_camera = Camera.main;

		var disembarked = SpaceflightController.m_instance.m_disembarked;

		if ( disembarked == null )
		{
			return;
		}

		// the vehicle, and the ship it came from
		if ( disembarked.m_terrainVehicle != null )
		{
			AddMarker( disembarked.m_terrainVehicle.transform, TerrainIconKey.c_terrainVehicle, null );
		}

		if ( ( disembarked.m_arthShip != null ) && disembarked.m_arthShip.activeInHierarchy )
		{
			AddMarker( disembarked.m_arthShip.transform, TerrainIconKey.c_ship, null );
		}

		var terrainGrid = disembarked.m_terrainGrid;

		if ( terrainGrid == null )
		{
			return;
		}

		// the deposits not yet mined out
		if ( terrainGrid.m_terrainElements != null )
		{
			foreach ( var element in terrainGrid.m_terrainElements.GetComponentsInChildren<TerrainElement>( false ) )
			{
				AddMarker( element.transform, TerrainIconKey.c_mineral, null );
			}
		}

		// the ruins, and the artifacts lying in them
		if ( terrainGrid.m_terrainRuins != null )
		{
			foreach ( var ruin in terrainGrid.m_terrainRuins.GetComponentsInChildren<TerrainRuin>( false ) )
			{
				AddMarker( ruin.transform, TerrainIconKey.GetRuinIcon( ruin ), null );

				foreach ( var artifactDisplay in ruin.m_artifactDisplays )
				{
					if ( ( artifactDisplay != null ) && artifactDisplay.activeInHierarchy )
					{
						AddMarker( artifactDisplay.transform, TerrainIconKey.c_artifact, null );
					}
				}
			}
		}

		// what the vehicle has dropped: a mineral or an artifact (the wreck of a vehicle is neither)
		var planetSurfaces = DataController.m_instance.m_playerData.m_planetSurfaces;

		foreach ( var droppedCargo in FindObjectsByType<TerrainDroppedCargo>( FindObjectsSortMode.None ) )
		{
			var record = FindDroppedCargo( planetSurfaces, droppedCargo.m_droppedCargoId );

			if ( ( record == null ) || record.m_isVehicleWreck )
			{
				continue;
			}

			AddMarker( droppedCargo.transform, ( record.m_artifactId >= 0 ) ? TerrainIconKey.c_artifact : TerrainIconKey.c_mineral, null );
		}

		// the lifeforms
		if ( terrainGrid.m_terrainLifeforms != null )
		{
			foreach ( var lifeform in terrainGrid.m_terrainLifeforms.m_lifeformList )
			{
				if ( lifeform != null )
				{
					AddMarker( lifeform.transform, TerrainIconKey.GetLifeformIcon( lifeform ), lifeform );
				}
			}
		}

		// in place at once (Show runs inside the Map button's Execute, before the console has made it the running button, so not LateUpdate's check)
		PlaceMarkers();
	}

	// the Map is at this height now
	public void SetHeight( float followDistance )
	{
		m_followDistance = followDistance;
	}

	// takes the markers off the surface
	public void Hide()
	{
		foreach ( var marker in m_markers )
		{
			if ( marker.m_renderer != null )
			{
				Destroy( marker.m_renderer.gameObject );
			}
		}

		m_markers.Clear();
	}

	// how many markers there are, and the icon of one of them (-1 for no such marker) - for the probe
	public int GetMarkerCount()
	{
		return m_markers.Count;
	}

	public int GetMarkerIcon( int index )
	{
		return ( ( index >= 0 ) && ( index < m_markers.Count ) ) ? m_markers[ index ].m_icon : -1;
	}

	// what a marker marks (null for no such marker) - for the probe
	public Transform GetMarkerTarget( int index )
	{
		return ( ( index >= 0 ) && ( index < m_markers.Count ) ) ? m_markers[ index ].m_target : null;
	}

	// a marker above this object
	void AddMarker( Transform target, int icon, TerrainLifeform lifeform )
	{
		var markerObject = new GameObject( "Icon " + TerrainIconKey.GetName( icon ) );

		markerObject.transform.SetParent( transform, false );
		markerObject.layer = target.gameObject.layer;

		var spriteRenderer = markerObject.AddComponent<SpriteRenderer>();

		spriteRenderer.sprite = TerrainIconKey.GetSprite( icon );
		spriteRenderer.sortingOrder = c_sortingOrder;

		m_markers.Add( new Marker { m_target = target, m_lifeform = lifeform, m_renderer = spriteRenderer, m_icon = icon } );
	}

	// the record of a dropped item (null if there is none)
	static PD_PlanetSurfaces.DroppedCargo FindDroppedCargo( PD_PlanetSurfaces planetSurfaces, int droppedCargoId )
	{
		if ( ( planetSurfaces == null ) || ( planetSurfaces.m_droppedCargoList == null ) )
		{
			return null;
		}

		foreach ( var record in planetSurfaces.m_droppedCargoList )
		{
			if ( ( record != null ) && ( record.m_id == droppedCargoId ) )
			{
				return record;
			}
		}

		return null;
	}

	// unity late update
	void LateUpdate()
	{
		// the Map is over: the markers and the key go
		if ( ( m_owner != null ) && ( ( SpaceflightController.m_instance == null ) || ( SpaceflightController.m_instance.m_buttonController.GetCurrentButton() != m_owner ) ) )
		{
			Destroy( gameObject );

			return;
		}

		PlaceMarkers();
	}

	// the markers follow what they mark, turn with the camera and keep their size on the screen
	void PlaceMarkers()
	{
		if ( m_camera == null )
		{
			m_camera = Camera.main;
		}

		var size = m_followDistance * c_sizePerHeight;
		var scale = new Vector3( size, size, size );

		foreach ( var marker in m_markers )
		{
			if ( marker.m_renderer == null )
			{
				continue;
			}

			// a marker goes with its object (a mined-out deposit, an artifact picked up, an eaten lifeform)
			var visible = ( marker.m_target != null ) && marker.m_target.gameObject.activeInHierarchy;

			marker.m_renderer.enabled = visible;

			if ( !visible )
			{
				continue;
			}

			// a lifeform that has died shows the dead lifeform's picture
			if ( marker.m_lifeform != null )
			{
				var icon = TerrainIconKey.GetLifeformIcon( marker.m_lifeform );

				if ( icon != marker.m_icon )
				{
					marker.m_icon = icon;
					marker.m_renderer.sprite = TerrainIconKey.GetSprite( icon );
				}
			}

			var markerTransform = marker.m_renderer.transform;

			markerTransform.position = marker.m_target.position + Vector3.up * ( size * c_heightPerSize );
			markerTransform.localScale = scale;

			if ( m_camera != null )
			{
				markerTransform.rotation = m_camera.transform.rotation;
			}
		}
	}

	// unity on destroy
	void OnDestroy()
	{
		Hide();

		if ( m_keyPanel != null )
		{
			Destroy( m_keyPanel.gameObject );
		}
	}
}
