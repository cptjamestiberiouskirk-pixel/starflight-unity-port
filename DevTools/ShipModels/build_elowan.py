"""Elowan vessels (ids 5, 6 and 7): procedural stand-ins shaped after the original's sensor pictures.

blender --background --factory-startup --python DevTools/ShipModels/build_elowan.py -- [--preview <png>] [--no-export]

The shapes are read from Research/Screenshots/Ships/Elowan * Analysis.png (seen from above, nose to the left).
The Elowan are a plant race and their ships are not symmetric: blades with striped light panels reach forward
and the two engine pods sit at different places on each side. Units are half pixels of the sensor picture.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ship_kit import Material, Ship, build_race  # noqa: E402

# a green, grown-looking hull, cyan light panels along the blades and red engine pods
HULL = Material("Elowan Hull", albedo=(0.17, 0.33, 0.14), specular=(0.25, 0.32, 0.22), smoothness=0.55)
TRIM = Material("Elowan Trim", albedo=(0.10, 0.17, 0.08), specular=(0.18, 0.22, 0.16), smoothness=0.45)
ENGINE = Material("Elowan Engine", albedo=(0.35, 0.06, 0.03), specular=(0.40, 0.20, 0.20), smoothness=0.60, emissive=(0.62, 0.14, 0.06))
LIGHT = Material("Elowan Light", albedo=(0.10, 0.30, 0.45), specular=(0.50, 0.50, 0.50), smoothness=0.80, emissive=(0.20, 0.65, 1.00))


def engine_pod(ship, x, front, back, radius):
    """An engine pod on one side only (no mirror), with a stalk to the hull."""
    split = front - (front - back) * 0.2
    ship.loft([(back, radius * 0.7, radius * 0.7, 0.0, x), (back + 4.0, radius, radius, 0.0, x), (split, radius, radius, 0.0, x)],
              ENGINE, segments=10, cap_end=False)
    ship.loft([(split, radius, radius, 0.0, x), (front, 0.0, 0.0, 0.0, x)], HULL, segments=10, cap_start=False)


def stalk(ship, x_from, x_to, y, width=8.0, thickness=3.0):
    low, high = min(x_from, x_to), max(x_from, x_to)
    ship.slab([(low, y - width), (high, y - width * 0.6), (high, y + width * 0.6), (low, y + width)], thickness, TRIM)


def blade(ship, x, front, back, half_width, light_from, light_to, z=0.0):
    """A leaf-like blade pointing forward with a striped light panel on top."""
    ship.loft([(back, half_width, half_width * 0.6, z, x), ((front + back) / 2.0, half_width * 0.9, half_width * 0.55, z, x), (front, 0.0, 0.0, z, x)],
              HULL, segments=8, power=1.7)

    # light stripes on the blade's back, with gaps between them
    y = light_to
    step = (light_to - light_from) / 5.0

    while y > light_from + 1e-3:
        ship.box((x, y - step * 0.35, z + half_width * 0.5), (half_width * 0.9, step * 0.6, 1.5), LIGHT)
        y -= step


def transport():
    ship = Ship("Elowan Transport")

    # a big seed pod at the back with three blades of different lengths reaching forward
    ship.ellipsoid((0.0, 0.0, 0.0), (47.0, 68.0, 30.0), HULL, segments=16, rings=10, power=2.2)
    ship.ellipsoid((0.0, -20.0, 22.0), (26.0, 36.0, 12.0), TRIM, segments=12, rings=6)
    blade(ship, 20.0, 166.0, 40.0, 6.0, 70.0, 150.0)
    blade(ship, -2.0, 118.0, 40.0, 5.5, 70.0, 110.0)
    blade(ship, -25.0, 91.0, 40.0, 5.0, 60.0, 85.0)
    stalk(ship, 40.0, 53.0, 30.0)
    engine_pod(ship, 53.0, 78.0, -22.0, 6.0)
    stalk(ship, -40.0, -59.0, -20.0)
    engine_pod(ship, -59.0, 31.0, -69.0, 6.0)

    return ship


def scout():
    ship = Ship("Elowan Scout")

    # a slender spindle, wide at the back, with a light panel along its front and two offset pods
    ship.loft([(-105.0, 0.0, 0.0, 0.0), (-75.0, 15.0, 8.0, 0.0), (-40.0, 27.0, 12.0, 0.0), (-5.0, 18.0, 9.0, 0.0), (15.0, 11.0, 7.0, 0.0), (100.0, 8.0, 5.0, 0.0), (122.0, 0.0, 0.0, 0.0)],
              HULL, segments=10, power=1.8)

    y = 100.0

    while y > 25.0:
        ship.box((0.0, y - 6.0, 6.0), (8.0, 9.0, 1.5), LIGHT)
        y -= 15.0

    stalk(ship, 10.0, 32.0, -20.0, 6.0)
    engine_pod(ship, 32.0, 27.0, -70.0, 5.0)
    stalk(ship, -12.0, -38.0, -55.0, 6.0)
    engine_pod(ship, -38.0, -12.0, -97.0, 5.0)

    return ship


def warship():
    ship = Ship("Elowan Warship")

    # two slender hulls joined at the back by a curved claw, each with a striped blade, and two offset pods
    ship.loft([(-92.0, 0.0, 0.0, 0.0, 25.0), (-80.0, 12.0, 9.0, 0.0, 25.0), (-20.0, 15.0, 10.0, 0.0, 25.0), (60.0, 9.0, 6.0, 0.0, 25.0), (130.0, 0.0, 0.0, 0.0, 25.0)],
              HULL, segments=10, power=1.8)
    ship.loft([(-80.0, 0.0, 0.0, 0.0, -37.0), (-70.0, 13.0, 9.0, 0.0, -37.0), (-20.0, 15.0, 10.0, 0.0, -37.0), (20.0, 10.0, 7.0, 0.0, -37.0), (55.0, 0.0, 0.0, 0.0, -37.0)],
              HULL, segments=10, power=1.8)

    # the claw: a half ring at the back from the upper hull round to the lower one
    ship.ring_sector((-6.0, -75.0), (42.0, 42.0), 0.7, 90.0, 270.0, 9.0, TRIM, segments=14)

    y = 120.0

    while y > 20.0:
        ship.box((25.0, y - 7.0, 9.0), (9.0, 10.0, 1.5), LIGHT)
        y -= 17.0

    y = 45.0

    while y > -20.0:
        ship.box((-37.0, y - 7.0, 9.0), (9.0, 10.0, 1.5), LIGHT)
        y -= 17.0

    stalk(ship, 35.0, 49.0, -10.0, 6.0)
    engine_pod(ship, 49.0, 42.0, -57.0, 6.0)
    stalk(ship, -47.0, -63.0, -55.0, 6.0)
    engine_pod(ship, -63.0, -5.0, -105.0, 6.0)

    return ship


# STRINFO 7.3: transport 0.7, warship 0.3, scout 0.1 times an Interstel ship (the scout is shown at the 0.3 floor)
VESSELS = [
    {"id": 5, "name": "Elowan Transport", "strinfo": 0.7, "screenshot": "Elowan Transport Analysis.png", "build": transport},
    {"id": 6, "name": "Elowan Scout", "strinfo": 0.1, "screenshot": "Elowan Scout Analysis.png", "build": scout},
    {"id": 7, "name": "Elowan Warship", "strinfo": 0.3, "screenshot": "Elowan Warship Analysis.png", "build": warship},
]

build_race("Elowan", VESSELS)
