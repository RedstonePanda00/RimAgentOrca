using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimAgent
{
    public sealed class RimAgentMod : Mod
    {
        public const string DisplayName = "RimAgent";

        public static RimAgentMod Instance;
        public static RimAgentSettings Settings;

        public RimAgentMod(ModContentPack content) : base(content)
        {
            Instance = this;
            RimAgentMigration.Initialize(content);
            Settings = GetSettings<RimAgentSettings>();

            // Composition root: wire lower layers to upper-layer implementations.
            LlmToolSchemas.RegisterSource(new OrcaStoryToolSchemaSource());
            LlmToolSchemas.RegisterSource(new OrcaMcpToolSchemaSource());
            RimAgentSettings.OnDefToolOverridesChanged = AiStoryToolRegistry.Reset;

            LongEventHandler.ExecuteWhenFinished(delegate
            {
                OrcaChatWindowManager.EnsureInitialized();
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

        public static string FormatPluginControllerRoutingHint(OrcaControllerRoutingContext context)
        {
            return OrcaExtensionManager.ControllerRoutingHint(context);
        }

    }
}
