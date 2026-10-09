"""Thrynn vessels (ids 8, 9 and 10): procedural stand-ins shaped after the original's sensor pictures.

blender --background --factory-startup --python DevTools/ShipModels/build_thrynn.py -- [--preview <png>] [--no-export]

The shapes are read from Research/Screenshots/Ships/Thrynn * Analysis.png (seen from above, nose to the left).
Units here are half pixels of that picture; ship_kit scales each ship to its size.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ship_kit import Material, Ship, build_race  # noqa: E402

# a grey-green reptile hull, a window band at the front, red engine pods as in the sensor pictures
HULL = Material("Thrynn Hull", albedo=(0.26, 0.31, 0.25), specular=(0.30, 0.32, 0.30), smoothness=0.50)
TRIM = Material("Thrynn Trim", albedo=(0.12, 0.13, 0.12), specular=(0.20, 0.20, 0.20), smoothness=0.40)
ENGINE = Material("Thrynn Engine", albedo=(0.35, 0.06, 0.03), specular=(0.40, 0.20, 0.20), smoothness=0.60, emissive=(0.60, 0.14, 0.05))
LIGHT = Material("Thrynn Light", albedo=(0.10, 0.30, 0.40), specular=(0.50, 0.50, 0.50), smoothness=0.80, emissive=(0.20, 0.75, 0.90))


def engine_pod(ship, x, front, back, radius, z=0.0):
    """An engine pod along Y with a dark nose cone and a glowing body."""
    split = front - (front - back) * 0.2
    ship.loft([(back, radius * 0.8, radius * 0.8, z, x), (back + 4.0, radius, radius, z, x), (split, radius, radius, z, x)],
              ENGINE, segments=10, mirror=True, cap_end=False)
    ship.loft([(split, radius, radius, z, x), (front, radius * 0.4, radius * 0.4, z, x)], TRIM, segments=10, mirror=True, cap_start=False)


def pylon(ship, inner_x, outer_x, front, back, thickness=3.0):
    ship.slab([(inner_x, back), (outer_x, back + 6.0), (outer_x, front), (inner_x, front - 6.0)], thickness, TRIM, mirror=True)


def transport():
    ship = Ship("Thrynn Transport")

    # an oval cargo hull with a flat back, a window band across the front and a pod on each side
    ship.loft([(-92.0, 30.0, 14.0, 0.0), (-86.0, 42.0, 19.0, 0.0), (-60.0, 50.0, 22.0, 0.0), (10.0, 50.0, 22.0, 0.0), (55.0, 42.0, 19.0, 0.0), (82.0, 24.0, 13.0, 0.0), (91.0, 0.0, 0.0, 0.0)],
              HULL, segments=16, power=2.3)
    ship.box((0.0, 57.0, 17.5), (40.0, 6.0, 4.0), LIGHT)
    ship.box((0.0, -40.0, 21.0), (60.0, 70.0, 4.0), TRIM)
    pylon(ship, 40.0, 42.0, -32.0, -80.0)
    engine_pod(ship, 45.0, -30.0, -137.0, 6.5)

    return ship


def scout():
    ship = Ship("Thrynn Scout")

    # a round hull with a sensor spike, a light at the front and two pods
    ship.loft([(-67.0, 25.0, 10.0, 0.0), (-60.0, 40.0, 16.0, 0.0), (-35.0, 47.0, 20.0, 0.0), (15.0, 46.0, 19.0, 0.0), (45.0, 34.0, 15.0, 0.0), (62.0, 16.0, 8.0, 0.0), (67.0, 0.0, 0.0, 0.0)],
              HULL, segments=16, power=2.1)
    ship.ellipsoid((0.0, 38.0, 16.0), (11.0, 11.0, 6.0), LIGHT, segments=10, rings=6)
    ship.spike((30.0, 50.0, 4.0), (0.25, 1.0, 0.0), 40.0, 2.0, TRIM)
    pylon(ship, 42.0, 56.0, 10.0, -60.0)
    engine_pod(ship, 59.0, 20.0, -100.0, 6.0)

    return ship


def warship():
    ship = Ship("Thrynn Warship")

    # a bigger round hull with a light cluster, two sensor spikes, two pods on each side and laser cannons
    ship.loft([(-90.0, 35.0, 14.0, 0.0), (-80.0, 50.0, 20.0, 0.0), (-50.0, 55.0, 24.0, 0.0), (20.0, 55.0, 24.0, 0.0), (60.0, 44.0, 19.0, 0.0), (82.0, 25.0, 11.0, 0.0), (90.0, 0.0, 0.0, 0.0)],
              HULL, segments=18, power=2.1)
    ship.ellipsoid((0.0, 26.0, 21.0), (18.0, 18.0, 7.0), LIGHT, segments=12, rings=6)
    ship.box((0.0, -45.0, 23.0), (50.0, 60.0, 4.0), TRIM)
    ship.spike((32.0, 62.0, 0.0), (0.15, 1.0, 0.0), 45.0, 2.5, TRIM, mirror=True)
    engine_pod(ship, 46.0, 42.0, -77.0, 6.0, z=8.0)
    pylon(ship, 50.0, 64.0, 5.0, -100.0)
    engine_pod(ship, 66.0, 10.0, -110.0, 6.5)
    ship.cylinder(42.0, 75.0, 1.8, TRIM, segments=6, x=46.0, z=8.0, mirror=True)

    return ship


# STRINFO 7.4: warship 0.8 and scout 0.4 times an Interstel ship. The transport is not in STRINFO; its 600 tons
# give 1.2 by the rule the other entries follow (the vessel's mass over 500 tons)
VESSELS = [
    {"id": 8, "name": "Thrynn Transport", "strinfo": 1.2, "screenshot": "Thrynn Transport Analysis.png", "build": transport},
    {"id": 9, "name": "Thrynn Scout", "strinfo": 0.4, "screenshot": "Thrynn Scout Analysis.png", "build": scout},
    {"id": 10, "name": "Thrynn Warship", "strinfo": 0.8, "screenshot": "Thrynn Warship Analysis.png", "build": warship},
]

build_race("Thrynn", VESSELS)
