
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

		// get max values from ship components
		var shielding = ship.GetShielding();
		var armor = ship.GetArmor();

		var maxShield = shielding.m_points;
		var maxArmor = armor.m_points;

		// calculate percentages
		var shieldPercent = maxShield > 0 ? ( ship.m_shieldPoints * 100 / maxShield ) : 0;
		var armorPercent = maxArmor > 0 ? ( ship.m_armorPoints * 100 / maxArmor ) : 0;

		// determine status colors
		string shieldColor = shieldPercent >= 75 ? "green" : ( shieldPercent >= 25 ? "yellow" : "red" );
		string armorColor = armorPercent >= 75 ? "green" : ( armorPercent >= 25 ? "yellow" : "red" );

		// build report
		var report = "<color=yellow>Damage Report:</color>\n";

		if ( maxShield > 0 )
		{
			report += "Shields: <color=" + shieldColor + ">" + shieldPercent + "% (" + ship.m_shieldPoints + "/" + maxShield + ")</color>\n";
		}
		else
		{
			report += "Shields: <color=gray>None installed</color>\n";
		}

		if ( maxArmor > 0 )
		{
			report += "Armor: <color=" + armorColor + ">" + armorPercent + "% (" + ship.m_armorPoints + "/" + maxArmor + ")</color>\n";
		}
		else
		{
			report += "Armor: <color=gray>None installed</color>\n";
		}

		report += "Hull: <color=green>Operational</color>";

		SpaceflightController.m_instance.m_messages.AddText( report );
		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
