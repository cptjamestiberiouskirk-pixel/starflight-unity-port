
using UnityEngine;

public class RepairButton : ShipButton
{
	public override string GetLabel()
	{
		return "Repair";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;

		SpaceflightController.m_instance.m_messages.Clear();

		// check if we have an engineer assigned
		if ( !playerData.m_crewAssignment.IsAssigned( PD_CrewAssignment.Role.Engineer ) )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>No engineer assigned!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// get the engineer
		var engineer = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Engineer );

		// check if engineer is alive
		if ( engineer.m_vitality <= 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>The engineer is incapacitated!</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// calculate repair power based on engineering skill (5 points per skill level)
		var repairPower = engineer.m_engineering * 5;

		// get maximum armor points
		var armor = ship.GetArmor();
		var maxArmor = armor.m_points;

		// check if there's damage to repair
		if ( maxArmor <= 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>No armor installed to repair.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		if ( ship.m_armorPoints < maxArmor )
		{
			var previousArmor = ship.m_armorPoints;
			ship.m_armorPoints = Mathf.Min( maxArmor, ship.m_armorPoints + repairPower );
			var repairedAmount = ship.m_armorPoints - previousArmor;

			SpaceflightController.m_instance.m_messages.AddText(
				"<color=green>Repairs complete.</color>\n" +
				"Repaired: <color=white>" + repairedAmount + " points</color>\n" +
				"Armor: <color=white>" + ship.m_armorPoints + "/" + maxArmor + "</color>"
			);
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		}
		else
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>No repairs needed. Armor at maximum.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		}

		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		return false;
	}
}
