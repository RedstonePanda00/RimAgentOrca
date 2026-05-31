using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaSettingsWindow : Window
    {
        private readonly DeepseekTheOrcaMod mod;
        private readonly List<OrcaSettingsTab> tabs;
        private string selectedTabId = "agent_models";

        public OrcaSettingsWindow(DeepseekTheOrcaMod mod)
        {
            this.mod = mod;
            tabs = OrcaSettingsTabRegistry.CreateTabs();
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(980f, 720f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (mod == null)
            {
                Close();
                return;
            }

            mod.DrawSettingsUi(inRect, tabs, ref selectedTabId);
        }
    }
}
