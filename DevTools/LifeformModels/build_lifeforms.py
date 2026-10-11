"""The planet surface lifeforms: one model per body plan of the 1984 design notes (two per plant size).

blender --background --factory-startup --python DevTools/LifeformModels/build_lifeforms.py -- [--plan <names>] [--out <folder>] [--no-export]

--plan takes model names separated by commas (default: all). --out writes the models somewhere other than
Assets/Game Objects/Lifeforms (for trying changes out). Models are designed about one unit in size, head
along +Y, up +Z, standing on z = 0; lifeform_kit scales them so the largest side is 1.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np  # noqa: E402

from lifeform_kit import (  # noqa: E402
    DETAIL, LIFEFORMS_FOLDER, SKIN, Lifeform, build_one, fbm, frame_from_direction, log, normalize, rotation,
    script_arguments, smoothstep, sphere_mesh, srgb, vec, voronoi, write_manifest,
)

# ------------------------------------------------------------------------------------------ skin helpers


def to_linear(lum):
    """Paint values are given as they look (sRGB); Blender bakes linear values."""
    return np.power(lum, 2.2)


def mirrored(points, symmetric):
    q = np.array(points, dtype=np.float64)

    if symmetric:
        q[:, 0] = np.abs(q[:, 0])

    return q


def relief(seed, kind="scales", amplitude=0.004, scale=40.0, lumps=0.006, lump_scale=6.0, symmetric=True, tag_amplitude=None):
    """Skin detail as a displacement along the normal: scales, wrinkles, warts, pores or ridges, over large lumps."""
    def displace(points, normals, tags):
        q = mirrored(points, symmetric)
        d = lumps * (fbm(q, lump_scale, 3, seed) - 0.5) * 2.0

        if kind == "scales":
            f1, f2, cell = voronoi(q, scale, seed + 1)
            d += amplitude * (smoothstep(0.0, 0.22, f2 - f1) - 0.5 + 0.25 * (cell - 0.5))
        elif kind == "wrinkles":
            stretched = q * np.array([1.0, 3.5, 1.0])
            d += amplitude * (np.abs(fbm(stretched, scale * 0.3, 4, seed + 2) - 0.5) * -4.0 + 0.5)
        elif kind == "warts":
            f1, f2, cell = voronoi(q, scale, seed + 3)
            d += amplitude * (1.0 - smoothstep(0.0, 0.45, f1)) * (cell > 0.45) * 1.6
            d += amplitude * 0.25 * (fbm(q, scale * 2.0, 2, seed + 4) - 0.5)
        elif kind == "pores":
            f1, f2, cell = voronoi(q, scale, seed + 5)
            d -= amplitude * (1.0 - smoothstep(0.0, 0.25, f1))
            d += amplitude * 0.5 * (fbm(q, scale * 0.5, 3, seed + 6) - 0.5)
        elif kind == "plates":
            f1, f2, cell = voronoi(q, scale, seed + 7, jitter=0.6)
            d += amplitude * (smoothstep(0.0, 0.12, f2 - f1) - 0.5)
        elif kind == "bark":
            stretched = q * np.array([3.0, 3.0, 0.35])
            d += amplitude * (fbm(stretched, scale * 0.25, 4, seed + 8) - 0.5) * 2.0
            f1, f2, cell = voronoi(stretched, scale * 0.12, seed + 9)
            d -= amplitude * 0.8 * (1.0 - smoothstep(0.0, 0.12, f2 - f1))

        if tag_amplitude:
            for tag, factor in tag_amplitude.items():
                d = np.where(tags == tag, d * factor, d)

        return d

    return displace


def skin(seed, pattern="mottled", light=0.80, dark=0.40, belly=0.95, scale=7.0, smoothness=0.42, spec=0.24, cavity=0.35, symmetric=True, tones=None, glossy_tags=None, coverage=0.5, hue=(1.0, 0.985, 0.965)):
    """A paint for the tinted skin: a light ground, a darker pattern on the back, a pale belly, darker crevices.

    pattern: mottled, spots, stripes, bands (rings along the length), rosettes, none. tones: {tag: (lum factor, pattern factor)}.
    hue multiplies the grey: near white for skin that is tinted, a colour for bark and other untinted bodies.
    """
    def paint(points, normals, tags, displacement):
        q = mirrored(points, symmetric)
        n = len(points)

        if pattern == "mottled":
            m = smoothstep(coverage - 0.08, coverage + 0.12, fbm(q, scale, 4, seed))
        elif pattern == "spots":
            f1, f2, cell = voronoi(q, scale, seed + 1)
            m = smoothstep(0.42, 0.26, f1 + 0.15 * fbm(q, scale * 4.0, 2, seed)) * (cell < coverage + 0.2)
        elif pattern == "rosettes":
            f1, f2, cell = voronoi(q, scale, seed + 1)
            ring = smoothstep(0.42, 0.32, f1) - smoothstep(0.24, 0.12, f1)
            m = np.clip(ring, 0.0, 1.0)
        elif pattern == "stripes":
            warp = fbm(q, scale * 0.6, 3, seed) * 2.5
            s = np.sin(q[:, 1] * scale * 6.0 + warp * 3.0)
            m = smoothstep(0.2, 0.6, s) * smoothstep(-0.35, 0.1, normals[:, 2] + 0.1 * np.abs(normals[:, 0]))
        elif pattern == "bands":
            warp = fbm(q, scale * 0.4, 3, seed) * 0.8
            s = np.sin(q[:, 1] * scale * 6.0 + warp)
            m = smoothstep(0.3, 0.7, s)
        else:
            m = np.zeros(n)

        # the pattern is strongest on the back and fades onto the flanks
        back = smoothstep(-0.55, 0.35, normals[:, 2])
        m = m * back
        lum = light + (dark - light) * m

        if tones:
            for tag, (factor, pattern_factor) in tones.items():
                where = tags == tag
                lum = np.where(where, (light + (dark - light) * m * pattern_factor) * factor, lum)

        # a pale belly, then dirt in the crevices of the relief
        belly_mask = smoothstep(0.05, -0.65, normals[:, 2])
        lum = lum + (belly - lum) * belly_mask * 0.85
        amplitude = max(float(np.percentile(np.abs(displacement), 95)), 1e-6) if n else 1.0
        low = np.clip(-displacement / amplitude, 0.0, 1.0)
        lum = lum * (1.0 - cavity * low)
        fine = 0.94 + 0.06 * fbm(q, scale * 9.0, 2, seed + 3)
        lum = to_linear(np.clip(lum * fine, 0.0, 1.0))
        colour = lum[:, None] * vec(hue)[None, :]
        smooth = np.full(n, smoothness) * (0.85 + 0.3 * (1.0 - low))

        if glossy_tags:
            for tag, value in glossy_tags.items():
                smooth = np.where(tags == tag, value, smooth)

        return colour, np.clip(smooth, 0.0, 1.0), np.full(n, spec), np.zeros(n)

    return paint


def solid(colour, smoothness=0.4, spec=0.22, noise=0.08, seed=0, scale=30.0, fade_to=None, fade_axis=2, fade_range=(0.0, 1.0)):
    """A paint for an explicit part: one colour with a little noise, optionally fading to another along an axis."""
    c0 = vec(colour)

    def paint(params, verts):
        n = len(verts)
        variation = 1.0 - noise + 2.0 * noise * fbm(verts, scale, 3, seed)
        colour_values = np.tile(c0, (n, 1))

        if fade_to is not None:
            t = smoothstep(fade_range[0], fade_range[1], verts[:, fade_axis])
            colour_values = colour_values + (vec(fade_to) - c0)[None, :] * t[:, None]

        return colour_values * variation[:, None], np.full(n, smoothness), np.full(n, spec), np.zeros(n)

    return paint


def tube_paint(colour, tip_colour=None, smoothness=0.45, spec=0.24, rings=0.0, ring_count=12.0, seed=0, glow_tip=0.0):
    """A paint for a tube part: a colour fading to the tip colour, with optional dark rings."""
    c0 = vec(colour)
    c1 = vec(tip_colour) if tip_colour is not None else c0

    def paint(params, verts):
        t = params["t"]
        n = len(t)
        colour_values = c0[None, :] + (c1 - c0)[None, :] * smoothstep(0.3, 1.0, t)[:, None]
        variation = 0.92 + 0.16 * fbm(verts, 40.0, 3, seed)

        if rings > 0.0:
            band = 0.5 + 0.5 * np.cos(t * ring_count * 2.0 * math.pi)
            variation = variation * (1.0 - rings * smoothstep(0.75, 1.0, band))

        emit = smoothstep(0.85, 1.0, t) * glow_tip

        return colour_values * variation[:, None], np.full(n, smoothness), np.full(n, spec), emit

    return paint


def leaf_paint(seed, light=0.78, vein=0.95, edge=0.55, back=0.92, smoothness=0.5, spec=0.25, spots=0.0):
    """A paint for a leaf: pale veins, darker towards the edge, a paler underside."""
    def paint(params, verts):
        u = params["u"]
        v = params["v"]
        side = params["side"]
        n = len(u)
        midrib = 1.0 - smoothstep(0.0, 0.08, np.abs(v))
        lateral = np.abs(np.sin((u * 7.0 - np.abs(v) * 2.2) * math.pi))
        lateral = (1.0 - smoothstep(0.0, 0.18, lateral)) * smoothstep(0.98, 0.5, np.abs(v)) * 0.6
        vein_mask = np.clip(midrib + lateral, 0.0, 1.0)
        lum = light * (1.0 - (1.0 - edge / light) * smoothstep(0.4, 1.0, np.abs(v)) * 0.6)
        lum = lum * (0.9 + 0.2 * fbm(verts, 25.0, 3, seed))

        if spots > 0.0:
            f1, f2, cell = voronoi(verts, 60.0, seed + 4)
            lum = lum * (1.0 - spots * smoothstep(0.35, 0.2, f1) * (cell > 0.6))

        lum = lum + (vein - lum) * vein_mask
        lum = np.where(side < 0.0, np.minimum(lum * back + 0.08, 1.0), lum)
        lum = to_linear(np.clip(lum, 0.0, 1.0))
        colour_values = np.stack([lum, lum, lum * 0.97], axis=1)

        return colour_values, np.full(n, smoothness), np.full(n, spec), np.zeros(n)

    return paint


# colours of the parts that are never tinted
BONE = srgb(0.86, 0.80, 0.66)
HORN = srgb(0.20, 0.17, 0.14)
CLAW_TIP = srgb(0.72, 0.66, 0.55)
BARK = srgb(0.36, 0.30, 0.24)
BARK_DARK = srgb(0.20, 0.16, 0.13)
POD = srgb(0.85, 0.72, 0.30)
GLOW_BLUE = srgb(0.35, 0.80, 1.00)
BARK_HUE = (1.0, 0.80, 0.62)


# ------------------------------------------------------------------------------------------ the body plans


def bilateral_four_legs():
    """A heavy six-eyed grazer on four pillar legs, with a ridge of plates down its back and a pair of horns."""
    lf = Lifeform("BilateralFourLegs", "BilateralFourLegs")
    lf.body_triangles = 2600

    # torso, chest, haunch and a shoulder hump
    lf.ellipsoid((0.0, -0.02, 0.43), (0.16, 0.30, 0.16), k=0.05)
    lf.sphere((0.0, 0.17, 0.44), 0.165, k=0.08)
    lf.sphere((0.0, -0.22, 0.45), 0.15, k=0.08)
    lf.ellipsoid((0.0, 0.10, 0.56), (0.11, 0.17, 0.08), k=0.06)
    lf.ellipsoid((0.0, 0.0, 0.33), (0.13, 0.26, 0.1), k=0.06)

    # neck and a low, long head with a heavy jaw
    lf.cone((0.0, 0.26, 0.50), (0.0, 0.42, 0.43), 0.11, 0.075, k=0.05, tag="neck")
    lf.ellipsoid((0.0, 0.49, 0.40), (0.075, 0.10, 0.07), k=0.04, tag="head")
    lf.cone((0.0, 0.50, 0.39), (0.0, 0.62, 0.34), 0.06, 0.04, k=0.03, tag="head")
    lf.ellipsoid((0.0, 0.53, 0.33), (0.06, 0.08, 0.035), k=0.03, tag="head")
    lf.cut_ellipsoid((0.0, 0.62, 0.345), (0.05, 0.05, 0.006), k=0.006)
    lf.ellipsoid((0.045, 0.45, 0.44), (0.03, 0.05, 0.03), k=0.02, tag="head", mirror=True)

    # legs: shoulder, elbow, wrist, foot; hip, knee, hock, foot
    front = [(0.12, 0.19, 0.40), (0.15, 0.22, 0.22), (0.145, 0.21, 0.08), (0.145, 0.235, 0.03)]
    rear = [(0.12, -0.24, 0.42), (0.155, -0.15, 0.24), (0.15, -0.28, 0.10), (0.15, -0.255, 0.03)]
    lf.chain(front, [0.075, 0.048, 0.034, 0.04], k=0.015, join_k=0.06, tag="leg", mirror=True)
    lf.chain(rear, [0.085, 0.052, 0.034, 0.04], k=0.015, join_k=0.06, tag="leg", mirror=True)
    lf.ellipsoid((0.125, -0.20, 0.36), (0.07, 0.11, 0.12), frame=rotation(-25.0, 0.0, 0.0), k=0.04, tag="leg", mirror=True)
    lf.ellipsoid((0.125, 0.19, 0.33), (0.06, 0.08, 0.10), k=0.04, tag="leg", mirror=True)
    lf.ellipsoid((0.145, 0.245, 0.025), (0.05, 0.06, 0.03), k=0.02, tag="foot", mirror=True)
    lf.ellipsoid((0.15, -0.245, 0.025), (0.05, 0.06, 0.03), k=0.02, tag="foot", mirror=True)

    # a short thick tail
    lf.chain([(0.0, -0.36, 0.47), (0.0, -0.50, 0.42), (0.0, -0.58, 0.32)], [0.07, 0.045, 0.02], k=0.02, join_k=0.05, tag="tail")

    # the ridge of armour plates
    for i in range(8):
        y = 0.27 - i * 0.075
        z = 0.635 - 0.004 * (i - 2.5) ** 2
        lf.ellipsoid((0.0, y, z), (0.022, 0.05 - i * 0.002, 0.05 - i * 0.002), frame=rotation(-28.0, 0.0, 0.0), k=0.012, tag="plate")
        lf.ellipsoid((0.05, y - 0.01, z - 0.04), (0.018, 0.035, 0.03), frame=rotation(-28.0, 0.0, -20.0), k=0.012, tag="plate", mirror=True)

    lf.flat_base(0.0, 0.006)
    lf.displace = relief(41, "scales", amplitude=0.0028, scale=60.0, lumps=0.004, tag_amplitude={"plate": 0.6, "foot": 1.4, "head": 0.7})
    lf.paint = skin(41, "spots", light=0.82, dark=0.30, scale=11.0, smoothness=0.36, coverage=0.55, tones={"plate": (0.55, 0.2), "foot": (0.5, 0.0)})

    # three eyes a side, horns, and claws on every foot
    for i, (y, z, r) in enumerate([(0.565, 0.415, 0.017), (0.525, 0.44, 0.014), (0.49, 0.455, 0.011)]):
        lf.eye((0.058 - i * 0.004, y, z), r, (0.75, 0.55, 0.25), iris=srgb(0.95, 0.55, 0.08), pupil="bar", sclera=srgb(0.18, 0.10, 0.04), iris_size=0.8, mirror=True)

    lf.claw((0.05, 0.44, 0.47), (0.55, 0.45, 0.6), 0.16, 0.022, bend=0.6, colour_base=HORN, colour_tip=BONE, curl_axis=(0.0, 1.0, 0.2), mirror=True)

    for foot in ((0.145, 0.245), (0.15, -0.245)):
        for angle in (-28.0, 0.0, 28.0):
            a = math.radians(angle)
            base = (foot[0] + 0.03 * math.sin(a), foot[1] + 0.04 * math.cos(a), 0.022)
            lf.claw(base, (math.sin(a), math.cos(a), -0.35), 0.035, 0.011, bend=0.4, colour_base=HORN, colour_tip=CLAW_TIP, curl_axis=(0.0, 0.0, -1.0), mirror=True)

    return lf


def snakelike():
    """A segmented serpent reared up like a cobra, with a hood, four eyes, mandibles and a row of dorsal spines."""
    lf = Lifeform("Snakelike", "Snakelike", symmetric=False)
    lf.body_triangles = 3000
    count = 170
    points = []
    radii = []

    for i in range(count):
        t = i / (count - 1)
        rise = smoothstep(0.66, 1.0, t)
        x = 0.19 * math.sin(2.0 * math.pi * 1.45 * t + 0.4) * (1.0 - 0.85 * rise)
        y = -0.52 + 0.86 * t - 0.10 * rise * rise
        r = 0.037 * (0.10 + 0.90 * smoothstep(0.0, 0.45, t)) * (1.0 - 0.2 * smoothstep(0.8, 1.0, t))
        segment = 1.0 + 0.11 * math.cos(2.0 * math.pi * t * 40.0) ** 3
        z = r * 0.92 + 0.27 * rise ** 1.5
        points.append((x, y, z))
        radii.append(r * segment)

    lf.chain(points, radii, k=0.003, tag="body")
    head = vec(points[-1])
    forward = normalize(vec(points[-1]) - vec(points[-6]))
    forward = normalize(forward * vec((1.0, 1.0, 0.0)) + vec((0.0, 0.0, -0.15)))
    frame = frame_from_direction(forward)

    # the head, a wedge leaning forward, and the hood spread behind it
    side = normalize(np.cross(forward, vec((0.0, 0.0, 1.0))))
    lf.ellipsoid(head + forward * 0.045, (0.05, 0.03, 0.075), frame=frame, k=0.02, tag="head")
    lf.sphere(head + forward * 0.03 + side * 0.03 + vec((0.0, 0.0, -0.006)), 0.03, k=0.025, tag="head")
    lf.sphere(head + forward * 0.03 - side * 0.03 + vec((0.0, 0.0, -0.006)), 0.03, k=0.025, tag="head")
    lf.ellipsoid(head + forward * 0.10 + vec((0.0, 0.0, -0.008)), (0.03, 0.02, 0.045), frame=frame, k=0.02, tag="head")
    neck = vec(points[-16]) - forward * 0.01
    lf.ellipsoid(neck, (0.11, 0.095, 0.011), frame=frame_from_direction(forward + vec((0.0, 0.0, 0.3))), k=0.02, tag="hood")
    lf.cut_ellipsoid(head + forward * 0.13 + vec((0.0, 0.0, -0.028)), (0.03, 0.04, 0.004), frame=frame, k=0.004)
    lf.flat_base(0.0, 0.006)
    lf.displace = relief(7, "scales", amplitude=0.0022, scale=95.0, lumps=0.0015, symmetric=False, tag_amplitude={"head": 0.7})
    lf.paint = skin(7, "bands", light=0.86, dark=0.28, scale=9.0, smoothness=0.55, spec=0.26, symmetric=False, tones={"hood": (0.9, 1.6)})

    for sign in (1.0, -1.0):
        for along, up_offset, r in ((0.075, 0.022, 0.014), (0.045, 0.03, 0.010)):
            center = head + forward * along + side * sign * 0.036 + vec((0.0, 0.0, up_offset))
            lf.eye(center, r, forward * 0.6 + side * sign * 0.75 + vec((0.0, 0.0, 0.25)), iris=srgb(0.95, 0.85, 0.15), pupil="slit", sclera=srgb(0.25, 0.20, 0.02), iris_size=1.1)

        lf.claw(head + forward * 0.12 + side * sign * 0.022 + vec((0.0, 0.0, -0.025)), forward + side * sign * 0.25 + vec((0.0, 0.0, -0.3)), 0.06, 0.009, bend=0.9, colour_base=HORN, colour_tip=CLAW_TIP, curl_axis=-side * sign)

    for i in range(12, count - 30, 9):
        p = vec(points[i])
        lf.claw(p + vec((0.0, 0.0, radii[i] * 0.85)), (0.0, -0.4, 1.0), 0.03 + 0.012 * math.sin(i), radii[i] * 0.22, bend=0.3, colour_base=BONE * 0.6, colour_tip=BONE, curl_axis=(0.0, -1.0, 0.0))

    return lf


def bilateral_no_legs():
    """A great slug: a humped mantle on a broad rippled foot, eyes on tall stalks and feelers at the mouth."""
    lf = Lifeform("BilateralNoLegs", "BilateralNoLegs")
    lf.body_triangles = 2600
    lf.chain([(0.0, -0.50, 0.03), (0.0, -0.34, 0.07), (0.0, -0.12, 0.11), (0.0, 0.10, 0.11), (0.0, 0.26, 0.09), (0.0, 0.36, 0.08)],
        [0.025, 0.075, 0.115, 0.12, 0.09, 0.07], k=0.04, tag="body")
    lf.ellipsoid((0.0, -0.06, 0.03), (0.17, 0.45, 0.035), k=0.05, tag="foot")
    lf.ellipsoid((0.0, 0.08, 0.16), (0.125, 0.18, 0.10), k=0.05, tag="mantle")

    for i in range(6):
        y = -0.30 + i * 0.09
        lf.ellipsoid((0.0, y, 0.20 - 0.012 * abs(i - 3)), (0.025, 0.04, 0.03), k=0.03, tag="ridge")

    for i in range(5):
        y = -0.32 + i * 0.13
        lf.sphere((0.15 - 0.01 * abs(i - 2), y, 0.035), 0.03, k=0.035, tag="foot", mirror=True)

    lf.chain([(0.03, 0.36, 0.12), (0.055, 0.42, 0.22), (0.075, 0.46, 0.30)], [0.017, 0.012, 0.009], k=0.01, join_k=0.03, tag="stalk", mirror=True)
    lf.chain([(0.04, 0.40, 0.06), (0.075, 0.47, 0.04), (0.09, 0.51, 0.025)], [0.014, 0.009, 0.005], k=0.01, join_k=0.025, tag="stalk", mirror=True)
    lf.cut_ellipsoid((0.0, 0.43, 0.045), (0.03, 0.02, 0.008), k=0.006)
    lf.cut_ellipsoid((0.12, 0.10, 0.15), (0.012, 0.025, 0.018), k=0.008)
    lf.flat_base(0.0, 0.006)
    lf.displace = relief(13, "warts", amplitude=0.004, scale=38.0, lumps=0.005, tag_amplitude={"stalk": 0.3, "foot": 0.6})
    lf.paint = skin(13, "stripes", light=0.85, dark=0.38, scale=2.2, smoothness=0.72, spec=0.3, tones={"foot": (0.72, 0.0), "ridge": (0.7, 0.6)})
    lf.eye((0.075, 0.465, 0.315), 0.024, (0.3, 0.8, 0.5), iris=srgb(0.30, 0.95, 0.55), pupil="round", sclera=srgb(0.05, 0.12, 0.08), iris_size=0.9, mirror=True, glow=0.6)

    return lf


def bilateral_one_leg():
    """A hopper balanced on one powerful leg, with a long counterweight tail, a hooked beak and a crest of spines."""
    lf = Lifeform("BilateralOneLeg", "BilateralOneLeg")
    lf.body_triangles = 2500
    lf.ellipsoid((0.0, 0.0, 0.56), (0.12, 0.18, 0.14), frame=rotation(-22.0, 0.0, 0.0), k=0.04)
    lf.cone((0.0, 0.10, 0.62), (0.0, 0.19, 0.70), 0.10, 0.08, k=0.05, tag="neck")
    lf.sphere((0.0, 0.22, 0.72), 0.085, k=0.04, tag="head")
    lf.ellipsoid((0.0, 0.26, 0.69), (0.06, 0.06, 0.05), k=0.03, tag="head")
    lf.chain([(0.0, -0.12, 0.56), (0.0, -0.30, 0.52), (0.0, -0.46, 0.45), (0.0, -0.60, 0.40), (0.0, -0.68, 0.40)], [0.085, 0.055, 0.032, 0.014, 0.006], k=0.02, join_k=0.06, tag="tail")
    lf.ellipsoid((0.0, -0.66, 0.405), (0.008, 0.05, 0.035), k=0.015, tag="tail")

    # the leg: a heavy thigh, the knee forward, the shin back, and a three toed foot with a heel spur
    lf.ellipsoid((0.0, 0.02, 0.44), (0.10, 0.12, 0.13), frame=rotation(30.0, 0.0, 0.0), k=0.05, tag="leg")
    lf.chain([(0.0, 0.0, 0.45), (0.0, 0.11, 0.28), (0.0, -0.03, 0.11), (0.0, 0.0, 0.035)], [0.08, 0.05, 0.032, 0.03], k=0.015, join_k=0.04, tag="leg")

    for angle in (-35.0, 0.0, 35.0):
        a = math.radians(angle)
        lf.cone((0.0, 0.0, 0.03), (0.13 * math.sin(a), 0.01 + 0.13 * math.cos(a), 0.018), 0.028, 0.016, k=0.015, tag="foot")
        lf.claw((0.14 * math.sin(a), 0.01 + 0.14 * math.cos(a), 0.02), (math.sin(a), math.cos(a), -0.4), 0.04, 0.014, bend=0.5, colour_base=HORN, colour_tip=CLAW_TIP, curl_axis=(0.0, 0.0, -1.0))

    lf.cone((0.0, 0.0, 0.03), (0.0, -0.09, 0.02), 0.025, 0.014, k=0.015, tag="foot")
    lf.flat_base(0.0, 0.005)
    lf.displace = relief(19, "wrinkles", amplitude=0.003, scale=55.0, lumps=0.004, tag_amplitude={"foot": 1.5})
    lf.paint = skin(19, "stripes", light=0.86, dark=0.36, scale=3.0, smoothness=0.4, tones={"foot": (0.55, 0.0)})

    lf.claw((0.0, 0.285, 0.70), (0.0, 1.0, -0.25), 0.11, 0.04, bend=1.0, colour_base=srgb(0.30, 0.24, 0.16), colour_tip=srgb(0.80, 0.72, 0.50), curl_axis=(0.0, 0.0, -1.0))
    lf.claw((0.0, 0.28, 0.665), (0.0, 1.0, -0.05), 0.07, 0.03, bend=-0.3, colour_base=srgb(0.30, 0.24, 0.16), colour_tip=srgb(0.70, 0.62, 0.45), curl_axis=(0.0, 0.0, 1.0))
    lf.eye((0.062, 0.25, 0.75), 0.032, (0.75, 0.55, 0.25), iris=srgb(0.95, 0.30, 0.15), pupil="round", sclera=srgb(0.85, 0.80, 0.55), iris_size=0.7, mirror=True)

    for i, (y, z) in enumerate([(0.18, 0.80), (0.13, 0.79), (0.08, 0.76)]):
        lf.claw((0.0, y, z), (0.0, -0.6, 1.0), 0.12 - i * 0.025, 0.014, bend=-0.6, colour_base=srgb(0.55, 0.15, 0.10), colour_tip=srgb(0.95, 0.75, 0.30), curl_axis=(0.0, -1.0, 0.0))

    return lf


def bilateral_two_legs():
    """A striding biped, theropod-like: a long balancing tail, four eyes and a sail of a crest."""
    lf = Lifeform("BilateralTwoLegs", "BilateralTwoLegs")
    lf.body_triangles = 2700
    lf.ellipsoid((0.0, 0.0, 0.50), (0.10, 0.20, 0.115), frame=rotation(14.0, 0.0, 0.0), k=0.04)
    lf.chain([(0.0, 0.14, 0.56), (0.0, 0.24, 0.68), (0.0, 0.29, 0.77)], [0.085, 0.058, 0.045], k=0.02, join_k=0.05, tag="neck")
    lf.ellipsoid((0.0, 0.35, 0.79), (0.05, 0.085, 0.045), frame=rotation(-8.0, 0.0, 0.0), k=0.025, tag="head")
    lf.ellipsoid((0.0, 0.36, 0.755), (0.04, 0.075, 0.025), frame=rotation(-12.0, 0.0, 0.0), k=0.02, tag="head")
    lf.cut_ellipsoid((0.0, 0.43, 0.768), (0.045, 0.06, 0.004), frame=rotation(-10.0, 0.0, 0.0), k=0.003)
    for i in range(5):
        y = 0.355 - i * 0.035
        z = 0.825 - i * 0.022
        lf.cone((0.0, y, z), (0.0, y - 0.07, z + 0.075 - i * 0.01), 0.013, 0.003, k=0.012, tag="crest")
    lf.chain([(0.0, -0.15, 0.52), (0.0, -0.34, 0.50), (0.0, -0.52, 0.45), (0.0, -0.66, 0.40)], [0.085, 0.05, 0.024, 0.006], k=0.02, join_k=0.05, tag="tail")

    lf.ellipsoid((0.085, 0.0, 0.41), (0.06, 0.09, 0.12), frame=rotation(25.0, 0.0, 0.0), k=0.04, tag="leg", mirror=True)
    lf.chain([(0.08, -0.02, 0.47), (0.10, 0.08, 0.29), (0.10, -0.05, 0.12), (0.10, 0.0, 0.03)], [0.07, 0.045, 0.026, 0.025], k=0.012, join_k=0.04, tag="leg", mirror=True)

    for angle in (-25.0, 0.0, 25.0):
        a = math.radians(angle)
        tip = (0.10 + 0.09 * math.sin(a), 0.09 * math.cos(a), 0.015)
        lf.cone((0.10, 0.0, 0.025), tip, 0.022, 0.012, k=0.012, tag="foot", mirror=True)
        lf.claw(tip, (math.sin(a), math.cos(a), -0.3), 0.035, 0.012, bend=0.6, colour_base=HORN, colour_tip=CLAW_TIP, curl_axis=(0.0, 0.0, -1.0), mirror=True)

    lf.flat_base(0.0, 0.004)
    lf.displace = relief(23, "scales", amplitude=0.0024, scale=75.0, lumps=0.003, tag_amplitude={"crest": 0.4})
    lf.paint = skin(23, "stripes", light=0.84, dark=0.30, scale=3.4, smoothness=0.42, tones={"crest": (0.75, 1.5), "foot": (0.6, 0.0)})

    for along, up, r in ((0.375, 0.805, 0.016), (0.34, 0.82, 0.012)):
        lf.eye((0.042, along, up), r, (0.6, 0.7, 0.3), iris=srgb(0.95, 0.80, 0.10), pupil="slit", sclera=srgb(0.30, 0.22, 0.05), iris_size=0.95, mirror=True)

    for i in range(6):
        lf.claw((0.018, 0.40 + i * 0.012, 0.758), (0.0, 0.2, -1.0), 0.016, 0.004, bend=0.0, colour_base=BONE, colour_tip=BONE, mirror=True)

    return lf


def bilateral_many_legs():
    """An arthropod: a segmented carapace on eight jointed legs, with mandibles, antennae and clustered eyes."""
    lf = Lifeform("BilateralManyLegs", "BilateralManyLegs")
    lf.body_triangles = 2600
    segments = [((0.0, 0.30, 0.20), (0.065, 0.07, 0.052), "head"), ((0.0, 0.18, 0.215), (0.082, 0.085, 0.062), "body"),
        ((0.0, 0.03, 0.225), (0.10, 0.10, 0.072), "body"), ((0.0, -0.13, 0.225), (0.10, 0.095, 0.07), "body"),
        ((0.0, -0.28, 0.21), (0.08, 0.085, 0.06), "body"), ((0.0, -0.40, 0.19), (0.055, 0.065, 0.045), "body"),
        ((0.0, -0.49, 0.175), (0.03, 0.05, 0.03), "body")]

    for center, radii, tag in segments:
        lf.ellipsoid(center, radii, k=0.012, tag=tag)
        lf.ellipsoid((center[0], center[1] - radii[1] * 0.2, center[2] + radii[2] * 0.55), (radii[0] * 0.8, radii[1] * 0.75, radii[2] * 0.5), k=0.01, tag="plate")

    for i, (y, spread) in enumerate([(0.20, 0.55), (0.08, 0.18), (-0.04, -0.15), (-0.16, -0.5)]):
        knee = (0.20, y + spread * 0.12, 0.36)
        foot = (0.33, y + spread * 0.30, 0.006)
        lf.chain([(0.07, y, 0.20), (0.12, y + spread * 0.05, 0.27), knee, ((knee[0] + foot[0]) / 2.0 + 0.02, (knee[1] + foot[1]) / 2.0, 0.17), foot],
            [0.022, 0.018, 0.016, 0.011, 0.005], k=0.006, join_k=0.02, tag="leg", mirror=True)
        lf.sphere(knee, 0.02, k=0.008, tag="joint", mirror=True)

    lf.flat_base(0.0, 0.002)
    lf.displace = relief(29, "plates", amplitude=0.0015, scale=40.0, lumps=0.0015, tag_amplitude={"leg": 0.5, "joint": 0.5})
    lf.paint = skin(29, "mottled", light=0.82, dark=0.40, scale=6.0, smoothness=0.68, spec=0.32, tones={"leg": (0.85, 0.3), "joint": (0.5, 0.0), "plate": (0.92, 1.2)})

    lf.claw((0.03, 0.355, 0.18), (0.35, 1.0, -0.25), 0.08, 0.014, bend=0.9, colour_base=HORN, colour_tip=srgb(0.60, 0.20, 0.10), curl_axis=(-1.0, 0.0, 0.0), mirror=True)
    antenna = [(0.025 + 0.05 * t, 0.33 + 0.22 * t, 0.24 + 0.20 * math.sin(t * 2.2)) for t in np.linspace(0.0, 1.0, 12)]
    lf.tube("Antenna", antenna, [0.008 * (1.0 - 0.75 * t) for t in np.linspace(0.0, 1.0, 12)], tube_paint(srgb(0.25, 0.20, 0.16), srgb(0.70, 0.55, 0.30), rings=0.5, ring_count=9.0), material=DETAIL, mirror=True, segments_high=10, segments_low=5, low_stride=2)

    for (x, y, z, r) in ((0.04, 0.345, 0.225, 0.013), (0.052, 0.325, 0.235, 0.010), (0.028, 0.33, 0.245, 0.009)):
        lf.eye((x, y, z), r, (0.6, 0.8, 0.3), iris=srgb(0.85, 0.05, 0.05), pupil="none", sclera=srgb(0.25, 0.02, 0.02), mirror=True, glow=0.3)

    return lf


def radial_no_legs():
    """A sessile urchin: a ribbed dome bristling with spines, a ring of eyes and a feeding crown on top."""
    lf = Lifeform("RadialNoLegs", "RadialNoLegs", symmetric=False)
    lf.body_triangles = 1700
    lf.ellipsoid((0.0, 0.0, 0.16), (0.27, 0.27, 0.19), k=0.05)
    ribs = 9

    for i in range(ribs):
        a = 2.0 * math.pi * i / ribs
        c, s = math.cos(a), math.sin(a)
        lf.cone((0.04 * c, 0.04 * s, 0.33), (0.25 * c, 0.25 * s, 0.17), 0.035, 0.06, k=0.05, tag="rib")
        lf.cone((0.25 * c, 0.25 * s, 0.17), (0.27 * c, 0.27 * s, 0.03), 0.06, 0.05, k=0.05, tag="rib")

    for i in range(12):
        a = 2.0 * math.pi * (i + 0.5) / 12
        lf.sphere((0.07 * math.cos(a), 0.07 * math.sin(a), 0.345), 0.022, k=0.02, tag="crown")

    lf.cut_sphere((0.0, 0.0, 0.37), 0.055, k=0.02)
    lf.flat_base(0.0, 0.01)
    lf.displace = relief(31, "warts", amplitude=0.003, scale=45.0, lumps=0.004, symmetric=False)
    lf.paint = skin(31, "mottled", light=0.84, dark=0.45, scale=5.0, smoothness=0.45, symmetric=False, tones={"rib": (0.95, 0.4), "crown": (0.6, 0.0)})

    for i in range(ribs):
        a = 2.0 * math.pi * i / ribs
        c, s = math.cos(a), math.sin(a)
        for j, (rr, z, length) in enumerate(((0.12, 0.31, 0.11), (0.20, 0.25, 0.15), (0.26, 0.17, 0.17), (0.30, 0.08, 0.13))):
            wobble = 0.15 * math.sin(i * 3.1 + j * 1.7)
            base = vec((rr * c, rr * s, z))
            outward = normalize(vec((c, s, 0.9 - j * 0.35)))
            tangent = vec((-s, c, 0.0))
            lf.claw(base, outward + tangent * wobble, length, 0.011, bend=0.15, colour_base=srgb(0.30, 0.10, 0.20), colour_tip=srgb(0.95, 0.85, 0.75), curl_axis=tangent)

    for i in range(ribs):
        a = 2.0 * math.pi * (i + 0.5) / ribs
        c, s = math.cos(a), math.sin(a)
        lf.eye((0.255 * c, 0.255 * s, 0.13), 0.018, (c, s, 0.25), iris=srgb(0.20, 0.85, 1.0), pupil="round", sclera=srgb(0.02, 0.05, 0.08), iris_size=0.8, glow=0.8)

    for i in range(7):
        a = 2.0 * math.pi * i / 7
        c, s = math.cos(a), math.sin(a)
        points = [(0.03 * c, 0.03 * s, 0.33), (0.035 * c, 0.035 * s, 0.38), (0.05 * c, 0.05 * s, 0.42), (0.08 * c, 0.08 * s, 0.44)]
        lf.tube("Feeler", points, [0.009, 0.008, 0.006, 0.002], tube_paint(srgb(0.85, 0.45, 0.55), srgb(1.0, 0.85, 0.6), glow_tip=0.5), material=DETAIL, segments_high=10, segments_low=5, low_stride=2)

    return lf


def radial_few_legs():
    """A walking sphere: a ring of glowing eyes around a ridged ball, carried on two long jointed legs."""
    lf = Lifeform("RadialFewLegs", "RadialFewLegs")
    lf.body_triangles = 2200
    lf.sphere((0.0, 0.0, 0.56), 0.19, k=0.03)

    for i in range(18):
        a = 2.0 * math.pi * i / 18
        lf.sphere((0.19 * math.cos(a), 0.19 * math.sin(a), 0.54), 0.03, k=0.03, tag="ridge")

    lf.ellipsoid((0.0, 0.0, 0.725), (0.08, 0.08, 0.035), k=0.05, tag="cap")
    lf.cut_ellipsoid((0.0, 0.0, 0.37), (0.05, 0.05, 0.03), k=0.02)

    lf.ellipsoid((0.12, 0.0, 0.43), (0.07, 0.07, 0.06), k=0.04, tag="leg", mirror=True)
    lf.chain([(0.12, 0.0, 0.42), (0.27, 0.02, 0.55), (0.34, 0.0, 0.30), (0.33, -0.01, 0.04)], [0.05, 0.035, 0.028, 0.026], k=0.012, join_k=0.04, tag="leg", mirror=True)
    lf.sphere((0.27, 0.02, 0.55), 0.04, k=0.015, tag="joint", mirror=True)
    lf.ellipsoid((0.33, 0.0, 0.022), (0.06, 0.07, 0.022), k=0.02, tag="foot", mirror=True)
    lf.flat_base(0.0, 0.004)
    lf.displace = relief(37, "pores", amplitude=0.0025, scale=55.0, lumps=0.003)
    lf.paint = skin(37, "spots", light=0.86, dark=0.42, scale=14.0, smoothness=0.5, tones={"ridge": (0.75, 0.0), "joint": (0.6, 0.0), "foot": (0.55, 0.0)})

    for i in range(8):
        a = 2.0 * math.pi * (i + 0.5) / 8
        c, s = math.cos(a), math.sin(a)
        lf.eye((0.185 * c, 0.185 * s, 0.61), 0.026, (c, s, 0.35), iris=srgb(0.55, 1.0, 0.35), pupil="slit", sclera=srgb(0.04, 0.08, 0.02), iris_size=0.9, glow=0.7)

    for i in range(5):
        a = 2.0 * math.pi * i / 5
        c, s = math.cos(a), math.sin(a)
        points = [(0.03 * c, 0.03 * s, 0.77), (0.06 * c, 0.06 * s, 0.84), (0.11 * c, 0.11 * s, 0.88), (0.15 * c, 0.15 * s, 0.86)]
        lf.tube("Feeler", points, [0.014, 0.011, 0.008, 0.004], tube_paint(srgb(0.62, 0.55, 0.45), srgb(0.98, 0.85, 0.55), rings=0.3, ring_count=6.0), material=DETAIL, segments_high=12, segments_low=6, low_stride=1)

    for side in (1.0, -1.0):
        for angle in (-40.0, 40.0, 180.0):
            a = math.radians(angle)
            lf.claw((0.33 * side + 0.05 * math.sin(a), 0.055 * math.cos(a), 0.015), (math.sin(a), math.cos(a), -0.2), 0.03, 0.011, bend=0.5, colour_base=HORN, colour_tip=CLAW_TIP, curl_axis=(0.0, 0.0, -1.0))

    return lf


def radial_many_legs():
    """A seven legged walker: a domed mantle crowned with eyes, on arched tentacle legs that curl at the tips."""
    lf = Lifeform("RadialManyLegs", "RadialManyLegs", symmetric=False)
    lf.body_triangles = 2600
    lf.ellipsoid((0.0, 0.0, 0.42), (0.16, 0.16, 0.13), k=0.04)
    lf.ellipsoid((0.0, 0.0, 0.55), (0.11, 0.11, 0.10), k=0.06, tag="dome")
    legs = 7

    for i in range(legs):
        a = 2.0 * math.pi * (i + 0.5) / legs
        c, s = math.cos(a), math.sin(a)
        twist = 0.04 * math.sin(i * 2.3)
        path = [(0.10, 0.36), (0.25, 0.42), (0.38, 0.33), (0.46, 0.17), (0.50, 0.05), (0.55, 0.012), (0.60, 0.03), (0.615, 0.06)]
        points = [(r * c - twist * s * r, r * s + twist * c * r, z) for r, z in path]
        lf.chain(points, [0.065, 0.05, 0.038, 0.028, 0.02, 0.013, 0.008, 0.004], k=0.012, join_k=0.06, tag="leg")

    lf.cut_ellipsoid((0.0, 0.0, 0.29), (0.05, 0.05, 0.03), k=0.02)
    lf.flat_base(0.0, 0.002)
    lf.displace = relief(43, "warts", amplitude=0.0025, scale=60.0, lumps=0.003, symmetric=False)
    lf.paint = skin(43, "spots", light=0.86, dark=0.36, scale=12.0, smoothness=0.6, spec=0.28, symmetric=False, coverage=0.6, tones={"dome": (0.9, 1.3)})

    for i in range(legs):
        a = 2.0 * math.pi * i / legs
        c, s = math.cos(a), math.sin(a)
        lf.eye((0.13 * c, 0.13 * s, 0.53), 0.026, (c, s, 0.6), iris=srgb(1.0, 0.65, 0.10), pupil="bar", sclera=srgb(0.30, 0.18, 0.02), iris_size=0.85)

    lf.eye((0.0, 0.0, 0.645), 0.035, (0.0, 0.0, 1.0), iris=srgb(1.0, 0.25, 0.10), pupil="round", sclera=srgb(0.15, 0.03, 0.02), iris_size=0.8, glow=0.5)

    return lf


def amorphous_no_legs():
    """A heap of glistening protoplasm, lumpy and lopsided, with eyes of all sizes sunk in it."""
    lf = Lifeform("AmorphousNoLegs", "AmorphousNoLegs", symmetric=False)
    lf.body_triangles = 2400
    rng = np.random.RandomState(53)
    blobs = [((0.0, 0.0, 0.14), (0.28, 0.24, 0.16)), ((0.12, 0.08, 0.26), (0.13, 0.12, 0.12)), ((-0.10, -0.06, 0.24), (0.15, 0.14, 0.13)),
        ((0.20, -0.12, 0.10), (0.12, 0.10, 0.09)), ((-0.22, 0.12, 0.08), (0.10, 0.12, 0.08)), ((0.02, -0.02, 0.38), (0.08, 0.09, 0.08)),
        ((0.27, 0.10, 0.06), (0.07, 0.08, 0.06)), ((-0.05, 0.22, 0.07), (0.10, 0.07, 0.07)), ((-0.28, -0.12, 0.05), (0.07, 0.07, 0.05))]

    for center, radii in blobs:
        lf.ellipsoid(center, radii, frame=rotation(rng.uniform(-20, 20), rng.uniform(-20, 20), rng.uniform(0, 180)), k=0.08)

    for i in range(14):
        a = rng.uniform(0.0, 2.0 * math.pi)
        r = rng.uniform(0.05, 0.3)
        lf.sphere((r * math.cos(a), r * math.sin(a), rng.uniform(0.1, 0.32)), rng.uniform(0.03, 0.06), k=0.05, tag="lump")

    lf.cut_ellipsoid((0.10, 0.25, 0.12), (0.06, 0.03, 0.025), frame=rotation(0.0, 0.0, 20.0), k=0.02)
    lf.flat_base(0.0, 0.02)
    lf.displace = relief(53, "pores", amplitude=0.003, scale=40.0, lumps=0.008, lump_scale=8.0, symmetric=False)
    lf.paint = skin(53, "mottled", light=0.86, dark=0.50, scale=6.0, smoothness=0.72, spec=0.34, symmetric=False, belly=0.86, cavity=0.5)

    for center, direction, r in (((0.06, 0.20, 0.26), (0.2, 1.0, 0.4), 0.04), ((0.18, 0.13, 0.30), (0.6, 0.6, 0.6), 0.024), ((-0.08, 0.18, 0.31), (-0.3, 0.8, 0.7), 0.018),
            ((0.0, 0.04, 0.45), (0.1, 0.4, 1.0), 0.028), ((0.27, 0.0, 0.17), (1.0, 0.3, 0.3), 0.02), ((-0.2, 0.12, 0.2), (-0.7, 0.6, 0.4), 0.015)):
        lf.eye(center, r * 1.35, direction, iris=srgb(0.85, 0.25, 0.05), pupil="round", sclera=srgb(0.92, 0.88, 0.70), iris_size=0.6)

    return lf


def amorphous_legs():
    """A lopsided blob hauling itself on uneven pseudopods, one of them raised, with eyes on soft stalks."""
    lf = Lifeform("AmorphousLegs", "AmorphousLegs", symmetric=False)
    lf.body_triangles = 2800
    rng = np.random.RandomState(61)
    lf.ellipsoid((0.0, 0.0, 0.30), (0.20, 0.18, 0.15), frame=rotation(10.0, -8.0, 30.0), k=0.08)
    lf.ellipsoid((0.08, -0.06, 0.40), (0.12, 0.11, 0.10), k=0.08)
    lf.ellipsoid((-0.10, 0.06, 0.36), (0.10, 0.12, 0.09), k=0.08)

    for i in range(8):
        a = rng.uniform(0.0, 2.0 * math.pi)
        lf.sphere((0.15 * math.cos(a), 0.15 * math.sin(a), rng.uniform(0.25, 0.42)), rng.uniform(0.03, 0.05), k=0.05, tag="lump")

    legs = [(20.0, 0.42, 1.0), (85.0, 0.36, 0.8), (150.0, 0.44, 1.1), (215.0, 0.33, 0.7), (290.0, 0.40, 0.9)]

    for angle, reach, thickness in legs:
        a = math.radians(angle)
        c, s = math.cos(a), math.sin(a)
        bend = rng.uniform(-0.25, 0.25)
        points = []

        for t in np.linspace(0.0, 1.0, 7):
            r = 0.08 + reach * t
            side = bend * math.sin(t * math.pi) * 0.4
            z = 0.27 * (1.0 - t) ** 1.6 + 0.02 + 0.06 * math.sin(t * math.pi) * (1.0 - t)
            points.append((r * c - side * s, r * s + side * c, z))

        radii = [thickness * v for v in (0.08, 0.06, 0.047, 0.036, 0.027, 0.019, 0.01)]
        lf.chain(points, radii, k=0.02, join_k=0.07, tag="pseudopod")

    raised = [(-0.06, 0.12, 0.38), (-0.10, 0.24, 0.48), (-0.10, 0.34, 0.58), (-0.06, 0.40, 0.66), (0.0, 0.42, 0.70)]
    lf.chain(raised, [0.06, 0.045, 0.032, 0.02, 0.01], k=0.02, join_k=0.07, tag="pseudopod")

    for base, tip in (((0.05, 0.05, 0.46), (0.09, 0.13, 0.58)), ((0.12, -0.02, 0.44), (0.20, 0.0, 0.53)), ((-0.04, -0.02, 0.47), (-0.06, 0.02, 0.60))):
        lf.cone(base, tip, 0.025, 0.012, k=0.03, tag="stalk")

    lf.flat_base(0.0, 0.006)
    lf.displace = relief(61, "warts", amplitude=0.003, scale=42.0, lumps=0.007, lump_scale=7.0, symmetric=False)
    lf.paint = skin(61, "spots", light=0.84, dark=0.38, scale=10.0, smoothness=0.7, spec=0.3, symmetric=False, coverage=0.55, tones={"pseudopod": (0.95, 0.7)})

    for center, direction, r in (((0.09, 0.135, 0.595), (0.3, 0.9, 0.5), 0.028), ((0.205, 0.0, 0.545), (0.9, 0.2, 0.5), 0.022), ((-0.06, 0.02, 0.62), (-0.2, 0.5, 1.0), 0.024)):
        lf.eye(center, r, direction, iris=srgb(0.95, 0.35, 0.90), pupil="slit", sclera=srgb(0.90, 0.85, 0.75), iris_size=0.7)

    return lf


# ------------------------------------------------------------------------------------------ plants


def lance(u):
    return math.sin(math.pi * min(u, 1.0)) ** 0.75 * (1.0 - 0.35 * u) + 0.02


def ovate(u):
    return math.sin(math.pi * min(u, 1.0) ** 0.8) ** 0.9 + 0.02


def strap(u):
    return (1.0 - u ** 3) * min(1.0, u * 8.0) * 0.9 + 0.05


def pod(lf, center, radii, direction, colour, glow, seed, pattern="stripes"):
    """A glowing seed pod or fruit (never tinted)."""
    frame = frame_from_direction(direction)
    colour = vec(colour)

    def build(detail):
        segments, rings = (32, 16) if detail == "high" else (10, 7)

        return sphere_mesh(center, radii, frame, segments, rings)

    def paint(params, verts):
        n = len(verts)

        if pattern == "stripes":
            mark = 0.5 + 0.5 * np.cos(params["phi"] * 6.0)
        else:
            mark = smoothstep(0.25, 0.45, voronoi(params["unit"], 4.0, seed)[0])

        values = np.tile(colour, (n, 1)) * (0.55 + 0.45 * mark)[:, None]

        return values, np.full(n, 0.72), np.full(n, 0.32), glow * (0.5 + 0.5 * mark)

    lf.add_part("Pod", build, paint, DETAIL)


def leaved_small_a():
    """A ground rosette of broad spotted leaves around a glowing seed bulb on a short spike."""
    lf = Lifeform("LeavedSmallA", "LeavedSmall", kind="plant", symmetric=False)
    lf.body_triangles = 500
    lf.ellipsoid((0.0, 0.0, 0.03), (0.06, 0.06, 0.045), k=0.02, tag="core")
    lf.flat_base(0.0, 0.005)
    lf.displace = relief(71, "wrinkles", amplitude=0.002, scale=40.0, lumps=0.002, symmetric=False)
    lf.paint = skin(71, "none", light=0.7, belly=0.7, smoothness=0.4, symmetric=False)
    paint = leaf_paint(71, light=0.80, vein=0.96, edge=0.52, spots=0.35)

    for i in range(8):
        a = 2.0 * math.pi * i / 8 + 0.2
        direction = (math.cos(a) * math.cos(0.45), math.sin(a) * math.cos(0.45), math.sin(0.45))
        lf.leaf("Leaf", paint, base=vec((0.03 * math.cos(a), 0.03 * math.sin(a), 0.04)), direction=direction, length=0.46, width=0.13, outline=lance, fold=0.35, droop=0.9, twist=10.0 * math.sin(i * 2.0), low=(6, 2))

    for i in range(6):
        a = 2.0 * math.pi * (i + 0.5) / 6
        direction = (math.cos(a) * math.cos(1.0), math.sin(a) * math.cos(1.0), math.sin(1.0))
        lf.leaf("Leaf", paint, base=vec((0.015 * math.cos(a), 0.015 * math.sin(a), 0.05)), direction=direction, length=0.30, width=0.09, outline=lance, fold=0.45, droop=0.7, low=(5, 2))

    stem = [(0.0, 0.0, 0.04), (0.005, 0.01, 0.15), (0.0, 0.02, 0.27), (-0.01, 0.02, 0.36)]
    lf.tube("Stem", stem, [0.012, 0.01, 0.009, 0.008], tube_paint(srgb(0.75, 0.75, 0.70)), material=SKIN, segments_high=12, segments_low=6, low_stride=1, tip=False)
    pod(lf, (-0.01, 0.022, 0.40), (0.035, 0.035, 0.05), (0.0, 0.1, 1.0), srgb(0.95, 0.85, 0.45), 1.0, 72)

    return lf


def leaved_small_b():
    """A clump of parasol sprouts: round cupped leaves on slender stems from a knobbly root bulb."""
    lf = Lifeform("LeavedSmallB", "LeavedSmall", kind="plant", symmetric=False)
    lf.body_triangles = 700
    lf.ellipsoid((0.0, 0.0, 0.03), (0.09, 0.08, 0.05), k=0.03, tag="bulb")

    for i in range(6):
        a = 2.0 * math.pi * i / 6
        lf.sphere((0.07 * math.cos(a), 0.07 * math.sin(a), 0.025), 0.03, k=0.03, tag="bulb")

    lf.flat_base(0.0, 0.006)
    lf.body_material = DETAIL
    lf.displace = relief(73, "warts", amplitude=0.003, scale=50.0, lumps=0.004, symmetric=False)
    lf.paint = skin(73, "mottled", light=0.50, dark=0.32, belly=0.45, hue=BARK_HUE, scale=8.0, smoothness=0.3, symmetric=False)
    stems = [(0.0, 0.0, 0.55, 0.13), (50.0, 0.10, 0.38, 0.10), (130.0, 0.12, 0.30, 0.085), (200.0, 0.09, 0.45, 0.11), (270.0, 0.13, 0.24, 0.075), (330.0, 0.08, 0.34, 0.09)]
    paint = leaf_paint(73, light=0.82, vein=0.97, edge=0.6)

    for angle, lean, height, radius in stems:
        a = math.radians(angle)
        c, s = math.cos(a), math.sin(a)
        points = [(0.02 * c, 0.02 * s, 0.04)]

        for t in np.linspace(0.15, 1.0, 6):
            r = lean * t ** 1.3 + 0.02
            points.append((r * c, r * s, 0.04 + height * t))

        lf.tube("Stem", points, [0.011 - 0.004 * t for t in np.linspace(0.0, 1.0, 7)], tube_paint(srgb(0.80, 0.80, 0.75), srgb(0.95, 0.95, 0.9)), material=SKIN, segments_high=10, segments_low=5, low_stride=2, tip=False)
        top = vec(points[-1])
        normal = normalize(vec((c * lean * 1.5, s * lean * 1.5, 1.0)))
        lf.disc("Parasol", paint, center=top - normal * 0.004, normal=normal, radius=radius, cup=-0.28, wave=0.6, lobes=7, low=(3, 14))

    return lf


def leaved_medium_a():
    """A woody shrub: a gnarled stem forking into six branches, each ending in a whorl of glossy leaves."""
    lf = Lifeform("LeavedMediumA", "LeavedMedium", kind="plant", symmetric=False)
    lf.body_triangles = 1000
    lf.body_material = DETAIL
    rng = np.random.RandomState(79)
    lf.chain([(0.0, 0.0, 0.0), (0.01, 0.0, 0.10), (0.0, 0.01, 0.17)], [0.045, 0.035, 0.03], k=0.02, tag="trunk")

    for i in range(4):
        a = 2.0 * math.pi * i / 4 + 0.4
        lf.cone((0.0, 0.0, 0.03), (0.08 * math.cos(a), 0.08 * math.sin(a), 0.0), 0.03, 0.012, k=0.03, tag="trunk")

    branches = [(0.0, 0.30, 0.62), (40.0, 0.26, 0.48), (115.0, 0.30, 0.52), (190.0, 0.27, 0.44), (265.0, 0.29, 0.55), (320.0, 0.16, 0.66)]
    tips = []

    for angle, reach, height in branches:
        a = math.radians(angle)
        c, s = math.cos(a), math.sin(a)
        mid = (reach * 0.45 * c, reach * 0.45 * s, 0.17 + (height - 0.17) * 0.55)
        tip = (reach * c, reach * s, height)
        lf.chain([(0.0, 0.01, 0.15), mid, tip], [0.025, 0.017, 0.009], k=0.01, join_k=0.03, tag="branch")
        tips.append(vec(tip))

    lf.flat_base(0.0, 0.004)
    lf.displace = relief(79, "bark", amplitude=0.003, scale=60.0, lumps=0.002, symmetric=False)
    lf.paint = skin(79, "mottled", light=0.46, dark=0.34, belly=0.46, scale=14.0, hue=BARK_HUE, smoothness=0.25, symmetric=False, cavity=0.6)
    paint = leaf_paint(79, light=0.80, vein=0.92, edge=0.55, smoothness=0.65)

    for t, tip in enumerate(tips):
        outward = normalize(tip * vec((1.0, 1.0, 0.0)) + vec((0.0, 0.0, 0.6)))
        for j in range(8):
            a = 2.0 * math.pi * j / 8 + t
            around = normalize(vec((math.cos(a), math.sin(a), 0.0)))
            direction = normalize(around + outward * 0.9 + vec((0.0, 0.0, 0.3)))
            lf.leaf("Leaf", paint, base=tip - direction * 0.01, direction=direction, length=0.15 + 0.03 * rng.rand(), width=0.075, outline=ovate, fold=0.25, droop=0.5, twist=rng.uniform(-25, 25), low=(4, 2))

    return lf


def leaved_medium_b():
    """A bottle plant: a ribbed succulent trunk with a fountain of drooping strap leaves and a glowing seed pod."""
    lf = Lifeform("LeavedMediumB", "LeavedMedium", kind="plant", symmetric=False)
    lf.body_triangles = 1400
    profile = [(0.0, 0.10), (0.06, 0.14), (0.16, 0.135), (0.28, 0.09), (0.40, 0.05), (0.50, 0.04), (0.56, 0.045)]
    lf.chain([(0.0, 0.0, z) for z, r in profile], [r for z, r in profile], k=0.03, tag="trunk")

    for i in range(10):
        a = 2.0 * math.pi * i / 10
        c, s = math.cos(a), math.sin(a)
        lf.chain([(r * 0.92 * c, r * 0.92 * s, z) for z, r in profile[1:6]], [0.03, 0.03, 0.022, 0.014, 0.01], k=0.02, join_k=0.02, tag="rib")

    lf.flat_base(0.0, 0.005)
    lf.displace = relief(83, "wrinkles", amplitude=0.0025, scale=50.0, lumps=0.002, symmetric=False)
    lf.paint = skin(83, "bands", light=0.86, dark=0.55, scale=2.4, belly=0.86, smoothness=0.5, symmetric=False, tones={"rib": (0.92, 0.6)})
    paint = leaf_paint(83, light=0.84, vein=0.95, edge=0.62)

    for i in range(11):
        a = 2.0 * math.pi * i / 11 + 0.3
        elevation = 0.42 + 0.2 * math.sin(i * 1.9)
        direction = (math.cos(a) * math.cos(elevation), math.sin(a) * math.cos(elevation), math.sin(elevation))
        lf.leaf("Leaf", paint, base=vec((0.02 * math.cos(a), 0.02 * math.sin(a), 0.56)), direction=direction, length=0.42 + 0.06 * math.sin(i * 2.7), width=0.05, outline=strap, fold=0.5, droop=1.25, twist=30.0 * math.sin(i), low=(9, 2))

    stalk = [(0.0, 0.0, 0.55), (0.01, 0.0, 0.68), (0.04, 0.02, 0.80), (0.08, 0.04, 0.88)]
    lf.tube("Stalk", stalk, [0.012, 0.01, 0.008, 0.007], tube_paint(srgb(0.70, 0.75, 0.55)), material=SKIN, segments_high=10, segments_low=5, low_stride=1, tip=False)
    pod(lf, (0.10, 0.05, 0.86), (0.04, 0.04, 0.065), (0.4, 0.2, -1.0), srgb(0.55, 0.95, 1.0), 1.0, 85, pattern="cells")

    return lf


def leaved_large_a():
    """An umbrella tree: a flared, buttressed trunk forking into limbs that hold a flat crown of broad leaves."""
    lf = Lifeform("LeavedLargeA", "LeavedLarge", kind="plant", symmetric=False)
    lf.body_triangles = 900
    lf.body_material = DETAIL
    rng = np.random.RandomState(89)
    lf.chain([(0.0, 0.0, 0.0), (0.01, 0.0, 0.20), (0.0, 0.01, 0.40), (0.0, 0.0, 0.48)], [0.07, 0.055, 0.048, 0.045], k=0.02, tag="trunk")

    for i in range(6):
        a = 2.0 * math.pi * i / 6 + 0.3
        c, s = math.cos(a), math.sin(a)
        lf.chain([(0.02 * c, 0.02 * s, 0.10), (0.10 * c, 0.10 * s, 0.025), (0.17 * c, 0.17 * s, 0.0)], [0.035, 0.025, 0.012], k=0.01, join_k=0.04, tag="root")

    tips = []

    for i in range(5):
        a = 2.0 * math.pi * i / 5 + 0.1
        c, s = math.cos(a), math.sin(a)
        elbow = (0.17 * c, 0.17 * s, 0.66)
        lf.chain([(0.0, 0.0, 0.46), elbow], [0.04, 0.026], k=0.01, join_k=0.03, tag="branch")

        for j, turn in enumerate((-0.45, 0.45)):
            b = a + turn
            tip = (elbow[0] + 0.17 * math.cos(b), elbow[1] + 0.17 * math.sin(b), 0.76 + 0.02 * j)
            lf.chain([elbow, tip], [0.022, 0.01], k=0.008, tag="branch")
            tips.append(vec(tip))

    # short limbs up the middle, so that the crown has no hole
    for i in range(4):
        a = 2.0 * math.pi * i / 4 + 0.7
        tip = (0.08 * math.cos(a), 0.08 * math.sin(a), 0.80)
        lf.chain([(0.0, 0.0, 0.46), (0.03 * math.cos(a), 0.03 * math.sin(a), 0.66), tip], [0.03, 0.018, 0.009], k=0.01, join_k=0.02, tag="branch")
        tips.append(vec(tip))

    lf.flat_base(0.0, 0.004)
    lf.displace = relief(89, "bark", amplitude=0.004, scale=45.0, lumps=0.003, symmetric=False)
    lf.paint = skin(89, "mottled", light=0.48, dark=0.34, belly=0.48, scale=12.0, hue=BARK_HUE, smoothness=0.22, symmetric=False, cavity=0.6)
    paint = leaf_paint(89, light=0.80, vein=0.93, edge=0.55, smoothness=0.55)

    for t, tip in enumerate(tips):
        outward = normalize(tip * vec((1.0, 1.0, 0.0)))
        for j in range(6):
            a = 2.0 * math.pi * j / 6 + t * 0.7
            around = vec((math.cos(a), math.sin(a), 0.0))
            direction = normalize(around + outward * 0.8 + vec((0.0, 0.0, 0.25)))
            lf.leaf("Leaf", paint, base=tip - direction * 0.01, direction=direction, length=0.17 + 0.03 * rng.rand(), width=0.085, outline=ovate, fold=0.2, droop=0.6, twist=rng.uniform(-20, 20), low=(4, 2))

    return lf


def leaved_large_b():
    """A palm-like tree: a curving ringed trunk, a crown of long feathered fronds and a cluster of glowing fruit."""
    lf = Lifeform("LeavedLargeB", "LeavedLarge", kind="plant", symmetric=False)
    lf.body_triangles = 1200
    lf.body_material = DETAIL
    count = 90
    points = []
    radii = []

    for i in range(count):
        t = i / (count - 1)
        points.append((0.14 * t * t, 0.0, 0.78 * t))
        radii.append((0.062 - 0.024 * t + 0.03 * max(0.0, 0.08 - t)) * (1.0 + 0.07 * math.cos(2.0 * math.pi * t * 22.0)))

    lf.chain(points, radii, k=0.006, tag="trunk")
    lf.sphere((0.14, 0.0, 0.79), 0.05, k=0.02, tag="crown")
    lf.flat_base(0.0, 0.004)
    lf.displace = relief(97, "bark", amplitude=0.003, scale=50.0, lumps=0.002, symmetric=False)
    lf.paint = skin(97, "bands", light=0.50, dark=0.36, belly=0.50, scale=4.5, hue=BARK_HUE, smoothness=0.25, symmetric=False, cavity=0.6)
    top = vec((0.14, 0.0, 0.80))
    leaflet_paint = leaf_paint(97, light=0.80, vein=0.92, edge=0.6)
    rachis_paint = tube_paint(srgb(0.70, 0.72, 0.55), srgb(0.80, 0.82, 0.65))

    for i in range(8):
        a = 2.0 * math.pi * i / 8 + 0.2
        c, s = math.cos(a), math.sin(a)
        rise = 0.55 if i % 2 == 0 else 0.25
        frond = []

        for t in np.linspace(0.0, 1.0, 10):
            r = 0.42 * t
            z = rise * 0.45 * t - 0.55 * t * t * (1.0 - rise * 0.6)
            frond.append(top + vec((r * c, r * s, z)))

        lf.tube("Rachis", frond, [0.008 * (1.0 - 0.7 * t) for t in np.linspace(0.0, 1.0, 10)], rachis_paint, material=SKIN, segments_high=8, segments_low=4, low_stride=3)
        side = vec((-s, c, 0.0))

        for j in range(1, 10):
            t = j / 10.0
            p = frond[j]
            along = normalize(frond[min(j + 1, 9)] - frond[j - 1])

            for sign in (1.0, -1.0):
                direction = normalize(side * sign + along * 0.55 + vec((0.0, 0.0, -0.15)))
                lf.leaf("Leaflet", leaflet_paint, base=p, direction=direction, length=0.13 * (1.0 - 0.45 * t) + 0.03, width=0.016, outline=lance, fold=0.4, droop=0.5, low=(2, 1))

    for i in range(6):
        a = 2.0 * math.pi * i / 6
        center = top + vec((0.045 * math.cos(a), 0.045 * math.sin(a), -0.05 - 0.02 * (i % 2)))
        pod(lf, center, (0.028, 0.028, 0.03), (0.0, 0.0, -1.0), srgb(1.0, 0.55, 0.15), 0.8, 98)

    return lf


BUILDERS = [
    snakelike, bilateral_no_legs, bilateral_one_leg, bilateral_two_legs, bilateral_four_legs, bilateral_many_legs,
    radial_no_legs, radial_few_legs, radial_many_legs, amorphous_no_legs, amorphous_legs,
    leaved_small_a, leaved_small_b, leaved_medium_a, leaved_medium_b, leaved_large_a, leaved_large_b,
]


def main():
    arguments = script_arguments()
    folder = LIFEFORMS_FOLDER
    export = "--no-export" not in arguments

    if "--out" in arguments:
        folder = os.path.abspath(arguments[arguments.index("--out") + 1])

    wanted = None

    if "--plan" in arguments:
        wanted = set(arguments[arguments.index("--plan") + 1].split(","))

    entries = []

    for builder in BUILDERS:
        lifeform = builder()

        if wanted is not None and lifeform.name not in wanted:
            continue

        try:
            entries.append(build_one(lifeform, folder, export))
        except Exception:  # noqa: BLE001
            import traceback
            log("%s FAILED: %s" % (lifeform.name, traceback.format_exc()))

    if export and entries:
        manifest = os.path.join(os.path.dirname(os.path.abspath(__file__)), "manifest-lifeforms.json") if folder == LIFEFORMS_FOLDER else os.path.join(folder, "manifest-lifeforms.json")
        write_manifest(entries, manifest)
        log("manifest " + manifest)


main()
