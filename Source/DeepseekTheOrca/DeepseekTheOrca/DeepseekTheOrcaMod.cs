using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class DeepseekTheOrcaMod : Mod
    {
        public const string DisplayName = "RimAgent";

        public static DeepseekTheOrcaMod Instance;
        public static DeepseekTheOrcaSettings Settings;

        public DeepseekTheOrcaMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<DeepseekTheOrcaSettings>();
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                OrcaChatPersonaManager.ApplyDefaultPersonaSelection(Settings);
                OrcaStorytellerAppearance.ApplyCurrent();
            });
        }

        public override string SettingsCategory()
        {
            return DisplayName;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Find.WindowStack != null && !Find.WindowStack.IsOpen(typeof(OrcaSettingsWindow)))
            {
                Find.WindowStack.Add(new OrcaSettingsWindow(this));
            }

            if (Find.WindowStack != null)
            {
                Find.WindowStack.TryRemove(typeof(Dialog_ModSettings));
            }
        }

        internal void DrawSettingsUi(Rect inRect, List<OrcaSettingsTab> tabs, ref string selectedTabId)
        {
            Settings.EnsureLlmConnections();
            OrcaLlmConnectionManager.Tick(Settings);
            OrcaHttpMcpClient.Tick();
            OrcaSettingsLayout.Draw(inRect, new OrcaSettingsContext(this, Settings), tabs, ref selectedTabId);
        }

        public static string FormatEnabledPluginPrompt()
        {
            StringBuilder builder = new StringBuilder();
            OrcaExtensionManager.AppendSystemPrompt(builder);
            return builder.ToString().TrimEnd();
        }

        public static string FormatPluginControllerRoutingHint()
        {
            return OrcaExtensionManager.ControllerRoutingHint();
        }

    }
}
