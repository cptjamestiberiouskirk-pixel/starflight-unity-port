
using UnityEngine;

public class MapButton : ShipButton
{
	float m_followDistance;

	// the shipped game's icons over the surface while the Map is up, and their key on the auxiliary display (LISTICONS ICONS: roadmap 8.27 - the original's
	// terrain vehicle has a seventh button for the key, which the port's console has no room for)
	TerrainIconMarkers m_iconMarkers;
	TerrainIconKeyPanel m_iconKey;

	// whether the stick was pushed left or right last frame (one page a push)
	bool m_stickWasPushed;

	public override string GetLabel()
	{
		return "Map";
	}

	public override bool Execute()
	{
		// reset the follow distance
		m_followDistance = 192.0f;

		// get the terrain vehicle game object
		var gameObject = SpaceflightController.m_instance.m_playerCamera.GetCameraFollowGameObject();

		// position the camera directly above the tv
		SpaceflightController.m_instance.m_playerCamera.SetCameraFollow( gameObject, Vector3.up * m_followDistance );

		// mark what is on the surface with the shipped game's icons, and show their key
		ShowIcons();

		// return true to keep the button lit and active
		return true;
	}

	public override bool Update()
	{
		// get the terrain vehicle game object
		var gameObject = SpaceflightController.m_instance.m_playerCamera.GetCameraFollowGameObject();

		// check if we want to turn off the map
		if ( InputController.m_instance.m_submit )
		{
			// debounce the input
			InputController.m_instance.Debounce();

			// deactivate the current button
			SpaceflightController.m_instance.m_buttonController.DeactivateButton();

			// the icons and their key go
			HideIcons();

			// reset the camera follow
			SpaceflightController.m_instance.m_playerCamera.SetCameraFollow( gameObject, new Vector3( 0.0f, 75.0f, -75.0f ) );

			// play the deactivate sound
			SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );
		}
		else
		{
			// get the controller stick position
			float z = InputController.m_instance.m_y;

			// check if the zoom vector will actually zoom the camera (that the controller is not centered)
			if ( ( z < -0.5f ) || ( z > 0.5f ) )
			{
				// change the follow distance
				m_followDistance -= z * 150.0f * Time.deltaTime;

				// restrict follow distance
				m_followDistance = Mathf.Clamp( m_followDistance, 192.0f, 512.0f );

				// update the camera position
				SpaceflightController.m_instance.m_playerCamera.SetCameraFollow( gameObject, Vector3.up * m_followDistance );

				// the icons keep their size on the screen
				if ( m_iconMarkers != null )
				{
					m_iconMarkers.SetHeight( m_followDistance );
				}
			}

			// left and right turn the key's page
			TurnKeyPage();
		}

		// returning true prevents the default spaceflight update from running
		return true;
	}

	// marks what is on the surface with the shipped game's icons, and shows their key over the auxiliary display
	void ShowIcons()
	{
		HideIcons();

		var markersObject = new GameObject( "Icon markers" );

		m_iconMarkers = markersObject.AddComponent<TerrainIconMarkers>();
		m_iconMarkers.m_owner = this;

		var display = SpaceflightController.m_instance.m_displayController.m_terrainVehicleDisplay;

		if ( display != null )
		{
			m_iconKey = TerrainIconKeyPanel.Create( (RectTransform) display.transform );
			m_iconMarkers.m_keyPanel = m_iconKey;
		}

		m_iconMarkers.Show( m_followDistance );

		m_stickWasPushed = false;
	}

	// takes the icons and their key away (the markers take the key with them)
	void HideIcons()
	{
		if ( m_iconMarkers != null )
		{
			Object.Destroy( m_iconMarkers.gameObject );
		}

		m_iconMarkers = null;
		m_iconKey = null;
	}

	// one page of the key a push of the stick to the left or the right
	void TurnKeyPage()
	{
		var input = InputController.m_instance;
		var step = input.m_east ? 1 : ( input.m_west ? -1 : 0 );

		if ( step == 0 )
		{
			m_stickWasPushed = false;
		}
		else if ( !m_stickWasPushed )
		{
			m_stickWasPushed = true;

			if ( m_iconKey != null )
			{
				m_iconKey.ShowPage( ( m_iconKey.GetPage() + step + TerrainIconKeyPanel.c_pageCount ) % TerrainIconKeyPanel.c_pageCount );

				SoundController.m_instance.PlaySound( SoundController.Sound.Click );
			}
		}
	}

	// the key shown, and the markers (null while the Map is not up) - for the probe
	public TerrainIconKeyPanel GetIconKey()
	{
		return m_iconKey;
	}

	public TerrainIconMarkers GetIconMarkers()
	{
		return m_iconMarkers;
	}
}
