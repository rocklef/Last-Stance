# Builds the two low-poly fighters as jointed GLB rigs.
#   blender -b -P blender/build_fighters.py
# Every body part is its own object whose origin sits on its joint, parented in a chain
# (Hips > Spine > Chest > Head / UpperArm > ForeArm > Hand, Hips > Thigh > Shin > Foot),
# all with identity rotation, so Unity can pose them by rotating transforms.
# Fighters face Blender -Y (glTF +Z, Unity +Z). Unity's right side (+X) is Blender -X,
# so "_R" parts are built at negative X.
import bpy, bmesh, math, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "UnityProject", "Assets", "Fighters")
os.makedirs(OUT, exist_ok=True)


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


MATS = {}


def mat(name, rgb, rough=0.7, metal=0.0):
    key = name
    if key in MATS:
        return MATS[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    MATS[key] = m
    return m


def loft(name, sections, material, sides=8, parent=None, loc=(0, 0, 0), cap=True, rot0=None):
    """sections: list of (z, rx, ry[, cx, cy]) rings, bottom-to-top or top-to-bottom."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    a0 = math.pi / sides if rot0 is None else rot0
    for s in sections:
        z, rx, ry = s[0], s[1], s[2]
        cx = s[3] if len(s) > 3 else 0.0
        cy = s[4] if len(s) > 4 else 0.0
        ring = [bm.verts.new((cx + rx * math.cos(a0 + i * 2 * math.pi / sides),
                              cy + ry * math.sin(a0 + i * 2 * math.pi / sides), z)) for i in range(sides)]
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    return link(name, me, parent, loc)


def box(name, size, center, material, parent=None, bevel=0.0, taper=None, rot=(0, 0, 0)):
    """Box centred on `center` in the parent's space, rotated about its own centre; taper=(sx, sy) scales the top face."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        x, y, z = v.co
        if taper and z > 0:
            x *= taper[0]
            y *= taper[1]
        v.co = (x * size[0], y * size[1], z * size[2])
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    ob = link(name, me, parent, center)
    ob.rotation_euler = rot
    return ob


def ico(name, radius, center, material, parent=None, scale=(1, 1, 1), subdiv=1):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    for v in bm.verts:
        v.co = (center[0] + v.co.x * scale[0], center[1] + v.co.y * scale[1], center[2] + v.co.z * scale[2])
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    return link(name, me, parent, (0, 0, 0))


def cone(name, r, h, center, material, parent=None, sides=6, rot=(0, 0, 0)):
    """Spike rising along local +Z from `center`, tilted by `rot`."""
    ob = loft(name, [(0, r, r), (h, 0.002, 0.002)], material, sides=sides, parent=parent, loc=center)
    ob.rotation_euler = rot
    return ob


def link(name, me, parent, loc):
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if parent is not None:
        ob.parent = parent
    ob.location = loc
    return ob


def joint(name, parent, loc):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_size = 0.05
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    ob.location = loc
    return ob


def build(spec):
    clear()
    MATS.clear()
    H = spec["height"]
    B = spec["bulk"]
    A = spec["arm_bulk"]
    skin = mat("Skin", spec["skin"], 0.6)
    top = mat("Top", spec["top"], 0.75)
    pants = mat("Pants", spec["pants"], 0.8)
    belt = mat("Belt", spec["belt"], 0.6)
    wrap = mat("Wraps", spec["wraps"], 0.8)
    hair = mat("Hair", spec["hair"], 0.5)
    dark = mat("Eyes", (0.02, 0.02, 0.02), 0.3)
    accent = mat("Accent", spec["accent"], 0.5)
    iron = mat("Iron", (0.32, 0.33, 0.35), 0.35, 0.9)
    shoe = mat("Shoes", spec["shoes"], 0.7)

    root = bpy.data.objects.new(spec["name"], None)
    bpy.context.scene.collection.objects.link(root)

    z = lambda v: v * H
    # ---- pelvis / hips ----
    hips = joint("Hips", root, (0, 0, z(0.98)))
    loft("HipsMesh", [(z(-0.13), 0.165 * B, 0.12 * B), (z(0.0), 0.17 * B, 0.125 * B), (z(0.09), 0.155 * B, 0.115 * B)], pants, parent=hips)
    loft("BeltMesh", [(z(0.03), 0.175 * B, 0.13 * B), (z(0.10), 0.165 * B, 0.123 * B)], belt, parent=hips, cap=False)
    # belt knot + tails hanging at the front
    box("BeltKnot", (0.07, 0.04, 0.05), (0.0, -0.13 * B, z(0.065)), belt, parent=hips)
    box("BeltTailA", (0.035, 0.015, 0.2), (0.03, -0.14 * B, z(-0.05)), belt, parent=hips)
    box("BeltTailB", (0.035, 0.015, 0.17), (-0.03, -0.138 * B, z(-0.035)), belt, parent=hips)
    if spec["gi_skirt"]:
        loft("GiSkirt", [(z(-0.12), 0.19 * B, 0.14 * B), (z(0.03), 0.172 * B, 0.128 * B)], top, parent=hips, cap=False, sides=10)

    # ---- spine / abdomen ----
    spine = joint("Spine", hips, (0, 0, z(0.10)))
    abd_mat = top if spec["gi_top"] else skin
    loft("SpineMesh", [(0, 0.155 * B, 0.115 * B), (z(0.10), 0.16 * B, 0.12 * B), (z(0.19), 0.175 * B, 0.125 * B)], abd_mat, parent=spine)
    if not spec["gi_top"]:
        # abs: two columns of blocks
        for row in range(3):
            for side in (-1, 1):
                box(f"Ab{row}{side}", (0.055 * B, 0.02, 0.045), (side * 0.035 * B, -0.118 * B, z(0.03 + row * 0.055)), skin, parent=spine, bevel=0.006)

    # ---- chest ----
    chest = joint("Chest", spine, (0, 0, z(0.18)))
    loft("ChestMesh", [(0, 0.175 * B, 0.125 * B), (z(0.10), 0.215 * B, 0.14 * B), (z(0.20), 0.235 * B * spec["shoulder"], 0.13 * B),
                       (z(0.26), 0.15 * B * spec["shoulder"], 0.09 * B)], abd_mat, parent=chest, sides=10)
    if spec["gi_top"]:
        # V neckline of skin, collar trim in the accent colour
        box("NeckV", (0.09, 0.02, 0.11), (0, -0.133 * B, z(0.19)), skin, parent=chest, taper=(1.4, 1.0))
        box("CollarL", (0.035, 0.02, 0.22), (0.045, -0.136 * B, z(0.15)), accent, parent=chest, rot=(0, 0.42, 0))
        box("CollarR", (0.035, 0.02, 0.22), (-0.045, -0.136 * B, z(0.15)), accent, parent=chest, rot=(0, -0.42, 0))
    else:
        for side in (-1, 1):
            box(f"Pec{side}", (0.13 * B, 0.03, 0.09), (side * 0.075 * B, -0.128 * B, z(0.15)), skin, parent=chest, bevel=0.01)

    # ---- head ----
    head = joint("Head", chest, (0, 0, z(0.27)))
    loft("NeckMesh", [(z(-0.03), 0.06 * B, 0.06 * B), (z(0.08), 0.052 * B, 0.055 * B)], skin, parent=head, sides=7)
    hs = spec["head"]
    loft("HeadMesh", [(z(0.05), 0.055 * hs, 0.06 * hs, 0, -0.015), (z(0.09), 0.085 * hs, 0.095 * hs, 0, -0.01), (z(0.16), 0.095 * hs, 0.105 * hs),
                      (z(0.23), 0.09 * hs, 0.1 * hs, 0, 0.005), (z(0.285), 0.055 * hs, 0.065 * hs, 0, 0.01)], skin, parent=head, sides=8)
    # face: eyes, brows, nose
    for side in (-1, 1):
        box(f"Eye{side}", (0.022, 0.01, 0.012), (side * 0.035, -0.1 * hs, z(0.175)), dark, parent=head)
        box(f"Brow{side}", (0.035, 0.012, 0.01), (side * 0.036, -0.101 * hs, z(0.197)), hair, parent=head, rot=(0, side * 0.3, 0))
        box(f"Ear{side}", (0.015, 0.035, 0.05), (side * 0.095 * hs, 0.0, z(0.17)), skin, parent=head)
    box("Nose", (0.022, 0.03, 0.04), (0, -0.108 * hs, z(0.155)), skin, parent=head, taper=(0.6, 0.5))
    if spec["style"] == "kaito":
        # short spiky hair + topknot + headband with tails
        loft("HairCap", [(z(0.19), 0.1 * hs, 0.11 * hs, 0, 0.005), (z(0.25), 0.096 * hs, 0.106 * hs, 0, 0.01), (z(0.30), 0.06 * hs, 0.07 * hs, 0, 0.015)],
             hair, parent=head, sides=8)
        for i, (x, y) in enumerate([(0.05, -0.07), (-0.05, -0.07), (0.0, -0.08), (0.08, -0.02), (-0.08, -0.02)]):
            cone(f"Spike{i}", 0.03, 0.06, (x * hs, y * hs, z(0.25)), hair, parent=head, sides=5, rot=(-0.6 if y < -0.05 else 0.3, x * -6, 0))
        loft("Topknot", [(z(0.29), 0.035, 0.035, 0, 0.04), (z(0.36), 0.03, 0.03, 0, 0.055), (z(0.38), 0.015, 0.015, 0, 0.06)], hair, parent=head, sides=6)
        loft("Headband", [(z(0.19), 0.102 * hs, 0.112 * hs, 0, 0.004), (z(0.225), 0.1 * hs, 0.109 * hs, 0, 0.006)], accent, parent=head, cap=False, sides=8)
        for i, side in enumerate((-1, 1)):
            box(f"BandTail{i}", (0.03, 0.01, 0.2), (side * 0.025, 0.125 * hs, z(0.13)), accent, parent=head, rot=(0.45, 0, side * 0.15))
    else:
        # bald head with a heavy beard and scar
        loft("Beard", [(z(0.06), 0.06 * hs, 0.05 * hs, 0, -0.055), (z(0.11), 0.09 * hs, 0.06 * hs, 0, -0.06), (z(0.15), 0.095 * hs, 0.05 * hs, 0, -0.05)],
             hair, parent=head, sides=8)
        box("Scar", (0.01, 0.01, 0.07), (0.04, -0.104 * hs, z(0.19)), accent, parent=head, rot=(0, 0.3, 0))
        box("Mustache", (0.08, 0.02, 0.018), (0, -0.108 * hs, z(0.135)), hair, parent=head)

    # ---- arms ----
    for side, tag in ((-1, "R"), (1, "L")):
        sx = side * 0.225 * B * spec["shoulder"]
        up = joint(f"UpperArm_{tag}", chest, (sx, 0, z(0.215)))
        ico(f"Delt_{tag}", 0.068 * A, (0, 0, 0), skin, parent=up, scale=(1, 1, 0.95))
        loft(f"UpperArmMesh_{tag}", [(0, 0.068 * A, 0.07 * A), (z(-0.14), 0.066 * A, 0.064 * A), (z(-0.28), 0.05 * A, 0.052 * A)], skin, parent=up, sides=7)
        fore = joint(f"ForeArm_{tag}", up, (0, 0, z(-0.285)))
        loft(f"ForeArmMesh_{tag}", [(z(0.01), 0.05 * A, 0.052 * A), (z(-0.08), 0.056 * A, 0.055 * A), (z(-0.24), 0.04 * A, 0.04 * A)], skin, parent=fore, sides=7)
        if spec["bracers"]:
            loft(f"Bracer_{tag}", [(z(-0.06), 0.062 * A, 0.062 * A), (z(-0.2), 0.05 * A, 0.05 * A)], iron, parent=fore, sides=7, cap=False)
        else:
            loft(f"ArmWrap_{tag}", [(z(-0.14), 0.05 * A, 0.05 * A), (z(-0.235), 0.043 * A, 0.043 * A)], wrap, parent=fore, sides=7, cap=False)
        hand = joint(f"Hand_{tag}", fore, (0, 0, z(-0.245)))
        box(f"Fist_{tag}", (0.1 * A, 0.115 * A, 0.11 * A), (0, -0.005, -0.055 * A), wrap, parent=hand, bevel=0.012)
        box(f"Knuckles_{tag}", (0.09 * A, 0.035 * A, 0.07 * A), (0, -0.05 * A, -0.06 * A), wrap, parent=hand, bevel=0.008)
        box(f"Thumb_{tag}", (0.03, 0.05, 0.03), (-side * 0.04 * A, -0.04 * A, -0.035 * A), skin, parent=hand, bevel=0.005)

    if spec["pauldron"]:
        # iron shoulder guard with spikes on the left shoulder
        up = bpy.data.objects["UpperArm_L"]
        ico("Pauldron", 0.11 * A, (0.02, 0, 0.025), iron, parent=up, scale=(1.05, 1.1, 0.8))
        for i, (x, y) in enumerate([(0.05, -0.04), (0.07, 0.03), (0.02, 0.07)]):
            cone(f"PauldronSpike{i}", 0.03 * A, 0.11 * A, (x * A, y * A, 0.075 * A), iron, parent=up, sides=5, rot=(-y * 9, 0.7, 0))

    # ---- legs ----
    L = spec["leg_bulk"]
    for side, tag in ((-1, "R"), (1, "L")):
        th = joint(f"Thigh_{tag}", hips, (side * 0.095 * B, 0, z(-0.03)))
        loft(f"ThighMesh_{tag}", [(z(0.04), 0.095 * L, 0.1 * L), (z(-0.2), 0.092 * L, 0.095 * L), (z(-0.42), 0.07 * L, 0.075 * L)], pants, parent=th, sides=8)
        sh = joint(f"Shin_{tag}", th, (0, 0, z(-0.43)))
        loft(f"ShinMesh_{tag}", [(z(0.02), 0.072 * L, 0.078 * L), (z(-0.18), 0.075 * L, 0.08 * L), (z(-0.36), 0.055 * L, 0.058 * L)], pants, parent=sh, sides=8)
        loft(f"AnkleWrap_{tag}", [(z(-0.30), 0.056 * L, 0.06 * L), (z(-0.41), 0.048 * L, 0.05 * L)], wrap, parent=sh, sides=7, cap=False)
        ft = joint(f"Foot_{tag}", sh, (0, 0, z(-0.41)))
        box(f"FootMesh_{tag}", (0.09 * L, 0.24, 0.075), (0, -0.05, -0.055), shoe, parent=ft, bevel=0.012, taper=(0.9, 0.85))

    # weapon-free: fists only. Export.
    path = os.path.join(OUT, spec["name"] + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_yup=True, export_apply=False,
                              export_materials='EXPORT', export_extras=False)
    print("FIGHTER exported", path, len([o for o in bpy.data.objects]), "objects")


KAITO = dict(name="Kaito", style="kaito", height=1.0, bulk=1.0, arm_bulk=1.0, leg_bulk=1.0, shoulder=1.0, head=1.0,
             skin=(0.80, 0.56, 0.40), top=(0.05, 0.05, 0.07), pants=(0.07, 0.09, 0.17), belt=(0.08, 0.3, 0.85),
             wraps=(0.9, 0.88, 0.82), hair=(0.03, 0.03, 0.035), accent=(0.1, 0.4, 0.95), shoes=(0.06, 0.06, 0.07),
             gi_top=True, gi_skirt=True, bracers=False, pauldron=False)

BRAKKA = dict(name="Brakka", style="brakka", height=1.06, bulk=1.28, arm_bulk=1.45, leg_bulk=1.25, shoulder=1.12, head=1.08,
              skin=(0.52, 0.33, 0.22), top=(0.4, 0.1, 0.06), pants=(0.5, 0.06, 0.05), belt=(0.05, 0.05, 0.05),
              wraps=(0.6, 0.08, 0.07), hair=(0.12, 0.06, 0.03), accent=(0.75, 0.35, 0.3), shoes=(0.12, 0.08, 0.05),
              gi_top=False, gi_skirt=False, bracers=True, pauldron=True)

for s in (KAITO, BRAKKA):
    build(s)
