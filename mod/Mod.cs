using ICities;
using UnityEngine;
using CitiesHarmony.API;

namespace CS1McpBridge
{
    /// <summary>
    /// Mod entry point. Cities: Skylines scans the loaded assembly for IUserMod,
    /// ILoadingExtension and IThreadingExtension implementations automatically —
    /// no manual registration needed.
    /// </summary>
    public class Mod : IUserMod
    {
        public string Name => "CS1 MCP Bridge";
        public string Description => "Exposes Cities: Skylines to external tools over a local socket (MCP backend).";

        public void OnEnabled()
        {
            // CitiesHarmony provides the Harmony runtime. We don't patch anything yet,
            // but keeping the dependency wired means commands that need hooks later
            // (e.g. event callbacks) have it available.
            HarmonyHelper.DoOnHarmonyReady(() => { /* patches go here when needed */ });
        }

        public void OnDisabled() { }
    }

    /// <summary>Starts/stops the socket server as cities are loaded and unloaded.</summary>
    public class Loader : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            // Only run inside an actual playable city, not the main menu / asset editor.
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame &&
                mode != LoadMode.NewGameFromScenario)
                return;

            BridgeServer.Start();
        }

        public override void OnLevelUnloading()
        {
            BridgeServer.Stop();
        }
    }

    /// <summary>
    /// OnUpdate runs on the game's MAIN thread every frame — this is where we pump
    /// queued main-thread work (camera moves, screenshots). Simulation-thread work
    /// is instead routed through SimulationManager.AddAction (see Dispatch).
    /// </summary>
    public class Threading : ThreadingExtensionBase
    {
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            Dispatch.PumpMainThread();
        }
    }

    internal static class Log
    {
        const string Tag = "[CS1McpBridge] ";
        public static void Info(string m) => Debug.Log(Tag + m);
        public static void Error(object m) => Debug.LogError(Tag + m);
    }
}
