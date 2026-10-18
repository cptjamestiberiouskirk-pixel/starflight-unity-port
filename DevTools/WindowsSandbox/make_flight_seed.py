"""Makes the save a flight check starts from (flight-check.ps1 runs it).

usage: python make_flight_seed.py <save with a crew and a ship> <engines class 1 to 5> <output file>

The save is any of the game's own (a playthrough stage save from the probe does: %TEMP%\\starflight-probe\\playthrough\\stage-2.json).
The copy is the active game, in a star system, 4000 units west of the sun and standing still, with engines of the class given and
the acceleration the Starport would work out for them (the engines' class x 500 over the ship's mass, whole G). It prints the mass
and the acceleration for flight-check.ps1.
"""
import json
import sys

source, engines_class, output = sys.argv[1], int(sys.argv[2]), sys.argv[3]

with open(source, encoding="utf-8-sig") as f:
    save = json.load(f)

general = save["m_general"]
ship = save["m_playerShip"]

start = {"x": -4000.0, "y": 0.0, "z": 0.0}

save["m_isCurrentGame"] = True
general["m_location"] = 3  # PD_General.Location.StarSystem
general["m_coordinates"] = dict(start)
general["m_lastStarSystemCoordinates"] = dict(start)
general["m_currentDirection"] = {"x": 1.0, "y": 0.0, "z": 0.0}
general["m_currentSpeed"] = 0.0

ship["m_enginesClass"] = engines_class
ship["m_acceleration"] = engines_class * 500 // ship["m_mass"] if ship["m_mass"] > 0 else 0

with open(output, "w", encoding="utf-8") as f:
    json.dump(save, f)

print(ship["m_mass"], ship["m_acceleration"], general.get("m_currentStarId"))
