"""Generate GPU fixtures using the actual straight-line SHPK passes as the oracle.

Usage: python make_cases.py <ShpkProbe output> <output.json>
"""
import copy
import json
import sys
from pathlib import Path
import numpy as np
from sm5_reference import execute

root=Path(sys.argv[1])
def texture(pixels,width=1):
    return dict(width=width,height=len(pixels)//width,pixels=[v for p in pixels for v in p])
def sample(tex,uv,linear=False):
    a=np.array(tex['pixels']).reshape(tex['height'],tex['width'],4)
    x,y=uv[:2]*np.array([tex['width'],tex['height']])
    if not linear:return a[int(np.floor(y))%tex['height'],int(np.floor(x))%tex['width']]
    x-=.5;y-=.5; ix=int(np.floor(x));iy=int(np.floor(y));fx=x-ix;fy=y-iy
    return sum(a[max(0,min(tex['height']-1,iy+dy)),max(0,min(tex['width']-1,ix+dx))]*wx*wy
               for dx,wx in [(0,1-fx),(1,fx)] for dy,wy in [(0,1-fy),(1,fy)])

rows=[]
for i in range(32):
    rows.append(dict(Diffuse=dict(X=.2+i*.03,Y=.4,Z=.1),GlossStrength=float(i+1),
                     Specular=dict(X=.03,Y=.04,Z=.05),SpecularStrength=.2,
                     Emissive=dict(X=.1,Y=.2,Z=.3),TileIndex=float(i),TileAlpha=.2+i*.02,
                     TileMatrix=dict(UU=1.,UV=.25,VU=-.2,VV=2.)))
norm=[];orb=[]
for i in range(64):
    for pixel in range(4):
        norm.append([.4+i*.001,.52+pixel*.02,1.,1.])
        orb.append([.4,.5,.5+pixel*.03+i*.002,.8])
base=dict(name='',uv0=[.27,.38],uv1=[.65,.4],vertex=[.4,.64,.81,.8],
          metadata=dict(ShaderPackage='characterlegacy.shpk',g_AlphaThreshold=[.5],CategoryVertexColorMode='ValVertexColorEmissive',
                        ColorTable=dict(ColorTable=dict(Rows=rows)),DecalColor=[.3,.7,.2,.6]),
          textures={'g_SamplerNormal':texture([[.65,.4,.8,.7]]),'g_SamplerMask':texture([[.2,.3,.6,.9]]),
                    'g_SamplerDiffuse':texture([[.4,.5,.7,1.]]),'g_SamplerIndex':texture([[.2,.3,0.,1.]]),
                    'chara_tile_norm_array':texture(norm,2),'chara_tile_orb_array':texture(orb,2),
                    'Decal':texture([[.3,.5,.7,.6]])})

def oracle(case,shader,index):
    manifest=json.loads((root/shader/'manifest.json').read_text())
    meta=case['metadata'];tex=case['textures'];regs={'v1':case['vertex'],'v2':case['uv0']+case['uv1'],
        'v3':[0,0,1,0],'v4':[1,0,0,0],'v5':[0,1,0,0],'v6':[0,0,0,1],
        'v7':[0,0,0,1],'v8':[0,0,0,1]}
    bindings=manifest['Pixels'][index]
    for cb in bindings['Constants']:
        slot=cb['Slot']; name=cb['Name']
        for i in range(cb['Size']):regs[f'cb{slot}[{i}]']=[0,0,0,0]
        if name=='g_MaterialParameter':regs[f'cb{slot}[0]']=meta.get('g_DiffuseColor',[1,1,1])+[meta.get('g_AlphaThreshold',[.5])[0]]
        if name=='g_CameraParameter':
            for i in range(3):regs[f'cb{slot}[{55+i}]']=[float(j==i) for j in range(4)]
        if name=='g_CustomizeParameter':
            regs[f'cb{slot}[6]']=meta.get('OptionColor',[.2,.3,.4])+[0]
            regs[f'cb{slot}[2]']=[0,0,0,meta.get('FacePaintUVMultiplier',1)]
            regs[f'cb{slot}[3]']=[0,0,0,meta.get('FacePaintUVOffset',0)]
        if name=='g_DecalColor':regs[f'cb{slot}[0]']=meta['DecalColor']
    table=[]
    for r in meta['ColorTable']['ColorTable']['Rows']:
        col=lambda k:[r[k][c] for c in 'XYZ']
        table.extend([col('Diffuse')+[r['GlossStrength']],col('Specular')+[r['SpecularStrength']],
                      col('Emissive')+[0],[0]*4,[0]*4,[0]*4,[0,r['TileIndex']/64,r['TileAlpha'],0],
                      [r['TileMatrix'][k] for k in ['UU','UV','VU','VV']]])
    table=texture(table,8)
    names={'g_SamplerTileNormal':'chara_tile_norm_array','g_SamplerTileOrb':'chara_tile_orb_array','g_SamplerDecal':'Decal'}
    sampler_names={f"s{s['Slot']}":s['Name'] for s in bindings['Samplers']}
    textures={}
    for t in bindings['Textures']:
        name=t['Name'];fixture=table if name=='g_SamplerTable' else tex[names.get(name,name)]
        def sampling(uv,sampler,fixture=fixture,name=name):
            uv=uv.copy()
            if name in ['g_SamplerTileNormal','g_SamplerTileOrb']:uv[1]=(int(round(uv[2]))+uv[1]%1)/64
            return sample(fixture,uv, name=='g_SamplerTable' and sampler_names[sampler]=='g_SamplerTable')
        textures[f"t{t['Slot']}"]=dict(size=[fixture['width'],fixture['height'],1,1],sample=sampling)
    return execute((root/shader/f'ps_{index}.asm').read_text(),regs,textures)

cases=[]
# Independent geometric expectation, not derived from the interpreter.
c=copy.deepcopy(base);c['name']='shpk_flat_normal'
c['textures']['g_SamplerNormal']=texture([[.5,.5,.8,.7]])
c['textures']['chara_tile_norm_array']=texture([[.5,.5,1.,1.]]*256,2)
out=oracle(c,'characterlegacy',130)
assert np.allclose(out['o0'][:3],[.5,.5,1.]), out
c['expected']={'Normal':[0.,0.,1.]};cases.append(c)
for pair in range(16):
    for mix in [0.,.5,1.]:
        c=copy.deepcopy(base);c['name']=f'shpk_legacy_{pair}_{int(mix*2)}';c['textures']['g_SamplerIndex']=texture([[pair/15,mix,0.,1.]])
        out=oracle(c,'characterlegacy',130)
        c['expected']=dict(BaseColor=[x*x for x in out['o2'][:3]],Normal=[(x*2-1)*(1 if i!=1 else -1) for i,x in enumerate(out['o0'][:3])],Roughness=out['o1'][1])
        cases.append(c)
for mode,index in [('GetValuesCompatibility',135),('GetDecalColorRGBA',172),('GetDecalColorAlpha',214)]:
    c=copy.deepcopy(base);c['name']='shpk_'+mode
    c['metadata']['GetValues' if mode=='GetValuesCompatibility' else 'GetDecalColor']=mode
    out=oracle(c,'characterlegacy',index)
    c['expected']=dict(BaseColor=[x*x for x in out['o2'][:3]],Normal=[(x*2-1)*(1 if i!=1 else -1) for i,x in enumerate(out['o0'][:3])],Roughness=out['o1'][1])
    cases.append(c)
c=copy.deepcopy(base);c['name']='shpk_vertex_off';c['metadata']['CategoryVertexColorMode']='ValVertexColorOff'
out=oracle(c,'characterlegacy',1)
c['expected']={'BaseColor':[x*x for x in out['o2'][:3]]};cases.append(c)
for threshold in [0.,.5]:
    for alpha in [.2,.625,.9]:
        c=copy.deepcopy(base);c['name']=f'shpk_alpha_{threshold}_{alpha}'.replace('.','_')
        c['metadata']['g_AlphaThreshold']=[threshold];c['textures']['g_SamplerNormal']['pixels'][2]=alpha
        out=oracle(c,'characterlegacy',151)
        c['expected']={'Opacity':0. if out is None else 1.};cases.append(c)
for shader in ['charactertattoo','characterocclusion']:
    for alpha in [0.,.0005,.002,.5,1.]:
        for decal in ([False,True] if shader=='charactertattoo' else [False]):
            c=copy.deepcopy(base);c['name']=f'shpk_{shader}_{str(alpha).replace(".","_")}_{decal}'
            c['metadata'].update(ShaderPackage=shader+'.shpk',OptionColor=[.2,.3,.4],g_DiffuseColor=[.7,1.2,.9],FacePaintUVMultiplier=1.,FacePaintUVOffset=0.)
            c['textures']['g_SamplerNormal']=texture([[.7,.2,.6,alpha]])
            if decal:c['metadata']['GetDecalColor']='GetDecalColorAlpha'
            out=oracle(c,shader,1 if decal else 0)
            if shader=='charactertattoo':
                # Discarded color is undefined; only check coverage for those pixels.
                c['expected']={'Opacity':0. if out is None else out['o2'][3]}
                if out:c['expected']['BaseColor']=[x*x for x in out['o2'][:3]]
            else:c['expected']={'EmissiveColor':[1,1,1] if out is None else out['o2'][:3]}
            cases.append(c)
Path(sys.argv[2]).write_text(json.dumps(cases,indent=2))
print(f'{len(cases)} SHPK oracle cases written to {sys.argv[2]}')
