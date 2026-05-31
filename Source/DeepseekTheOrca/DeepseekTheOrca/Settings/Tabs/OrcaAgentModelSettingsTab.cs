using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaAgentModelSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;

        public override string Id
        {
            get { return "agent_models"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageAgentModels".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 20; }
        }

        public override void OnSelected(OrcaSettingsContext context)
        {
            scrollPosition = Vector2.zero;
        }

        public override void Draw(Rect rect, OrcaSettingsContext context)
        {
            DeepseekTheOrcaSettings settings = context == null ? null : context.settings;
            if (settings == null)
            {
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 780f);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("DTO_EnableAiPlanning".Translate(), ref settings.enableAiPlanning, "DTO_EnableAiPlanningTooltip".Translate());
            listing.GapLine();
            DrawModelSelector(listing, settings, "DTO_ModelFallback".Translate(), OrcaLlmModelRole.Fallback, "DTO_ModelFallbackTooltip".Translate(), allowFallback: false);
            DrawModelSelector(listing, settings, "DTO_ModelController".Translate(), OrcaLlmModelRole.Controller, "DTO_ModelControllerTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, settings, "DTO_ModelDecision".Translate(), OrcaLlmModelRole.Decision, "DTO_ModelDecisionTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, settings, "DTO_ModelDialogue".Translate(), OrcaLlmModelRole.Dialogue, "DTO_ModelDialogueTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, settings, "DTO_ModelTool".Translate(), OrcaLlmModelRole.Tool, "DTO_ModelToolTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, settings, "DTO_ModelVision".Translate(), OrcaLlmModelRole.Vision, "DTO_ModelVisionTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, settings, "DTO_ModelWebSearch".Translate(), OrcaLlmModelRole.WebSearch, "DTO_ModelWebSearchTooltip".Translate(), allowFallback: true);

            LlmConnectionStatus connectionStatus;
            string connectionMessage;
            LlmConnectionTester.Snapshot(out connectionStatus, out connectionMessage);
            listing.Label("DTO_ConnectionStatus".Translate() + ": " + OrcaSettingsFormatters.ConnectionStatusText(connectionStatus));
            listing.Label(OrcaSettingsFormatters.TranslateIfKey(connectionMessage));
            listing.Gap();
            listing.Label("DTO_OfflineNote".Translate());

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawModelSelector(Listing_Standard listing, DeepseekTheOrcaSettings settings, TaggedString label, OrcaLlmModelRole role, string tooltip, bool allowFallback)
        {
            listing.Label(label, -1f, tooltip);
            string value = settings.ModelReferenceForRole(role);
            string buttonLabel = value.NullOrEmpty() && allowFallback
                ? "DTO_ModelUseFallback".Translate().ToString()
                : settings.ModelReferenceLabel(value);
            if (!listing.ButtonText(buttonLabel))
            {
                return;
            }

            List<OrcaModelOption> modelOptions = settings.AvailableModelOptions();
            Find.WindowStack.Add(new OrcaModelSelectionWindow(settings, role, allowFallback, modelOptions));
        }
    }
}
