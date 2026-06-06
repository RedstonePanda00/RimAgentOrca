using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaKnowledgeSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;

        public override string Id
        {
            get { return "knowledge"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageKnowledge".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 46; }
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

            List<OrcaKnowledgeEntry> entries = OrcaKnowledgeManager.AllEntries();
            float viewHeight = Mathf.Max(rect.height, 170f + entries.Count * 70f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.Label("DTO_KnowledgeManagerTitle".Translate());
            listing.Label("DTO_KnowledgeFolder".Translate() + ": " + OrcaKnowledgeManager.KnowledgeFolderPath);
            listing.Label("DTO_KnowledgeMaxInjectedEntries".Translate() + ": " + settings.knowledgeMaxInjectedEntries);
            settings.knowledgeMaxInjectedEntries = (int)listing.Slider(settings.knowledgeMaxInjectedEntries, 1, 12);

            Rect buttons = listing.GetRect(32f);
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, 150f, 32f), "DTO_KnowledgeReload".Translate()))
            {
                OrcaKnowledgeManager.Reload();
            }

            listing.GapLine();
            listing.Label("DTO_KnowledgeStoredCount".Translate(entries.Count));
            if (entries.Count == 0)
            {
                listing.Label("DTO_KnowledgeEmpty".Translate());
            }

            for (int i = 0; i < entries.Count; i++)
            {
                DrawEntry(listing.GetRect(64f), entries[i]);
                listing.Gap(6f);
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawEntry(Rect rect, OrcaKnowledgeEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, OrcaSettingsWidgets.RowHoverFill);
            }

            string title = (entry.label.NullOrEmpty() ? entry.id : entry.label) + " | " + entry.source;
            Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width, 22f), title);
            GUI.color = new Color(0.74f, 0.78f, 0.82f, 1f);
            string categories = entry.categories == null || entry.categories.Count == 0 ? "" : string.Join(", ", entry.categories.Take(5).ToArray());
            Widgets.Label(new Rect(rect.x, rect.y + 27f, rect.width, 20f), categories);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y + 46f, rect.width, 18f), Clamp(entry.text, 160));
            TooltipHandler.TipRegion(rect, entry.text);
        }

        private static string Clamp(string text, int maxChars)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "...";
        }
    }
}
