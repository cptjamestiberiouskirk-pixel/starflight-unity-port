"""Shared helpers for the procedural lifeform models (Blender 5.2, run in background mode).

build_lifeforms.py describes each body plan with the Lifeform class and calls build(). A lifeform is made of
two kinds of parts:

- a body: a signed distance field (smooth unions of spheres, ellipsoids and round cones), turned into a dense
  mesh with surface nets, then given skin detail (displacement) and a painted albedo per vertex;
- explicit parts: meshes made from code at a high and a low resolution (eyes, claws, spines, leaves).

The high meshes are baked onto a low mesh (the body decimated, plus the low parts) with Cycles: albedo,
specular and smoothness, a tangent space normal map and ambient occlusion. Nothing is random at build time
(the noise and the scattering use fixed seeds), so a rebuild gives the same models. See README.md.
"""

import json
import math
import os
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
LIFEFORMS_FOLDER = os.path.join(PROJECT_ROOT, "Assets", "Game Objects", "Lifeforms")

# the order of the body plans in the game code (Research/Notes/planet.txt, icons 8 to 24)
BODY_PLANS = [
    "Snakelike", "BilateralNoLegs", "BilateralOneLeg", "BilateralTwoLegs", "BilateralFourLegs", "BilateralManyLegs",
    "RadialNoLegs", "RadialFewLegs", "RadialManyLegs", "AmorphousNoLegs", "AmorphousLegs",
    "LeavedSmall", "LeavedMedium", "LeavedLarge",
]

SKIN = "Skin"
DETAIL = "Detail"


def log(message):
    print("LIFEFORM " + message, flush=True)


# ------------------------------------------------------------------------------------------ small maths

def vec(values):
    return np.asarray(values, dtype=np.float64)


def normalize(v):
    v = vec(v)
    length = np.linalg.norm(v)

    return v / length if length > 1e-12 else v


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)

    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def srgb(r, g, b):
    """A colour given as sRGB (as a colour picker shows it) in the linear values Blender works in."""
    def channel(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    return np.array([channel(r), channel(g), channel(b)])


def rotation(x_degrees=0.0, y_degrees=0.0, z_degrees=0.0):
    """A 3x3 rotation (columns are the local axes in the model frame), applied X, then Y, then Z."""
    m = Matrix.Rotation(math.radians(z_degrees), 3, "Z") @ Matrix.Rotation(math.radians(y_degrees), 3, "Y") @ Matrix.Rotation(math.radians(x_degrees), 3, "X")

    return np.array(m, dtype=np.float64)


def frame_from_direction(direction, up=(0.0, 0.0, 1.0)):
    """A 3x3 frame whose third column is direction (normalized), with the first column across it."""
    w = normalize(direction)
    u = np.cross(vec(up), w)

    if np.linalg.norm(u) < 1e-6:
        u = np.cross(vec((0.0, 1.0, 0.0)), w)

    u = normalize(u)
    v = np.cross(w, u)

    return np.stack([u, v, w], axis=1)


def mirror_x(point):
    p = vec(point).copy()
    p[0] = -p[0]

    return p


# ------------------------------------------------------------------------------------------ noise (numpy, repeatable)

def _hash(ix, iy, iz, seed):
    """A repeatable value in [0, 1) for each lattice point."""
    h = (ix * 374761393 + iy * 668265263 + iz * 1440662683 + (seed + 1) * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h = h ^ (h >> 16)

    return (h & 0xFFFFFF).astype(np.float64) / float(0x1000000)


def value_noise(points, scale=1.0, seed=0):
    """Smooth value noise in [0, 1]."""
    p = np.asarray(points, dtype=np.float64) * scale
    i = np.floor(p).astype(np.int64)
    f = p - i
    f = f * f * (3.0 - 2.0 * f)
    result = np.zeros(len(p))

    for dx in (0, 1):
        wx = f[:, 0] if dx else 1.0 - f[:, 0]
        for dy in (0, 1):
            wy = f[:, 1] if dy else 1.0 - f[:, 1]
            for dz in (0, 1):
                wz = f[:, 2] if dz else 1.0 - f[:, 2]
                result += wx * wy * wz * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)

    return result


def fbm(points, scale=1.0, octaves=4, seed=0, gain=0.5, lacunarity=2.03):
    """Fractal value noise in [0, 1]."""
    total = np.zeros(len(points))
    amplitude = 1.0
    weight = 0.0
    frequency = scale

    for octave in range(octaves):
        total += amplitude * value_noise(points, frequency, seed + octave * 31)
        weight += amplitude
        amplitude *= gain
        frequency *= lacunarity

    return total / weight


def voronoi(points, scale=1.0, seed=0, jitter=0.9):
    """Cellular noise: distance to the nearest and second nearest feature point (in cell units), and the nearest cell's value."""
    p = np.asarray(points, dtype=np.float64) * scale
    i = np.floor(p).astype(np.int64)
    f1 = np.full(len(p), 9.0)
    f2 = np.full(len(p), 9.0)
    cell = np.zeros(len(p))

    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                cx = i[:, 0] + dx
                cy = i[:, 1] + dy
                cz = i[:, 2] + dz
                fx = cx + 0.5 + jitter * (_hash(cx, cy, cz, seed) - 0.5)
                fy = cy + 0.5 + jitter * (_hash(cx, cy, cz, seed + 7) - 0.5)
                fz = cz + 0.5 + jitter * (_hash(cx, cy, cz, seed + 13) - 0.5)
                d = np.sqrt((p[:, 0] - fx) ** 2 + (p[:, 1] - fy) ** 2 + (p[:, 2] - fz) ** 2)
                closer = d < f1
                f2 = np.where(closer, f1, np.minimum(f2, d))
                cell = np.where(closer, _hash(cx, cy, cz, seed + 29), cell)
                f1 = np.where(closer, d, f1)

    return f1, f2, cell


# ------------------------------------------------------------------------------------------ signed distance primitives

class Sphere:
    def __init__(self, center, radius):
        self.center = vec(center)
        self.radius = float(radius)

    def bounds(self):
        return self.center - self.radius, self.center + self.radius

    def distance(self, p):
        return np.linalg.norm(p - self.center, axis=1) - self.radius


class Ellipsoid:
    """An ellipsoid with radii along the columns of frame (a 3x3 rotation). Distance is iq's bound approximation."""

    def __init__(self, center, radii, frame=None):
        self.center = vec(center)
        self.radii = vec(radii)
        self.frame = np.eye(3) if frame is None else np.asarray(frame, dtype=np.float64)

    def bounds(self):
        r = float(np.max(self.radii))

        return self.center - r, self.center + r

    def distance(self, p):
        q = (p - self.center) @ self.frame
        k0 = np.linalg.norm(q / self.radii, axis=1)
        k1 = np.linalg.norm(q / (self.radii * self.radii), axis=1)
        k1 = np.maximum(k1, 1e-9)

        return k0 * (k0 - 1.0) / k1


class RoundCone:
    """Two spheres (a, ra) and (b, rb) joined by their common tangent cone: iq's exact distance."""

    def __init__(self, a, b, ra, rb):
        self.a = vec(a)
        self.b = vec(b)
        self.ra = float(ra)
        self.rb = float(rb)

    def bounds(self):
        r = max(self.ra, self.rb)

        return np.minimum(self.a, self.b) - r, np.maximum(self.a, self.b) + r

    def distance(self, p):
        ba = self.b - self.a
        l2 = float(np.dot(ba, ba))
        rr = self.ra - self.rb
        a2 = l2 - rr * rr

        # one sphere inside the other: the bigger sphere
        if l2 < 1e-12 or a2 <= 1e-12:
            if self.ra >= self.rb:
                return np.linalg.norm(p - self.a, axis=1) - self.ra

            return np.linalg.norm(p - self.b, axis=1) - self.rb

        il2 = 1.0 / l2
        pa = p - self.a
        y = pa @ ba
        z = y - l2
        w = pa * l2 - np.outer(y, ba)
        x2 = np.einsum("ij,ij->i", w, w)
        y2 = y * y * l2
        z2 = z * z * l2
        k = np.sign(rr) * rr * rr * x2
        d_b = np.sqrt(x2 + z2) * il2 - self.rb
        d_a = np.sqrt(x2 + y2) * il2 - self.ra
        d_side = (np.sqrt(np.maximum(x2 * a2 * il2, 0.0)) + y * rr) * il2 - self.ra

        return np.where(np.sign(z) * a2 * z2 > k, d_b, np.where(np.sign(y) * a2 * y2 < k, d_a, d_side))


class Ground:
    """Everything below the plane z = height is inside (used with intersect to give a flat sole)."""

    def __init__(self, height=0.0):
        self.height = height

    def bounds(self):
        return None

    def distance(self, p):
        return self.height - p[:, 2]


def smooth_union(a, b, k):
    if k <= 0.0:
        return np.minimum(a, b)

    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)

    return b + (a - b) * h - k * h * (1.0 - h)


def smooth_subtract(base, cutter, k):
    if k <= 0.0:
        return np.maximum(base, -cutter)

    h = np.clip(0.5 - 0.5 * (base + cutter) / k, 0.0, 1.0)

    return base + (-cutter - base) * h + k * h * (1.0 - h)


def smooth_intersect(a, b, k):
    if k <= 0.0:
        return np.maximum(a, b)

    h = np.clip(0.5 - 0.5 * (b - a) / k, 0.0, 1.0)

    return b + (a - b) * h + k * h * (1.0 - h)


class Field:
    """A body: an ordered list of operations on signed distance primitives. Tags name the region of each union."""

    def __init__(self):
        self.ops = []
        self.tags = []

    def tag_index(self, tag):
        if tag not in self.tags:
            self.tags.append(tag)

        return self.tags.index(tag)

    def add(self, primitive, k, tag):
        self.ops.append(("union", primitive, k, self.tag_index(tag)))

    def cut(self, primitive, k):
        self.ops.append(("subtract", primitive, k, -1))

    def intersect(self, primitive, k):
        self.ops.append(("intersect", primitive, k, -1))

    def bounds(self):
        low = np.full(3, 1e9)
        high = np.full(3, -1e9)

        for mode, primitive, k, tag in self.ops:
            if mode == "union":
                lo, hi = primitive.bounds()
                low = np.minimum(low, lo)
                high = np.maximum(high, hi)

        return low, high

    def _apply(self, d, t, mode, value, k, tag):
        if mode == "union":
            if tag >= 0:
                t = np.where(value < d, tag, t)
            d = smooth_union(d, value, k)
        elif mode == "subtract":
            d = smooth_subtract(d, value, k)
        else:
            d = smooth_intersect(d, value, k)

        return d, t

    def evaluate(self, points, margin):
        """Distance and region tag at points (N, 3). A primitive is only evaluated within its bounds plus margin and k."""
        p = np.asarray(points, dtype=np.float64)
        d = np.full(len(p), 1.0)
        t = np.full(len(p), 0, dtype=np.int32)

        for mode, primitive, k, tag in self.ops:
            box = primitive.bounds()

            if box is None:
                d, t = self._apply(d, t, mode, primitive.distance(p), k, tag)
                continue

            pad = margin + 2.0 * k
            lo = box[0] - pad
            hi = box[1] + pad
            inside = np.all((p >= lo) & (p <= hi), axis=1)
            index = np.nonzero(inside)[0]

            if len(index) == 0:
                continue

            sub_d, sub_t = self._apply(d[index], t[index], mode, primitive.distance(p[index]), k, tag)
            d[index] = sub_d
            t[index] = sub_t

        return d, t

    def evaluate_grid(self, origin, step, shape):
        """Distance on a regular grid, evaluated block by block (each primitive only near its bounds)."""
        d = np.full(shape, 1.0, dtype=np.float32)
        axes = [origin[i] + step * np.arange(shape[i]) for i in range(3)]

        for mode, primitive, k, tag in self.ops:
            box = primitive.bounds()

            if box is None:
                lo_index = np.zeros(3, dtype=int)
                hi_index = np.array(shape)
            else:
                pad = 3.0 * step + 2.0 * k
                lo_index = np.clip(np.floor((box[0] - pad - origin) / step).astype(int), 0, np.array(shape))
                hi_index = np.clip(np.ceil((box[1] + pad - origin) / step).astype(int) + 1, 0, np.array(shape))

            if np.any(hi_index <= lo_index):
                continue

            xs = axes[0][lo_index[0]:hi_index[0]]
            ys = axes[1][lo_index[1]:hi_index[1]]
            zs = axes[2][lo_index[2]:hi_index[2]]
            gx, gy, gz = np.meshgrid(xs, ys, zs, indexing="ij")
            p = np.stack([gx.ravel(), gy.ravel(), gz.ravel()], axis=1)
            value = primitive.distance(p).reshape(gx.shape)
            block = d[lo_index[0]:hi_index[0], lo_index[1]:hi_index[1], lo_index[2]:hi_index[2]].astype(np.float64)
            block, _ = self._apply(block, np.zeros(block.shape, dtype=np.int32), mode, value, k, -1)
            d[lo_index[0]:hi_index[0], lo_index[1]:hi_index[1], lo_index[2]:hi_index[2]] = block

        return d

    def gradient(self, points, eps, margin):
        g = np.zeros((len(points), 3))

        for axis in range(3):
            offset = np.zeros(3)
            offset[axis] = eps
            plus, _ = self.evaluate(points + offset, margin)
            minus, _ = self.evaluate(points - offset, margin)
            g[:, axis] = (plus - minus) / (2.0 * eps)

        return g


# ------------------------------------------------------------------------------------------ surface nets

_CORNERS = [(dx, dy, dz) for dz in (0, 1) for dy in (0, 1) for dx in (0, 1)]
_EDGES = [(a, b) for a in range(8) for b in range(8) if a < b and sum(abs(_CORNERS[a][i] - _CORNERS[b][i]) for i in range(3)) == 1]


def surface_nets(d, origin, step):
    """A quad mesh of the zero level of a distance grid: one vertex per cell the surface crosses."""
    inside = d < 0.0
    nx, ny, nz = d.shape
    cx, cy, cz = nx - 1, ny - 1, nz - 1
    count = np.zeros((cx, cy, cz), dtype=np.uint8)

    for dx, dy, dz in _CORNERS:
        count += inside[dx:dx + cx, dy:dy + cy, dz:dz + cz]

    active = (count > 0) & (count < 8)
    ci, cj, ck = np.nonzero(active)
    offset_sum = np.zeros((len(ci), 3))
    offset_count = np.zeros(len(ci))

    for a, b in _EDGES:
        ca = _CORNERS[a]
        cb = _CORNERS[b]
        da = d[ci + ca[0], cj + ca[1], ck + ca[2]].astype(np.float64)
        db = d[ci + cb[0], cj + cb[1], ck + cb[2]].astype(np.float64)
        crossing = (da < 0.0) != (db < 0.0)
        t = np.where(crossing, da / np.where(crossing, da - db, 1.0), 0.0)
        point = np.array(ca, dtype=np.float64)[None, :] + t[:, None] * (np.array(cb, dtype=np.float64) - np.array(ca, dtype=np.float64))[None, :]
        offset_sum += np.where(crossing[:, None], point, 0.0)
        offset_count += crossing

    offset = offset_sum / np.maximum(offset_count, 1.0)[:, None]
    verts = vec(origin)[None, :] + step * (np.stack([ci, cj, ck], axis=1) + offset)
    index = np.full((cx, cy, cz), -1, dtype=np.int64)
    index[ci, cj, ck] = np.arange(len(ci))
    quads = []

    # x edges: between grid points (i, j, k) and (i + 1, j, k), with the four cells around them
    a = inside[:-1, 1:-1, 1:-1]
    b = inside[1:, 1:-1, 1:-1]
    ii, jj, kk = np.nonzero(a != b)
    jj = jj + 1
    kk = kk + 1
    flip = ~a[ii, jj - 1, kk - 1]
    quads.append((np.stack([index[ii, jj - 1, kk - 1], index[ii, jj, kk - 1], index[ii, jj, kk], index[ii, jj - 1, kk]], axis=1), flip))

    a = inside[1:-1, :-1, 1:-1]
    b = inside[1:-1, 1:, 1:-1]
    ii, jj, kk = np.nonzero(a != b)
    ii = ii + 1
    kk = kk + 1
    flip = ~a[ii - 1, jj, kk - 1]
    quads.append((np.stack([index[ii - 1, jj, kk - 1], index[ii - 1, jj, kk], index[ii, jj, kk], index[ii, jj, kk - 1]], axis=1), flip))

    a = inside[1:-1, 1:-1, :-1]
    b = inside[1:-1, 1:-1, 1:]
    ii, jj, kk = np.nonzero(a != b)
    ii = ii + 1
    jj = jj + 1
    flip = ~a[ii - 1, jj - 1, kk]
    quads.append((np.stack([index[ii - 1, jj - 1, kk], index[ii, jj - 1, kk], index[ii, jj, kk], index[ii - 1, jj, kk]], axis=1), flip))

    faces = []

    for quad, flipped in quads:
        quad = np.where(flipped[:, None], quad[:, ::-1], quad)
        faces.append(quad)

    faces = np.concatenate(faces, axis=0)
    faces = faces[np.all(faces >= 0, axis=1)]

    return verts, faces


def laplacian_smooth(verts, faces, iterations, factor=0.5):
    """Move every vertex towards the average of its neighbours (along the quad edges)."""
    edges = np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 3]], faces[:, [3, 0]]], axis=0)
    count = np.bincount(edges[:, 0], minlength=len(verts)) + np.bincount(edges[:, 1], minlength=len(verts))
    count = np.maximum(count, 1)[:, None]

    for iteration in range(iterations):
        total = np.zeros_like(verts)
        np.add.at(total, edges[:, 0], verts[edges[:, 1]])
        np.add.at(total, edges[:, 1], verts[edges[:, 0]])
        verts = verts + factor * (total / count - verts)

    return verts


# ------------------------------------------------------------------------------------------ explicit part meshes

def sphere_mesh(center, radii, frame, segments, rings):
    """A UV sphere whose pole (+Z of frame) points along the frame's third column. Returns verts, faces, params."""
    verts = []
    theta = []
    phi = []
    verts.append((0.0, 0.0, -1.0))
    theta.append(math.pi)
    phi.append(0.0)

    for ring in range(1, rings):
        t = math.pi - math.pi * ring / rings
        for i in range(segments):
            a = 2.0 * math.pi * i / segments
            verts.append((math.sin(t) * math.cos(a), math.sin(t) * math.sin(a), math.cos(t)))
            theta.append(t)
            phi.append(a)

    verts.append((0.0, 0.0, 1.0))
    theta.append(0.0)
    phi.append(0.0)
    faces = []
    last = len(verts) - 1

    for i in range(segments):
        j = (i + 1) % segments
        faces.append([0, 1 + j, 1 + i])
        faces.append([last, last - segments + i, last - segments + j])

    for ring in range(rings - 2):
        a0 = 1 + ring * segments
        b0 = a0 + segments
        for i in range(segments):
            j = (i + 1) % segments
            faces.append([a0 + i, a0 + j, b0 + j, b0 + i])

    local = np.array(verts) * vec(radii)[None, :]
    world = vec(center)[None, :] + local @ np.asarray(frame).T

    return world, faces, {"theta": np.array(theta), "phi": np.array(phi), "unit": np.array(verts)}


def tube_mesh(points, radii, segments, cap_start=True, tip=False, up=(0.0, 0.0, 1.0)):
    """A tube along points with a radius per point. tip closes the end in a point."""
    points = np.asarray(points, dtype=np.float64)
    radii = np.asarray(radii, dtype=np.float64)
    n = len(points)
    tangents = np.zeros_like(points)
    tangents[1:-1] = points[2:] - points[:-2]
    tangents[0] = points[1] - points[0]
    tangents[-1] = points[-1] - points[-2]
    tangents = tangents / np.maximum(np.linalg.norm(tangents, axis=1), 1e-12)[:, None]

    # parallel transport of a normal along the tube
    normal = np.cross(tangents[0], vec(up))

    if np.linalg.norm(normal) < 1e-6:
        normal = np.cross(tangents[0], vec((1.0, 0.0, 0.0)))

    normal = normalize(normal)
    normals = [normal]

    for i in range(1, n):
        axis = np.cross(tangents[i - 1], tangents[i])
        s = np.linalg.norm(axis)

        if s > 1e-9:
            axis = axis / s
            angle = math.atan2(s, float(np.dot(tangents[i - 1], tangents[i])))
            m = np.array(Matrix.Rotation(angle, 3, Vector(axis)))
            normal = m @ normal

        normal = normalize(normal - tangents[i] * np.dot(normal, tangents[i]))
        normals.append(normal)

    verts = []
    t_param = []
    a_param = []
    faces = []
    length = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))])
    length = length / max(length[-1], 1e-12)
    ring_count = n - 1 if tip else n

    for i in range(ring_count):
        binormal = np.cross(tangents[i], normals[i])
        for s in range(segments):
            a = 2.0 * math.pi * s / segments
            verts.append(points[i] + radii[i] * (math.cos(a) * normals[i] + math.sin(a) * binormal))
            t_param.append(length[i])
            a_param.append(a)

    for i in range(ring_count - 1):
        for s in range(segments):
            j = (s + 1) % segments
            faces.append([i * segments + s, i * segments + j, (i + 1) * segments + j, (i + 1) * segments + s])

    if tip:
        verts.append(points[-1])
        t_param.append(1.0)
        a_param.append(0.0)
        last = len(verts) - 1
        base = (ring_count - 1) * segments
        for s in range(segments):
            faces.append([base + s, base + (s + 1) % segments, last])
    else:
        verts.append(points[-1])
        t_param.append(1.0)
        a_param.append(0.0)
        last = len(verts) - 1
        base = (ring_count - 1) * segments
        for s in range(segments):
            faces.append([base + s, base + (s + 1) % segments, last])

    if cap_start:
        verts.append(points[0])
        t_param.append(0.0)
        a_param.append(0.0)
        first = len(verts) - 1
        for s in range(segments):
            faces.append([(s + 1) % segments, s, first])

    return np.array(verts), faces, {"t": np.array(t_param), "a": np.array(a_param)}


def sheet_mesh(points, normals, params, subdivide, thickness, wrap=False):
    """A double sided sheet from a grid of points (rows, columns, 3) with a normal at each point.

    subdivide splits every grid cell into subdivide x subdivide cells by bilinear interpolation, so the dense
    sheet lies exactly on the coarse one and the bake can use a tiny ray offset. The normals are interpolated
    too, and returned as custom normals for both sides. params: per point values (rows, columns), interpolated.
    wrap joins the last column to the first (a disc). The back is thickness below the front (thinner at the tips).
    """
    rows, columns = points.shape[:2]
    cells = columns if wrap else columns - 1
    fine_rows = (rows - 1) * subdivide + 1
    fine_columns = cells * subdivide + (0 if wrap else 1)

    def sample(grid, r, c):
        r0 = min(int(r), rows - 2)
        c0 = int(c)
        fr = r - r0
        fc = c - c0
        c1 = (c0 + 1) % columns if wrap else min(c0 + 1, columns - 1)
        c0 = c0 % columns
        top = grid[r0, c0] * (1.0 - fc) + grid[r0, c1] * fc
        bottom = grid[r0 + 1, c0] * (1.0 - fc) + grid[r0 + 1, c1] * fc

        return top * (1.0 - fr) + bottom * fr

    fine_points = np.zeros((fine_rows, fine_columns, 3))
    fine_normals = np.zeros((fine_rows, fine_columns, 3))
    fine_params = {key: np.zeros((fine_rows, fine_columns)) for key in params}

    for r in range(fine_rows):
        for c in range(fine_columns):
            gr = r / subdivide
            gc = c / subdivide
            fine_points[r, c] = sample(points, gr, gc)
            fine_normals[r, c] = normalize(sample(normals, gr, gc))
            for key in params:
                fine_params[key][r, c] = sample(params[key], gr, gc)

    verts = []
    vert_normals = []
    out = {key: [] for key in params}
    out["side"] = []
    faces = []
    u = fine_params.get("u", np.zeros((fine_rows, fine_columns)))

    for side_sign in (1.0, -1.0):
        start = len(verts)
        for r in range(fine_rows):
            for c in range(fine_columns):
                offset = side_sign * thickness * 0.5 * (1.0 - 0.6 * u[r, c])
                verts.append(fine_points[r, c] + fine_normals[r, c] * offset)
                vert_normals.append(fine_normals[r, c] * side_sign)
                for key in params:
                    out[key].append(fine_params[key][r, c])
                out["side"].append(side_sign)

        for r in range(fine_rows - 1):
            for c in range(fine_columns if wrap else fine_columns - 1):
                c1 = (c + 1) % fine_columns
                a = start + r * fine_columns + c
                b = start + r * fine_columns + c1
                e = a + fine_columns
                f = b + fine_columns
                faces.append([a, b, f, e] if side_sign > 0.0 else [a, e, f, b])

    result = {key: np.array(values) for key, values in out.items()}
    result["normals"] = np.array(vert_normals)

    return np.array(verts), faces, result


def leaf_mesh(base, direction, length, width, along, across, outline, side=None, fold=0.25, droop=0.3, curl=0.0, twist=0.0, thickness=0.004, subdivide=1):
    """A double sided leaf from base: along length segments, across segments (even), outline(u) the half width (0 to 1).

    direction: where the leaf points; side: across the leaf (by default level, so the upper face looks up).
    fold lifts the edges (a V along the midrib), droop bends the leaf down along its length, curl rolls its tip,
    twist turns it about the midrib (degrees). subdivide makes the dense copy for the bake (see sheet_mesh).
    """
    d = normalize(direction)
    side = leaf_side(d) if side is None else vec(side)
    s = normalize(side - d * np.dot(side, d))
    up = np.cross(s, d)
    rows = along + 1
    columns = across + 1

    def surface_point(u, v):
        w = outline(u) * width
        x = v * w
        bend = droop * u * u + curl * max(0.0, u - 0.6) ** 2 * 4.0
        angle = math.radians(twist) * u
        lift = fold * abs(v) * w
        local_side = math.cos(angle) * x - math.sin(angle) * lift
        local_up = math.sin(angle) * x + math.cos(angle) * lift
        along_length = u * length

        return vec(base) + d * along_length * math.cos(bend) + up * (local_up - along_length * math.sin(bend)) + s * local_side

    grid = np.zeros((rows, columns, 3))
    grid_u = np.zeros((rows, columns))
    grid_v = np.zeros((rows, columns))

    for r in range(rows):
        for c in range(columns):
            grid_u[r, c] = r / along
            grid_v[r, c] = -1.0 + 2.0 * c / across
            grid[r, c] = surface_point(grid_u[r, c], grid_v[r, c])

    # smooth normals of the coarse grid (from finely spaced samples of the true surface)
    normals = np.zeros((rows, columns, 3))
    eps = 1e-3

    for r in range(rows):
        for c in range(columns):
            u0, v0 = grid_u[r, c], grid_v[r, c]
            du = surface_point(min(u0 + eps, 1.0), v0) - surface_point(max(u0 - eps, 0.0), v0)
            dv = surface_point(u0, min(v0 + eps, 1.0)) - surface_point(u0, max(v0 - eps, -1.0))
            n = np.cross(dv, du)
            normals[r, c] = normalize(n) if np.linalg.norm(n) > 1e-12 else up

    return sheet_mesh(grid, normals, {"u": grid_u, "v": grid_v}, subdivide, thickness)


def disc_mesh(center, normal, radius, rings, segments, cup=0.2, wave=0.0, lobes=7, thickness=0.004, subdivide=1):
    """A round double sided leaf (a parasol) around center, facing normal. cup lifts the rim (a fraction of radius),
    wave scallops the rim with lobes. Params: u is the distance from the middle (0 to 1), v is -1 to 1 between veins.
    """
    frame = frame_from_direction(normal)
    rows = rings + 1
    grid = np.zeros((rows, segments, 3))
    normals = np.zeros((rows, segments, 3))
    grid_u = np.zeros((rows, segments))
    grid_cos = np.zeros((rows, segments))
    grid_sin = np.zeros((rows, segments))

    def point(u, angle):
        r = radius * u * (1.0 + wave * 0.12 * math.cos(lobes * angle) * u)
        height = radius * (cup * u * u + wave * 0.06 * math.sin(lobes * angle) * u ** 3)

        return vec(center) + frame @ np.array([r * math.cos(angle), r * math.sin(angle), height])

    for ring in range(rows):
        u = 0.08 + 0.92 * ring / rings
        # the columns go clockwise, so that the faces of the upper side look along normal
        for i in range(segments):
            angle = -2.0 * math.pi * i / segments
            grid[ring, i] = point(u, angle)
            grid_u[ring, i] = u
            grid_cos[ring, i] = math.cos(angle)
            grid_sin[ring, i] = math.sin(angle)
            du = point(u + 1e-3, angle) - point(u - 1e-3, angle)
            dcolumn = point(u, angle - 1e-3) - point(u, angle + 1e-3)
            normals[ring, i] = normalize(np.cross(dcolumn, du))

    verts, faces, params = sheet_mesh(grid, normals, {"u": grid_u, "cos": grid_cos, "sin": grid_sin}, subdivide, thickness, wrap=True)
    params["v"] = np.sin(np.arctan2(params["sin"], params["cos"]) * lobes / 2.0)

    return verts, faces, params


def leaf_side(direction):
    """The side vector for a leaf pointing along direction, so that its upper face looks up."""
    d = normalize(direction)
    side = np.cross(d, vec((0.0, 0.0, 1.0)))

    if np.linalg.norm(side) < 1e-6:
        side = vec((1.0, 0.0, 0.0))

    return normalize(side)


class Part:
    """An explicit part: build(detail) returns verts, faces, params for detail 'high' or 'low'; paint(params, verts) the colours."""

    def __init__(self, name, build, paint, material, thin=False):
        self.name = name
        self.build = build
        self.paint = paint
        self.material = material
        self.thin = thin


# ------------------------------------------------------------------------------------------ the lifeform

class Lifeform:
    """One body plan. Build in a frame with the head along +Y, up along +Z, standing on z = 0."""

    def __init__(self, name, plan, kind="animal", symmetric=True):
        self.name = name
        self.plan = plan
        self.kind = kind
        self.symmetric = symmetric
        self.field = Field()
        self.parts = []
        self.step = 0.004
        self.body_triangles = 2400
        self.body_material = SKIN
        self.displace = None
        self.paint = None
        self.texture_size = 1024
        self.notes = ""

    # body primitives ------------------------------------------------------------------------------

    def sphere(self, center, radius, k=0.02, tag="body", mirror=False):
        self.field.add(Sphere(center, radius), k, tag)
        if mirror:
            self.field.add(Sphere(mirror_x(center), radius), k, tag)

    def ellipsoid(self, center, radii, frame=None, k=0.02, tag="body", mirror=False):
        self.field.add(Ellipsoid(center, radii, frame), k, tag)
        if mirror:
            m = np.diag([-1.0, 1.0, 1.0])
            mirrored = None if frame is None else m @ np.asarray(frame) @ m
            self.field.add(Ellipsoid(mirror_x(center), radii, mirrored), k, tag)

    def cone(self, a, b, ra, rb, k=0.02, tag="body", mirror=False):
        self.field.add(RoundCone(a, b, ra, rb), k, tag)
        if mirror:
            self.field.add(RoundCone(mirror_x(a), mirror_x(b), ra, rb), k, tag)

    def chain(self, points, radii, k=0.01, tag="body", join_k=None, mirror=False):
        """Round cones along a polyline; the first one joins the body with join_k (default k)."""
        for i in range(len(points) - 1):
            kk = (join_k if join_k is not None else k) if i == 0 else k
            self.cone(points[i], points[i + 1], radii[i], radii[i + 1], kk, tag, mirror)

    def cut_sphere(self, center, radius, k=0.01, mirror=False):
        self.field.cut(Sphere(center, radius), k)
        if mirror:
            self.field.cut(Sphere(mirror_x(center), radius), k)

    def cut_ellipsoid(self, center, radii, frame=None, k=0.01, mirror=False):
        self.field.cut(Ellipsoid(center, radii, frame), k)
        if mirror:
            m = np.diag([-1.0, 1.0, 1.0])
            mirrored = None if frame is None else m @ np.asarray(frame) @ m
            self.field.cut(Ellipsoid(mirror_x(center), radii, mirrored), k)

    def flat_base(self, height=0.0, k=0.01):
        """Cut everything below height, with a rounded edge: a sole the lifeform rests on."""
        self.field.intersect(Ground(height), k)

    # explicit parts -------------------------------------------------------------------------------

    def add_part(self, name, build, paint, material=DETAIL, mirror=False, thin=False):
        self.parts.append(Part(name, build, paint, material, thin))

        if mirror:
            def mirrored(detail, build=build):
                verts, faces, params = build(detail)
                verts = verts.copy()
                verts[:, 0] = -verts[:, 0]
                faces = [list(reversed(f)) for f in faces]

                return verts, faces, params

            self.parts.append(Part(name + " R", mirrored, paint, material, thin))

    def eye(self, center, radius, direction, iris=(0.9, 0.6, 0.1), pupil="round", sclera=(0.02, 0.02, 0.02), mirror=False, iris_size=0.55, glow=0.0):
        """A glossy eye ball; the pupil faces direction. pupil: round, slit, bar or none."""
        frame = frame_from_direction(direction)
        iris_colour = vec(iris)
        sclera_colour = vec(sclera)

        def build(detail):
            segments, rings = (36, 18) if detail == "high" else ((12, 8) if radius > 0.024 else (10, 6))

            return sphere_mesh(center, (radius, radius, radius), frame, segments, rings)

        def paint(params, verts):
            theta = params["theta"]
            unit = params["unit"]
            iris_angle = iris_size
            pupil_angle = iris_angle * 0.45

            if pupil == "slit":
                pupil_mask = smoothstep(pupil_angle * 1.05, pupil_angle * 0.9, np.where(unit[:, 2] > 0, np.abs(unit[:, 0]) * 3.0 + theta * 0.25, 9.0))
            elif pupil == "bar":
                pupil_mask = smoothstep(0.16, 0.12, np.where(unit[:, 2] > 0, np.abs(unit[:, 1]), 9.0)) * smoothstep(iris_angle * 0.95, iris_angle * 0.8, theta)
            elif pupil == "none":
                pupil_mask = np.zeros(len(theta))
            else:
                pupil_mask = smoothstep(pupil_angle * 1.1, pupil_angle * 0.9, theta)

            iris_mask = smoothstep(iris_angle * 1.08, iris_angle * 0.92, theta)
            streak = 0.65 + 0.35 * value_noise(np.stack([params["phi"] * 3.0, theta * 6.0, np.zeros(len(theta))], axis=1), 2.0, 5)
            ring = 1.0 - 0.6 * smoothstep(iris_angle * 0.6, iris_angle * 0.98, theta)
            colour = sclera_colour[None, :] * (1.0 - iris_mask[:, None]) + iris_colour[None, :] * (iris_mask * streak * ring)[:, None]
            colour = colour * (1.0 - pupil_mask[:, None]) + 0.005 * pupil_mask[:, None]
            smooth = np.full(len(theta), 0.93)
            spec = np.full(len(theta), 0.42)
            emit = (iris_mask * (1.0 - pupil_mask) * glow)

            return colour, smooth, spec, emit

        self.add_part("Eye", build, paint, DETAIL, mirror)

    def claw(self, base, direction, length, radius, bend=0.3, colour_base=(0.10, 0.08, 0.07), colour_tip=(0.55, 0.50, 0.42), mirror=False, curl_axis=None, material=DETAIL):
        """A curved claw (or horn, spine, tooth) from base: a tapering tube bent towards curl_axis."""
        d = normalize(direction)
        curl = normalize(curl_axis) if curl_axis is not None else normalize(np.cross(np.cross(d, vec((0, 0, -1.0))), d)) if abs(d[2]) < 0.99 else vec((0, 1.0, 0))
        points = []
        radii = []
        count = 9

        for i in range(count):
            t = i / (count - 1)
            angle = bend * t * t
            points.append(vec(base) + length * (d * t + curl * angle * t * 0.8))
            radii.append(radius * (1.0 - t) ** 0.85 + radius * 0.04)

        c0 = vec(colour_base)
        c1 = vec(colour_tip)

        def build(detail):
            segments = 14 if detail == "high" else 5
            if detail == "high":
                return tube_mesh(points, radii, segments, cap_start=True, tip=True)

            idx = [0, 3, 6, 8]

            return tube_mesh([points[i] for i in idx], [radii[i] for i in idx], segments, cap_start=True, tip=True)

        def paint(params, verts):
            t = params["t"]
            ridge = 0.85 + 0.15 * value_noise(np.stack([params["a"] * 2.0, t * 30.0, np.zeros(len(t))], axis=1), 1.0, 11)
            colour = (c0[None, :] + (c1 - c0)[None, :] * smoothstep(0.1, 1.0, t)[:, None]) * ridge[:, None]

            return colour, np.full(len(t), 0.62), np.full(len(t), 0.3), np.zeros(len(t))

        self.add_part("Claw", build, paint, material, mirror)

    def tube(self, name, points, radii, paint, material=SKIN, mirror=False, segments_high=16, segments_low=6, low_stride=3, tip=True):
        """A free tube (antenna, stalk, stem): high and low resolution, low keeps every low_stride point."""
        points = [vec(p) for p in points]

        def build(detail):
            if detail == "high":
                return tube_mesh(points, radii, segments_high, cap_start=True, tip=tip)

            idx = list(range(0, len(points), low_stride))

            if idx[-1] != len(points) - 1:
                idx.append(len(points) - 1)

            return tube_mesh([points[i] for i in idx], [radii[i] for i in idx], segments_low, cap_start=True, tip=tip)

        self.add_part(name, build, paint, material, mirror)

    def disc(self, name, paint, material=SKIN, low=(2, 9), subdivide=4, **disc_args):
        """A round leaf; the high copy is the low one subdivided (thin parts bake with a tiny ray offset)."""
        def build(detail):
            rings, segments = low

            return disc_mesh(rings=rings, segments=segments, subdivide=subdivide if detail == "high" else 1, **disc_args)

        self.add_part(name, build, paint, material, thin=True)

    def leaf(self, name, paint, material=SKIN, low=(4, 2), subdivide=4, **leaf_args):
        """A leaf; the high copy is the low one subdivided (thin parts bake with a tiny ray offset)."""
        def build(detail):
            along, across = low

            return leaf_mesh(along=along, across=across, subdivide=subdivide if detail == "high" else 1, **leaf_args)

        self.add_part(name, build, paint, material, thin=True)


# ------------------------------------------------------------------------------------------ mesh objects

def make_object(name, verts, faces, collection=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in np.asarray(verts, dtype=np.float64)], [], [list(map(int, f)) for f in faces])
    mesh.validate(clean_customdata=False)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    (collection or bpy.context.scene.collection).objects.link(obj)

    for polygon in mesh.polygons:
        polygon.use_smooth = True

    return obj


def set_point_colours(obj, name, rgba):
    mesh = obj.data
    attribute = mesh.color_attributes.get(name) or mesh.color_attributes.new(name, "FLOAT_COLOR", "POINT")
    attribute.data.foreach_set("color", np.asarray(rgba, dtype=np.float32).ravel())


def vertex_array(obj):
    verts = np.zeros(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", verts)

    return verts.reshape(-1, 3)


def set_vertex_array(obj, verts):
    obj.data.vertices.foreach_set("co", np.asarray(verts, dtype=np.float64).ravel())
    obj.data.update()


def vertex_normals(obj):
    normals = np.zeros(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("normal", normals)

    return normals.reshape(-1, 3)


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def activate(objects, active):
    bpy.ops.object.select_all(action="DESELECT")

    for obj in objects:
        obj.select_set(True)

    bpy.context.view_layer.objects.active = active


# ------------------------------------------------------------------------------------------ building

def build_body(lifeform):
    """The body's dense mesh (displaced and painted) and its smooth copy for the low mesh."""
    field = lifeform.field
    step = lifeform.step
    low, high = field.bounds()
    low = low - 4.0 * step
    high = high + 4.0 * step
    shape = tuple(int(math.ceil((high[i] - low[i]) / step)) + 1 for i in range(3))
    started = time.time()
    grid = field.evaluate_grid(low, step, shape)
    verts, faces = surface_nets(grid, low, step)
    log("%s body grid %s, %d verts, %d quads (%.1fs)" % (lifeform.name, shape, len(verts), len(faces), time.time() - started))

    # relax the vertices, then put them back on the surface (Newton steps along the gradient)
    for iteration in range(3):
        verts = laplacian_smooth(verts, faces, 2, 0.5)
        d, _ = field.evaluate(verts, step)
        g = field.gradient(verts, step * 0.5, step)
        verts = verts - (d / np.maximum(np.einsum("ij,ij->i", g, g), 1e-9))[:, None] * g

    d, tags = field.evaluate(verts, step)
    g = field.gradient(verts, step * 0.5, step)
    normals = g / np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
    tag_names = np.array(field.tags)[tags]

    smooth_obj = make_object(lifeform.name + " Body Smooth", verts, faces)

    displacement = np.zeros(len(verts))

    if lifeform.displace is not None:
        displacement = lifeform.displace(verts, normals, tag_names)

    high_verts = verts + normals * displacement[:, None]
    high_obj = make_object(lifeform.name + " Body High", high_verts, faces)

    colour, smooth, spec, emit = lifeform.paint(verts, normals, tag_names, displacement)
    set_point_colours(high_obj, "Col", np.concatenate([colour, np.ones((len(verts), 1))], axis=1))
    set_point_colours(high_obj, "Surf", np.stack([smooth, spec, emit, np.ones(len(verts))], axis=1))
    log("%s body done (%.1fs)" % (lifeform.name, time.time() - started))

    return high_obj, smooth_obj


def decimate(obj, triangles, symmetric):
    current = triangle_count(obj)
    modifier = obj.modifiers.new("Decimate", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = min(1.0, triangles / max(current, 1))
    modifier.use_collapse_triangulate = True

    if symmetric:
        modifier.use_symmetry = True
        modifier.symmetry_axis = "X"

    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    obj.modifiers.remove(modifier)
    old = obj.data
    obj.data = mesh
    bpy.data.meshes.remove(old)

    for polygon in mesh.polygons:
        polygon.use_smooth = True


def build_parts(lifeform, detail):
    objects = []

    for index, part in enumerate(lifeform.parts):
        verts, faces, params = part.build(detail)
        obj = make_object("%s %s %d %s" % (lifeform.name, part.name, index, detail), verts, faces)
        obj["material"] = part.material
        obj["group"] = 2 if part.thin else 1

        if "normals" in params:
            obj.data.normals_split_custom_set_from_vertices([tuple(n) for n in params["normals"]])

        if detail == "high":
            colour, smooth, spec, emit = part.paint(params, np.asarray(verts))
            set_point_colours(obj, "Col", np.concatenate([colour, np.ones((len(verts), 1))], axis=1))
            set_point_colours(obj, "Surf", np.stack([smooth, spec, emit, np.ones(len(verts))], axis=1))

        objects.append(obj)

    return objects


def join(objects, name):
    activate(objects, objects[0])
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.data.name = name

    return obj


def triangulate(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def normalize_objects(lifeform, objects, reference):
    """Scale so the reference's largest side is 1 unit, standing on the origin (plants keep their base at the origin)."""
    verts = vertex_array(reference)
    low = verts.min(axis=0)
    high = verts.max(axis=0)
    scale = 1.0 / float(np.max(high - low))
    offset = np.array([(low[0] + high[0]) / 2.0, (low[1] + high[1]) / 2.0, low[2]])

    if lifeform.kind == "plant":
        offset = np.array([0.0, 0.0, low[2]])

    for obj in objects:
        set_vertex_array(obj, (vertex_array(obj) - offset) * scale)

    return scale


def unwrap(obj):
    activate([obj], obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(62.0), island_margin=0.004, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)

    try:
        bpy.ops.uv.pack_islands(shape_method="CONCAVE", rotate=True, rotate_method="ANY", margin=0.005)
    except Exception as error:  # noqa: BLE001
        log("pack_islands failed: %s" % error)

    bpy.ops.object.mode_set(mode="OBJECT")
    mesh = obj.data

    # the second UV set (the SF shaders read occlusion from it) is a copy of the first
    if len(mesh.uv_layers) < 2:
        second = mesh.uv_layers.new(name="UV2")
        first = mesh.uv_layers[0]
        uvs = np.zeros(len(mesh.loops) * 2)
        first.data.foreach_get("uv", uvs)
        second.data.foreach_set("uv", uvs)

    mesh.uv_layers.active_index = 0


# ------------------------------------------------------------------------------------------ baking

def use_gpu(scene):
    scene.render.engine = "CYCLES"

    try:
        preferences = bpy.context.preferences.addons["cycles"].preferences
        preferences.compute_device_type = "HIP"
        preferences.refresh_devices()
        found = False

        for device in preferences.devices:
            device.use = device.type == "HIP" and "780M" not in device.name
            found = found or device.use

        scene.cycles.device = "GPU" if found else "CPU"
    except Exception as error:  # noqa: BLE001
        log("GPU not available (%s), using the CPU" % error)
        scene.cycles.device = "CPU"


def bake_material():
    material = bpy.data.materials.get("LF Bake")

    if material is not None:
        return material

    material = bpy.data.materials.new("LF Bake")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.name = "Emission"
    col = nodes.new("ShaderNodeAttribute")
    col.attribute_name = "Col"
    col.name = "Col"
    surf = nodes.new("ShaderNodeAttribute")
    surf.attribute_name = "Surf"
    surf.name = "Surf"
    separate = nodes.new("ShaderNodeSeparateColor")
    separate.name = "Separate"
    material.node_tree.links.new(surf.outputs["Color"], separate.inputs["Color"])
    material.node_tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])

    return material


def set_bake_channel(channel):
    material = bake_material()
    tree = material.node_tree
    emission = tree.nodes["Emission"]

    for link in list(emission.inputs["Color"].links):
        tree.links.remove(link)

    if channel == "albedo":
        tree.links.new(tree.nodes["Col"].outputs["Color"], emission.inputs["Color"])
    elif channel == "mask":
        emission.inputs["Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    else:
        output = {"smoothness": "Red", "specular": "Green", "emission": "Blue"}[channel]
        tree.links.new(tree.nodes["Separate"].outputs[output], emission.inputs["Color"])


def new_image(name, size, colour_space):
    image = bpy.data.images.get(name)

    if image is not None:
        bpy.data.images.remove(image)

    image = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    image.colorspace_settings.name = colour_space

    return image


def target_material(name):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    target = nodes.new("ShaderNodeTexImage")
    target.name = "Bake Target"

    return material


def set_target(materials, image):
    for material in materials:
        node = material.node_tree.nodes["Bake Target"]
        node.image = image
        material.node_tree.nodes.active = node


def image_pixels(image):
    pixels = np.zeros(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)

    return pixels.reshape(image.size[1], image.size[0], 4)


def save_image(image, path):
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()


def split_by_group(obj, group):
    """A copy of obj with only the faces of one bake group (0 the body, 1 the explicit parts)."""
    copy = obj.copy()
    copy.data = obj.data.copy()
    copy.name = "%s group %d" % (obj.name, group)
    bpy.context.scene.collection.objects.link(copy)
    bm = bmesh.new()
    bm.from_mesh(copy.data)
    layer = bm.faces.layers.int.get("lf_group")
    doomed = [f for f in bm.faces if f[layer] != group]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bm.to_mesh(copy.data)
    bm.free()

    return copy if len(copy.data.polygons) else None


def bake_lifeform(lifeform, low_obj, high_body, high_groups, folder):
    """Bake the high meshes onto the low mesh and save the maps next to the model. Returns their paths.

    The body and the parts are baked apart (each from its own high meshes) and put together with a mask
    of the parts, so that a claw never picks up the skin it grows out of, nor the skin the claw.
    """
    scene = bpy.context.scene
    use_gpu(scene)
    scene.cycles.samples = 16
    scene.render.bake.margin_type = "EXTEND"
    size = lifeform.texture_size
    material = bake_material()
    high_objects = [high_body] + list(high_groups.values())

    for obj in high_objects:
        obj.data.materials.clear()
        obj.data.materials.append(material)

    materials = [m for m in low_obj.data.materials]
    base = os.path.join(folder, lifeform.name)
    paths = {}
    started = time.time()
    # (low faces, high objects, ray offset outside the low surface, ray length): the body and the thick parts are
    # decimated or coarser than their high copies; thin parts (leaves) lie exactly on theirs, close to each other
    groups = [(split_by_group(low_obj, 0), [high_body], 0.012, 0.05)]

    for group, extrusion, ray in ((1, 0.012, 0.05), (2, 0.0015, 0.004)):
        if group in high_groups:
            groups.append((split_by_group(low_obj, group), [high_groups[group]], extrusion, ray))

    groups = [g for g in groups if g[0] is not None]

    def bake(kind, colour_space, margin=8):
        """One pass per group; returns the pixels of each."""
        results = []

        for low, high, extrusion, ray in groups:
            image = new_image("bake pass", size, colour_space)
            set_target(materials, image)
            activate(high + [low], low)
            bpy.ops.object.bake(type=kind, use_selected_to_active=True, cage_extrusion=extrusion, max_ray_distance=ray, margin=margin, use_clear=True, normal_space="TANGENT")
            results.append(image_pixels(image).copy())
            bpy.data.images.remove(image)

        return results

    # where each later group's faces are (a little wider than their islands, so their own margin wins at the edges)
    masks = []
    set_bake_channel("mask")

    for low, high, extrusion, ray in groups[1:]:
        image = new_image("bake mask", size, "Non-Color")
        set_target(materials, image)
        activate(high + [low], low)
        bpy.ops.object.bake(type="EMIT", use_selected_to_active=True, cage_extrusion=extrusion, max_ray_distance=1.0, margin=2, use_clear=True)
        masks.append(image_pixels(image)[:, :, 0:1] > 0.5)
        bpy.data.images.remove(image)

    def combine(results):
        combined = results[0]

        for mask, result in zip(masks, results[1:]):
            combined = np.where(mask, result, combined)

        return combined

    def save(pixels, name, colour_space, alpha=False):
        image = bpy.data.images.new(lifeform.name + " - " + name, pixels.shape[1], pixels.shape[0], alpha=alpha, float_buffer=False)
        image.colorspace_settings.name = colour_space
        image.pixels.foreach_set(np.ascontiguousarray(pixels, dtype=np.float32).ravel())
        path = base + " - " + name + ".png"
        save_image(image, path)

        return path

    set_bake_channel("albedo")
    albedo = combine(bake("EMIT", "sRGB"))
    albedo[:, :, 3] = 1.0
    paths["albedo"] = save(albedo, "Albedo", "sRGB")

    set_bake_channel("smoothness")
    smooth = combine(bake("EMIT", "Non-Color"))
    set_bake_channel("specular")
    spec = combine(bake("EMIT", "Non-Color"))
    set_bake_channel("emission")
    glow = combine(bake("EMIT", "Non-Color"))[:, :, 0]

    combined = np.zeros((size, size, 4), dtype=np.float32)
    combined[:, :, 0:3] = spec[:, :, 0:1]
    combined[:, :, 3] = smooth[:, :, 0]
    paths["specular"] = save(combined, "Specular", "Non-Color", alpha=True)

    if float(glow.max()) > 0.01:
        emissive = np.ones((size, size, 4), dtype=np.float32)
        emissive[:, :, 0:3] = albedo[:, :, 0:3] * glow[:, :, None]
        paths["emissive"] = save(emissive, "Emissive", "sRGB")

    normal = combine(bake("NORMAL", "Non-Color"))
    normal[:, :, 3] = 1.0
    paths["normal"] = save(normal, "Normal", "Non-Color")

    for group in groups:
        bpy.data.objects.remove(group[0])

    # occlusion of the low mesh alone, standing on a ground plane (the fine detail is in the normal map)
    for obj in high_objects:
        obj.hide_render = True

    # thin parts are flat shaded for this bake: a hemisphere around a smooth normal dips into a folded leaf
    shaded = low_obj.copy()
    shaded.data = low_obj.data.copy()
    bpy.context.scene.collection.objects.link(shaded)
    low_obj.hide_render = True

    if shaded.data.has_custom_normals:
        shaded.data.normals_split_custom_set([(0.0, 0.0, 0.0)] * len(shaded.data.loops))

    groups_of_faces = np.zeros(len(shaded.data.polygons), dtype=np.int32)
    shaded.data.attributes["lf_group"].data.foreach_get("value", groups_of_faces)
    shaded.data.polygons.foreach_set("use_smooth", (groups_of_faces != 2).tolist())
    shaded.data.update()

    bpy.ops.mesh.primitive_plane_add(size=6.0, location=(0.0, 0.0, -0.001))
    ground = bpy.context.view_layer.objects.active
    scene.cycles.samples = 256
    occlusion = new_image(lifeform.name + " - Occlusion", size, "Non-Color")
    set_target(materials, occlusion)
    activate([shaded], shaded)
    scene.world = scene.world or bpy.data.worlds.new("World")
    scene.world.light_settings.distance = 0.25
    bpy.ops.object.bake(type="AO", use_selected_to_active=False, margin=8, use_clear=True)
    # occlusion changes slowly: half the size, averaged over 2 x 2 texels (which also smooths the sampling noise)
    pixels = image_pixels(occlusion)
    half = (pixels[0::2, 0::2] + pixels[1::2, 0::2] + pixels[0::2, 1::2] + pixels[1::2, 1::2]) / 4.0
    half[:, :, 3] = 1.0
    paths["occlusion"] = save(half, "Occlusion", "Non-Color")
    bpy.data.objects.remove(ground)
    bpy.data.objects.remove(shaded)
    low_obj.hide_render = False

    for obj in high_objects:
        obj.hide_render = False

    log("%s baked in %.1fs" % (lifeform.name, time.time() - started))

    return paths


def final_materials(lifeform, low_obj, paths):
    """Point the model's materials at the baked maps (what the FBX references; Unity remakes them)."""
    images = {}

    for key, path in paths.items():
        image = bpy.data.images.load(path, check_existing=True)
        image.colorspace_settings.name = "sRGB" if key in ("albedo", "emissive") else "Non-Color"
        images[key] = image

    for material in low_obj.data.materials:
        tree = material.node_tree
        tree.nodes.clear()
        output = tree.nodes.new("ShaderNodeOutputMaterial")
        bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
        tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
        albedo = tree.nodes.new("ShaderNodeTexImage")
        albedo.image = images["albedo"]
        tree.links.new(albedo.outputs["Color"], bsdf.inputs["Base Color"])
        normal_image = tree.nodes.new("ShaderNodeTexImage")
        normal_image.image = images["normal"]
        normal_map = tree.nodes.new("ShaderNodeNormalMap")
        tree.links.new(normal_image.outputs["Color"], normal_map.inputs["Color"])
        tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
        specular = tree.nodes.new("ShaderNodeTexImage")
        specular.image = images["specular"]
        invert = tree.nodes.new("ShaderNodeMath")
        invert.operation = "SUBTRACT"
        invert.inputs[0].default_value = 1.0
        tree.links.new(specular.outputs["Alpha"], invert.inputs[1])
        tree.links.new(invert.outputs["Value"], bsdf.inputs["Roughness"])


# ------------------------------------------------------------------------------------------ export

def export_fbx(obj, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    activate([obj], obj)

    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        use_tspace=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="RELATIVE",
    )


def to_unity_frame(obj):
    """Turn the head from +Y (the build frame) to -Y, which the FBX export takes to Unity's +Z (as the ship models)."""
    obj.data.transform(Matrix.Rotation(math.radians(180.0), 4, "Z"))
    obj.data.update()


def script_arguments():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def build_one(lifeform, folder, export=True):
    """Make, bake and export one lifeform. Returns its manifest entry."""
    reset_scene()
    started = time.time()
    high_body, smooth_body = build_body(lifeform)
    high_parts = build_parts(lifeform, "high")

    # one object per bake group for the high parts: a bake from dozens of objects is several times slower
    high_groups = {}

    members_of = {group: [o for o in high_parts if o["group"] == group] for group in (1, 2)}

    for group, members in members_of.items():
        if members:
            high_groups[group] = join(members, "%s Parts High %d" % (lifeform.name, group)) if len(members) > 1 else members[0]

    high_parts = list(high_groups.values())

    low_parts = build_parts(lifeform, "low")
    decimate(smooth_body, lifeform.body_triangles, lifeform.symmetric)
    smooth_body["material"] = lifeform.body_material

    # the low mesh: the decimated body and the low parts, with the tinted skin first
    skin = target_material("%s %s" % (lifeform.name, SKIN))
    detail = target_material("%s %s" % (lifeform.name, DETAIL))
    low_objects = [smooth_body] + low_parts

    for obj in low_objects:
        obj.data.materials.clear()
        obj.data.materials.append(skin)
        obj.data.materials.append(detail)
        index = 0 if obj["material"] == SKIN else 1

        for polygon in obj.data.polygons:
            polygon.material_index = index

        # the bake group: 0 for the body, 1 for the explicit parts
        group = obj.data.attributes.new("lf_group", "INT", "FACE")
        group.data.foreach_set("value", [0 if obj is smooth_body else int(obj["group"])] * len(obj.data.polygons))

    low = join(low_objects, lifeform.name)
    triangulate(low)
    high_objects = [high_body] + high_parts
    normalize_objects(lifeform, high_objects + [low], low)

    # a material slot no face uses would be an empty submesh in Unity
    used = set(p.material_index for p in low.data.polygons)

    if 1 not in used:
        low.data.materials.pop(index=1)

    unwrap(low)
    os.makedirs(folder, exist_ok=True)
    paths = bake_lifeform(lifeform, low, high_body, high_groups, folder)
    final_materials(lifeform, low, paths)
    low.data.attributes.remove(low.data.attributes["lf_group"])

    verts = vertex_array(low)
    size = verts.max(axis=0) - verts.min(axis=0)
    entry = {
        "name": lifeform.name,
        "plan": lifeform.plan,
        "kind": lifeform.kind,
        "model": "Assets/Game Objects/Lifeforms/%s.fbx" % lifeform.name,
        "triangles": triangle_count(low),
        "size": {"width": round(float(size[0]), 3), "height": round(float(size[2]), 3), "length": round(float(size[1]), 3)},
        "materials": [m.name for m in low.data.materials],
        "textures": {key: os.path.basename(path) for key, path in sorted(paths.items())},
    }

    if export:
        to_unity_frame(low)
        export_fbx(low, os.path.join(folder, lifeform.name + ".fbx"))

    log("%s done: %d triangles, size %s (%.1fs)" % (lifeform.name, entry["triangles"], entry["size"], time.time() - started))

    return entry


def write_manifest(entries, path):
    existing = {}

    if os.path.exists(path):
        with open(path, "r", encoding="utf-8") as manifest_file:
            for entry in json.load(manifest_file).get("lifeforms", []):
                existing[entry["name"]] = entry

    for entry in entries:
        existing[entry["name"]] = entry

    order = {name: i for i, name in enumerate(BODY_PLANS)}
    ordered = sorted(existing.values(), key=lambda e: (order.get(e["plan"], 99), e["name"]))

    with open(path, "w", encoding="utf-8", newline="\n") as manifest_file:
        json.dump({"forward": "Unity +Z", "up": "Unity +Y", "lifeforms": ordered}, manifest_file, indent="\t")
        manifest_file.write("\n")
