using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class GeminiSettingsWindow : Window
    {
        public GeminiSettingsWindow()
        {
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }
        public override Vector2 InitialSize { get { return new Vector2(520f, 480f); } }
        public override void DoWindowContents(Rect rect)
        {
            var settings = DeepseekTheOrcaMod.Settings.geminiHissSettings;
            var listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("DTO_GeminiName".Translate() + " — " + "DTO_GeminiSettings".Translate());
            listing.Gap();
            listing.Label("DTO_GeminiSettingsNote".Translate());
            DrawInt(listing, "DTO_GeminiEventCount", ref settings.eventCount, 1, 10);
            DrawInt(listing, "DTO_GeminiBudget", ref settings.budget, settings.eventCount, 60);
            DrawInt(listing, "DTO_GeminiMinGap", ref settings.minGapHours, 1, 12);
            settings.maxGapHours = Mathf.Max(settings.maxGapHours, settings.minGapHours);
            DrawInt(listing, "DTO_GeminiMaxGap", ref settings.maxGapHours, settings.minGapHours, 12);
            DrawInt(listing, "DTO_GeminiCooldown", ref settings.cooldownHours, 1, 168);
            settings.Normalize();
            listing.End();
        }
        private static void DrawInt(Listing_Standard listing, string key, ref int value, int min, int max)
        {
            listing.Label(key.Translate() + ": " + value);
            value = Mathf.RoundToInt(listing.Slider(value, min, max));
        }
        public override void PostClose()
        {
            base.PostClose();
            if (DeepseekTheOrcaMod.Instance != null) DeepseekTheOrcaMod.Instance.WriteSettings();
        }
    }
}
