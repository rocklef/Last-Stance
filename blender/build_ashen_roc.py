"""Build the original Ashen Roc character and three FBX animation takes.

Run with Blender 5.x:
  blender.exe --background --python build_ashen_roc.py -- <UnityProject/Assets/Boss> <LastStance/blender>
The .blend source is saved separately from Unity's imported FBX assets.
"""
import bpy
import math
import os
import sys
from mathutils import Vector


def args_after_separator():
    if "--" not in sys.argv:
        raise RuntimeError("Expected output arguments after --")
    return sys.argv[sys.argv.index("--") + 1:]


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.armatures, bpy.data.actions):
        for data in list(datablocks):
            if data.users == 0:
                datablocks.remove(data)


def material(name, color, metallic=0.0, roughness=0.5, emission=None):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    p = m.node_tree.nodes.get("Principled BSDF")
    p.inputs["Base Color"].default_value = (*color, 1.0)
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = roughness
    if emission:
        p.inputs["Emission Color"].default_value = (*emission, 1.0)
        p.inputs["Emission Strength"].default_value = 2.5
    return m


def add_ash_texture(mat, dark, light, scale=24.0):
    """Fine pumice/feather grain so large surfaces do not read as flat plastic."""
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 3.2
    noise.inputs["Roughness"].default_value = .72
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = .22
    ramp.color_ramp.elements[0].color = (*dark, 1.0)
    ramp.color_ramp.elements[1].position = .78
    ramp.color_ramp.elements[1].color = (*light, 1.0)
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .18
    bump.inputs["Distance"].default_value = .055
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], shader.inputs["Base Color"])
    links.new(noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])


MAT_BODY = None
MAT_FEATHER = None
MAT_DARK = None
MAT_BEAK = None
MAT_EYE = None
MAT_GOLD = None
OBJECTS_BY_BONE = {}


def action_fcurves(action):
    """Support Blender 4.x legacy and Blender 5.x layered actions."""
    if getattr(action, "is_action_legacy", False):
        return list(action.fcurves)
    curves = []
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                bag = strip.channelbag(slot)
                if bag:
                    curves.extend(list(bag.fcurves))
    return curves


def add_uv(name, loc, scale, mat, bone, seg=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(mat)
    OBJECTS_BY_BONE.setdefault(bone, []).append(o)
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def add_cone(name, loc, radius, depth, mat, bone, direction=(0, 0, 1), vertices=24, radius2=0):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=radius2, depth=depth, location=loc)
    o = bpy.context.object
    o.name = name
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(Vector(direction).normalized())
    o.data.materials.append(mat)
    OBJECTS_BY_BONE.setdefault(bone, []).append(o)
    for p in o.data.polygons:
        p.use_smooth = True
    bevel = o.modifiers.new("Soft carved edges", "BEVEL")
    bevel.width = 0.035
    bevel.segments = 2
    o.modifiers.new("Weighted corner normals", "WEIGHTED_NORMAL")
    return o


def add_loft_body(name, rings, mat, bone, segments=32):
    """A single continuous, smooth chest-to-throat mesh; avoids the stacked-sphere look."""
    verts, faces = [], []
    for z, rx, ry, center_y in rings:
        for i in range(segments):
            angle = (2.0 * math.pi * i) / segments
            verts.append((rx * math.cos(angle), center_y + ry * math.sin(angle), z))
    for ring in range(len(rings) - 1):
        lo, hi = ring * segments, (ring + 1) * segments
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((lo + i, lo + j, hi + j, hi + i))
    faces.append(tuple(reversed(range(segments))))
    top = (len(rings) - 1) * segments
    faces.append(tuple(top + i for i in range(segments)))
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(mat)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for poly in mesh.polygons:
        poly.use_smooth = True
    sub = obj.modifiers.new("Sculpted continuous silhouette", "SUBSURF")
    sub.levels = 2
    sub.render_levels = 2
    OBJECTS_BY_BONE.setdefault(bone, []).append(obj)
    return obj


def add_feather_mesh(name, bone, feathers, mat):
    """Batch sculpted, ridged flight feathers into one mesh for each rig segment."""
    verts, faces = [], []
    for start, end, width, depth in feathers:
        a, b = Vector(start), Vector(end)
        d = (b - a).normalized()
        side = Vector((-d.y, d.x, 0)).normalized()
        samples = ((0.0, .025), (.20, .70), (.50, 1.0), (.78, .58), (1.0, 0.0))
        top_start = len(verts)
        for u, width_factor in samples:
            center = a.lerp(b, u)
            lift = math.sin(math.pi * u) * depth * .65
            half = side * width * width_factor
            verts.extend((tuple(center - half + Vector((0, 0, depth + lift))),
                          tuple(center + Vector((0, 0, depth * 1.8 + lift + .035))),
                          tuple(center + half + Vector((0, 0, depth + lift)))))
        for i in range(len(samples) - 1):
            l, c, r = top_start + i * 3, top_start + i * 3 + 1, top_start + i * 3 + 2
            ln, cn, rn = l + 3, c + 3, r + 3
            faces.extend(((l, ln, cn, c), (c, cn, rn, r)))
        bottom_start = len(verts)
        for u, width_factor in samples:
            center = a.lerp(b, u)
            half = side * width * width_factor
            verts.extend((tuple(center - half - Vector((0, 0, depth))),
                          tuple(center - Vector((0, 0, depth))),
                          tuple(center + half - Vector((0, 0, depth)))))
        for i in range(len(samples) - 1):
            l, c, r = bottom_start + i * 3, bottom_start + i * 3 + 1, bottom_start + i * 3 + 2
            ln, cn, rn = l + 3, c + 3, r + 3
            faces.extend(((l, c, cn, ln), (c, r, rn, cn)))
            tl, tc, tr = top_start + i * 3, top_start + i * 3 + 1, top_start + i * 3 + 2
            tln, tcn, trn = tl + 3, tc + 3, tr + 3
            faces.extend(((tl, l, ln, tln), (tr, trn, rn, r)))
        faces.extend(((top_start, top_start + 1, top_start + 2,
                       bottom_start + 2, bottom_start + 1, bottom_start),
                      (top_start + 12, top_start + 11, top_start + 10,
                       bottom_start + 10, bottom_start + 11, bottom_start + 12)))
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(mat)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bevel = obj.modifiers.new("Feather edge softness", "BEVEL")
    bevel.width, bevel.segments = .025, 2
    obj.modifiers.new("Feather normals", "WEIGHTED_NORMAL")
    OBJECTS_BY_BONE.setdefault(bone, []).append(obj)
    return obj


def create_bird_mesh():
    # Facing Blender -Y. One continuous chest and rising throat under a colossal segmented span.
    add_loft_body("Roc | sculpted breast and neck", [
        (.64, .18, .32, .10), (1.02, .39, .56, .10), (1.55, .62, .75, .10),
        (2.18, .76, .90, .02), (2.72, .72, .85, -.08),
        (3.13, .57, .67, -.25), (3.45, .43, .53, -.48),
        (3.72, .34, .42, -.73), (3.94, .28, .32, -.88)], MAT_BODY, "body")
    add_uv("Roc | skull", (0, -1.12, 4.04), (.42, .46, .40), MAT_BODY, "head")
    # A layered gorget breaks up the neck/body join and catches the rim light.
    gorget = []
    for i in range(13):
        x = (i - 6) * .105
        gorget.append(((x, -1.08, 3.45 - abs(i - 6) * .018),
                       (x * 1.34, -.78, 2.78 - abs(i - 6) * .045), .105, .045))
    add_feather_mesh("Roc | throat gorget", "neck", gorget, MAT_FEATHER)
    # Long hooked beak and separate movable lower jaw.
    add_cone("Roc | hooked upper beak", (0, -1.58, 3.91), .37, 1.05, MAT_BEAK, "head", (0, -1, -.24), radius2=.012, vertices=32)
    add_uv("Roc | lower jaw", (0, -1.53, 3.67), (.25, .55, .105), MAT_BEAK, "jaw")
    # Bright eyes and sculpted brow ridges on both sides.
    for s in (-1, 1):
        add_uv("Roc | narrow spirit eye", (s * .40, -1.515, 4.08), (.055, .075, .042), MAT_EYE, "head", 20, 16)
        add_uv("Roc | slate orbital plate", (s * .41, -1.45, 4.10), (.112, .085, .105), MAT_DARK, "head", 20, 16)
        add_cone("Roc | hooked orbital ridge", (s * .43, -1.27, 4.32), .13, .44, MAT_DARK, "head", (s * .2, -.05, 1), radius2=.01)
        # Powerful scaled legs and hooked talons under the body.
        add_uv("Roc | feathered haunch", (s * .47, .16, 1.72), (.29, .43, .48), MAT_DARK, "body")
        add_cone("Roc | plated shin", (s * .49, -.04, 1.25), .19, .78, MAT_GOLD, "body", (0, 0, -1), radius2=.105)
        add_uv("Roc | taloned foot", (s * .49, -.33, .85), (.30, .47, .17), MAT_BEAK, "body")
        for claw in range(3):
            x = s * .49 + (claw - 1) * .18
            add_cone("Roc | talon", (x, -.68, .73), .12, .62, MAT_GOLD, "body", (0, -.65, -.76), radius2=.008, vertices=12)
    # Crown plume; makes the head readable in silhouette from the ground.
    for i in range(7):
        x = (i - 3) * .16
        add_cone("Roc | crown sickle plume", (x, -.71, 4.55), .125, 1.0 + .13 * (3 - abs(i - 3)), MAT_DARK if i % 2 else MAT_FEATHER, "head", (x * .28, .16, 1), radius2=.006, vertices=16)

    # Tail fan with distinct rigid tail bone and a second layer of copper-edged feathers.
    for i in range(13):
        x = (i - 6) * .15
        y = 1.22 + abs(i - 6) * .06
        z = 2.53 - abs(i - 6) * .035
        add_feather_mesh("Roc | swept tail fan", "tail", [((x * .3, y, z), (x * 1.95, y + 2.5, z - .22), .20, .075)], MAT_DARK if i % 3 else MAT_FEATHER)

    # Broad wings: three independently animated segments, with two overlapping rows of tapered
    # feather blades. Outer primaries are long enough to read as a bird in the sky.
    for s, suffix in ((-1, "L"), (1, "R")):
        for region, x0, x1, bone, length in (("upper", .65, 2.9, "wing_upper." + suffix, 1.05),
                                               ("middle", 2.62, 4.65, "wing_mid." + suffix, 1.40),
                                               ("tip", 4.36, 6.55, "wing_tip." + suffix, 1.95)):
            # A thin, continuous, swept membrane gives the fan a strong wing silhouette;
            # individually sculpted feathers form its trailing edge.
            sign = s
            if region == "upper":
                contour = [(sign * .58, -.18, 3.40), (sign * 2.95, -.04, 3.21),
                           (sign * 3.15, 1.22, 2.88), (sign * .95, 1.26, 2.76)]
            elif region == "middle":
                contour = [(sign * 2.70, -.04, 3.21), (sign * 4.72, .18, 2.94),
                           (sign * 4.97, 1.77, 2.52), (sign * 2.93, 1.47, 2.78)]
            else:
                contour = [(sign * 4.50, .14, 2.98), (sign * 6.62, .38, 2.57),
                           (sign * 6.92, 2.30, 2.13), (sign * 4.75, 1.86, 2.48)]
            membrane = bpy.data.meshes.new("Roc | %s %s wing membrane" % (suffix, region))
            membrane.from_pydata(contour, [], [(0, 1, 2, 3)])
            membrane.materials.append(MAT_BODY)
            membrane.update()
            membrane_obj = bpy.data.objects.new("Roc | %s %s wing membrane" % (suffix, region), membrane)
            bpy.context.collection.objects.link(membrane_obj)
            membrane_obj.modifiers.new("Membrane thickness", "SOLIDIFY").thickness = .045
            membrane_obj.modifiers.new("Membrane normals", "WEIGHTED_NORMAL")
            OBJECTS_BY_BONE.setdefault(bone, []).append(membrane_obj)
            fs = []
            for i in range(15):
                u = i / 14
                x = s * (x0 + (x1 - x0) * u)
                y = .10 + u * .45
                z = 3.26 - u * .48
                primary = (x + s * length * (.28 + .22 * math.sin(u * math.pi)), y + .90 + .66 * u, z - .40 - .24 * u)
                fs.append(((x, y, z), primary, .28 - .08 * u, .085))
                # A shorter cover-feather row gives each wing thickness and overlapping texture.
                fs.append(((x * .91, y - .36, z + .12), (x + s * length * .30, y + .42, z - .12), .19, .075))
            add_feather_mesh("Roc | " + suffix + " " + region + " flight feathers", bone, fs, MAT_FEATHER if region == "tip" else MAT_DARK)
    # Copper feather accents over the scapulars.
    for s in (-1, 1):
        fs = []
        for i in range(9):
            x = s * (.55 + i * .075)
            fs.append(((x, -.1, 3.55 - i * .025), (x + s * .15, .75 + i * .1, 3.12 - i * .03), .12, .045))
        add_feather_mesh("Roc | copper scapulars", "body", fs, MAT_GOLD)


def create_rig():
    bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
    arm = bpy.context.object
    arm.name = "AshenRoc_Rig"
    arm.data.name = "AshenRoc_Skeleton"
    eb = arm.data.edit_bones
    eb.remove(eb[0])
    def bone(name, head, tail, parent=None):
        b = eb.new(name)
        b.head, b.tail = head, tail
        b.parent = eb.get(parent) if parent else None
        if b.parent:
            b.use_connect = False
        return b
    bone("root", (0, 0, .3), (0, 0, 1.0))
    bone("body", (0, 0, 1.0), (0, 0, 3.0), "root")
    bone("neck", (0, -.25, 3.0), (0, -.65, 3.65), "body")
    bone("head", (0, -.65, 3.65), (0, -1.05, 4.3), "neck")
    bone("jaw", (0, -.75, 3.7), (0, -1.4, 3.62), "head")
    bone("tail", (0, .75, 2.55), (0, 2.3, 2.35), "body")
    for s, suffix in ((-1, "L"), (1, "R")):
        bone("wing_upper." + suffix, (s * .7, .1, 3.05), (s * 2.8, .25, 2.95), "body")
        bone("wing_mid." + suffix, (s * 2.8, .25, 2.95), (s * 4.6, .5, 2.7), "wing_upper." + suffix)
        bone("wing_tip." + suffix, (s * 4.6, .5, 2.7), (s * 6.4, .8, 2.35), "wing_mid." + suffix)
    bpy.ops.object.mode_set(mode="OBJECT")
    # Rigid weights preserve sculpted detail while each articulated section remains controllable.
    for bone_name, objs in OBJECTS_BY_BONE.items():
        for obj in objs:
            group = obj.vertex_groups.new(name=bone_name)
            group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
            mod = obj.modifiers.new("Roc skeletal rig", 'ARMATURE')
            mod.object = arm
            obj.parent = arm
    arm.show_in_front = True
    arm.data.display_type = 'OCTAHEDRAL'
    return arm


def key_action(arm, name, frame_values, loop=False):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    action = bpy.data.actions.new(name)
    arm.animation_data_create()
    arm.animation_data.action = action
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
    for frame, values in frame_values:
        for bone_name, rot in values.items():
            pb = arm.pose.bones.get(bone_name)
            if not pb:
                continue
            pb.rotation_euler = rot
            pb.keyframe_insert(data_path="rotation_euler", frame=frame, group=bone_name)
    action.name = name
    action.use_fake_user = True
    for fc in action_fcurves(action):
        for key in fc.keyframe_points:
            key.interpolation = 'BEZIER'
        if loop:
            mod = fc.modifiers.new('CYCLES')
    return action


def make_actions(arm):
    def wings(angle):
        return {f"wing_{seg}.{side}": (0, angle * (1 if side == 'L' else -1), 0)
                for seg in ("upper", "mid", "tip") for side in ("L", "R")}
    key_action(arm, "FlyIn", [(1, wings(-.38)), (7, wings(.68)), (13, wings(-.35)), (19, wings(.68)), (25, wings(-.38))], loop=True)
    key_action(arm, "Perch", [(1, wings(-.08)), (15, wings(.08)), (30, wings(-.08))], loop=True)
    attack_frames = [(1, wings(-.08)), (7, wings(.45)), (13, wings(-.25)), (19, wings(.1))]
    key_action(arm, "MouthAttack", attack_frames, loop=False)
    # Animated jaw open / close is a separate part of the mouth-spit beat.
    pb = arm.pose.bones["jaw"]
    pb.rotation_euler = (0, 0, 0); pb.keyframe_insert(data_path="rotation_euler", frame=1, group="jaw")
    pb.rotation_euler = (math.radians(-35), 0, 0); pb.keyframe_insert(data_path="rotation_euler", frame=8, group="jaw")
    pb.rotation_euler = (math.radians(-35), 0, 0); pb.keyframe_insert(data_path="rotation_euler", frame=13, group="jaw")
    pb.rotation_euler = (0, 0, 0); pb.keyframe_insert(data_path="rotation_euler", frame=19, group="jaw")
    arm.animation_data.action.name = "MouthAttack"
    for fc in action_fcurves(arm.animation_data.action):
        for key in fc.keyframe_points:
            key.interpolation = 'BEZIER'
    # Export action clips separately so Unity can import clean, named, Generic rig clips.
    actions = {a.name: a for a in bpy.data.actions if a.name in ("FlyIn", "Perch", "MouthAttack")}
    return actions


def export_fbx(filepath, arm, action=None):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    for objs in OBJECTS_BY_BONE.values():
        for obj in objs:
            obj.select_set(True)
    bpy.context.view_layer.objects.active = arm
    if action:
        arm.animation_data.action = action
    bpy.ops.export_scene.fbx(filepath=filepath, use_selection=True,
        object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=bool(action), bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        mesh_smooth_type='FACE', use_armature_deform_only=True, path_mode='AUTO')


def add_stage_cube(name, loc, scale, mat, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    if bevel:
        mod = obj.modifiers.new("Worn cut stone edges", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        obj.modifiers.new("Weighted normals", "WEIGHTED_NORMAL")
    return obj


def add_area(name, loc, color, energy, size, aim):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.color, data.shape, data.size = energy, color, 'DISK', size
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (Vector(aim) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return obj


def build_showcase(arm, actions, preview_dir):
    """Create a review scene showing the actual hero model, perched bird and shape-changing attack."""
    os.makedirs(preview_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end, scene.render.fps = 1, 170, 30
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1600, 900, 70
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.render.resolution_percentage = 70
    scene.view_settings.view_transform = 'AgX'
    scene.world.color = (.018, .024, .038)

    roof = material("Stage | smoked red tile", (.17, .035, .025), .18, .34)
    stone = material("Stage | weathered temple stone", (.115, .13, .15), .08, .82)
    trim = material("Stage | old bronze", (.38, .19, .055), .72, .28)
    ground = material("Stage | dusk flagstone", (.07, .08, .10), .12, .76)
    hot = material("Roc power | incandescent arrow cores", (.75, .13, .022), .22, .28, (1.0, .08, .008))
    cold = material("Roc power | ash iron", (.11, .16, .19), .6, .31, (.04, .12, .16))

    # Deep courtyard floor and a large pagoda roof perch with real supporting structure.
    add_stage_cube("Courtyard floor | scale reference", (0, -13, -5.05), (125, 100, .45), ground, .12)
    add_stage_cube("Pagoda perch | upper tiled roof", (0, 1.2, 8.80), (56, 14, .62), roof, .18)
    add_stage_cube("Pagoda perch | bronze eave", (0, 1.2, 8.40), (56.8, 14.5, .12), trim, .045)
    for x in (-19.5, -6.5, 6.5, 19.5):
        add_stage_cube("Temple | lacquered support pillar", (x, 2.0, 2.05), (.92, 1.0, 13.3), roof, .085)
        add_stage_cube("Temple | capital block", (x, 2.0, 8.50), (1.6, 1.7, .42), trim, .055)
    add_stage_cube("Pagoda | rear fascia", (0, 8.3, 9.50), (54, .62, 1.50), roof, .1)
    # Layered upturned roof shoulders frame the bird silhouette.
    for side in (-1, 1):
        shoulder = add_stage_cube("Pagoda | swept roof shoulder", (side * 28.0, 1.2, 8.95), (.62, 13.5, .68), trim, .12)
        shoulder.rotation_euler[1] = side * math.radians(-10)

    # Place the actual Kaito Mixamo model in the court below the bird for a clear scale read.
    fbx_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "UnityProject", "Assets", "Mixamo", "Characters", "Leonard.fbx")
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=fbx_path, automatic_bone_orientation=True)
    imported = [o for o in bpy.context.scene.objects if o not in before]
    human_arm = next((o for o in imported if o.type == 'ARMATURE'), None)
    for obj in imported:
        if obj.type in {'CAMERA', 'LIGHT'}:
            bpy.data.objects.remove(obj, do_unlink=True)
    if human_arm:
        human_arm.name = "Kaito | imported Mixamo rig"
        human_arm.location = (0, -42.0, -4.84)
        human_arm.rotation_euler[2] = math.radians(180)
        # His shoulders and gaze lift toward the arriving Roc.
        human_arm.animation_data_create()
        hero_action = bpy.data.actions.new("Kaito | looks up at the Ashen Roc")
        human_arm.animation_data.action = hero_action
        for frame, tilt in ((1, 0), (56, 0), (65, math.radians(-18)), (92, math.radians(-18)), (110, math.radians(2)), (156, math.radians(2))):
            neck = human_arm.pose.bones.get("mixamorig9:Neck")
            head = human_arm.pose.bones.get("mixamorig9:Head")
            if neck:
                neck.rotation_mode = 'XYZ'; neck.rotation_euler.x = tilt * .35; neck.keyframe_insert(data_path="rotation_euler", frame=frame, group=neck.name)
            if head:
                head.rotation_mode = 'XYZ'; head.rotation_euler.x = tilt; head.keyframe_insert(data_path="rotation_euler", frame=frame, group=head.name)

    # The bird lands on the tiled ridge above Kaito. Retain its keyed FlyIn / Perch / MouthAttack actions.
    arm.scale = (3.4, 3.4, 4.5)
    arm.location = (0, .55, 7.28)
    arm.rotation_euler[2] = 0
    arm.animation_data.action = None
    track = arm.animation_data.nla_tracks.new()
    fly = track.strips.new("01 | Fly in and fold wings", 1, actions["FlyIn"])
    fly.blend_type = 'REPLACE'
    perch_start = int(fly.frame_end) + 1
    perch = track.strips.new("02 | Land and watch the hero", perch_start, actions["Perch"])
    perch.repeat = 1.0
    perch.blend_type = 'REPLACE'
    attack_start = int(perch.frame_end) + 1
    attack = track.strips.new("03 | Open beak and spit arrow swarm", attack_start, actions["MouthAttack"])
    attack.blend_type = 'REPLACE'
    finish_start = int(attack.frame_end) + 1
    finish = track.strips.new("04 | Hold perch over the fight", finish_start, actions["Perch"])
    finish.repeat = 2.4
    finish.blend_type = 'REPLACE'

    # A deliberately designed, beveled arrow shard mesh is instanced through five readable forms.
    outline = [(-.12, -.60), (.12, -.60), (.12, .23), (.40, .19), (0, .70), (-.40, .19), (-.12, .23)]
    av, af = [], []
    for z in (-.045, .045): av.extend([(x, y, z) for x, y in outline])
    af.append(tuple(range(7))); af.append(tuple(reversed(range(7, 14))))
    for i in range(7):
        j = (i + 1) % 7
        af.append((i, j, j + 7, i + 7))
    arrow_mesh = bpy.data.meshes.new("Instanced | arrow shard geometry")
    arrow_mesh.from_pydata(av, [], af); arrow_mesh.materials.append(cold); arrow_mesh.materials.append(hot)
    for i, poly in enumerate(arrow_mesh.polygons): poly.material_index = 1 if i in (0, 1, 3, 4, 5, 6, 7, 8) else 0
    arrow_mesh.update()
    arrow_objects = []
    count = 96
    mouth = Vector((0, .55 - 3.4 * 1.95, 7.28 + 4.5 * 3.77))
    for i in range(count):
        obj = bpy.data.objects.new("Roc power | arrow particle %03d" % i, arrow_mesh)
        bpy.context.collection.objects.link(obj)
        obj.rotation_mode = 'QUATERNION'
        arrow_objects.append(obj)
        u = i / float(count - 1)
        # First: beak stream; second: living segmented serpent; third: broad wing-blade fan;
        # fourth: drill spiral; fifth: dive at Kaito; final: bursting shards.
        stream = mouth + Vector((math.sin(u * 17) * .28, -u * 7.0, math.cos(u * 13) * .32))
        serpent = Vector((math.sin(u * math.pi * 3.2) * 5.0, mouth.y - u * 25.0, 14.0 + math.sin(u * math.pi * 2) * 2.4 + u * 1.5))
        fan = Vector(((u - .5) * 20.0, -18.0 - math.sin(u * math.pi) * 2.4, 12.2 + math.sin(u * math.pi) * 5.2))
        theta = u * math.pi * 5.0
        spiral = Vector((math.cos(theta) * (3.0 + u * 5.5), -22.0 - u * 10.0, 10.0 + u * 11.0 + math.sin(theta) * 2.5))
        dive = Vector(((u - .5) * 12.0, -42.0 - (1 - u) * 3.5, 3.7 - u * 8.2))
        burst = Vector((dive.x + math.sin(i * 12.989) * 4.0, dive.y - abs(math.cos(i * 4.7)) * 2.0, dive.z + math.cos(i * 9.17) * 3.2))
        stages = ((1, mouth + Vector((math.sin(i * 3.1) * .11, 0, math.cos(i * 2.4) * .11))),
                  (60, mouth), (74, mouth), (80, stream), (98, serpent),
                  (115, fan), (132, spiral), (148, dive), (160, burst))
        tangent_targets = (stream, serpent, fan, spiral, dive, burst)
        for frame, position in stages:
            obj.location = position
            obj.scale = (.26 + .13 * (.5 + .5 * math.sin(i * 2.1)),) * 3
            if frame == 1: direction = Vector((0, -1, .08))
            elif frame <= 80: direction = (stream - mouth).normalized()
            elif frame <= 98: direction = Vector((math.cos(u * 10), 0, math.sin(u * 10) + .3))
            elif frame <= 115: direction = Vector((1 if i % 2 else -1, -.18, .45))
            elif frame <= 132: direction = Vector((-math.sin(theta), .15, math.cos(theta)))
            elif frame <= 148: direction = Vector((0, -1, -1.2))
            else: direction = (burst - dive).normalized()
            obj.rotation_quaternion = Vector((0, 1, 0)).rotation_difference(direction)
            obj.keyframe_insert(data_path="location", frame=frame)
            obj.keyframe_insert(data_path="scale", frame=frame)
            obj.keyframe_insert(data_path="rotation_quaternion", frame=frame)
        # Keep the bird clean while it lands and stares; its mouth/spit beat begins at frame 74.
        for frame in (20, 60, 69):
            obj.scale = (0, 0, 0)
            obj.keyframe_insert(data_path="scale", frame=frame)
        obj.scale = (.07, .07, .07)
        obj.keyframe_insert(data_path="scale", frame=74)
    # The swarm glows like hot ash and paints a short-lived trail through the air.
    world = scene.world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (.13, .24, .43, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = .52
    add_area("Key | dawn on ash plumage", (3, -18, 27), (1.0, .78, .59), 5200, 16, (0, -2, 13))
    add_area("Rim | blue sky edge", (-18, 3, 19), (.36, .59, 1.0), 4300, 14, (0, 0, 13))
    add_area("Face | ember bounce", (9, -14, 13), (1.0, .34, .12), 2100, 8, (0, -2, 15))
    sun_data = bpy.data.lights.new("Dawn edge", 'SUN'); sun_data.energy = 2.2; sun_data.color = (.72, .83, 1.0)
    sun = bpy.data.objects.new("Sunset edge", sun_data); bpy.context.collection.objects.link(sun); sun.rotation_euler = (math.radians(23), math.radians(-24), math.radians(-28))

    cam_data = bpy.data.cameras.new("Ashen Roc showcase camera")
    cam = bpy.data.objects.new("Ashen Roc showcase camera", cam_data); bpy.context.collection.objects.link(cam)
    cam.location = (0.0, -65.0, -3.7)
    target = Vector((0, -7.0, 8.0))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = 35
    scene.camera = cam
    scene.frame_set(1)

    # Give the saved file a useful first-open view: camera composition, rendered materials, timeline.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                area.spaces.active.region_3d.view_perspective = 'CAMERA'
                area.spaces.active.shading.type = 'MATERIAL'
    scene.render.resolution_percentage = 70
    # Hero model portrait first: give its head, blade-like wings and true scale the screen.
    cam.location = (23.0, -48.0, 19.0)
    close_target = Vector((0, -.6, 17.0))
    cam.rotation_euler = (close_target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = 32
    scene.frame_set(20)
    scene.render.filepath = os.path.join(preview_dir, "bird_closeup.png")
    bpy.ops.render.render(write_still=True)
    # Restore the wide low-angle action shot so the tiny hero reads beneath the giant bird.
    cam.location = (0.0, -65.0, -3.7)
    target = Vector((0, -7.0, 8.0))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = 35
    for frame, name in ((20, "bird_perched"), (66, "beak_opens"), (82, "ash_stream"), (100, "serpent_form"), (117, "wing_blade_fan"), (134, "spiral_dive"), (150, "impact_dive"), (163, "impact_burst")):
        scene.frame_set(frame)
        scene.render.filepath = os.path.join(preview_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
    scene.frame_set(20)
    # Leave Blender itself on the close bird portrait, not the distant action camera framing.
    cam.location = (23.0, -48.0, 19.0)
    saved_target = Vector((0, -.6, 17.0))
    cam.rotation_euler = (saved_target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = 32


def main():
    cli_args = args_after_separator()
    out_dir, source_dir = cli_args[:2]
    preview_dir = cli_args[2] if len(cli_args) > 2 else os.path.join(source_dir, "AshenRoc_Preview")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(source_dir, exist_ok=True)
    clear_scene()
    global MAT_BODY, MAT_FEATHER, MAT_DARK, MAT_BEAK, MAT_EYE, MAT_GOLD
    MAT_BODY = material("Ash Roc | moonlit pumice", (.38, .44, .53), .28, .52)
    MAT_FEATHER = material("Ash Roc | silver feather ridges", (.68, .75, .84), .18, .42)
    MAT_DARK = material("Ash Roc | deep slate undersides", (.13, .19, .28), .3, .55)
    MAT_BEAK = material("Ash Roc | obsidian beak", (.035, .055, .09), .68, .25)
    MAT_EYE = material("Ash Roc | cold blue-white eye", (.22, .72, 1.0), .15, .2, (.08, .5, 1.0))
    MAT_GOLD = material("Ash Roc | aged ivory details", (.74, .61, .36), .48, .32)
    add_ash_texture(MAT_BODY, (.19, .26, .35), (.62, .70, .80), 19.0)
    add_ash_texture(MAT_FEATHER, (.38, .45, .55), (.88, .92, .97), 31.0)
    add_ash_texture(MAT_DARK, (.075, .12, .20), (.30, .39, .52), 23.0)
    create_bird_mesh()
    arm = create_rig()
    actions = make_actions(arm)
    export_fbx(os.path.join(out_dir, "AshenRoc.fbx"), arm)
    for action_name, action in actions.items():
        export_fbx(os.path.join(out_dir, "AshenRoc_" + action_name + ".fbx"), arm, action)
    # Keep export selections clean, then stage and save the Blender presentation for review.
    bpy.ops.object.select_all(action="DESELECT")
    build_showcase(arm, actions, preview_dir)
    blend_path = os.path.join(source_dir, "AshenRoc.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print("ASHEN_ROC_EXPORT_OK", blend_path, sorted(actions.keys()))


main()
