using UnityEngine;

// the terrain vehicle's mining laser (since 2026-10-17, in place of the Cargo button's one-press pickup of a deposit): Mine aims a beam at the nearest mineral
// deposit within its reach and cuts it into the vehicle's hold a tenth of a cubic meter at a time, on the vehicle's energy, until the deposit is gone, the hold is
// full or the energy runs out, or until the fire button is pressed again. The 1984 design notes (planet.txt) mine the same way, the button held down while the
// minerals come aboard a tenth at a time. The deposits, the hold and the values stay the original's
public class TVMineButton : ShipButton
{
	// how far the mining laser reaches (the vehicle stops beside the deposit)
	public const float c_mineRange = 20.0f;

	// how fast the laser cuts, in tenths of a cubic meter a second (the largest deposit, 9.9 cubic meters, in under seven seconds)
	public const float c_tenthsPerSecond = 15.0f;

	// the energy a tenth of a cubic meter takes, of the vehicle's tank (filling the hold of 50 cubic meters takes about a sixth of it)
	public const float c_energyPerTenth = 10.0f / TerrainVehicle.c_tankUnits;

	// how often the beam is renewed while it cuts, and how long each pulse lasts (they overlap, so the beam pulses without going out)
	const float c_pulseSeconds = 0.12f;
	const float c_pulseLifetime = 0.2f;

	// how big the beam is drawn next to the ships' lasers in space, its colour, and how high above the vehicle and the deposit it runs
	const float c_beamScale = 0.15f;
	static readonly Color c_beamColor = new Color( 1.0f, 0.55f, 0.15f, 1.0f );
	const float c_vehicleBeamHeight = 3.0f;
	const float c_depositBeamHeight = 0.5f;

	// the beam, made the first time it is needed and kept with the disembarked location
	static LaserBeam s_beam;

	// the deposit being cut, its size when the cutting began, and its volume then
	TerrainElement m_target;
	Vector3 m_startScale;
	int m_startVolume;

	// how much has come aboard, the time to the next tenth and to the next pulse of the beam
	int m_mined;
	float m_cutTimer;
	float m_pulseTimer;

	public override string GetLabel()
	{
		return "Mine";
	}

	public override bool Execute()
	{
		var terrainVehicle = DataController.m_instance.m_playerData.m_terrainVehicle;
		var messages = SpaceflightController.m_instance.m_messages;

		messages.Clear();

		var target = FindNearestDeposit( c_mineRange );

		if ( target == null )
		{
			return Refuse( "There is no mineral deposit within reach of the mining laser." );
		}

		if ( terrainVehicle.GetRemainingVolume() <= 0 )
		{
			return Refuse( "The cargo hold is full." );
		}

		if ( terrainVehicle.IsOutOfFuel() )
		{
			return Refuse( "There is not enough energy to run the mining laser." );
		}

		m_target = target;
		m_startScale = target.transform.localScale;
		m_startVolume = Mathf.Max( 1, target.m_volume );
		m_mined = 0;
		m_cutTimer = 0.0f;
		m_pulseTimer = 0.0f;

		messages.AddText( "<color=white>Mining " + target.GetElementName() + " (" + Tools.VolumeToText( target.m_volume ) + " cubic meters)...</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Scanning, 0.6f, 1.0f, true );

		// keep the button lit while the laser cuts
		return true;
	}

	public override bool Update()
	{
		// (Execute sets the deposit before the first update; a button left over from before has none)
		if ( m_target == null )
		{
			Stop( null );

			return true;
		}

		// the fire button stops the laser
		if ( InputController.m_instance.m_submit )
		{
			InputController.m_instance.Debounce();

			Stop( "Mining stopped." );

			return true;
		}

		var playerData = DataController.m_instance.m_playerData;
		var terrainVehicle = playerData.m_terrainVehicle;

		// the beam, renewed in pulses while it cuts
		m_pulseTimer -= Time.deltaTime;

		if ( m_pulseTimer <= 0.0f )
		{
			m_pulseTimer = c_pulseSeconds;

			FireBeam();
		}

		// a tenth of a cubic meter aboard at each step of the cut (a long frame takes several, never more than a second's worth)
		m_cutTimer += Time.deltaTime;

		var step = 1.0f / c_tenthsPerSecond;

		for ( var i = 0; ( i < (int) c_tenthsPerSecond ) && ( m_cutTimer >= step ); i++ )
		{
			m_cutTimer -= step;

			if ( terrainVehicle.GetRemainingVolume() <= 0 )
			{
				Stop( "The cargo hold is full." );

				return true;
			}

			if ( terrainVehicle.IsOutOfFuel() )
			{
				Stop( "There is not enough energy to run the mining laser." );

				return true;
			}

			terrainVehicle.AddElement( m_target.m_elementId, 1 );
			terrainVehicle.UseUpFuel( c_energyPerTenth );

			m_target.m_volume--;
			m_mined++;

			// what is left of the deposit is remembered at every step, so a save made while the laser cuts matches the hold
			if ( playerData.m_planetSurfaces != null )
			{
				playerData.m_planetSurfaces.SetDepositVolumeLeft( m_target.m_planetId, m_target.m_depositIndex, m_target.m_volume );
			}

			if ( m_target.m_volume <= 0 )
			{
				m_target.MineOut();

				Stop( null );

				return true;
			}
		}

		// the deposit shrinks as it is cut away
		m_target.transform.localScale = m_startScale * Mathf.Pow( (float) m_target.m_volume / m_startVolume, 1.0f / 3.0f );

		return true;
	}

	// the nearest deposit within reach that is still there
	static TerrainElement FindNearestDeposit( float range )
	{
		var terrainGrid = SpaceflightController.m_instance.m_disembarked.m_terrainGrid;
		var terrainVehicle = SpaceflightController.m_instance.m_terrainVehicle;

		if ( ( terrainGrid == null ) || ( terrainGrid.m_terrainElements == null ) || ( terrainVehicle == null ) )
		{
			return null;
		}

		TerrainElement nearest = null;

		var nearestDistance = range;
		var vehiclePosition = terrainVehicle.transform.position;

		foreach ( Transform child in terrainGrid.m_terrainElements.transform )
		{
			if ( !child.gameObject.activeInHierarchy || !child.TryGetComponent<TerrainElement>( out var deposit ) )
			{
				continue;
			}

			if ( deposit.HasBeenPickedUp() || ( deposit.m_volume <= 0 ) )
			{
				continue;
			}

			// along the ground (the vehicle's height is only drawn)
			var offset = deposit.transform.position - vehiclePosition;

			offset.y = 0.0f;

			var distance = offset.magnitude;

			if ( distance <= nearestDistance )
			{
				nearestDistance = distance;
				nearest = deposit;
			}
		}

		return nearest;
	}

	// a pulse of the beam from the vehicle to the deposit
	void FireBeam()
	{
		if ( s_beam == null )
		{
			var beamObject = new GameObject( "Mining Laser" );

			beamObject.transform.SetParent( SpaceflightController.m_instance.m_disembarked.transform, false );

			s_beam = beamObject.AddComponent<LaserBeam>();
			s_beam.SetScale( c_beamScale );
			s_beam.SetColor( c_beamColor );
		}

		var from = SpaceflightController.m_instance.m_terrainVehicle.transform.position + Vector3.up * c_vehicleBeamHeight;
		var to = m_target.transform.position + Vector3.up * c_depositBeamHeight;

		s_beam.Fire( from, to, c_pulseLifetime );
	}

	// the laser stops: how much came aboard, and why it stopped
	void Stop( string reason )
	{
		var controller = SpaceflightController.m_instance;

		SoundController.m_instance.StopSound( SoundController.Sound.Scanning );

		if ( m_target != null )
		{
			var text = "<color=green>Mined " + Tools.VolumeToText( m_mined ) + " cubic meters of " + m_target.GetElementName() + ".</color>";

			if ( m_target.m_volume > 0 )
			{
				text += "\n<color=yellow>" + Tools.VolumeToText( m_target.m_volume ) + " cubic meters are left in the deposit.</color>";
			}

			if ( reason != null )
			{
				text += "\n<color=white>" + reason + "</color>";
			}

			controller.m_messages.Clear();
			controller.m_messages.AddText( text );
		}

		m_target = null;

		controller.m_buttonController.DeactivateButton();
		controller.m_displayController.m_terrainVehicleDisplay.Show();
	}

	// the laser cannot start
	static bool Refuse( string reason )
	{
		SpaceflightController.m_instance.m_messages.AddText( "<color=red>" + reason + "</color>" );

		SoundController.m_instance.PlaySound( SoundController.Sound.Error );
		SpaceflightController.m_instance.m_buttonController.UpdateButtonSprites();

		return false;
	}
}
