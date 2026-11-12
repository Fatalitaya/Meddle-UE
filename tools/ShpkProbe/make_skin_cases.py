"""Run actual skin PS bytecode arithmetic as an independent attribute oracle."""
import copy
import json
from pathlib import Path
import sys
import numpy as np
from sm5_reference import execute

root=Path(sys.argv[1])/'skin'
manifest=json.loads((root/'manifest.json').read_text())

def tex(rgba):
    return dict(width=1,height=1,pixels=rgba)

base=dict(name='',uv0=[.25,.3],uv1=[.7,.8],vertex=[.36,.49,.64,.8],
    metadata=dict(ShaderPackage='skin.shpk',SkinColor=[.6,.3,.2],MuscleTone=.35,
                  LipColor=[.5,.05,.08,.7],MainColor=[.1,.2,.3],MeshColor=[.4,.5,.6],
                  DecalColor=[.1,.5,.3,.4],FacePaintUVMultiplier=1,FacePaintUVOffset=0,
                  g_DiffuseColor=[1.2,.9,.7],g_TileScale=[16,16],g_TileAlpha=[.8],g_TileIndex=[0],
                  g_LipRoughnessScale=[.7],g_AlphaThreshold=[0]),
    textures={'g_SamplerNormal':tex([.65,.3,.8,.6]),'g_SamplerMask':tex([.4,.55,.3,.7]),
              'g_SamplerDiffuse':tex([.6,.4,.7,1]),'Decal':tex([.3,.9,.2,.8]),
              'chara_tile_norm_array':tex([.7,.6,.2,0]),'chara_tile_orb_array':tex([.7,.25,.8,.4])})

def oracle(case,index):
    shader=manifest['Pixels'][index];d=case['metadata']
    regs={'v1':case['vertex'],'v2':case['uv0']+case['uv1'],'v3':[0,0,1,0],
          'v4':[1,0,0,0],'v5':[0,1,0,0],'v6':[0,0,0,0],
          'v7':[0,0,0,1],'v8':[0,0,0,1]}
    for cb in shader['Constants']:
        slot=cb['Slot'];name=cb['Name']
        for i in range(cb['Size']):regs[f'cb{slot}[{i}]']=[0]*4
        if name=='g_MaterialParameter':
            values=list(manifest['MaterialParamDefaults'])
            for p in manifest['MaterialParams']:
                key=p['Name'].split(' (')[0]
                if key in d:
                    v=d[key];v=v if isinstance(v,list) else [v]
                    offset=p['ByteOffset']//4;values[offset:offset+len(v)]=v
            for i in range(len(values)//4):regs[f'cb{slot}[{i}]']=values[4*i:4*i+4]
        elif name=='g_CustomizeParameter':
            regs[f'cb{slot}[0]']=d['SkinColor']+[d['MuscleTone']]
            regs[f'cb{slot}[1]']=d['LipColor']
            regs[f'cb{slot}[2]']=d['MainColor']+[d['FacePaintUVMultiplier']]
            regs[f'cb{slot}[3]']=d['MeshColor']+[d['FacePaintUVOffset']]
        elif name=='g_DecalColor':regs[f'cb{slot}[0]']=d['DecalColor']
        elif name=='g_CameraParameter':
            for i in range(3):regs[f'cb{slot}[{55+i}]']=[float(i==j) for j in range(4)]
    aliases={'g_SamplerTileNormal':'chara_tile_norm_array','g_SamplerTileOrb':'chara_tile_orb_array','g_SamplerDecal':'Decal'}
    textures={}
    for t in shader['Textures']:
        if t['Name']=='g_ShaderTypeParameter':
            textures[f"t{t['Slot']}"]={'load':lambda index,offset:[0]*4};continue
        fixture=case['textures'].get(aliases.get(t['Name'],t['Name']),tex([.5,.5,0,0]))
        def sample(uv,sampler,fixture=fixture):
            # Normalized tile coordinate already includes the array layer in Z.
            w=fixture['width'];h=fixture['height'];array=h==128
            x=int(np.floor(uv[0]*w))%w
            y=(int(uv[2])*2+int(np.floor(uv[1]*2))%2) if array else int(np.floor(uv[1]*h))%h
            return fixture['pixels'][(y*w+x)*4:(y*w+x)*4+4]
        textures[f"t{t['Slot']}"]={'sample':sample}
    text=(root/f'ps_{index}.asm').read_text()
    if index==19:
        # Isolate the emissive surface term before the game's lighting-dependent gain.
        text=text[:text.index('round_z r2.z')]+ '\nmov o0.xyz, r1.xyzx\nret\n'
        return dict(EmissiveColor=execute(text,regs,textures)['o0'][:3])
    out=execute(text,regs,textures)
    normal=np.array(out['o0'][:3])*2-1
    normal=normal[[1,0,2]]*[1,-1,1] if d.get('SourceTangentBasis')=='FFXIVBinormal' else normal*[1,-1,1]
    return dict(BaseColor=(np.array(out['o2'][:3])**2).tolist(),Normal=normal.tolist(),
                Roughness=out['o1'][1],Specular=.5*np.clip(out['o1'][0]*255/254,0,1))

cases=[]
for branch,index in [('Face',0),('Body',6),('BodyJJM',10),('FaceEmissive',17),('Face2',14)]:
    for tile_g in [0.,.5,1.]:
        for basis in [False,True]:
            c=copy.deepcopy(base);c['metadata']['GetMaterialValue']='GetMaterialValue'+branch
            c['textures']['chara_tile_orb_array']['pixels'][1]=tile_g
            if basis:c['metadata']['SourceTangentBasis']='FFXIVBinormal'
            c['expected']=oracle(c,index);cases.append(c)
    for parameter,value in [('g_TileAlpha',0.),('g_TileAlpha',2.),('MuscleTone',0.),('MuscleTone',2.),('g_LipRoughnessScale',0.)]:
        c=copy.deepcopy(base);c['metadata']['GetMaterialValue']='GetMaterialValue'+branch
        c['metadata'][parameter]=value if parameter=='MuscleTone' else [value]
        c['expected']=oracle(c,index);cases.append(c)
    c=copy.deepcopy(base);c['metadata'].update(GetMaterialValue='GetMaterialValue'+branch,g_TileIndex=[2],g_TileScale=[3,5])
    for usage in ['chara_tile_norm_array','chara_tile_orb_array']:
        c['textures'][usage]=dict(width=2,height=128,pixels=[v for row in range(128) for x in range(2)
            for v in ([.55+x*.05,.6,.3,1] if 'norm' in usage else [.8,.2+(row%2)*.5,.4+x*.3,.25+(row//2)*.01])])
    c['expected']=oracle(c,index);cases.append(c)
    c=copy.deepcopy(base);c['metadata']['GetMaterialValue']='GetMaterialValue'+branch
    c['textures']['g_SamplerMask']['pixels'][2]=1 # BodyJJM disables tile on this boundary.
    c['textures']['g_SamplerNormal']['pixels'][:2]=[1,1] # Z reconstruction floor.
    c['expected']=oracle(c,index);cases.append(c)
for color in [[0,0,0],[.5,1,2],[3,.2,.1]]:
    c=copy.deepcopy(base);c['metadata'].update(GetMaterialValue='GetMaterialValueFaceEmissive',g_EmissiveColor=color)
    c['expected']=oracle(c,19);cases.append(c)
for i,c in enumerate(cases):c['name']=f'skin_shpk_{i}'
Path(sys.argv[2]).write_text(json.dumps(cases,indent=2))
print(f'{len(cases)} skin SHPK cases / {sum(len(c["expected"]) for c in cases)} attributes')
