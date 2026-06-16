using Verse;

namespace DeepseekTheOrca
{
    public sealed class DeepseekTheOrcaGameComponent : GameComponent
    {
        public DeepseekTheOrcaGameComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            LlmRequestScheduler.Tick();
            OrcaHttpMcpClient.Tick();
            OrcaProactiveConversationManager.Tick();
            OrcaSessionMemory.Tick();
            OrcaNarrativeHistoryMemory.Tick();
            OrcaIncidentSchedule.Tick();
            OrcaToolBundleRouter.Tick();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            OrcaNarrativeHistoryMemory.ExposeData();
            OrcaIncidentSchedule.ExposeData();
        }
    }
}
