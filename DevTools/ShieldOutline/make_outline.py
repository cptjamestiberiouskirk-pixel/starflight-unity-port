"""Draws the player ship's shield outline (Assets/Game Objects/UI/Shields/Shields Outline.png) around the ship's silhouette.

The silhouettes come from the probe: run the scenarios starport-shieldoutline and shieldoutline with -Graphics and PROBE_SHIELD_DUMP set to
a folder, and each writes the ship's silhouette there in the outline sprite's own pixels (starport.png, status.png). This script takes their
union, puts a box around the rear (from the engines to the weapon mounts, as the original outline did) and the convex hull of that and the
rest of the ship, grows it by a margin, and draws its outline as a 9 pixel line in the outline's red, anti-aliased. Run the two scenarios again
afterwards: they check that every pixel of the ship is inside the new outline, clear of its line.

    python make_outline.py <dump folder> <output png>

Needs Pillow only.
"""
import os
import sys
from PIL import Image, ImageChops, ImageDraw

WIDTH, HEIGHT = 1024, 512
COLOUR = (169, 31, 35)
MARGIN = 14         # pixels between the ship and the inside of the line
STROKE = 9          # the line's width, as the original's
SCALE = 4           # supersampling for the anti-aliased edge


def convex_hull(points):
    points = sorted(set(points))
    if len(points) <= 2:
        return points

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in points:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(points):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def grown(polygon, radius, scale):
    """the polygon grown by a radius, with round corners (its Minkowski sum with a disc), drawn at the given scale"""
    image = Image.new('L', (WIDTH * scale, HEIGHT * scale), 0)
    draw = ImageDraw.Draw(image)
    scaled = [(x * scale, y * scale) for x, y in polygon]
    r = radius * scale
    draw.polygon(scaled, fill=255)
    for i in range(len(scaled)):
        a, b = scaled[i], scaled[(i + 1) % len(scaled)]
        draw.line([a, b], fill=255, width=int(round(2 * r)))
        draw.ellipse([a[0] - r, a[1] - r, a[0] + r, a[1] + r], fill=255)
    return image


def main():
    folder, output = sys.argv[1], sys.argv[2]
    masks = [Image.open(os.path.join(folder, name)).convert('L') for name in ('starport.png', 'status.png') if os.path.exists(os.path.join(folder, name))]
    if not masks:
        sys.exit('no silhouettes in ' + folder)
    union = masks[0]
    for mask in masks[1:]:
        union = ImageChops.lighter(union, mask)
    if union.size != (WIDTH, HEIGHT):
        sys.exit('the silhouettes must be %d x %d' % (WIDTH, HEIGHT))

    # the silhouettes are pictures in the sprite's own orientation, so their pixels are the sprite's
    pixels = union.load()
    points = [(x, y) for y in range(HEIGHT) for x in range(WIDTH) if pixels[x, y] > 127]
    if not points:
        sys.exit('the silhouettes are empty')
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    top, bottom = min(ys), max(ys)

    # the rear box: from the engines to the end of the widest parts (the weapon mounts)
    widest = [x for x, y in points if y <= top + 12 or y >= bottom - 12]
    rear = [(min(xs), top), (max(widest) + 1, top), (min(xs), bottom + 1), (max(widest) + 1, bottom + 1)]

    # the hull of the box and the ship's pixel corners
    corners = rear + [(x + dx, y + dy) for x, y in points for dx in (0, 1) for dy in (0, 1)]
    hull = convex_hull(corners)

    inner = grown(hull, MARGIN, SCALE)
    outer = grown(hull, MARGIN + STROKE, SCALE)
    ring = ImageChops.subtract(outer, inner).resize((WIDTH, HEIGHT), Image.BOX)

    bounds = ring.getbbox()
    if bounds is None or bounds[0] <= 0 or bounds[1] <= 0 or bounds[2] >= WIDTH or bounds[3] >= HEIGHT:
        sys.exit('the outline does not fit in the sprite: %s' % (bounds,))

    picture = Image.new('RGBA', (WIDTH, HEIGHT), COLOUR + (0,))
    picture.putalpha(ring)
    # fully transparent pixels keep the colour, so the edge does not fade to black when the sprite is filtered
    picture.save(output, optimize=True)
    print('ship spans x %d to %d, y %d to %d; outline %s; written %s' % (min(xs), max(xs), top, bottom, bounds, output))


if __name__ == '__main__':
    main()
