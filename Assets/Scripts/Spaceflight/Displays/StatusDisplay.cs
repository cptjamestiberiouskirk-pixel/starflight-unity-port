
using UnityEngine;
using TMPro;

public class StatusDisplay : ShipDisplay
{
	// the values text
	public TextMeshProUGUI m_values;

	// the shield outline
	public GameObject m_shieldOutline;

	// the shield gauge
	public RectTransform m_shieldGauge;

	// the armor gauge
	public RectTransform m_armorGauge;

	// the missile launcher
	public GameObject m_missileLauncher;

	// the laser cannon
	public GameObject m_laserCannon;

	// the cargo pods
	public GameObject[] m_cargoPods;

	// unity start
	public override void Start()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		// show only as many cargo pods as we have purchased
		for ( int cargoPodId = 0; cargoPodId < m_cargoPods.Length; cargoPodId++ )
		{
			m_cargoPods[ cargoPodId ].SetActive( cargoPodId < playerData.m_playerShip.m_numCargoPods );
		}

		// hide or show the missile launchers depending on if we have them
		m_missileLauncher.SetActive( playerData.m_playerShip.m_missileLauncherClass > 0 );

		// hide or show the missile launchers depending on if we have them
		m_laserCannon.SetActive( playerData.m_playerShip.m_laserCannonClass > 0 );
	}

	// unity update
	public override void Update()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		// update the date
		m_values.text = playerData.m_general.m_currentStardateDHMY + "\n";

		// get the most armor and shield points this ship can have (the armor is never zero - a ship with no armor plating has the points of its bare hull)
		int maximumArmorPoints = playerData.m_playerShip.GetMaximumArmorPoints();
		int maximumShieldPoints = playerData.m_playerShip.GetMaximumShieldPoints();

		// update the damage text
		if ( playerData.m_playerShip.m_armorPoints < maximumArmorPoints )
		{
			float damagePercent = ( 1.0f - (float) playerData.m_playerShip.m_armorPoints / (float) maximumArmorPoints ) * 100.0f;
			m_values.text += "<color=red>" + damagePercent.ToString( "N0" ) + "% Hull Damage</color>\n";
		}
		else
		{
			m_values.text += "None\n";
		}

		// update the cargo usage
		float cargoUsage = (float) playerData.m_playerShip.m_volumeUsed / (float) playerData.m_playerShip.m_volume * 100.0f;
		string cargoColor = ( cargoUsage > 90.0f ) ? "red" : ( ( cargoUsage > 75.0f ) ? "yellow" : "white" );
		m_values.text += "<color=" + cargoColor + ">" + cargoUsage.ToString( "N1" ) + "% Full</color>\n";

		// get to the endurium in the ship storage
		PD_ElementReference elementReference = playerData.m_playerShip.m_elementStorage.Find( 5 );

		// update the amount of energy remaining
		if ( elementReference == null )
		{
			m_values.text += "<color=red>None</color>\n";
		}
		else
		{
			float energyAmount = (float) elementReference.m_volume / 10.0f;
			string energyColor = ( energyAmount < 5.0f ) ? "red" : ( ( energyAmount < 15.0f ) ? "yellow" : "white" );
			m_values.text += "<color=" + energyColor + ">" + energyAmount.ToString( "N1" ) + "M<sup>3</sup></color>\n";
		}

		// do we have shields?
		if ( playerData.m_playerShip.m_shieldingClass == 0 )
		{
			// no
			m_values.text += "None\n";

			// hide the shield outline
			m_shieldOutline.SetActive( false );
		}
		else
		{
			// are our shields up?
			if ( playerData.m_playerShip.m_shieldsAreUp )
			{
				// yes
				m_values.text += "Up\n";

				// show the shield outline
				m_shieldOutline.SetActive( true );
			}
			else
			{
				// no
				m_values.text += "Down\n";

				// hide the shield outline
				m_shieldOutline.SetActive( false );
			}
		}

		// do we have weapons?
		if ( ( playerData.m_playerShip.m_missileLauncherClass == 0 ) && ( playerData.m_playerShip.m_laserCannonClass == 0 ) )
		{
			// no
			m_values.text += "None\n";
		}
		else
		{
			// are the weapons armed?
			if ( playerData.m_playerShip.m_weaponsAreArmed )
			{
				//
				m_values.text += "Armed\n";
			}
			else
			{
				m_values.text += "Unarmed\n";
			}
		}

		// update the shield gauge (how much of its full charge the installed shielding has - empty if there is no shielding)
		float shieldCharge = ( maximumShieldPoints > 0 ) ? ( (float) playerData.m_playerShip.m_shieldPoints / (float) maximumShieldPoints ) : 0.0f;
		m_shieldGauge.anchorMax = new Vector2( 1.0f, Mathf.Clamp01( shieldCharge ) );

		// update the armor gauge (how much of its armor points the ship has left)
		m_armorGauge.anchorMax = new Vector2( 1.0f, Mathf.Clamp01( (float) playerData.m_playerShip.m_armorPoints / (float) maximumArmorPoints ) );
	}

	// the status display label
	public override string GetLabel()
	{
		return "Status";
	}
}
