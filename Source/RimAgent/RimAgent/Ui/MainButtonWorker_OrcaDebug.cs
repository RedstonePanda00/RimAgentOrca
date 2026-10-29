using RimWorld;
using Verse;

namespace RimAgent
{
    public sealed class MainButtonWorker_OrcaDebug : MainButtonWorker_ToggleTab
    {
        public override bool Visible
        {
            get { return base.Visible && OrcaStorytellerUtility.IsActiveOrcaStoryteller; }
        }
    }
}
