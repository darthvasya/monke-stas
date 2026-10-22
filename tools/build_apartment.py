"""Streamer's apartment shell (Season 0): walls, door/window openings, floors, terrace.

    python build_apartment.py           # write apartment.json (SolidCraft command list)
    python build_apartment.py --build   # also build it in a running SolidCraft (--control 7878),
                                        # save, export for Unity, render previews

Layout follows the hand-drawn plan (docs/apartment-plan.md), sizes are rough guesses.
X - left to right, Y - towards the back of the flat (terrace at Y=0), Z - up; mm.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT_MODEL = os.path.join(ROOT, "game", "Assets", "_Project", "Art", "Models", "Apartment")
OUT_PREVIEW = os.path.join(ROOT, "docs", "img")

# ----------------------------------------------------------------------------- parameters
H = 3000            # ceiling height
T_EXT = 200         # exterior wall thickness
T_INT = 100         # interior wall thickness
FLOOR = 100         # floor slab thickness (below Z=0)
DOOR_W, DOOR_H = 900, 2100
GLASS_T = 40        # glass thickness

# Grid lines of the plan (wall centre lines)
X_W, X_MID, X_LIV, X_COR, X_BED, X_E = 1320, 4010, 4400, 7070, 7820, 8850
Y_LIV, Y_SHOWER, Y_COR, Y_ROOMS, Y_N = 1870, 7860, 9700, 11040, 13980
TERRACE_X, TERRACE_Y = (0, 11740), (0, Y_LIV)

ROOMS = {  # name: (x0, y0, x1, y1)
    "Living": (X_LIV, Y_LIV, X_E, Y_COR),
    "Corridor": (X_W, Y_COR, X_COR, Y_ROOMS),
    "Shower": (X_W, Y_SHOWER, X_LIV, Y_COR),
    "StreamerRoom": (X_W, Y_ROOMS, X_MID, Y_N),
    "Bedroom": (X_MID, Y_ROOMS, X_BED, Y_N),
}

# name, axis ("x": runs along X at fixed y; "y": runs along Y at fixed x), fixed coord, from, to, thickness
WALLS = [
    ("Wall_LivingEast", "y", X_E, Y_LIV, Y_COR, T_EXT),
    ("Wall_LivingNorthEast", "x", Y_COR, X_COR, X_E, T_EXT),
    ("Wall_CorridorEast", "y", X_COR, Y_COR, Y_ROOMS, T_EXT),
    ("Wall_BedroomSouthEast", "x", Y_ROOMS, X_COR, X_BED, T_EXT),
    ("Wall_BedroomEast", "y", X_BED, Y_ROOMS, Y_N, T_EXT),
    ("Wall_North", "x", Y_N, X_W, X_BED, T_EXT),
    ("Wall_West", "y", X_W, Y_SHOWER, Y_N, T_EXT),
    ("Wall_ShowerSouth", "x", Y_SHOWER, X_W, X_LIV, T_EXT),
    ("Wall_LivingWest", "y", X_LIV, Y_LIV, Y_SHOWER, T_EXT),
    ("Wall_CorridorNorth", "x", Y_ROOMS, X_W, X_COR, T_INT),
    ("Wall_StreamerBedroom", "y", X_MID, Y_ROOMS, Y_N, T_INT),
    ("Wall_CorridorSouth", "x", Y_COR, X_W, X_COR, T_INT),
    ("Wall_ShowerLiving", "y", X_LIV, Y_SHOWER, Y_COR, T_INT),
]

# Openings: wall, centre (absolute X or Y along the wall), width, sill height, top height, name
OPENINGS = [
    ("Wall_CorridorNorth", 2250, DOOR_W, 0, DOOR_H, "Door_StreamerRoom"),
    ("Wall_CorridorNorth", 5500, DOOR_W, 0, DOOR_H, "Door_Bedroom"),
    ("Wall_CorridorSouth", 2500, DOOR_W, 0, DOOR_H, "Door_Shower"),
    ("Wall_CorridorSouth", 5700, 1000, 0, DOOR_H, "Door_Living"),
    ("Wall_North", (X_W + X_MID) / 2, 1400, 900, 2400, "Window_StreamerRoom"),
    ("Wall_North", (X_MID + X_BED) / 2, 1800, 900, 2400, "Window_Bedroom"),
]

# Classic windows: vertical mullions per window, transom (horizontal bar) height from the sill
WINDOW_MULLIONS = {"Window_StreamerRoom": 1, "Window_Bedroom": 2}
TRANSOM = 0.68                # fraction of the window height
WF_W, WF_D = 60, 80           # window frame profile: face width, depth

# Panoramic glass front of the living room (Y = Y_LIV): lintel on top, glass below, glass door in it
GLASS_TOP = 2700
ENTRANCE_X = 5660           # entrance door centre
RAILING_H = 1000

GLASS_COLOR = "#9FC7D9"
WALL_COLOR = "#E8E4DC"
FLOOR_COLOR = "#B89A74"
TERRACE_COLOR = "#8C8C8C"
FRAME_COLOR = "#2B2B2B"
WINDOW_FRAME_COLOR = "#F4F2EE"


# ----------------------------------------------------------------------------- geometry
def box(name, x0, y0, z0, x1, y1, z1, op="new", targets=None):
    p = {"length": round(x1 - x0, 2), "width": round(y1 - y0, 2), "height": round(z1 - z0, 2),
         "corner": [round(x0, 2), round(y0, 2), round(z0, 2)], "operation": op, "name": name}
    if op == "new":
        p["body_name"] = name
    else:
        p["targets"] = targets
    return {"command": "solid.box", "params": p}


def wall_box(axis, c, a, b, t, z0=0, z1=H):
    """Wall from a to b along its axis; extended by t/2 so corners close."""
    h = t / 2
    if axis == "x":
        return a - h, c - h, z0, b + h, c + h, z1
    return c - h, a - h, z0, c + h, b + h, z1


def in_wall(axis, c, u0, u1, z0, z1, depth):
    """Box spanning u0..u1 along a wall (at fixed coordinate c), depth across it."""
    d = depth / 2
    if axis == "x":
        return u0, c - d, z0, u1, c + d, z1
    return c - d, u0, z0, c + d, u1, z1


def classic_window(name, axis, c, u0, u1, z0, z1, mullions):
    """Glass pane plus a white frame: outer frame, vertical mullions, one transom."""
    glass = [box(f"Glass_{name}", *in_wall(axis, c, u0, u1, z0, z1, GLASS_T))]
    w, parts = WF_W, []
    parts.append(("Bottom", u0, u1, z0, z0 + w))
    parts.append(("Top", u0, u1, z1 - w, z1))
    parts.append(("Left", u0, u0 + w, z0 + w, z1 - w))
    parts.append(("Right", u1 - w, u1, z0 + w, z1 - w))
    zt = z0 + (z1 - z0) * TRANSOM
    parts.append(("Transom", u0 + w, u1 - w, zt - w / 2, zt + w / 2))
    for i in range(1, mullions + 1):
        um = u0 + (u1 - u0) * i / (mullions + 1)
        parts.append((f"Mullion{i}", um - w / 2, um + w / 2, z0 + w, z1 - w))
    frames = [box(f"WindowFrame_{name}_{p}", *in_wall(axis, c, a, b, za, zb, WF_D)) for p, a, b, za, zb in parts]
    return glass, frames


def script():
    cmds, groups = [], {"walls": [], "floors": [], "glass": [], "frames": [], "window_frames": []}
    walls = {w[0]: w for w in WALLS}

    for name, (x0, y0, x1, y1) in ROOMS.items():
        n = f"Floor_{name}"
        cmds.append(box(n, x0, y0, -FLOOR, x1, y1, 0))
        groups["floors"].append(n)
    cmds.append(box("Floor_Terrace", TERRACE_X[0], TERRACE_Y[0], -FLOOR, TERRACE_X[1], TERRACE_Y[1], 0))

    for name, axis, c, a, b, t in WALLS:
        cmds.append(box(name, *wall_box(axis, c, a, b, t)))
        groups["walls"].append(name)

    for wall, centre, width, z0, z1, name in OPENINGS:
        _, axis, c, _, _, t = walls[wall]
        u0, u1 = centre - width / 2, centre + width / 2
        cmds.append(box(name, *in_wall(axis, c, u0, u1, z0, z1, 2 * t), op="cut", targets=[wall]))
        if name in WINDOW_MULLIONS:
            glass, frames = classic_window(name, axis, c, u0, u1, z0, z1, WINDOW_MULLIONS[name])
            cmds += glass + frames
            groups["glass"] += [g["params"]["name"] for g in glass]
            groups["window_frames"] += [f["params"]["name"] for f in frames]

    # Glass front of the living room
    h = T_EXT / 2
    cmds.append(box("Wall_LivingFrontLintel", X_LIV - h, Y_LIV - h, GLASS_TOP, X_E + h, Y_LIV + h, H))
    groups["walls"].append("Wall_LivingFrontLintel")
    d0, d1 = ENTRANCE_X - DOOR_W / 2, ENTRANCE_X + DOOR_W / 2
    g = GLASS_T / 2
    for name, x0, x1 in (("Glass_FrontLeft", X_LIV + h, d0), ("Glass_FrontRight", d1, X_E - h)):
        cmds.append(box(name, x0, Y_LIV - g, 0, x1, Y_LIV + g, GLASS_TOP))
        groups["glass"].append(name)
    cmds.append(box("Door_Entrance", d0, Y_LIV - g, 0, d1, Y_LIV + g, DOOR_H))
    groups["glass"].append("Door_Entrance")
    f = 30  # door frame posts and head
    for name, x0, x1, z0, z1 in (("Frame_EntranceLeft", d0 - f, d0, 0, GLASS_TOP),
                                  ("Frame_EntranceRight", d1, d1 + f, 0, GLASS_TOP),
                                  ("Frame_EntranceHead", d0 - f, d1 + f, DOOR_H, DOOR_H + f)):
        cmds.append(box(name, x0, Y_LIV - 40, z0, x1, Y_LIV + 40, z1))
        groups["frames"].append(name)

    # Glass railing around the terrace
    tx0, tx1 = TERRACE_X
    for name, x0, y0, x1, y1 in (("Railing_South", tx0, 0, tx1, GLASS_T),
                                 ("Railing_West", tx0, 0, tx0 + GLASS_T, Y_LIV - h),
                                 ("Railing_East", tx1 - GLASS_T, 0, tx1, Y_LIV - h)):
        cmds.append(box(name, x0, y0, 0, x1, y1, RAILING_H))
        groups["glass"].append(name)

    looks = [
        (groups["walls"], WALL_COLOR, None),
        (groups["floors"], FLOOR_COLOR, None),
        (["Floor_Terrace"], TERRACE_COLOR, None),
        (groups["glass"], GLASS_COLOR, {"metallic": 0.0, "roughness": 0.05, "dielectric_ior": 1.5}),
        (groups["frames"], FRAME_COLOR, {"metallic": 0.7, "roughness": 0.4, "dielectric_ior": 1.5}),
        (groups["window_frames"], WINDOW_FRAME_COLOR, None),
    ]
    for bodies, color, pbr in looks:
        p = {"bodies": bodies, "color": color}
        if pbr:
            p["pbr"] = pbr
        cmds.append({"command": "appearance.assign", "params": p})
    all_bodies = (groups["walls"] + groups["floors"] + ["Floor_Terrace"] + groups["glass"]
                  + groups["frames"] + groups["window_frames"])
    return cmds, all_bodies


# ----------------------------------------------------------------------------- Unity export
MATERIALS = {  # name: (Kd colour, opacity)
    "Wall": (WALL_COLOR, 1.0), "Floor": (FLOOR_COLOR, 1.0), "Terrace": (TERRACE_COLOR, 1.0),
    "Tiles": ("#F0F0F0", 1.0),
    "Glass": (GLASS_COLOR, 0.3), "Frame": (FRAME_COLOR, 1.0), "WindowFrame": (WINDOW_FRAME_COLOR, 1.0),
}


def material_of(body):
    if body == "Floor_Terrace":
        return "Terrace"
    if body == "Floor_Shower":
        return "Tiles"
    if body.startswith(("Glass_", "Railing_", "Door_Entrance")):
        return "Glass"
    return body.split("_")[0]


def to_unity_obj(src, dst):
    """SolidCraft OBJ (mm, Z up, right-handed) -> metres, Y up, plus .mtl with a material per body type.

    Unity's OBJ importer negates X, so writing (-x, z, y) lands in Unity as (x, z, y):
    X right, Y up, Z towards the back of the flat."""
    def conv(parts, scale):
        x, y, z = (float(v) for v in parts)
        return f"{-x * scale:.4f} {z * scale:.4f} {y * scale:.4f}"

    def box_uv(p, n):
        """Planar UV by the face's dominant normal axis, 1 UV unit = 1 m."""
        x, y, z = (c * 0.001 for c in p)
        ax = max(range(3), key=lambda i: abs(n[i]))
        u, v = ((y, z), (x, z), (x, y))[ax]
        return f"vt {u:.4f} {v:.4f}"

    mtl_name = os.path.splitext(os.path.basename(dst))[0] + ".mtl"
    out = [f"mtllib {mtl_name}"]
    verts, normals, n_vt = [], [], 0
    with open(src, encoding="utf-8") as f:
        for line in f:
            tag, *rest = line.split()
            if tag == "v":
                verts.append([float(c) for c in rest])
                out.append("v " + conv(rest, 0.001))
            elif tag == "vn":
                normals.append([float(c) for c in rest])
                out.append("vn " + conv(rest, 1))
            elif tag == "o":
                out += [line.strip(), f"usemtl {material_of(rest[0])}"]
            elif tag == "f":
                corners = []
                for corner in rest:
                    vi, _, ni = corner.split("/")
                    out.append(box_uv(verts[int(vi) - 1], normals[int(ni) - 1]))
                    n_vt += 1
                    corners.append(f"{vi}/{n_vt}/{ni}")
                out.append("f " + " ".join(corners))
            elif tag != "#":
                out.append(line.strip())
    with open(dst, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")

    with open(os.path.join(os.path.dirname(dst), mtl_name), "w", encoding="utf-8") as f:
        for name, (color, opacity) in MATERIALS.items():
            r, g, b = (int(color[i:i + 2], 16) / 255 for i in (1, 3, 5))
            f.write(f"newmtl {name}\nKd {r:.3f} {g:.3f} {b:.3f}\nd {opacity}\n\n")


# ----------------------------------------------------------------------------- SolidCraft
def build(cmds, bodies):
    from solidcraft import SolidCraft
    sc = SolidCraft()
    docs = sc("ui.documents")["documents"]
    mine = [i for i, d in enumerate(docs) if d["name"] == "apartment"]
    if mine:
        sc("ui.documents", {"action": "switch", "index": mine[0]})
        sc.run("file.new", {"name": "apartment"})
    else:
        sc("ui.documents", {"action": "new"})
    r = sc("engine.batch", {"commands": cmds})
    if "failed" in r:
        raise RuntimeError(r["failed"])
    doc = sc("document.inspect")
    print("bodies:", len(doc["bodies"]), "timeline errors:",
          [t["name"] for t in doc["timeline"] if t.get("error")])

    os.makedirs(OUT_MODEL, exist_ok=True)
    os.makedirs(OUT_PREVIEW, exist_ok=True)
    p = lambda *a: os.path.join(*a).replace("\\", "/")
    sc.run("file.save_as", {"path": p(HERE, "apartment.solidcraft")})
    raw = p(HERE, "apartment_raw.obj")
    sc.run("file.export", {"path": raw, "bodies": bodies})
    to_unity_obj(raw, os.path.join(OUT_MODEL, "Apartment.obj"))
    os.remove(raw)
    shots = [("apartment_top.png", {"view": "top"}, {"fit": True}),
             ("apartment_3d.png", {"view": "front"}, {"yaw": -0.6, "pitch": 0.75, "fit": True})]
    for fname, view, cam in shots:
        sc("ui.view", view)
        sc("ui.camera", cam)
        sc("ui.render", {"path": p(OUT_PREVIEW, fname), "width": 1400, "height": 1200})


def main():
    cmds, bodies = script()
    with open(os.path.join(HERE, "apartment.json"), "w", encoding="utf-8") as f:
        f.write('{"commands": [\n' + ",\n".join("  " + json.dumps(c, ensure_ascii=False) for c in cmds) + "\n]}\n")
    print(f"commands: {len(cmds)}, bodies: {len(bodies)}")
    if "--build" in sys.argv:
        build(cmds, bodies)


if __name__ == "__main__":
    main()
