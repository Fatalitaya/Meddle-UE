using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x20)]
public struct DeformerStruct
{
    [FieldOffset(0x10)]
    public unsafe ResourceHandle* PbdPointer;

    [FieldOffset(0x18)]
    public ushort RaceSexId;

    [FieldOffset(0x1A)]
    public ushort DeformerId;
}
