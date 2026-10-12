
using UnityEngine;

using System.Collections.Generic;
using System.Text.RegularExpressions;

public class Encounter : MonoBehaviour
{
	// the text of the comm that stands in when the game data has nothing to say for a race, subject and stance
	const string c_noCommFoundText = "ERROR";

	// how many numbers a veloxi drone asks the player to confirm before it lets the ship into orbit (the original's number is not known - the project owner chose 3)
	const int c_droneNumberCount = 3;

	// how long the "permission to orbit granted" message stays up before the ship is back in orbit (in seconds)
	const float c_returnToOrbitDelay = 3.0f;

	// the speed the alien ships move at
	public float m_alienShipSpeed;

	// the rate the alien ships turn at
	public float m_alienShipTurnRate;

	// the camera dolly speed
	public float m_cameraDollySpeed;

	// how often to update the target coordinates
	public float m_targetCoordinateUpdateFrequency;

	// the main encounter map
	public GameObject m_main;

	// an encounter that began in orbit is drawn over its planet (the 1984 notes, gameflow.txt: "If an encounter occurs when a ship is in orbit the ship drops to a
	// tactical map which has a planet in it ... Ships and weapons move OVER the planet, and it scrolls around and does not affect anything"; the later step of
	// ruling 5) - the port draws the planet's own surface, the orbit view's sphere with its material, as large as the planet's mass makes it there; how large and
	// how deep below the ships it lies are the port's
	GameObject m_planetBackdrop;

	const float c_backdropRadiusPerScale = 8.0f;
	const float c_backdropDepth = 600.0f;

	// the race models
	public GameObject[] m_alienRaceModelList;

	// alien ship models (need 8)
	public GameObject[] m_alienShipModelList;

	// template models that we will clone as needed (need 23)
	public GameObject[] m_alienShipModelTemplate;

	// debris template models - matches m_alienShipModelTemplate indices
	public GameObject[] m_alienShipDebrisTemplate;

	// the current encounter data (both player and game)
	public PD_Encounter m_pdEncounter;
	public GD_Encounter m_gdEncounter;

	// alien ship data for each model slot (same index as m_alienShipModelList - null if the slot is not in use)
	PD_AlienShip[] m_alienShipList;

	// the model slot of each alien ship (same index as the encounter's alien ship list - negative if the ship has no model)
	int[] m_alienShipModelSlotList;

	// did we just enter this encounter from hyperspace?
	bool m_justEntered;

	// counts down to the return into orbit after a veloxi drone has let the ship in (negative while it has not)
	float m_returnToOrbitTimer = -1.0f;

	// current dolly distance
	float m_currentOffset;

	// whether the aliens' shields and weapons have been looked at since the encounter was shown, and what they were (a change is reported, as the original's <AUX>)
	bool m_shieldsAndWeaponsKnown;
	bool m_aliensHadShieldsUp;
	bool m_aliensWereArmed;

	// unity start
	void Start()
	{
		// turn off all of the alien ship model templates
		foreach ( var alienShipModelTemplate in m_alienShipModelTemplate )
		{
			// an empty slot has nothing to turn off (ResetAlienShipModels reports it when a ship needs it)
			if ( alienShipModelTemplate != null )
			{
				alienShipModelTemplate.SetActive( false );
			}
		}

		// turn off all of the debris model templates
		if ( m_alienShipDebrisTemplate != null )
		{
			foreach ( var debrisTemplate in m_alienShipDebrisTemplate )
			{
				if ( debrisTemplate != null )
				{
					debrisTemplate.SetActive( false );
				}
			}
		}
	}

	/// <summary>
	/// Spawn debris model at the location of a destroyed ship.
	/// </summary>
	public void SpawnDebrisForShip( int alienIndex, int vesselId, Vector3 position )
	{
		// get the model container for this alien ship (its model slot is not the same as its index in the alien ship list)
		var alienShipModel = GetAlienShipModel( alienIndex );

		if ( alienShipModel == null )
		{
			Debug.Log( $"SpawnDebrisForShip: Alien ship {alienIndex} has no model" );
			return;
		}

		// measure the destroyed ship before it is removed, so that its debris can be given the same size
		var shipSize = GetLongestSide( alienShipModel.transform, alienShipModel );

		// and keep its materials, for its wreck (the debris models were imported with blank ones)
		var shipMaterials = ShipWreck.GatherShipMaterials( alienShipModel );

		// clear any existing children (the destroyed ship model)
		Tools.DestroyChildrenOf( alienShipModel );

		// check if we have a debris template for this vessel
		GameObject debrisTemplate = null;

		if ( ( m_alienShipDebrisTemplate != null ) && ( vesselId >= 0 ) && ( vesselId < m_alienShipDebrisTemplate.Length ) )
		{
			debrisTemplate = m_alienShipDebrisTemplate[ vesselId ];
		}

		if ( debrisTemplate == null )
		{
			// no - the ship is gone and there is no wreckage to show, so hide its model
			Debug.Log( $"SpawnDebrisForShip: No debris template for vesselId {vesselId}" );

			alienShipModel.SetActive( false );

			return;
		}

		// spawn the debris model as a child
		var debrisInstance = Instantiate( debrisTemplate, alienShipModel.transform );
		debrisInstance.transform.localPosition = Vector3.zero;

		// keep the template's own rotation: an imported model's root can carry the turn that puts it the right way up
		debrisInstance.transform.localRotation = debrisTemplate.transform.localRotation;
		
		// start from the template's local scale
		debrisInstance.transform.localScale = debrisTemplate.transform.localScale;

		// then fit the debris to the size of the ship it replaces (the debris models are imported at scales of their own)
		var debrisSize = GetLongestSide( alienShipModel.transform, debrisInstance );

		if ( ( shipSize > 0.0f ) && ( debrisSize > 0.0f ) )
		{
			debrisInstance.transform.localScale *= shipSize / debrisSize;
		}

		debrisInstance.SetActive( true );

		// drawn with the ship's own materials, scorched, with embers that cool
		ShipWreck.MakeWreck( debrisInstance, shipMaterials );
		
		Debug.Log( $"SpawnDebrisForShip: Spawned debris for vesselId {vesselId} at {position} with scale {debrisInstance.transform.localScale}" );

		// position the container at the debris location
		alienShipModel.transform.position = position;
		alienShipModel.SetActive( true );

		// add slow tumbling rotation to the debris
		var tumble = debrisInstance.AddComponent<DebrisTumble>();
		tumble.m_rotationSpeed = new Vector3( Random.Range( -10f, 10f ), Random.Range( -10f, 10f ), Random.Range( -10f, 10f ) );
	}

	/// <summary>
	/// The longest side of the meshes under model, measured along the axes of container (zero if there are none).
	/// </summary>
	static float GetLongestSide( Transform container, GameObject model )
	{
		var toContainer = container.worldToLocalMatrix;
		var bounds = new Bounds();
		var hasBounds = false;

		foreach ( var meshFilter in model.GetComponentsInChildren<MeshFilter>( true ) )
		{
			if ( meshFilter.sharedMesh == null )
			{
				continue;
			}

			// put the eight corners of the mesh's own bounds into the container's frame
			var matrix = toContainer * meshFilter.transform.localToWorldMatrix;
			var meshBounds = meshFilter.sharedMesh.bounds;

			for ( var corner = 0; corner < 8; corner++ )
			{
				var sign = new Vector3( ( ( corner & 1 ) == 0 ) ? -1.0f : 1.0f, ( ( corner & 2 ) == 0 ) ? -1.0f : 1.0f, ( ( corner & 4 ) == 0 ) ? -1.0f : 1.0f );
				var point = matrix.MultiplyPoint3x4( meshBounds.center + Vector3.Scale( meshBounds.extents, sign ) );

				if ( hasBounds )
				{
					bounds.Encapsulate( point );
				}
				else
				{
					bounds = new Bounds( point, Vector3.zero );
					hasBounds = true;
				}
			}
		}

		return hasBounds ? Mathf.Max( bounds.size.x, bounds.size.y, bounds.size.z ) : 0.0f;
	}

	/// <summary>
	/// Get the model for an alien ship, by its index in the encounter's alien ship list.
	/// Returns null if the ship has no model (not added to the encounter yet, or already dead when the models were set up).
	/// </summary>
	public GameObject GetAlienShipModel( int alienIndex )
	{
		// do we know the model slots yet and is this a valid alien ship index?
		if ( ( m_alienShipModelSlotList == null ) || ( alienIndex < 0 ) || ( alienIndex >= m_alienShipModelSlotList.Length ) )
		{
			// no
			return null;
		}

		// get the model slot for this alien ship
		var modelSlot = m_alienShipModelSlotList[ alienIndex ];

		// does this alien ship have a model?
		if ( ( modelSlot < 0 ) || ( modelSlot >= m_alienShipModelList.Length ) )
		{
			// no
			return null;
		}

		return m_alienShipModelList[ modelSlot ];
	}

	// unity update
	void Update()
	{
		// don't do anything if the game is paused
		if ( SpaceflightController.m_instance.m_gameIsPaused )
		{
			return;
		}

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// take aboard the debris of a wreck the ship flies over
		UpdateSalvage();

		// check if all enemy ships are destroyed
		if ( AllEnemyShipsDestroyed() )
		{
			// show victory message (only once)
			HandleVictory( playerData );
			
			// still allow player to leave - check if player has left the encounter area
			if ( playerData.m_general.m_coordinates.magnitude >= 4096.0f )
			{
				LeaveEncounter();
			}
			return;
		}

		// aliens that have been fired on stay hostile until the player leaves the encounter
		if ( m_pdEncounter.m_attackedByPlayer )
		{
			m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;
		}

		// update race encounter
		switch ( m_gdEncounter.m_race )
		{
			case GameData.Race.Elowan:

				UpdateElowanEncounter();
				break;

			case GameData.Race.Gazurtoid:

				UpdateGazurtoidEncounter();
				break;

			case GameData.Race.Mechan:

				UpdateMechanEncounter();
				break;

			case GameData.Race.Mysterion:

				UpdateMysterionEncounter();
				break;

			case GameData.Race.NomadProbe:

				UpdateNomadProbeEncounter();
				break;

			case GameData.Race.Spemin:

				UpdateSpeminEncounter();
				break;

			case GameData.Race.Thrynn:

				UpdateThrynnEncounter();
				break;

			case GameData.Race.Velox:

				UpdateVeloxEncounter();
				break;

			case GameData.Race.VeloxProbe:

				// a drone that has let the ship into orbit ends the encounter itself
				if ( UpdateVeloxProbeEncounter() )
				{
					return;
				}

				break;

			case GameData.Race.Minstrel:

				UpdateMinstrelEncounter();
				break;

			case GameData.Race.Uhlek:

				UpdateUhlekEncounter();
				break;

			case GameData.Race.NoahDerelict:

				// the derelict's distress beacon, and its recorded message once the ship answers (STRINFO 2.11)
				DefaultEncounterUpdate();
				break;

			default:

				// everyone else has nothing to say, but they do shoot back once they have been fired on
				UpdateAlienCombat();
				break;
		}

		// tell the captain when the aliens raise or lower their shields, or arm or disarm their weapons
		UpdateShieldsAndWeaponsNews();

		// and let the aliens' mood follow the player's shields and weapons
		UpdatePlayerShieldsAndWeapons();

		// do we want to update the ships (both alien and player)?
		if ( !m_pdEncounter.m_connected )
		{
			// update the position and rotation of the active alien ship models
			for ( var alienShipIndex = 0; alienShipIndex < m_alienShipModelList.Length; alienShipIndex++ )
			{
				var alienShipModel = m_alienShipModelList[ alienShipIndex ];

				if ( alienShipModel.activeInHierarchy )
				{
					var alienShip = m_alienShipList[ alienShipIndex ];

					// skip this model if there is no alien ship in its slot (this should never happen)
					if ( alienShip == null )
					{
						continue;
					}

					// destroyed ships don't fly any more (the wreckage stays where the ship was destroyed)
					if ( alienShip.m_isDead )
					{
						continue;
					}

					var vessel = gameData.m_vesselList[ alienShip.m_vesselId ];

					switch ( vessel.m_race )
					{
						case GameData.Race.Mechan:
							UpdateMechanAlienShip( alienShip, alienShipModel );
							break;

						default:
							DefaultAlienShipUpdate( alienShip, alienShipModel );
							break;
					}
				}
			}

			// finalize alien ships and camera transform
			FinalizeAlienShips();

			// has the player left the encounter?
			if ( playerData.m_general.m_coordinates.magnitude >= 4096.0f )
			{
				// yes - go back to where the encounter began
				LeaveEncounter();
			}
		}
	}

	// the player has flown out of the encounter - go back to where the ship was when the encounter began
	// the Crystal Pearl has warped the ship out of the encounter - it leaves the encounter the way it would by flying out of it
	public void WarpOut()
	{
		LeaveEncounter();
	}

	void LeaveEncounter()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// calculate the normalized exit direction vector
		var exitDirection = Vector3.Normalize( playerData.m_general.m_coordinates );

		// where did the encounter begin?
		switch ( playerData.m_general.m_lastLocation )
		{
			case PD_General.Location.Hyperspace:

				// in hyperspace - come out of it away from the aliens, on the side the player left on
				playerData.m_general.m_lastHyperspaceCoordinates += exitDirection * SpaceflightController.m_instance.m_encounterRange * 1.25f;

				SpaceflightController.m_instance.SwitchLocation( PD_General.Location.Hyperspace );

				break;

			case PD_General.Location.InOrbit:

				// in orbit around a planet - did the aliens let the ship into orbit (a veloxi drone that was answered correctly)?
				if ( SpaceflightController.m_instance.HasOrbitPermission( m_pdEncounter.m_encounterId ) )
				{
					// yes - back into orbit (the encounter does not begin again while the ship stays in this star system)
					SpaceflightController.m_instance.SwitchLocation( PD_General.Location.InOrbit );
				}
				else
				{
					// no - an encounter from orbit ends at the level of the star system, as in the original game
					// the ship is where it was when it went into orbit, next to the planet (going back into orbit ourselves would begin the same encounter again at once)
					SpaceflightController.m_instance.SwitchLocation( PD_General.Location.StarSystem );
				}

				break;

			default:

				// in a star system - come out of it away from the aliens, on the side the player left on
				playerData.m_general.m_lastStarSystemCoordinates += exitDirection * SpaceflightController.m_instance.m_encounterRange * 1.25f;

				SpaceflightController.m_instance.SwitchLocation( playerData.m_general.m_lastLocation );

				break;
		}

		// say so, as the original does ("LEAVING ENCOUNTER..." and "...ENCOUNTER TERMINATED", made in STARB.COM as "LEAVING STARPORT..." and "...STARPORT CLEAR" are)
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Leaving encounter...</color>" );
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>...Encounter terminated.</color>" );
	}

	// check if all enemy ships in the encounter are destroyed
	bool AllEnemyShipsDestroyed()
	{
		if ( m_pdEncounter == null )
		{
			return false;
		}

		var alienShipList = m_pdEncounter.GetAlienShipList();
		if ( alienShipList == null )
		{
			return false;
		}

		// check if at least one ship was added to the encounter
		bool anyShipWasAdded = false;

		foreach ( var alienShip in alienShipList )
		{
			if ( alienShip.m_addedToEncounter )
			{
				anyShipWasAdded = true;

				// if any added ship is still alive, return false
				if ( !alienShip.m_isDead )
				{
					return false;
				}
			}
		}

		// return true only if ships were added and all are dead
		return anyShipWasAdded;
	}

	// flag to prevent multiple victory messages
	bool m_victoryHandled = false;

	// the debris a destroyed ship leaves is in the original's vessel records ("Endurium Debris" and "Debris 1" to "Debris 3": GD_Vessel.m_enduriumVolume and
	// m_elementVolumeA to C, of the elements m_elementIdA to C), and the Thrynn tell the crew that wrecked Elowan ships "you could salvage" for their plutonium - the
	// ship takes a wreck's debris aboard by flying over it, once, as much of it as the hold has room for (the way it is collected is the port's choice: the
	// sources do not say)
	const float c_salvageRange = 64.0f;

	void UpdateSalvage()
	{
		if ( ( m_pdEncounter == null ) || m_pdEncounter.m_connected )
		{
			return;
		}

		var gameData = DataController.m_instance.m_gameData;
		var shipCoordinates = DataController.m_instance.m_playerData.m_general.m_coordinates;

		foreach ( var alienShip in m_pdEncounter.GetAlienShipList() )
		{
			if ( !alienShip.m_addedToEncounter || !alienShip.m_isDead || alienShip.m_salvaged )
			{
				continue;
			}

			if ( Vector3.Distance( alienShip.m_coordinates, shipCoordinates ) > c_salvageRange )
			{
				continue;
			}

			alienShip.m_salvaged = true;

			if ( ( alienShip.m_vesselId >= 0 ) && ( alienShip.m_vesselId < gameData.m_vesselList.Length ) )
			{
				SalvageWreck( gameData.m_vesselList[ alienShip.m_vesselId ] );
			}
		}
	}

	// takes the debris of a vessel's wreck aboard, and says what was taken
	void SalvageWreck( GD_Vessel vessel )
	{
		var gameData = DataController.m_instance.m_gameData;
		var taken = new List<string>();
		var hadDebris = false;

		hadDebris |= TakeDebris( gameData.m_misc.m_enduriumElementId, vessel.m_enduriumVolume, taken );
		hadDebris |= TakeDebris( vessel.m_elementIdA, vessel.m_elementVolumeA, taken );
		hadDebris |= TakeDebris( vessel.m_elementIdB, vessel.m_elementVolumeB, taken );
		hadDebris |= TakeDebris( vessel.m_elementIdC, vessel.m_elementVolumeC, taken );

		if ( !hadDebris )
		{
			return;
		}

		var messages = SpaceflightController.m_instance.m_messages;

		if ( taken.Count == 0 )
		{
			messages.AddText( "<color=white>There is no room in the cargo hold for the debris, Captain.</color>" );

			return;
		}

		messages.AddText( "<color=green>Salvaged from the wreck: " + string.Join( ", ", taken ) + ".</color>" );
	}

	// takes debris of an element aboard, as much of it as the hold has room for, and adds what was taken to the list - returns whether there was any
	static bool TakeDebris( int elementId, int volume, List<string> taken )
	{
		var gameData = DataController.m_instance.m_gameData;
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		if ( ( volume <= 0 ) || ( elementId < 0 ) || ( elementId >= gameData.m_elementList.Length ) )
		{
			return false;
		}

		var amount = Mathf.Min( volume, Mathf.Max( 0, ship.GetRemainingVolume() ) );

		if ( amount > 0 )
		{
			ship.AddElement( elementId, amount );

			taken.Add( ( amount / 10 ) + "." + ( amount % 10 ) + " cubic meters of " + gameData.m_elementList[ elementId ].m_name );
		}

		return true;
	}

	// handle victory when all enemies are destroyed
	void HandleVictory( PlayerData playerData )
	{
		// only handle victory once per encounter
		if ( m_victoryHandled )
		{
			return;
		}

		m_victoryHandled = true;

		// show victory message - player can scan debris for salvage before leaving manually
		SpaceflightController.m_instance.m_messages.AddText( "<color=#00FF00>All enemy ships destroyed!</color>" );
		SpaceflightController.m_instance.m_messages.AddText( "<color=#FFFF00>Fly over the debris to salvage it, then leave when ready.</color>" );
	}

	// call this when the player fires on the aliens - they turn hostile and stay hostile until the player leaves the encounter
	public void PlayerAttacked()
	{
		// there is no one to turn hostile if we are not in an encounter
		if ( m_pdEncounter == null )
		{
			return;
		}

		// remember the attack (this is what keeps the aliens hostile for the rest of the encounter)
		m_pdEncounter.m_attackedByPlayer = true;

		// the aliens are hostile now
		m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;
	}

	// call this to let us know we have just entered an encounter
	public void JustEntered()
	{
		m_justEntered = true;
		m_victoryHandled = false;
	}

	// call this to hide the encounter stuff
	public void Hide()
	{
		if ( !gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Hiding the encounter location." );

		// take the missiles out of the air (the encounter is over - one that arrived later would still do its damage)
		if ( CombatController.m_instance != null )
		{
			CombatController.m_instance.ClearMissiles();
		}

		// hide the encounter objects
		gameObject.SetActive( false );

		// slide the message box back in
		SpaceflightController.m_instance.m_messages.SlideIn();
	}

	// call this to show the encounter stuff
	public void Show()
	{
		if ( gameObject.activeInHierarchy )
		{
			return;
		}

		Debug.Log( "Showing the encounter location." );

		// show the hyperspace objects
		gameObject.SetActive( true );

		// the aliens' shields and weapons are taken as they are when the encounter shows, without a word (the original's IAPWR sets them without one)
		m_shieldsAndWeaponsKnown = false;

		// and so are the player's, for the aliens' mood
		m_playerShieldsSeen = false;

		// show the main encounter location stuff
		m_main.SetActive( true );

		// reset the current offset
		m_currentOffset = 1024.0f;

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// move the ship to where we are in the encounter
		SpaceflightController.m_instance.m_playerShip.transform.position = playerData.m_general.m_coordinates = playerData.m_general.m_lastEncounterCoordinates;

		// calculate the new rotation of the player
		var newRotation = Quaternion.LookRotation( playerData.m_general.m_currentDirection, Vector3.up );

		// update the player rotation
		SpaceflightController.m_instance.m_playerShip.m_ship.rotation = newRotation;

		// unfreeze the player
		SpaceflightController.m_instance.m_playerShip.Unfreeze();

		// follow the player ship
		SpaceflightController.m_instance.m_playerCamera.SetCameraFollow( SpaceflightController.m_instance.m_playerShip.gameObject, Vector3.up * m_currentOffset );

		// fade in the map
		SpaceflightController.m_instance.m_viewport.StartFade( 1.0f, 2.0f );

		// show the status display
		SpaceflightController.m_instance.m_displayController.ChangeDisplay( SpaceflightController.m_instance.m_displayController.m_statusDisplay );

		// play the star system music track
		MusicController.m_instance.ChangeToTrack( MusicController.Track.Encounter );

		// get the current encounter id
		var encounterId = playerData.m_general.m_currentEncounterId;

		// get to the encounter game data
		m_gdEncounter = gameData.m_encounterList[ encounterId ];

		// find the encounter in the player data (Radar keeps the list sorted by distance, so we have to search by id)
		m_pdEncounter = playerData.FindEncounter( encounterId );

		if ( m_pdEncounter != null )
		{
			Debug.Log( $"[Encounter.Show] Found encounter {encounterId}, stance={m_pdEncounter.m_alienStance}" );

			// ensure the encounter is properly initialized
			if ( m_pdEncounter.GetAlienShipList() == null )
			{
				m_pdEncounter.Reset( encounterId );
			}
		}
		else
		{
			Debug.LogError( $"Invalid encounter ID: {encounterId}" );
		}

		// forget the combat target (a target picked in another encounter, or before leaving this one, does not carry over)
		// and make sure no missile from before is still in the air
		if ( CombatController.m_instance != null )
		{
			CombatController.m_instance.SetTarget( -1 );
			CombatController.m_instance.ClearMissiles();
		}

		// allocate array for alien ship list
		m_alienShipList = new PD_AlienShip[ m_alienShipModelList.Length ];

		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		// did we just enter this encounter from hyperspace?
		if ( m_justEntered )
		{
			Debug.Log( $"[Encounter.Show] m_justEntered=true, will reset stance to Neutral" );

			// yes - reset all of the alien ships
			foreach ( var alienShip in alienShipList )
			{
				alienShip.m_addedToEncounter = false;
			}

			// an encounter in orbit has no place of its own in the star system - it is at its planet, and that is the side its ships come from
			if ( m_pdEncounter.GetLocation() == PD_General.Location.InOrbit )
			{
				var planetController = SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId );

				if ( planetController != null )
				{
					m_pdEncounter.SetCoordinates( planetController.transform.localPosition );
				}
			}

			// add the first round of alien ships to the encounter
			AddAlienShips();

			// center the encounter coordinates on the player
			var encounterLocation = m_pdEncounter.GetLocation();

			if ( encounterLocation == PD_General.Location.Hyperspace )
			{
				m_pdEncounter.SetCoordinates( playerData.m_general.m_lastHyperspaceCoordinates );
			}
			else if ( encounterLocation == PD_General.Location.StarSystem )
			{
				m_pdEncounter.SetCoordinates( playerData.m_general.m_lastStarSystemCoordinates );
			}

			// reset the conversation
			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Scanners indicate unidentified object!</color>" );

			// reset the various encounter stuff
			m_pdEncounter.m_shownCommList.Clear();
			m_pdEncounter.m_connected = false;
			m_pdEncounter.m_disconnected = false;
			m_pdEncounter.m_connectionTimer = 0.0f;
			m_pdEncounter.m_aliensWantToConnect = false;
			m_pdEncounter.m_playerWantsToConnect = false;
			m_pdEncounter.m_lastSubjectFromPlayer = GD_Comm.Subject.None;
			m_pdEncounter.m_lastQuestionFromAliens = 0;
			m_pdEncounter.m_alreadyScanned = false;
			m_pdEncounter.m_scanTimer = 0.0f;
			m_pdEncounter.m_conversationTimer = 0.0f;
			// the races the shipped game starts friendly are friendly, the others neutral
			m_pdEncounter.m_alienStance = AlienPersonality.StartsFriendly( m_gdEncounter.m_race ) ? GD_Comm.Stance.Friendly : GD_Comm.Stance.Neutral;
			m_pdEncounter.m_playerStance = GD_Comm.Stance.Neutral;
			m_pdEncounter.m_questionLikelihood = 50;
			m_pdEncounter.m_numCorrectAnswers = 0;
			m_pdEncounter.m_mechan9NoHumansWarningDone = false;
			m_pdEncounter.m_attackedByPlayer = false;
			m_pdEncounter.m_homeworldWarningCount = 0;
			m_pdEncounter.m_homeworldWarningsGiven = false;
			m_pdEncounter.m_aliensAreCowardly = false;
			m_pdEncounter.m_specialQuestionAsked = false;
			m_pdEncounter.m_moodSet = false;
			m_pdEncounter.m_alienPhrasesLeft = 0;
			m_pdEncounter.m_playerPhrases = 0;
			m_pdEncounter.m_playerHailedFirst = false;
			m_pdEncounter.m_playerPostures = 0;
			m_pdEncounter.m_alienHails = 0;
			m_pdEncounter.m_rivalLineSaid = false;

			// initialize race encounter
			switch ( m_gdEncounter.m_race )
			{
				case GameData.Race.Elowan:

					InitializeElowanEncounter();
					break;

				case GameData.Race.Gazurtoid:

					InitializeGazurtoidEncounter();
					break;

				case GameData.Race.Mechan:

					InitializeMechanEncounter();
					break;

				case GameData.Race.Mysterion:

					InitializeMysterionEncounter();
					break;

				case GameData.Race.NomadProbe:

					InitializeNomadProbeEncounter();
					break;

				case GameData.Race.Spemin:

					InitializeSpeminEncounter();
					break;

				case GameData.Race.Thrynn:

					InitializeThrynnEncounter();
					break;

				case GameData.Race.Velox:

					InitializeVeloxEncounter();
					break;

				case GameData.Race.VeloxProbe:

					InitializeVeloxProbeEncounter();
					break;

				case GameData.Race.Minstrel:

					InitializeMinstrelEncounter();
					break;

				case GameData.Race.Uhlek:

					InitializeUhlekEncounter();
					break;

				case GameData.Race.NoahDerelict:

					// the derelict's conversation timers (without them its comm link ended the moment it opened)
					DefaultEncounterInitialize();
					break;
			}

			// a home fleet only warns the ship off - it asks no questions
			if ( IsHomeFleet() )
			{
				m_pdEncounter.m_questionLikelihood = 0;
			}

			// play the alarm sound
			SoundController.m_instance.PlaySound( SoundController.Sound.Alarm );

			// reset flag
			m_justEntered = false;
		}
		else
		{
			// resuming an encounter (e.g., from saved game)
			// check if any ships are already added to the encounter
			bool anyShipsAdded = false;
			foreach ( var alienShip in alienShipList )
			{
				if ( alienShip.m_addedToEncounter && !alienShip.m_isDead )
				{
					anyShipsAdded = true;
					break;
				}
			}

			// if no ships are in the encounter, add them now
			if ( !anyShipsAdded )
			{
				AddAlienShips();
			}
			else
			{
				// just reset the alien ship models
				ResetAlienShipModels();
			}
		}

		// are we are already connected to the aliens (from save game)?
		if ( m_pdEncounter.m_connected )
		{
			// yes - reconnect to the aliens immediately (reuse conversation string)
			Connect();
		}

		// an encounter that began in orbit is drawn over the planet
		ShowPlanetBackdrop();

		// slide the message box out
		SpaceflightController.m_instance.m_messages.SlideOut();

		// force an update now
		Update();
	}

	// puts the planet under an encounter that began in orbit, centred under the ship where the encounter began (it then scrolls with everything else), and takes
	// it away for any other encounter
	void ShowPlanetBackdrop()
	{
		var playerData = DataController.m_instance.m_playerData;
		var inOrbit = ( m_pdEncounter != null ) && ( m_pdEncounter.GetLocation() == PD_General.Location.InOrbit );
		var planetController = inOrbit ? SpaceflightController.m_instance.m_starSystem.GetPlanetController( playerData.m_general.m_currentPlanetId ) : null;
		var orbitModel = SpaceflightController.m_instance.m_inOrbit.m_planetModel;

		if ( ( planetController == null ) || ( planetController.m_planet == null ) || ( orbitModel == null ) )
		{
			if ( m_planetBackdrop != null )
			{
				m_planetBackdrop.SetActive( false );
			}

			return;
		}

		if ( m_planetBackdrop == null )
		{
			m_planetBackdrop = new GameObject( "Planet Backdrop" );
			m_planetBackdrop.layer = gameObject.layer;
			m_planetBackdrop.transform.SetParent( transform, false );

			var orbitMeshFilter = orbitModel.GetComponent<MeshFilter>();

			m_planetBackdrop.AddComponent<MeshFilter>().sharedMesh = ( orbitMeshFilter != null ) ? orbitMeshFilter.sharedMesh : null;
			m_planetBackdrop.AddComponent<MeshRenderer>();
		}

		m_planetBackdrop.GetComponent<MeshRenderer>().sharedMaterial = planetController.GetMaterial();
		m_planetBackdrop.SetActive( true );

		// (the orbit view's sphere has a radius of one; Show runs when the encounter is entered or loaded, so the planet is placed under the ship then)
		var radius = planetController.m_planet.GetScale().x * c_backdropRadiusPerScale;

		m_planetBackdrop.transform.localScale = Vector3.one * radius;
		m_planetBackdrop.transform.rotation = Quaternion.identity;
		m_planetBackdrop.transform.position = playerData.m_general.m_coordinates + Vector3.down * ( radius + c_backdropDepth );
	}

	// true while the planet is drawn under the encounter (for the probe)
	public bool IsPlanetBackdropShown()
	{
		return ( m_planetBackdrop != null ) && m_planetBackdrop.activeInHierarchy;
	}

	// adds a number of alien ships to the encounter - up to the maximum allowed by the encounter
	void AddAlienShips()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		// go through alien ship slots (up to the maximum allowed at once)
		for ( var alienShipIndex = 0; alienShipIndex < m_gdEncounter.m_maxNumShipsAtOnce; alienShipIndex++ )
		{
			// no - go through all of the alien ships in the encounter and add the next one
			foreach ( var alienShip in alienShipList )
			{
				// has this alien ship already been added to the encounter?
				if ( alienShip.m_addedToEncounter )
				{
					// yes - skip it
					continue;
				}

				// is this alien ship dead?
				if ( alienShip.m_isDead )
				{
					// yes - skip it
					continue;
				}

				// generate a random position inside of a unit circle
				var randomPosition = Random.insideUnitCircle;

				Vector3 coordinates;

				if ( m_justEntered )
				{
					// where the player was when the aliens arrived, in the space the encounter is in (this was always the place in hyperspace, which for an
					// encounter inside a star system has nothing to do with where the aliens are, so they appeared on any side but the one they came from)
					var playerCoordinates = ( m_pdEncounter.GetLocation() == PD_General.Location.Hyperspace ) ? playerData.m_general.m_lastHyperspaceCoordinates : playerData.m_general.m_lastStarSystemCoordinates;

					// the direction the aliens come from (if they are exactly where the player is there is no such direction, and they would appear on top of the player - so pick one)
					var directionOfApproach = Vector3.Normalize( m_pdEncounter.m_currentCoordinates - playerCoordinates );

					if ( directionOfApproach == Vector3.zero )
					{
						directionOfApproach = Vector3.forward;
					}

					// put alien ship in area approximately in the correct direction of approach
					coordinates = new Vector3( randomPosition.x, 0.0f, randomPosition.y ) * 256.0f + directionOfApproach * 4096.0f;
				}
				else
				{
					// put alien ship in a random position on a circle around the player
					coordinates = Vector3.Normalize( new Vector3( randomPosition.x, 0.0f, randomPosition.y ) ) * 4096.0f;
				}

				// make alien ship face the center of the encounter space
				var direction = -Vector3.Normalize( coordinates );

				// update the alien ship
				alienShip.m_coordinates = coordinates;
				alienShip.m_targetCoordinates = Vector3.zero;
				alienShip.m_currentDirection = direction;
				alienShip.m_lastDirection = direction;
				alienShip.m_currentBankingAngle = 0.0f;
				alienShip.m_timeSinceLastTargetCoordinateChange = (float) alienShipIndex / (float) m_gdEncounter.m_maxNumShipsAtOnce * m_targetCoordinateUpdateFrequency;
				alienShip.m_addedToEncounter = true;

				// we are done adding an alien ship to this slot
				break;
			}
		}

		// reset the alien ship models
		ResetAlienShipModels();
	}

	// update and position 3d models based on encounter alien ship data
	void ResetAlienShipModels()
	{
		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		// inactivate all of the alien ship models
		foreach ( var alienShipModel in m_alienShipModelList )
		{
			alienShipModel.SetActive( false );
		}

		// forget which alien ship was in each model slot
		for ( var modelSlot = 0; modelSlot < m_alienShipList.Length; modelSlot++ )
		{
			m_alienShipList[ modelSlot ] = null;
		}

		// no alien ship has a model slot yet
		m_alienShipModelSlotList = new int[ alienShipList.Length ];

		for ( var alienShipIndex = 0; alienShipIndex < m_alienShipModelSlotList.Length; alienShipIndex++ )
		{
			m_alienShipModelSlotList[ alienShipIndex ] = -1;
		}

		// start adding alien ship models (living ships are packed into the model slots, so a ship's model slot is not the same as its index in the alien ship list)
		var nextModelSlot = 0;

		// go through all of the alien ships in the encounter
		for ( var alienShipIndex = 0; alienShipIndex < alienShipList.Length; alienShipIndex++ )
		{
			var alienShip = alienShipList[ alienShipIndex ];

			// is this ship in the encounter and alive?
			if ( alienShip.m_addedToEncounter && !alienShip.m_isDead )
			{
				// stop if we have run out of model slots
				if ( nextModelSlot >= m_alienShipModelList.Length )
				{
					Debug.LogError( $"Not enough alien ship models for this encounter (have {m_alienShipModelList.Length})" );
					break;
				}

				// get the model we will update
				var alienShipModel = m_alienShipModelList[ nextModelSlot ];

				// remove old model
				Tools.DestroyChildrenOf( alienShipModel );

				// reset the transform of the model
				alienShipModel.transform.SetPositionAndRotation( Vector3.zero, Quaternion.identity );

				// clone the model
				if ( alienShip.m_vesselId < 0 || alienShip.m_vesselId >= m_alienShipModelTemplate.Length )
				{
					Debug.LogError( $"Invalid vesselId {alienShip.m_vesselId} for alien ship (template array length={m_alienShipModelTemplate.Length})" );
					continue;
				}

				var alienShipModelTemplate = m_alienShipModelTemplate[ alienShip.m_vesselId ];

				if ( alienShipModelTemplate == null )
				{
					Debug.LogError( $"No ship model template assigned for vesselId {alienShip.m_vesselId}" );
					continue;
				}

				var clonedAlienShipModel = Instantiate( alienShipModelTemplate, alienShipModelTemplate.transform.localPosition, alienShipModelTemplate.transform.localRotation, alienShipModel.transform );

				clonedAlienShipModel.SetActive( true );

				// show the model
				alienShipModel.SetActive( true );

				// remember the alien ship associated with this model
				m_alienShipList[ nextModelSlot ] = alienShip;

				// remember the model slot associated with this alien ship
				m_alienShipModelSlotList[ alienShipIndex ] = nextModelSlot;

				// next!
				nextModelSlot++;
			}
		}
	}

	// initialize encounter with elowan
	void InitializeElowanEncounter()
	{
		DefaultEncounterInitialize();

		// after Elan has been destroyed the Elowan are "now and forever mortal enemies" (their comm in the game data, STRINFO 2.4)
		if ( BlackEgg.IsElanDestroyed() )
		{
			m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;
		}
	}

	// initialize encounter with gazurtoid
	void InitializeGazurtoidEncounter()
	{
		DefaultEncounterInitialize();
	}

	// initialize encounter with mechans
	void InitializeMechanEncounter()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// set scan timer
		m_pdEncounter.m_scanTimer = Random.Range( 5.0f, 90.0f );

		// set statement timer
		m_pdEncounter.m_conversationTimer = Random.Range( 5.0f, 30.0f );

		// determine initial stance
		if ( playerData.m_general.m_mechan9Unlocked )
		{
			// player has unlocked the mechans
			m_pdEncounter.m_alienStance = GD_Comm.Stance.Friendly;

			// start at 0% question likelihood
			m_pdEncounter.m_questionLikelihood = 0;
		}
		else if ( playerData.m_crewAssignment.HasAtLeastOneHumanCrew() )
		{
			// neutral since there is at least one human crew member
			m_pdEncounter.m_alienStance = GD_Comm.Stance.Neutral;

			// set connection timer
			m_pdEncounter.m_connectionTimer = Random.Range( 120.0f, 300.0f );

			// start at 50% question likelihood
			m_pdEncounter.m_questionLikelihood = 50;
		}
		else
		{
			// hostile since there are no human crew members
			m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

			// set connection timer
			m_pdEncounter.m_connectionTimer = Random.Range( 60.0f, 120.0f );

			// start at 0% question likelihood
			m_pdEncounter.m_questionLikelihood = 0;
		}

		// mechans have not warned the player yet
		m_pdEncounter.m_mechan9NoHumansWarningDone = false;
	}

	// initialize encounter with mysterion
	void InitializeMysterionEncounter()
	{
		DefaultEncounterInitialize();
	}

	// initialize encounter with nomad probe
	void InitializeNomadProbeEncounter()
	{
		DefaultEncounterInitialize();
	}

	// initialize encounter with spemin
	void InitializeSpeminEncounter()
	{
		DefaultEncounterInitialize();
	}

	// initialize encounter with thrynn
	void InitializeThrynnEncounter()
	{
		DefaultEncounterInitialize();
	}

	// initialize encounter with velox
	void InitializeVeloxEncounter()
	{
		DefaultEncounterInitialize();
	}

	// initialize encounter with velox probe
	void InitializeVeloxProbeEncounter()
	{
		DefaultEncounterInitialize();

		// the drone has not let the ship into orbit yet
		m_returnToOrbitTimer = -1.0f;
	}

	// initialize encounter with minstrel
	void InitializeMinstrelEncounter()
	{
		DefaultEncounterInitialize();
	}

	// initialize encounter with uhlek
	void InitializeUhlekEncounter()
	{
		// the uhlek attack on sight
		m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;
	}

	// the default encounter initialization
	void DefaultEncounterInitialize()
	{
		// set statement timer
		m_pdEncounter.m_conversationTimer = Random.Range( 5.0f, 30.0f );

		// set connection timer
		m_pdEncounter.m_connectionTimer = Random.Range( 120.0f, 300.0f );

		// start at 50% question likelihood
		m_pdEncounter.m_questionLikelihood = 50;
	}

	// update encounter with elowan
	void UpdateElowanEncounter()
	{
		// after Elan has been destroyed the Elowan say only what they think of the player, and then they attack
		if ( BlackEgg.IsElanDestroyed() )
		{
			UpdateElowanAfterElan();

			return;
		}

		// a Thrynn aboard: they say so in place of a greeting, and end the comm link (STRINFO 2.4: "we wouldst have no dealings with thee") - they do not attack
		if ( DataController.m_instance.m_playerData.m_crewAssignment.HasCrewOfRace( GameData.Race.Thrynn ) && !m_pdEncounter.m_attackedByPlayer )
		{
			UpdateElowanNoDealings();

			return;
		}

		// has the player answered their question about energy?
		if ( ( m_pdEncounter.m_lastQuestionFromAliens == c_elowanEnergyQuestionCommId ) && ( m_pdEncounter.m_lastSubjectFromPlayer == GD_Comm.Subject.Yes ) )
		{
			AnswerElowanEnergyQuestion();
		}

		UpdateMood();

		DefaultEncounterUpdate();
	}

	// the Elowan with a Thrynn in the crew: they say their one line for it in place of a hail, and the comm link is not open to the player again in the encounter
	void UpdateElowanNoDealings()
	{
		if ( !m_pdEncounter.m_disconnected )
		{
			m_pdEncounter.m_conversationTimer -= Time.deltaTime;

			if ( m_pdEncounter.m_conversationTimer <= 0.0f )
			{
				SayRaceComm( c_elowanThrynnAboardCommId );

				if ( m_pdEncounter.m_connected )
				{
					Disconnect();
				}
				else
				{
					m_pdEncounter.m_disconnected = true;
					m_pdEncounter.m_playerWantsToConnect = false;
					m_pdEncounter.m_aliensWantToConnect = false;
				}
			}
		}

		UpdateAlienCombat();
	}

	// what the Elowan and the Thrynn say of a crew that is friends with the other race (STRINFO 2.4 and 2.5): a crew member aboard, or a first comm link opened
	// with the other race
	const int c_elowanThrynnAboardCommId = 1;
	const int c_elowanThrynnFirstCommId = 2;
	const int c_thrynnElowanAboardCommId = 297;
	const int c_thrynnElowanFirstCommId = 298;

	// the rival race of the Elowan or the Thrynn (None for any other race)
	static GameData.Race GetRivalRace( GameData.Race race )
	{
		if ( race == GameData.Race.Elowan )
		{
			return GameData.Race.Thrynn;
		}

		if ( race == GameData.Race.Thrynn )
		{
			return GameData.Race.Elowan;
		}

		return GameData.Race.None;
	}

	// true if the crew is friends with the aliens' rival race: one of the rival race is aboard, or the crew opened a comm link with the rival race first
	bool IsFriendOfRival()
	{
		var rival = GetRivalRace( m_gdEncounter.m_race );

		if ( rival == GameData.Race.None )
		{
			return false;
		}

		var playerData = DataController.m_instance.m_playerData;

		return playerData.m_crewAssignment.HasCrewOfRace( rival ) || ( playerData.m_general.m_firstContactRace == rival );
	}

	// the line the Elowan or the Thrynn say once in an encounter, in place of a greeting, to a crew that is friends with their rival race - null if there is none
	GD_Comm FindRivalLine()
	{
		if ( m_pdEncounter.m_rivalLineSaid || !IsFriendOfRival() )
		{
			return null;
		}

		var rivalAboard = DataController.m_instance.m_playerData.m_crewAssignment.HasCrewOfRace( GetRivalRace( m_gdEncounter.m_race ) );

		if ( m_gdEncounter.m_race == GameData.Race.Elowan )
		{
			return FindRaceComm( rivalAboard ? c_elowanThrynnAboardCommId : c_elowanThrynnFirstCommId );
		}

		return FindRaceComm( rivalAboard ? c_thrynnElowanAboardCommId : c_thrynnElowanFirstCommId );
	}

	// the Elowan's question of their own, and their answers to a yes (STRINFO 2.4)
	const int c_elowanEnergyQuestionCommId = 6;
	const int c_elowanNeedNotDireCommId = 4;
	const int c_elowanEnergyGiftCommId = 5;

	// "answering yes when carrying less than 20 units of Endurium" gets "15 Arth units of fuel", with 20 or more the Elowan see through it - in tenths of a cubic meter
	// (an Arth unit is a cubic meter, as in the Starport's prices)
	const int c_elowanEnergyDireBelow = 200;
	const int c_elowanEnergyGift = 150;

	// a yes to "Is thine need for energy dire?": the Elowan scan the hold, and give 15 cubic meters of Endurium to a ship with less than 20 (as much of it as the hold
	// has room for) - a no gets nothing
	void AnswerElowanEnergyQuestion()
	{
		var gameData = DataController.m_instance.m_gameData;
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		var enduriumId = gameData.m_misc.m_enduriumElementId;
		var endurium = ( ship.m_elementStorage == null ) ? null : ship.m_elementStorage.Find( enduriumId );
		var held = ( endurium == null ) ? 0 : endurium.m_volume;

		if ( held >= c_elowanEnergyDireBelow )
		{
			SayRaceComm( c_elowanNeedNotDireCommId );

			return;
		}

		var gift = Mathf.Min( c_elowanEnergyGift, Mathf.Max( 0, ship.GetRemainingVolume() ) );

		if ( gift > 0 )
		{
			ship.AddElement( enduriumId, gift );
		}

		SayRaceComm( c_elowanEnergyGiftCommId );
	}

	// the comm of the Elowan for a ship that has destroyed Elan ("Thou hast done a deed most unthinkably foul... We are now and forever mortal enemies.")
	const int c_elanDestroyedCommId = 3;

	// the Elowan after Elan has been destroyed: hostile, they say their one comm in place of a hail, and the comm link is never open to the player again
	void UpdateElowanAfterElan()
	{
		m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

		// have they said it yet? (the comm link counts as ended once they have)
		if ( !m_pdEncounter.m_disconnected )
		{
			m_pdEncounter.m_conversationTimer -= Time.deltaTime;

			if ( m_pdEncounter.m_conversationTimer <= 0.0f )
			{
				GD_Comm elanComm = null;

				foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
				{
					if ( ( comm.m_id == c_elanDestroyedCommId ) && ( comm.m_race == GameData.Race.Elowan ) )
					{
						elanComm = comm;
					}
				}

				if ( elanComm != null )
				{
					AddComm( GD_Comm.Subject.Custom, false, elanComm );
				}

				m_pdEncounter.m_disconnected = true;
				m_pdEncounter.m_playerWantsToConnect = false;
				m_pdEncounter.m_aliensWantToConnect = false;
			}
		}

		UpdateAlienCombat();
	}

	// update encounter with gazurtoid
	void UpdateGazurtoidEncounter()
	{
		DefaultEncounterUpdate();
	}

	// update encounter with mechans
	void UpdateMechanEncounter()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// did we warn the player yet?
		if ( !m_pdEncounter.m_mechan9NoHumansWarningDone )
		{
			// no - did we scan the player?
			if ( m_pdEncounter.m_alreadyScanned )
			{
				// yes - did we do the scan at least 3 seconds ago?
				if ( m_pdEncounter.m_scanTimer < -3.0f )
				{
					// yes - is there at least one crew member?
					if ( playerData.m_crewAssignment.HasAtLeastOneHumanCrew() )
					{
						// yes - nothing to do
					}
					else
					{
						// no - show no humans warning message
						AddComm( GD_Comm.Subject.Custom, false );
					}

					// update flag
					m_pdEncounter.m_mechan9NoHumansWarningDone = true;
				}
			}
		}

		// did the mechans ask a question?
		if ( m_pdEncounter.m_lastQuestionFromAliens != 0 )
		{
			// yes - did the player answer?
			if ( m_pdEncounter.m_lastSubjectFromPlayer != GD_Comm.Subject.None )
			{
				// yes - get the correct answer
				GD_Comm.Subject correctAnswer = GD_Comm.Subject.Yes;

				switch ( m_pdEncounter.m_lastQuestionFromAliens )
				{
					case 101:
					case 102:
					case 103:
					case 105:
						correctAnswer = GD_Comm.Subject.No;
						break;
				}

				// did the player answer correctly?
				if ( m_pdEncounter.m_lastSubjectFromPlayer == correctAnswer )
				{
					// yes - add to the number of correct answers
					m_pdEncounter.m_numCorrectAnswers++;

					// did the player answer enough questions correctly? (mechans that have been fired on stay hostile until the player
					// leaves, whatever the answers are - without this they still unlocked mechan 9 for every later encounter)
					if ( ( m_pdEncounter.m_numCorrectAnswers == 5 ) && !m_pdEncounter.m_attackedByPlayer )
					{
						// yes - yay! we are are friends!
						playerData.m_general.m_mechan9Unlocked = true;

						// mechans are friendly now
						m_pdEncounter.m_alienStance = GD_Comm.Stance.Friendly;

						// no more questions from the mechans
						m_pdEncounter.m_questionLikelihood = 0;
					}
				}
				else
				{
					// no - oh boy mechans hate the player now
					m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

					// no more questions from the mechans
					m_pdEncounter.m_questionLikelihood = 0;

					// mechans want to terminate comms shortly
					m_pdEncounter.m_connectionTimer = 15.0f;
				}
			}
		}

		// do the default encounter stuff
		if ( DefaultEncounterUpdate() )
		{
			// mechans more likely to ask questions as time goes by to try and figure out who the player is
			if ( m_pdEncounter.m_alienStance == GD_Comm.Stance.Neutral )
			{
				m_pdEncounter.m_questionLikelihood += 25;
			}
		}
	}

	// update encounter with mysterion
	void UpdateMysterionEncounter()
	{
		DefaultEncounterUpdate();
	}

	// update encounter with nomad probe
	void UpdateNomadProbeEncounter()
	{
		DefaultEncounterUpdate();
	}

	// update encounter with spemin
	void UpdateSpeminEncounter()
	{
		UpdateCowardice();

		UpdateMood();

		DefaultEncounterUpdate();
	}

	// "after some of their ships are destroyed, the Spemin will act cowardly" (STRINFO 2.7), and so do the Thrynn and the Veloxi, the other races the game data
	// has surrender lines for (the owner's ruling of 2026-10-11): they stop fighting, even if the player goes on firing, they surrender (each surrender line
	// first, see FindComm), and they say their obsequious lines (the Spemin's "we lay down our powerful defensive screens" is bluster: their ships have no shields
	// in the game data) - a home fleet does not, it only warns
	void UpdateCowardice()
	{
		if ( !m_pdEncounter.m_aliensAreCowardly && !IsHomeFleet() && HaveLostSomeShips() )
		{
			m_pdEncounter.m_aliensAreCowardly = true;
		}

		if ( m_pdEncounter.m_aliensAreCowardly )
		{
			m_pdEncounter.m_alienStance = GD_Comm.Stance.Obsequious;
		}
	}

	// true once the aliens have lost at least half of the ships that have come into the encounter, and still have one - STRINFO's "some of their ships" as the port
	// reads it (two of three, one of two)
	bool HaveLostSomeShips()
	{
		var joined = 0;
		var lost = 0;

		foreach ( var alienShip in m_pdEncounter.GetAlienShipList() )
		{
			if ( !alienShip.m_addedToEncounter )
			{
				continue;
			}

			joined++;

			if ( alienShip.m_isDead )
			{
				lost++;
			}
		}

		return ( lost > 0 ) && ( lost < joined ) && ( lost * 2 >= joined );
	}

	// update encounter with thrynn
	void UpdateThrynnEncounter()
	{
		UpdateCowardice();

		UpdateThrynnTrade();

		UpdateMood();

		DefaultEncounterUpdate();
	}

	// the Thrynn's offers (STRINFO 2.5), their question of their own: to buy the artifact in the hold they pay the most for, at their prices (STRINFO 1.3.1,
	// GD_Artifact.m_thrynnPrice), or else the plutonium in the hold, or else to sell "a device that will make your ship invulnerable" - the Black Box - for 30
	// of the player's energy crystals - and their two answers, "You do not have enough. Do not try to deceive us." and "Very well."
	const int c_thrynnPlutoniumOfferCommId = 299;
	const int c_thrynnArtifactOfferCommId = 300;
	const int c_thrynnBlackBoxOfferCommId = 301;
	const int c_thrynnNotEnoughCommId = 302;
	const int c_thrynnVeryWellCommId = 303;

	// the Black Box's price, in tenths of a cubic meter of Endurium
	const int c_thrynnBlackBoxPrice = 300;

	// the artifact in the hold the Thrynn pay the most for - null if there is none they want
	GD_Artifact FindThrynnArtifact()
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		if ( ship.m_artifactStorage == null )
		{
			return null;
		}

		GD_Artifact best = null;

		foreach ( var artifact in DataController.m_instance.m_gameData.m_artifactList )
		{
			if ( ( artifact.m_thrynnPrice > 0 ) && ( ( best == null ) || ( artifact.m_thrynnPrice > best.m_thrynnPrice ) ) && ( ship.m_artifactStorage.Find( artifact.m_id ) != null ) )
			{
				best = artifact;
			}
		}

		return best;
	}

	// the plutonium in the hold the Thrynn offer to buy, in whole cubic meters (they pay one cubic meter of Endurium for each: the owner's ruling of 2026-10-11 on
	// "the amount of Endurium offered varies directly with the amount of plutonium available")
	int FindThrynnPlutonium()
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var plutoniumId = DataController.m_instance.m_gameData.FindElementId( "Plutonium" );
		var plutonium = ( ( plutoniumId < 0 ) || ( ship.m_elementStorage == null ) ) ? null : ship.m_elementStorage.Find( plutoniumId );

		return ( plutonium == null ) ? 0 : plutonium.m_volume / 10;
	}

	// the Endurium the Thrynn pay for this artifact of the hold, in tenths of a cubic meter: their price, but no more than the hold has room for once the artifact
	// is out of it (the shipped game offers the smaller of the two, COMMSPEC-OV)
	int GetThrynnArtifactPayment( GD_Artifact artifact )
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		return Mathf.Min( artifact.m_thrynnPrice * 10, Mathf.Max( 0, ship.GetRemainingVolume() + artifact.m_volume ) );
	}

	// the Thrynn's next offer (null if they have nothing to offer)
	GD_Comm FindThrynnOffer()
	{
		var artifact = FindThrynnArtifact();

		if ( artifact != null )
		{
			// the game data writes the artifact as X, the price as Y and the plural of "unit" as "unitxx" ("you have aboard your ship a(n) [ARTIFACT]. This
			// artifact is of interest to us. We will buy it from you for [AMOUNT] cubic Arth unit(s)", STRINFO 2.5) - the shipped game fills them in as the port
			// does here (COMMSPEC-OV in the decompiled original): "a" or "an" by the first letter, the name with a full stop after it, the amount it will pay
			// (its price, cut down to the room in the hold) and "unitss" for more than one
			var offer = FindRaceComm( c_thrynnArtifactOfferCommId );

			if ( offer != null )
			{
				var article = ( "AEIOU".IndexOf( char.ToUpper( artifact.m_name[ 0 ] ) ) >= 0 ) ? "an " : "a ";
				var payment = GetThrynnArtifactPayment( artifact );

				offer = WithAmount( offer, "X", article + artifact.m_name + "." );
				offer = WithAmount( offer, "Y", ( payment / 10 ).ToString() );
				offer = WithWord( offer, ". thiss ", ". Thiss " );
				offer = WithWord( offer, " unitxx ", ( payment > 10 ) ? " unitss " : " unit " );
			}

			return offer;
		}

		var plutonium = FindThrynnPlutonium();

		if ( plutonium > 0 )
		{
			return WithAmount( FindRaceComm( c_thrynnPlutoniumOfferCommId ), "X", plutonium.ToString() );
		}

		if ( !DataController.m_instance.m_playerData.m_playerShip.HasArtifact( "Black Box" ) )
		{
			return FindRaceComm( c_thrynnBlackBoxOfferCommId );
		}

		return null;
	}

	// the answer to a Thrynn offer: a yes makes the trade, a yes to the Black Box without 30 cubic meters of Endurium gets "you do not have enough", and a no gets
	// "very well" (the sources give no other answer, and no attack)
	void UpdateThrynnTrade()
	{
		var offer = m_pdEncounter.m_lastQuestionFromAliens;

		if ( ( offer != c_thrynnPlutoniumOfferCommId ) && ( offer != c_thrynnArtifactOfferCommId ) && ( offer != c_thrynnBlackBoxOfferCommId ) )
		{
			return;
		}

		// not answered yet?
		var answer = m_pdEncounter.m_lastSubjectFromPlayer;

		if ( answer == GD_Comm.Subject.None )
		{
			return;
		}

		// the question has been answered
		m_pdEncounter.m_lastQuestionFromAliens = 0;

		if ( answer != GD_Comm.Subject.Yes )
		{
			if ( answer == GD_Comm.Subject.No )
			{
				SayRaceComm( c_thrynnVeryWellCommId );
			}

			return;
		}

		var gameData = DataController.m_instance.m_gameData;
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var enduriumId = gameData.m_misc.m_enduriumElementId;

		if ( offer == c_thrynnArtifactOfferCommId )
		{
			var artifact = FindThrynnArtifact();

			// their Endurium, the amount they offered (as much of it as the hold has room for)
			var payment = ( artifact != null ) ? GetThrynnArtifactPayment( artifact ) : 0;

			if ( ( artifact != null ) && ship.RemoveArtifact( artifact.m_name ) )
			{

				if ( payment > 0 )
				{
					ship.AddElement( enduriumId, payment );
				}

				SayRaceComm( c_thrynnVeryWellCommId );
			}
		}
		else if ( offer == c_thrynnPlutoniumOfferCommId )
		{
			var plutonium = FindThrynnPlutonium();

			if ( plutonium > 0 )
			{
				ship.RemoveElement( gameData.FindElementId( "Plutonium" ), plutonium * 10 );

				var payment = Mathf.Min( plutonium * 10, Mathf.Max( 0, ship.GetRemainingVolume() ) );

				if ( payment > 0 )
				{
					ship.AddElement( enduriumId, payment );
				}

				SayRaceComm( c_thrynnVeryWellCommId );
			}
		}
		else
		{
			var endurium = ( ship.m_elementStorage == null ) ? null : ship.m_elementStorage.Find( enduriumId );

			if ( ( endurium == null ) || ( endurium.m_volume < c_thrynnBlackBoxPrice ) )
			{
				SayRaceComm( c_thrynnNotEnoughCommId );

				return;
			}

			ship.RemoveElement( enduriumId, c_thrynnBlackBoxPrice );
			ship.AddArtifact( gameData.FindArtifactId( "Black Box" ) );

			SayRaceComm( c_thrynnVeryWellCommId );
		}
	}

	// update encounter with velox
	void UpdateVeloxEncounter()
	{
		UpdateCowardice();

		UpdateVeloxDemand();

		UpdateMood();

		DefaultEncounterUpdate();
	}

	// the Veloxi's demands (STRINFO 2.2), their question of their own: the Crystal Orb ("most precious Small Egg of Veloxi"), the Ellipsoid ("focusing stone of
	// Veloxi"), or else a tribute of 3 Arth energy crystals ("the veloxi always ask for tribute", alpha.txt) - and the thanks for a tribute paid
	const int c_veloxEllipsoidDemandCommId = 397;
	const int c_veloxCrystalOrbDemandCommId = 398;
	const int c_veloxTributeDemandCommId = 399;
	const int c_veloxTributeThanksCommId = 415;

	// the tribute, in tenths of a cubic meter of Endurium
	const int c_veloxTribute = 30;

	// the answer to a Veloxi demand: a yes hands over what they demand, and anything else - a no, or ending the comm link with no answer - "will result in a Veloxi
	// attack" (STRINFO 2.2 for the Crystal Orb and the Ellipsoid; for the tribute, and for a yes with less than 3 cubic meters of Endurium, the owner's ruling of
	// 2026-10-11)
	void UpdateVeloxDemand()
	{
		var demand = m_pdEncounter.m_lastQuestionFromAliens;

		if ( ( demand != c_veloxEllipsoidDemandCommId ) && ( demand != c_veloxCrystalOrbDemandCommId ) && ( demand != c_veloxTributeDemandCommId ) )
		{
			return;
		}

		// not answered yet?
		var answer = m_pdEncounter.m_lastSubjectFromPlayer;

		if ( answer == GD_Comm.Subject.None )
		{
			return;
		}

		var gameData = DataController.m_instance.m_gameData;
		var ship = DataController.m_instance.m_playerData.m_playerShip;
		var messages = SpaceflightController.m_instance.m_messages;
		var handedOver = false;

		if ( answer == GD_Comm.Subject.Yes )
		{
			if ( demand == c_veloxCrystalOrbDemandCommId )
			{
				handedOver = ship.RemoveArtifact( "Crystal Orb" );

				if ( handedOver )
				{
					messages.AddText( "<color=white>The Veloxi take the Crystal Orb aboard.</color>" );
				}
			}
			else if ( demand == c_veloxEllipsoidDemandCommId )
			{
				handedOver = ship.RemoveArtifact( "Ellipsoid" );

				if ( handedOver )
				{
					messages.AddText( "<color=white>The Veloxi take the Ellipsoid aboard.</color>" );
				}
			}
			else
			{
				var enduriumId = gameData.m_misc.m_enduriumElementId;
				var endurium = ( ship.m_elementStorage == null ) ? null : ship.m_elementStorage.Find( enduriumId );

				if ( ( endurium != null ) && ( endurium.m_volume >= c_veloxTribute ) )
				{
					ship.RemoveElement( enduriumId, c_veloxTribute );

					handedOver = true;

					SayRaceComm( c_veloxTributeThanksCommId );
				}
			}
		}

		// the question has been answered
		m_pdEncounter.m_lastQuestionFromAliens = 0;

		if ( handedOver )
		{
			return;
		}

		// the attack - they end the comm link, since the aliens hold their fire while it is up
		m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

		if ( m_pdEncounter.m_connected )
		{
			Disconnect();
		}
		else
		{
			m_pdEncounter.m_disconnected = true;
		}
	}

	// update encounter with a veloxi drone - before it lets a ship into orbit it asks the player to confirm a series of numbers, and only a yes to the multiples of 6
	// (and a no to the others) gets "permission to orbit granted" - anything else gets "permission to orbit denied" and an attack (STRINFO 2.3)
	// returns true if the drone has ended the encounter
	bool UpdateVeloxProbeEncounter()
	{
		// has the drone let the ship into orbit?
		if ( m_returnToOrbitTimer >= 0.0f )
		{
			// yes - give the player a moment to read it, then back into orbit
			m_returnToOrbitTimer -= Time.deltaTime;

			if ( m_returnToOrbitTimer < 0.0f )
			{
				if ( m_pdEncounter.m_connected )
				{
					Disconnect();
				}

				LeaveEncounter();

				return true;
			}

			return false;
		}

		// are we talking to the drone?
		if ( !m_pdEncounter.m_connected )
		{
			// no - did the player end the comm link before answering all of the numbers? that fails the test
			if ( m_pdEncounter.m_disconnected && ( m_pdEncounter.m_alienStance != GD_Comm.Stance.Hostile ) )
			{
				DenyOrbit();
			}

			// the drone hails like everyone else, and attacks once it is hostile
			DefaultEncounterUpdate();

			return false;
		}

		// is a number waiting for an answer?
		if ( m_pdEncounter.m_lastQuestionFromAliens != 0 )
		{
			// did the player answer it?
			var answer = m_pdEncounter.m_lastSubjectFromPlayer;

			if ( ( answer != GD_Comm.Subject.Yes ) && ( answer != GD_Comm.Subject.No ) )
			{
				// no - keep waiting
				return false;
			}

			// yes - the right answer is a yes to a multiple of 6 and a no to any other number
			var number = m_pdEncounter.m_lastQuestionFromAliens;
			var rightAnswer = ( ( number % 6 ) == 0 ) ? GD_Comm.Subject.Yes : GD_Comm.Subject.No;

			// the number has been answered
			m_pdEncounter.m_lastQuestionFromAliens = 0;

			// a ship that fired on the drone never gets permission
			if ( ( answer != rightAnswer ) || m_pdEncounter.m_attackedByPlayer )
			{
				DenyOrbit();

				return false;
			}

			// another right answer - was it the last number?
			m_pdEncounter.m_numCorrectAnswers++;

			if ( m_pdEncounter.m_numCorrectAnswers >= c_droneNumberCount )
			{
				// yes - let the ship into orbit
				GrantOrbit();

				return false;
			}

			// no - the next number comes shortly
			m_pdEncounter.m_conversationTimer = Random.Range( 2.0f, 4.0f );

			return false;
		}

		// the first number comes shortly after the comm link is up
		if ( m_pdEncounter.m_conversationTimer > 4.0f )
		{
			m_pdEncounter.m_conversationTimer = Random.Range( 2.0f, 4.0f );
		}

		// is it time for the next number?
		m_pdEncounter.m_conversationTimer -= Time.deltaTime;

		if ( m_pdEncounter.m_conversationTimer <= 0.0f )
		{
			AskDroneNumber();
		}

		return false;
	}

	// the drone asks the player to confirm a number from 1 to 99 - about half of them are multiples of 6
	void AskDroneNumber()
	{
		int number;

		if ( Random.Range( 0, 2 ) == 0 )
		{
			// a multiple of 6 (6 to 96)
			number = 6 * Random.Range( 1, 17 );
		}
		else
		{
			// any other number (a multiple of 6 moves up by one, which is at most 97)
			number = Random.Range( 1, 100 );

			if ( ( number % 6 ) == 0 )
			{
				number++;
			}
		}

		// show the number (the drones ask in numbers only, so there is nothing to garble)
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Receiving:</color>\n<color=#0a0>" + number + "?</color>" );

		// play the beep sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Beep );

		// the player answers with yes or no
		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.AnswerQuestion );

		// forget the last subject from the player
		m_pdEncounter.m_lastSubjectFromPlayer = GD_Comm.Subject.None;

		// remember the number (it is never 0, which means no question)
		m_pdEncounter.m_lastQuestionFromAliens = number;
	}

	// the drone lets the ship into orbit
	void GrantOrbit()
	{
		// "permission to orbit granted"
		AddComm( GD_Comm.Subject.OrbitGranted, false );

		// the drone is friendly now
		m_pdEncounter.m_alienStance = GD_Comm.Stance.Friendly;

		// it does not stop the ship again while the ship stays in this star system
		SpaceflightController.m_instance.GrantOrbitPermission( m_pdEncounter.m_encounterId );

		// back into orbit once the player has had a moment to read it
		m_returnToOrbitTimer = c_returnToOrbitDelay;
	}

	// the drone refuses the ship and attacks
	void DenyOrbit()
	{
		// "permission to orbit denied"
		AddComm( GD_Comm.Subject.OrbitDenied, false );

		// followed by a veloxi drone attack
		m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

		// no more numbers
		m_pdEncounter.m_lastQuestionFromAliens = 0;

		// the aliens hold their fire while the comm link is up, so the drone ends it
		if ( m_pdEncounter.m_connected )
		{
			Disconnect();
		}
	}

	// update encounter with minstrel
	void UpdateMinstrelEncounter()
	{
		DefaultEncounterUpdate();
	}

	// update encounter with uhlek
	void UpdateUhlekEncounter()
	{
		// the uhlek are always hostile (this also covers a game that was saved in the middle of an uhlek encounter before they attacked on sight)
		m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

		// but once their mind-ganglion has been destroyed they are harmless (STRINFO 5.1: "the Uhlek will be rendered harmless"; the Spemin: "helpless until another one
		// developed")
		if ( BlackEgg.IsUhlekMindGanglionDestroyed() )
		{
			return;
		}

		// they never talk (there is nothing for them to say in the game data) - they just attack
		UpdateAlienCombat();
	}

	// default encounter update
	bool DefaultEncounterUpdate()
	{
		// keep track of whether the aliens said something
		bool aliensSaidSomething = false;

		// update scan timer
		m_pdEncounter.m_scanTimer -= Time.deltaTime;

		// have the aliens already scanned us?
		if ( !m_pdEncounter.m_alreadyScanned )
		{
			// no - did the timer expire?
			if ( m_pdEncounter.m_scanTimer <= 0.0f )
			{
				// yes - are the aliens close enough to scan?
				if ( ClosestDistanceToAlienShip() < 512.0f )
				{
					// yes - play the scan sound
					SoundController.m_instance.PlaySound( SoundController.Sound.RadarBlip );

					// update the message
					SpaceflightController.m_instance.m_messages.AddText( "<color=white>Captain, we're being scanned.</color>" );

					// update the flag
					m_pdEncounter.m_scanTimer = 0.0f;
					m_pdEncounter.m_alreadyScanned = true;
				}
			}
		}

		// have we already disconnected?
		if ( !m_pdEncounter.m_disconnected )
		{
			// did the aliens ask a question?
			if ( m_pdEncounter.m_lastQuestionFromAliens == 0 )
			{
				// no - update the statement timer
				m_pdEncounter.m_conversationTimer -= Time.deltaTime;

				// is it time for an update?
				if ( m_pdEncounter.m_conversationTimer <= 0.0f )
				{
					// reset the conversation timer
					m_pdEncounter.m_conversationTimer = Random.Range( 10.0f, 30.0f );

					// have aliens with a personality said all the phrases they will? then they are tired of talking (compart.txt 11)
					if ( m_pdEncounter.m_connected && IsTiredOfTalking() )
					{
						AddComm( GD_Comm.Subject.Terminate, false );
					}
					// do they transmit rather than talk? their next line, and when the transmission is over they end the comm link
					else if ( m_pdEncounter.m_connected && IsTransmitter() )
					{
						aliensSaidSomething = true;

						var transmission = FindNextTransmission();

						if ( transmission != null )
						{
							AddComm( GD_Comm.Subject.Statement, false, transmission );
						}
						else
						{
							AddComm( GD_Comm.Subject.Terminate, false );
						}
					}
					// are we connected?
					else if ( m_pdEncounter.m_connected )
					{
						// the aliens will have said something
						aliensSaidSomething = true;

						// one phrase fewer left to say
						m_pdEncounter.m_alienPhrasesLeft--;

						// was there a pending question from the player?
						if ( ( m_pdEncounter.m_lastSubjectFromPlayer >= GD_Comm.Subject.Themselves ) && ( m_pdEncounter.m_lastSubjectFromPlayer <= GD_Comm.Subject.TheAncients ) )
						{
							// yes - answer the question
							AddComm( m_pdEncounter.m_lastSubjectFromPlayer, false );

							// there is no longer have a pending question
							m_pdEncounter.m_lastSubjectFromPlayer = GD_Comm.Subject.None;
						}
						else
						{
							// no - do we want to do a question?
							if ( Random.Range( 1, 101 ) > m_pdEncounter.m_questionLikelihood )
							{
								// no - do a normal statement
								AddComm( GD_Comm.Subject.Statement, false );
							}
							else
							{
								// yes - ask a question (the first time, the race's own question if it has one)
								var specialQuestion = FindSpecialQuestion();

								if ( specialQuestion != null )
								{
									m_pdEncounter.m_specialQuestionAsked = true;

									AddComm( GD_Comm.Subject.Question, false, specialQuestion );
								}
								else
								{
									AddComm( GD_Comm.Subject.Question, false );
								}
							}
						}
					}
					else
					{
						// are we already connected?
						if ( m_pdEncounter.m_connected )
						{
							// do a normal statement
							AddComm( GD_Comm.Subject.Statement, false );
						}
						else
						{
							// did the player hail?
							if ( m_pdEncounter.m_playerWantsToConnect )
							{
								// yes - do a response
								AddComm( GD_Comm.Subject.GreetingResponse, false );
							}
							else
							{
								// no - do a hail
								AddComm( GD_Comm.Subject.GreetingHail, false );
							}
						}
					}
				}

				// are we connected? (aliens with a personality tire of talking by the number of their phrases instead)
				if ( m_pdEncounter.m_connected && ( AlienPersonality.Find( m_gdEncounter.m_race ) == null ) )
				{
					// yes - update connection timer
					m_pdEncounter.m_connectionTimer -= Time.deltaTime;

					// did the timer expire?
					if ( m_pdEncounter.m_connectionTimer <= 0.0f )
					{
						// yes - the aliens want to terminate the conversation
						AddComm( GD_Comm.Subject.Terminate, false );
					}
				}
			}
		}

		// did the aliens ask a question?
		if ( m_pdEncounter.m_lastQuestionFromAliens != 0 )
		{
			// yes - did the player answer?
			if ( m_pdEncounter.m_lastSubjectFromPlayer != GD_Comm.Subject.None )
			{
				// yes - forget the question (it should already have been handled before calling this default encounter update function)
				m_pdEncounter.m_lastQuestionFromAliens = 0;
			}
		}

		// has a home fleet given its last warning? it ends the comm link and attacks
		if ( m_pdEncounter.m_homeworldWarningsGiven )
		{
			m_pdEncounter.m_alienStance = GD_Comm.Stance.Hostile;

			if ( m_pdEncounter.m_connected )
			{
				Disconnect();
			}
			else if ( !m_pdEncounter.m_disconnected )
			{
				m_pdEncounter.m_disconnected = true;
				m_pdEncounter.m_playerWantsToConnect = false;
				m_pdEncounter.m_aliensWantToConnect = false;
			}
		}

		// handle combat if aliens are hostile
		UpdateAlienCombat();

		// let the caller know if the aliens said something
		return aliensSaidSomething;
	}

	// update alien combat behavior
	void UpdateAlienCombat()
	{
		// the aliens hold their fire for as long as the comm link is up (every alien fire rule in the original design notes needs "?IN-COMM FALSE")
		// this is the one place all alien fire comes through, so it covers every race - the time to their next shot does not run down while they talk
		if ( m_pdEncounter.m_connected )
		{
			return;
		}

		// only attack if hostile stance
		if ( m_pdEncounter.m_alienStance != GD_Comm.Stance.Hostile )
		{
			// DEBUG: Log stance periodically
			if ( Time.frameCount % 300 == 0 )
			{
				Debug.Log( $"UpdateAlienCombat: Aliens not hostile (stance={m_pdEncounter.m_alienStance}, m_pdEncounter hashcode={m_pdEncounter.GetHashCode()})" );
			}
			return;
		}

		// Log first time we detect hostile stance
		if ( Time.frameCount % 60 == 0 )
		{
			Debug.Log( $"UpdateAlienCombat: ALIENS ARE HOSTILE! (m_pdEncounter hashcode={m_pdEncounter.GetHashCode()})" );
		}

		// check if CombatController exists
		if ( CombatController.m_instance == null )
		{
			Debug.LogWarning( "UpdateAlienCombat: CombatController.m_instance is null!" );
			return;
		}

		// get game data
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;

		// update combat timer
		m_pdEncounter.m_combatTimer -= Time.deltaTime;
		
		// DEBUG: Log combat state periodically
		if ( Time.frameCount % 120 == 0 )
		{
			var alienShipList = m_pdEncounter.GetAlienShipList();
			int aliveCount = 0;
			float closestDistance = float.MaxValue;
			foreach ( var ship in alienShipList )
			{
				if ( !ship.m_isDead && ship.m_addedToEncounter )
				{
					aliveCount++;
					float dist = Vector3.Distance( ship.m_coordinates, playerData.m_general.m_coordinates );
					if ( dist < closestDistance ) closestDistance = dist;
				}
			}
			Debug.Log( $"UpdateAlienCombat: HOSTILE! Timer={m_pdEncounter.m_combatTimer:F2}, AliveShips={aliveCount}, ClosestDist={closestDistance:F0}" );
		}

		// time to attack?
		if ( m_pdEncounter.m_combatTimer <= 0.0f )
		{
			Debug.Log( "UpdateAlienCombat: Timer expired, attempting to fire!" );
			
			// reset timer based on alien vessel fire delay
			m_pdEncounter.m_combatTimer = Random.Range( 2.0f, 5.0f );

			// pick a random alien ship to fire
			var alienShipList = m_pdEncounter.GetAlienShipList();
			var eligibleShips = new System.Collections.Generic.List<int>();

			for ( int i = 0; i < alienShipList.Length; i++ )
			{
				var alienShip = alienShipList[ i ];

				// skip dead or not in encounter
				if ( alienShip.m_isDead || !alienShip.m_addedToEncounter )
				{
					continue;
				}

				// check range
				float distance = Vector3.Distance( alienShip.m_coordinates, playerData.m_general.m_coordinates );
				if ( distance < 1000.0f )
				{
					eligibleShips.Add( i );
				}
				else
				{
					Debug.Log( $"UpdateAlienCombat: Ship {i} out of range (distance={distance:F0})" );
				}
			}

			Debug.Log( $"UpdateAlienCombat: Found {eligibleShips.Count} eligible ships to fire" );

			// fire from a random eligible ship
			if ( eligibleShips.Count > 0 )
			{
				int attackerIndex = eligibleShips[ Random.Range( 0, eligibleShips.Count ) ];
				var attackerShip = alienShipList[ attackerIndex ];
				var vessel = gameData.m_vesselList[ attackerShip.m_vesselId ];

				// adjust fire delay based on vessel
				m_pdEncounter.m_combatTimer = vessel.m_fireDelay * 0.5f + Random.Range( 1.0f, 3.0f );

				Debug.Log( $"UpdateAlienCombat: Ship {attackerIndex} ({vessel.m_name}) FIRING at player!" );

				// fire at player
				CombatController.m_instance.AlienFiresAtPlayer( attackerShip, vessel );
			}
		}
	}

	// mechan ship movement
	void UpdateMechanAlienShip( PD_AlienShip alienShip, GameObject alienShipModel )
	{
		BuzzPlayer( alienShip, alienShipModel, 1.0f );
	}

	// default alien ship movement
	void DefaultAlienShipUpdate( PD_AlienShip alienShip, GameObject alienShipModel )
	{
		BuzzPlayer( alienShip, alienShipModel, 1.0f );
	}

	// moves the alien ship towards the player
	void BuzzPlayer( PD_AlienShip alienShip, GameObject alienShipModel, float alienShipSpeedMultiplier )
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// move the alien ship towards the player
		MoveAlienShip( alienShip, alienShipModel, alienShipSpeedMultiplier, playerData.m_general.m_coordinates );
	}

	// moves the alien ship towards the target coordinates
	void MoveAlienShip( PD_AlienShip alienShip, GameObject alienShipModel, float alienShipSpeedMultiplier, Vector3 targetCoordinates )
	{
		// update time since we changed target coordinates for this alien ship
		alienShip.m_timeSinceLastTargetCoordinateChange += Time.deltaTime;

		// change target coordinates every so often
		if ( alienShip.m_timeSinceLastTargetCoordinateChange >= m_targetCoordinateUpdateFrequency )
		{
			var randomCoordinates = Random.insideUnitCircle;

			alienShip.m_targetCoordinates = targetCoordinates + Vector3.Normalize( new Vector3( randomCoordinates.x, 0.0f, randomCoordinates.y ) ) * 256.0f;

			alienShip.m_timeSinceLastTargetCoordinateChange -= m_targetCoordinateUpdateFrequency;
		}

		// steer the alien ship towards the target coordinates
		var desiredDirection = Vector3.Normalize( alienShip.m_targetCoordinates - alienShip.m_coordinates );

		alienShip.m_currentDirection = Vector3.Slerp( alienShip.m_currentDirection, desiredDirection, Time.deltaTime * m_alienShipTurnRate );

		// move the alien ship forward, at the speed of its vessel
		var gameData = DataController.m_instance.m_gameData;
		var speedFactor = ( ( alienShip.m_vesselId >= 0 ) && ( alienShip.m_vesselId < gameData.m_vesselList.Length ) ) ? gameData.m_vesselList[ alienShip.m_vesselId ].GetSpeedFactor() : 1.0f;

		alienShip.m_coordinates += alienShip.m_currentDirection * Time.deltaTime * m_alienShipSpeed * alienShipSpeedMultiplier * speedFactor;
	}

	void FinalizeAlienShips()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// remember the extents
		var xExtent = 0.0f;
		var zExtent = 0.0f;

		// update the position and rotation of the active alien ship models
		for ( var alienShipIndex = 0; alienShipIndex < m_alienShipModelList.Length; alienShipIndex++ )
		{
			var alienShipModel = m_alienShipModelList[ alienShipIndex ];

			if ( alienShipModel.activeInHierarchy )
			{
				// get to the alien ship
				var alienShip = m_alienShipList[ alienShipIndex ];

				// skip this model if there is no alien ship in its slot (this should never happen)
				if ( alienShip == null )
				{
					continue;
				}

				// set the rotation of the ship
				alienShipModel.transform.rotation = Quaternion.LookRotation( alienShip.m_currentDirection, Vector3.up );

				// get the number of degrees we are turning the ship (compared to the last frame)
				var bankingAngle = Vector3.SignedAngle( alienShip.m_currentDirection, alienShip.m_lastDirection, Vector3.up );

				// scale the angle enough so we actually see the ship banking (but max it out at 60 degrees in either direction)
				bankingAngle = Mathf.Max( -60.0f, Mathf.Min( 60.0f, bankingAngle * 48.0f ) );

				// interpolate towards the new banking angle
				alienShip.m_currentBankingAngle = Mathf.Lerp( alienShip.m_currentBankingAngle, bankingAngle, Time.deltaTime );

				// save the last direction
				alienShip.m_lastDirection = alienShip.m_currentDirection;

				// bank the ship based on the calculated angle
				alienShipModel.transform.rotation = Quaternion.AngleAxis( alienShip.m_currentBankingAngle, alienShip.m_currentDirection ) * alienShipModel.transform.rotation;

				// set the position of the ship
				alienShipModel.transform.position = alienShip.m_coordinates;

				// the camera only has to keep living ships in view (wreckage stays behind when the player flies away from it)
				if ( alienShip.m_isDead )
				{
					continue;
				}

				// figure out how far away from the player this alien ship is
				var playerToShip = alienShip.m_coordinates - playerData.m_general.m_coordinates;

				xExtent = Mathf.Max( xExtent, Mathf.Abs( playerToShip.x ) );
				zExtent = Mathf.Max( zExtent, Mathf.Abs( playerToShip.z ) );
			}
		}

		// add some space around the extents
		xExtent += 192.0f;
		zExtent += 192.0f;

		// recalculate what the camera distance from the zero plane should be
		var verticalFieldOfView = SpaceflightController.m_instance.m_viewport.m_camera.fieldOfView * Mathf.Deg2Rad;
		var horizontalFieldOfView = 2.0f * Mathf.Atan( Mathf.Tan( verticalFieldOfView * 0.5f ) * SpaceflightController.m_instance.m_viewport.m_camera.aspect );
		var horizontalAngle = Mathf.Deg2Rad * ( 180.0f - 90.0f - horizontalFieldOfView * Mathf.Rad2Deg * 0.5f );
		var verticalAngle = Mathf.Deg2Rad * ( 180.0f - 90.0f - verticalFieldOfView * Mathf.Rad2Deg * 0.5f );
		var tanHorizontalAngle = Mathf.Tan( horizontalAngle );
		var tanVerticalAngle = Mathf.Tan( verticalAngle );

		var targetDollyDistance = Mathf.Max( xExtent * tanHorizontalAngle, zExtent * tanVerticalAngle, 1024.0f );

		// slowly dolly the camera
		m_currentOffset = Mathf.Lerp( m_currentOffset, targetDollyDistance, Time.deltaTime * m_cameraDollySpeed );

		// update the camera follow altitude
		SpaceflightController.m_instance.m_playerCamera.SetCameraFollow( SpaceflightController.m_instance.m_playerShip.gameObject, Vector3.up * m_currentOffset );
	}

	// the aliens' mood decides their posture unless a rule has decided it: the player has fired on them, they have turned cowardly, they are a home fleet, Elan is
	// gone (the Elowan), or they have turned to fight
	bool MoodDecides()
	{
		if ( m_pdEncounter.m_attackedByPlayer || m_pdEncounter.m_aliensAreCowardly || IsHomeFleet() )
		{
			return false;
		}

		if ( ( m_gdEncounter.m_race == GameData.Race.Elowan ) && BlackEgg.IsElanDestroyed() )
		{
			return false;
		}

		return m_pdEncounter.m_alienStance != GD_Comm.Stance.Hostile;
	}

	// the mood of a new encounter (and of one from a save file written before the aliens had moods)
	void EnsureMood( AlienPersonality personality )
	{
		if ( !m_pdEncounter.m_moodSet )
		{
			// the race's mood, and more against a ship that meets them with its shields up or its weapons armed
			m_pdEncounter.m_mood = personality.m_startingMood + AlienPersonality.GetMeetingMood( DataController.m_instance.m_playerData.m_playerShip );
			m_pdEncounter.m_moodSet = true;

			// the Elowan and the Thrynn hold the crew's friendship with their rival race against it: they start in their hostile band (the owner's ruling of 2026-10-11),
			// which for the Elowan is fight or flight, as the original says the rival lines only to aliens past their neutral posture (COMMSPEC-OV: A-POSTU > 1)
			if ( IsFriendOfRival() )
			{
				m_pdEncounter.m_mood = Mathf.Max( m_pdEncounter.m_mood, personality.m_hostileFrom );
			}
		}
	}

	// how much stronger than the aliens the player must be for them to grovel instead of threatening, and to take flight instead of fighting (COMM-OV EDL>POSTURE)
	const int c_grovelOverHostile = 15;
	const int c_fleeOverFight = 5;

	// the player's strength as the shipped game weighs it (COMM-OV): the classes of the ship's armor, shields, missile launcher and laser cannon, and as much again
	// for every 500 points by which the captain's five skills together pass 550
	int GetPlayerStrength()
	{
		var playerData = DataController.m_instance.m_playerData;
		var ship = playerData.m_playerShip;

		var classes = Mathf.Max( 0, ship.m_armorClass ) + Mathf.Max( 0, ship.m_shieldingClass ) + Mathf.Max( 0, ship.m_missileLauncherClass ) + Mathf.Max( 0, ship.m_laserCannonClass );

		var captain = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Captain );

		if ( captain == null )
		{
			return classes;
		}

		var skills = captain.m_science + captain.m_navigation + captain.m_engineering + captain.m_communications + captain.m_medicine;

		return classes + classes * Mathf.Max( 0, skills - 550 ) / 500;
	}

	// the aliens' strength as the shipped game weighs it (COMM-OV IAPWR): each ship that is there adds its vessel's strength, by the share of its armor and shields
	// that it has left
	int GetAlienStrength()
	{
		var gameData = DataController.m_instance.m_gameData;
		var alienStrength = 0;

		foreach ( var alienShip in m_pdEncounter.GetAlienShipList() )
		{
			if ( ( alienShip == null ) || !alienShip.m_addedToEncounter || alienShip.m_isDead || ( alienShip.m_vesselId <= 0 ) || ( alienShip.m_vesselId >= gameData.m_vesselList.Length ) )
			{
				continue;
			}

			var vessel = gameData.m_vesselList[ alienShip.m_vesselId ];
			var fullPoints = vessel.m_armor + vessel.m_shields;

			if ( fullPoints > 0 )
			{
				alienStrength += (int) ( (long) vessel.m_strength * ( Mathf.Max( 0, alienShip.m_armorPoints ) + Mathf.Max( 0, alienShip.m_shieldPoints ) ) / fullPoints );
			}
		}

		return alienStrength;
	}

	// the player's strength less the aliens' (COMM-OV EDL>POSTURE)
	int GetStrengthDifferential()
	{
		return GetPlayerStrength() - GetAlienStrength();
	}

	// the posture the aliens' mood gives them below "fight or flight": friendly, diplomatic (neutral), or hostile - obsequious instead against a player more than 15
	// stronger (compart.txt 4, COMM-OV EDL>POSTURE)
	GD_Comm.Stance GetMoodPosture( AlienPersonality personality )
	{
		var mood = m_pdEncounter.m_mood;

		if ( mood < personality.m_friendlyBelow )
		{
			return GD_Comm.Stance.Friendly;
		}

		if ( mood < personality.m_hostileFrom )
		{
			return GD_Comm.Stance.Neutral;
		}

		return ( GetStrengthDifferential() > c_grovelOverHostile ) ? GD_Comm.Stance.Obsequious : GD_Comm.Stance.Hostile;
	}

	// true if a ship of this encounter that is still there matches (the original's IAPWR looks over the encounter's ships for shields and for weapons)
	bool AnyAlienShipHas( System.Func<GD_Vessel, bool> match )
	{
		var gameData = DataController.m_instance.m_gameData;

		var alienShipList = m_pdEncounter.GetAlienShipList();

		if ( alienShipList == null )
		{
			return false;
		}

		foreach ( var alienShip in alienShipList )
		{
			if ( ( alienShip == null ) || alienShip.m_isDead || ( alienShip.m_vesselId < 0 ) || ( alienShip.m_vesselId >= gameData.m_vesselList.Length ) )
			{
				continue;
			}

			if ( match( gameData.m_vesselList[ alienShip.m_vesselId ] ) )
			{
				return true;
			}
		}

		return false;
	}

	// true while the aliens surrender: cowardly and obsequious (the original's WEA24 lowers their shields and disarms their weapons then)
	bool AliensAreSurrendering( GD_Comm.Stance posture )
	{
		return m_pdEncounter.m_aliensAreCowardly && ( posture == GD_Comm.Stance.Obsequious );
	}

	// reports a change of the aliens' shields or weapons, in the original's words (COMM-OV: RAISE-SHIELDS, LOWER-SHIELDS, ARM-WEAPONS and DISARM-WEAPONS say
	// "CAPTAIN, THEY'VE RAISED THEIR SHIELDS." and so on) - the state the encounter begins with is taken without a word, as is everything once no alien ship is left
	void UpdateShieldsAndWeaponsNews()
	{
		if ( !HasLivingAlienShips() )
		{
			return;
		}

		var shieldsUp = AliensHaveShieldsUp();
		var armed = AliensAreArmed();

		if ( !m_shieldsAndWeaponsKnown )
		{
			m_shieldsAndWeaponsKnown = true;
			m_aliensHadShieldsUp = shieldsUp;
			m_aliensWereArmed = armed;

			return;
		}

		if ( shieldsUp != m_aliensHadShieldsUp )
		{
			m_aliensHadShieldsUp = shieldsUp;

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Captain, they've " + ( shieldsUp ? "raised" : "lowered" ) + " their shields.</color>" );
		}

		if ( armed != m_aliensWereArmed )
		{
			m_aliensWereArmed = armed;

			SpaceflightController.m_instance.m_messages.AddText( "<color=white>Captain, they've " + ( armed ? "armed" : "disarmed" ) + " their weapons.</color>" );
		}
	}

	// whether the aliens have raised their shields, as the original's ?A-SHIELDS-UP (COMM-OV, <AUX>): up when a ship of the encounter has shields and their
	// posture is not friendly, never for the Spemin, and down once they surrender
	public bool AliensHaveShieldsUp()
	{
		if ( ( m_pdEncounter == null ) || ( m_gdEncounter == null ) || ( m_gdEncounter.m_race == GameData.Race.Spemin ) )
		{
			return false;
		}

		var posture = GetAlienTalkStance();

		if ( ( posture == GD_Comm.Stance.Friendly ) || AliensAreSurrendering( posture ) )
		{
			return false;
		}

		return AnyAlienShipHas( vessel => vessel.m_shields > 0 );
	}

	// whether the aliens have armed their weapons, as the original's ?A-WEAP (COMM-OV, <AUX>): armed when a ship of the encounter has weapons and their
	// posture is hostile, never while they surrender
	public bool AliensAreArmed()
	{
		if ( ( m_pdEncounter == null ) || ( m_gdEncounter == null ) )
		{
			return false;
		}

		var posture = GetAlienTalkStance();

		if ( ( posture != GD_Comm.Stance.Hostile ) || AliensAreSurrendering( posture ) )
		{
			return false;
		}

		return AnyAlienShipHas( vessel => ( vessel.m_laserClass > 0 ) || ( vessel.m_missileClass > 0 ) || vessel.m_hasPlasmaBolts );
	}

	// the stance whose lines the aliens say: their mood's posture where the mood decides (a hostile posture is abusive talk, not an attack), else their stance
	GD_Comm.Stance GetAlienTalkStance()
	{
		var personality = AlienPersonality.Find( m_gdEncounter.m_race );

		if ( ( personality == null ) || !MoodDecides() )
		{
			return m_pdEncounter.m_alienStance;
		}

		EnsureMood( personality );

		if ( m_pdEncounter.m_mood >= personality.m_fightFrom )
		{
			return m_pdEncounter.m_alienStance;
		}

		return GetMoodPosture( personality );
	}

	// the mood sets the aliens' stance where it decides: they hold their fire in every posture, and only once they are too agitated to talk do they end the comm
	// link and fight - or take flight from a stronger ship, still holding their fire (compart.txt 4 and 11; flight is the port's: they stay, and keep quiet)
	void UpdateMood()
	{
		var personality = AlienPersonality.Find( m_gdEncounter.m_race );

		if ( personality == null )
		{
			return;
		}

		EnsureMood( personality );

		if ( !MoodDecides() )
		{
			return;
		}

		if ( m_pdEncounter.m_mood >= personality.m_fightFrom )
		{
			if ( m_pdEncounter.m_connected )
			{
				AddComm( GD_Comm.Subject.Terminate, false );
			}

			m_pdEncounter.m_alienStance = ( GetStrengthDifferential() > c_fleeOverFight ) ? GD_Comm.Stance.Obsequious : GD_Comm.Stance.Hostile;

			return;
		}

		var posture = GetMoodPosture( personality );

		m_pdEncounter.m_alienStance = ( posture == GD_Comm.Stance.Hostile ) ? GD_Comm.Stance.Neutral : posture;
	}

	// a statement of the player's moves the aliens' mood by the effect of the player's posture on their race, divided by the number of phrases the player has made
	// in this comm link with it, and a question by half of that (COMM-OV STATEMENT-EDL and the question's rule: POSTURE-EFFECT / P-PHRASES, and 2/ for a question)
	void ApplyPlayerPosture( bool isQuestion )
	{
		m_pdEncounter.m_playerPhrases++;

		var personality = AlienPersonality.Find( m_gdEncounter.m_race );

		if ( personality == null )
		{
			return;
		}

		var effect = personality.GetPostureEffect( m_pdEncounter.m_playerStance ) / m_pdEncounter.m_playerPhrases;

		if ( isQuestion )
		{
			effect *= 0.5f;
		}

		ChangeMood( effect );
	}

	// the full effect of the player's posture on the aliens' mood: once for the reply to their hail, and once for every posture the player hails or speaks in after
	// hailing first (COMM-OV RESPOND>EDL and NEW-POSTURE>EDL)
	void ApplyFullPostureEffect()
	{
		var personality = AlienPersonality.Find( m_gdEncounter.m_race );

		if ( personality != null )
		{
			ChangeMood( personality.GetPostureEffect( m_pdEncounter.m_playerStance ) );
		}
	}

	// after the player has hailed first, the first use of each posture in the encounter has its full effect on the aliens' mood (COMM-OV NEW-POSTURE>EDL)
	void ApplyNewPosture()
	{
		var postureBit = GD_Comm.GetPostureBit( m_pdEncounter.m_playerStance );

		if ( !m_pdEncounter.m_playerHailedFirst || ( postureBit == 0 ) || ( ( m_pdEncounter.m_playerPostures & postureBit ) != 0 ) )
		{
			return;
		}

		m_pdEncounter.m_playerPostures |= postureBit;

		ApplyFullPostureEffect();
	}

	// the player's shields and weapons as the aliens' mood last saw them (not saved: they are seen again when an encounter is shown)
	bool m_playerShieldsSeen;
	bool m_playerShieldsWereUp;
	bool m_playerWeaponsWereArmed;

	// raising the shields during an encounter angers the aliens by 5 and lowering them calms them by 5, arming the weapons by 10 (COMM-OV <EDL>: +5>EDL, -5, +10,
	// -10) - before the mood is set the meeting counts the shields and weapons as they are (AlienPersonality.GetMeetingMood)
	void UpdatePlayerShieldsAndWeapons()
	{
		var ship = DataController.m_instance.m_playerData.m_playerShip;

		if ( m_playerShieldsSeen && m_pdEncounter.m_moodSet )
		{
			if ( ship.m_shieldsAreUp != m_playerShieldsWereUp )
			{
				ChangeMood( ship.m_shieldsAreUp ? 5.0f : -5.0f );
			}

			if ( ship.m_weaponsAreArmed != m_playerWeaponsWereArmed )
			{
				ChangeMood( ship.m_weaponsAreArmed ? 10.0f : -10.0f );
			}
		}

		m_playerShieldsSeen = true;
		m_playerShieldsWereUp = ship.m_shieldsAreUp;
		m_playerWeaponsWereArmed = ship.m_weaponsAreArmed;
	}

	// moves the aliens' mood by a line of theirs or by the player's answer to their question, kept within 0 to 100 (COMM-OV +!EDL) - only where the mood decides
	void ChangeMood( float delta )
	{
		var personality = AlienPersonality.Find( m_gdEncounter.m_race );

		if ( ( personality == null ) || !MoodDecides() || ( delta == 0.0f ) )
		{
			return;
		}

		EnsureMood( personality );

		m_pdEncounter.m_mood = Mathf.Clamp( m_pdEncounter.m_mood + delta, 0.0f, 100.0f );
	}

	// the player's Yes or No moves the aliens' mood by the change their question has for it (COMM-OV YES>EDL and NO-EDL: Velox "Of course Veloxi are superior all
	// the time. Agreeing?" No 10, Yes 0)
	void ApplyAnswer( bool yes )
	{
		if ( m_pdEncounter.m_lastQuestionFromAliens == 0 )
		{
			return;
		}

		var question = FindRaceComm( m_pdEncounter.m_lastQuestionFromAliens );

		if ( question != null )
		{
			ChangeMood( yes ? question.m_deltaYes : question.m_deltaNo );
		}
	}

	// how many phrases the aliens say before they are tired of talking: "a random number in the range determined by the alien's initial posture" (compart.txt 11)
	static int GetPhraseLimit( GD_Comm.Stance posture )
	{
		switch ( posture )
		{
			case GD_Comm.Stance.Obsequious:
				return Random.Range( 10, 21 );

			case GD_Comm.Stance.Friendly:
				return Random.Range( 5, 16 );

			case GD_Comm.Stance.Hostile:
				return Random.Range( 2, 6 );
		}

		return Random.Range( 3, 9 );
	}

	// true once aliens with a personality have said all their phrases
	bool IsTiredOfTalking()
	{
		return ( AlienPersonality.Find( m_gdEncounter.m_race ) != null ) && ( m_pdEncounter.m_alienPhrasesLeft <= 0 );
	}

	// true if the aliens transmit rather than talk: the Minstrels' song, the Nomad probe's planetary data, the Noah 9 derelict's recorded message and the Mysterions'
	// signal - STRINFO 2.8 to 2.11 give them as what follows the greeting, in order, and the game data has them as the race's lines for when it has no more to
	// say (subject 5), which the port only gave as the answer to a question it had no answer for
	bool IsTransmitter()
	{
		switch ( m_gdEncounter.m_race )
		{
			case GameData.Race.Minstrel:
			case GameData.Race.NomadProbe:
			case GameData.Race.NoahDerelict:
			case GameData.Race.Mysterion:

				return true;
		}

		return false;
	}

	// the next line of a transmission, in the order of the game data - null once all of it has been sent in this encounter
	GD_Comm FindNextTransmission()
	{
		var general = DataController.m_instance.m_playerData.m_general;
		var lines = new List<GD_Comm>();
		var endsCommLink = false;

		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( ( comm.m_race == m_gdEncounter.m_race ) && ( comm.m_subject == GD_Comm.Subject.NoMoreInformation ) )
			{
				lines.Add( comm );

				endsCommLink = endsCommLink || comm.m_endsCommLink;
			}
		}

		// a transmission with lines that end the comm link (each of the Nomad probes' planets, every second verse of the Minstrels' song) goes on from the line
		// after the last one sent, at this meeting or the next, and from the first again after the last: the shipped game says the line used least, and the line
		// that ends the link ends it (COMM-OV DO-UA)
		if ( endsCommLink )
		{
			var lastCommId = general.GetLastCommId( m_gdEncounter.m_race, GD_Comm.Subject.NoMoreInformation );
			var nextIndex = lines.FindIndex( line => line.m_id == lastCommId ) + 1;

			if ( nextIndex >= lines.Count )
			{
				nextIndex = 0;
			}

			general.SetLastCommId( m_gdEncounter.m_race, GD_Comm.Subject.NoMoreInformation, lines[ nextIndex ].m_id );
			m_pdEncounter.m_shownCommList.Add( lines[ nextIndex ].m_id );

			return lines[ nextIndex ];
		}

		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( ( comm.m_race == m_gdEncounter.m_race ) && ( comm.m_subject == GD_Comm.Subject.NoMoreInformation ) && !m_pdEncounter.m_shownCommList.Contains( comm.m_id ) )
			{
				m_pdEncounter.m_shownCommList.Add( comm.m_id );

				return comm;
			}
		}

		return null;
	}

	// a name in the aliens' own language, made of its syllables (the game data's garble words, which STRINFO's names are made of: the Veloxi's captain Finoctzi of
	// the ship Tochupu, Fi-noc-tzi and To-chu-pu, the Thrynn's captain Yrhey of the ship Iart'l, Yrh-ey and I-ar-t'l) - two or three syllables, for the captain (0)
	// or the ship (1) of an encounter, the same at every meeting (STRINFO gives other names for the same lines in brackets, so the original made them up too;
	// the number of syllables is the port's)
	string GetAlienName( int which )
	{
		var syllables = new List<string>();

		foreach ( var garble in DataController.m_instance.m_gameData.m_garbleList )
		{
			if ( ( garble.m_race == m_gdEncounter.m_race ) && !string.IsNullOrEmpty( garble.m_text ) )
			{
				syllables.Add( garble.m_text );
			}
		}

		if ( syllables.Count == 0 )
		{
			return "";
		}

		// (its own random numbers, so the game's do not move)
		var random = new System.Random( m_gdEncounter.m_id * 7 + which * 104729 + 1 );
		var count = random.Next( 2, 4 );
		var name = "";

		for ( var i = 0; i < count; i++ )
		{
			name += syllables[ random.Next( syllables.Count ) ];
		}

		return char.ToUpper( name[ 0 ] ) + name.Substring( 1 ).ToLower();
	}

	// a comm of the game data by its id, if it is one of the race of this encounter (null if it is not)
	GD_Comm FindRaceComm( int commId )
	{
		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( ( comm.m_id == commId ) && ( comm.m_race == m_gdEncounter.m_race ) )
			{
				return comm;
			}
		}

		return null;
	}

	// the aliens say a comm of their race's own (nothing if the game data does not have it)
	void SayRaceComm( int commId )
	{
		var comm = FindRaceComm( commId );

		if ( comm != null )
		{
			AddComm( GD_Comm.Subject.Custom, false, comm );
		}
	}

	// the question of a race's own that the aliens ask the first time they ask a question in an encounter - null if the race has none or has asked it
	GD_Comm FindSpecialQuestion()
	{
		if ( m_pdEncounter.m_specialQuestionAsked )
		{
			return null;
		}

		switch ( m_gdEncounter.m_race )
		{
			case GameData.Race.Elowan:

				return FindRaceComm( c_elowanEnergyQuestionCommId );

			case GameData.Race.Thrynn:

				return FindThrynnOffer();

			case GameData.Race.Velox:
			{
				// the most precious of what they want first
				var ship = DataController.m_instance.m_playerData.m_playerShip;

				if ( ship.HasArtifact( "Crystal Orb" ) )
				{
					return FindRaceComm( c_veloxCrystalOrbDemandCommId );
				}

				if ( ship.HasArtifact( "Ellipsoid" ) )
				{
					return FindRaceComm( c_veloxEllipsoidDemandCommId );
				}

				// the game data writes the amount of the tribute as X (STRINFO: "pay tribute of 3 Arth energy crystals")
				return WithAmount( FindRaceComm( c_veloxTributeDemandCommId ), "X", ( c_veloxTribute / 10 ).ToString() );
			}
		}

		return null;
	}

	// a copy of a comm with a placeholder of the game data filled in (the game data writes the amounts the aliens name as X and Y) - null for null
	static GD_Comm WithAmount( GD_Comm comm, string placeholder, string amount )
	{
		if ( comm == null )
		{
			return null;
		}

		return WithWord( comm, " " + placeholder + " ", " " + amount + " " );
	}

	// a copy of a comm with these words of its text replaced - null for null
	static GD_Comm WithWord( GD_Comm comm, string words, string replacement )
	{
		if ( comm == null )
		{
			return null;
		}

		return new GD_Comm( comm.m_text.Replace( words, replacement ) )
		{
			m_id = comm.m_id,
			m_race = comm.m_race,
			m_subject = comm.m_subject,
			m_stance = comm.m_stance
		};
	}

	// true if this encounter is a race's home fleet: it is met in orbit, and the game data has homeworld lines for its race (the Spemin, the Thrynn and the Veloxi,
	// whose lines STRINFO 2.7, 2.5 and 2.2 give "while in orbit of" their homeworlds - the encounters 139, 165 and 233)
	bool IsHomeFleet()
	{
		if ( ( m_pdEncounter == null ) || ( m_gdEncounter == null ) || ( m_pdEncounter.GetLocation() != PD_General.Location.InOrbit ) )
		{
			return false;
		}

		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( comm.m_homeworld && ( comm.m_race == m_gdEncounter.m_race ) )
			{
				return true;
			}
		}

		return false;
	}

	// the next warning of a home fleet: a hail or a statement from its homeworld hails, the answer to the player's hail from its homeworld responses, and the answer
	// to a question from its homeworld refusals ("Us are not answer the questions") - each list in the order of the game data, and every warning one step further
	// along than the one before, whichever list it comes from - the last line of a list is the last warning, and the fleet attacks after it ("You must pay the
	// consequences") - null if the game data has no homeworld line for the subject
	GD_Comm FindHomeworldWarning( GD_Comm.Subject subject )
	{
		GD_Comm.Subject homeworldSubject;

		if ( ( ( subject >= GD_Comm.Subject.Themselves ) && ( subject <= GD_Comm.Subject.TheAncients ) ) || ( subject == GD_Comm.Subject.NoMoreInformation ) )
		{
			homeworldSubject = GD_Comm.Subject.NoMoreInformation;
		}
		else if ( subject == GD_Comm.Subject.GreetingResponse )
		{
			homeworldSubject = GD_Comm.Subject.GreetingResponse;
		}
		else if ( ( subject == GD_Comm.Subject.GreetingHail ) || ( subject == GD_Comm.Subject.Statement ) || ( subject == GD_Comm.Subject.Question ) )
		{
			homeworldSubject = GD_Comm.Subject.GreetingHail;
		}
		else
		{
			return null;
		}

		// the homeworld lines of the race for this subject (the comm list is in the order of the ids)
		var warnings = new List<GD_Comm>();

		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( comm.m_homeworld && ( comm.m_race == m_gdEncounter.m_race ) && ( comm.m_subject == homeworldSubject ) )
			{
				warnings.Add( comm );
			}
		}

		if ( warnings.Count == 0 )
		{
			return null;
		}

		// one step further than the last warning (the last line once they have all been given)
		var index = Mathf.Clamp( m_pdEncounter.m_homeworldWarningCount, 0, warnings.Count - 1 );

		m_pdEncounter.m_homeworldWarningCount++;

		if ( index == warnings.Count - 1 )
		{
			m_pdEncounter.m_homeworldWarningsGiven = true;
		}

		return warnings[ index ];
	}

	// true if the race has a line on this subject in this posture (not a homeworld line, nor a surrender line unless the aliens are cowardly)
	bool HasLineIn( GameData.Race race, GD_Comm.Subject subject, GD_Comm.Stance stance )
	{
		foreach ( var comm in DataController.m_instance.m_gameData.m_commList )
		{
			if ( comm.m_homeworld || ( comm.m_surrender && !m_pdEncounter.m_aliensAreCowardly ) )
			{
				continue;
			}

			if ( ( comm.m_race == race ) && ( comm.m_subject == subject ) && comm.IsSaidIn( stance ) )
			{
				return true;
			}
		}

		return false;
	}

	// find a comm based on the subject
	GD_Comm FindComm( GD_Comm.Subject subject, bool outgoing )
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// which race and stance to use?
		GameData.Race race;
		GD_Comm.Stance stance;

		if ( outgoing )
		{
			// remember what the player just said
			m_pdEncounter.m_lastSubjectFromPlayer = subject;

			race = GameData.Race.Human;
			stance = m_pdEncounter.m_playerStance;

			if ( ( subject >= GD_Comm.Subject.Themselves ) && ( subject <= GD_Comm.Subject.TheAncients ) )
			{
				subject = GD_Comm.Subject.Question;
			}
		}
		else
		{
			race = m_gdEncounter.m_race;
			stance = GetAlienTalkStance();

			// a home fleet says only its homeworld's warnings
			if ( IsHomeFleet() )
			{
				var warning = FindHomeworldWarning( subject );

				if ( warning != null )
				{
					return warning;
				}
			}

			// the Elowan and the Thrynn greet a crew that is friends with their rival race with what they think of it, once
			if ( ( subject == GD_Comm.Subject.GreetingHail ) || ( subject == GD_Comm.Subject.GreetingResponse ) )
			{
				var rivalLine = FindRivalLine();

				if ( rivalLine != null )
				{
					m_pdEncounter.m_rivalLineSaid = true;

					return rivalLine;
				}
			}

			// cowardly aliens say each of their surrender lines for the subject first, in whichever stance the game data has it (the Thrynn's is neutral)
			if ( m_pdEncounter.m_aliensAreCowardly )
			{
				foreach ( var comm in gameData.m_commList )
				{
					if ( comm.m_surrender && ( comm.m_race == race ) && ( comm.m_subject == subject ) && !m_pdEncounter.m_shownCommList.Contains( comm.m_id ) )
					{
						m_pdEncounter.m_shownCommList.Add( comm.m_id );

						return comm;
					}
				}
			}
		}

		// go through each comm in the comm list and build a list of possible comms to choose from
		var possibleComms = new List<GD_Comm>();

		foreach ( var comm in gameData.m_commList )
		{
			if ( comm.m_homeworld || ( comm.m_surrender && !m_pdEncounter.m_aliensAreCowardly ) )
			{
				continue;
			}

			// in the speaker's posture: by the shipped game's posture mask of the line where it has one (COMM-OV, FIND-PHRASE), else by its stance
			if ( ( comm.m_race == race ) && ( comm.m_subject == subject ) && comm.IsSaidIn( stance ) )
			{
				possibleComms.Add( comm );
			}
		}

		// the aliens have no line on this subject in their posture: the original says one of their subject 5 lines in it instead (PHRASE-SEARCH)
		if ( ( possibleComms.Count == 0 ) && !outgoing && ( subject != GD_Comm.Subject.NoMoreInformation ) && HasLineIn( race, GD_Comm.Subject.NoMoreInformation, stance ) )
		{
			return FindComm( GD_Comm.Subject.NoMoreInformation, false );
		}

		// if we didn't find anything then try the neutral stance
		if ( possibleComms.Count == 0 )
		{
			foreach ( var comm in gameData.m_commList )
			{
				if ( comm.m_homeworld || ( comm.m_surrender && !m_pdEncounter.m_aliensAreCowardly ) )
				{
					continue;
				}

				if ( ( comm.m_race == race ) && ( comm.m_subject == subject ) && ( comm.m_stance == GD_Comm.Stance.Neutral ) )
				{
					possibleComms.Add( comm );
				}
			}
		}

		// if the subject is between 7 and 11 the comm is a response to a question, so show them in order
		if ( ( subject >= GD_Comm.Subject.Themselves ) && ( subject <= GD_Comm.Subject.TheAncients ) )
		{
			int lastCommId = playerData.m_general.GetLastCommId( race, subject );

			foreach ( var comm in possibleComms )
			{
				if ( comm.m_id > lastCommId )
				{
					playerData.m_general.SetLastCommId( m_gdEncounter.m_race, subject, comm.m_id );

					return comm;
				}
			}

			// they have run out of answers to this question so reset the list
			playerData.m_general.SetLastCommId( m_gdEncounter.m_race, subject, 0 );

			// return an "i dont know" response
			return FindComm( GD_Comm.Subject.NoMoreInformation, false );
		}

		// make sure we have something to pick from!
		if ( possibleComms.Count == 0 )
		{
			Debug.Log( "Whoops - no suitable comm found! (" + race + ", " + subject + ", " + stance + ")" );

			return new GD_Comm( c_noCommFoundText );
		}

		// have we shown all possible comms for this subject?
		var alreadyShownThemAll = true;

		foreach ( var comm in possibleComms )
		{
			if ( !m_pdEncounter.m_shownCommList.Contains( comm.m_id ) )
			{
				alreadyShownThemAll = false;
				break;
			}
		}

		// if we have shown them all then reset them all
		if ( alreadyShownThemAll )
		{
			foreach ( var comm in possibleComms )
			{
				m_pdEncounter.m_shownCommList.Remove( comm.m_id );
			}
		}

		// remove all of the ones we've shown before from the list
		for ( var i = 0; i < possibleComms.Count; i++ )
		{
			if ( m_pdEncounter.m_shownCommList.Contains( possibleComms[ i ].m_id ) )
			{
				possibleComms.RemoveAt( i );

				i--;
			}
		}

		// now pick and return a random one - but the Noah 9 derelict's in the order of the game data: the shipped game says the line of a subject used least, the
		// first of them on a tie (FIND-PHRASE in COMM-OV, the decompiled original), so the officer's "we're picking up a distress beacon signal" comes before the
		// SOS, and then they take turns (STRINFO 2.11 prints them in that order)
		var randomNumber = ( race == GameData.Race.NoahDerelict ) ? 0 : Random.Range( 0, possibleComms.Count );

		var selectedComm = possibleComms[ randomNumber ];

		m_pdEncounter.m_shownCommList.Add( selectedComm.m_id );

		return selectedComm;
	}

	// adds some text to the conversation
	public void AddComm( GD_Comm.Subject subject, bool outgoing, GD_Comm forcedComm = null )
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the captains's personnel file
		var personnelFile = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.Captain );

		// remember what the player said last (looking for an outgoing comm changes it)
		var lastSubjectFromPlayer = m_pdEncounter.m_lastSubjectFromPlayer;

		// find a comm (or say the one the caller wants said)
		var comm = ( forcedComm != null ) ? forcedComm : FindComm( subject, outgoing );

		// the player has no statement to make in a posture the game data has no statements for (it has none for the neutral posture) - transmit nothing then
		// (this used to transmit the word ERROR)
		if ( outgoing && ( subject == GD_Comm.Subject.Statement ) && ( comm.m_text == c_noCommFoundText ) )
		{
			// nothing was said
			m_pdEncounter.m_lastSubjectFromPlayer = lastSubjectFromPlayer;

			return;
		}

		// nor do the aliens say anything in a posture the game data has no line for (the Spemin have no neutral statements) - this showed the word ERROR - but
		// aliens with no line to end the comm link with still end it
		if ( !outgoing && ( comm.m_text == c_noCommFoundText ) )
		{
			if ( subject == GD_Comm.Subject.Terminate )
			{
				Disconnect();
			}

			return;
		}

		// a statement or a question of the player's moves the aliens' mood
		if ( outgoing && ( ( subject == GD_Comm.Subject.Statement ) || ( ( subject >= GD_Comm.Subject.Themselves ) && ( subject <= GD_Comm.Subject.TheAncients ) ) ) )
		{
			ApplyPlayerPosture( subject != GD_Comm.Subject.Statement );
		}

		// and so does the player's answer to their question
		if ( outgoing && ( ( subject == GD_Comm.Subject.Yes ) || ( subject == GD_Comm.Subject.No ) ) )
		{
			ApplyAnswer( subject == GD_Comm.Subject.Yes );
		}

		// and the hails: the player's reply to the aliens' hail has the posture's full effect and counts as a phrase, a hail of the player's before the aliens have
		// hailed makes the player the hailer, whose postures each have their full effect once, and the aliens' second and third hail without a reply anger them by 4
		// and by 9 (COMM-OV >RESPOND and RESPOND>EDL, HAILER and NEW-POSTURE>EDL, <TALK> and +4/9>EDL)
		if ( outgoing && ( subject == GD_Comm.Subject.GreetingResponse ) )
		{
			m_pdEncounter.m_playerPhrases++;
			m_pdEncounter.m_alienHails = 0;

			ApplyFullPostureEffect();
		}
		else if ( outgoing && ( subject == GD_Comm.Subject.GreetingHail ) )
		{
			if ( !m_pdEncounter.m_aliensWantToConnect )
			{
				m_pdEncounter.m_playerHailedFirst = true;
			}

			ApplyNewPosture();
		}
		else if ( !outgoing && ( subject == GD_Comm.Subject.GreetingHail ) )
		{
			m_pdEncounter.m_alienHails++;

			if ( ( m_pdEncounter.m_alienHails >= 2 ) && ( m_pdEncounter.m_alienHails < 4 ) )
			{
				ChangeMood( m_pdEncounter.m_alienHails * m_pdEncounter.m_alienHails );
			}
		}

		// copy the comm text (because we will replace tokens)
		var text = comm.m_text;

		// replace tokens with values
		text = text.Replace( "&", "ISS " + playerData.m_playerShip.m_name );
		text = text.Replace( "*", personnelFile.m_name );

		// and the names of the aliens' captain (%) and ship (+)
		if ( !outgoing && ( text.Contains( "%" ) || text.Contains( "+" ) ) )
		{
			text = text.Replace( "%", GetAlienName( 0 ) ).Replace( "+", GetAlienName( 1 ) );
		}

		// check for the slash token (replace with subject)
		if ( text.Contains( "/" ) )
		{
			// process tokens
			if ( m_pdEncounter.m_lastSubjectFromPlayer != GD_Comm.Subject.GeneralInfo )
			{
				string askingAbout = null;

				switch ( m_pdEncounter.m_lastSubjectFromPlayer )
				{
					case GD_Comm.Subject.Themselves:
						askingAbout = "your race";
						break;

					case GD_Comm.Subject.OtherRaces:
						askingAbout = "other races";
						break;

					case GD_Comm.Subject.OldEmpire:
						askingAbout = "the Old Empire";
						break;

					case GD_Comm.Subject.TheAncients:
						askingAbout = "the Ancients";
						break;
				}

				text = text.Replace( "(", "" );
				text = text.Replace( ")", "" );
				text = text.Replace( "/", askingAbout );
			}
			else
			{
				var regex = new Regex( @"\(([^\}]+)\)" );

				text = regex.Replace( text, "" );
			}
		}

		// is this an outgoing message (from player)?
		if ( outgoing )
		{
			// yes - update timer so alien response doesn't take too long
			m_pdEncounter.m_conversationTimer += 5.0f;

			if ( m_pdEncounter.m_conversationTimer > 10.0f )
			{
				m_pdEncounter.m_conversationTimer = Random.Range( 5.0f, 10.0f );
			}

			// finalize text
			text = "<color=white>Transmitting:</color>\n<color=#0aa>" + text + "</color>";
		}
		else
		{
			// get the comm text, with the name of the captain and of the ship filled in (this started again from the text
			// in the game data, so what the aliens said still had the * and the & in it)
			var commText = text;

			// get possible garble words for this race
			var possibleGarbles = new List<GD_Garble>();

			foreach ( var garble in gameData.m_garbleList )
			{
				if ( garble.m_race == m_gdEncounter.m_race )
				{
					possibleGarbles.Add( garble );
				}
			}

			// the Whining Orb translates the language of the Spemin (the Thrynn: "There is an orb device which makes a queer whining sound. This translates the
			// Spemin language.") - nothing they say is garbled with it aboard
			var translated = ( m_gdEncounter.m_race == GameData.Race.Spemin ) && playerData.m_playerShip.HasArtifact( "Whining Orb" );

			// and a crew member of their own race understands every word: "all phrases will be un-scrambled if any member of the player's crew is the same race as
			// the race of the alien" (compart.txt 13; alpha.txt: the officer's skill and race affect the translation - the owner's ruling of 2026-10-11)
			translated = translated || playerData.m_crewAssignment.HasCrewOfRace( m_gdEncounter.m_race );

			// did we find any?
			if ( ( possibleGarbles.Count > 0 ) && !translated )
			{
				// yes - get the personnel file of the comm officer
				personnelFile = playerData.m_crewAssignment.GetPersonnelFile( PD_CrewAssignment.Role.CommunicationsOfficer );

				// compute the training level (in 0 to 1 range)
				var trainingLevel = personnelFile.m_communications / 250.0f;

				// damaged comm equipment garbles more: the officer's training counts in proportion to its health
				trainingLevel *= playerData.m_playerShip.GetSystemFactor( PD_PlayerShip.ShipSystem.Comm );

				// create and seed the noise generator
				var fastNoise = new FastNoise( comm.m_id );

				fastNoise.SetNoiseType( FastNoise.NoiseType.Value );
				fastNoise.SetFrequency( 1.0f );

				// split the text into words
				var splitCommText = commText.Split();

				// start building the new comm text
				var garbledCommText = new List<string>();

				// go through each word
				var commWordIndex = 0;
				var lastWordHadSpecialCharacter = false;
				var lastWordWasGarbled = false;

				foreach ( var commWord in splitCommText )
				{
					// two spaces in a row leave an empty word between them (the captain's name is used as it was typed, and can have them) -
					// there is nothing to show for it, and the code below looks at the last character of every word
					if ( commWord.Length == 0 )
					{
						continue;
					}

					commWordIndex++;

					// get the difficulty level of this word
					var difficulty = fastNoise.GetNoise( commWordIndex, 0 ) * 0.45f + 0.5f;

					// is this word too hard for our poor comm officer?
					if ( difficulty > trainingLevel )
					{
						// yes - pick number of syllables (based on race)
						int minSyllables = 1;
						int maxSyllables = 1;

						switch ( m_gdEncounter.m_race )
						{
							case GameData.Race.Gazurtoid:
								minSyllables = 5;
								maxSyllables = 10;
								break;

							case GameData.Race.Mysterion:
								minSyllables = 5;
								maxSyllables = 5;
								break;

							case GameData.Race.Spemin:
								minSyllables = 1;
								maxSyllables = 3;
								break;

							case GameData.Race.Velox:
								minSyllables = 1;
								maxSyllables = 5;
								break;

							case GameData.Race.Elowan:
								minSyllables = 1;
								maxSyllables = 4;
								break;

							case GameData.Race.Thrynn:
								minSyllables = 1;
								maxSyllables = 5;
								break;
						}

						var syllables = minSyllables + Mathf.FloorToInt( 0.25f + ( ( maxSyllables + 0.5f ) * ( fastNoise.GetNoise( 0, commWordIndex ) * 0.5f + 0.5f ) ) );

						// start building the garble word
						var garbledWord = "";

						for ( var syllableIndex = 0; syllableIndex < syllables; syllableIndex++ )
						{
							var garbleIndex = Mathf.FloorToInt( Mathf.Lerp( 0, possibleGarbles.Count, fastNoise.GetNoise( commWordIndex, syllableIndex ) * 0.5f + 0.5f ) );

							garbledWord += possibleGarbles[ garbleIndex ].m_text;
						}

						// fix case of word
						garbledWord = garbledWord.ToLower();

						if ( garbledWord.Length > 0 && ( ( garbledCommText.Count == 0 ) || lastWordHadSpecialCharacter ) )
						{
							garbledWord = garbledWord[ 0 ].ToString().ToUpper() + garbledWord.Substring( 1 );
						}

						// add on the garbled word
						garbledCommText.Add( garbledWord );

						// this word does not have a special character
						lastWordHadSpecialCharacter = false;

						// this is a garbled word
						lastWordWasGarbled = true;
					}
					else
					{
						// no - just add on the word
						garbledCommText.Add( commWord );

						// check if this word has a special character at the end (so the next garbled word starts a new sentence)
						lastWordHadSpecialCharacter = !System.Char.IsLetter( commWord[ commWord.Length - 1 ] );

						// this isn't a garbled word
						lastWordWasGarbled = false;
					}
				}

				// put everything back together
				commText = System.String.Join( " ", garbledCommText );

				// add a period (or a question mark) if the last word was garbled
				if ( lastWordWasGarbled )
				{
					commText += ( subject == GD_Comm.Subject.Question ) ? "?" : ".";
				}
			}

			// finalize text
			text = "<color=white>Receiving:</color>\n<color=#0a0>" + commText + "</color>";

			// was this a question?
			if ( subject == GD_Comm.Subject.Question )
			{
				// yes - change the button set
				SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.AnswerQuestion );

				// forget the last subject from the player
				m_pdEncounter.m_lastSubjectFromPlayer = GD_Comm.Subject.None;

				// remember the question from the aliens
				m_pdEncounter.m_lastQuestionFromAliens = comm.m_id;
			}

			// was this an answer?
			if ( ( comm.m_subject >= GD_Comm.Subject.Themselves ) && ( comm.m_subject <= GD_Comm.Subject.TheAncients ) )
			{
				// yes -add it to the ships log
				playerData.m_shipsLog.AddAlienComm( comm, commText );
			}
		}

		// update the message
		SpaceflightController.m_instance.m_messages.AddText( text );

		// play the beep sound
		SoundController.m_instance.PlaySound( SoundController.Sound.Beep );

		// are we trying to connect?
		if ( ( subject == GD_Comm.Subject.GreetingHail ) || ( subject == GD_Comm.Subject.GreetingResponse ) )
		{
			if ( outgoing )
			{
				// allow the aliens to connect to the player
				m_pdEncounter.m_playerWantsToConnect = true;
			}
			else
			{
				// allow the player to connect to the aliens
				m_pdEncounter.m_aliensWantToConnect = true;
			}

			// do both sides want to connect?
			if ( m_pdEncounter.m_playerWantsToConnect && m_pdEncounter.m_aliensWantToConnect )
			{
				// yes - make it happen
				Connect();
			}
		}

		// what saying the line does in the shipped game, for a line of the aliens' (COMM-OV DO-UA): it moves their mood (the Spemin's "Enough jabbering. It is
		// time for a glorious Spemin victory!" by 100, to fight), or it ends the comm link
		if ( !outgoing )
		{
			ChangeMood( comm.m_moodAction );
		}

		// did we want the communications to terminate?
		if ( subject == GD_Comm.Subject.Terminate )
		{
			// yes - get out of video chat
			Disconnect();
		}
		else if ( !outgoing && comm.m_endsCommLink && m_pdEncounter.m_connected )
		{
			Disconnect();
		}
	}

	// the model of the race of the aliens in this encounter, for the comm screen - null if the race has none (or no slot in the list)
	GameObject GetAlienRaceModel()
	{
		var race = (int) m_gdEncounter.m_race;

		if ( ( m_alienRaceModelList == null ) || ( race < 0 ) || ( race >= m_alienRaceModelList.Length ) )
		{
			return null;
		}

		return m_alienRaceModelList[ race ];
	}

	// connect the aliens and the players
	public void Connect()
	{
		// hide the player ship and camera
		SpaceflightController.m_instance.m_playerCamera.Hide();
		SpaceflightController.m_instance.m_playerShip.Hide();

		// hide the encounter location
		m_main.SetActive( false );

		// instantly black out the viewer
		SpaceflightController.m_instance.m_viewport.StartFade( 0.0f, 0.0f );

		// show the race of the aliens in this encounter (the list has no slot for the races after the Minstrels, such as the Noah 9 derelict)
		var raceModel = GetAlienRaceModel();

		if ( raceModel != null )
		{
			raceModel.SetActive( true );
		}
		else
		{
			Debug.Log( "Sorry, alien race " + m_gdEncounter.m_race + " is not available to display yet!" );
		}

		// are the aliens trying to ask a question that we haven't answered yet?
		if ( ( m_pdEncounter.m_lastQuestionFromAliens != 0 ) && ( m_pdEncounter.m_lastSubjectFromPlayer == GD_Comm.Subject.None ) )
		{
			// yes - change to the answer question button set
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.AnswerQuestion );
		}
		else
		{
			// no - change to the normal comm buttons
			SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Comm );
		}

		// aliens with a personality say only so many phrases, picked by their posture as the conversation begins (compart.txt 11)
		if ( AlienPersonality.Find( m_gdEncounter.m_race ) != null )
		{
			m_pdEncounter.m_alienPhrasesLeft = GetPhraseLimit( GetAlienTalkStance() );
		}

		// the first comm link with the Thrynn or the Elowan is remembered for the rest of the game
		var playerGeneral = DataController.m_instance.m_playerData.m_general;

		if ( ( playerGeneral.m_firstContactRace == GameData.Race.None ) && ( GetRivalRace( m_gdEncounter.m_race ) != GameData.Race.None ) )
		{
			playerGeneral.m_firstContactRace = m_gdEncounter.m_race;
		}

		// we are connected now
		m_pdEncounter.m_connected = true;
	}

	// disconnect the aliens and the players
	public void Disconnect()
	{
		// hide the race of the aliens in this encounter
		var raceModel = GetAlienRaceModel();

		if ( raceModel != null )
		{
			raceModel.SetActive( false );
		}

		// show the encounter location
		m_main.SetActive( true );

		// show the player camera and ship
		SpaceflightController.m_instance.m_playerCamera.Show();
		SpaceflightController.m_instance.m_playerShip.Show();

		// change the buttons
		SpaceflightController.m_instance.m_buttonController.ChangeButtonSet( ButtonController.ButtonSet.Bridge );

		// we are not connected any more
		m_pdEncounter.m_connected = false;
		m_pdEncounter.m_disconnected = true;

		// the end of every comm link, in the original's words (COMM-OV: "COMMUNICATIONS HAVE BEEN TERMINATED")
		SpaceflightController.m_instance.m_messages.AddText( "<color=white>Communications have been terminated.</color>" );

		// no one wants to connect any more
		m_pdEncounter.m_playerWantsToConnect = false;
		m_pdEncounter.m_aliensWantToConnect = false;

		// the next comm link counts its phrases and its hailer again (COMM-OV clears P-PHRASES and HAILER at the end of a link)
		m_pdEncounter.m_playerPhrases = 0;
		m_pdEncounter.m_playerHailedFirst = false;
	}

	// call this to find out if there are living alien ships in the encounter
	public bool HasLivingAlienShips()
	{
		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		// go through each alien ship
		foreach ( var alienShip in alienShipList )
		{
			// has this alien ship been added to the encounter?
			if ( alienShip.m_addedToEncounter )
			{
				// is this alien ship still living?
				if ( !alienShip.m_isDead )
				{
					// yep!
					return true;
				}
			}
		}

		// nope!
		return false;
	}

	// whether or not the aliens want to connect
	public bool AliensWantToConnect()
	{
		return m_pdEncounter.m_aliensWantToConnect;
	}

	// whether or not we are connected to the aliens
	public bool IsConnected()
	{
		return m_pdEncounter.m_connected;
	}

	// hail (or respond to) the alien ships
	public void Hail( GD_Comm.Stance stance, bool responding )
	{
		// set the stance to what the player selected
		m_pdEncounter.m_playerStance = stance;

		// a posture the player has not used yet, after hailing first, moves the aliens' mood (the hail itself does it in AddComm)
		if ( m_pdEncounter.m_connected )
		{
			ApplyNewPosture();
		}

		// have we connected already?
		if ( !m_pdEncounter.m_connected )
		{
			// play communication chirp sound
			SoundController.m_instance.PlaySound( SoundController.Sound.CommChirp );

			// no - send the hail (responding or hailing)
			AddComm( responding ? GD_Comm.Subject.GreetingResponse : GD_Comm.Subject.GreetingHail, true );
		}
	}

	// return the distance to the nearest alien ship
	float ClosestDistanceToAlienShip()
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// closest distance is "infinity"
		var closestDistance = float.MaxValue;

		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		foreach ( var alienShip in alienShipList )
		{
			if ( alienShip.m_addedToEncounter && !alienShip.m_isDead )
			{
				var distance = Vector3.Distance( alienShip.m_coordinates, playerData.m_general.m_coordinates );

				if ( distance < closestDistance )
				{
					closestDistance = distance;
				}
			}
		}

		return closestDistance;
	}

	// update the current scanner selection (0 = player's ship, 1...n = alien ships)
	public int UpdateScannerSelection( int currentSelection, int selectChangeDirection )
	{
		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		// do we have the player's ship currently selected?
		if ( currentSelection == 0 )
		{
			// yes - move the current selection to the beginning or the ending of the alien ship list
			if ( selectChangeDirection > 0 )
			{
				currentSelection = -1;
			}
			else
			{
				currentSelection = alienShipList.Length;
			}
		}
		else
		{
			currentSelection--;
		}

		do
		{
			currentSelection += selectChangeDirection;

			if ( ( currentSelection < 0 ) || ( currentSelection == alienShipList.Length ) )
			{
				return 0;
			}
		}
		while ( alienShipList[ currentSelection ].m_addedToEncounter == false );

		return currentSelection + 1;
	}

	// gets the coordinates for the current selection in the encounter
	public Vector3 GetScannerPosition( int currentSelection )
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		// is the current selection the player's ship?
		if ( currentSelection == 0 )
		{
			// yes - just return the position of the player
			return playerData.m_general.m_coordinates;
		}

		// return the position of the alien ship
		return alienShipList[ currentSelection - 1 ].m_coordinates;
	}

	// start the scanning cinematic
	public void StartScanning( int currentSelection )
	{
		// get to the list of alien ships
		var alienShipList = m_pdEncounter.GetAlienShipList();

		// was the player ship selected?
		if ( currentSelection == 0 )
		{
			// yes - let the player know
			SoundController.m_instance.PlaySound( SoundController.Sound.Error );

			SpaceflightController.m_instance.m_messages.Clear();
			SpaceflightController.m_instance.m_messages.AddText( "<color=red>That is your ship.</color>" );

			// deactivate the sensor button
			SpaceflightController.m_instance.m_buttonController.DeactivateButton();

			// show the status display
			SpaceflightController.m_instance.m_displayController.ChangeDisplay( SpaceflightController.m_instance.m_displayController.m_statusDisplay );
		}
		else
		{
			// is the alien ship dead?
			if ( alienShipList[ currentSelection - 1 ].m_isDead )
			{
				// yes - scan the debris for salvage
				var scanType = SensorsDisplay.ScanType.Debris;

				// get the vessel data for the destroyed ship
				var gameData = DataController.m_instance.m_gameData;
				var vesselId = alienShipList[ currentSelection - 1 ].m_vesselId;
				var vessel = gameData.m_vesselList[ vesselId ];

				// calculate salvage value based on vessel type (mineral density represents salvage potential)
				var salvageDensity = UnityEngine.Mathf.Clamp( vessel.m_mass / 5, 10, 80 );

				// start the debris scan (with the picture of what is left of this vessel, and the mass of the vessel in tons)
				SpaceflightController.m_instance.m_displayController.m_sensorsDisplay.StartScanning( scanType, 0, vessel.m_mass, 0, salvageDensity, vesselId );
			}
			else
			{
				// no - get the vessel id
				var vesselId = alienShipList[ currentSelection - 1 ].m_vesselId;
				var scanType = (SensorsDisplay.ScanType) vesselId;

				// get the vessel data (the sensors read its mass in tons, its bio density and its energy, as in the original)
				var gameData = DataController.m_instance.m_gameData;

				if ( ( vesselId >= 0 ) && ( vesselId < gameData.m_vesselList.Length ) )
				{
					var vessel = gameData.m_vesselList[ vesselId ];

					// start the scan
					SpaceflightController.m_instance.m_displayController.m_sensorsDisplay.StartScanning( scanType, 0, vessel.m_mass, vessel.m_bioDensity, vessel.m_mineralDensity );
				}
				else
				{
					// a vessel we have no data for (start the scan with nothing to read)
					SpaceflightController.m_instance.m_displayController.m_sensorsDisplay.StartScanning( scanType, 0, 0, 0, 0 );
				}
			}
		}
	}
}
