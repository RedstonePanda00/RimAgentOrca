using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class MainButtonWorker_OrcaDebug : MainButtonWorker_ToggleTab
    {
        public override bool Visible
        {
            get { return base.Visible && OrcaStorytellerUtility.IsActiveOrcaStoryteller; }
        }
    }

    public static class OrcaStorytellerUtility
    {
        public static bool IsActiveOrcaStoryteller
        {
            get
            {
                return Find.Storyteller != null
                    && Find.Storyteller.def != null
                    && Find.Storyteller.def.defName == "DTO_DeepseekTheOrca";
            }
        }
    }
}
