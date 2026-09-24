using Verse;

namespace DeepseekTheOrca
{
    public sealed class DeepseekTheOrcaGameComponent : GameComponent
    {
        public OrcaModuleData moduleData = new OrcaModuleData();
        private readonly Game owner;

        public DeepseekTheOrcaGameComponent(Game game)
        {
            owner = game;
            // Global configuration/persona memories survive; transient game work does not.
            OrcaIncidentSchedule.Reset();
            OrcaNarrativeHistoryMemory.Reset();
            OrcaNarrativeDirector.Reset();
            OrcaProactiveConversationManager.Reset();
            StorytellerComp_DeepseekOrca.ResetRuntime();
            OrcaToolBundleRouter.ResetRuntime();
            OrcaDecisionProvider.UpdateConfiguration();
        }

        public override void GameComponentUpdate()
        {
            // Runs while paused as well as while ticking, independent of visible windows.
            OrcaDecisionProvider.UpdateConfiguration();
            OrcaChatAgentHub.Update(owner);
            SingleToolDebugRunner.Update();
            OrcaPersonaBehaviors.Update();
            OrcaExtensionManager.Update();
        }

        public override void StartedNewGame() { NotifyStarted(false); }
        public override void LoadedGame() { NotifyStarted(true); }
        private void NotifyStarted(bool loaded)
        {
            // Construction also occurs during loading, before Current.Game is stable.
            // Activate the live owner only after RimWorld finishes initializing it.
            OrcaGameRuntime.Begin(owner);
            OrcaChatAgentHub.BeginGame(owner);
            OrcaRuntimeDiagnostics.Record("Game ready", "loaded=" + loaded + "; build=" + typeof(DeepseekTheOrcaGameComponent).Module.ModuleVersionId);
            OrcaPersonaBehaviors.GameStarted(owner, loaded);
            OrcaExtensionManager.NotifyGameStarted(owner, loaded);
        }

        public override void GameComponentTick()
        {
            LlmRequestScheduler.Tick();
            OrcaHttpMcpClient.Tick();
            OrcaProactiveConversationManager.Tick();
            OrcaSessionMemory.Tick();
            OrcaNarrativeHistoryMemory.Tick();
            OrcaIncidentSchedule.Tick();
            OrcaPersonaBehaviors.Tick();
            OrcaToolBundleRouter.Tick();
            OrcaExtensionManager.Tick();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            OrcaNarrativeHistoryMemory.ExposeData();
            OrcaIncidentSchedule.ExposeData();
            Scribe_Deep.Look(ref moduleData, "moduleData");
            if (moduleData == null) moduleData = new OrcaModuleData();
        }
    }
}
