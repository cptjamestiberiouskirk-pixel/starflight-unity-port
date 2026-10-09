"""Gazurtoid vessels (ids 14 and 15): procedural stand-ins shaped after the original's sensor pictures.

blender --background --factory-startup --python DevTools/ShipModels/build_gazurtoid.py -- [--preview <png>] [--no-export]

The shapes are read from Research/Screenshots/Ships/Gazurtoid * Analysis.png (seen from above, nose to the left):
a ragged mass with spines, most of them sideways, lights scattered over it and a red core in the middle. STRINFO
calls them huge (60 and 90 times an Interstel ship). The lumps and the spines come from a fixed random seed, so a
rebuild gives the same ships. Units are half pixels of the sensor picture.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ship_kit import Material, Ship, build_race  # noqa: E402

# a dark, wet-looking organic hull, cyan lights and a magenta-red core (the engine)
HULL = Material("Gazurtoid Hull", albedo=(0.20, 0.19, 0.17), specular=(0.35, 0.35, 0.33), smoothness=0.65)
TRIM = Material("Gazurtoid Trim", albedo=(0.11, 0.10, 0.09), specular=(0.22, 0.22, 0.20), smoothness=0.50)
ENGINE = Material("Gazurtoid Engine", albedo=(0.40, 0.05, 0.15), specular=(0.45, 0.25, 0.30), smoothness=0.70, emissive=(0.90, 0.15, 0.45))
LIGHT = Material("Gazurtoid Light", albedo=(0.10, 0.30, 0.40), specular=(0.50, 0.50, 0.50), smoothness=0.80, emissive=(0.20, 0.70, 0.90))


def spiny_mass(ship, seed, radii, spines, lights):
    generator = random.Random(seed)

    ship.blob((0.0, 0.0, 0.0), radii, HULL, seed, roughness=0.18, segments=20, rings=12)
    ship.blob((0.0, 0.0, radii[2] * 0.45), (radii[0] * 0.6, radii[1] * 0.6, radii[2] * 0.6), TRIM, seed + 1, roughness=0.12, segments=14, rings=8)

    # spines from the rim, mostly out to the sides and along the length, flat like the streaks of the sensor picture
    for _ in range(spines):
        angle = generator.uniform(0.0, 2.0 * math.pi)
        x = math.sin(angle)
        y = math.cos(angle)
        base = (x * radii[0] * 0.85, y * radii[1] * 0.85, generator.uniform(-0.3, 0.3) * radii[2])
        direction = (x, y * generator.uniform(0.6, 1.0), generator.uniform(-0.15, 0.15))
        length = generator.uniform(0.25, 0.6) * min(radii[0], radii[1])
        ship.spike(base, direction, length, generator.uniform(2.5, 5.0), TRIM)

    # lights scattered over the top, the core in the middle
    for _ in range(lights):
        angle = generator.uniform(0.0, 2.0 * math.pi)
        distance = generator.uniform(0.3, 0.7)
        position = (math.sin(angle) * radii[0] * distance, math.cos(angle) * radii[1] * distance, radii[2] * 0.75)
        size = generator.uniform(5.0, 8.0)
        ship.ellipsoid(position, (size, size, size * 0.6), LIGHT, segments=8, rings=6)

    ship.ellipsoid((0.0, -radii[1] * 0.12, radii[2] * 0.9), (18.0, 18.0, 10.0), ENGINE, segments=12, rings=8)


def scout():
    ship = Ship("Gazurtoid Scout")
    spiny_mass(ship, 1401, (70.0, 87.0, 38.0), 30, 6)

    return ship


def warship():
    ship = Ship("Gazurtoid Warship")
    spiny_mass(ship, 1501, (100.0, 125.0, 52.0), 44, 7)

    return ship


# STRINFO 7.5: scout 60 and warship 90 times an Interstel ship (shown at 7.0 and 7.5 by the size rule)
VESSELS = [
    {"id": 14, "name": "Gazurtoid Scout", "strinfo": 60.0, "screenshot": "Gazurtoid Scout Analysis.png", "build": scout},
    {"id": 15, "name": "Gazurtoid Warship", "strinfo": 90.0, "screenshot": "Gazurtoid Warship Analysis.png", "build": warship},
]

build_race("Gazurtoid", VESSELS)
