# Renders a quick look-dev image of GLBs side by side.
#   blender -b -P blender/preview.py -- out.png a.glb b.glb ...
import bpy, sys, math

argv = sys.argv[sys.argv.index("--") + 1:]
out, files = argv[0], argv[1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
for i, f in enumerate(files):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f)
    for ob in set(bpy.data.objects) - before:
        if ob.parent is None:
            ob.location.x += (i - (len(files) - 1) / 2) * 1.0
            ob.rotation_euler.z += math.radians(25 if i == 0 else -25)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 1000, 800
scene.render.filepath = out
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.3, 0.28, 1)
scene.world = world
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 4
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-30))
scene.collection.objects.link(sun)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
cam.location = (0.3, -4.2, 1.35)
cam.rotation_euler = (math.radians(88), 0, math.radians(4))
cam.data.lens = 45
scene.collection.objects.link(cam)
scene.camera = cam
bpy.ops.render.render(write_still=True)
print("PREVIEW", out)
