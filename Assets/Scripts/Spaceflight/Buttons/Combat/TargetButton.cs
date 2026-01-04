
/// <summary>
/// Button to select a target for combat.
/// </summary>
public class TargetButton : ShipButton
{
	public override string GetLabel()
	{
		return "Target";
	}

	public override bool Execute()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// only works in encounter
		if ( playerData.m_general.m_location != PD_General.Location.Encounter )
		{
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// get the encounter
		var encounter = SpaceflightController.m_instance.m_encounter;
		
		// check if encounter data is valid
		if ( encounter.m_pdEncounter == null )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>No encounter data available.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		var alienShipList = encounter.m_pdEncounter.GetAlienShipList();

		// check if alien ship list is valid
		if ( alienShipList == null || alienShipList.Length == 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>No targets in range.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// get combat controller
		var combatController = CombatController.m_instance ?? SpaceflightController.m_instance.m_combatController;
		if ( combatController == null )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>Combat system unavailable.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// find next valid target
		int currentTarget = combatController.GetTargetIndex();
		int newTarget = -1;

		// cycle through targets
		for ( int i = 0; i < alienShipList.Length; i++ )
		{
			int checkIndex = ( currentTarget + 1 + i ) % alienShipList.Length;
			var alienShip = alienShipList[ checkIndex ];

			// skip dead ships
			if ( alienShip.m_isDead )
			{
				continue;
			}

			// skip ships not in encounter
			if ( !alienShip.m_addedToEncounter )
			{
				continue;
			}

			newTarget = checkIndex;
			break;
		}

		if ( newTarget < 0 )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>No valid targets.</color>" );
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );
			return false;
		}

		// set the target
		combatController.SetTarget( newTarget );

		// get target info
		var gameData = DataController.m_instance.m_gameData;
		var targetShip = alienShipList[ newTarget ];
		var vessel = gameData.m_vesselList[ targetShip.m_vesselId ];

		// calculate distance
		var distance = UnityEngine.Vector3.Distance( playerData.m_general.m_coordinates, targetShip.m_coordinates );

		SpaceflightController.m_instance.m_messages.AddText( $"<color=white>Target: {vessel.m_name} (Range: {distance:F0})</color>" );
		SoundController.m_instance.PlaySound( SoundController.Sound.Beep );

		return true;
	}
}
