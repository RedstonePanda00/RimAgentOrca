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
            OrcaHttpMcpClient.Tick();
            LlmToolCallDebugRunner.Tick();
            OrcaProactiveConversationManager.Tick();
            OrcaSessionMemory.Tick();
            OrcaNarrativeHistoryMemory.Tick();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            OrcaNarrativeHistoryMemory.ExposeData();
        }
    }
}
