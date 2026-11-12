# Read-only audit for client 2026.11.03.0000.0000, SHA256
# 52042b60631cf2c48851b3aaee36e9ec4fedd6d7f2c68f0e9a8c3befb0af9f98
# Requires Python capstone and pefile. Usage: python housing-light-audit.py [PID]
# No PID: static evidence only. PID: also read current housing and light memory.
import pefile,capstone,struct,re,sys
p=pefile.PE(r'F:\FFXIV\FF14\game\ffxiv_dx11.exe',fast_load=True); b=p.get_memory_mapped_image(); base=p.OPTIONAL_HEADER.ImageBase
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
def dis(a,n=180):
 for i in md.disasm(b[a-base:a-base+n],a): print(hex(i.address),i.mnemonic,i.op_str)
def sig(s):
 pat=b''.join(b'.' if t=='??' else re.escape(bytes.fromhex(t)) for t in s.split())
 return [m.start() for m in re.finditer(pat,b,re.S)]
def xrefs(a):
 for m in re.finditer(b'[\xe8\xe9]',b):
  x=m.start()
  if x+5<len(b) and base+x+5+struct.unpack_from('<i',b,x+1)[0]==a: print('xref',hex(base+x))

for a,n in [(0x140e1ecd0,18),(0x141729cb9,29),(0x140e38760,301),
            (0x140e35c60,195),(0x1406f4030,237),(0x14071c2c7,27),
            (0x1406f94f0,924),(0x1406fe970,274),(0x140741440,102)]:
 print('\nFUNCTION/RANGE',hex(a));dis(a,n)
if len(sys.argv)<2: sys.exit(0)
pid=int(sys.argv[1])
for a,n in [(0x1421d0ab0,4),(0x14211be4c,1),(0x1421d0a50,1),(0x14214b500,1)]:print(hex(a),struct.unpack_from('<'+'f'*n,b,a-base))
import ctypes as c
from ctypes import wintypes as w
k=c.WinDLL('kernel32',use_last_error=True)
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=w.HANDLE
k.ReadProcessMemory.argtypes=[w.HANDLE,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)];k.ReadProcessMemory.restype=w.BOOL
h=k.OpenProcess(0x410,False,pid)
def read(a,n):
 buf=c.create_string_buffer(n);got=c.c_size_t()
 if not k.ReadProcessMemory(h,a,buf,n,c.byref(got)):raise OSError(c.get_last_error(),hex(a))
 return buf.raw
def q(a):return struct.unpack('<Q',read(a,8))[0]
# Resolve module base with Toolhelp.
class MOD(c.Structure):
 _fields_=[('dwSize',w.DWORD),('th32ModuleID',w.DWORD),('th32ProcessID',w.DWORD),('GlblcntUsage',w.DWORD),('ProccntUsage',w.DWORD),('modBaseAddr',c.c_void_p),('modBaseSize',w.DWORD),('hModule',w.HMODULE),('szModule',w.WCHAR*256),('szExePath',w.WCHAR*260)]
k.CreateToolhelp32Snapshot.argtypes=[w.DWORD,w.DWORD];k.CreateToolhelp32Snapshot.restype=w.HANDLE
k.Module32FirstW.argtypes=[w.HANDLE,c.POINTER(MOD)];k.Module32FirstW.restype=w.BOOL
snap=k.CreateToolhelp32Snapshot(0x18,pid);m=MOD();m.dwSize=c.sizeof(m);assert k.Module32FirstW(snap,c.byref(m))
rb=m.modBaseAddr;print('base',hex(rb));manager=q(rb+0x2af90f0);print('manager',hex(manager))
current=q(manager);indoor=q(manager+0x10);print('current',hex(current),'indoor',hex(indoor))
if indoor:
 data=read(indoor+0x128d0,24);print('housing fields',data.hex(), 'inverted',data[0], 'ui',5-data[0], 'current,target,speed',struct.unpack_from('<3f',data,4),'transition',data[16],'ssao',data[20])
world=q(rb+0x2ab3560);print('LayoutWorld',hex(world))
if world:
 for off in [0x20,0x50]:
  ptr=q(world+off);print('world child',hex(off),hex(ptr))
  if ptr and off==0x50:print('multiplier',struct.unpack('<f',read(ptr+0x80,4)))
layout=q(world+0x20);area=q(layout+0xb0);print('active layout brightness',struct.unpack('<f',read(area+0x80,4)))
def nodes(head):
 stack=[q(head+8)];seen=set()
 while stack:
  node=stack.pop()
  if node in seen or read(node+0x19,1)[0]:continue
  seen.add(node);yield node
  stack.extend([q(node),q(node+0x10)])
for node in nodes(q(layout+0x268)):
 typ=read(node+0x20,1)[0]
 if typ!=3:continue
 inner=q(node+0x28)
 for j,n in enumerate(nodes(q(inner))):
  inst=q(n+0x28);scene=q(inst+0x30);light=q(scene+0x90) if scene else 0
  vt=q(inst);setter=q(vt+0x1c8)
  print('light',hex(inst),'setterVA',hex(setter-rb+base),'rgba',struct.unpack('<4f',read(light+0x28,16)) if light else None,'range',struct.unpack('<f',read(light+0x8c,4)) if light else None)
  if j>=14:break

k.CloseHandle.argtypes=[w.HANDLE]
k.CloseHandle(snap)
k.CloseHandle(h)
