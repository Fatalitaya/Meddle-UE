using System.Numerics;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Meddle.Plugin.Services;
using Microsoft.Extensions.Logging;
using Camera = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera;
using CameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager;
using World = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.World;

namespace Meddle.Plugin.Utils;

public unsafe class SigUtil : IService, IDisposable
{
    private const string AttachVTableSignature =
        "48 8D 05 ?? ?? ?? ?? 48 89 03 48 89 6B 58 48 89 6B 60 89 6B 68 48 89 6B 70";

    //public static ISigScanner? SigScanner { get; set; } = null!;
    private readonly ISigScanner sigScanner;
    private readonly ILogger<SigUtil> logger;
    private readonly IGameInteropProvider gameInterop;
    private nint attachVTable;
    private bool attachVTableResolved;
    
    public SigUtil(ISigScanner sigScanner, ILogger<SigUtil> logger, IGameInteropProvider gameInterop)
    {
        this.sigScanner = sigScanner;
        this.logger = logger;
        this.gameInterop = gameInterop;
    }
    
    public World* GetWorld()
    {
        var world = World.Instance();
        if (world == null)
            throw new Exception("World instance is null");
        return world;
    }
    
    public GameObjectManager* GetGameObjectManager()
    {
        var gameObjectManager = GameObjectManager.Instance();
        if (gameObjectManager == null)
            return null;
        
        return gameObjectManager;
    }
    
    public BattleChara* GetLocalPlayer()
    {
        var localPlayer = Control.GetLocalPlayer();
        return localPlayer;
    }
    
    public Vector3 GetLocalPosition()
    {
        var localPlayer = GetLocalPlayer();
        if (localPlayer == null) return Vector3.Zero;
        return localPlayer->Position;
    }
    
    public HousingManager* GetHousingManager()
    {
        var manager = HousingManager.Instance();
        if (manager == null)
            throw new Exception("Housing manager is null");
        return manager;
    }
    
    public LayoutWorld* GetLayoutWorld()
    {
        var layoutWorld = LayoutWorld.Instance();
        if (layoutWorld == null)
            throw new Exception("LayoutWorld instance is null");
        return layoutWorld;
    }
    
    public CameraManager* GetCameraManager()
    {
        var manager = CameraManager.Instance();
        if (manager == null)
            throw new Exception("Camera manager is null");
        return manager;
    }
    
    public Camera* GetCamera()
    {
        var manager = CameraManager.Instance();
        if (manager == null)
            throw new Exception("Camera manager is null");
        if (manager->CurrentCamera == null)
            throw new Exception("Current camera is null");
        return manager->CurrentCamera;
    }
    
    public Device* GetDevice()
    {
        var device = Device.Instance();
        if (device == null)
            throw new Exception("Device instance is null");
        return device;
    }
    
    public Control* GetControl()
    {
        var control = Control.Instance();
        if (control == null)
            throw new Exception("Control instance is null");
        return control;
    }
    
    public void Dispose()
    {
        logger.LogDebug("Disposing SigUtil");
    }
    
    public void* TryGetStaticAddressFromSig(string sig, int offset)
    {
        if (sigScanner == null)
            throw new Exception("SigScanner not set");

        if (sigScanner.TryGetStaticAddressFromSig(sig, out var ptr, offset))
        {
            if (ptr != IntPtr.Zero)
                return (void*)ptr;
        }

        throw new Exception($"Failed to find signature {sig} at offset {offset}");
    }

    /// <summary>
    /// Resolves the optional Attach vtable capability. Failure to resolve the
    /// signature must never take the plugin down; it only means linked attach
    /// traversal is unavailable.
    /// </summary>
    public bool TryGetAttachVTable(out nint address)
    {
        if (attachVTableResolved)
        {
            address = attachVTable;
            return address != 0;
        }

        attachVTableResolved = true;
        address = 0;
        try
        {
            if (!sigScanner.TryGetStaticAddressFromSig(AttachVTableSignature, out address, 3))
            {
                logger.LogDebug("Attach vtable signature not found");
                return false;
            }

            attachVTable = address;
            return address != 0;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Attach vtable signature resolution failed");
            attachVTable = 0;
            return false;
        }
    }
}
