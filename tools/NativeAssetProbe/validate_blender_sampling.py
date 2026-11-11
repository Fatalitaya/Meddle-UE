"""Blender numeric regression for CLIP filtering and the two pinned Alpha groups.

Run Blender --factory-startup -b <pinned shaders.blend> --python this.py.
"""
import bpy
import tempfile
from pathlib import Path

scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=4
scene.render.resolution_y=4
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='OPEN_EXR'
scene.render.image_settings.color_depth='32'
camera=bpy.data.objects.new('SamplingCamera',bpy.data.cameras.new('SamplingCamera'))
scene.collection.objects.link(camera)
scene.camera=camera
for obj in scene.objects:
    if obj!=camera:obj.hide_render=True
world=bpy.data.worlds.new('SamplingWorld')
scene.world=world
world.use_nodes=True
tree=world.node_tree
background=tree.nodes['Background']
image=bpy.data.images.new('PackedData',width=1,height=1,float_buffer=True)
image.colorspace_settings.name='Non-Color'
image.alpha_mode='CHANNEL_PACKED'
image.pixels=[.3,.6,.2,.8]
sample=tree.nodes.new('ShaderNodeTexImage')
sample.image=image
sample.extension='CLIP'
sample.interpolation='Linear'
uv=tree.nodes.new('ShaderNodeCombineXYZ')
uv.inputs[1].default_value=.5
tree.links.new(uv.outputs[0],sample.inputs[0])
count=0
with tempfile.TemporaryDirectory(prefix='MeddleSampling-') as folder:
    scene.render.filepath=str(Path(folder)/'sample.exr')
    def check(socket,expected):
        global count
        tree.links.new(socket,background.inputs['Color'])
        bpy.ops.render.render(write_still=True)
        output=bpy.data.images.load(scene.render.filepath,check_existing=False)
        actual=list(output.pixels)[:3]
        bpy.data.images.remove(output)
        assert all(abs(a-b)<.002 for a,b in zip(actual,expected)),(actual,expected)
        count+=1
    for u in [-.1,0,.1,.5,.9,1,1.1]:
        uv.inputs[0].default_value=u
        weight=max(0,min(1,min(u,1-u)+.5))
        check(sample.outputs[0],[v*weight for v in [.3,.6,.2]])
    for name in ['alpha_threshold','alpha_threshold_div']:
        group=tree.nodes.new('ShaderNodeGroup')
        group.node_tree=bpy.data.node_groups[name]
        for threshold in [0,.5]:
            for alpha in [0,.5,1]:
                group.inputs[0].default_value=alpha
                group.inputs[1].default_value=threshold
                expected=alpha*(alpha>threshold) if name=='alpha_threshold' else (alpha/(threshold or 1) if alpha>0 else 0)
                check(group.outputs[0],[expected]*3)
        tree.nodes.remove(group)
print('MEDDLE_BLENDER_SAMPLING_PASSED',count)
