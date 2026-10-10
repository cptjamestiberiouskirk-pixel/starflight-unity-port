using UnityEngine;

// the Black Egg, the planet bomb of the Old Empire. "To activate it, you must drop it" (the Starport's analysis): dropped by the terrain vehicle it lies armed on the
// planet (the original's kernel keeps the planet of a black egg that has been dropped and armed, disys.txt ?BOMB). Once the ship is back up above the planet the egg
// counts down and destroys it (the original's screens, SS Story: "Captain, we are receiving a countdown transmission from the Black Egg...", 5 to 1, "Boom!")
public class BlackEgg
{
	// the control nexus of the Crystal Planet, the only point where it is vulnerable (STRINFO 2.12 and 5.1: 47N x 45E)
	public const float c_nexusLatitude = 47.0f;
	public const float c_nexusLongitude = 45.0f;

	// how close to the nexus the egg has to lie, in degrees north-south and east-west (the port's choice: the terrain vehicle's display shows whole degrees)
	public const float c_nexusTolerance = 1.0f;

	// Elan, the homeworld of the Elowan, and the planet of the Uhlek mind-ganglion (STRINFO 5.1)
	const int c_elanStarX = 148;
	const int c_elanStarY = 63;
	const int c_elanPlanetFromSun = 2;
	const int c_uhlekStarX = 55;
	const int c_uhlekStarY = 32;
	const int c_uhlekPlanetFromSun = 2;

	// the numbers of the countdown, and the time between two of them (in seconds)
	const int c_countdownStart = 5;
	const float c_countdownInterval = 1.0f;

	// the dropped egg that is counting down (-1 for none)
	int m_droppedCargoId = -1;

	// the next number of the countdown (0 when the next thing is the explosion)
	int m_countdown;

	// the time until the next number
	float m_timer;

	// call this every frame the game is not paused
	public void Update()
	{
		var spaceflightController = SpaceflightController.m_instance;
		var playerData = DataController.m_instance.m_playerData;
		var gameData = DataController.m_instance.m_gameData;

		// the egg counts down only while the ship is above the planets of its star system: in orbit or in the star system, not while it launches or lands, not in an encounter
		var location = playerData.m_general.m_location;

		if ( ( location != PD_General.Location.InOrbit ) && ( location != PD_General.Location.StarSystem ) )
		{
			return;
		}

		if ( spaceflightController.m_playerCamera.IsLaunchingOrLanding() || spaceflightController.m_combatController.PlayerIsDestroyed() || ( playerData.m_planetSurfaces == null ) )
		{
			return;
		}

		// is an egg counting down?
		var droppedCargo = ( m_droppedCargoId < 0 ) ? null : playerData.m_planetSurfaces.FindDroppedCargo( m_droppedCargoId );

		if ( droppedCargo == null )
		{
			// no - is there one lying armed on a planet of this star system?
			droppedCargo = FindArmedEgg( playerData.m_general.m_currentStarId );

			if ( droppedCargo == null )
			{
				m_droppedCargoId = -1;

				return;
			}

			// yes - its countdown begins
			m_droppedCargoId = droppedCargo.m_id;
			m_countdown = c_countdownStart;
			m_timer = c_countdownInterval;

			AddStoryText( "BlackEggCountdown" );

			return;
		}

		// the countdown goes on only in the egg's star system
		if ( ( droppedCargo.m_planetId < 0 ) || ( droppedCargo.m_planetId >= gameData.m_planetList.Length ) || ( gameData.m_planetList[ droppedCargo.m_planetId ].m_starId != playerData.m_general.m_currentStarId ) )
		{
			return;
		}

		m_timer -= Time.deltaTime;

		if ( m_timer > 0.0f )
		{
			return;
		}

		m_timer += c_countdownInterval;

		// the next number
		if ( m_countdown > 0 )
		{
			spaceflightController.m_messages.AddText( "<color=red>" + m_countdown + "</color>" );

			m_countdown--;

			return;
		}

		// boom
		m_droppedCargoId = -1;

		spaceflightController.m_messages.AddText( "<color=red>BOOM!</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Explosion );

		Detonate( droppedCargo );
	}

	// the dropped Black Egg on a planet of this star (null if there is none)
	static PD_PlanetSurfaces.DroppedCargo FindArmedEgg( int starId )
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var blackEggId = gameData.FindArtifactId( "Black Egg" );

		if ( blackEggId < 0 )
		{
			return null;
		}

		foreach ( var droppedCargo in playerData.m_planetSurfaces.m_droppedCargoList )
		{
			if ( ( droppedCargo.m_artifactId != blackEggId ) || ( droppedCargo.m_planetId < 0 ) || ( droppedCargo.m_planetId >= gameData.m_planetList.Length ) )
			{
				continue;
			}

			if ( ( gameData.m_planetList[ droppedCargo.m_planetId ].m_starId == starId ) && !playerData.m_planetSurfaces.IsPlanetDestroyed( droppedCargo.m_planetId ) )
			{
				return droppedCargo;
			}
		}

		return null;
	}

	// the egg goes off
	static void Detonate( PD_PlanetSurfaces.DroppedCargo droppedCargo )
	{
		var gameData = DataController.m_instance.m_gameData;
		var playerData = DataController.m_instance.m_playerData;
		var planetId = droppedCargo.m_planetId;
		var planet = gameData.m_planetList[ planetId ];

		// the Crystal Planet is vulnerable only at its control nexus: anywhere else the egg damages it but does not destroy it (STRINFO 2.12)
		if ( planet.IsCrystalPlanet() && !IsAtTheNexus( droppedCargo ) )
		{
			playerData.m_planetSurfaces.RemoveDroppedCargo( droppedCargo.m_id );

			AddStoryText( "CrystalPlanetDamaged" );

			return;
		}

		// the planet is gone, and what was dropped on it with it
		playerData.m_planetSurfaces.DestroyPlanet( planetId );

		var spaceflightController = SpaceflightController.m_instance;

		spaceflightController.m_starSystem.RemoveDestroyedPlanet( planetId );

		// what comes of it
		if ( planet.IsCrystalPlanet() )
		{
			// the game is won: no star flares any more, and Interstel pays its bonus when the ship is back at the Starport
			playerData.m_general.m_gameWon = true;
			playerData.m_general.m_winBonusPending = true;

			AddStoryText( "CrystalPlanetDestroyed" );
		}
		else if ( planetId == gameData.FindPlanetFromSun( c_elanStarX, c_elanStarY, c_elanPlanetFromSun ) )
		{
			AddStoryText( "ElanDestroyed" );
		}
		else if ( planetId == gameData.FindPlanetFromSun( c_uhlekStarX, c_uhlekStarY, c_uhlekPlanetFromSun ) )
		{
			AddStoryText( "UhlekMindGanglionDestroyed" );
		}

		// a ship in orbit around it is in the star system now
		if ( ( playerData.m_general.m_location == PD_General.Location.InOrbit ) && ( playerData.m_general.m_currentPlanetId == planetId ) )
		{
			spaceflightController.SwitchLocation( PD_General.Location.StarSystem );
		}
	}

	// true if the egg lies at the control nexus of the Crystal Planet
	public static bool IsAtTheNexus( PD_PlanetSurfaces.DroppedCargo droppedCargo )
	{
		// the port's world x is east-west and z north-south (see Tools.LatLongToWorldCoordinates)
		Tools.WorldToLatLongCoordinates( new Vector3( droppedCargo.m_x, droppedCargo.m_y, droppedCargo.m_z ), out var eastLongitude, out var northLatitude );

		return ( Mathf.Abs( northLatitude - c_nexusLatitude ) <= c_nexusTolerance ) && ( Mathf.Abs( eastLongitude - c_nexusLongitude ) <= c_nexusTolerance );
	}

	// shows a text of the original game in the messages
	static void AddStoryText( string key )
	{
		var storyText = DataController.m_instance.m_gameData.FindStoryText( key );

		if ( storyText != null )
		{
			SpaceflightController.m_instance.m_messages.AddText( "<color=yellow>" + storyText.m_text + "</color>" );
		}
	}
}
