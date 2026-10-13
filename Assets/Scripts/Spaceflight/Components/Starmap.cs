
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

	// unity start
	void Start()
	{
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

		// the cursor is on the ship to begin with
		UpdateLabel();
	}

	// returns true if the starmap is open
	public bool IsOpen()
	{
		return gameObject.activeInHierarchy;
	}
}
