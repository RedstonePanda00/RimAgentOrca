using Verse;

namespace DeepseekTheOrca
{
    internal static class OrcaGameRuntime
    {
        private static Game owner;
        internal static void Begin(Game game)
        {
            End();
            owner = game;
        }
        internal static void CheckLifetime()
        {
            if (owner != null && !ReferenceEquals(owner, Current.Game)) End();
        }
        private static void End()
        {
            if (owner == null) return;
            var previous = owner;
            owner = null;
            OrcaPersonaBehaviors.GameEnded(previous);
            OrcaExtensionManager.NotifyGameEnded(previous);
            OrcaDecisionProvider.ClearConnectedProvider();
            OrcaToolBundleRouter.ResetRuntime();
            SingleToolDebugRunner.Reset();
            OrcaLongTermMemoryService.SuspendQueuedWork();
        }
    }

    [StaticConstructorOnStartup]
    internal static class OrcaGameLifetime
    {
        static OrcaGameLifetime()
        {
            var watcher = new UnityEngine.GameObject("RimAgent.GameLifetime");
            watcher.AddComponent<OrcaGameLifetimeMonitor>();
            UnityEngine.Object.DontDestroyOnLoad(watcher);
        }
    }

    // GameComponent.Update stops at the menu. This watcher only invalidates old work;
    // it never scans a map or advances a conversation outside its owning game.
    public sealed class OrcaGameLifetimeMonitor : UnityEngine.MonoBehaviour
    {
        private void Update()
        {
            OrcaRuntimeDiagnostics.Flush();
            OrcaChatAgentHub.CheckLifetime();
            NovelGameComponent.CheckLifetime();
            OrcaGameRuntime.CheckLifetime();
        }
    }
}
