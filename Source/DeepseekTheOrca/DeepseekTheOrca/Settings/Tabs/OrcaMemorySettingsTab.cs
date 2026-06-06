using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaMemorySettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;

        public override string Id
        {
            get { return "memory"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageMemory".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 45; }
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

            List<OrcaMemoryRecord> records = OrcaLongTermMemoryService.AllRecords();
            List<OrcaRecentExperienceRecord> recent = OrcaLongTermMemoryService.AllRecentExperiences();
            float viewHeight = Mathf.Max(rect.height, 330f + records.Count * 86f + Mathf.Min(5, recent.Count) * 54f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.Label("DTO_MemoryManagerTitle".Translate());
            listing.Label("Current persona memory: " + OrcaLongTermMemoryService.CurrentPersonaId());
            listing.CheckboxLabeled("DTO_EnableLongTermMemory".Translate(), ref settings.enableLongTermMemory, "DTO_EnableLongTermMemoryTooltip".Translate());
            listing.Label("DTO_MemoryMaxInjectedEntries".Translate() + ": " + settings.memoryMaxInjectedEntries);
            settings.memoryMaxInjectedEntries = (int)listing.Slider(settings.memoryMaxInjectedEntries, 1, 12);
            listing.Label("DTO_MemoryMergeCosineThreshold".Translate() + ": " + settings.memoryMergeCosineThreshold.ToString("0.00"));
            settings.memoryMergeCosineThreshold = listing.Slider(settings.memoryMergeCosineThreshold, 0.75f, 0.98f);
            listing.CheckboxLabeled("DTO_EnableSemanticMemoryQuery".Translate(), ref settings.enableSemanticMemoryQuery, "DTO_EnableSemanticMemoryQueryTooltip".Translate());
            listing.Label("DTO_SemanticMemoryQueryWaitMs".Translate() + ": " + settings.semanticMemoryQueryWaitMs);
            settings.semanticMemoryQueryWaitMs = (int)listing.Slider(settings.semanticMemoryQueryWaitMs, 0, 5000);
            listing.Label("DTO_SemanticMemoryQueryHardTimeoutMs".Translate() + ": " + settings.semanticMemoryQueryHardTimeoutMs);
            settings.semanticMemoryQueryHardTimeoutMs = (int)listing.Slider(settings.semanticMemoryQueryHardTimeoutMs, settings.semanticMemoryQueryWaitMs, 15000);
            listing.Label("DTO_MemoryCompactionTokenThreshold".Translate() + ": " + settings.memoryCompactionTokenThreshold);
            settings.memoryCompactionTokenThreshold = (int)listing.Slider(settings.memoryCompactionTokenThreshold, 1000, 20000);
            listing.Label("DTO_MemoryChunkTokenSize".Translate() + ": " + settings.memoryChunkTokenSize);
            settings.memoryChunkTokenSize = (int)listing.Slider(settings.memoryChunkTokenSize, 150, 1200);
            listing.Label("DTO_MemoryChunkOverlapTokens".Translate() + ": " + settings.memoryChunkOverlapTokens);
            settings.memoryChunkOverlapTokens = (int)listing.Slider(settings.memoryChunkOverlapTokens, 0, settings.memoryChunkTokenSize / 2);
            listing.GapLine();

            Rect buttons = listing.GetRect(32f);
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, 150f, 32f), "DTO_MemoryClearAll".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("DTO_MemoryClearConfirm".Translate(), OrcaLongTermMemoryService.Clear));
            }

            listing.Gap();
            listing.Label("DTO_MemoryRecentBuffer".Translate() + ": " + recent.Count);
            for (int i = 0; i < recent.Count && i < 5; i++)
            {
                DrawRecent(listing.GetRect(48f), recent[i]);
                listing.Gap(4f);
            }
            listing.GapLine();
            listing.Label("DTO_MemoryStoredCount".Translate(records.Count));
            if (records.Count == 0)
            {
                listing.Label("DTO_MemoryEmpty".Translate());
            }

            for (int i = 0; i < records.Count; i++)
            {
                DrawRecord(listing.GetRect(80f), records[i]);
                listing.Gap(6f);
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawRecent(Rect rect, OrcaRecentExperienceRecord record)
        {
            if (record == null)
            {
                return;
            }

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, OrcaSettingsWidgets.RowHoverFill);
            }

            Widgets.Label(new Rect(rect.x, rect.y + 3f, rect.width, 20f), "DTO_MemoryRecent".Translate() + " | " + record.source);
            GUI.color = new Color(0.74f, 0.78f, 0.82f, 1f);
            Widgets.Label(new Rect(rect.x, rect.y + 24f, rect.width, 20f), Clamp(record.text, 180));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, record.text);
        }

        private static void DrawRecord(Rect rect, OrcaMemoryRecord record)
        {
            if (record == null)
            {
                return;
            }

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, OrcaSettingsWidgets.RowHoverFill);
            }

            Rect deleteRect = new Rect(rect.xMax - 76f, rect.y + 8f, 68f, 28f);
            if (Widgets.ButtonText(deleteRect, "DTO_ChatPersonaDelete".Translate()))
            {
                OrcaLongTermMemoryService.Delete(record.id);
            }

            float textWidth = deleteRect.x - rect.x - 8f;
            string retry = record.embeddingRetryCount > 0 ? " r" + record.embeddingRetryCount : "";
            Widgets.Label(new Rect(rect.x, rect.y + 4f, textWidth, 22f), record.memoryKind + " | " + record.consolidationState + " | x" + record.occurrenceCount + " | " + record.embeddingState + retry + " | importance " + record.importance.ToString("0.00"));
            GUI.color = new Color(0.74f, 0.78f, 0.82f, 1f);
            string tags = record.tags == null || record.tags.Count == 0 ? "" : "tags: " + string.Join(", ", record.tags.Take(5).ToArray());
            string saves = record.saveIds == null || record.saveIds.Count == 0 ? "" : " | saves: " + record.saveIds.Count;
            Widgets.Label(new Rect(rect.x, rect.y + 27f, textWidth, 20f), tags + saves);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y + 50f, textWidth, 24f), Clamp(record.DisplayText, 160));
            TooltipHandler.TipRegion(rect, record.DisplayText + "\n\nCluster: " + record.clusterId + "\n\nExemplar:\n" + record.exemplarText);
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
