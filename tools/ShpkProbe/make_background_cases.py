"""Independent background oracle: execute extracted SM5, then decode its GBuffer."""
import copy
from functools import lru_cache
import json
from pathlib import Path
import sys
import numpy as np
from sm5_reference import execute

root = Path(sys.argv[1])
cases = []

@lru_cache(None)
def manifest_for(shader):
    return json.loads((root/shader/"manifest.json").read_text())

def make(shader, ps, multi=False, height=False, vertex=False, alpha=False, stain=None, normal=False):
    manifest = manifest_for(shader)
    d = {'ShaderPackage': shader+'.shpk', 'ApplyVertexColor': 'ApplyVertexColorOn' if vertex else 'ApplyVertexColorOff',
         'GetValues': 'GetAlphaMultiValues' if height else 'GetMultiValues' if multi else 'GetSingleValues',
         'ApplyAlphaTest': 'ApplyAlphaTestOn' if alpha else 'ApplyAlphaTestOff'}
    values = list(manifest['MaterialParamDefaults'])
    for p in manifest['MaterialParams']:
        name=p['Name'].split(' (')[0]
        if name != 'Unknown constant': d[name]=values[p['ByteOffset']//4:(p['ByteOffset']+p['ByteSize'])//4]
    d.update(g_DiffuseColor=[1.2,.8,.3],g_MultiDiffuseColor=[.4,1.3,.7],g_EmissiveColor=[.2,.1,.3],
             g_MultiEmissiveColor=[.1,.3,.2],g_SpecularColor=[.7]*3,g_MultiSpecularColor=[.2]*3,
             g_AlphaMultiParam=[.2,.3,0,2],g_AlphaThreshold=[.4],g_NormalScale=[.7],g_MultiNormalScale=[1.2],
             g_NormalUVScale=[2,3,4,5],g_ColorUVScale=[.7,.8,.9,1.1],g_SpecularUVScale=[1.2,.6,.8,.9])
    if stain is not None:d['BgStainLinear']=stain
    t={}
    def texture(k,rgba):t[k]=dict(width=1,height=1,pixels=rgba)
    texture('g_SamplerColorMap0',[.6,.4,.8,.7]);texture('g_SamplerSpecularMap0',[.4,.55,.3,.8])
    if multi:
        texture('g_SamplerColorMap1',[.3,.8,.2,.25]);texture('g_SamplerSpecularMap1',[.7,.35,.5,.6])
    if height or normal or shader=='bgcolorchange':
        texture('g_SamplerNormalMap0',[.6,.35,.8,.2])
        if multi:texture('g_SamplerNormalMap1',[.35,.6,.7,.9])
    return dict(name='',metadata=d,textures=t,uv0=[.2,.3],uv1=[.7,.8],vertex=[.2,.5,.8,.35],oracle=dict(shader=shader,ps=ps))

def oracle(c):
    shader=c['oracle']['shader'];index=c['oracle']['ps'];d=c['metadata']
    m=manifest_for(shader);p=m['Pixels'][index]
    # Identity object/camera basis: binormal +Y, normal +Z, tangent +X.
    regs={'v1':[0,0,1,.5],'v2':c['uv0']+c['uv1'],'v3':[0,0,0,1],'v4':c['vertex']}
    if shader=='bgskin':regs.update(v4=[0,0,0,1],v5=c['vertex'])
    if shader=='bgcolorchange' or index in [9,10]:
        regs.update(v2=[0,1,0,1],v3=c['uv0']+c['uv1'],v4=[0,0,0,1],v5=c['vertex'])
    for cb in p['Constants']:
        slot=cb['Slot'];name=cb['Name']
        for i in range(cb['Size']):regs[f'cb{slot}[{i}]']=[0]*4
        if name=='g_MaterialParameter':
            values=list(m['MaterialParamDefaults'])
            for param in m['MaterialParams']:
                key=param['Name'].split(' (')[0]
                if key in d:
                    v=d[key];o=param['ByteOffset']//4;values[o:o+len(v)]=v
            for i in range(len(values)//4):regs[f'cb{slot}[{i}]']=values[4*i:4*i+4]
        elif name=='g_CameraParameter':
            for i in range(3):regs[f'cb{slot}[{3+i}]']=[float(i==j) for j in range(4)]
        elif name=='g_InstancingData':
            regs[f'cb{slot}[4]']=d.get('BgStainLinear',[x*x for x in d.get('StainColor',[1,1,1])[:3]])[:3]+[d.get('BgEmissivePower',1)]
            color=d.get('LayoutColor',[0,0,0,0])
            regs[f'cb{slot}[5]']=[x*x for x in color[:3]]+[color[3]]
    textures={}
    for tex in p['Textures']:
        fixture=c['textures'][tex['Name']]
        def sample(uv,sampler,f=fixture):
            x=int(np.floor(uv[0]*f['width']))%f['width'];y=int(np.floor(uv[1]*f['height']))%f['height']
            return f['pixels'][(y*f['width']+x)*4:(y*f['width']+x)*4+4]
        textures[f"t{tex['Slot']}"]={'sample':sample}
    out=execute((root/shader/f'ps_{index}.asm').read_text(),regs,textures)
    if out is None:return dict(Opacity=0)
    result=dict(BaseColor=(np.array(out['o2'][:3])**2).tolist(),Roughness=out['o1'][1],
                Specular=.5*np.clip(out['o1'][0]*255/254,0,1),EmissiveColor=(np.array(out['o4'][:3])**2).tolist() if out['o4'][3]>0 else [0,0,0],Opacity=1)
    if shader=='bgcolorchange' or index in [9,10]:
        n=np.array(out['o0'][:3])*2-1
        if d.get('SourceTangentBasis')=='FFXIVBinormal':n=n[[1,0,2]]
        result['Normal']=(n*[1,-1,1]).tolist()
    return result

for shader in ['bg','terrain','bgskin','bgprop']:
    branches=[(3,False,False,False)] if shader=='bgprop' else [(3,False,False,False),(4,False,False,True),(5,True,False,False),(6,True,False,True),(7,True,True,False),(8,True,True,True)]
    for ps,multi,height,vertex in branches:
        for a in [0,.35,.5,1]:
            c=make(shader,ps,multi,height,vertex);c['vertex'][3]=a;cases.append(c)
for ps in [9,10]:cases.append(make('bg',ps,vertex=ps==10,normal=True))
for stain in [[1,1,1],[.25,.04,.64],[0,0,0]]:
    for mask in [0,.35,1]:
        c=make('bgcolorchange',3,stain=stain);c['textures']['g_SamplerColorMap0']['pixels'][3]=mask;cases.append(c)
for opacity in [.3,.4,.8]:
    c=make('bg',1927,alpha=True);c['textures']['g_SamplerColorMap0']['pixels'][3]=opacity;cases.append(c)
# Vary UV/scales independently with spatially varying textures.
c=make('bg',5,multi=True)
for t in c['textures'].values():t.update(width=2,height=2,pixels=t['pixels']+[.1,.2,.3,.4]+[.4,.3,.2,.1]+[.6,.7,.8,.9])
cases.append(c)
for shader in ['bg','bgcolorchange']:
    for blend in [0,.4,1]:
        for power in [0,.5,1]:
            c=make(shader,3);c['metadata'].update(LayoutColor=[2,.3,.7,blend],BgEmissivePower=power)
            cases.append(c)
    c=make(shader,3);c['metadata'].update(LayoutColor=[2,.3,.7,1],g_EmissiveColor=[0,0,0]);cases.append(c)
for shader in ['bg','bgcolorchange']:
    c=make(shader,3);c['metadata']['g_EmissiveColor']=[-.2,0,0];cases.append(c)
for mask in [0,.5,1]:
    c=make('bgcolorchange',3);c['metadata']['StainColor']=[.5,.2,.8,1]
    c['metadata']['SourceTangentBasis']='FFXIVBinormal'
    c['textures']['g_SamplerColorMap0']['pixels'][3]=mask;cases.append(c)
for i,c in enumerate(cases):
    c['name']=f'bg_shpk_{i}';c['expected']=oracle(c)
Path(sys.argv[2]).write_text(json.dumps(cases,indent=2))
print(f'{len(cases)} background cases / {sum(len(c["expected"]) for c in cases)} attributes')
