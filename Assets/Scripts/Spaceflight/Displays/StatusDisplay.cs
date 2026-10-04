
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

	// what the text was last built from (building it takes memory, and this runs every frame, so the text is only built again when one of these changes)
	string m_shownStardate;
	int m_shownArmorPoints = int.MinValue;
	int m_shownMaximumArmorPoints;
	int m_shownVolumeUsed;
	int m_shownVolume;
	int m_shownEndurium;
	int m_shownShieldingClass;
	bool m_shownShieldsAreUp;
	bool m_shownHasWeapons;
	bool m_shownWeaponsAreArmed;

	// unity update
	public override void Update()
	{
		// get to the player data
		PlayerData playerData = DataController.m_instance.m_playerData;

		// get the most armor and shield points this ship can have (the armor is never zero - a ship with no armor plating has the points of its bare hull)
		int maximumArmorPoints = playerData.m_playerShip.GetMaximumArmorPoints();
		int maximumShieldPoints = playerData.m_playerShip.GetMaximumShieldPoints();

		// get to the endurium in the ship storage (in tenths of a cubic meter, -1 if there is none)
		PD_ElementReference elementReference = playerData.m_playerShip.m_elementStorage.Find( 5 );
		int endurium = ( elementReference == null ) ? -1 : elementReference.m_volume;

		// do we have weapons?
		bool hasWeapons = ( playerData.m_playerShip.m_missileLauncherClass != 0 ) || ( playerData.m_playerShip.m_laserCannonClass != 0 );

		// has anything the text shows changed since we last built it?
		if ( ( m_shownStardate != playerData.m_general.m_currentStardateDHMY ) || ( m_shownArmorPoints != playerData.m_playerShip.m_armorPoints ) || ( m_shownMaximumArmorPoints != maximumArmorPoints ) || ( m_shownVolumeUsed != playerData.m_playerShip.m_volumeUsed ) || ( m_shownVolume != playerData.m_playerShip.m_volume ) || ( m_shownEndurium != endurium ) || ( m_shownShieldingClass != playerData.m_playerShip.m_shieldingClass ) || ( m_shownShieldsAreUp != playerData.m_playerShip.m_shieldsAreUp ) || ( m_shownHasWeapons != hasWeapons ) || ( m_shownWeaponsAreArmed != playerData.m_playerShip.m_weaponsAreArmed ) )
		{
			// yes - remember what the text is built from this time
			m_shownStardate = playerData.m_general.m_currentStardateDHMY;
			m_shownArmorPoints = playerData.m_playerShip.m_armorPoints;
			m_shownMaximumArmorPoints = maximumArmorPoints;
			m_shownVolumeUsed = playerData.m_playerShip.m_volumeUsed;
			m_shownVolume = playerData.m_playerShip.m_volume;
			m_shownEndurium = endurium;
			m_shownShieldingClass = playerData.m_playerShip.m_shieldingClass;
			m_shownShieldsAreUp = playerData.m_playerShip.m_shieldsAreUp;
			m_shownHasWeapons = hasWeapons;
			m_shownWeaponsAreArmed = playerData.m_playerShip.m_weaponsAreArmed;

			// update the date
			string text = playerData.m_general.m_currentStardateDHMY + "\n";

			// update the damage text
			if ( playerData.m_playerShip.m_armorPoints < maximumArmorPoints )
			{
				float damagePercent = ( 1.0f - (float) playerData.m_playerShip.m_armorPoints / (float) maximumArmorPoints ) * 100.0f;
				text += "<color=red>" + damagePercent.ToString( "N0" ) + "% Hull Damage</color>\n";
			}
			else
			{
				text += "None\n";
			}

			// update the cargo usage
			float cargoUsage = (float) playerData.m_playerShip.m_volumeUsed / (float) playerData.m_playerShip.m_volume * 100.0f;
			string cargoColor = ( cargoUsage > 90.0f ) ? "red" : ( ( cargoUsage > 75.0f ) ? "yellow" : "white" );
			text += "<color=" + cargoColor + ">" + cargoUsage.ToString( "N1" ) + "% Full</color>\n";

			// update the amount of energy remaining
			if ( elementReference == null )
			{
				text += "<color=red>None</color>\n";
			}
			else
			{
				float energyAmount = (float) elementReference.m_volume / 10.0f;
				string energyColor = ( energyAmount < 5.0f ) ? "red" : ( ( energyAmount < 15.0f ) ? "yellow" : "white" );
				text += "<color=" + energyColor + ">" + energyAmount.ToString( "N1" ) + "M<sup>3</sup></color>\n";
			}

			// do we have shields?
			if ( playerData.m_playerShip.m_shieldingClass == 0 )
			{
				// no
				text += "None\n";
			}
			else
			{
				// are our shields up?
				text += playerData.m_playerShip.m_shieldsAreUp ? "Up\n" : "Down\n";
			}

			// do we have weapons?
			if ( !hasWeapons )
			{
				// no
				text += "None\n";
			}
			else
			{
				// are the weapons armed?
				text += playerData.m_playerShip.m_weaponsAreArmed ? "Armed\n" : "Unarmed\n";
			}

			// put the text on the display (it used to be put there seven times a frame, a piece at a time)
			m_values.text = text;
		}

		// show the shield outline only when we have shields and they are up
		bool showShieldOutline = ( playerData.m_playerShip.m_shieldingClass != 0 ) && playerData.m_playerShip.m_shieldsAreUp;

		if ( m_shieldOutline.activeSelf != showShieldOutline )
		{
			m_shieldOutline.SetActive( showShieldOutline );
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
