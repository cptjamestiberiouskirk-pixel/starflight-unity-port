
public class DamageButton : ShipButton
{
	public override string GetLabel()
	{
		return "Damage";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;

		SpaceflightController.m_instance.m_messages.Clear();

		// get max values from the ship (a ship with no armor plating still has the armor points of its bare hull)
		var maxShield = ship.GetMaximumShieldPoints();
		var maxArmor = ship.GetMaximumArmorPoints();

		// calculate percentages
		var shieldPercent = maxShield > 0 ? ( ship.m_shieldPoints * 100 / maxShield ) : 0;
		var armorPercent = maxArmor > 0 ? ( ship.m_armorPoints * 100 / maxArmor ) : 0;

		// determine status colors (using hex codes for TextMeshPro compatibility)
		string shieldColor = shieldPercent >= 75 ? "#00FF00" : ( shieldPercent >= 25 ? "#FFFF00" : "#FF0000" );
		string armorColor = armorPercent >= 75 ? "#00FF00" : ( armorPercent >= 25 ? "#FFFF00" : "#FF0000" );

		// build report
		var report = "<color=#FFFF00>Damage Report:</color>\n";

		if ( maxShield > 0 )
		{
			report += "Shields: <color=" + shieldColor + ">" + shieldPercent + "% (" + ship.m_shieldPoints + "/" + maxShield + ")</color>\n";
		}
		else
		{
			report += "Shields: <color=#808080>None installed</color>\n";
		}

		var armorStatus = "<color=" + armorColor + ">" + armorPercent + "% (" + ship.m_armorPoints + "/" + maxArmor + ")</color>";

		if ( ship.HasArmorPlating() )
		{
			// the armor plating is what takes the damage
			report += "Armor: " + armorStatus + "\n";
			report += "Hull: <color=#00FF00>Operational</color>";
		}
		else
		{
			// no armor plating - the damage goes to the hull itself
			report += "Armor: <color=#808080>None installed</color>\n";
			report += "Hull: " + armorStatus;
		}

		SpaceflightController.m_instance.m_messages.AddText( report );
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
