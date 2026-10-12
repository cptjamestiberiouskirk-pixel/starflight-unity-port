
using UnityEngine;
using TMPro;

// the UI kit's frame on the viewport (its Display Frame in the Spaceflight mockups): corner marks, and the name of where the ship is in the top left corner
// ("HYPERSPACE", "STAR SYSTEM", ...), set again only when the location changes
// (it lives in the viewport, made by Starflight Remake > UI Kit > Apply to the Spaceflight Console)
public class ViewportFrame : MonoBehaviour
{
	// the location's name
	public TextMeshProUGUI m_label;

	// the location the label shows (none until the first update)
	bool m_shown;
	PD_General.Location m_location;

	// unity late update
	void LateUpdate()
	{
		if ( ( m_label == null ) || ( DataController.m_instance == null ) )
		{
			return;
		}

		var location = DataController.m_instance.m_playerData.m_general.m_location;

		if ( m_shown && ( location == m_location ) )
		{
			return;
		}

		m_shown = true;
		m_location = location;
		m_label.text = GetName( location );
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
