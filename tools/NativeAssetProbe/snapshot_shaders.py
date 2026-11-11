"""Run with Blender -b shaders.blend --python this.py -- output.json.

Evidence only: this is not a production Blender graph converter.
Source: PassiveModding/MeddleTools, AGPL-3.0, commit below.
"""
import bpy
import json
import sys
import hashlib

REFERENCE_SHA256 = '4031183acbd6a2e59958bf1cf29716aac519cba5f5f1b3ac20a7fc3354776704'
digest = hashlib.sha256(open(bpy.data.filepath, 'rb').read()).hexdigest()
if digest != REFERENCE_SHA256:
    raise ValueError('Expected shaders.blend from MeddleTools 56aa1252, got SHA256 ' + digest)

def value(v):
    if isinstance(v, (str, bool, int, float)):
        return v
    try:
        return list(v)
    except TypeError:
        return None

def socket(s):
    return dict(name=s.name, type=s.type, default=value(getattr(s, 'default_value', None)))

def tree(t):
    nodes = []
    for n in t.nodes:
        d = dict(name=n.name, label=n.label, type=n.bl_idname,
                 inputs=[socket(s) for s in n.inputs], outputs=[socket(s) for s in n.outputs])
        for key in ['operation', 'blend_type', 'data_type', 'clamp_factor', 'clamp_result', 'use_clamp',
                    'interpolation', 'extension', 'uv_map', 'space', 'layer_name', 'gradient_type', 'vector_type', 'interpolation_type', 'clamp']:
            if hasattr(n, key):
                d[key] = getattr(n, key)
        if getattr(n, 'node_tree', None):
            d['group'] = n.node_tree.name
        if hasattr(n, 'color_ramp'):
            d['ramp'] = dict(interpolation=n.color_ramp.interpolation,
                             elements=[(e.position, list(e.color)) for e in n.color_ramp.elements])
        nodes.append(d)
    return dict(nodes=nodes, links=[(l.from_node.name, list(l.from_node.outputs).index(l.from_socket),
                                    l.to_node.name, list(l.to_node.inputs).index(l.to_socket)) for l in t.links])

materials = {m.name: tree(m.node_tree) for m in bpy.data.materials
             if m.name in ['meddle ' + k + '.shpk' for k in ['character', 'skin', 'hair', 'iris']]}
groups = {}
def visit(t):
    for n in t['nodes']:
        g = n.get('group')
        if g and g not in groups:
            groups[g] = tree(bpy.data.node_groups[g])
            visit(groups[g])
for t in materials.values():
    visit(t)
result = dict(commit='56aa1252ba341fbc732df6e79b95e388d2057ba0', blender=bpy.app.version_string,
              sha256=digest,
              materials=materials, groups=groups)
with open(sys.argv[sys.argv.index('--') + 1], 'w', encoding='utf-8') as f:
    json.dump(result, f, ensure_ascii=False, indent=2)
