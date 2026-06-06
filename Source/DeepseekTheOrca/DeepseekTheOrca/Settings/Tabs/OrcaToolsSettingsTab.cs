using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaToolsSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;
        private string maxToolCallsBuffer;
        private string toolSearchTopKBuffer;
        private string toolSemanticSearchWaitMsBuffer;
        private string maxToolResultEstimatedTokensBuffer;
        private string planningMtbDaysBuffer;
        private string tavilyMaxResultsBuffer;

        public override string Id
        {
            get { return "tools"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageTools".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 60; }
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

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 820f);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("DTO_EnableWebSearch".Translate(), ref settings.enableWebSearch, "DTO_EnableWebSearchTooltip".Translate());
            if (settings.enableWebSearch)
            {
                settings.webSearchMode = "tavily";
                listing.Gap(4f);
                listing.Label("DTO_WebSearchProvider".Translate() + ": " + "DTO_WebSearchProviderTavily".Translate(), -1f, "DTO_WebSearchProviderTooltip".Translate());
                listing.Label("DTO_TavilyApiKey".Translate(), -1f, "DTO_TavilyApiKeyTooltip".Translate());
                settings.tavilyApiKey = listing.TextEntry(settings.tavilyApiKey ?? "");
                listing.TextFieldNumericLabeled("DTO_TavilyMaxResults".Translate(), ref settings.tavilyMaxResults, ref tavilyMaxResultsBuffer, 1, 10);
                if (listing.ButtonText("DTO_TavilySearchDepth".Translate() + ": " + settings.tavilySearchDepth))
                {
                    settings.tavilySearchDepth = NextTavilySearchDepth(settings.tavilySearchDepth);
                }
            }

            listing.GapLine();
            listing.CheckboxLabeled("DTO_EnableSemanticToolSearch".Translate(), ref settings.enableSemanticToolSearch, "DTO_EnableSemanticToolSearchTooltip".Translate());
            listing.TextFieldNumericLabeled("DTO_ToolSearchTopK".Translate(), ref settings.toolSearchTopK, ref toolSearchTopKBuffer, 1, 12);
            listing.TextFieldNumericLabeled("DTO_ToolSemanticSearchWaitMs".Translate(), ref settings.toolSemanticSearchWaitMs, ref toolSemanticSearchWaitMsBuffer, 0, 3000);
            listing.TextFieldNumericLabeled("DTO_MaxToolResultEstimatedTokens".Translate(), ref settings.maxToolResultEstimatedTokens, ref maxToolResultEstimatedTokensBuffer, 200, 4000);
            listing.GapLine();
            listing.TextFieldNumericLabeled("DTO_MaxToolCalls".Translate(), ref settings.maxToolCalls, ref maxToolCallsBuffer, 1, 32);
            listing.TextFieldNumericLabeled("DTO_PlanningMtbDays".Translate(), ref settings.planningMtbDays, ref planningMtbDaysBuffer, 0.1f, 60f);

            listing.End();
            Widgets.EndScrollView();
        }

        private static string NextTavilySearchDepth(string current)
        {
            switch (current)
            {
                case "basic":
                    return "advanced";
                case "advanced":
                    return "fast";
                case "fast":
                    return "ultra-fast";
                default:
                    return "basic";
            }
        }
    }
}
