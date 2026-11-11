"""Blender-only smoke test of the independent interpreter against real node execution.

blender --factory-startup -b <pinned shaders.blend> --python this.py
Uses numeric EXR readback, not screenshots or visual acceptance.
"""
import bpy
import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0,str(Path(__file__).parent))
from formula_reference import Evaluation, vec

cases=json.loads(Path(__file__).with_name('formula_cases.json').read_text(encoding='utf-8'))
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=4
scene.render.resolution_y=4
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='OPEN_EXR'
scene.render.image_settings.color_depth='32'
camera=bpy.data.objects.new('FormulaCamera',bpy.data.cameras.new('FormulaCamera'))
scene.collection.objects.link(camera)
scene.camera=camera
# Remove geometry from the render; the world evaluates only constant attributes.
for obj in scene.objects:
    if obj!=camera:obj.hide_render=True
world=bpy.data.worlds.new('FormulaReferenceWorld')
scene.world=world
world.use_nodes=True
tree=world.node_tree
background=tree.nodes.get('Background')
selected=[c for c in cases if c['name'] in ['hair','hair_dual_False','hair_dual_True','iris','iris_9','iris_10',
                                         'skin_GetMaterialValueFace','skin_GetMaterialValueBodyJJM','skin_GetMaterialValueFaceEmissive']]
count=0
with tempfile.TemporaryDirectory(prefix='MeddleBlenderReference-') as folder:
    for case in selected:
        shader=case['metadata']['ShaderPackage']
        template=bpy.data.materials['meddle '+shader].node_tree.nodes['Group']
        node=tree.nodes.new('ShaderNodeGroup');node.node_tree=template.node_tree
        arguments=[]
        for socket in template.inputs:
            v=socket.default_value
            arguments.append(list(v) if hasattr(v,'__len__') else v)
        def tex(usage,uv=0):
            texture=case['textures'][usage]
            x=min(texture['width']-1,int(case['uv0' if uv==0 else 'uv1'][0]*texture['width']))
            return texture['pixels'][x*4:][:4]
        n=tex('g_SamplerNormal');m=tex('g_SamplerMask');d=tex('g_SamplerDiffuse')
        if shader=='hair.shpk':
            n1=tex('g_SamplerNormal',1);arguments[:6]=[n,n1,n[3],n1[3],m,m[3]]
        elif shader=='skin.shpk':arguments[1:7]=[d,d[3],n,n[3],m,m[3]]
        else:
            arguments[:4]=[d,n,m,case['vertex']]
            arguments[9]=[case['uv0'][0],1-case['uv0'][1],0]
        evaluation=Evaluation(case)
        evaluation.overrides(node.node_tree.name,arguments)
        for socket,value in zip(node.inputs,arguments):
            if socket.type=='RGBA':value=vec(value,4)
            socket.default_value=value
        tests=[0,3] if shader=='hair.shpk' else [0,1,7] if shader=='skin.shpk' else [0,6,7]
        from formula_reference import SNAPSHOT
        reference_tree=SNAPSHOT['materials']['meddle '+shader]
        for output in tests:
            expected=vec(evaluation.tree(reference_tree,'Group',output))
            tree.links.new(node.outputs[output],background.inputs['Color'])
            scene.render.filepath=str(Path(folder)/'value.exr')
            bpy.ops.render.render(write_still=True)
            image=bpy.data.images.load(scene.render.filepath,check_existing=False)
            actual=list(image.pixels)[:3]
            bpy.data.images.remove(image)
            assert all(abs(a-b)<max(.003,abs(b)*.003) for a,b in zip(actual,expected)),(case['name'],output,actual,expected)
            count+=1
        tree.nodes.remove(node)
print('MEDDLE_BLENDER_REFERENCE_PASSED',count)
