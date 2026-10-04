
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

		// get maximum armor points (a ship with no armor plating has the points of its bare hull, and the engineer can repair those as well)
		var maxArmor = ship.GetMaximumArmorPoints();

		// what the engineer works on - the armor plating, or the hull itself if no plating is installed
		var armorName = ship.GetArmorName();
		var armorLabel = ship.HasArmorPlating() ? "Armor" : "Hull";

		// check if there's damage to repair
		if ( ship.m_armorPoints < maxArmor )
		{
			// repairs take time - the engineer keeps at it until the armor is whole again (the better the engineer the faster it goes)
			var alreadyUnderWay = ship.m_repairsAreUnderWay;

			if ( !alreadyUnderWay )
			{
				ship.StartRepairs();
			}

			SpaceflightController.m_instance.m_messages.AddText(
				( alreadyUnderWay ? "<color=green>Still repairing the " + armorName + ", Captain.</color>\n" : "<color=green>Beginning repairs on the " + armorName + ", Captain.</color>\n" ) +
				armorLabel + ": <color=white>" + ship.m_armorPoints + "/" + maxArmor + "</color>\n" +
				"Estimated time: <color=white>" + FormatDuration( ship.GetRepairTimeRemaining() ) + "</color>"
			);
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		}
		else
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>No repairs needed. " + armorLabel + " at maximum.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
		}

		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
		return false;
	}
}
