using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class HookManager : IService, IDisposable
{
    private readonly List<IDalamudHook> hooks = [];
    private readonly ILogger<HookManager> logger;
    private readonly ISigScanner sigScanner;
    private readonly IGameInteropProvider gameInterop;

    public HookManager(ILogger<HookManager> logger, ISigScanner sigScanner, IGameInteropProvider gameInterop)
    {
        this.logger = logger;
        this.sigScanner = sigScanner;
        this.gameInterop = gameInterop;
    }

    internal Hook<TDelegate> CreateHook<TDelegate>(string signature, TDelegate detour) where TDelegate : Delegate
    {
        var delegateName = typeof(TDelegate).Name;
        if (sigScanner.TryScanText(signature, out var ptr))
        {
            logger.LogDebug("Found {delegateName} {signature} at {ptr:X}", delegateName, signature, ptr);
            var hook = gameInterop.HookFromAddress(ptr, detour);
            hooks.Add(hook);
            return hook;
        }
        logger.LogError("Failed to find {delegateName} {signature}", delegateName, signature);
        return null!;
    }

    /// <summary>
    /// Creates an OPTIONAL hook. Unlike <see cref="CreateHook{TDelegate}"/>,
    /// every failure path is caught and reported as a warning (never thrown),
    /// and a miss returns <c>null</c> instead of <c>null!</c>. Capabilities
    /// created through this API are independent: one missing signature never
    /// affects another capability.
    ///
    /// Signatures that begin with an <c>E8</c> relative call byte are resolved
    /// to the actual call target, which is the standard form for the resource
    /// hook signatures used by Phase D. Other patterns are hooked at the scan
    /// address directly.
    /// </summary>
    internal Hook<TDelegate>? CreateOptionalHook<TDelegate>(
        string signature,
        TDelegate detour,
        string capabilityName,
        out string? failureReason)
        where TDelegate : Delegate
    {
        failureReason = null;
        try
        {
            if (!sigScanner.TryScanText(signature, out var scanPtr))
            {
                failureReason = "signature not found";
                logger.LogWarning(
                    "Optional capability {Capability} unavailable: signature not found",
                    capabilityName);
                return null;
            }

            var target = ResolveCallTarget(scanPtr);
            logger.LogDebug(
                "Found optional capability {Capability} at {Target:X}",
                capabilityName,
                target);
            var hook = gameInterop.HookFromAddress(target, detour);
            hooks.Add(hook);
            return hook;
        }
        catch (Exception ex)
        {
            failureReason = $"HookFromAddress failed: {ex.Message}";
            logger.LogWarning(ex, "Optional capability {Capability} unavailable", capabilityName);
            return null;
        }
    }

    private static unsafe nint ResolveCallTarget(nint scanPtr)
    {
        var first = *(byte*)scanPtr;
        if (first != 0xE8)
        {
            return scanPtr;
        }

        var relative = *(int*)(scanPtr + 1);
        return scanPtr + 5 + relative;
    }

    public void Dispose()
    {
        foreach (var hook in hooks)
        {
            try
            {
                hook.Dispose();
            }
            catch
            {
                // A hook that failed to enable may already be disposed by its
                // capability owner; dispose is best-effort.
            }
        }
    }
}
