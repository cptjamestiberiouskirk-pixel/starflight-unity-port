
using UnityEngine;

// the engineer's Repair, as the original's (ENGINEER in the decompiled original, (/REPAIR)): the DAMAGE display with a cursor that the stick moves up and down
// its seven rows, the text window telling about the row under the cursor, and the fire button choosing what to repair - the engineer then asks for the
// repair's mineral (SS Repair: "Captain, we need 2 cubic meters of Molybdenum for repairs. Shall I take this from cargo?"), the owner's ruling of 2026-10-12
// (which replaced the engineer repairing the worst first)
public class RepairButton : ShipButton
{
	// the names of the repair items as the original's Repair says them (.SHIP-ITEM), by repair item: the hull, then each PD_PlayerShip.ShipSystem
	static readonly string[] c_itemNames = { "Hull", "Engines", "Sensors", "Communications", "Shields", "Missile launcher", "Laser cannon" };

	// what the No and Yes buttons answer: the engineer's question about the mineral, or whether to cease one repair and begin another
	enum Question
	{
		None,
		Mineral,
		Switch
	}

	static Question s_question = Question.None;

	// the repair item asked about (for the switch, the one to begin), and the buttons that were on the console then (Yes and No go back to them)
	static int s_askedItem = -1;
	static ButtonController.ButtonSet s_buttonSetBeforeQuestion = ButtonController.ButtonSet.Bridge;

	// the row the cursor is on, true while the stick is held to one side (one row per push), and true while the player is choosing
	int m_row;
	bool m_stickWasPushed;
	bool m_choosing;

	public override string GetLabel()
	{
		return "Repair";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		SpaceflightController.m_instance.m_messages.Clear();

		// the original's Repair begins with the DAMAGE display (ENGINEER in the decompiled original: (/REPAIR) calls (/DAMAGE) first)
		var damageDisplay = ShowDamageDisplay();

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

		// the cursor starts on the hull (the push that chose the button is not a step)
		m_row = PD_PlayerShip.c_hullRepairItem;
		m_stickWasPushed = true;
		m_choosing = true;

		if ( damageDisplay != null )
		{
			damageDisplay.SetCursor( m_row );
		}

		ShowRow( m_row );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );

		// the stick and the fire button are Repair's until the player has chosen
		return true;
	}

	public override bool Update()
	{
		if ( !m_choosing )
		{
			return false;
		}

		var input = InputController.m_instance;

		// the fire button chooses the row under the cursor, and ends the choice whatever the row (the original's loop ends on ?TRIG)
		if ( input.m_submit )
		{
			input.Debounce();

			ChooseRow( m_row );

			return true;
		}

		// up and down go through the rows, round from the last to the first (the original's NCRS, MOD 7)
		var step = input.m_south ? 1 : ( input.m_north ? -1 : 0 );

		if ( step == 0 )
		{
			m_stickWasPushed = false;
		}
		else if ( !m_stickWasPushed )
		{
			m_stickWasPushed = true;

			m_row = ( m_row + step + c_itemNames.Length ) % c_itemNames.Length;

			SoundController.m_instance.PlaySound( SoundController.Sound.Click );

			var damageDisplay = SpaceflightController.m_instance.m_displayController.m_damageDisplay;

			if ( damageDisplay != null )
			{
				damageDisplay.SetCursor( m_row );
			}

			ShowRow( m_row );
		}

		return true;
	}

	// the row Repair's cursor is on (-1 when the player is not choosing)
	public int GetChosenRow()
	{
		return m_choosing ? m_row : -1;
	}

	// the fire button on a row (the probe calls it too): the choice ends, and the engineer is told what to repair
	public void ChooseRow( int item )
	{
		m_choosing = false;

		var displayController = SpaceflightController.m_instance.m_displayController;

		if ( displayController.m_damageDisplay != null )
		{
			displayController.m_damageDisplay.SetCursor( -1 );
		}

		SpaceflightController.m_instance.m_buttonController.DeactivateButton();

		Choose( item );
	}

	// shows the DAMAGE display (null if there is none)
	static DamageDisplay ShowDamageDisplay()
	{
		var displayController = SpaceflightController.m_instance.m_displayController;

		if ( displayController.m_damageDisplay != null )
		{
			displayController.ChangeDisplay( displayController.m_damageDisplay );
		}

		return displayController.m_damageDisplay;
	}

	// the name of a repair item as the original's Repair says it
	public static string GetItemName( int item )
	{
		return ( ( item >= 0 ) && ( item < c_itemNames.Length ) ) ? c_itemNames[ item ] : "?";
	}

	// what the text window says about the row under the cursor (the original's WF205): "ENGINES: SLIGHTLY DAMAGED", its efficiency, whether it works at all (not
	// for the hull), how long a complete repair takes, and whether it is being repaired - "MISSILE LAUNCHER: NONE" for what the ship does not have
	static void ShowRow( int item )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();

		if ( !ship.HasRepairItem( item ) )
		{
			messages.AddText( "<color=white>" + GetItemName( item ) + ": None</color>" );

			return;
		}

		var health = ship.GetRepairItemHealth( item );
		var text = GetItemName( item ) + ": " + GetDamageWords( health ) + "\nEfficiency: " + health + "%";

		if ( item != PD_PlayerShip.c_hullRepairItem )
		{
			text += "\nCurrent functionality: " + ( ship.IsRepairItemOperative( item ) ? "Operative" : "Inoperative" );
		}

		if ( health < 100 )
		{
			text += "\nEstimated time for complete repairs:\n" + FormatRepairTime( GetRepairSeconds( ship, item ) );
		}

		if ( ship.m_repairsAreUnderWay && ( ship.m_repairItem == item ) )
		{
			text += "\nCurrently under repair.";
		}

		messages.AddText( "<color=white>" + text + "</color>" );
	}

	// how damaged a repair item is, in the original's words (ENGINEER, WEEA0): destroyed at 0, then critically, heavily, moderately, slightly and minimally
	// damaged from 1, 20, 40, 70 and 90, and not damaged at 100
	public static string GetDamageWords( int health )
	{
		if ( health <= 0 )
		{
			return "Destroyed";
		}

		if ( health < 20 )
		{
			return "Critically damaged";
		}

		if ( health < 40 )
		{
			return "Heavily damaged";
		}

		if ( health < 70 )
		{
			return "Moderately damaged";
		}

		if ( health < 90 )
		{
			return "Slightly damaged";
		}

		if ( health < 100 )
		{
			return "Minimally damaged";
		}

		return "Not damaged";
	}

	// how many seconds of play a complete repair of an item takes (the hull by its armor points, a system at the same pace) - 0 if nobody can do it
	static float GetRepairSeconds( PD_PlayerShip ship, int item )
	{
		var rate = ship.GetRepairRate();
		var maximumArmor = ship.GetMaximumArmorPoints();

		if ( ( rate <= 0.0f ) || ( maximumArmor <= 0 ) )
		{
			return 0.0f;
		}

		// the part already done counts for the repair under way
		var progress = ( ship.m_repairsAreUnderWay && ( ship.m_repairItem == item ) ) ? ship.m_repairProgress : 0.0f;

		if ( item == PD_PlayerShip.c_hullRepairItem )
		{
			return Mathf.Max( 0.0f, maximumArmor - ship.m_armorPoints - progress ) / rate;
		}

		return Mathf.Max( 0.0f, 100 - ship.GetRepairItemHealth( item ) - progress ) / ( rate * 100.0f / maximumArmor );
	}

	// a repair's time in the game's time, as the original writes it (ENGINEER, WF0FF): minutes under an hour, else hours under a day, else days, rounded to the
	// nearest, with an S for more than one - "2 HOURS."
	public static string FormatRepairTime( float secondsOfPlay )
	{
		var minutes = Mathf.Max( 1, Mathf.RoundToInt( secondsOfPlay * PD_General.c_gameSecondsPerSecond / 60.0f ) );

		int count;
		string unit;

		if ( minutes < 60 )
		{
			count = minutes;
			unit = "minute";
		}
		else if ( minutes < 24 * 60 )
		{
			count = Mathf.Max( 1, Mathf.RoundToInt( minutes / 60.0f ) );
			unit = "hour";
		}
		else
		{
			count = Mathf.Max( 1, Mathf.RoundToInt( minutes / ( 24.0f * 60.0f ) ) );
			unit = "day";
		}

		return count + " " + unit + ( ( count > 1 ) ? "s" : "" ) + ".";
	}

	// the fire button on a row (the original's (/REPAIR) after XYSCAN): nothing for what the ship does not have, "NO REPAIRS NEEDED." for what is whole,
	// "DAMAGED BEYOND REPAIR." for what is destroyed, "ALREADY UNDERGOING REPAIRS." for the repair under way, the question whether to cease it for another,
	// else the repair begins
	static void Choose( int item )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;

		if ( !ship.HasRepairItem( item ) )
		{
			return;
		}

		var health = ship.GetRepairItemHealth( item );

		if ( health >= 100 )
		{
			messages.Clear();
			messages.AddText( "<color=white>" + GetItemName( item ) + ": No repairs needed.</color>" );

			return;
		}

		if ( health <= 0 )
		{
			messages.Clear();
			messages.AddText( "<color=white>" + GetItemName( item ) + ":\nDamaged beyond repair.</color>" );

			return;
		}

		if ( ship.m_repairsAreUnderWay )
		{
			messages.Clear();

			if ( ship.m_repairItem == item )
			{
				messages.AddText( "<color=white>" + GetItemName( item ) + ":\nAlready undergoing repairs.</color>" );

				return;
			}

			// the original's question ([N Y]: No and Yes on the console)
			messages.AddText( "<color=white>Cease repairs on " + GetItemName( ship.m_repairItem ).ToLower() + "\nand begin repairs on " + GetItemName( item ).ToLower() + "?</color>" );

			Ask( Question.Switch, item );

			return;
		}

		messages.Clear();

		BeginRepairs( item );
	}

	// the engineer begins on a repair item (the original's "BEGINNING REPAIRS ON ..."), and asks for its mineral
	static void BeginRepairs( int item )
	{
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Beginning repairs on " + GetItemName( item ).ToLower() + ".</color>" );

		AskForRepair( item );
	}

	// puts No and Yes on the console for a question
	static void Ask( Question question, int item )
	{
		var buttonController = SpaceflightController.m_instance.m_buttonController;

		s_question = question;
		s_askedItem = item;

		if ( buttonController.GetCurrentButtonSet() != ButtonController.ButtonSet.RepairMineral )
		{
			s_buttonSetBeforeQuestion = buttonController.GetCurrentButtonSet();
		}

		buttonController.ChangeButtonSet( ButtonController.ButtonSet.RepairMineral );
	}

	// the engineer asks for the mineral a repair needs, as the original's does (SS Repair: "Captain, we need 2 cubic meters of Molybdenum for repairs. Shall I take
	// this from cargo?" - and with none in the hold "... Ceasing repair"; "CUBIC METER" with an S for more than one, REPAIR-OV WF26C)
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
		var cubicMeters = needed / 10;

		messages.AddText( "<color=white>Captain, we need " + cubicMeters + " cubic meter" + ( ( cubicMeters != 1 ) ? "s" : "" ) + " of " + mineral + " for repairs.</color>" );

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
		Ask( Question.Mineral, item );
	}

	// the answer to the question on the console: for the mineral, yes takes it from the hold and starts the repair and no stops; for the switch, yes ceases the
	// repair under way and begins the other ("CEASING REPAIRS ON ..."), no keeps on with it ("CONTINUING REPAIRS ON ...")
	public static void Answer( bool yes )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;
		var question = s_question;
		var item = s_askedItem;

		s_question = Question.None;
		s_askedItem = -1;

		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( s_buttonSetBeforeQuestion );

		if ( question == Question.Switch )
		{
			messages.Clear();

			if ( !yes || ( item < 0 ) )
			{
				messages.AddText( "<color=white>Continuing repairs on " + GetItemName( ship.m_repairItem ).ToLower() + ".</color>" );

				SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

				return;
			}

			messages.AddText( "<color=white>Ceasing repairs on " + GetItemName( ship.m_repairItem ).ToLower() + ".</color>" );

			ship.StopRepairs();

			BeginRepairs( item );

			return;
		}

		var mineralId = ( item < 0 ) ? -1 : ship.GetRepairMineralId( item );
		var needed = ( item < 0 ) ? 0 : ship.GetRepairMineralNeeded( item );
		var inTheHold = ( mineralId < 0 ) ? null : ship.m_elementStorage.Find( mineralId );

		if ( !yes || ( needed <= 0 ) || ( inTheHold == null ) || ( inTheHold.m_volume < needed ) )
		{
			messages.AddText( "<color=white>Ceasing repair</color>" );

			SoundController.m_instance.PlaySound( SoundController.Sound.Deactivate );

			return;
		}

		ship.RemoveElement( mineralId, needed );
		ship.StartRepairs( item );

		messages.AddText( "<color=white>Continuing repair</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Activate );
	}
}
