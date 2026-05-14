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
            LlmToolCallDebugRunner.Tick();
            OrcaProactiveConversationManager.Tick();
            OrcaSessionMemory.Tick();
        }
    }
}
