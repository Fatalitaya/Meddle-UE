using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.Havok.Animation;
using FFXIVClientStructs.Havok.Animation.Animation;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using FFXIVClientStructs.Havok.Common.Base.Reflection.Registry;
using FFXIVClientStructs.Havok.Common.Base.Types;
using FFXIVClientStructs.Havok.Common.Serialize.Resource;
using FFXIVClientStructs.Havok.Common.Serialize.Util;
using Meddle.Formats.Files;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Unreal;

namespace Meddle.Plugin.Havok;

public sealed record PapBakeResult(AssetBone[] Bones, (string Name, BakedAnimation Animation)[] Clips);

/// <summary>Runs on the framework thread. Only managed baked samples leave the Havok lifetime.</summary>
public static unsafe class PapAnimationBaker
{
    public static PapBakeResult Bake(PapFile pap, SklbFile sklb, int fps, bool inPlace, CancellationToken token = default)
    {
        if (fps is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(fps));
        using var rigResource = new Resource(sklb.Skeleton.ToArray());
        using var animResource = new Resource(pap.HavokData.ToArray());
        var rig = rigResource.Container;
        var animations = animResource.Container;
        if (rig->Skeletons.Length != 1 || rig->Skeletons[0].ptr == null)
            throw new InvalidDataException("SKLB must contain exactly one skeleton.");
        var nativeSkeleton = rig->Skeletons[0].ptr;
        var skeleton = new ParsedHkaSkeleton(nativeSkeleton);
        var bones = new List<AssetBone> { new("Meddle_Root", -1, BoneTransform.Identity) };
        for (var i = 0; i < skeleton.BoneNames.Count; i++)
        {
            if (skeleton.BoneParents[i] < -1 || skeleton.BoneParents[i] >= i)
                throw new InvalidDataException("SKLB must order parents before children.");
            bones.Add(new(skeleton.BoneNames[i] ?? $"Bone_{i}", skeleton.BoneParents[i] + 1,
                Convert(skeleton.ReferencePose[i])));
        }
        var clips = new List<(string, BakedAnimation)>();
        foreach (var clip in pap.Animations)
        {
            token.ThrowIfCancellationRequested();
            var index = clip.HavokIndex;
            if (index < 0 || index >= animations->Animations.Length) throw new InvalidDataException("PAP animation index is outside its HKX container.");
            var animation = animations->Animations[index].ptr;
            if (animation == null || !float.IsFinite(animation->Duration) || animation->Duration <= 0)
                throw new InvalidDataException("PAP clip has no finite positive duration.");
            hkaAnimationBinding* binding = null;
            for (var i = 0; i < animations->Bindings.Length; i++)
                if (animations->Bindings[i].ptr != null && animations->Bindings[i].ptr->Animation.ptr == animation)
                    binding = animations->Bindings[i].ptr;
            if (binding == null) throw new InvalidDataException("PAP clip has no animation binding.");
            if (!string.Equals(binding->OriginalSkeletonName.String, nativeSkeleton->Name.String, StringComparison.Ordinal))
                throw new InvalidDataException("PAP binding and selected SKLB skeleton names differ.");
            if (animation->NumberOfTransformTracks < 0 || animation->NumberOfTransformTracks > skeleton.BoneNames.Count || animation->NumberOfFloatTracks < 0)
                throw new InvalidDataException("Invalid Havok track counts.");
            if (binding->TransformTrackToBoneIndices.Length != 0 && binding->TransformTrackToBoneIndices.Length != animation->NumberOfTransformTracks)
                throw new InvalidDataException("Havok binding track map length differs from animation.");
            var mapping = new int[animation->NumberOfTransformTracks];
            for (var i = 0; i < mapping.Length; i++)
            {
                mapping[i] = binding->TransformTrackToBoneIndices.Length == 0 ? i : binding->TransformTrackToBoneIndices[i];
                if (mapping[i] < -1 || mapping[i] >= skeleton.BoneNames.Count) throw new InvalidDataException("Havok binding bone index is invalid.");
            }
            var count = checked((int)Math.Ceiling(animation->Duration * fps));
            var keys = bones.Select(b => Enumerable.Repeat(b.ReferencePose, count + 1).ToArray()).ToArray();
            var transforms = (hkQsTransformf*)NativeMemory.AlignedAlloc((nuint)Math.Max(1, mapping.Length) * (nuint)sizeof(hkQsTransformf), 16);
            var floats = (float*)NativeMemory.AlignedAlloc((nuint)Math.Max(4, animation->NumberOfFloatTracks) * sizeof(float), 16);
            try
            {
                // FFXIV's Havok has four hkReferencedObject virtuals (slot 3 deletes
                // the object). Verified against the game's spline-animation vtable:
                // slot 4 forwards transform/float counts at +0x18/+0x1c to slot 5.
                // This ABI has no cache parameter. QS output is 16-byte aligned.
                var sample = (delegate* unmanaged<hkaAnimation*, float, hkQsTransformf*, float*, void>)(*(void***)animation)[4];
                for (var frame = 0; frame <= count; frame++)
                {
                    token.ThrowIfCancellationRequested();
                    sample(animation, Math.Min((float)frame / fps, animation->Duration), transforms, floats);
                    for (var track = 0; track < mapping.Length; track++)
                    {
                        var bone = mapping[track]; if (bone < 0) continue;
                        var value = new Transform(transforms[track]);
                        var hint = (hkaAnimationBinding.BlendHintEnum)binding->BlendHint.Storage;
                        if (hint != hkaAnimationBinding.BlendHintEnum.Normal)
                        {
                            var reference = skeleton.ReferencePose[bone];
                            var rotation = hint == hkaAnimationBinding.BlendHintEnum.Additive
                                ? reference.Rotation * value.Rotation : value.Rotation * reference.Rotation;
                            value = new Transform(reference.Translation + value.Translation, Quaternion.Normalize(rotation), reference.Scale * value.Scale);
                        }
                        keys[bone + 1][frame] = Convert(value);
                    }
                    if (!inPlace && animation->ExtractedMotion.ptr != null)
                    {
                        var motion = animation->ExtractedMotion.ptr;
                        var getFrame = (delegate* unmanaged<void*, float, hkQsTransformf*, void>)(*(void***)motion)[4];
                        getFrame(motion, Math.Min((float)frame / fps, animation->Duration), transforms);
                        keys[0][frame] = Convert(new Transform(*transforms));
                    }
                }
                var baked = new BakedAnimation(fps, count, keys.Select((k, b) => new BoneTrack(b, k)).ToArray());
                if (animation->ExtractedMotion.ptr == null || inPlace)
                    RootMotion.Extract(bones, baked, inPlace);
                else
                {
                    var initial = keys[0][0];
                    var inverse = Quaternion.Inverse(initial.Rotation);
                    for (var frame = 0; frame <= count; frame++)
                        keys[0][frame] = keys[0][frame] with { Translation = Vector3.Transform(keys[0][frame].Translation - initial.Translation, inverse), Rotation = Quaternion.Normalize(inverse * keys[0][frame].Rotation) };
                }
                clips.Add((clip.GetName, baked));
            }
            finally { NativeMemory.AlignedFree(transforms); NativeMemory.AlignedFree(floats); }
        }
        int Depth(int index) { var depth = 0; while (bones[index].Parent >= 0) { depth++; index = bones[index].Parent; } return depth; }
        var order = Enumerable.Range(0, bones.Count).OrderBy(Depth).ThenBy(i => bones[i].Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var remap = new int[order.Length]; for (var i = 0; i < order.Length; i++) remap[order[i]] = i;
        var canonicalBones = order.Select(i => bones[i] with { Parent = bones[i].Parent < 0 ? -1 : remap[bones[i].Parent] }).ToArray();
        return new(canonicalBones, clips.Select(clip => (clip.Item1, clip.Item2 with
        { Tracks = clip.Item2.Tracks.Select(t => t with { BoneIndex = remap[t.BoneIndex] }).OrderBy(t => t.BoneIndex).ToArray() })).ToArray());
    }

    private static BoneTransform Convert(Transform value) => Coordinates.Transform(
        Matrix4x4.CreateScale(value.Scale) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(value.Rotation)) * Matrix4x4.CreateTranslation(value.Translation));

    private sealed class Resource : IDisposable
    {
        private GCHandle pin;
        private hkResource* resource;
        public hkaAnimationContainer* Container { get; }
        public Resource(byte[] bytes)
        {
            var registry = hkBuiltinTypeRegistry.Instance();
            if (registry == null) throw new InvalidOperationException("Havok is not initialized.");
            var options = new hkSerializeUtil.LoadOptions
            {
                Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int> { Storage = (int)hkSerializeUtil.LoadOptionBits.Default },
                ClassNameRegistry = registry->GetClassNameRegistry(), TypeInfoRegistry = registry->GetTypeInfoRegistry()
            };
            pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                hkSerializeUtil.ErrorDetails error = default;
                resource = hkSerializeUtil.LoadFromBuffer((byte*)pin.AddrOfPinnedObject(), bytes.Length, &error, &options);
                if (resource == null) throw new InvalidDataException("Havok could not load the animation resource.");
                var root = (hkRootLevelContainer*)resource->GetContentsPointer("hkRootLevelContainer", options.TypeInfoRegistry);
                if (root == null) throw new InvalidDataException("Missing Havok root container.");
                Container = (hkaAnimationContainer*)root->findObjectByType("hkaAnimationContainer", null);
                if (Container == null) throw new InvalidDataException("Missing Havok animation container.");
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (resource != null) { resource->RemoveReference(); resource = null; }
            if (pin.IsAllocated) pin.Free();
        }
    }
}
