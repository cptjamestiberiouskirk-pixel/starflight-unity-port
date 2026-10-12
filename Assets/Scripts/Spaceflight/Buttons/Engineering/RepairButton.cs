
using UnityEngine;

public class RepairButton : ShipButton
{
	// the repair item the engineer has asked for a mineral for, and the buttons that were on the console then (Yes and No go back to them)
	static int s_askedItem = -1;
	static ButtonController.ButtonSet s_buttonSetBeforeQuestion = ButtonController.ButtonSet.Bridge;

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

		// the original's Repair begins with the DAMAGE display (ENGINEER in the decompiled original: (/REPAIR) calls (/DAMAGE) first)
		var displayController = SpaceflightController.m_instance.m_displayController;

		if ( displayController.m_damageDisplay != null )
		{
			displayController.ChangeDisplay( displayController.m_damageDisplay );
		}

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

		// already at work? (pressing the button again does not speed anything up)
		if ( ship.m_repairsAreUnderWay )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=green>Still repairing the " + ship.GetRepairItemName( ship.m_repairItem ) + ", Captain.</color>\nEstimated time: <color=white>" + FormatDuration( GetTimeRemaining( ship ) ) + "</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		// the engineer repairs the worst first (the owner's ruling of 2026-10-11 for roadmap 1.2)
		var item = ship.FindWorstRepairItem();

		if ( item < 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>No repairs are required, Captain.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
			SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();
			return false;
		}

		AskForRepair( item );

		return false;
	}

	// the engineer asks for the mineral a repair needs, as the original's does (SS Repair: "Captain, we need 2 cubic meters of Molybdenum for repairs. Shall I take this
	// from cargo?" - and with none in the hold "... Ceasing repair")
	public static void AskForRepair( int item )
	{
		var gameData = DataController.m_instance.m_gameData;
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;
		var buttonController = SpaceflightController.m_instance.m_buttonController;

		var mineralId = ship.GetRepairMineralId( item );
		var needed = ship.GetRepairMineralNeeded( item );

		if ( ( mineralId < 0 ) || ( needed <= 0 ) )
		{
			return;
		}

		var mineral = gameData.m_elementList[ mineralId ].m_name;
		var inTheHold = ship.m_elementStorage.Find( mineralId );
		var held = ( inTheHold == null ) ? 0 : inTheHold.m_volume;

		messages.AddText( "<color=white>Captain, we need " + ( needed / 10 ) + " cubic meters of " + mineral + " for repairs.</color>" );

		if ( held < needed )
		{
			messages.AddText( "<color=white>Ceasing repair</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			buttonController.UpdateButtonSprites();

			return;
		}

		messages.AddText( "<color=white>Shall I take this from cargo?</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		// the answer (No and Yes on the console, then back to whatever was there)
		s_askedItem = item;

		if ( buttonController.GetCurrentButtonSet() != ButtonController.ButtonSet.RepairMineral )
		{
			s_buttonSetBeforeQuestion = buttonController.GetCurrentButtonSet();
		}

		buttonController.ChangeButtonSet( ButtonController.ButtonSet.RepairMineral );
	}

	// the answer to the question: yes takes the mineral from the hold and starts the repair, no stops
	public static void Answer( bool takeTheMineral )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;
		var item = s_askedItem;

		s_askedItem = -1;

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( s_buttonSetBeforeQuestion );

		var mineralId = ( item < 0 ) ? -1 : ship.GetRepairMineralId( item );
		var needed = ( item < 0 ) ? 0 : ship.GetRepairMineralNeeded( item );
		var inTheHold = ( mineralId < 0 ) ? null : ship.m_elementStorage.Find( mineralId );

		if ( !takeTheMineral || ( needed <= 0 ) || ( inTheHold == null ) || ( inTheHold.m_volume < needed ) )
		{
			messages.AddText( "<color=white>Ceasing repair</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

			return;
		}

		ship.RemoveElement( mineralId, needed );
		ship.StartRepairs( item );

		messages.AddText( "<color=green>Continuing repair</color>" );
		messages.AddText( "<color=green>Beginning repairs on the " + ship.GetRepairItemName( item ) + ", Captain.</color>\nEstimated time: <color=white>" + FormatDuration( GetTimeRemaining( ship ) ) + "</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}

	// how long the repair under way still takes (the hull by its armor points, a system at the same pace)
	static float GetTimeRemaining( PD_PlayerShip ship )
	{
		if ( ship.m_repairItem == PD_PlayerShip.c_hullRepairItem )
		{
			return ship.GetRepairTimeRemaining();
		}

		var rate = ship.GetRepairRate();
		var maximumArmor = ship.GetMaximumArmorPoints();

		if ( ( rate <= 0.0f ) || ( maximumArmor <= 0 ) )
		{
			return 0.0f;
		}

		return ( 100 - ship.GetRepairItemHealth( ship.m_repairItem ) ) / ( rate * 100.0f / maximumArmor );
	}
}
