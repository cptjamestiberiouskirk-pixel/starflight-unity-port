# Makes the Milky Way of the sky: a faint band of light round the whole sky, with star clouds, dark dust lanes and a warmer
# bulge, as an equirectangular picture that Unity imports as a cubemap (Assets/Game Objects/Skyboxes/Milky Way/milky_way.png).
# The skybox shader adds it faintly under the territory's nebula, by the direction of each pixel.
#
#   blender --background --factory-startup --python DevTools/SystemView/build_sky_art.py [-- <out png>]
#
# Everything is worked out on directions (3D noise), so the picture has no seam and nothing pinches at its poles. Top row
# = up (+Y), the middle column = forward (+Z). The band goes through straight down (-Y), where the camera of the star
# system view looks, at 28 degrees from the screen's up, so that it crosses the view on a slant.
import bpy, sys, os, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = argv[0] if argv else os.path.join(ROOT, "Assets", "Game Objects", "Skyboxes", "Milky Way", "milky_way.png")
os.makedirs(os.path.dirname(OUT), exist_ok=True)

W, H = 4096, 2048
SEED = 1986

# ---------------------------------------------------------------- directions
x = (np.arange(W, dtype=np.float32) + 0.5) / W * 2.0 * np.pi - np.pi
y = np.pi / 2.0 - (np.arange(H, dtype=np.float32) + 0.5) / H * np.pi
lon, lat = np.meshgrid(x, y)
D = np.stack([np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)], axis=-1).astype(np.float32)
del lon, lat

# ---------------------------------------------------------------- 3D value noise
def hash3(ix, iy, iz, seed):
    h = (ix.astype(np.int64) * 73856093) ^ (iy.astype(np.int64) * 19349663) ^ (iz.astype(np.int64) * 83492791) ^ (seed * 2654435761)
    h = (h ^ (h >> 13)) * 1274126177
    h = h ^ (h >> 16)
    return ((h & 0xFFFFFF).astype(np.float32) / float(0xFFFFFF))

def noise3(p, seed):
    f = np.floor(p); t = p - f; t = t * t * (3.0 - 2.0 * t)
    i = f.astype(np.int64)
    out = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (t[..., 0] if dx else 1 - t[..., 0]) * (t[..., 1] if dy else 1 - t[..., 1]) * (t[..., 2] if dz else 1 - t[..., 2])
                out = out + w * hash3(i[..., 0] + dx, i[..., 1] + dy, i[..., 2] + dz, seed)
    return out

def fbm(p, octaves, seed, lacunarity=2.0, gain=0.5):
    acc = np.zeros(p.shape[:-1], np.float32); amp = 1.0; tot = 0.0; freq = 1.0
    for o in range(octaves):
        acc += amp * noise3(p * freq, seed + o * 17); tot += amp; amp *= gain; freq *= lacunarity
    return acc / tot

# ---------------------------------------------------------------- the band
theta = math.radians(-28.0)
n = np.array([math.cos(theta), 0.0, math.sin(theta)], np.float32)        # the band's plane goes through straight down
e1 = np.array([0.0, -1.0, 0.0], np.float32)
e2 = np.cross(n, e1)
b = np.arcsin(np.clip(D @ n, -1, 1))                                      # angle from the band's middle
a = np.arctan2(D @ e2, D @ e1)                                            # angle along the band, 0 straight down

print("SKY noise")
warp = fbm(D * 3.0, 4, SEED + 100) - 0.5
width = 0.055 * (0.7 + 0.7 * fbm(D * 3.0, 3, SEED + 200))                # the band swells and narrows
bw = b + warp * 0.05
profile = np.exp(-(bw / width) ** 2)
halo = np.exp(-(bw / (width * 3.0)) ** 2) * 0.25

# the bulge, about 70 degrees along the band from straight down
centre = 1.2
da = np.angle(np.exp(1j * (a - centre)))
bulge = np.exp(-(da / 0.45) ** 2) * np.exp(-(b / 0.12) ** 2)

clouds = fbm(D * 22.0, 6, SEED + 300)                                     # star clouds
clouds = np.clip((clouds - 0.35) * 1.8, 0, 1)
grain = fbm(D * 140.0, 3, SEED + 400)                                     # unresolved stars, a fine mottle
lanes = fbm(D * 16.0 + warp[..., None] * 3.0, 6, SEED + 500)              # dark dust along the middle
lanes = np.clip((lanes - 0.46) * 4.0, 0, 1) * np.exp(-(bw / (width * 0.6)) ** 2)

light = (profile * (0.30 + 0.70 * clouds) * (0.85 + 0.3 * grain) + halo * 0.4 + bulge * (0.9 + 0.4 * clouds)) * (1.0 - 0.78 * lanes)
light = np.clip(light, 0, None)

# colours: the arms a cool white, the bulge warm, the dust reddening what it lets through
cool = np.array([0.80, 0.86, 1.00], np.float32)
warm = np.array([1.00, 0.84, 0.66], np.float32)
mix = np.clip(bulge * 1.4, 0, 1)[..., None]
rgb = light[..., None] * (cool * (1 - mix) + warm * mix)
rgb *= (1.0 - 0.25 * lanes[..., None] * np.array([0.0, 0.4, 0.9], np.float32))

# a soft tone curve, so that the brightest parts do not clip and the faint ones stay faint
rgb = 1.0 - np.exp(-rgb * 1.4)
print("SKY peak", float(rgb.max()), "mean", float(rgb.mean()))

# ---------------------------------------------------------------- save (Blender stores rows bottom first)
rgba = np.ones((H, W, 4), np.float32); rgba[..., :3] = np.clip(rgb, 0, 1)
img = bpy.data.images.new("milky_way", W, H, alpha=False)
img.colorspace_settings.name = "sRGB"
img.pixels.foreach_set(rgba[::-1].ravel())
img.filepath_raw = OUT; img.file_format = "PNG"
bpy.context.scene.render.image_settings.color_depth = "8"
img.save()
print("SKY wrote", OUT)

# a preview of what the camera of the star system view sees, straight down (33.7 degrees high, 1372 x 1012)
if len(argv) > 1:
    pw, ph = 1372, 1012
    f = (ph * 0.5) / math.tan(math.radians(33.7 * 0.5))
    u = (np.arange(pw) - pw / 2 + 0.5) / f; v = (np.arange(ph) - ph / 2 + 0.5) / f
    U, V = np.meshgrid(u, v)
    d = np.stack([U, -np.ones_like(U), -V], axis=-1); d /= np.linalg.norm(d, axis=-1, keepdims=True)   # screen right = +X, up = +Z
    lo = np.arctan2(d[..., 0], d[..., 2]); la = np.arcsin(d[..., 1])
    px = np.clip(((lo + np.pi) / (2 * np.pi) * W).astype(int), 0, W - 1); py = np.clip(((np.pi / 2 - la) / np.pi * H).astype(int), 0, H - 1)
    view = np.clip(rgb[py, px], 0, 1)
    pr = np.ones((ph, pw, 4), np.float32); pr[..., :3] = view
    im2 = bpy.data.images.new("preview", pw, ph, alpha=False); im2.pixels.foreach_set(pr[::-1].ravel())
    im2.filepath_raw = argv[1]; im2.file_format = "PNG"; im2.save()
    print("SKY preview", argv[1])
