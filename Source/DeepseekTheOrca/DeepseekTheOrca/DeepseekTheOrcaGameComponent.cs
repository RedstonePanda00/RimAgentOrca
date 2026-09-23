using Verse;

namespace DeepseekTheOrca
{
    public sealed class DeepseekTheOrcaGameComponent : GameComponent
    {
        public GeminiHissState geminiHiss = new GeminiHissState();

        public DeepseekTheOrcaGameComponent(Game game)
        {
            // Global configuration/persona memories survive; transient game work does not.
            OrcaIncidentSchedule.Reset();
            OrcaNarrativeHistoryMemory.Reset();
            OrcaNarrativeDirector.Reset();
            OrcaProactiveConversationManager.Reset();
            StorytellerComp_DeepseekOrca.ResetRuntime();
            OrcaToolBundleRouter.ResetRuntime();
            if (OrcaDecisionProvider.HasConnectedProvider)
                OrcaDecisionProvider.SetConnectedProvider(new LlmIncidentDecisionProvider());
            OrcaChatAgentHub.ClearConversation();
        }

        public override void GameComponentTick()
        {
            LlmRequestScheduler.Tick();
            OrcaHttpMcpClient.Tick();
            OrcaProactiveConversationManager.Tick();
            OrcaSessionMemory.Tick();
            OrcaNarrativeHistoryMemory.Tick();
            OrcaIncidentSchedule.Tick();
            GeminiHissService.Tick();
            OrcaToolBundleRouter.Tick();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            OrcaNarrativeHistoryMemory.ExposeData();
            OrcaIncidentSchedule.ExposeData();
            Scribe_Deep.Look(ref geminiHiss, "geminiHiss");
            if (geminiHiss == null) geminiHiss = new GeminiHissState();
        }
    }
}
