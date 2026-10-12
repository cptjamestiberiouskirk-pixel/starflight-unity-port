# 3D star system view mock-up: renders the main view beside a planet of one star system at the game's framing (camera 1024 above the ship, 30 degree
# vertical field of view, 1372 x 1012 pixels), with the planet lit from its star in the star's colour and a faint atmosphere on the lit limb, then
# composites it in numpy over a new sky (Milky Way band, faint nebula haze, stars of many colours), with the planet's orbit path and the ship from the
# real capture. Two looks: "realistic" and "cinematic" (adds bloom, a soft flare and a richer sky).
#
#   blender --background --factory-startup --python mockup_system_view.py -- <game data json> <maps dir> <captures dir> <out dir> <star id> <orbit> [<orbit> ...]
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
DATA, MAPS, CAPS, OUT = argv[0], argv[1], argv[2], argv[3]
STAR_ID = int(argv[4])
ORBITS = [int(a) for a in argv[5:]]
GAME_TIME = 0.0
os.makedirs(OUT, exist_ok=True)

W, H = 1920, 1080
CX, CY = 640, 540           # the ship's place on the screen (the camera's viewport is left of the console)
CROP = (12, 56, 1384, 1068)  # the main view
CAM_HEIGHT = 1024.0
FOV_V = 33.7                 # matched to the captures (the planet's size and distance on screen)

def conv(o):
    if isinstance(o, dict): return {k: conv(v) for k, v in o.items()}
    if isinstance(o, list): return [conv(v) for v in o]
    if isinstance(o, str):
        try: return int(o)
        except ValueError:
            try: return float(o)
            except ValueError: return o
    return o

data = conv(json.load(open(DATA, encoding="utf-8-sig")))
star = next(s for s in data["m_starList"] if s["m_id"] == STAR_ID)
surfaces = data["m_surfaceList"]
atmospheres = data["m_atmosphereList"]
planets = {p["m_orbitPosition"]: p for p in data["m_planetList"] if p["m_starId"] == STAR_ID}

# SystemDisplay.GetStarColor (blackbody, as the owner ruled for the map)
STAR_RGB = {"M": (1, .52, .32), "K": (1, .70, .42), "G": (1, .88, .60), "F": (1, .97, .90), "A": (.80, .85, 1), "B": (.68, .76, 1), "O": (.61, .69, 1)}
star_rgb = STAR_RGB.get(star["m_class"], (1, 1, 1))

def orbit_angle(p): return ((GAME_TIME + 1000.0 + STAR_ID * 4) / (122.0 * p["m_orbitPosition"])) * 360.0
def orbit_radius(p): return (50.0 + (225.0 - 50.0) * (p["m_orbitPosition"] - 1) / 7.0) * (8192.0 / 256.0)
def planet_xz(p):
    a = math.radians(orbit_angle(p)); r = orbit_radius(p)
    return (-math.sin(a) * r, math.cos(a) * r)
def planet_scale(p): return 32.0 + (256.0 - 32.0) * math.sqrt(max(p["m_mass"] - 6, 0) / 500000.0)
def is_gas_giant(p): return p["m_surfaceId"] == 1

def hsv(h):
    h = (h % 360) / 60.0; i = int(h); f = h - i
    return [(1, f, 0), (1 - f, 1, 0), (0, 1, f), (0, 1 - f, 1), (f, 0, 1), (1, 0, 1 - f)][i % 6]

def rim_rgba(p):   # SystemDisplay.GetAtmosphereRimColor
    if is_gas_giant(p): return (0.85, 0.72, 0.95, 0.6)
    if p["m_atmosphereDensityId"] == 0: return (0, 0, 0, 0)
    c = np.array(hsv(atmospheres[p["m_atmosphereIdA"]]["m_hue"]), float)
    if p.get("m_atmosphereIdB", -1) != -1: c = c + 0.5 * np.array(hsv(atmospheres[p["m_atmosphereIdB"]]["m_hue"]))
    c = c / max(c.max(), 1e-6)
    c = c + (1 - c) * 0.45
    return (c[0], c[1], c[2], min(1.0, p["m_atmosphereDensityId"] / 5.0 * 1.2))

def map_png(p):
    prefix = "star%d_orbit%d_planet%d_" % (STAR_ID, p["m_orbitPosition"], p["m_id"])
    for f in os.listdir(MAPS):
        if f.startswith(prefix) and f.endswith(".png"): return os.path.join(MAPS, f)
    return None

# ---------------------------------------------------------------- the sky (numpy, shared by every frame)
rng = np.random.default_rng(1986)

def blur(img, r):
    if r <= 0: return img
    k = np.exp(-0.5 * (np.arange(-3 * r, 3 * r + 1) / r) ** 2); k /= k.sum()
    out = img
    for ax in (0, 1):
        out = np.apply_along_axis(lambda v: np.convolve(v, k, mode="same"), ax, out)
    return out

def fbm(h, w, octaves, seed):
    r = np.random.default_rng(seed); acc = np.zeros((h, w)); amp = 1.0; tot = 0
    for o in range(octaves):
        s = 2 ** (o + 2)
        g = r.random((h // s + 2, w // s + 2))
        ys = np.linspace(0, g.shape[0] - 1.001, h); xs = np.linspace(0, g.shape[1] - 1.001, w)
        y0 = ys.astype(int); x0 = xs.astype(int); fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
        acc += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy); tot += amp; amp *= 0.55
    return acc / tot

def make_sky(cinematic):
    h, w = H + 200, W + 200
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    # the Milky Way: a band across the view, with dust lanes
    d = ((xx - w * 0.5) * math.sin(math.radians(28)) - (yy - h * 0.5) * math.cos(math.radians(28))) / (h * 0.32)
    band = np.exp(-d * d)
    n1 = blur(fbm(h, w, 4, 11), 4); n2 = blur(fbm(h, w, 4, 12), 4)
    glow = band * (0.35 + 0.65 * n1) * (1 - 0.75 * np.clip((n2 - 0.45) * 3, 0, 1) * band)
    sky = np.zeros((h, w, 3))
    sky += glow[..., None] * np.array([0.085, 0.078, 0.090]) * (1.5 if cinematic else 1.0)
    # faint nebula haze, two hues
    n3 = blur(fbm(h, w, 3, 21), 6); n4 = blur(fbm(h, w, 3, 22), 6)
    neb = np.clip((n3 - 0.5) * 2.2, 0, 1) ** 2; neb2 = np.clip((n4 - 0.55) * 2.5, 0, 1) ** 2
    sky += neb[..., None] * np.array([0.030, 0.016, 0.050]) * (2.0 if cinematic else 1.0)
    sky += neb2[..., None] * np.array([0.010, 0.034, 0.046]) * (2.0 if cinematic else 1.0)
    # stars: many faint, few bright, colours from red to blue-white, more of them in the band
    count = int(w * h / 1100)
    sx = rng.random(count) * w; sy = rng.random(count) * h
    keep = rng.random(count) < (0.35 + 0.65 * np.exp(-((((sx - w * 0.5) * math.sin(math.radians(28)) - (sy - h * 0.5) * math.cos(math.radians(28))) / (h * 0.32)) ** 2)))
    sx, sy = sx[keep], sy[keep]
    mag = rng.pareto(1.7, sx.size) * 0.30 + 0.18
    t = rng.random(sx.size)
    palette = np.array([[1.0, 0.62, 0.42], [1.0, 0.82, 0.62], [1.0, 0.96, 0.88], [0.86, 0.90, 1.0], [0.70, 0.80, 1.0]])
    col = palette[np.clip((t * 5).astype(int), 0, 4)]
    pts = np.zeros((h, w, 3))
    ix = np.clip(sx.astype(int), 0, w - 1); iy = np.clip(sy.astype(int), 0, h - 1)
    np.add.at(pts, (iy, ix), col * np.minimum(mag, 3.0)[:, None])
    sky += pts * 0.8 + blur(pts, 0.8) * 2.2 + (blur(pts, 2.5) * (2.0 if cinematic else 0.9))
    sky = blur(sky, 0.6) * 0.35 + sky * 0.65
    return sky

# ---------------------------------------------------------------- Blender: one planet, lit from its star
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 48
scene.cycles.use_denoising = False
scene.cycles.max_bounces = 0
scene.render.film_transparent = True
scene.render.resolution_x = W; scene.render.resolution_y = H; scene.render.resolution_percentage = 100
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("w"); world.color = (0.0006, 0.0006, 0.0008); scene.world = world

cam_data = bpy.data.cameras.new("cam"); cam_data.sensor_fit = "VERTICAL"; cam_data.angle_y = math.radians(FOV_V)
cam_data.clip_start = 1.0; cam_data.clip_end = 100000.0
cam_data.shift_x = (W / 2 - CX) / H   # with a vertical sensor fit the shift is in units of the height
cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam

sun_data = bpy.data.lights.new("sun", "SUN"); sun_data.energy = 6.5; sun_data.color = star_rgb; sun_data.angle = math.radians(0.5)
sun = bpy.data.objects.new("sun", sun_data); scene.collection.objects.link(sun)

def project(pt, ship):
    # game (x, y, z) to pixel (u, v), camera above the ship looking down, north up
    dx, dz = pt[0] - ship[0], pt[2] - ship[1]; depth = CAM_HEIGHT - pt[1]
    f = (H * 0.5) / math.tan(math.radians(FOV_V * 0.5))
    return (CX + dx * f / depth, CY - dz * f / depth)

def render_planet(p, ship):
    for o in list(bpy.data.objects):
        if o.name.startswith("planet") or o.name.startswith("atmo"): bpy.data.objects.remove(o)
    px, pz = planet_xz(p); scale = planet_scale(p)
    radius = scale
    centre = (px, -16.0 - scale, pz)
    # Blender: x = game x, y = game z, z = game y
    cam.location = (ship[0], ship[1], CAM_HEIGHT); cam.rotation_euler = (0, 0, 0)
    loc = Vector((centre[0], centre[2], centre[1]))
    bpy.ops.mesh.primitive_uv_sphere_add(segments=128, ring_count=64, radius=radius, location=loc)
    pl = bpy.context.active_object; pl.name = "planet"
    bpy.ops.object.shade_smooth()
    # poles toward screen north, tilted 30 degrees toward the camera, as the game turns its planets
    pl.rotation_euler = (math.radians(90 - 30), 0, math.radians(orbit_angle(p) * 7 % 360))
    mat = bpy.data.materials.new("pm"); mat.use_nodes = True; nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]; bsdf.inputs["Roughness"].default_value = 0.9
    bsdf.inputs["Specular IOR Level"].default_value = 0.15
    png = map_png(p)
    if png:
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(png)
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    pl.data.materials.append(mat)
    # light from the star: the direction from the star to the planet
    d = Vector((centre[0], centre[2], 0)).normalized()
    sun.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    L = -d
    rim = rim_rgba(p)
    if False:   # the atmosphere is drawn in 2D (see atmosphere())
        bpy.ops.mesh.primitive_uv_sphere_add(segments=128, ring_count=64, radius=radius * 1.09, location=loc)
        at = bpy.context.active_object; at.name = "atmo"; bpy.ops.object.shade_smooth()
        am = bpy.data.materials.new("am"); am.use_nodes = True; an = am.node_tree
        for n in list(an.nodes): an.nodes.remove(n)
        out = an.nodes.new("ShaderNodeOutputMaterial")
        geo = an.nodes.new("ShaderNodeNewGeometry")
        lw = an.nodes.new("ShaderNodeLayerWeight"); lw.inputs["Blend"].default_value = 0.6
        dot = an.nodes.new("ShaderNodeVectorMath"); dot.operation = "DOT_PRODUCT"
        an.links.new(geo.outputs["Normal"], dot.inputs[0]); dot.inputs[1].default_value = (L.x, L.y, L.z)
        lit = an.nodes.new("ShaderNodeMapRange"); lit.inputs["From Min"].default_value = -0.15; lit.inputs["From Max"].default_value = 0.9
        an.links.new(dot.outputs["Value"], lit.inputs["Value"])
        pw = an.nodes.new("ShaderNodeMath"); pw.operation = "POWER"; pw.inputs[1].default_value = 1.6
        an.links.new(lw.outputs["Facing"], pw.inputs[0])
        mul = an.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"
        an.links.new(pw.outputs[0], mul.inputs[0]); an.links.new(lit.outputs["Result"], mul.inputs[1])
        mul2 = an.nodes.new("ShaderNodeMath"); mul2.operation = "MULTIPLY"; mul2.inputs[1].default_value = rim[3] * 1.4
        an.links.new(mul.outputs[0], mul2.inputs[0])
        em = an.nodes.new("ShaderNodeEmission"); em.inputs["Color"].default_value = (rim[0] * star_rgb[0], rim[1] * star_rgb[1], rim[2] * star_rgb[2], 1)
        an.links.new(mul2.outputs[0], em.inputs["Strength"])
        tr = an.nodes.new("ShaderNodeBsdfTransparent")
        add = an.nodes.new("ShaderNodeAddShader")
        an.links.new(tr.outputs[0], add.inputs[0]); an.links.new(em.outputs[0], add.inputs[1])
        an.links.new(add.outputs[0], out.inputs["Surface"])
        am.blend_method = "BLEND" if hasattr(am, "blend_method") else None
        at.data.materials.append(am)
    path = os.path.join(OUT, "render_%d.png" % p["m_orbitPosition"])
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    px_ = np.array(img.pixels[:], np.float32).reshape(H, W, 4)[::-1]
    bpy.data.images.remove(img)
    return px_, (px, pz), radius

# ---------------------------------------------------------------- compositing
def load_png(path):
    img = bpy.data.images.load(path)
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)[::-1]
    bpy.data.images.remove(img)
    return a

def draw_orbit(canvas, ship, p, cinematic):
    r = orbit_radius(p); a0 = math.radians(orbit_angle(p))
    acc = np.zeros(canvas.shape[:2])
    for i in range(0, 2400):
        a = a0 - math.radians(i * 0.05)      # behind the planet (it moves to larger angles)
        g = (-math.sin(a) * r, 0.0, math.cos(a) * r)
        u, v = project(g, ship)
        if -2 < u < W + 1 and -2 < v < H + 1:
            behind = i * 0.05
            alpha = 0.10 + 0.35 * math.exp(-behind / 18.0)
            iu, iv = int(u), int(v)
            if 0 <= iu < W and 0 <= iv < H: acc[iv, iu] = max(acc[iv, iu], alpha)
    for i in range(1, 600):                  # ahead of the planet, fainter
        a = a0 + math.radians(i * 0.05)
        u, v = project((-math.sin(a) * r, 0.0, math.cos(a) * r), ship)
        iu, iv = int(u), int(v)
        if 0 <= iu < W and 0 <= iv < H: acc[iv, iu] = max(acc[iv, iu], 0.08)
    line = np.clip(blur(acc, 0.8) * 2.2, 0, 1)
    col = np.array(star_rgb) * 0.55 + 0.25
    return canvas + line[..., None] * col * (0.55 if cinematic else 0.4)

def ship_layer(capture):
    cap = load_png(capture)[..., :3]
    cy, cx = CY, CX
    box = cap[cy - 75:cy + 75, cx - 45:cx + 45]
    lum = box.max(axis=2)
    alpha = np.clip((lum - 0.05) * 4, 0, 1)
    # keep only the ship (the middle of the box): stars around it are dropped
    yy, xx = np.mgrid[0:box.shape[0], 0:box.shape[1]]
    alpha *= ((np.abs(xx - 45) < 26) & (np.abs(yy - 75) < 64))
    return box, alpha, (cy - 75, cx - 45)

def atmosphere(out, p, ship, cinematic):
    # the atmosphere: a soft glow just beyond the limb and a brighter limb, only on the side that faces the star
    rim = rim_rgba(p)
    if rim[3] <= 0: return out
    px, pz = planet_xz(p); scale = planet_scale(p)
    u, v = project((px, -16.0 - scale, pz), ship)
    f = (H * 0.5) / math.tan(math.radians(FOV_V * 0.5))
    rpx = scale * f / (CAM_HEIGHT + 16.0 + scale)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    dx, dy = xx - u, yy - v
    dist = np.sqrt(dx * dx + dy * dy) / rpx
    n = math.hypot(px, pz) or 1.0
    sx, sy = -px / n, pz / n                 # toward the star, on the screen (v points down)
    cosang = (dx * sx + dy * sy) / (dist * rpx + 1e-6)
    lit = np.clip(cosang * 0.75 + 0.35, 0, 1) ** 1.6
    halo = np.where(dist > 1.0, np.exp(-(dist - 1.0) / 0.05), 0.0)
    limb = np.where(dist <= 1.0, np.clip((dist - 0.80) / 0.20, 0, 1) ** 2, 0.0)
    glow = (halo * 1.5 + limb * 0.7) * lit * rim[3]
    col = np.array(rim[:3]) * np.array(star_rgb)
    return out + glow[..., None] * col * (1.3 if cinematic else 1.0)

def composite(p, ship, capture, cinematic, sky):
    planet, (px, pz), radius = PLANETS[p["m_orbitPosition"]]
    off = (60, 40)
    out = sky[off[0]:off[0] + H, off[1]:off[1] + W].copy()
    out = draw_orbit(out, ship, p, cinematic)
    rgb, a = planet[..., :3], planet[..., 3:4]
    out = out * (1 - a) + rgb
    out = atmosphere(out, p, ship, cinematic)
    box, alpha, (y0, x0) = ship_layer(capture)
    region = out[y0:y0 + box.shape[0], x0:x0 + box.shape[1]]
    out[y0:y0 + box.shape[0], x0:x0 + box.shape[1]] = region * (1 - alpha[..., None]) + box * alpha[..., None]
    # a faint engine glow behind the ship
    yy, xx = np.mgrid[0:H, 0:W]
    g = np.exp(-(((xx - CX) / 5.0) ** 2 + ((yy - (CY + 48)) / 9.0) ** 2))
    out += g[..., None] * np.array([0.35, 0.55, 1.0]) * (0.8 if cinematic else 0.5)
    if cinematic:
        bright = np.clip(out - 0.6, 0, None)
        out += blur(bright, 6) * 0.9 + blur(bright, 18) * 0.6
        # a soft flare from the star's direction across the view
        u, v = project((0.0, 0.0, 0.0), ship)
        vx, vy = CX - u, CY - v; n = math.hypot(vx, vy) or 1.0
        for k, (t, s, c) in enumerate([(0.15, 60, 0.04), (0.35, 28, 0.05), (0.6, 14, 0.06)]):
            fx, fy = CX - vx / n * 420 * t * 2, CY - vy / n * 420 * t * 2
            out += np.exp(-(((xx - fx) ** 2 + (yy - fy) ** 2) / (2 * s * s)))[..., None] * np.array(star_rgb) * c
    return np.clip(out, 0, 1)[CROP[1]:CROP[3], CROP[0]:CROP[2]]

def save(arr, path):
    h, w = arr.shape[:2]
    rgba = np.ones((h, w, 4), np.float32); rgba[..., :3] = arr
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    img.pixels.foreach_set(rgba[::-1].ravel()); img.filepath_raw = path; img.file_format = "PNG"; img.save()
    bpy.data.images.remove(img)

PLANETS = {}
for orbit in ORBITS:
    p = planets[orbit]
    px, pz = planet_xz(p); scale = planet_scale(p)
    ship = (px, pz - (scale * 1.5 + 120.0))
    PLANETS[orbit] = render_planet(p, ship)
    print("MOCKUP planet", p["m_id"], "orbit", orbit, "scale", round(scale, 1), "pos", (round(px), round(pz)))

for cinematic in (False, True):
    sky = make_sky(cinematic)
    for orbit in ORBITS:
        p = planets[orbit]
        px, pz = planet_xz(p); scale = planet_scale(p)
        ship = (px, pz - (scale * 1.5 + 120.0))
        surf = surfaces[p["m_surfaceId"]]["m_name"].replace(" ", "")
        capture = os.path.join(CAPS, "star%d_%s_orbit%d_%s.png" % (STAR_ID, star["m_class"], orbit, surf))
        img = composite(p, ship, capture, cinematic, sky)
        save(img, os.path.join(OUT, "star%d_orbit%d_%s.png" % (STAR_ID, orbit, "cinematic" if cinematic else "realistic")))
print("MOCKUP done")
