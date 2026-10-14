# Builds the low-poly temple courtyard around the arena and exports it as one GLB.
#   blender -b -P blender/build_courtyard.py
# Positions are written in Unity coordinates through P(x, z, y) (Unity +Z = north, the
# main hall; the fight happens around the origin). Materials are named so Unity can swap
# them for textured URP materials: Stone, Plaster, Wood, DarkWood, Vermilion, RoofTile,
# Bark, Blossom, LanternGlow, Gold. UVs are box-projected at 1 UV unit per metre.
import bpy, bmesh, math, os, random

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "UnityProject", "Assets", "Courtyard", "Courtyard.glb")
os.makedirs(os.path.dirname(OUT), exist_ok=True)
random.seed(7)
bpy.ops.wm.read_factory_settings(use_empty=True)


def P(x, z, y=0.0):
    """Unity (x, y, z) -> Blender (-x, -z, y)."""
    return (-x, -z, y)


MATS = {}


def mat(name, rgb, rough=0.8, metal=0.0, emit=None):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = 3.0
    MATS[name] = m
    return m


STONE = mat("Stone", (0.5, 0.48, 0.45))
PLASTER = mat("Plaster", (0.86, 0.82, 0.74), 0.95)
WOOD = mat("Wood", (0.35, 0.18, 0.1), 0.6)
DARK = mat("DarkWood", (0.12, 0.07, 0.05), 0.7)
RED = mat("Vermilion", (0.72, 0.12, 0.05), 0.55)
ROOF = mat("RoofTile", (0.2, 0.22, 0.25), 0.6)
BARK = mat("Bark", (0.22, 0.13, 0.09), 0.9)
BLOSSOM = mat("Blossom", (0.98, 0.62, 0.74), 0.8)
GLOW = mat("LanternGlow", (1.0, 0.7, 0.35), 0.5, emit=(1.0, 0.55, 0.2))
GOLD = mat("Gold", (0.85, 0.62, 0.2), 0.3, 0.9)
MOSS = mat("Moss", (0.16, 0.28, 0.12), 0.95)
WATER = mat("Water", (0.04, 0.18, 0.22), 0.12, 0.25, emit=(0.015, 0.05, 0.06))
CLOTH = mat("Cloth", (0.62, 0.1, 0.08), 0.75)
BAMBOO = mat("Bamboo", (0.23, 0.42, 0.14), 0.62)

root = bpy.data.objects.new("Courtyard", None)
bpy.context.scene.collection.objects.link(root)
_count = [0]


def box_uv(bm, scale=1.0):
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            if ax == 0:
                l[uv].uv = (c.y * scale, c.z * scale)
            elif ax == 1:
                l[uv].uv = (c.x * scale, c.z * scale)
            else:
                l[uv].uv = (c.x * scale, c.y * scale)


def finish(name, bm, material, parent=None):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    box_uv(bm)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    _count[0] += 1
    ob = bpy.data.objects.new(f"{name}_{_count[0]}", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent or root
    return ob


def box(name, center, size, material, parent=None, taper=None, rot_y=0.0):
    """center/size in Unity axes (x, y, z) -> blender; rot_y is Unity yaw in degrees."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    cx, cy, cz = center
    sx, sy, sz = size
    a = math.radians(rot_y)
    for v in bm.verts:
        x, y, z = v.co  # blender-space unit cube: x -> unity -x, y -> unity -z, z -> unity y
        ux, uy, uz = -x * sx, z * sy, -y * sz
        if taper and z > 0:
            ux *= taper[0]
            uz *= taper[1]
        rx = ux * math.cos(a) + uz * math.sin(a)
        rz = -ux * math.sin(a) + uz * math.cos(a)
        v.co = P(cx + rx, cz + rz, cy + uy)
    return finish(name, bm, material, parent)


def cyl(name, base, radius, height, material, sides=8, top_radius=None, parent=None):
    bm = bmesh.new()
    tr = radius if top_radius is None else top_radius
    bot = [bm.verts.new(P(base[0] + radius * math.cos(i * 2 * math.pi / sides), base[2] + radius * math.sin(i * 2 * math.pi / sides), base[1])) for i in range(sides)]
    top = [bm.verts.new(P(base[0] + tr * math.cos(i * 2 * math.pi / sides), base[2] + tr * math.sin(i * 2 * math.pi / sides), base[1] + height)) for i in range(sides)]
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    return finish(name, bm, material, parent)


def roof(name, center, y, w, d, rise, overhang=1.2, upturn=0.45, thick=0.25, ridge_frac=0.35, material=ROOF):
    """Hip roof with flared, upturned eaves. center=(x, z); w/d = footprint; y = eave height."""
    cx, cz = center
    bm = bmesh.new()
    hw, hd = w / 2 + overhang, d / 2 + overhang

    def ring(sw, sd, yy, lift, inset=0.0):
        pts = []
        for (sx, sz) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            # corner raised, edge midpoints lower for a curved eave
            pts.append((cx + sx * sw, yy + lift, cz + sz * sd))
            nx, nz = {(-1, -1): (1, -1), (1, -1): (1, 1), (1, 1): (-1, 1), (-1, 1): (-1, -1)}[(sx, sz)]
            pts.append((cx + (sx + nx) / 2 * sw, yy, cz + (sz + nz) / 2 * sd))
        return pts

    eave = ring(hw, hd, y, upturn)
    mid = ring(hw * 0.62, hd * 0.62, y + rise * 0.55, upturn * 0.3)
    ridge_half = max(w, d) * ridge_frac / 2
    if w >= d:
        ridge = [(cx - ridge_half, y + rise, cz), (cx + ridge_half, y + rise, cz)]
    else:
        ridge = [(cx, y + rise, cz - ridge_half), (cx, y + rise, cz + ridge_half)]

    def layer(dy):
        e = [bm.verts.new(P(p[0], p[2], p[1] + dy)) for p in eave]
        m = [bm.verts.new(P(p[0], p[2], p[1] + dy)) for p in mid]
        r = [bm.verts.new(P(p[0], p[2], p[1] + dy)) for p in ridge]
        return e, m, r

    e, m, r = layer(0)
    eb, mb, rb = layer(-thick)
    n = len(eave)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((e[i], e[j], m[j], m[i]))
        bm.faces.new((mb[i], mb[j], eb[j], eb[i]))
        bm.faces.new((eb[i], eb[j], e[j], e[i]))
    # mid ring to ridge: each mid vertex joins the nearer ridge end
    def near(p):
        return 0 if (p[0] - ridge[0][0]) ** 2 + (p[2] - ridge[0][2]) ** 2 < (p[0] - ridge[1][0]) ** 2 + (p[2] - ridge[1][2]) ** 2 else 1
    for i in range(n):
        j = (i + 1) % n
        a, b = near(mid[i]), near(mid[j])
        for (mm, rr) in ((m, r), (mb, rb)):
            if a == b:
                bm.faces.new((mm[i], mm[j], rr[a]))
            else:
                bm.faces.new((mm[i], mm[j], rr[b], rr[a]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
    ob = finish(name, bm, material)
    # ridge beam
    rl = abs(ridge[1][0] - ridge[0][0]) + abs(ridge[1][2] - ridge[0][2])
    box(name + "Ridge", (cx, y + rise + 0.12, cz), (rl + 0.6 if w >= d else 0.35, 0.3, 0.35 if w >= d else rl + 0.6), DARK)
    for p in ridge:  # gold ridge ornaments
        box(name + "Orn", (p[0], y + rise + 0.45, p[2]), (0.25, 0.6, 0.25), GOLD, taper=(0.4, 0.4))
    return ob


# ---------------------------------------------------------------- main hall (north)
def main_hall(cz=19.0):
    W, D = 18.0, 9.0
    box("HallBase", (0, 0.6, cz), (W + 2, 1.2, D + 2), STONE)
    for i in range(6):  # front steps down to the courtyard
        box("HallStep", (0, 0.1 + i * 0.2, cz - D / 2 - 1 - 2.4 + i * 0.4), (7, 0.2, 0.4 + (5 - i) * 0.0), STONE)
    box("HallStepBlock", (0, 0.55, cz - D / 2 - 1.6), (7, 1.1, 1.3), STONE)
    floor_y = 1.2
    box("HallWallBack", (0, floor_y + 2.5, cz + D / 2 - 0.2), (W - 0.5, 5, 0.3), WOOD)
    box("HallWallL", (-W / 2 + 0.2, floor_y + 2.5, cz), (0.3, 5, D - 0.5), WOOD)
    box("HallWallR", (W / 2 - 0.2, floor_y + 2.5, cz), (0.3, 5, D - 0.5), WOOD)
    # front: vermilion pillars with dark lattice doors between
    cols = 7
    for i in range(cols):
        x = -W / 2 + 0.5 + i * (W - 1) / (cols - 1)
        cyl("HallPillar", (x, floor_y, cz - D / 2 + 0.4), 0.32, 5.2, RED, sides=10)
        cyl("HallPillarBase", (x, floor_y, cz - D / 2 + 0.4), 0.45, 0.3, STONE, sides=8)
        if i < cols - 1:
            nx = -W / 2 + 0.5 + (i + 1) * (W - 1) / (cols - 1)
            box("HallDoor", ((x + nx) / 2, floor_y + 2.0, cz - D / 2 + 0.9), (nx - x - 0.7, 4.0, 0.15), DARK)
            for k in range(4):  # lattice bars
                box("Lattice", ((x + nx) / 2, floor_y + 0.6 + k * 1.0, cz - D / 2 + 0.8), (nx - x - 0.8, 0.08, 0.1), WOOD)
    box("HallBeam", (0, floor_y + 5.2, cz - D / 2 + 0.4), (W + 0.4, 0.55, 0.6), RED)
    box("HallBeam2", (0, floor_y + 4.5, cz - D / 2 + 0.4), (W, 0.25, 0.4), RED)
    box("HallBracketRow", (0, floor_y + 5.65, cz), (W + 0.8, 0.4, D + 0.8), DARK)
    roof("HallRoof", (0, cz), floor_y + 5.9, W, D, 3.4, overhang=2.0, upturn=0.6, thick=0.35)
    # upper storey + second roof
    box("HallUpper", (0, floor_y + 8.2, cz), (W * 0.55, 2.2, D * 0.55), WOOD)
    for i in range(5):
        x = -W * 0.275 + 0.3 + i * (W * 0.55 - 0.6) / 4
        cyl("UpperPillar", (x, floor_y + 7.1, cz - D * 0.275 - 0.05), 0.2, 2.2, RED, sides=8)
    roof("HallRoofTop", (0, cz), floor_y + 9.3, W * 0.55, D * 0.55, 2.6, overhang=1.6, upturn=0.55, thick=0.3)
    # plaque above the door
    box("Plaque", (0, floor_y + 4.85, cz - D / 2 + 0.05), (2.6, 0.9, 0.12), DARK)
    box("PlaqueGold", (0, floor_y + 4.85, cz - D / 2 - 0.02), (2.2, 0.6, 0.05), GOLD)


# ---------------------------------------------------------------- pagoda (north-east, beyond the wall)
def pagoda(cx, cz, tiers=5):
    box("PagodaBase", (cx, 0.7, cz), (7, 1.4, 7), STONE)
    y, w = 1.4, 5.0
    for t in range(tiers):
        h = 2.6 if t == 0 else 1.9
        box("PagodaBody", (cx, y + h / 2, cz), (w, h, w), PLASTER if t % 2 == 0 else WOOD)
        for sx in (-1, 1):
            for sz in (-1, 1):
                cyl("PagodaPost", (cx + sx * w / 2, y, cz + sz * w / 2), 0.16, h, RED, sides=6)
        box("PagodaBand", (cx, y + h - 0.2, cz), (w + 0.15, 0.35, w + 0.15), RED)
        roof("PagodaRoof", (cx, cz), y + h, w, w, 1.2, overhang=1.3, upturn=0.5, thick=0.25, ridge_frac=0.0)
        y += h + 0.35
        w *= 0.84
    cyl("PagodaSpire", (cx, y + 0.3, cz), 0.12, 4.5, GOLD, sides=6)
    for k in range(6):
        cyl("PagodaRing", (cx, y + 0.9 + k * 0.5, cz), 0.35 - k * 0.03, 0.12, GOLD, sides=8)


# ---------------------------------------------------------------- torii gate (south)
def torii(cz):
    for sx in (-1, 1):
        cyl("ToriiPillar", (sx * 3.2, 0, cz), 0.38, 7.2, RED, sides=10, top_radius=0.33)
        cyl("ToriiFoot", (sx * 3.2, 0, cz), 0.5, 0.6, DARK, sides=10)
    box("ToriiNuki", (0, 5.9, cz), (8.6, 0.45, 0.35), RED)
    box("ToriiShimaki", (0, 7.25, cz), (9.4, 0.5, 0.6), RED)
    box("ToriiKasagi", (0, 7.75, cz), (10.4, 0.45, 0.8), DARK)
    for sx in (-1, 1):  # upswept kasagi tips
        box("ToriiTip", (sx * 5.3, 7.9, cz), (1.0, 0.4, 0.78), DARK, taper=(1.0, 1.0))
    box("ToriiGakuzuka", (0, 6.6, cz), (0.4, 1.0, 0.3), RED)
    box("ToriiPlaque", (0, 6.6, cz - 0.2), (0.9, 1.2, 0.1), DARK)


# ---------------------------------------------------------------- stone lantern (toro)
def lantern(x, z, s=1.0):
    box("ToroFoot", (x, 0.15 * s, z), (1.0 * s, 0.3 * s, 1.0 * s), STONE, taper=(0.8, 0.8))
    cyl("ToroPost", (x, 0.3 * s, z), 0.18 * s, 1.1 * s, STONE, sides=6)
    box("ToroPlatform", (x, 1.5 * s, z), (0.9 * s, 0.2 * s, 0.9 * s), STONE, taper=(1.2, 1.2))
    box("ToroFirebox", (x, 1.85 * s, z), (0.55 * s, 0.5 * s, 0.55 * s), STONE)
    box("ToroLightX", (x, 1.85 * s, z), (0.6 * s, 0.3 * s, 0.3 * s), GLOW)
    box("ToroLightZ", (x, 1.85 * s, z), (0.3 * s, 0.3 * s, 0.6 * s), GLOW)
    box("ToroCap", (x, 2.25 * s, z), (1.2 * s, 0.35 * s, 1.2 * s), STONE, taper=(0.25, 0.25))
    box("ToroFinial", (x, 2.55 * s, z), (0.22 * s, 0.3 * s, 0.22 * s), STONE, taper=(0.3, 0.3))


# ---------------------------------------------------------------- courtyard walls
def wall(x0, z0, x1, z1, h=3.2):
    L = math.hypot(x1 - x0, z1 - z0)
    yaw = math.degrees(math.atan2(x1 - x0, z1 - z0))
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    box("WallFooting", (cx, 0.3, cz), (0.9, 0.6, L), STONE, rot_y=yaw)
    box("WallPlaster", (cx, 0.6 + (h - 0.6) / 2, cz), (0.6, h - 0.6, L), PLASTER, rot_y=yaw)
    for k in range(3):  # decorative white-line bands like a temple tsuiji-bei wall
        box("WallBand", (cx, 1.4 + k * 0.45, cz), (0.64, 0.06, L), WOOD, rot_y=yaw)
    box("WallBeam", (cx, h + 0.05, cz), (0.7, 0.18, L), DARK, rot_y=yaw)
    box("WallCap", (cx, h + 0.35, cz), (1.5, 0.45, L + 0.4), ROOF, taper=(0.15, 1.0), rot_y=yaw)
    n = int(L / 3.5)
    for i in range(n + 1):
        t = i / n
        px, pz = x0 + (x1 - x0) * t, z0 + (z1 - z0) * t
        box("WallPost", (px, 0.6 + (h - 0.6) / 2, pz), (0.7, h - 0.6, 0.3), WOOD, rot_y=yaw)


# ---------------------------------------------------------------- cherry trees
def cherry(x, z, s=1.0):
    trunk_h = 2.6 * s
    # bent trunk as three stacked tapered segments
    px, pz, y = x, z, 0.0
    r = 0.32 * s
    lean = random.uniform(0, 2 * math.pi)
    for k in range(3):
        seg = trunk_h / 3
        nx, nz = px + math.cos(lean) * 0.25 * s, pz + math.sin(lean) * 0.25 * s
        bm = bmesh.new()
        sides = 7
        bot = [bm.verts.new(P(px + r * math.cos(i * 2 * math.pi / sides), pz + r * math.sin(i * 2 * math.pi / sides), y)) for i in range(sides)]
        r2 = r * 0.8
        top = [bm.verts.new(P(nx + r2 * math.cos(i * 2 * math.pi / sides), nz + r2 * math.sin(i * 2 * math.pi / sides), y + seg)) for i in range(sides)]
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((bot[i], bot[j], top[j], top[i]))
        bm.faces.new(list(reversed(bot)))
        bm.faces.new(top)
        finish("CherryTrunk", bm, BARK)
        px, pz, y, r = nx, nz, y + seg, r2
        lean += random.uniform(-0.8, 0.8)
    # branches + blossom clouds
    for b in range(5):
        a = b * 2 * math.pi / 5 + random.uniform(-0.3, 0.3)
        L = random.uniform(1.6, 2.4) * s
        ex, ez, ey = px + math.cos(a) * L, pz + math.sin(a) * L, y + random.uniform(0.6, 1.4) * s
        bm = bmesh.new()
        sides = 5
        rb = 0.13 * s
        v0 = [bm.verts.new(P(px + rb * math.cos(i * 2 * math.pi / sides), pz + rb * math.sin(i * 2 * math.pi / sides), y - 0.2)) for i in range(sides)]
        v1 = [bm.verts.new(P(ex + rb * 0.4 * math.cos(i * 2 * math.pi / sides), ez + rb * 0.4 * math.sin(i * 2 * math.pi / sides), ey)) for i in range(sides)]
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((v0[i], v0[j], v1[j], v1[i]))
        bm.faces.new(v1)
        finish("CherryBranch", bm, BARK)
        for c in range(3):
            bm = bmesh.new()
            rad = random.uniform(0.8, 1.25) * s
            bmesh.ops.create_icosphere(bm, subdivisions=1, radius=rad)
            ox = ex + random.uniform(-0.6, 0.6) * s
            oz = ez + random.uniform(-0.6, 0.6) * s
            oy = ey + random.uniform(-0.2, 0.5) * s
            c0 = P(ox, oz, oy)
            for v in bm.verts:
                v.co = (c0[0] + v.co.x * random.uniform(0.9, 1.2), c0[1] + v.co.y * random.uniform(0.9, 1.2), c0[2] + v.co.z * 0.75)
            finish("CherryBlossom", bm, BLOSSOM)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.5 * s)
    c0 = P(px, pz, y + 1.3 * s)
    for v in bm.verts:
        v.co = (c0[0] + v.co.x, c0[1] + v.co.y, c0[2] + v.co.z * 0.7)
    finish("CherryBlossom", bm, BLOSSOM)


# ------------------------------------------------------------------ environmental storytelling
def bamboo_grove(x, z, count=12, s=1.0):
    """Procedural bamboo, safely outside the 9m combat radius, for depth and wind movement."""
    for i in range(count):
        a = random.random() * 2 * math.pi
        r = random.uniform(0.2, 2.3) * s
        px, pz = x + math.cos(a) * r, z + math.sin(a) * r
        h = random.uniform(4.0, 6.8) * s
        rad = random.uniform(0.075, 0.12) * s
        # segmented culm reads much better than a single green cylinder in silhouette
        pieces = max(3, int(h / 1.1))
        for n in range(pieces):
            seg = h / pieces - 0.035
            cyl("BambooCulm", (px, n * h / pieces, pz), rad * (1 - n * 0.035), seg, BAMBOO, sides=7)
        for n in range(2, pieces):
            ay = n * h / pieces
            for side in (-1, 1):
                # leaf clusters, angled out of the culm; their low-poly shape catches the sunset
                bm = bmesh.new()
                bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.34 * s)
                ox = px + side * (0.30 + n * 0.025) * s
                oz = pz + random.uniform(-0.22, 0.22) * s
                c0 = P(ox, oz, ay + 0.28 * s)
                for v in bm.verts:
                    v.co = (c0[0] + v.co.x * 1.55, c0[1] + v.co.y * 0.42, c0[2] + v.co.z * 0.25)
                finish("BambooLeaves", bm, BAMBOO)


def koi_pond(x, z, s=1.0):
    """A shallow side-garden pond: reflection, mossy stones and lily pads make the arena feel lived in."""
    box("PondRim", (x, 0.08, z), (5.6 * s, 0.18, 3.7 * s), STONE, taper=(0.9, 0.9))
    box("PondWater", (x, 0.19, z), (4.95 * s, 0.035, 3.05 * s), WATER)
    for i in range(15):
        a = random.random() * 2 * math.pi
        r = random.uniform(1.4, 2.8) * s
        px, pz = x + math.cos(a) * r, z + math.sin(a) * r * 0.58
        cyl("PondStone", (px, 0.18, pz), random.uniform(0.18, 0.36) * s, random.uniform(0.1, 0.22) * s, STONE, sides=7)
        if i % 2 == 0:
            cyl("PondMoss", (px, 0.38, pz), random.uniform(0.11, 0.2) * s, 0.015, MOSS, sides=8)
    for i in range(7):
        px, pz = x + random.uniform(-1.8, 1.8) * s, z + random.uniform(-0.85, 0.85) * s
        cyl("LilyPad", (px, 0.23, pz), random.uniform(0.16, 0.27) * s, 0.015, MOSS, sides=10)


def banner_row(x, z, n=5, spacing=1.15):
    """Ritual banners add a strong vertical rhythm around the hall without obstructing play."""
    for i in range(n):
        px = x + (i - (n - 1) / 2) * spacing
        cyl("BannerPole", (px, 0, z), 0.055, 3.2, DARK, sides=6)
        color = CLOTH if i % 2 == 0 else GOLD
        box("PrayerBanner", (px, 2.15, z - 0.06), (0.58, 1.42, 0.035), color)
        box("BannerTop", (px, 2.9, z), (0.72, 0.07, 0.1), GOLD)


# ---------------------------------------------------------------- layout
main_hall(19.0)
pagoda(-19.0, 26.0)
torii(-17.5)
# courtyard enclosure: side walls, back walls flanking the hall, front walls flanking the torii
wall(-16, -17, -16, 16)
wall(16, -17, 16, 16)
wall(-16, 16, -11, 16)
wall(11, 16, 16, 16)
wall(-16, -17, -5, -17)
wall(5, -17, 16, -17)
for (x, z) in [(-11, 11), (11, 11), (-11, -11), (11, -11), (-5.5, 12.8), (5.5, 12.8), (-6.5, -15), (6.5, -15)]:
    lantern(x, z, 1.1 if abs(z) > 12 else 1.0)
for (x, z, s) in [(-13.2, 13.0, 1.15), (13.4, 12.6, 1.0), (-13.5, -13.5, 1.05), (13.0, -14.0, 1.2), (-13.8, 0.5, 0.9), (13.8, -0.5, 0.95)]:
    cherry(x, z, s)
# The gardens sit beside the ring, so they enrich the camera parallax without changing combat collision.
bamboo_grove(-14.2, 4.5, 15, 1.0)
bamboo_grove(14.0, -7.0, 12, 0.9)
koi_pond(12.4, 4.8, 0.72)
banner_row(0, 15.1, 7, 1.15)
# distant hills to close the horizon beyond the walls
for i in range(14):
    a = i / 14 * 2 * math.pi
    R = random.uniform(60, 75)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1)
    c0 = P(math.cos(a) * R, math.sin(a) * R, -2)
    sx, sy, sz = random.uniform(18, 30), random.uniform(7, 14), random.uniform(18, 30)
    for v in bm.verts:
        v.co = (c0[0] + v.co.x * sx, c0[1] + v.co.y * sz, c0[2] + v.co.z * sy)
    finish("Hill", bm, mat("Hills", (0.22, 0.2, 0.28), 1.0))

# merge everything that shares a material into one object to keep draw calls low
by_mat = {}
for ob in list(bpy.data.objects):
    if ob.type == "MESH":
        by_mat.setdefault(ob.data.materials[0].name, []).append(ob)
for name, obs in by_mat.items():
    bpy.ops.object.select_all(action="DESELECT")
    for ob in obs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    obs[0].name = name
    obs[0].data.name = name

bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', export_yup=True, export_materials='EXPORT')
print("COURTYARD exported", OUT, len(bpy.data.objects), "objects")

import sys
if "--" in sys.argv:
    out = sys.argv[sys.argv.index("--") + 1]
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    world = bpy.data.worlds.new("w"); world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.9, 0.55, 0.35, 1)
    scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sun.data.energy = 3
    sun.rotation_euler = (math.radians(70), 0, math.radians(200)); scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); cam.data.lens = 24
    scene.collection.objects.link(cam); scene.camera = cam
    for tag, loc, rot in (("north", P(3, -9, 2.2), (math.radians(84), 0, math.radians(190))),
                          ("south", P(-3, 9, 2.2), (math.radians(84), 0, math.radians(10)))):
        cam.location = loc; cam.rotation_euler = rot
        scene.render.filepath = out.replace(".png", "_" + tag + ".png")
        bpy.ops.render.render(write_still=True)
        print("PREVIEW", scene.render.filepath)
