using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaDebugSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;

        public override string Id
        {
            get { return "debug"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageDebug".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 80; }
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

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 420f);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("DTO_DebugLogging".Translate(), ref settings.debugLogging, "DTO_DebugLoggingTooltip".Translate());
            listing.Gap();
            listing.Label("DTO_DebugTabTitle".Translate());
            listing.Label("DTO_DebugToolChainTab".Translate());
            listing.Label("DTO_DebugSingleToolTab".Translate());

            listing.End();
            Widgets.EndScrollView();
        }
    }
}
