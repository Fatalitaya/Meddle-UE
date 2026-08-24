using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

/// <summary>
/// Optional, bounded evidence tracker for PAP resource loads. It owns two
/// independent optional hooks (sync/async GetResource) and a pure-managed
/// observation buffer. It deliberately provides no animation logic and no
/// actor attribution: the observed paths are process-level temporal evidence.
///
/// Startup is lazy (<see cref="EnsureStarted"/>) and idempotent: no hook is
/// installed unless the audit collector or the Animation Evidence tab actually
/// requests it. Missing signatures, hook creation failures and enable failures
/// are recorded per capability and never affect the rest of the plugin.
/// </summary>
public unsafe sealed class PapResourceTracker : IService, IDisposable
{
    // Runtime-evidence signatures, not stable ABI. Both capabilities are
    // independent: if one stops matching, the other keeps working.
    public const string GetResourceSyncSig = "E8 ?? ?? ?? ?? 48 8B C8 8B C3 F0 0F C0 81";
    public const string GetResourceAsyncSig = "E8 ?? ?? ?? 00 48 8B D8 EB ?? F0 FF 83 ?? ?? 00 00";

    private readonly ILogger<PapResourceTracker> logger;
    private readonly HookManager hookManager;
    private readonly RecentPapObservationBuffer buffer = new();
    private readonly object startGate = new();

    private Hook<GetResourceSyncDelegate>? syncHook;
    private Hook<GetResourceAsyncDelegate>? asyncHook;
    private string? syncError;
    private string? asyncError;
    private bool started;
    private bool disposed;

    public PapResourceTracker(ILogger<PapResourceTracker> logger, HookManager hookManager)
    {
        this.logger = logger;
        this.hookManager = hookManager;
    }

    /// <summary>
    /// Lazily installs the optional hooks. Idempotent; safe to call from any
    /// thread. Does nothing after <see cref="Dispose"/>.
    /// </summary>
    public void EnsureStarted()
    {
        lock (startGate)
        {
            if (started || disposed)
            {
                return;
            }

            started = true;
            syncHook = hookManager.CreateOptionalHook<GetResourceSyncDelegate>(
                GetResourceSyncSig,
                GetResourceSyncDetour,
                "PapResourceHook.Sync",
                out var syncCreateError);
            syncError = syncHook == null
                ? syncCreateError ?? "unavailable"
                : TryEnable(syncHook, "PapResourceHook.Sync");

            asyncHook = hookManager.CreateOptionalHook<GetResourceAsyncDelegate>(
                GetResourceAsyncSig,
                GetResourceAsyncDetour,
                "PapResourceHook.Async",
                out var asyncCreateError);
            asyncError = asyncHook == null
                ? asyncCreateError ?? "unavailable"
                : TryEnable(asyncHook, "PapResourceHook.Async");
        }
    }

    public PapResourceTrackerSnapshot GetSnapshot()
    {
        bool snapshotStarted;
        bool syncAvailable;
        string? snapshotSyncError;
        bool asyncAvailable;
        string? snapshotAsyncError;

        lock (startGate)
        {
            snapshotStarted = started;
            syncAvailable = syncHook != null && syncError == null;
            snapshotSyncError = syncError;
            asyncAvailable = asyncHook != null && asyncError == null;
            snapshotAsyncError = asyncError;
        }

        return new PapResourceTrackerSnapshot(
            Started: snapshotStarted,
            SyncAvailable: syncAvailable,
            SyncError: snapshotSyncError,
            AsyncAvailable: asyncAvailable,
            AsyncError: snapshotAsyncError,
            Recent: buffer.Snapshot());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (startGate)
        {
            disposed = true;
            try
            {
                syncHook?.Dispose();
            }
            catch
            {
                // best-effort; HookManager also tracks the hook
            }

            try
            {
                asyncHook?.Dispose();
            }
            catch
            {
                // best-effort; HookManager also tracks the hook
            }

            buffer.Clear();
        }
    }

    private string? TryEnable<TDelegate>(Hook<TDelegate> hook, string capabilityName)
        where TDelegate : Delegate
    {
        try
        {
            hook.Enable();
            logger.LogInformation("Optional capability {Capability} enabled", capabilityName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Optional capability {Capability} failed to enable", capabilityName);
            try
            {
                hook.Dispose();
            }
            catch
            {
                // best-effort
            }

            return $"Enable failed: {ex.Message}";
        }
    }

    private ResourceHandle* GetResourceSyncDetour(
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        byte* path,
        void* unknown,
        void* unkDebugPtr,
        uint unkDebugInt)
    {
        var result = syncHook!.Original(manager, category, type, hash, path, unknown, unkDebugPtr, unkDebugInt);
        TryTrack(result, PapResourceObservationSource.Sync);
        return result;
    }

    private ResourceHandle* GetResourceAsyncDetour(
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        byte* path,
        void* unknown,
        bool isUnknown,
        void* unkDebugPtr,
        uint unkDebugInt)
    {
        var result = asyncHook!.Original(manager, category, type, hash, path, unknown, isUnknown, unkDebugPtr, unkDebugInt);
        TryTrack(result, PapResourceObservationSource.Async);
        return result;
    }

    /// <summary>
    /// Reads the resolved resource handle's own file name (not the requested
    /// path) and records it when it is a .pap. Observation-side failures are
    /// swallowed; the original call result is never modified.
    /// </summary>
    private void TryTrack(ResourceHandle* result, PapResourceObservationSource source)
    {
        try
        {
            if (result == null)
            {
                return;
            }

            var fileName = result->FileName.ParseString();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            if (!fileName.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            buffer.Observe(fileName, DateTime.UtcNow, source);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to track PAP resource observation");
        }
    }

    // The CStringPointer native argument is a single pointer-sized ref struct;
    // byte* is ABI-identical and keeps the hook delegate free of ref-struct
    // restrictions.
    private unsafe delegate ResourceHandle* GetResourceSyncDelegate(
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        byte* path,
        void* unknown,
        void* unkDebugPtr,
        uint unkDebugInt);

    private unsafe delegate ResourceHandle* GetResourceAsyncDelegate(
        ResourceManager* manager,
        ResourceCategory* category,
        uint* type,
        uint* hash,
        byte* path,
        void* unknown,
        bool isUnknown,
        void* unkDebugPtr,
        uint unkDebugInt);

    public sealed record PapResourceTrackerSnapshot(
        bool Started,
        bool SyncAvailable,
        string? SyncError,
        bool AsyncAvailable,
        string? AsyncError,
        IReadOnlyList<PapResourceObservation> Recent);
}
