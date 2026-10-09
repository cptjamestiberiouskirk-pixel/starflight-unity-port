"""Veloxi vessels (ids 11, 12, 13 and 18): procedural stand-ins shaped after the original's sensor pictures.

blender --background --factory-startup --python DevTools/ShipModels/build_veloxi.py -- [--preview <png>] [--no-export]

The shapes are read from Research/Screenshots/Ships/Velox * Analysis.png (the sensor silhouette, seen from
above with the nose to the left). Units here are pixels of that picture; ship_kit scales each ship to its size.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ship_kit import Material, Ship, build_race, rotate  # noqa: E402

# a dark bronze chitin hull, with the red engine pods and the blue lights of the sensor pictures
HULL = Material("Veloxi Hull", albedo=(0.24, 0.21, 0.13), specular=(0.32, 0.29, 0.20), smoothness=0.55)
TRIM = Material("Veloxi Trim", albedo=(0.08, 0.07, 0.06), specular=(0.20, 0.20, 0.20), smoothness=0.40)
ENGINE = Material("Veloxi Engine", albedo=(0.35, 0.05, 0.03), specular=(0.40, 0.20, 0.20), smoothness=0.60, emissive=(0.55, 0.10, 0.05))
LIGHT = Material("Veloxi Light", albedo=(0.10, 0.25, 0.50), specular=(0.50, 0.50, 0.50), smoothness=0.80, emissive=(0.25, 0.60, 1.00))
EYE = Material("Veloxi Drone Eye", albedo=(0.40, 0.03, 0.02), specular=(0.50, 0.30, 0.30), smoothness=0.80, emissive=(0.95, 0.12, 0.05))


def sensor_boom(ship, start, tip, radius, ball):
    """The thin boom with the round sensor at its tip that every Veloxi ship has."""
    ship.cylinder(start, tip - ball * 0.6, radius, TRIM, segments=8)
    ship.ellipsoid((0.0, tip, 0.0), (ball, ball, ball * 0.85), HULL, segments=12, rings=8)
    ship.ellipsoid((0.0, tip + ball * 0.75, 0.0), (ball * 0.45, ball * 0.40, ball * 0.45), LIGHT, segments=8, rings=6)


def engine_pod(ship, x, front, back, radius):
    """A wingtip engine pod: a dark nose and a glowing red body, open to the rear like the sensor picture."""
    split = front - (front - back) * 0.25
    ship.loft([(back, radius * 0.7, radius * 0.7, 0.0, x), (back + 3.0, radius, radius, 0.0, x), (split, radius, radius, 0.0, x)],
              ENGINE, segments=10, mirror=True, cap_end=False)
    ship.loft([(split, radius, radius, 0.0, x), (front - 4.0, radius * 0.75, radius * 0.75, 0.0, x), (front, 0.0, 0.0, 0.0, x)],
              HULL, segments=10, mirror=True, cap_start=False)


def dorsal_fin(ship, outline, thickness=2.0):
    """A fin standing on the top of the hull. outline: (height, y) points."""
    ship.slab(outline, thickness, TRIM, matrix=rotate(-90.0, "Y"))


def scout():
    ship = Ship("Veloxi Scout")
    sensor_boom(ship, 20.0, 91.0, 3.5, 11.0)
    ship.loft([(-55.0, 0.0, 0.0, 0.0), (-50.0, 7.0, 5.0, 0.0), (-30.0, 13.0, 9.0, 1.0), (0.0, 12.0, 9.0, 1.0), (15.0, 8.0, 6.0, 0.0), (28.0, 4.0, 3.5, 0.0)],
              HULL, segments=12, power=2.2)
    ship.ellipsoid((0.0, 7.0, 9.5), (3.5, 4.0, 2.5), LIGHT, segments=8, rings=6)

    # the crescent wing: a straight front edge out to the pods, a concave rear edge
    ship.slab([(4.0, 2.0), (18.0, 0.0), (32.0, -3.0), (46.0, -9.0), (52.0, -14.0), (54.0, -60.0), (46.0, -78.0), (32.0, -66.0), (18.0, -58.0), (4.0, -54.0)],
              5.0, HULL, bevel=0.12, mirror=True)
    engine_pod(ship, 58.0, -10.0, -95.0, 10.0)
    dorsal_fin(ship, [(8.0, -48.0), (8.0, -5.0), (13.0, -22.0), (14.0, -44.0)])

    return ship


def warship():
    ship = Ship("Veloxi Warship")
    sensor_boom(ship, 55.0, 113.0, 4.5, 13.0)
    ship.loft([(-95.0, 0.0, 0.0, 0.0), (-88.0, 9.0, 7.0, 0.0), (-60.0, 18.0, 13.0, 1.0), (-10.0, 18.0, 13.0, 1.0), (30.0, 12.0, 9.0, 0.0), (62.0, 5.0, 4.5, 0.0)],
              HULL, segments=12, power=2.2)
    ship.ellipsoid((0.0, 0.0, 14.0), (6.0, 8.0, 3.0), LIGHT, segments=8, rings=6)

    # the stepped front wing out to the inner pods, then the outer wing back to the outer pods
    ship.slab([(4.0, 60.0), (10.0, 55.0), (22.0, 40.0), (36.0, 22.0), (48.0, 8.0), (52.0, 4.0), (52.0, -60.0), (40.0, -64.0), (20.0, -62.0), (4.0, -60.0)],
              6.0, HULL, bevel=0.10, mirror=True)
    ship.slab([(40.0, -20.0), (66.0, -40.0), (82.0, -52.0), (88.0, -58.0), (88.0, -135.0), (80.0, -138.0), (70.0, -118.0), (58.0, -106.0), (40.0, -98.0), (20.0, -94.0), (4.0, -92.0), (4.0, -40.0)],
              5.0, HULL, bevel=0.10, mirror=True)
    engine_pod(ship, 60.0, 10.0, -62.0, 9.0)
    engine_pod(ship, 91.0, -55.0, -142.0, 10.0)

    # twin laser cannons over the inner pods, a missile rack under each outer wing
    ship.cylinder(0.0, 26.0, 1.6, TRIM, segments=6, x=60.0, z=9.5, mirror=True)
    ship.box((74.0, -95.0, -4.5), (8.0, 24.0, 4.0), TRIM, mirror=True)
    dorsal_fin(ship, [(12.0, -80.0), (12.0, -20.0), (19.0, -40.0), (21.0, -74.0)], 2.5)

    return ship


def transport():
    ship = Ship("Veloxi Transport")
    sensor_boom(ship, 15.0, 87.0, 4.0, 12.0)
    ship.loft([(-104.0, 0.0, 0.0, 0.0), (-100.0, 14.0, 12.0, 0.0), (-70.0, 28.0, 20.0, 0.0), (-20.0, 30.0, 22.0, 0.0), (5.0, 22.0, 16.0, 0.0), (20.0, 10.0, 8.0, 0.0), (25.0, 4.0, 4.0, 0.0)],
              HULL, segments=14, power=2.4)
    ship.ellipsoid((0.0, 0.0, 21.0), (8.0, 10.0, 3.0), LIGHT, segments=8, rings=6)

    # the two cargo lobes, banded, and the pylons out to the engine pods
    ship.ellipsoid((46.0, -58.0, 0.0), (28.0, 70.0, 20.0), HULL, segments=14, rings=10, power=2.6, mirror=True)
    ship.box((46.0, -30.0, 18.5), (34.0, 4.0, 3.0), TRIM, mirror=True)
    ship.box((46.0, -90.0, 17.5), (34.0, 4.0, 3.0), TRIM, mirror=True)
    ship.slab([(70.0, -80.0), (88.0, -84.0), (88.0, -140.0), (70.0, -125.0)], 4.0, TRIM, mirror=True)
    engine_pod(ship, 94.0, -70.0, -170.0, 11.0)

    return ship


def drone():
    ship = Ship("Veloxi Drone")

    # a needle at both ends and a diamond body, with two red cores in a row and a pair of stub fins
    ship.loft([(-137.0, 0.0, 0.0, 0.0), (-106.0, 2.2, 1.6, 0.0), (-60.0, 8.0, 5.0, 0.0), (-30.0, 20.0, 9.0, 0.0), (30.0, 20.0, 9.0, 0.0), (60.0, 8.0, 5.0, 0.0), (106.0, 2.2, 1.6, 0.0), (137.0, 0.0, 0.0, 0.0)],
              HULL, segments=12, power=1.6)
    ship.ellipsoid((0.0, 21.0, 0.0), (11.0, 11.0, 11.0), EYE, segments=10, rings=8)
    ship.ellipsoid((0.0, -19.0, 0.0), (11.0, 11.0, 11.0), EYE, segments=10, rings=8)
    ship.slab([(12.0, 26.0), (12.0, 6.0), (30.0, 16.0)], 2.0, TRIM, mirror=True)
    ship.slab([(12.0, -4.0), (12.0, -24.0), (30.0, -14.0)], 2.0, TRIM, mirror=True)

    return ship


# STRINFO 7.2: transport and warship 1 times an Interstel ship, scout 0.6, drone 0.3
VESSELS = [
    {"id": 11, "name": "Veloxi Transport", "strinfo": 1.0, "screenshot": "Velox Transport Analysis.png", "build": transport},
    {"id": 12, "name": "Veloxi Scout", "strinfo": 0.6, "screenshot": "Velox Scout Analysis.png", "build": scout},
    {"id": 13, "name": "Veloxi Warship", "strinfo": 1.0, "screenshot": "Velox Warship Analysis.png", "build": warship},
    {"id": 18, "name": "Veloxi Drone", "strinfo": 0.3, "screenshot": "Velox Probe Analysis.png", "build": drone},
]

build_race("Veloxi", VESSELS)
