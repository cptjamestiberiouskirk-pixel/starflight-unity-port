
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// the UI kit's target bracket (the Figma file's Encounter and Combat mockups): four corners around the alien ship the player has targeted, with its vessel and its
// shields (in a hostile encounter) or its range under them, red while the encounter is hostile and amber otherwise; nothing shows when there is no target
// (it lives in the viewport, made by Starflight Remake > UI Kit > Apply to the Spaceflight Console)
public class TargetBracket : MonoBehaviour
{
	// the corners' parent, placed and sized around the target
	public RectTransform m_bracket;

	// the corners' bars, coloured by the encounter's stance
	public Graphic[] m_bars;

	// the vessel and its range or shields, under the corners
	public TextMeshProUGUI m_label;

	// the space around the target's outline, and the smallest bracket
	const float c_padding = 10.0f;
	const float c_minimumSize = 48.0f;

	// the area the bracket is placed in (the viewport) and the camera that draws the canvas
	RectTransform m_area;
	Camera m_uiCamera;

	// the target's model and its renderers (looked up when the target changes, not every frame)
	GameObject m_model;
	Renderer[] m_renderers;
	int m_rendererVesselId = -1;

	// what the label and the colours show now, so that they are only set again when it changes
	int m_vesselId = -1;
	bool m_hostile;
	bool m_coloured;
	string m_rangeFormat;
	string m_shieldsFormat;

	// unity awake
	void Awake()
	{
		m_area = transform.parent as RectTransform;

		var canvas = GetComponentInParent<Canvas>();

		m_uiCamera = ( ( canvas != null ) && ( canvas.renderMode != RenderMode.ScreenSpaceOverlay ) ) ? canvas.worldCamera : null;

		Show( false );
	}

	// unity late update (after the encounter has moved the ships)
	void LateUpdate()
	{
		// the target, if there is one to show
		var alienShip = FindTarget( out var encounter, out var targetIndex );

		if ( alienShip == null )
		{
			Show( false );
			return;
		}

		// its model (the renderers are looked up once per target)
		var model = encounter.GetAlienShipModel( targetIndex );

		if ( ( model == null ) || !model.activeInHierarchy )
		{
			Show( false );
			return;
		}

		// a model slot is used again by the next encounter with another vessel's meshes in it, so the renderers are looked up again for a new slot,
		// a new vessel, or when one of them has gone
		if ( ( model != m_model ) || ( alienShip.m_vesselId != m_rendererVesselId ) || RenderersGone() )
		{
			m_model = model;
			m_rendererVesselId = alienShip.m_vesselId;
			m_renderers = model.GetComponentsInChildren<Renderer>();
		}

		// the target's outline on the screen, in the viewport's space
		var camera = SpaceflightController.m_instance.m_playerCamera.m_camera;

		if ( ( camera == null ) || ( m_area == null ) || !ScreenOutline( camera, out var min, out var max ) )
		{
			Show( false );
			return;
		}

		RectTransformUtility.ScreenPointToLocalPointInRectangle( m_area, min, m_uiCamera, out var localMin );
		RectTransformUtility.ScreenPointToLocalPointInRectangle( m_area, max, m_uiCamera, out var localMax );

		var size = new Vector2( Mathf.Max( c_minimumSize, Mathf.Abs( localMax.x - localMin.x ) + c_padding * 2.0f ), Mathf.Max( c_minimumSize, Mathf.Abs( localMax.y - localMin.y ) + c_padding * 2.0f ) );

		m_bracket.localPosition = ( localMin + localMax ) * 0.5f;
		m_bracket.sizeDelta = size;

		// the colours and the label
		var pdEncounter = encounter.m_pdEncounter;
		var hostile = pdEncounter.m_attackedByPlayer || ( pdEncounter.m_alienStance == GD_Comm.Stance.Hostile );

		UpdateColours( hostile );
		UpdateLabel( alienShip, hostile );

		Show( true );
	}

	// returns the targeted alien ship and its encounter, or null when there is nothing to show (not in an encounter, no target, a dead target)
	static PD_AlienShip FindTarget( out Encounter encounter, out int targetIndex )
	{
		encounter = null;
		targetIndex = -1;

		var controller = SpaceflightController.m_instance;
		var combat = CombatController.m_instance;

		if ( ( controller == null ) || ( combat == null ) || ( DataController.m_instance == null ) )
		{
			return null;
		}

		if ( DataController.m_instance.m_playerData.m_general.m_location != PD_General.Location.Encounter )
		{
			return null;
		}

		encounter = controller.m_encounter;
		targetIndex = combat.GetTargetIndex();

		if ( ( encounter == null ) || ( encounter.m_pdEncounter == null ) || ( targetIndex < 0 ) )
		{
			return null;
		}

		var alienShips = encounter.m_pdEncounter.GetAlienShipList();

		if ( ( alienShips == null ) || ( targetIndex >= alienShips.Length ) )
		{
			return null;
		}

		var alienShip = alienShips[ targetIndex ];

		return ( ( alienShip == null ) || alienShip.m_isDead || !alienShip.m_addedToEncounter ) ? null : alienShip;
	}

	// the target's outline on the screen: the corners of its renderers' bounds, projected (false when it is behind the camera)
	bool ScreenOutline( Camera camera, out Vector2 min, out Vector2 max )
	{
		min = new Vector2( float.MaxValue, float.MaxValue );
		max = new Vector2( float.MinValue, float.MinValue );

		var found = false;

		for ( var r = 0; r < m_renderers.Length; r++ )
		{
			var renderer = m_renderers[ r ];

			// only the ship's meshes: particles, trails and beams reach far from it (a beam can reach behind the camera)
			if ( ( renderer == null ) || !renderer.enabled || !( ( renderer is MeshRenderer ) || ( renderer is SkinnedMeshRenderer ) ) )
			{
				continue;
			}

			var bounds = renderer.bounds;

			for ( var corner = 0; corner < 8; corner++ )
			{
				var point = bounds.center + Vector3.Scale( bounds.extents, new Vector3( ( ( corner & 1 ) == 0 ) ? -1.0f : 1.0f, ( ( corner & 2 ) == 0 ) ? -1.0f : 1.0f, ( ( corner & 4 ) == 0 ) ? -1.0f : 1.0f ) );
				var screen = camera.WorldToScreenPoint( point );

				if ( screen.z <= 0.0f )
				{
					return false;
				}

				min = Vector2.Min( min, screen );
				max = Vector2.Max( max, screen );
				found = true;
			}
		}

		// a target outside the view has no bracket (it would be drawn outside the viewport, over the console)
		var view = camera.pixelRect;

		return found && ( max.x >= view.xMin ) && ( min.x <= view.xMax ) && ( max.y >= view.yMin ) && ( min.y <= view.yMax );
	}

	// whether a renderer looked up for the target has been destroyed since (or none were found)
	bool RenderersGone()
	{
		if ( ( m_renderers == null ) || ( m_renderers.Length == 0 ) )
		{
			return true;
		}

		for ( var r = 0; r < m_renderers.Length; r++ )
		{
			if ( m_renderers[ r ] == null )
			{
				return true;
			}
		}

		return false;
	}

	// red while the encounter is hostile, the kit's amber otherwise
	void UpdateColours( bool hostile )
	{
		if ( m_coloured && ( hostile == m_hostile ) )
		{
			return;
		}

		var theme = UIKitTheme.Get();

		if ( theme == null )
		{
			return;
		}

		var colour = hostile ? theme.m_statusDanger : theme.m_accentSecondary;

		for ( var i = 0; i < m_bars.Length; i++ )
		{
			if ( m_bars[ i ] != null )
			{
				m_bars[ i ].color = colour;
			}
		}

		if ( m_label != null )
		{
			m_label.color = colour;
		}

		m_hostile = hostile;
		m_coloured = true;
	}

	// the vessel's name with its shields (hostile) or its range (otherwise); the formats are made once per vessel, and TextMesh Pro fills in the number
	// without making a new string every frame
	void UpdateLabel( PD_AlienShip alienShip, bool hostile )
	{
		if ( m_label == null )
		{
			return;
		}

		var gameData = DataController.m_instance.m_gameData;

		if ( ( alienShip.m_vesselId < 0 ) || ( alienShip.m_vesselId >= gameData.m_vesselList.Length ) )
		{
			return;
		}

		var vessel = gameData.m_vesselList[ alienShip.m_vesselId ];

		if ( alienShip.m_vesselId != m_vesselId )
		{
			m_vesselId = alienShip.m_vesselId;
			m_rangeFormat = vessel.m_name + " · Range {0:0}";
			m_shieldsFormat = vessel.m_name + " · Shields {0:0}%";
		}

		if ( hostile && ( vessel.m_shields > 0 ) )
		{
			m_label.SetText( m_shieldsFormat, 100.0f * Mathf.Max( 0, alienShip.m_shieldPoints ) / vessel.m_shields );
		}
		else
		{
			var range = Vector3.Distance( DataController.m_instance.m_playerData.m_general.m_coordinates, alienShip.m_coordinates );

			m_label.SetText( m_rangeFormat, range );
		}
	}

	// shows or hides the bracket (and forgets its colours when hidden, so that they are set again on the next target)
	void Show( bool show )
	{
		if ( ( m_bracket != null ) && ( m_bracket.gameObject.activeSelf != show ) )
		{
			m_bracket.gameObject.SetActive( show );
		}

		if ( !show )
		{
			m_coloured = false;
		}
	}
}
