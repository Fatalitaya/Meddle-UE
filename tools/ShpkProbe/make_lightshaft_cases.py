"""Execute extracted lightshaft PS0 as the independent RGB oracle.

VS interpolants are fixed for this pixel-stage test. Native full materials also
compile the production instance projection and angular vertex interpolators.
"""
import json
from pathlib import Path
import sys
from sm5_reference import execute

root=Path(sys.argv[1]);cases=[]
asm=(root/'lightshaft/ps_0.asm').read_text()
for kind in ['Type0','Type1']:
    for alpha,attenuation in [(0,.35),(1,.35),(.5,0),(.5,1)]:
        color=[1.2,.8,2.4]
        pixels=[[.6,.2,.8,alpha],[.3,.9,.4,1-alpha]]
        regs={'v1':[.2,.3,.2,.3], 'v2':[x*attenuation for x in color]+[0]}
        textures={f't{i}':{'sample':lambda uv,sampler,p=p:p} for i,p in enumerate(pixels)}
        result=execute(asm,regs,textures)['o0']
        assert result[3]==0
        cases.append(dict(name=f'lightshaft_shpk_{len(cases)}',
            metadata=dict(ShaderPackage='lightshaft.shpk',Type=kind,IsTransparent=False,g_Color=color),
            textures={f'g_Sampler{i}':dict(width=1,height=1,pixels=p) for i,p in enumerate(pixels)},
            uv0=[.2,.3],uv1=[.2,.3],vertex=[1,1,1,attenuation],lightshaftAttenuation=attenuation,
            expected=dict(EmissiveColor=list(result[:3]),Opacity=1)))
Path(sys.argv[2]).write_text(json.dumps(cases,indent=2))
print(f'{len(cases)} lightshaft PS0 cases')
