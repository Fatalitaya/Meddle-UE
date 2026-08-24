using System.Runtime.InteropServices;

namespace Meddle.Plugin.Models.Structs;

// Runtime layout observed in upstream Meddle attach traversal.
//
// ExecuteType meanings are partial reverse-engineered observations, not a
// complete enum:
//   0 - root / mount / unmounted-player (observed)
//   3 - ornament / mounted-player (observed)
//   4 - weapon (observed)
//   1, 2, 5 and other values are unknown / not independently verified.
//
// The union at 0x60 is interpreted based on ExecuteType:
//   ExecuteType == 4 -> OwnerSkeleton (Skeleton*)
//   ExecuteType == 3 -> OwnerCharacter (CharacterBase*)
//   otherwise         -> meaning not verified
[StructLayout(LayoutKind.Explicit, Size = 0x78)]
public unsafe struct MeddleAttach
{
    [FieldOffset(0x00)] public void* VTable;
    [FieldOffset(0x38)] public MeddleAttach* LinkedListPrevious;
    [FieldOffset(0x40)] public MeddleAttach* LinkedListNext;

    [FieldOffset(0x50)] public int ExecuteType;
    [FieldOffset(0x54)] private int UnkValue;

    [FieldOffset(0x58)] public void* TargetSkeleton;
    [FieldOffset(0x60)] public void* OwnerUnion;
    [FieldOffset(0x68)] public int AttachmentCount;
    [FieldOffset(0x70)] public void* SkeletonBoneAttachments;
}
