
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
			// repairs take time - the engineer keeps at it until the armor is whole again (the better the engineer the faster it goes)
			var alreadyUnderWay = ship.m_repairsAreUnderWay;

			if ( !alreadyUnderWay )
			{
				ship.StartRepairs();
			}

			SpaceflightController.m_instance.m_messages.AddText(
				( alreadyUnderWay ? "<color=green>Still repairing the armor, Captain.</color>\n" : "<color=green>Beginning repairs on the armor, Captain.</color>\n" ) +
				"Armor: <color=white>" + ship.m_armorPoints + "/" + maxArmor + "</color>\n" +
				"Estimated time: <color=white>" + FormatDuration( ship.GetRepairTimeRemaining() ) + "</color>"
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
