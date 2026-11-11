"""SHPK normal/core-alpha oracle, plus independent UE edge-coverage boundaries."""
import copy
import json
from pathlib import Path
import sys
import numpy as np
from sm5_reference import execute

root = Path(sys.argv[1]) / 'hair'
manifest = json.loads((root / 'manifest.json').read_text())

def tex(pixels):
    return dict(width=len(pixels), height=1, pixels=sum(pixels, []))

base = dict(name='', uv0=[.25,.5], uv1=[.75,.5], vertex=[.7,.5,.3,.4],
    metadata=dict(ShaderPackage='hair.shpk', g_AlphaThreshold=[.75], g_NormalScale=[.5],
                  g_DiffuseColor=[1.4]*3, IsTransparent=True, **{'0xAD94E254':[1]}),
    textures={'g_SamplerNormal':tex([[.65,.3,.1,1],[.2,.7,.9,1]]),
              'g_SamplerMask':tex([[.2,.4,.6,1]])})

def oracle(case, index):
    shader=manifest['Pixels'][index]
    regs={'v1':case['vertex'],'v2':case['uv0']+case['uv1'],
          'v3':[0,0,1,0],'v4':[1,0,0,0],'v5':[0,1,0,0],
          'v6':[0,0,0,1],'v7':[0,0,0,1],'v8':[0,0,0,1],'v9':[0,0,0,1]}
    for cb in shader['Constants']:
        slot=cb['Slot']; name=cb['Name']
        for i in range(cb['Size']): regs[f'cb{slot}[{i}]']=[0]*4
        if name=='g_MaterialParameter':
            values=list(manifest['MaterialParamDefaults'])
            for parameter in manifest['MaterialParams']:
                key=parameter['Name'].split(' (')[0]
                if key=='Unknown constant':key=f"0x{parameter['Id']:08X}"
                if key in case['metadata']:
                    offset=parameter['ByteOffset']//4
                    for i,value in enumerate(case['metadata'][key]):values[offset+i]=value
            for i in range(len(values)//4):regs[f'cb{slot}[{i}]']=values[i*4:i*4+4]
        elif name=='g_CameraParameter':
            for i in range(3):regs[f'cb{slot}[{55+i}]']=[float(i==j) for j in range(4)]
    textures={}
    for texture in shader['Textures']:
        fixture=case['textures'].get(texture['Name'],tex([[0,0,0,0]]))
        def sample(uv,sampler,fixture=fixture):
            i=int(np.floor(uv[0]*fixture['width']))%fixture['width']
            return fixture['pixels'][i*4:i*4+4]
        textures[f"t{texture['Slot']}"]=dict(size=[fixture['width'],1,1,1],sample=sample)
    return execute((root/f'ps_{index}.asm').read_text(),regs,textures)

cases=[]
for face in [False,True]:
    for basis in [False,True]:
        for scale in [0.,.5,1.,2.]:
            for xy in [[.5,.5],[.65,.3],[.2,.7],[1.,1.]]:
                c=copy.deepcopy(base); c['name']=f'hair_shpk_normal_{len(cases)}'
                c['metadata']['g_NormalScale']=[scale]
                if face:c['metadata']['GetSubColor']='GetSubColorFace'
                if basis:c['metadata']['SourceTangentBasis']='FFXIVBinormal'
                c['textures']['g_SamplerNormal']['pixels'][:2]=xy
                out=oracle(c,7 if face else 1)
                n=[v*2-1 for v in out['o0'][:3]]
                expected=[n[1],-n[0],n[2]] if basis else [n[0],-n[1],n[2]]
                if xy==[.5,.5]:assert np.allclose(expected,[0,0,1])
                c['expected']={'Normal':expected};cases.append(c)
for face in [False,True]:
    for transparent in [False,True]:
        for alpha in [0.,.003,1/255,.02,16/255,.3,.749,.75,1.]:
            for override in [0.,1.]:
                c=copy.deepcopy(base);c['name']=f'hair_shpk_alpha_{len(cases)}'
                c['metadata']['IsTransparent']=transparent
                c['metadata']['0xAD94E254']=[override]
                if face:c['metadata']['GetSubColor']='GetSubColorFace'
                c['textures']['g_SamplerNormal']['pixels'][3 if face else 7]=alpha
                a=alpha*(c['vertex'][3]*(1-override)+override)
                if transparent:expected=0. if a<1/255 else min(1.,a/.75)
                else:expected=0. if oracle(c,7 if face else 1) is None else 1.
                c['expected']={'Opacity':expected};cases.append(c)
Path(sys.argv[2]).write_text(json.dumps(cases,indent=2))
print(f'{len(cases)} hair normal/coverage cases')
