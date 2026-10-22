"""Texture for the streamer's cap (washed olive 6-panel dad cap, cream embroidery).

    python tools/make_cap_texture.py

Reads the cap dimensions written by Unity (CapBuilder: Stasik/Characters/Cap Test Scene) from
game/Assets/_Project/Art/Models/Characters/Streamer/Cap/cap_dims.json and writes Cap_Albedo.png next to it.
    python tools/make_cap_texture.py <dims.json> <out.png>    other dimensions (previews of fit variants)
UV layout (must match CapBuilder.cs):
  crown outside: u = azimuth (theta = pi/2 - 2*pi*u, front u=0.25, back u=0.75), v = 0.30 + 0.70 * elevation/90deg
  brim top: u 0..0.5 across, v = 0.02 + 0.24 * (crown -> edge); brim bottom: u 0.5..1; inside at (0.75, 0.14),
  brim edge at (0.25, 0.27), button at (0.75, 0.985).
Text is drawn in metres on the crown, so letters keep their shape whatever the head size.
"""
import json
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CAP_DIR = os.path.join(ROOT, "game", "Assets", "_Project", "Art", "Models", "Characters", "Streamer", "Cap")
FONTS = "C:/Windows/Fonts"

SIZE = 2048
FRONT_TEXT = "nymf71"
BACK_TEXT = ("маленький", "хуй")

OLIVE = (80, 80, 63)              # washed olive, greyer and darker than army green (renders ~1.3x lighter)
OLIVE_DARK = (64, 65, 51)
THREAD = (230, 219, 190)          # cream embroidery
STITCH = (66, 67, 53)             # tone-on-tone stitching, a bit darker than the fabric

FRONT_HEIGHT = 0.031              # m, "nymf71" (the reference script is small: ~1/4 of the front)
BACK_HEIGHT = 0.038               # m, the back lines; the joke is seen from the game camera behind the player
MAX_FRONT_ANGLE = 1.3             # rad of azimuth the front text may span (two front panels are 2.1)
MAX_BACK_ANGLE = 1.9              # rad for each back line (the two back panels are 2.1)
METAL = (38, 38, 40)

random.seed(71)


def speeds(dims, a):
    """Metres per radian on the front/back meridian at elevation a: (along the azimuth, along the elevation).
    The crown profile is a superellipse: horizontal cos(a)^p, height sin(a)^p (CapBuilder.Profile)."""
    p = dims.get("profile", 1.0)
    c, s = max(math.cos(a), 1e-4), max(math.sin(a), 1e-4)
    horizontal = dims["rx"] * c ** p
    vertical = math.hypot(dims["rz"] * p * c ** (p - 1) * s, dims["ry"] * p * s ** (p - 1) * c)
    return horizontal, vertical


def elevation_above(dims, a0, metres, step=0.002):
    """Elevation reached by walking `metres` up the front/back meridian from elevation a0."""
    a, walked = a0, 0.0
    while walked < metres and a < math.pi / 2:
        walked += speeds(dims, a + step / 2)[1] * step
        a += step
    return a


def elevation_at_height(dims, height):
    """Elevation where the crown is `height` m above the band (same as CapBuilder.ElevationAt)."""
    p = dims.get("profile", 1.0)
    return math.asin(min(1.0, height / dims["ry"]) ** (1 / p))


def crown_uv(theta, a):
    """theta: azimuth, 0 = front, + = character's right; a: elevation 0..pi/2. -> pixel (x, y)."""
    u = ((math.pi / 2 - theta) / (2 * math.pi)) % 1.0     # grows towards the character's left: text reads from outside
    v = 0.30 + 0.70 * a / (math.pi / 2)
    return u * SIZE, (1 - v) * SIZE


def fabric(w, h, base, seed):
    """Washed cotton: low-frequency blotches + fine grain. Wraps horizontally (the crown's u seam)."""
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h), base)
    cells = Image.new("L", (w // 16, h // 16))
    cells.putdata([rnd.randint(0, 255) for _ in range(cells.width * cells.height)])
    tiled = Image.new("L", (cells.width * 3, cells.height))
    for k in range(3):
        tiled.paste(cells, (k * cells.width, 0))
    blotch = tiled.resize((w * 3, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(24)).crop((w, 0, 2 * w, h))
    grain = Image.effect_noise((w, h), 28).filter(ImageFilter.GaussianBlur(0.6))
    light = Image.new("RGB", (w, h), tuple(min(255, c + 34) for c in base))
    dark = Image.new("RGB", (w, h), tuple(max(0, c - 22) for c in base))
    img = Image.composite(light, img, blotch.point(lambda p: int(max(0, p - 128) * 1.1)))
    img = Image.composite(dark, img, blotch.point(lambda p: int(max(0, 128 - p) * 0.9)))
    img = Image.composite(light, img, grain.point(lambda p: int(max(0, p - 150) * 0.5)))
    return img


def stitched_line(draw, pts, color, dash=9, gap=5, width=3):
    """Dashed line along a polyline (pixel coords)."""
    carry = 0.0
    on = True
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        seg = math.hypot(x1 - x0, y1 - y0)
        pos = -carry
        while pos < seg:
            length = dash if on else gap
            a = max(pos, 0)
            b = min(pos + length, seg)
            if on and b > a:
                draw.line([(x0 + (x1 - x0) * a / seg, y0 + (y1 - y0) * a / seg),
                           (x0 + (x1 - x0) * b / seg, y0 + (y1 - y0) * b / seg)], fill=color, width=width)
            if pos + length > seg:
                carry = seg - pos
                break
            pos += length
            on = not on


def meridian(theta, a0=0.0, a1=math.pi / 2 * 0.97, n=60):
    return [crown_uv(theta, a0 + (a1 - a0) * i / n) for i in range(n + 1)]


def embroidery(text, font_path, height_m, px_per_m=4000, spacing=0):
    """Text as a raised cream embroidery patch on a transparent canvas (metres -> px_per_m)."""
    font = ImageFont.truetype(font_path, int(height_m * px_per_m))
    box = font.getbbox(text)
    w, h = box[2] - box[0] + 40, box[3] - box[1] + 40
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).text((20 - box[0], 20 - box[1]), text, font=font, fill=255)
    # Satin-stitch look: diagonal thread lines inside the letters, a soft shadow below, a highlight on top.
    threads = Image.new("L", (w, h), 0)
    td = ImageDraw.Draw(threads)
    for x in range(-h, w, 5):
        td.line([(x, h), (x + h * 0.6, 0)], fill=255, width=2)
    colour = Image.new("RGB", (w, h), THREAD)
    shade = Image.new("RGB", (w, h), tuple(int(c * 0.82) for c in THREAD))
    colour = Image.composite(colour, shade, threads)
    shadow = mask.filter(ImageFilter.GaussianBlur(3))
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste((40, 44, 36, 255), (3, 4), shadow.point(lambda p: int(p * 0.55)))
    out.paste(colour, (0, 0), mask)
    rim = ImageChops.subtract(mask, mask.filter(ImageFilter.MinFilter(5)))
    out.paste(tuple(int(c * 0.72) for c in THREAD), (0, 0), rim)
    return out, px_per_m


def place_on_crown(tex, patch, px_per_m, theta, a, dims, max_angle=None):
    """Resample a metric patch onto the crown at (theta, a): u/v speeds come from the cap's radii. A patch wider than
    max_angle (rad of azimuth) is scaled down to fit. Returns the scale used."""
    horiz_m_per_rad, vert_m_per_rad = speeds(dims, a)
    scale = 1.0
    if max_angle is not None:
        scale = min(1.0, max_angle * horiz_m_per_rad / (patch.width / px_per_m))
    px_u = SIZE / (2 * math.pi) / horiz_m_per_rad * scale                # texture px per metre, horizontal
    px_v = (0.70 * SIZE) / (math.pi / 2) / vert_m_per_rad * scale        # vertical
    w = max(1, int(patch.width / px_per_m * px_u))
    h = max(1, int(patch.height / px_per_m * px_v))
    scaled = patch.resize((w, h), Image.LANCZOS)
    cx, cy = crown_uv(theta, a)
    tex.alpha_composite(scaled, (int(cx - w / 2), int(cy - h / 2)))
    return scale


def main():
    # Optional: python tools/make_cap_texture.py <dims.json> <out.png> (previews of fit variants).
    dims_path = sys.argv[1] if len(sys.argv) > 2 else os.path.join(CAP_DIR, "cap_dims.json")
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(CAP_DIR, "Cap_Albedo.png")
    with open(dims_path, encoding="utf-8") as f:
        dims = json.load(f)

    tex = Image.new("RGBA", (SIZE, SIZE), OLIVE + (255,))
    crown_h = int(SIZE * 0.70)
    tex.paste(fabric(SIZE, crown_h + 8, OLIVE, 1), (0, 0))                  # crown (v 0.30..1)
    brim_h = int(SIZE * 0.30)
    tex.paste(fabric(SIZE // 2, brim_h, OLIVE, 2), (0, SIZE - brim_h))      # brim top
    tex.paste(fabric(SIZE // 2, brim_h, OLIVE_DARK, 3), (SIZE // 2, SIZE - brim_h))  # brim bottom + inside
    draw = ImageDraw.Draw(tex)

    # Six panels: seams with a lighter worn ridge and double tone-on-tone stitching.
    for k in range(6):
        theta = k * math.pi / 3
        ridge = meridian(theta)
        draw.line(ridge, fill=tuple(min(255, c + 28) for c in OLIVE) + (255,), width=7)
        draw.line(ridge, fill=OLIVE_DARK + (255,), width=2)
        for side in (-1, 1):
            stitched_line(draw, meridian(theta + side * 0.035), STITCH + (255,))

    # Band: stitching along the bottom edge of the crown.
    band_a = elevation_above(dims, 0.0, 0.006)
    band = [crown_uv(2 * math.pi * i / 400 - math.pi / 2, band_a) for i in range(401)]
    stitched_line(draw, band, STITCH + (255,))

    # Eyelets: one high on every panel.
    eyelet_a = elevation_at_height(dims, 0.86 * dims["ry"])
    for k in range(6):
        x, y = crown_uv(math.pi / 6 + k * math.pi / 3, eyelet_a)
        draw.ellipse([x - 11, y - 9, x + 11, y + 9], fill=STITCH + (255,))
        draw.ellipse([x - 5, y - 4, x + 5, y + 4], fill=(28, 30, 26, 255))

    # Back: binding tape around the opening and the strap with its buckle.
    half, arch, top, strap = dims["openingHalfWidth"], dims["openingArchStart"], dims["openingTop"], dims["strapTop"]
    edge = []
    for i in range(61):
        t = -1 + 2 * i / 60
        if abs(t) > 0.999:
            a = strap
        else:
            a = arch + (top - arch) * math.sqrt(max(0.0, 1 - t * t))
        edge.append(crown_uv(math.pi + t * half * 1.04, a))
    draw.line(edge, fill=OLIVE_DARK + (255,), width=18)
    stitched_line(draw, edge, STITCH + (255,), width=2)
    (xa, y0), (xb, y1) = crown_uv(math.pi - half, strap), crown_uv(math.pi + half, 0.0)
    x0, x1 = min(xa, xb), max(xa, xb)
    draw.rectangle([x0, y0, x1, y1], fill=OLIVE_DARK + (255,))
    stitched_line(draw, [(x0, y0 + 6), (x1, y0 + 6)], STITCH + (255,), width=2)
    stitched_line(draw, [(x0, y1 - 6), (x1, y1 - 6)], STITCH + (255,), width=2)
    bx, _ = crown_uv(math.pi + half * 0.45, 0.0)
    draw.rectangle([bx - 16, y0 - 2, bx + 16, y1 + 2], fill=METAL + (255,))
    draw.rectangle([bx - 9, y0 + 6, bx + 9, y1 - 6], fill=OLIVE_DARK + (255,))
    draw.line([(bx, y0 + 4), (bx, y1 - 4)], fill=(150, 150, 152, 255), width=3)

    # Brim: rows of stitching parallel to the edge (top) and a centre band (bottom).
    for w in (0.92, 0.80, 0.68, 0.56, 0.44, 0.32):
        y = (1 - (0.02 + 0.24 * w)) * SIZE
        stitched_line(draw, [(4, y), (SIZE / 2 - 4, y)], STITCH + (255,), width=2)
    draw.rectangle([SIZE // 2, SIZE - 6, SIZE, SIZE], fill=OLIVE_DARK + (255,))

    # Embroidery: "nymf71" mid-front (script); on the back two lines stacked from just above the opening
    # (condensed serif), like "Poker / Power" on the reference. Positions are walked in metres along the crown.
    script, ppm = embroidery(FRONT_TEXT, f"{FONTS}/segoescb.ttf", height_m=FRONT_HEIGHT)
    place_on_crown(tex, script, ppm, 0.0, elevation_above(dims, 0.0, 0.016 + FRONT_HEIGHT / 2), dims, MAX_FRONT_ANGLE)
    serif = f"{FONTS}/GeorgiaPro-CondSemiBold.ttf"
    upper, _ = embroidery(BACK_TEXT[0], serif, height_m=BACK_HEIGHT)
    lower, _ = embroidery(BACK_TEXT[1], serif, height_m=BACK_HEIGHT)
    lower_h = (lower.height - 40) / ppm                                   # glyphs without the patch padding
    upper_h = (upper.height - 40) / ppm
    lower_a = elevation_above(dims, dims["openingTop"], 0.008 + lower_h / 2)
    place_on_crown(tex, lower, ppm, math.pi, lower_a, dims, MAX_BACK_ANGLE)
    upper_a = elevation_above(dims, lower_a, lower_h / 2 + 0.005 + upper_h / 2)
    place_on_crown(tex, upper, ppm, math.pi, upper_a, dims, MAX_BACK_ANGLE)

    tex.convert("RGB").save(out, optimize=True)
    print("wrote", out)


if __name__ == "__main__":
    main()
