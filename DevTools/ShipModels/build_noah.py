"""The Noah 9 derelict transport (id 23): a procedural stand-in shaped after the original's sensor picture.

blender --background --factory-startup --python DevTools/ShipModels/build_noah.py -- [--preview <png>] [--no-export]

The shape is read from Research/Screenshots/Ships/Noah Derelict Analysis.png (seen from above, nose to the left): a
small pointed nose, a neck and a big blocky body of cargo modules with a ragged outline. STRINFO 7.10: a dead ship
(energy 0), so nothing on it glows. Units are half pixels of the sensor picture.
"""

import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ship_kit import Material, Ship, build_race  # noqa: E402

# old, scorched plating; no lights and no engine glow on a derelict
HULL = Material("Noah 9 Hull", albedo=(0.27, 0.23, 0.19), specular=(0.18, 0.17, 0.15), smoothness=0.30)
TRIM = Material("Noah 9 Trim", albedo=(0.12, 0.10, 0.09), specular=(0.12, 0.12, 0.12), smoothness=0.25)
SCORCH = Material("Noah 9 Scorch", albedo=(0.05, 0.05, 0.05), specular=(0.05, 0.05, 0.05), smoothness=0.10)


def derelict():
    ship = Ship("Noah 9 Derelict Transport")
    generator = random.Random(2309)

    # nose and neck
    ship.loft([(70.0, 10.0, 8.0, 0.0), (100.0, 16.0, 12.0, 0.0), (118.0, 9.0, 7.0, 0.0), (130.0, 0.0, 0.0, 0.0)], TRIM, segments=8, power=1.6)
    ship.cylinder(55.0, 75.0, 7.0, HULL, segments=8)

    # three rows of cargo modules, each a little different, as the ragged outline of the picture
    for row, (front, back, half_width) in enumerate([(60.0, -5.0, 62.0), (-5.0, -70.0, 78.0), (-70.0, -125.0, 70.0)]):
        ship.box((0.0, (front + back) / 2.0, 0.0), (half_width * 1.4, front - back - 4.0, 36.0), HULL)

        for side in (-1.0, 1.0):
            width = generator.uniform(14.0, 22.0)
            x = side * (half_width * 0.7 + width / 2.0)
            length = generator.uniform(0.5, 0.9) * (front - back)
            y = generator.uniform(back + length / 2.0, front - length / 2.0)
            ship.box((x, y, generator.uniform(-6.0, 6.0)), (width, length, generator.uniform(18.0, 28.0)), HULL)

        # a rib across each row, and scorch marks where the hull was hit
        ship.box((0.0, front - 4.0, 0.0), (half_width * 1.5, 4.0, 40.0), TRIM)

        for _ in range(3):
            ship.box((generator.uniform(-0.6, 0.6) * half_width, generator.uniform(back + 8.0, front - 8.0), 18.2),
                     (generator.uniform(8.0, 18.0), generator.uniform(8.0, 16.0), 0.6), SCORCH)

    # a broken antenna mast and the stub of a torn-off module
    ship.spike((20.0, 30.0, 18.0), (0.2, 0.3, 1.0), 30.0, 2.0, TRIM)
    ship.box((-50.0, -128.0, 6.0), (24.0, 10.0, 14.0), TRIM)

    return ship


# STRINFO 7.10: 4 times the size of an Interstel ship
VESSELS = [
    {"id": 23, "name": "Noah 9 Derelict Transport", "strinfo": 4.0, "screenshot": "Noah Derelict Analysis.png", "build": derelict},
]

build_race("Noah 9", VESSELS)
