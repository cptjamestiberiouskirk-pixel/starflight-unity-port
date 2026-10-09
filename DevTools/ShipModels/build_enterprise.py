"""The Enterprise (id 21): a procedural stand-in shaped after the original's sensor picture.

blender --background --factory-startup --python DevTools/ShipModels/build_enterprise.py -- [--preview <png>] [--no-export]

The original's easter egg is the starship of the television series; the project owner chose a recognizable model
on 2026-10-09. The shape is read from Research/Screenshots/Ships/Enterprise Analysis.png (seen from above, nose to
the left): a saucer, a neck, a secondary hull and two nacelles with red caps. Units are half pixels of the picture.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ship_kit import Material, Ship, build_race, rotate  # noqa: E402

HULL = Material("Enterprise Hull", albedo=(0.72, 0.73, 0.75), specular=(0.40, 0.40, 0.42), smoothness=0.55)
TRIM = Material("Enterprise Trim", albedo=(0.45, 0.46, 0.48), specular=(0.30, 0.30, 0.32), smoothness=0.45)
BUSSARD = Material("Enterprise Bussard", albedo=(0.40, 0.05, 0.04), specular=(0.45, 0.25, 0.25), smoothness=0.70, emissive=(0.95, 0.15, 0.08))
LIGHT = Material("Enterprise Light", albedo=(0.12, 0.25, 0.45), specular=(0.50, 0.50, 0.50), smoothness=0.80, emissive=(0.30, 0.60, 1.00))
DEFLECTOR = Material("Enterprise Deflector", albedo=(0.45, 0.30, 0.10), specular=(0.50, 0.40, 0.30), smoothness=0.80, emissive=(1.00, 0.60, 0.20))


def enterprise():
    ship = Ship("The Enterprise")

    # the saucer: a lens with the bridge dome on top
    ship.lathe((0.0, 76.0, 18.0), [(0.0, -6.0), (30.0, -5.0), (50.0, -2.0), (54.0, 0.0), (50.0, 2.5), (30.0, 6.0), (0.0, 7.5)], HULL, segments=40)
    ship.lathe((0.0, 76.0, 25.0), [(16.0, 0.0), (12.0, 4.0), (6.0, 6.0), (0.0, 6.5)], TRIM, segments=20)
    ship.ellipsoid((0.0, 76.0, 31.5), (4.0, 4.0, 2.0), LIGHT, segments=8, rings=4)

    # the neck, sloping down and back from under the saucer to the secondary hull (outline: height, y)
    ship.slab([(6.0, -32.0), (14.0, -32.0), (20.0, 42.0), (11.0, 42.0)], 7.0, HULL, matrix=rotate(-90.0, "Y"))

    # the secondary hull with the deflector dish at its front
    ship.loft([(-97.0, 9.0, 9.0, 0.0), (-80.0, 14.0, 13.0, 0.0), (-20.0, 15.0, 15.0, 0.0), (8.0, 13.0, 13.0, 0.0), (15.0, 10.0, 10.0, 0.0)], HULL, segments=16)
    ship.ellipsoid((0.0, 16.0, -1.0), (7.0, 3.0, 7.0), DEFLECTOR, segments=12, rings=6)

    # pylons from the secondary hull up and out to the nacelles
    ship.beam((6.0, -58.0, 8.0), (41.0, -72.0, 24.0), 2.0, 7.0, TRIM, mirror=True)

    # the nacelles, red Bussard caps at the front and blue grilles along the inside
    ship.loft([(-132.0, 5.0, 5.0, 24.0, 41.0), (-128.0, 8.0, 8.0, 24.0, 41.0), (-15.0, 8.0, 8.0, 24.0, 41.0)], HULL, segments=12, mirror=True, cap_end=False)
    ship.loft([(-15.0, 8.0, 8.0, 24.0, 41.0), (-8.0, 7.5, 7.5, 24.0, 41.0), (-4.0, 0.0, 0.0, 24.0, 41.0)], BUSSARD, segments=12, mirror=True, cap_start=False)
    ship.box((34.0, -70.0, 24.0), (2.0, 80.0, 6.0), LIGHT, mirror=True)

    return ship


# STRINFO 7.13: 38 times the size of an Interstel ship (shown at 6.5 by the size rule)
VESSELS = [
    {"id": 21, "name": "The Enterprise", "strinfo": 38.0, "screenshot": "Enterprise Analysis.png", "build": enterprise},
]

build_race("Enterprise", VESSELS)
