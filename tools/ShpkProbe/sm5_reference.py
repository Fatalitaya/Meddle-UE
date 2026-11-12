"""Small straight-line SM5 interpreter for the audited attribute passes.

Consumes D3DDisassemble output, not production HLSL. Unknown instructions fail.
Texture sampling is supplied by the fixture; no lighting or rasterization emulator.
"""
import re
import numpy as np

LANES='xyzw'

def execute(text, registers, textures):
    regs={k:np.array(v,dtype=float) for k,v in registers.items()}
    def read(s):
        s=s.strip(); negative=s.startswith('-')
        if negative:s=s[1:]
        absolute=s.startswith('|')
        if absolute:s=s[1:-1]
        s=re.sub(r'\[(r\d+\.[xyzw])\s*\+\s*(\d+)\]',lambda m:'['+str(int(read(m[1])[0])+int(m[2]))+']',s)
        if s.startswith('l('):
            values=[float(np.array(int(x,16),dtype=np.uint32).view(np.float32)) if x.strip().startswith('0x') else float(x) for x in s[2:-1].split(',')]
            value=np.array(values*4 if len(values)==1 else values)
        else:
            match=re.fullmatch(r'([a-z]+\d+(?:\[\d+\])?)(?:\.([xyzw]+))?',s)
            if not match:raise ValueError(s)
            name,swizzle=match.groups(); value=regs.get(name,np.zeros(4))
            if swizzle:
                swizzle=swizzle*4 if len(swizzle)==1 else swizzle
                value=value[[LANES.index(c) for c in swizzle]]
        if absolute:value=np.abs(value)
        return -value if negative else value
    def write(dest,value,sat=False):
        name,_,mask=dest.partition('.'); mask=mask or LANES
        value=np.broadcast_to(value,(4,)); value=np.clip(value,0,1) if sat else value
        target=regs.setdefault(name,np.zeros(4))
        for lane in mask:target[LANES.index(lane)]=value[LANES.index(lane)]
    for line in text.splitlines():
        line=line.strip().strip('\x00')
        if not line or line.startswith('//') or line.startswith('dcl_') or line=='ps_5_0':continue
        if line=='ret':break
        op,operands=line.split(' ',1)
        args=re.split(r',\s*(?![^()]*\))',operands)
        if op.startswith('ld_structured'):
            dest,index,offset,tex=args
            name,_,swizzle=tex.partition('.')
            value=textures[name]['load'](int(read(index)[0]),int(read(offset)[0]))
            write(dest,np.array(value)[[LANES.index(c) for c in (swizzle or LANES)]])
            continue
        if op.startswith('sample'):
            dest,coord,tex,sampler,*_=args
            name,_,swizzle=tex.partition('.'); swizzle=swizzle or LANES
            sampled=textures[name]['sample'](read(coord),sampler)
            write(dest,np.array(sampled)[[LANES.index(c) for c in swizzle]])
            continue
        if op.startswith('resinfo'):
            name,_,swizzle=args[2].partition('.'); values=np.array(textures[name]['size'])
            write(args[0],values[[LANES.index(c) for c in (swizzle or LANES)]])
            continue
        sat=op.endswith('_sat'); op=op.removesuffix('_sat')
        if op=='discard_nz':
            if np.any(read(args[0])!=0):return None
            continue
        if op=='imul':
            if args[0]!='null':raise ValueError('High integer product is not implemented')
            write(args[1],read(args[2])*read(args[3]));continue
        if op=='swapc':
            test,a,b=(read(arg).copy() for arg in args[2:])
            write(args[0],np.where(test!=0,b,a));write(args[1],np.where(test!=0,a,b));continue
        dest=args[0]; values=[read(a) for a in args[1:]]; a=values[0]; b=values[1] if len(values)>1 else None
        with np.errstate(all='ignore'):
            if op=='mov':value=a
            elif op=='add':value=a+b
            elif op=='mul':value=a*b
            elif op=='mad':value=a*b+values[2]
            elif op=='div':value=a/b
            elif op=='min':value=np.minimum(a,b)
            elif op=='max':value=np.maximum(a,b)
            elif op=='sqrt':value=np.sqrt(a)
            elif op=='rsq':value=1/np.sqrt(a)
            elif op=='log':value=np.log2(a)
            elif op=='exp':value=np.exp2(a)
            elif op in ['dp2','dp3','dp4']:value=np.dot(a[:int(op[-1])],b[:int(op[-1])])
            # Integer predicate masks are -1 (all bits set), not normalized 1.
            # This matters for bg height blending's integer subtraction of masks.
            elif op=='and':
                if np.any(~((a==0)|(a==-1)|(b==0)|(b==-1))):
                    raise ValueError('Only predicate-mask AND is supported: '+line)
                value=np.where(a==-1,b,np.where(b==-1,a,0))
            elif op=='eq':value=-(a==b).astype(float)
            elif op=='lt':value=-(a<b).astype(float)
            elif op=='ge':value=-(a>=b).astype(float)
            elif op=='ftoi' or op=='round_z':value=np.trunc(a)
            elif op=='itof':value=a
            elif op=='utof':value=a.astype(np.int64).astype(np.uint32).astype(float)
            elif op=='iadd':value=(a.astype(np.int32)+b.astype(np.int32)).astype(np.int32)
            elif op=='ishl':value=a.astype(int)<<b.astype(int)
            elif op=='movc':value=np.where(a!=0,b,values[2])
            else:raise ValueError('Unsupported SM5 instruction: '+line)
        write(dest,value,sat)
    return {k:v.tolist() for k,v in regs.items() if k.startswith('o')}
