
using UnityEngine;
using UnityEngine.InputSystem;

public class InputController : MonoBehaviour
{
	// static reference to this instance
	public static InputController m_instance;

	// the game's input actions (Assets/Input/Starflight Input.inputactions): the ship map has the eight directions, submit and cancel, each on the keys
	// its axis had in the old input manager, and the UI map is what the event systems of the starport and spaceflight scenes read
	public InputActionAsset m_inputActions;

	// submit (fire1) and cancel (fire2) inputs
	public bool m_submit { get; private set; }
	public bool m_cancel { get; private set; }

	// directional arrow inputs
	public bool m_northWest { get; private set; }
	public bool m_north { get; private set; }
	public bool m_northEast { get; private set; }
	public bool m_east { get; private set; }
	public bool m_southEast { get; private set; }
	public bool m_south { get; private set; }
	public bool m_southWest { get; private set; }
	public bool m_west { get; private set; }

	public float m_x { get; private set; }
	public float m_y { get; private set; }

	// true if we want to ignore the inputs until everything is released
	bool m_debounce;

	// the ship map and its actions
	InputActionMap m_shipActionMap;
	InputAction m_northWestAction;
	InputAction m_northAction;
	InputAction m_northEastAction;
	InputAction m_eastAction;
	InputAction m_southEastAction;
	InputAction m_southAction;
	InputAction m_southWestAction;
	InputAction m_westAction;
	InputAction m_submitAction;
	InputAction m_cancelAction;

	// unity awake
	void Awake()
	{
		// remember this instance to this
		m_instance = this;

		// without the actions asset there is no input at all
		if ( m_inputActions == null )
		{
			Debug.LogError( "InputController: the input actions asset is not assigned, so the game gets no input." );

			return;
		}

		// find the ship map
		m_shipActionMap = m_inputActions.FindActionMap( "Ship" );

		if ( m_shipActionMap == null )
		{
			Debug.LogError( "InputController: the input actions asset has no Ship map, so the game gets no input." );

			return;
		}

		// find the actions of the ship map
		m_northWestAction = FindShipAction( "NW" );
		m_northAction = FindShipAction( "N" );
		m_northEastAction = FindShipAction( "NE" );
		m_eastAction = FindShipAction( "E" );
		m_southEastAction = FindShipAction( "SE" );
		m_southAction = FindShipAction( "S" );
		m_southWestAction = FindShipAction( "SW" );
		m_westAction = FindShipAction( "W" );
		m_submitAction = FindShipAction( "Submit" );
		m_cancelAction = FindShipAction( "Cancel" );
	}

	// unity on enable
	void OnEnable()
	{
		// the actions of a map only report presses while the map is enabled
		if ( m_shipActionMap != null )
		{
			m_shipActionMap.Enable();
		}
	}

	// unity on disable
	void OnDisable()
	{
		// stop listening to the keys
		if ( m_shipActionMap != null )
		{
			m_shipActionMap.Disable();
		}
	}

	// unity update
	void Update()
	{
		// update directional movement
		m_northWest = IsPressed( m_northWestAction );
		m_north = IsPressed( m_northAction );
		m_northEast = IsPressed( m_northEastAction );
		m_east = IsPressed( m_eastAction );
		m_southEast = IsPressed( m_southEastAction );
		m_south = IsPressed( m_southAction );
		m_southWest = IsPressed( m_southWestAction );
		m_west = IsPressed( m_westAction );

		// update buttons
		m_submit = IsPressed( m_submitAction );
		m_cancel = IsPressed( m_cancelAction );

		// check if we want to debounce the buttons
		if ( m_debounce )
		{
			if ( !m_submit && !m_cancel && !m_north && !m_northEast && !m_east && !m_southEast && !m_south && !m_southWest && !m_west && !m_northWest )
			{
				m_debounce = false;
			}
			else
			{
				Debounce();
			}
		}

		// set x direction
		if ( m_northWest || m_west || m_southWest )
		{
			m_x = -1.0f;
		}
		else if ( m_northEast || m_east || m_southEast )
		{
			m_x = 1.0f;
		}
		else
		{
			m_x = 0.0f;
		}

		// set y direction
		if ( m_northWest || m_north || m_northEast )
		{
			m_y = 1.0f;
		}
		else if ( m_southWest || m_south || m_southEast )
		{
			m_y = -1.0f;
		}
		else
		{
			m_y = 0.0f;
		}
	}

	// call this to ignore inputs until everything has been let go
	public void Debounce()
	{
		m_debounce = true;

		m_submit = false;
		m_cancel = false;

		m_north = false;
		m_northEast = false;
		m_east = false;
		m_southEast = false;
		m_south = false;
		m_southWest = false;
		m_west = false;
		m_northWest = false;
	}

	// find an action of the ship map, and say so when the asset does not have it
	InputAction FindShipAction( string actionName )
	{
		// look for the action
		var action = m_shipActionMap.FindAction( actionName );

		// an action that is missing is never pressed
		if ( action == null )
		{
			Debug.LogError( "InputController: the Ship map of the input actions asset has no " + actionName + " action." );
		}

		return action;
	}

	// true while any key bound to the action is held down, like Input.GetButton was for the axes of the old input manager
	static bool IsPressed( InputAction action )
	{
		return ( action != null ) && action.IsPressed();
	}
}
