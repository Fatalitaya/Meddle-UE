using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export.Ue;
using Microsoft.Extensions.Logging;
using SceneObject = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using SceneObjectType = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType;

namespace Meddle.Plugin.Services;

public class PbdHooks : IDisposable, IService
{
    public const string HumanCreateDeformerSig = "48 89 5C 24 ?? 57 48 83 EC 30 4C 8B C1 ";
    private const int PruneIntervalMs = 1000;
    private readonly DeformerObservationCache deformerCache = new();
    private readonly IFramework framework;
    private readonly ILogger<PbdHooks> logger;
    private readonly SigUtil sigUtil;
    private readonly Hook<HumanCreateDeformerDelegate>? humanCreateDeformerHook;
    private long lastPruneTimestamp;

    public PbdHooks(
        ILogger<PbdHooks> logger,
        HookManager hookManager,
        IFramework framework,
        SigUtil sigUtil)
    {
        this.logger = logger;
        this.framework = framework;
        this.sigUtil = sigUtil;
        humanCreateDeformerHook = hookManager.CreateHook<HumanCreateDeformerDelegate>(HumanCreateDeformerSig, Human_CreateDeformerDetour);
        humanCreateDeformerHook?.Enable();
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        logger.LogDebug("Disposing PbdHooks");
        framework.Update -= OnFrameworkUpdate;
        deformerCache.Clear();
        humanCreateDeformerHook?.Dispose();
    }

    public DeformerObservation? TryGetObservation(nint humanPtr, uint slot)
    {
        var identity = GetCurrentModelIdentity(humanPtr, slot);
        if (identity == null)
        {
            return null;
        }

        return deformerCache.TryGet(
            humanPtr,
            slot,
            identity.ModelPtr,
            identity.ModelResourceHandlePtr,
            identity.ModelPath);
    }

    public IReadOnlyList<DeformerObservationCache.DeformerObservationCacheEntry> GetDeformerCache()
    {
        return deformerCache.GetSnapshots();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = Environment.TickCount64;
        if (now - lastPruneTimestamp < PruneIntervalMs)
        {
            return;
        }

        lastPruneTimestamp = now;
        var alive = CollectAliveCharacterBasePointers();
        if (alive != null)
        {
            deformerCache.PruneToAliveHumans(alive);
        }
    }

    private unsafe HashSet<long>? CollectAliveCharacterBasePointers()
    {
        var gameObjectManager = sigUtil.GetGameObjectManager();
        if (gameObjectManager == null)
        {
            return null;
        }

        var alive = new HashSet<long>();
        for (var idx = 0; idx < gameObjectManager->Objects.GameObjectIdSorted.Length; idx++)
        {
            var objectPtr = gameObjectManager->Objects.GameObjectIdSorted[idx];
            if (objectPtr == null || objectPtr.Value == null)
            {
                continue;
            }

            var drawObject = objectPtr.Value->DrawObject;
            if (drawObject == null)
            {
                continue;
            }

            CollectDrawObject((SceneObject*)drawObject, alive);
        }

        return alive;
    }

    private static unsafe void CollectDrawObject(SceneObject* drawObject, HashSet<long> alive)
    {
        if (drawObject == null)
        {
            return;
        }

        if (drawObject->GetObjectType() == SceneObjectType.CharacterBase)
        {
            alive.Add((nint)drawObject);
        }

        foreach (var child in drawObject->ChildObjects)
        {
            if (child == null)
            {
                continue;
            }

            CollectDrawObject(child, alive);
        }
    }

    private unsafe nint Human_CreateDeformerDetour(nint humanPtr, uint slot)
    {
        var result = humanCreateDeformerHook!.Original(humanPtr, slot);
        var deformer = (DeformerStruct*)result;

        DeformerObservation observation;
        if (deformer == null)
        {
            observation = new DeformerObservation
            {
                State = DeformerObservationState.ObservedNone
            };
        }
        else if (deformer->PbdPointer != null)
        {
            var pbdPath = deformer->PbdPointer->FileName.ParseString();
            if (!string.IsNullOrWhiteSpace(pbdPath))
            {
                observation = new DeformerObservation
                {
                    State = DeformerObservationState.Observed,
                    RaceSexId = deformer->RaceSexId,
                    DeformerId = deformer->DeformerId,
                    PbdPath = pbdPath
                };
            }
            else
            {
                observation = new DeformerObservation
                {
                    State = DeformerObservationState.ObservedIncomplete,
                    RaceSexId = deformer->RaceSexId,
                    DeformerId = deformer->DeformerId
                };
            }
        }
        else
        {
            observation = new DeformerObservation
            {
                State = DeformerObservationState.ObservedIncomplete,
                RaceSexId = deformer->RaceSexId,
                DeformerId = deformer->DeformerId
            };
        }

        var identity = GetCurrentModelIdentity(humanPtr, slot);
        if (identity != null)
        {
            deformerCache.Store(
                humanPtr,
                slot,
                identity.ModelPtr,
                identity.ModelResourceHandlePtr,
                identity.ModelPath,
                observation);
        }

        return result;
    }

    private static unsafe CurrentModelIdentity? GetCurrentModelIdentity(nint humanPtr, uint slot)
    {
        if (humanPtr == 0)
        {
            return null;
        }

        var human = (Human*)humanPtr;
        foreach (var modelPtr in human->ModelsSpan)
        {
            if (modelPtr == null || modelPtr.Value == null)
            {
                continue;
            }

            var model = modelPtr.Value;
            if (model->SlotIndex != slot)
            {
                continue;
            }

            if (model->ModelResourceHandle == null)
            {
                return new CurrentModelIdentity((nint)model, 0, null);
            }

            var modelPath = model->ModelResourceHandle->FileName.ParseString();
            return new CurrentModelIdentity(
                (nint)model,
                (nint)model->ModelResourceHandle,
                modelPath);
        }

        return null;
    }

    public void InvalidateHuman(nint humanPtr)
    {
        deformerCache.ClearHuman(humanPtr);
    }

    public void NotifyHumanDestroyed(nint humanPtr)
    {
        deformerCache.ClearHuman(humanPtr);
    }

    private delegate nint HumanCreateDeformerDelegate(nint humanPtr, uint slot);

    private sealed record CurrentModelIdentity(
        nint ModelPtr,
        nint ModelResourceHandlePtr,
        string? ModelPath);
}
