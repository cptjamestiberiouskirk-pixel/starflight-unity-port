
using UnityEngine;
using TMPro;

// the UI kit's frame on the viewport (its Display Frame in the Spaceflight mockups): corner marks, the name of where the ship is in the top left corner
// ("HYPERSPACE", "STAR SYSTEM", ...), and in the top right corner what matters there, as the mockups have it: the planet in orbit ("ORBIT III · ROCK"), the orbit
// the ship can go into ("APPROACHING ORBIT III"), the comm link or the armed weapons in an encounter; both are set again only when what they show changes
// (it lives in the viewport, made by Starflight Remake > UI Kit > Apply to the Spaceflight Console)
public class ViewportFrame : MonoBehaviour
{
	// the location's name, and the status beside it
	public TextMeshProUGUI m_label;
	public TextMeshProUGUI m_status;

	// the orbits' numerals
	static readonly string[] c_numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

	// what the label and the status show (none until the first update)
	bool m_shown;
	PD_General.Location m_location;
	int m_statusKey = int.MinValue;

	// unity late update
	void LateUpdate()
	{
		if ( ( m_label == null ) || ( DataController.m_instance == null ) )
		{
			return;
		}

		var playerData = DataController.m_instance.m_playerData;
		var location = playerData.m_general.m_location;

		if ( !m_shown || ( location != m_location ) )
		{
			m_shown = true;
			m_location = location;
			m_label.text = GetName( location );
		}

		if ( m_status == null )
		{
			return;
		}

		// what the status shows, as a number that changes when it should (so that the text is only made then)
		var statusKey = GetStatusKey( location, playerData, out var planetId );

		if ( statusKey == m_statusKey )
		{
			return;
		}

		m_statusKey = statusKey;
		m_status.text = GetStatus( location, statusKey, planetId );
	}

	// the status's key: the planet in orbit or the one to orbit (by its id), the comm link (-2) or the armed weapons (-3) in an encounter, or nothing (-1)
	static int GetStatusKey( PD_General.Location location, PlayerData playerData, out int planetId )
	{
		planetId = -1;

		var controller = SpaceflightController.m_instance;

		switch ( location )
		{
			case PD_General.Location.InOrbit:

				planetId = playerData.m_general.m_currentPlanetId;

				return ( planetId >= 0 ) ? planetId : -1;

			case PD_General.Location.StarSystem:

				planetId = ( ( controller != null ) && ( controller.m_starSystem != null ) ) ? controller.m_starSystem.GetPlanetToOrbit() : -1;

				return ( planetId >= 0 ) ? 100000 + planetId : -1;

			case PD_General.Location.Encounter:

				var encounter = ( controller != null ) ? controller.m_encounter : null;

				if ( ( encounter != null ) && ( encounter.m_pdEncounter != null ) && encounter.m_pdEncounter.m_connected )
				{
					return -2;
				}

				return playerData.m_playerShip.m_weaponsAreArmed ? -3 : -1;

			default:

				return -1;
		}
	}

	// the status's text for its key (the label's style puts it in capitals)
	static string GetStatus( PD_General.Location location, int statusKey, int planetId )
	{
		switch ( statusKey )
		{
			case -1: return "";
			case -2: return "Comm link open";
			case -3: return "Weapons armed";
		}

		var gameData = DataController.m_instance.m_gameData;

		if ( ( planetId < 0 ) || ( planetId >= gameData.m_planetList.Length ) )
		{
			return "";
		}

		var planet = gameData.m_planetList[ planetId ];
		var orbit = planet.m_orbitPosition - 1;
		var numeral = ( ( orbit >= 0 ) && ( orbit < c_numerals.Length ) ) ? c_numerals[ orbit ] : "?";

		return ( location == PD_General.Location.InOrbit ) ? "Orbit " + numeral + " · " + planet.GetSurfaceText() : "Approaching orbit " + numeral;
	}

	// the name of a location as the frame shows it (the label's style puts it in capitals)
	public static string GetName( PD_General.Location location )
	{
		switch ( location )
		{
			case PD_General.Location.DockingBay: return "Docking Bay";
			case PD_General.Location.JustLaunched: return "Star System";
			case PD_General.Location.StarSystem: return "Star System";
			case PD_General.Location.Hyperspace: return "Hyperspace";
			case PD_General.Location.InOrbit: return "In Orbit";
			case PD_General.Location.Planetside: return "Planetside";
			case PD_General.Location.Encounter: return "Encounter";
			case PD_General.Location.Disembarked: return "Terrain Vehicle";
			default: return "";
		}
	}
}
