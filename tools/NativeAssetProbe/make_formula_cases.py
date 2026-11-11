"""Generate synthetic fixtures and expected attributes from the pinned socket graph."""
import copy
import json
from pathlib import Path
from formula_reference import Evaluation

def texture(*pixels):
    return dict(width=len(pixels),height=1,pixels=[c for p in pixels for c in p])

rows=[]
for i in range(32):
    rows.append(dict(Diffuse=dict(X=1+i,Y=.2,Z=.3),Emissive=dict(X=.1*i,Y=.2,Z=.3),
                     Metalness=i/32,Roughness=i/16,GlossStrength=4,SpecularStrength=.5,
                     TileIndex=i,TileAlpha=.5,TileMatrix=dict(UU=1,UV=0,VU=0,VV=1)))
base=dict(name='',uv0=[.25,.3],uv1=[.75,.6],vertex=[1,0,0,1],
          metadata=dict(g_DiffuseColor=[2,1,4],MainColor=[2,.2,.1],MeshColor=[.1,.3,3],
                        SkinColor=[.6,.3,.2],LipColor=[.4,.01,.02,.7],
                        LeftIrisColor=[2,.1,.2,1],RightIrisColor=[.1,.2,2,.5],
                        g_WhiteEyeColor=[.77,.77,.77],g_AlphaThreshold=[.5],
                        g_IrisRingUvRadius=[.2,.4],g_IrisRingUvFadeWidth=[.1,.1],
                        g_IrisRingColor=[3,2,1],g_IrisRingEmissiveIntensity=[2],
                        ColorTable=dict(ColorTable=dict(Rows=rows))),
          textures={k:texture(v) for k,v in {
              'g_SamplerDiffuse':[.3,.5,.8,.7], 'g_SamplerNormal':[.7,.4,.75,.6],
              'g_SamplerMask':[.2,.4,.6,.8], 'g_SamplerIndex':[.25,.5,0,1],
              'chara_tile_norm_array':[.4,.6,1,.5], 'chara_tile_orb_array':[.2,.3,.7,1]}.items()})
cases=[]
def add(shader,name,edit=lambda c:None):
    c=copy.deepcopy(base);c['name']=name;c['metadata']['ShaderPackage']=shader+'.shpk';edit(c)
    c['expected']=Evaluation(c).outputs()
    if shader=='hair':
        # Normal and coverage now follow SHPK, validated by ShpkProbe/make_hair_cases.py.
        del c['expected']['Normal'];del c['expected']['Opacity']
    cases.append(c)

for shader in ['hair','iris','skin','character']: add(shader,shader)
for value in ['GetMaterialValueFace','GetMaterialValueBodyJJM','GetMaterialValueFaceEmissive']:
    def skin(c):
        c['metadata']['GetMaterialValue']=value;c['metadata']['g_EmissiveColor']=[3,.2,.1]
    add('skin','skin_'+value,skin)
for face in [False,True]:
    def hair(c):
        c['textures']['g_SamplerNormal']=texture([.7,.4,0,.2],[.7,.4,1,.8])
        c['metadata']['GetSubColor']='GetSubColorFace' if face else 'GetSubColorHair'
    add('hair','hair_dual_'+str(face),hair)
for v in [[0,1,0,1],[.5,.5,0,1]]:
    add('iris','iris_'+str(len(cases)),lambda c:c.update(vertex=v))
for t in [0,.5]:
    for a in [0,.5,1]:
        for shader in ['hair','skin','character']:
            def alpha(c):
                c['metadata']['g_AlphaThreshold']=[t]
                c['textures']['g_SamplerNormal']=texture([.7,.4,a,a])
                c['textures']['g_SamplerDiffuse']=texture([.3,.5,.8,a])
            add(shader,f'{shader}_alpha_{t}_{a}'.replace('.','_'),alpha)
for r in sorted(set([0,.0625-1e-6,.0625,.5,1]+[i/16 for i in range(16)])):
    for g in [0,.49999,.5,1]:
        add('character',f'table_{r}_{g}'.replace('.','_').replace('-','_'),
            lambda c:c['textures'].update(g_SamplerIndex=texture([r,g,0,1])))
for uv in [[.5,.5],[.7,.5],[.8,.5],[.9,.5],[1,.5]]:
    add('iris','iris_ring_'+str(len(cases)),lambda c:c.update(uv0=uv))
add('character','compatibility',lambda c:c['metadata'].update(GetValues='GetValuesCompatibility'))
for shader in ['skin','character']:
    for u in [-.01,0,.5,1,1.01]:
        def decal(c):
            c['uv1']=[u,.5]
            c['metadata'].update(DecalColor=[.8,.1,2,.75],FacePaintUVMultiplier=1,FacePaintUVOffset=0)
            c['textures']['Decal']=texture([.3,.6,.2,.8])
        add(shader,f'{shader}_decal_{u}'.replace('.','_').replace('-','minus'),decal)
for shader in ['skin','character']:
    def tile(c):
        c['textures']['chara_tile_orb_array']=dict(width=1,height=64,pixels=[x for i in range(64) for x in [.1,.2,i/64,1]])
        c['textures']['chara_tile_norm_array']=dict(width=1,height=64,pixels=[x for i in range(64) for x in [.2+i/100,.3,1,.5]])
        c['metadata']['g_TileIndex']=[21]
        c['uv0']=[-.2,1.2]
    add(shader,shader+'_tile',tile)
Path(__file__).with_name('formula_cases.json').write_text(json.dumps(cases,indent=2),encoding='utf-8')
print(f'{len(cases)} independent reference cases generated')
