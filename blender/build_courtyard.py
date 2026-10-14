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
BLOSSOM_CARD = mat("BlossomCard", (1.0, 0.72, 0.82), 0.8)
GLOW = mat("LanternGlow", (1.0, 0.7, 0.35), 0.5, emit=(1.0, 0.55, 0.2))
GOLD = mat("Gold", (0.85, 0.62, 0.2), 0.3, 0.9)
MOSS = mat("Moss", (0.16, 0.28, 0.12), 0.95)
WATER = mat("Water", (0.04, 0.18, 0.22), 0.12, 0.25, emit=(0.015, 0.05, 0.06))
CLOTH = mat("Cloth", (0.62, 0.1, 0.08), 0.75)
BAMBOO = mat("Bamboo", (0.23, 0.42, 0.14), 0.62)
TATAMI = mat("Tatami", (0.66, 0.62, 0.4), 0.9)
PAPER = mat("Paper", (0.95, 0.92, 0.84), 0.95)

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


def finish(name, bm, material, parent=None, uv=True):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if uv:
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
    n = max(1, int(L / 3.5))
    for i in range(n + 1):
        t = i / n
        px, pz = x0 + (x1 - x0) * t, z0 + (z1 - z0) * t
        box("WallPost", (px, 0.6 + (h - 0.6) / 2, pz), (0.7, h - 0.6, 0.3), WOOD, rot_y=yaw)
    foot(cx, cz, 1.0, L + 0.4, yaw, h + 0.8, top=h + 0.6)


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
                v.co = (c0[0] + v.co.x * random.uniform(0.9, 1.2) * 0.62, c0[1] + v.co.y * random.uniform(0.9, 1.2) * 0.62, c0[2] + v.co.z * 0.75 * 0.62)
            finish("CherryBlossom", bm, BLOSSOM)
            blossom_cards(ox, oz, oy, rad, 30)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.5 * s * 0.65)
    c0 = P(px, pz, y + 1.3 * s)
    for v in bm.verts:
        v.co = (c0[0] + v.co.x, c0[1] + v.co.y, c0[2] + v.co.z * 0.7)
    finish("CherryBlossom", bm, BLOSSOM)
    blossom_cards(px, pz, y + 1.3 * s, 1.5 * s, 46)


def blossom_cards(cx, cz, cy, rad, n):
    """Random quads through a blossom cloud, each showing a cluster of petals (alpha-cut in Unity)."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    for i in range(n):
        # a point in a squashed sphere, biased to the surface so the canopy reads full
        while True:
            d = (random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))
            m = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
            if 0.2 < m <= 1:
                break
        k = (0.55 + 0.45 * m) / m
        px, py, pz = cx + d[0] * rad * k * 1.05, cy + d[1] * rad * k * 0.7, cz + d[2] * rad * k * 1.05
        size = random.uniform(0.55, 0.95) * rad * 0.75
        # a random orientation, tilted mostly outward
        a, b = random.uniform(0, 2 * math.pi), random.uniform(-0.9, 0.9)
        ux, uy, uz = math.cos(a), b, math.sin(a)
        l = math.sqrt(ux * ux + uy * uy + uz * uz); ux, uy, uz = ux / l, uy / l, uz / l
        # two in-plane axes
        vx, vy, vz = -uz, 0.0, ux
        l = math.sqrt(vx * vx + vz * vz) or 1; vx, vz = vx / l, vz / l
        wx, wy, wz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        corners = []
        for (su, sv) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            corners.append(bm.verts.new(P(px + (vx * su + wx * sv) * size * 0.5, pz + (vz * su + wz * sv) * size * 0.5, py + (vy * su + wy * sv) * size * 0.5)))
        f = bm.faces.new(corners)
        for loop, uv in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uvl].uv = uv
    finish("BlossomCard", bm, BLOSSOM_CARD, uv=False)


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


# ---------------------------------------------------------------- temple town (boss chase area)
# Everything solid also records its footprint (centre x/z, size x/z, yaw, height) so Unity can
# block Kaito, the camera and Moros's hunting swarms with it (Assets/Courtyard/Footprints.json).
FOOT = []
CAM = []    # camera / sight blockers you can walk under (tree canopies)
ROOFS = []  # climbable roofs: x, z, half-width, half-depth (incl. overhang), eave y, rise, ridge half-length, ridge along x
ROOMS = []  # enterable interiors: name, x, z, half-width, half-depth, ceiling height
CEIL = []   # interior ceilings (camera / sight blockers): x, z, size x, size z, y0, y1
LIGHTS = [] # interior lamps: x, y, z, range, intensity


def foot(x, z, sx, sz, yaw=0.0, h=3.0, roof=-1, top=None):
    """roof: index of the roof you can run on; top: height of a flat top you can stand on (walls, crates)."""
    FOOT.append([round(x, 3), round(z, 3), round(sx, 3), round(sz, 3), round(yaw, 2), round(h, 2), roof, -1 if top is None else round(top, 2)])


def roof_entry(x, z, w, d, eave, rise, overhang, ridge_frac):
    ROOFS.append([round(x, 3), round(z, 3), round(w / 2 + overhang, 3), round(d / 2 + overhang, 3), round(eave, 3), round(rise, 3),
                  round(max(w, d) * ridge_frac / 2, 3), 1 if w >= d else 0])
    return len(ROOFS) - 1


def canopy(x, z, s=1.0):
    CAM.append([round(x, 3), round(z, 3), round(5.6 * s, 3), round(2.2 * s, 3), round(5.2 * s, 3)])


def house(x, z, w, d, storeys=1, face=-1):
    """Machiya townhouse. face = +1 street to the north (+z), -1 street to the south (-z)."""
    h1 = 3.4
    box("HouseBase", (x, 0.25, z), (w + 0.3, 0.5, d + 0.3), STONE)
    box("HouseWall", (x, 0.5 + h1 / 2, z), (w, h1, d), PLASTER)
    for sx in (-1, 1):
        for sz in (-1, 1):
            cyl("HousePost", (x + sx * (w / 2 - 0.1), 0.5, z + sz * (d / 2 - 0.1)), 0.15, h1, DARK, sides=6)
    box("HouseBand", (x, 0.5 + h1 - 0.1, z), (w + 0.1, 0.2, d + 0.1), DARK)
    fz = z + face * (d / 2 + 0.03)
    n = max(3, int(w / 1.2))
    for i in range(n):  # koshi lattice on the street facade
        px = x - w / 2 + 0.6 + i * (w - 1.2) / (n - 1)
        box("Koshi", (px, 0.5 + 1.3, fz), (0.08, 2.0, 0.06), DARK)
    box("KoshiRail", (x, 0.5 + 2.35, fz), (w - 0.8, 0.1, 0.08), DARK)
    box("Noren", (x + w * 0.2, 0.5 + 2.4, fz + face * 0.05), (1.4, 0.8, 0.04), CLOTH)
    top = 0.5 + h1
    if storeys == 2:
        roof("HouseSkirt", (x, z), top, w, d, 0.5, overhang=0.8, upturn=0.15, thick=0.18, ridge_frac=0.8)
        box("HouseUpper", (x, top + 0.45 + 1.1, z), (w * 0.9, 2.2, d * 0.85), WOOD)
        box("MushiWindow", (x, top + 0.45 + 1.2, z + face * (d * 0.425 + 0.03)), (w * 0.6, 0.7, 0.06), DARK)
        top += 0.45 + 2.2
    rw, rd = w * (0.9 if storeys == 2 else 1), d * (0.85 if storeys == 2 else 1)
    roof("HouseRoof", (x, z), top, rw, rd, 2.0, overhang=0.9, upturn=0.3, thick=0.22, ridge_frac=0.6)
    foot(x, z, w + 0.5, d + 0.5, 0, top + 2.0, roof=roof_entry(x, z, rw, rd, top, 2.0, 0.9, 0.6))


def kura(x, z, w, d, face=-1):
    """White-walled storehouse with a dark tiled base and a heavy black roof."""
    h = 4.4
    box("KuraBase", (x, 0.3, z), (w + 0.4, 0.6, d + 0.4), STONE)
    box("KuraWall", (x, 0.6 + h / 2, z), (w, h, d), PLASTER)
    box("KuraNamako", (x, 0.6 + 0.65, z), (w + 0.06, 1.3, d + 0.06), ROOF)
    fz = z + face * (d / 2 + 0.05)
    box("KuraDoor", (x, 0.6 + 1.3, fz), (1.6, 2.4, 0.12), DARK)
    for sx in (-1, 1):
        box("KuraWindow", (x + sx * w * 0.3, 0.6 + 3.4, fz), (0.7, 0.6, 0.1), DARK)
    box("KuraEave", (x, 0.6 + h, z), (w + 0.3, 0.25, d + 0.3), DARK)
    roof("KuraRoof", (x, z), 0.6 + h + 0.1, w, d, 1.7, overhang=0.7, upturn=0.12, thick=0.3, ridge_frac=0.7)
    foot(x, z, w + 0.6, d + 0.6, 0, h + 2.6, roof=roof_entry(x, z, w, d, 0.6 + h + 0.1, 1.7, 0.7, 0.7))


def stall(x, z, w=2.6, d=1.4):
    for sx in (-1, 1):
        for sz in (-1, 1):
            cyl("StallPost", (x + sx * w / 2, 0, z + sz * d / 2), 0.07, 2.3, WOOD, sides=6)
    box("StallCounter", (x, 0.45, z), (w, 0.9, d * 0.6), WOOD)
    box("StallCanopy", (x, 2.35, z), (w + 0.5, 0.08, d + 0.6), CLOTH)
    for i in range(4):
        box("StallGoods", (x - w / 2 + 0.4 + i * (w - 0.8) / 3, 0.98, z), (0.4, 0.18, 0.35), GOLD if i % 2 else MOSS)
    foot(x, z, w + 0.3, d + 0.3, 0, 2.4, top=2.43)


def well(x, z):
    cyl("WellRing", (x, 0, z), 0.95, 0.8, STONE, sides=10)
    cyl("WellWater", (x, 0.55, z), 0.8, 0.1, WATER, sides=10)
    for sx in (-1, 1):
        cyl("WellPost", (x + sx * 0.95, 0, z), 0.08, 2.4, WOOD, sides=6)
    box("WellRoof", (x, 2.5, z), (2.4, 0.12, 1.5), ROOF)
    foot(x, z, 2.2, 2.2, 0, 2.5, top=2.56)


def bell_tower(x, z):
    box("ShoroBase", (x, 0.3, z), (4.4, 0.6, 4.4), STONE)
    for sx in (-1, 1):
        for sz in (-1, 1):
            cyl("ShoroPost", (x + sx * 1.6, 0.6, z + sz * 1.6), 0.18, 3.8, RED, sides=8)
    box("ShoroBeam", (x, 4.3, z), (3.8, 0.3, 3.8), RED)
    roof("ShoroRoof", (x, z), 4.45, 3.4, 3.4, 1.7, overhang=1.1, upturn=0.5, thick=0.25, ridge_frac=0.0)
    cyl("Bell", (x, 2.2, z), 0.55, 1.4, GOLD, sides=12, top_radius=0.42)
    foot(x, z, 4.6, 4.6, 0, 6.0, roof=roof_entry(x, z, 3.4, 3.4, 4.45, 1.7, 1.1, 0.0))


PROPS = []  # Poly Haven models placed by Unity: kind, x, z, yaw, size


def crates(x, z, n=3):
    """A stack of crates or a group of barrels (Poly Haven models, placed by Unity)."""
    PROPS.append(["crates" if len(PROPS) % 2 == 0 else "barrels", round(x, 3), round(z, 3), round(random.uniform(0, 360), 1), 2.0])
    foot(x, z, 2.0, 2.0, 0, 2.0, top=2.0)


def side_gate(x, z0, z1):
    """A red gate frame where a courtyard wall opens into the town."""
    for zz in (z0, z1):
        cyl("GatePillar", (x, 0, zz), 0.3, 4.6, RED, sides=10)
        foot(x, zz, 0.8, 0.8, 0, 4.6)
    box("GateLintel", (x, 4.3, (z0 + z1) / 2), (0.5, 0.35, abs(z1 - z0) + 0.8), RED)
    box("GateRoof", (x, 4.75, (z0 + z1) / 2), (1.6, 0.35, abs(z1 - z0) + 1.6), ROOF, taper=(0.2, 1.0))


def small_torii(z, half=2.6):
    for sx in (-1, 1):
        cyl("ToriiPillar", (sx * half, 0, z), 0.3, 5.4, RED, sides=10, top_radius=0.26)
        foot(sx * half, z, 0.8, 0.8, 0, 5.4)
    box("ToriiNuki", (0, 4.3, z), (half * 2 + 1.2, 0.35, 0.3), RED)
    box("ToriiKasagi", (0, 5.55, z), (half * 2 + 2.4, 0.4, 0.6), DARK)


def inner_wall(name, ax, az, bx, bz, h, t, material, trim, windows=True):
    L = math.hypot(bx - ax, bz - az)
    if L < 0.05:
        return
    yaw = math.degrees(math.atan2(bx - ax, bz - az))
    cx, cz = (ax + bx) / 2, (az + bz) / 2
    box(name + "Wall", (cx, h / 2, cz), (t, h, L), material, rot_y=yaw)
    box(name + "WallSkirt", (cx, 0.45, cz), (t + 0.06, 0.9, L), trim, rot_y=yaw)
    box(name + "WallBand", (cx, h - 0.25, cz), (t + 0.06, 0.18, L), trim, rot_y=yaw)
    for (px, pz) in ((ax, az), (bx, bz)):
        box(name + "Post", (px, h / 2, pz), (t + 0.12, h, 0.3), trim, rot_y=yaw)
    if windows and L > 2.6:
        n = int(L / 3.2)
        for i in range(n):
            f = (i + 0.5) / n
            box(name + "Window", (ax + (bx - ax) * f, h - 1.3, az + (bz - az) * f), (t + 0.08, 0.8, 1.1), DARK, rot_y=yaw)
    foot(cx, cz, t + 0.1, L, yaw, h, top=None)


def interior(name, cx, cz, w, d, eave, rise, doors, wall_mat=PLASTER, trim=DARK, floor_mat=WOOD):
    """An enterable hall: outer walls with doorways, a floor, a ceiling and a climbable roof.
    doors = [(side, offset, width)], side n/s/e/w (n = +z); offset runs from the side's west/south end."""
    t = 0.35
    box(name + "Floor", (cx, 0.03, cz), (w, 0.06, d), floor_mat)
    sides = {
        's': (cx - w / 2, cz - d / 2, cx + w / 2, cz - d / 2),
        'n': (cx - w / 2, cz + d / 2, cx + w / 2, cz + d / 2),
        'w': (cx - w / 2, cz - d / 2, cx - w / 2, cz + d / 2),
        'e': (cx + w / 2, cz - d / 2, cx + w / 2, cz + d / 2),
    }
    for side, (x0, z0, x1, z1) in sides.items():
        L = math.hypot(x1 - x0, z1 - z0)
        gaps = sorted([(o - dw / 2, o + dw / 2) for (sd, o, dw) in doors if sd == side])
        at, segs = 0.0, []
        for (g0, g1) in gaps:
            if g0 > at:
                segs.append((at, g0))
            at = g1
        if at < L:
            segs.append((at, L))
        for (a, b) in segs:
            inner_wall(name, x0 + (x1 - x0) * a / L, z0 + (z1 - z0) * a / L, x0 + (x1 - x0) * b / L, z0 + (z1 - z0) * b / L, eave, t, wall_mat, trim)
        yaw = math.degrees(math.atan2(x1 - x0, z1 - z0))
        for (g0, g1) in gaps:
            mx, mz = x0 + (x1 - x0) * (g0 + g1) / 2 / L, z0 + (z1 - z0) * (g0 + g1) / 2 / L
            box(name + "Lintel", (mx, 3.2 + (eave - 3.2) / 2, mz), (t, eave - 3.2, g1 - g0), wall_mat, rot_y=yaw)
            box(name + "DoorBeam", (mx, 3.2, mz), (t + 0.16, 0.24, g1 - g0 + 0.3), trim, rot_y=yaw)
            box(name + "DoorCurtain", (mx, 2.75, mz), (t + 0.2, 0.7, g1 - g0 - 0.2), CLOTH, rot_y=yaw)
    box(name + "Ceiling", (cx, eave + 0.1, cz), (w + 0.2, 0.2, d + 0.2), DARK)
    for k in range(int(w / 3) + 1):   # exposed ceiling beams
        box(name + "Beam", (cx - w / 2 + 0.3 + k * (w - 0.6) / max(1, int(w / 3)), eave - 0.2, cz), (0.3, 0.35, d), WOOD)
    box(name + "Eave", (cx, eave + 0.35, cz), (w + 0.5, 0.3, d + 0.5), DARK)
    roof(name + "Roof", (cx, cz), eave + 0.5, w, d, rise, overhang=1.0, upturn=0.35, thick=0.3, ridge_frac=0.5)
    ri = roof_entry(cx, cz, w, d, eave + 0.5, rise, 1.0, 0.5)
    CEIL.append([round(cx, 3), round(cz, 3), round(w + 0.2, 3), round(d + 0.2, 3), round(eave, 3), round(eave + 0.45, 3)])
    ROOMS.append([name, round(cx, 3), round(cz, 3), round(w / 2, 3), round(d / 2, 3), round(eave, 3)])
    return ri


def hanging_lamp(x, z, y, rng=9.0, inten=2.2):
    box("LampCord", (x, y + 0.8, z), (0.04, 1.2, 0.04), DARK)
    box("LampPaper", (x, y, z), (0.55, 0.75, 0.55), GLOW, taper=(0.8, 0.8))
    LIGHTS.append([round(x, 3), round(y, 3), round(z, 3), rng, inten])


def grand_dojo(cx, cz):
    """GRAND DOJO: a long training hall with two rows of red pillars, tatami, weapon racks and an altar."""
    w, d, eave = 26.0, 16.0, 6.5
    box("DojoPlinth", (cx, 0.15, cz), (w + 1.6, 0.3, d + 1.6), STONE)
    interior("Dojo", cx, cz, w, d, eave, 4.2, [('s', w / 2, 5.0), ('n', w / 2, 3.0), ('e', d / 2, 3.0), ('w', d / 2, 3.0)])
    for row in (-1, 1):
        for k in range(5):
            px, pz = cx - w / 2 + 3.6 + k * (w - 7.2) / 4, cz + row * d / 4
            cyl("DojoPillar", (px, 0, pz), 0.34, eave, RED, sides=10)
            cyl("DojoPillarFoot", (px, 0, pz), 0.48, 0.35, STONE, sides=8)
            foot(px, pz, 0.8, 0.8, 0, eave)
    for i in range(6):
        for j in range(3):
            box("Tatami", (cx - 7.5 + i * 3.0, 0.08, cz - 2.2 + j * 2.2), (2.9, 0.04, 2.1), TATAMI)
    for rx in (cx - 10.0, cx - 5.5, cx + 5.5, cx + 10.0):   # weapon racks along the north wall
        box("RackFrame", (rx, 1.1, cz + d / 2 - 0.7), (3.0, 0.12, 0.3), DARK)
        box("RackFrame", (rx, 0.4, cz + d / 2 - 0.7), (3.0, 0.12, 0.3), DARK)
        for q in range(7):
            cyl("RackSpear", (rx - 1.3 + q * 0.43, 0.1, cz + d / 2 - 0.7), 0.035, 2.4, WOOD, sides=5)
        foot(rx, cz + d / 2 - 0.7, 3.2, 0.6, 0, 2.4)
    box("AltarBase", (cx, 0.5, cz + d / 2 - 2.2), (5.0, 1.0, 2.2), DARK)
    box("AltarTop", (cx, 1.25, cz + d / 2 - 2.2), (3.2, 0.5, 1.4), RED)
    box("AltarGold", (cx, 1.9, cz + d / 2 - 2.3), (1.0, 0.8, 0.5), GOLD, taper=(0.6, 0.6))
    foot(cx, cz + d / 2 - 2.2, 5.2, 2.4, 0, 2.3, top=1.5)
    for k in range(4):   # banners on the side walls
        for sx in (-1, 1):
            box("DojoBanner", (cx + sx * (w / 2 - 0.25), 3.6, cz - d / 2 + 2.5 + k * 3.6), (0.05, 2.4, 1.0), CLOTH if k % 2 == 0 else GOLD)
    for (lx, lz) in ((-7, -3.5), (7, -3.5), (-7, 3.5), (7, 3.5), (0, 0)):
        hanging_lamp(cx + lx, cz + lz, eave - 1.6, 11.0, 2.6)


def sake_brewery(cx, cz):
    """SAKE BREWERY: a long timber warehouse full of giant vats and barrel stacks to hide behind."""
    w, d, eave = 22.0, 10.0, 5.6
    interior("Brewery", cx, cz, w, d, eave, 3.0, [('w', d / 2, 4.0), ('e', d / 2, 3.0), ('n', 6.0, 2.6), ('s', 15.0, 2.6)], wall_mat=WOOD, trim=DARK)
    for k in range(4):   # giant sake vats
        vx = cx - 7.5 + k * 5.0
        vz = cz + (2.2 if k % 2 == 0 else -2.2)
        cyl("Vat", (vx, 0, vz), 1.45, 3.1, WOOD, sides=12)
        for hh in (0.5, 1.5, 2.6):
            cyl("VatHoop", (vx, hh, vz), 1.5, 0.12, DARK, sides=12)
        foot(vx, vz, 3.0, 3.0, 0, 3.1, top=3.12)
    for k in range(5):   # barrel stacks
        bx, bz = cx - 9 + k * 4.5, cz + (-3.8 if k % 2 == 0 else 3.8)
        for q in range(3):
            cyl("Barrel", (bx - 0.8 + q * 0.8, 0, bz), 0.38, 0.95, WOOD, sides=10)
        cyl("Barrel", (bx - 0.4, 0.95, bz), 0.38, 0.95, WOOD, sides=10)
        foot(bx, bz, 2.5, 0.9, 0, 1.9, top=1.9)
    for (lx, lz) in ((-6, 0), (0, 0), (6, 0)):
        hanging_lamp(cx + lx, cz + lz, eave - 1.4, 9.0, 2.2)


def tea_house(cx, cz):
    """TEA HOUSE: a ryokan of paper-walled rooms off a central corridor - rooms to hide in."""
    w, d, eave = 20.0, 11.0, 4.2
    interior("TeaHouse", cx, cz, w, d, eave, 2.6, [('e', d / 2, 2.2), ('w', d / 2, 2.2), ('n', 7.5, 1.8), ('s', 12.5, 1.8)], wall_mat=PLASTER, trim=DARK)
    ph = 2.7
    for zside in (-1, 1):   # shoji walls along the corridor, one doorway per room
        z = cz + zside * 1.3
        rooms = 4
        rw = w / rooms
        for r in range(rooms):
            x0, x1 = cx - w / 2 + r * rw, cx - w / 2 + (r + 1) * rw
            gap0, gap1 = x0 + rw * 0.35, x0 + rw * 0.35 + 1.3
            for (a, b) in ((x0, gap0), (gap1, x1)):
                if b - a > 0.1:
                    inner_wall("Shoji", a, z, b, z, ph, 0.14, PAPER, WOOD, windows=False)
            if r > 0:   # room divider
                inner_wall("Shoji", x0, z, x0, cz + zside * (d / 2), ph, 0.14, PAPER, WOOD, windows=False)
            box("LowTable", ((x0 + x1) / 2, 0.22, z + zside * 2.2), (1.4, 0.4, 0.9), DARK)
            foot((x0 + x1) / 2, z + zside * 2.2, 1.5, 1.0, 0, 0.45)
            for i in range(2):
                box("RoomTatami", ((x0 + x1) / 2 + (i - 0.5) * 2.2, 0.08, z + zside * 2.1), (2.0, 0.04, 2.6), TATAMI)
            hanging_lamp((x0 + x1) / 2, z + zside * 2.1, eave - 1.2, 6.0, 1.5)


def town():
    # east market street (z -6..6), houses both sides with alleys between them and a back lane
    for (x, st) in [(21.0, 2), (29.5, 1), (38.0, 2)]:
        house(x, 10.0, 7.0, 7.0, st, face=-1)
    for (x, st) in [(21.5, 1), (30.0, 2), (38.5, 1)]:
        house(x, -10.0, 7.0, 7.0, st, face=1)
    for (x, z) in [(24.5, 4.9), (33.5, 4.9), (26.0, -4.9), (35.0, -4.9)]:
        stall(x, z)
    well(41.0, 0.5)
    for (x, z) in [(25.2, 12.6), (34.0, -12.8)]:
        cherry(x, z, 0.8)
        foot(x, z, 0.9, 0.9, 0, 3)
        canopy(x, z, 0.8)
    for (x, z) in [(18.6, 5.0), (18.6, -5.0), (43.0, 5.2), (43.0, -5.2)]:
        lantern(x, z, 0.95)
        foot(x, z, 1.1, 1.1, 0, 2.6)
    # west storehouse quarter
    for (x, w) in [(-22.0, 6.0), (-30.5, 6.0), (-39.0, 6.0)]:
        kura(x, 10.0, w, 6.0, face=-1)
        kura(x, -10.0, w, 6.0, face=1)
    bell_tower(-33.0, 0.0)
    for (x, z) in [(-26.0, 4.2), (-27.0, -4.6), (-42.0, 3.4), (-20.0, -4.0)]:
        crates(x, z)
    for (x, z) in [(-18.6, 5.0), (-18.6, -5.0), (-43.0, 5.2), (-43.0, -5.2)]:
        lantern(x, z, 0.95)
        foot(x, z, 1.1, 1.1, 0, 2.6)
    # south approach avenue beyond the torii, lined with lanterns, ending at a second gate
    for z in (-22.0, -28.0, -34.0, -40.0):
        for sx in (-1, 1):
            lantern(sx * 4.2, z, 1.0)
            foot(sx * 4.2, z, 1.1, 1.1, 0, 2.6)
    house(-9.5, -26.0, 7.0, 6.0, 1, face=1)
    house(9.5, -26.0, 7.0, 6.0, 2, face=1)
    kura(-9.5, -38.0, 6.0, 6.0, face=1)
    kura(9.5, -38.0, 6.0, 6.0, face=1)
    for sx in (-1, 1):
        cherry(sx * 9.5, -32.0, 0.8)
        foot(sx * 9.5, -32.0, 0.9, 0.9, 0, 3)
        canopy(sx * 9.5, -32.0, 0.8)
    small_torii(-44.5)
    # town walls enclosing the three districts
    wall(16, 16, 28, 16)
    wall(33, 16, 45, 16)
    wall(16, -16, 45, -16)
    wall(45, -16, 45, -3.6)
    wall(45, 3.6, 45, 16)
    side_gate(45, -3.6, 3.6)
    wall(-16, 16, -28, 16)
    wall(-33, 16, -45, 16)
    wall(-16, -16, -45, -16)
    wall(-45, -16, -45, -3.6)
    wall(-45, 3.6, -45, 16)
    side_gate(-45, -3.6, 3.6)
    far_east()
    far_west()
    north_heights()
    wall(-14, -17, -14, -46)
    wall(14, -17, 14, -46)
    wall(-14, -46, -3.6, -46)
    wall(3.6, -46, 14, -46)
    wall(-3.6, -46, 3.6, -46)


def far_east():
    """Brewery quarter beyond the east gate (x 45..80)."""
    sake_brewery(63.0, 0.0)
    for (x, st) in [(50.5, 1), (58.5, 2), (66.5, 1), (74.5, 2)]:
        house(x, 12.3, 6.0, 5.4, st, face=-1)
    for (x, kind) in [(50.5, 0), (58.5, 1), (66.5, 0), (74.5, 1)]:
        if kind:
            house(x, -12.3, 6.0, 5.4, 2, face=1)
        else:
            kura(x, -12.3, 5.4, 5.0, face=1)
    for (x, z) in [(48.5, 6.6), (48.5, -6.6), (77.5, 6.6), (77.5, -6.6)]:
        lantern(x, z, 0.95)
        foot(x, z, 1.1, 1.1, 0, 2.6)
    for (x, z) in [(55.0, -7.4), (71.0, 7.4)]:
        crates(x, z)
    wall(45, 16, 80, 16)
    wall(45, -16, 80, -16)
    wall(80, -16, 80, 16)


def far_west():
    """Bamboo and tea-house quarter beyond the west gate (x -80..-45)."""
    tea_house(-63.0, 0.0)
    for (x, z) in [(-50.0, 12.0), (-76.0, 12.0), (-50.0, -12.0), (-76.0, -12.0)]:
        bamboo_grove(x, z, 12, 0.9)
        foot(x, z, 4.6, 4.6, 0, 6.0)
    for x in (-58.5, -67.5):
        kura(x, 12.4, 5.0, 5.0, face=-1)
        house(x, -12.4, 6.0, 5.2, 2 if x < -60 else 1, face=1)
    for (x, z) in [(-48.5, 7.0), (-48.5, -7.0), (-77.5, 7.0), (-77.5, -7.0)]:
        lantern(x, z, 0.95)
        foot(x, z, 1.1, 1.1, 0, 2.6)
    wall(-45, 16, -80, 16)
    wall(-45, -16, -80, -16)
    wall(-80, -16, -80, 16)


def north_heights():
    """Temple heights behind the main hall (z 16..64): the Grand Dojo, the pagoda plaza, a rock garden."""
    grand_dojo(0.0, 46.0)
    foot(-19.0, 26.0, 7.4, 7.4, 0, 16.0)   # the pagoda now stands inside the district
    for (x, z) in [(-7.0, 30.0), (7.0, 30.0), (-7.0, 35.5), (7.0, 35.5), (-11.0, 22.0), (11.0, 22.0)]:
        lantern(x, z, 1.0)
        foot(x, z, 1.1, 1.1, 0, 2.6)
    # rock garden
    box("Gravel", (27.0, 0.03, 32.0), (12.0, 0.06, 9.0), mat("Gravel", (0.78, 0.76, 0.7), 0.95))
    for (x, z, r) in [(24.0, 30.0, 1.2), (29.5, 33.5, 0.9), (31.0, 29.5, 0.7)]:
        PROPS.append(["boulder", x, z, round(random.uniform(0, 360), 1), r * 2.2])
        foot(x, z, r * 2, r * 2, 0, r * 1.4, top=r * 1.4)
    for (x, z) in [(-30.0, 50.0), (30.0, 48.0), (-9.0, 28.5), (21.0, 24.0)]:
        cherry(x, z, 0.9)
        foot(x, z, 1.0, 1.0, 0, 3.0)
        canopy(x, z, 0.9)
    for (x, st) in [(-36.0, 2), (-26.5, 1), (26.5, 1), (36.0, 2)]:
        house(x, 59.0, 7.0, 6.0, st, face=-1)
    kura(37.5, 38.0, 6.0, 6.0, face=-1)
    kura(-37.5, 38.0, 6.0, 6.0, face=-1)
    bell_tower(-34.0, 26.0)
    crates(20.0, 56.0)
    crates(-18.0, 57.0)
    wall(-45, 16, -45, 64)
    wall(45, 16, 45, 64)
    wall(-45, 64, 45, 64)


# ---------------------------------------------------------------- layout
main_hall(19.0)
foot(0, 19.0, 20.4, 11.4, 0, 14)
foot(0, 12.1, 7.6, 2.6, 0, 1.2)       # hall steps
pagoda(-19.0, 26.0)
torii(-17.5)
for sx in (-1, 1):
    foot(sx * 3.2, -17.5, 1.1, 1.1, 0, 7.5)
# courtyard enclosure: side walls (with gates into the town), back walls flanking the hall,
# front walls flanking the torii
wall(-16, -17, -16, -3.4)
wall(-16, 3.4, -16, 16)
wall(16, -17, 16, -3.4)
wall(16, 3.4, 16, 16)
side_gate(-16, -3.4, 3.4)
side_gate(16, -3.4, 3.4)
# (the back walls beside the hall are gone: those gaps lead up to the temple heights)
wall(-16, -17, -5, -17)
wall(5, -17, 16, -17)
for (x, z) in [(-11, 11), (11, 11), (-11, -11), (11, -11), (-5.5, 12.8), (5.5, 12.8), (-6.5, -15), (6.5, -15)]:
    lantern(x, z, 1.1 if abs(z) > 12 else 1.0)
    foot(x, z, 1.2, 1.2, 0, 2.8)
for (x, z, s) in [(-13.2, 13.0, 1.15), (13.4, 12.6, 1.0), (-13.5, -13.5, 1.05), (13.0, -14.0, 1.2), (-13.8, 0.5, 0.9), (13.8, -0.5, 0.95)]:
    cherry(x, z, s)
    foot(x, z, 1.0, 1.0, 0, 3.0)
    canopy(x, z, s)
# The gardens sit beside the ring, so they enrich the camera parallax without changing combat collision.
bamboo_grove(-14.2, 4.5, 15, 1.0)
bamboo_grove(14.0, -7.0, 12, 0.9)
foot(-14.2, 4.5, 5.2, 5.2, 0, 7.0)
foot(14.0, -7.0, 4.8, 4.8, 0, 6.5)
koi_pond(12.4, 4.8, 0.72)
foot(12.4, 4.8, 4.2, 2.8, 0, 0.4)
town()
banner_row(0, 15.1, 7, 1.15)
# distant hills to close the horizon beyond the walls
for i in range(14):
    a = i / 14 * 2 * math.pi
    R = random.uniform(135, 155)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1)
    c0 = P(math.cos(a) * R, math.sin(a) * R, -2)
    sx, sy, sz = random.uniform(34, 55), random.uniform(14, 26), random.uniform(34, 55)
    for v in bm.verts:
        v.co = (c0[0] + v.co.x * sx, c0[1] + v.co.y * sz, c0[2] + v.co.z * sy)
    finish("Hill", bm, mat("Hills", (0.22, 0.2, 0.28), 1.0))

# merge objects that share a material AND a 24 m cell of the town: few draw calls, but the camera
# only draws the chunks it can see (one mesh per material spanning the whole town defeats culling)
CELL = 24.0
by_key = {}
for ob in list(bpy.data.objects):
    if ob.type != "MESH":
        continue
    mname = ob.data.materials[0].name
    if mname == "Hills":
        key = (mname, 0, 0)
    else:
        vs = ob.data.vertices
        cx = sum(v.co.x for v in vs) / max(1, len(vs))
        cy = sum(v.co.y for v in vs) / max(1, len(vs))
        key = (mname, math.floor(cx / CELL), math.floor(cy / CELL))
    by_key.setdefault(key, []).append(ob)
for (mname, gx, gy), obs in by_key.items():
    bpy.ops.object.select_all(action="DESELECT")
    for ob in obs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    name = mname if mname == "Hills" else f"{mname}_{gx}_{gy}"
    obs[0].name = name
    obs[0].data.name = name
print("CHUNKS", len(by_key))

bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', export_yup=True, export_materials='EXPORT')
import json
with open(os.path.join(os.path.dirname(OUT), "Footprints.json"), "w") as fp:
    json.dump({"items": [{"x": f[0], "z": f[1], "sx": f[2], "sz": f[3], "yaw": f[4], "h": f[5], "roof": f[6], "top": f[7]} for f in FOOT],
               "canopies": [{"x": c[0], "z": c[1], "size": c[2], "y0": c[3], "y1": c[4]} for c in CAM],
               "roofs": [{"x": r[0], "z": r[1], "hw": r[2], "hd": r[3], "eave": r[4], "rise": r[5], "ridge": r[6], "alongX": r[7]} for r in ROOFS],
               "rooms": [{"name": r[0], "x": r[1], "z": r[2], "hw": r[3], "hd": r[4], "ceiling": r[5]} for r in ROOMS],
               "ceilings": [{"x": c[0], "z": c[1], "sx": c[2], "sz": c[3], "y0": c[4], "y1": c[5]} for c in CEIL],
               "lights": [{"x": l[0], "y": l[1], "z": l[2], "range": l[3], "intensity": l[4]} for l in LIGHTS],
               "props": [{"kind": q[0], "x": q[1], "z": q[2], "yaw": q[3], "size": q[4]} for q in PROPS]}, fp)
print("FOOTPRINTS", len(FOOT), "ROOFS", len(ROOFS), "ROOMS", len(ROOMS), "LIGHTS", len(LIGHTS))
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
