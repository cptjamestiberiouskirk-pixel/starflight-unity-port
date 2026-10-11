# Builds the textures of the console's system map with Blender 5.2 (its Python and numpy, no scene):
#   System Map Star Field.png  the background, a star field with a faint nebula, 1024 x 1024, tiles seamlessly
#   System Map Ship.png        the ship marker, a chevron with a glow and a dotted heading line ahead of it
#
#   blender --background --factory-startup --python DevTools/SystemMap/build_system_map_art.py -- <output folder>
#
# Nothing is random at build time (fixed seeds), so a rebuild gives the same pictures. The planets, the star, the orbits
# and the ecosphere are drawn by shaders (Assets/Shaders/System Map *.shader) and need no textures.
import bpy, sys, os, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.getcwd(), "Assets", "Game Objects", "Display", "System Map")
os.makedirs(OUT, exist_ok=True)


def save_rgba(rgba, path):
    # rgba: rows from the bottom up, values 0 to 1 (written as they are: sRGB)
    h, w = rgba.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(np.clip(rgba, 0, 1), dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    print("SYSTEMMAP wrote", path, w, "x", h)


def blur(img, sigma):
    # a gaussian blur through the FFT: it wraps around the edges, so a picture that tiles still tiles afterwards
    fy = np.fft.fftfreq(img.shape[0])[:, None]
    fx = np.fft.rfftfreq(img.shape[1])[None, :]
    g = np.exp(-2.0 * (math.pi ** 2) * sigma ** 2 * (fx ** 2 + fy ** 2))
    if img.ndim == 2:
        return np.fft.irfft2(np.fft.rfft2(img) * g, s=img.shape)
    return np.stack([np.fft.irfft2(np.fft.rfft2(img[..., c]) * g, s=img.shape[:2]) for c in range(img.shape[2])], -1)


def normalized(a):
    return (a - a.min()) / max(a.max() - a.min(), 1e-6)


def star_field(size=1024, seed=1986):
    # about 2 texels per map unit: the display shows roughly 512 x 512 map units
    rng = np.random.default_rng(seed)
    rgb = np.zeros((size, size, 3), np.float32) + np.array([0.006, 0.008, 0.016], np.float32)

    # the nebula: two scales of smooth noise, in a violet and a faint teal
    n1 = normalized(blur(rng.standard_normal((size, size)).astype(np.float32), 85))
    n2 = normalized(blur(rng.standard_normal((size, size)).astype(np.float32), 33))
    nebula = np.clip(n1 * 0.75 + n2 * 0.35 - 0.45, 0, 1) ** 1.6
    rgb += np.array([0.16, 0.10, 0.30], np.float32) * (nebula * 0.32)[..., None]
    rgb += np.array([0.05, 0.18, 0.22], np.float32) * (np.clip(n2 - 0.55, 0, 1) * 0.25)[..., None]

    # the stars: most of them faint, a few bright ones with a small glow, in four colours
    count = 1000
    px = rng.integers(0, size, count)
    py = rng.integers(0, size, count)
    brightness = 0.08 + 0.9 * rng.random(count) ** 7
    tints = np.array([(1, 1, 1), (0.75, 0.85, 1), (1, 0.9, 0.75), (1, 0.8, 0.7)], np.float32)[rng.integers(0, 4, count)]
    points = np.zeros((size, size, 3), np.float32)
    np.add.at(points, (py, px), tints * brightness[:, None])
    glow = blur(points, 1.6) * 6.0
    rgb += blur(points, 0.55) * 2.2 + np.where(glow > 0.05, glow, 0) * 0.35

    rgba = np.ones((size, size, 4), np.float32)
    rgba[..., :3] = np.clip(rgb, 0, 1)
    return rgba


def ship_marker(units_wide=20, units_high=60, texels_per_unit=4, chevron_center=10.0):
    # a chevron pointing up (the heading), with its middle chevron_center units above the bottom, and dots ahead of it
    w, h = units_wide * texels_per_unit, units_high * texels_per_unit
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    cx, cy = w / 2.0, chevron_center * texels_per_unit
    size = 6.5 * texels_per_unit

    # the chevron, 4 x 4 supersampled: nose, left wing, notch, right wing
    pts = [(0, 1.0), (-0.68, -0.72), (0, -0.36), (0.68, -0.72)]
    P = [(cx + x * size, cy + y * size) for x, y in pts]
    tris = [(P[0], P[1], P[2]), (P[0], P[2], P[3])]
    chevron = np.zeros((h, w), np.float32)
    for ox in range(4):
        for oy in range(4):
            qx = xs + (ox + 0.5) / 4.0
            qy = ys + (oy + 0.5) / 4.0
            inside = np.zeros((h, w), bool)
            for (ax, ay), (bx, by), (tx, ty) in tris:
                d1 = (qx - bx) * (ay - by) - (ax - bx) * (qy - by)
                d2 = (qx - tx) * (by - ty) - (bx - tx) * (qy - ty)
                d3 = (qx - ax) * (ty - ay) - (tx - ax) * (qy - ay)
                inside |= ~(((d1 < 0) | (d2 < 0) | (d3 < 0)) & ((d1 > 0) | (d2 > 0) | (d3 > 0)))
            chevron += inside
    chevron /= 16.0

    # its glow (the blur wraps, so pad first and crop after)
    pad = 32
    padded = np.pad(chevron, pad)
    glow = np.clip(blur(padded, 2.5 * texels_per_unit)[pad:-pad, pad:-pad] * 1.1, 0, 1)

    # the heading: six dots ahead, fading out
    dots = np.zeros((h, w), np.float32)
    for i in range(3, 9):
        dy = cy + i * 5 * texels_per_unit
        r2 = ((xs + 0.5 - cx) ** 2 + (ys + 0.5 - dy) ** 2) / (texels_per_unit ** 2)
        dots = np.maximum(dots, np.exp(-r2 / 0.5) * (1 - i / 10.0) * 0.8)

    # straight alpha: the chevron over the glow and the dots
    white = np.array([0.85, 0.97, 1.0], np.float32)
    cyan = np.array([0.35, 0.85, 1.0], np.float32)
    dot_colour = np.array([0.55, 0.9, 1.0], np.float32)
    under = np.maximum(glow * 0.85, dots)
    under_rgb = np.where((dots > glow * 0.85)[..., None], dot_colour, cyan)
    alpha = chevron + (1 - chevron) * under
    premultiplied = chevron[..., None] * white + ((1 - chevron) * under)[..., None] * under_rgb
    rgba = np.zeros((h, w, 4), np.float32)
    rgba[..., :3] = premultiplied / np.maximum(alpha, 1e-4)[..., None]
    rgba[..., 3] = alpha
    return rgba


save_rgba(star_field(), os.path.join(OUT, "System Map Star Field.png"))
save_rgba(ship_marker(), os.path.join(OUT, "System Map Ship.png"))
print("SYSTEMMAP done")
