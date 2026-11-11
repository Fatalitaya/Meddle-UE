"""Independent numeric interpreter of the pinned node snapshot (test-only).

This walks Blender socket links, not the production HLSL. Unsupported nodes fail.
Normal Map is evaluated in an orthonormal tangent frame; UE reflects its Y axis.
"""
import json
import math
from pathlib import Path

SNAPSHOT = json.loads(Path(__file__).with_name('shaders.snapshot.json').read_text(encoding='utf-8'))

def scalar(x):
    return sum(a*b for a,b in zip(x, [.2126,.7152,.0722])) if isinstance(x, list) else float(x)

def vec(x, count=3):
    return (x + [1]*count)[:count] if isinstance(x, list) else [float(x)]*count

def sat(x):
    return min(1, max(0, x))

def div(x,y):
    return x/y if y else 0

def mix(a,b,t):
    t=sat(t)
    if isinstance(a,list) or isinstance(b,list):
        return [x*(1-t)+y*t for x,y in zip(vec(a,4),vec(b,4))]
    return a*(1-t)+b*t

class Evaluation:
    def __init__(self, case):
        self.case=case
        self.metadata=case['metadata']

    def tree(self, tree, name, output, arguments=None):
        nodes={n['name']:n for n in tree['nodes']}
        links={(b,j):(a,i) for a,i,b,j in tree['links']}
        cache={}
        def get(name, output):
            if (name,output) in cache: return cache[name,output]
            n=nodes[name]; typ=n['type']
            def inp(i):
                x=get(*links[name,i]) if (name,i) in links else n['inputs'][i]['default']
                return scalar(x) if n['inputs'][i]['type'] in ['VALUE','BOOLEAN'] else x
            if typ=='NodeGroupInput': r=arguments[output]
            elif typ=='NodeReroute': r=inp(0)
            elif typ=='ShaderNodeGroup':
                args=[inp(i) for i in range(len(n['inputs']))]
                connected=args.copy()
                self.overrides(n['group'],args)
                # node_configs changes default_value only; a connected socket still
                # takes its value from the link (notably equipment TileIndex/Alpha).
                for i in range(len(args)):
                    if (name,i) in links:args[i]=connected[i]
                t=SNAPSHOT['groups'][n['group']]
                o=next(x for x in t['nodes'] if x['type']=='NodeGroupOutput')
                src=next(((a,i) for a,i,b,j in t['links'] if b==o['name'] and j==output),None)
                r=self.tree(t,*src,args) if src else o['inputs'][output]['default']
            elif typ in ['ShaderNodeSeparateColor','ShaderNodeSeparateXYZ']: r=vec(inp(0))[output]
            elif typ in ['ShaderNodeCombineColor','ShaderNodeCombineXYZ']: r=[inp(i) for i in range(3)]
            elif typ=='ShaderNodeMath':
                a,b,c=[inp(i) for i in range(3)]; op=n['operation']
                r={'ADD':lambda:a+b,'SUBTRACT':lambda:a-b,'MULTIPLY':lambda:a*b,'DIVIDE':lambda:div(a,b),
                   'LESS_THAN':lambda:float(a<b),'GREATER_THAN':lambda:float(a>b),'COMPARE':lambda:float(abs(a-b)<=c),
                   'ROUND':lambda:math.floor(a+.5),'FLOOR':lambda:math.floor(a),'POWER':lambda:a**b}[op]()
                if n['use_clamp']: r=sat(r)
            elif typ=='ShaderNodeMix':
                t=inp(0); a,b=[inp(i) for i in ([6,7] if n['data_type']=='RGBA' else [2,3])]
                if n['blend_type']=='MULTIPLY': b=[x*y for x,y in zip(vec(a,4),vec(b,4))]
                elif n['blend_type']!='MIX': raise ValueError(n)
                r=mix(a,b,t)
            elif typ=='ShaderNodeNormalMap':
                assert inp(0)==1
                v=[2*x-1 for x in vec(inp(1))]; l=math.sqrt(sum(x*x for x in v));r=[x/l for x in v]
            elif typ=='ShaderNodeVectorMath':
                a,b=vec(inp(0)),vec(inp(1));op=n['operation']
                r=[{'ADD':lambda:x+y,'MULTIPLY':lambda:x*y,'MODULO':lambda:math.fmod(x,y) if y else 0}[op]() for x,y in zip(a,b)]
            elif typ=='ShaderNodeMapping':
                v,loc,rotation,scale=[vec(inp(i)) for i in range(4)];assert rotation==[0,0,0]
                r=[div(x-l,s) if n['vector_type']=='TEXTURE' else x*s+(0 if n['vector_type']=='VECTOR' else l) for x,l,s in zip(v,loc,scale)]
            elif typ=='ShaderNodeTexGradient':
                assert n['gradient_type']=='SPHERICAL';r=max(1-math.sqrt(sum(x*x for x in vec(inp(0)))),0)
                if output==0:r=[r,r,r,1]
            elif typ=='ShaderNodeMapRange':
                if n['data_type']=='FLOAT':
                    x,a,b,c,d=[inp(i) for i in range(5)];r=c+(d-c)*div(x-a,b-a)
                    if n['clamp']:r=min(max(c,d),max(min(c,d),r))
                else:
                    values=[vec(inp(i)) for i in range(6,11)]
                    r=[c+(d-c)*div(x-a,b-a) for x,a,b,c,d in zip(*values)]
                    if n['clamp']:r=[min(max(c,d),max(min(c,d),x)) for x,c,d in zip(r,values[3],values[4])]
            elif typ=='ShaderNodeUVMap':
                uv=self.case['uv1' if n['label']=='UVMap.001' else 'uv0'];r=[uv[0],1-uv[1],0]
            elif typ=='ShaderNodeVertexColor':r=self.case['vertex']
            elif typ=='ShaderNodeTexImage':
                usage=n['label'].removesuffix('_PngCachePath');texture=self.case['textures'].get(usage)
                if texture is None: r=[0,0,0,0]
                else:
                    uv=inp(0) if (name,0) in links else [self.case['uv0'][0],1-self.case['uv0'][1],0]
                    u,v=uv[:2]
                    if usage=='Decal':
                        # Independent four-tap border filtering, not production's coverage shortcut.
                        x=u*texture['width']-.5;y=(1-v)*texture['height']-.5
                        x0=math.floor(x);y0=math.floor(y);r=[0,0,0,0]
                        for xx,wx in [(x0,1-(x-x0)),(x0+1,x-x0)]:
                            for yy,wy in [(y0,1-(y-y0)),(y0+1,y-y0)]:
                                if 0<=xx<texture['width'] and 0<=yy<texture['height']:
                                    pixel=texture['pixels'][(yy*texture['width']+xx)*4:][:4]
                                    r=[a+b*wx*wy for a,b in zip(r,pixel)]
                    else:
                        u=u%1;v=(1-v)%1
                        x=min(texture['width']-1,int(u*texture['width']));y=min(texture['height']-1,int(v*texture['height']))
                        r=texture['pixels'][(y*texture['width']+x)*4:][:4]
                if output==1:r=r[3]
            elif typ=='ShaderNodeValToRGB':
                assert n['ramp']['interpolation']=='CONSTANT'
                row=min(15,max(0,math.floor(inp(0)*16)))*2+(1 if n['label'].endswith('B') else 0)
                data=self.metadata['ColorTable']['ColorTable']['Rows'][row]; label=n['label']
                key='Diffuse' if label.startswith('ColorRamp') else 'Emissive' if label.startswith('EmissionRamp') else None
                if key:r=[data[key][c] for c in ['X','Y','Z']]+[1]
                elif label.startswith('MetalnessRoughness'):r=[data[k] for k in ['Metalness','Roughness','GlossStrength','SpecularStrength']]
                elif label.startswith('TileMatrix'):r=[data['TileMatrix'][k] for k in ['UU','UV','VU','VV']]
                elif label.startswith('TileProperties'):r=[data['TileIndex'],data['TileAlpha'],1,1]
                elif label.startswith('SpecularRamp'):r=[1,1,1,0]
                else: raise ValueError(label)
                if output==1:r=r[3]
            else: raise ValueError((name,typ))
            cache[name,output]=r
            return r
        return get(name,output)

    def overrides(self,group,args):
        # node_configs.py maps only material-level group instances, not nested groups.
        m=self.metadata
        maps={
            'meddle hair.shpk':{6:'MainColor',7:'MeshColor',10:'g_DiffuseColor'},
            'meddle skin.shpk':{0:'g_DiffuseColor',7:'SkinColor',8:'LipColor',10:'MainColor',11:'MeshColor',12:'DecalColor',19:'g_EmissiveColor'},
            'meddle iris.shpk':{4:'g_WhiteEyeColor',5:'LeftIrisColor',6:'RightIrisColor',10:'g_IrisRingUvRadius',11:'g_IrisRingUvFadeWidth',12:'g_IrisRingColor',13:'g_IrisRingEmissiveIntensity'},
            'tile_select':{2:'g_TileIndex',3:'g_TileAlpha'},
            'meddle decaluv':{1:'FacePaintUVMultiplier',2:'FacePaintUVOffset'},
            'alpha_threshold':{1:'g_AlphaThreshold'},'alpha_threshold_div':{1:'g_AlphaThreshold'}}
        for i,k in maps.get(group,{}).items():
            if k in m:args[i]=m[k] if isinstance(args[i],list) else scalar(m[k][0] if isinstance(m[k],list) else m[k])
        if group=='meddle hair.shpk':args[9]=m.get('GetSubColor')=='GetSubColorFace'
        if group=='meddle skin.shpk':
            for i,k in [(9,'LipColor'),(13,'DecalColor')]:
                if k in m:args[i]=m[k][3]
            for i,k in [(15,'GetMaterialValueFace'),(16,'GetMaterialValueBodyJJM'),(17,'GetMaterialValueFaceEmissive')]:args[i]=m.get('GetMaterialValue')==k
        if group=='meddle iris.shpk':
            for i,k in [(7,'LeftIrisColor'),(8,'RightIrisColor')]:
                if k in m:args[i]=m[k][3]
        if group=='meddle character.shpk':args[11]=m.get('GetValues')=='GetValuesCompatibility'
        if group=='tile_select' and 'g_TileScale' in m:args[4:6]=m['g_TileScale'][:2]

    def outputs(self):
        t=SNAPSHOT['materials']['meddle '+self.case['metadata']['ShaderPackage']]
        bsdf=next(n for n in t['nodes'] if n['name']=='Principled BSDF')
        result={}
        for key,socket in [('BaseColor',0),('Normal',6),('Roughness',2),('Metallic',1),('Opacity',4),('EmissionColor',28),('EmissionStrength',29)]:
            src=next(((a,i) for a,i,b,j in t['links'] if b==bsdf['name'] and j==socket),None)
            result[key]=self.tree(t,*src) if src else bsdf['inputs'][socket]['default']
        strength=scalar(result.pop('EmissionStrength'));color=vec(result.pop('EmissionColor'));result['EmissiveColor']=[x*strength for x in color]
        result['BaseColor']=vec(result['BaseColor']);result['Normal']=vec(result['Normal']);result['Normal'][1]*=-1
        return result
