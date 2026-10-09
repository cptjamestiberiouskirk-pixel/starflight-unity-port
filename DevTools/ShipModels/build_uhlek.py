"""Uhlek vessels (ids 16 and 17): procedural stand-ins shaped after the original's sensor pictures.

blender --background --factory-startup --python DevTools/ShipModels/build_uhlek.py -- [--preview <png>] [--no-export]

The shapes are read from Research/Screenshots/Ships/Uhlek * Analysis.png (seen from above, nose to the left): a ring
open at the front around a core with blue lights and a red heart at the back; the warship has a second ring inside
the first. Units are half pixels of the sensor picture.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ship_kit import Material, Ship, build_race  # noqa: E402

# dark purple chitin, blue lights and a red glowing heart (the engine)
HULL = Material("Uhlek Hull", albedo=(0.17, 0.11, 0.19), specular=(0.30, 0.25, 0.32), smoothness=0.60)
TRIM = Material("Uhlek Trim", albedo=(0.08, 0.05, 0.09), specular=(0.20, 0.18, 0.22), smoothness=0.45)
ENGINE = Material("Uhlek Engine", albedo=(0.40, 0.05, 0.08), specular=(0.45, 0.25, 0.25), smoothness=0.70, emissive=(0.85, 0.10, 0.25))
LIGHT = Material("Uhlek Light", albedo=(0.12, 0.20, 0.50), specular=(0.50, 0.50, 0.50), smoothness=0.80, emissive=(0.30, 0.50, 1.00))


def scout():
    ship = Ship("Uhlek Scout")

    # the ring, open at the front between two arms, thick and rounded by a second, thinner layer on top
    ship.ring_sector((0.0, 0.0), (100.0, 112.0), 0.72, 14.0, 346.0, 26.0, HULL, segments=40)
    ship.ring_sector((0.0, 0.0), (94.0, 106.0), 0.78, 20.0, 340.0, 8.0, TRIM, segments=40, z=15.0)

    # the core, joined to the ring at the back, with two lights in front and the heart behind them
    ship.ellipsoid((0.0, -20.0, 0.0), (52.0, 75.0, 28.0), HULL, segments=16, rings=10)
    ship.ellipsoid((16.0, 8.0, 24.0), (9.0, 9.0, 6.0), LIGHT, segments=10, rings=6, mirror=True)
    ship.ellipsoid((0.0, -62.0, 22.0), (14.0, 14.0, 10.0), ENGINE, segments=10, rings=6)

    return ship


def warship():
    ship = Ship("Uhlek Warship")

    # two rings, one inside the other, both open at the front, and a core at the back
    ship.ring_sector((0.0, 0.0), (78.0, 136.0), 0.70, 16.0, 344.0, 22.0, HULL, segments=44)
    ship.ring_sector((0.0, -18.0), (50.0, 90.0), 0.62, 24.0, 336.0, 18.0, HULL, segments=40)
    ship.ring_sector((0.0, 0.0), (74.0, 132.0), 0.76, 22.0, 338.0, 6.0, TRIM, segments=44, z=12.0)
    ship.ellipsoid((0.0, -66.0, 0.0), (42.0, 58.0, 28.0), HULL, segments=16, rings=10)

    # three lights across the core and the heart behind them
    ship.ellipsoid((0.0, -45.0, 26.0), (9.0, 9.0, 6.0), LIGHT, segments=10, rings=6)
    ship.ellipsoid((34.0, -45.0, 14.0), (8.0, 8.0, 6.0), LIGHT, segments=10, rings=6, mirror=True)
    ship.ellipsoid((0.0, -92.0, 20.0), (14.0, 14.0, 10.0), ENGINE, segments=10, rings=6)

    return ship


# STRINFO 7.12: scout 2 and warship 10 times an Interstel ship (the warship is shown at 5.0 by the size rule)
VESSELS = [
    {"id": 16, "name": "Uhlek Scout", "strinfo": 2.0, "screenshot": "Uhlek Scout Analysis.png", "build": scout},
    {"id": 17, "name": "Uhlek Warship", "strinfo": 10.0, "screenshot": "Uhlek Warship Analysis.png", "build": warship},
]

build_race("Uhlek", VESSELS)
