using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class NovelSettingsWorker : OrcaExtensionSettingsWorker
    {
        private string daysBuffer, lengthBuffer, logBuffer, stateBuffer;
        public override Vector2 WindowSize { get { return new Vector2(740f, 640f); } }
        public override void DrawSettings(Rect rect, OrcaSettingsContext context)
        {
            var s = context.settings.moduleData.Get<NovelSettings>("RimAgent.Novel");
            float days = s.periodDays;
            int length = s.targetLength, log = s.logInterval, state = s.stateInterval;
            var list = new Listing_Standard(); list.Begin(rect);
            list.Label("DTO_NovelPeriod".Translate());
            Widgets.TextFieldNumeric(list.GetRect(28f), ref days, ref daysBuffer, 0.01f, 10000f);
            list.Label("DTO_NovelLength".Translate());
            Widgets.TextFieldNumeric(list.GetRect(28f), ref length, ref lengthBuffer, 1, 1000000);
            list.Label("DTO_NovelLogInterval".Translate());
            Widgets.TextFieldNumeric(list.GetRect(28f), ref log, ref logBuffer, 1, 600000);
            list.Label("DTO_NovelStateInterval".Translate());
            Widgets.TextFieldNumeric(list.GetRect(28f), ref state, ref stateBuffer, 1, 600000);
            list.Gap(); list.Label("DTO_NovelSettingsHelp".Translate());
            if (NovelGameComponent.Current != null && list.ButtonText("DTO_NovelOpen".Translate()))
                Find.WindowStack.Add(new NovelReaderWindow(NovelGameComponent.Current));
            list.End();
            if (days != s.periodDays || length != s.targetLength || log != s.logInterval || state != s.stateInterval)
            {
                s.periodDays = days; s.targetLength = length; s.logInterval = log; s.stateInterval = state;
                context.WriteSettings();
            }
        }
    }

    public static class NovelExport
    {
        public static string Format(NovelBook book, bool markdown)
        {
            var text = new StringBuilder();
            text.AppendLine((markdown ? "# " : "") + "RimAgent");
            foreach (var chapter in book.chapters.Where(c => c.complete))
            {
                text.AppendLine(); text.AppendLine((markdown ? "## " : "") + chapter.title);
                text.AppendLine(chapter.author + " | " + chapter.startTick + "–" + chapter.endTick + " ticks");
                text.AppendLine(); text.AppendLine(chapter.text);
            }
            return text.ToString();
        }
        public static string Save(NovelBook book, bool markdown)
        {
            string directory = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimAgent", "Novels");
            Directory.CreateDirectory(directory);
            string name = "novel-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + (markdown ? ".md" : ".txt");
            string path = Path.Combine(directory, name);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(Format(book, markdown));
            return path;
        }
    }

    public sealed class NovelReaderWindow : Window
    {
        private readonly NovelGameComponent component;
        private int selected;
        private Vector2 menuScroll, textScroll;
        private string exportMessage = "", cachedText = "";
        private float cachedWidth, cachedHeight;
        public NovelReaderWindow(NovelGameComponent component)
        {
            this.component = component; doCloseX = true; draggable = true; resizeable = true;
            absorbInputAroundWindow = true; closeOnClickedOutside = false;
        }
        public override Vector2 InitialSize { get { return new Vector2(1060f, 760f); } }
        public override void DoWindowContents(Rect rect)
        {
            if (NovelGameComponent.Current != component) { Close(); return; }
            var book = component.Book;
            bool enabled = OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, rect.width - 40, 32), "DTO_NovelName".Translate());
            Text.Font = GameFont.Small;
            string status = !enabled ? "DTO_NovelOff" : component.IsCollecting ? "DTO_NovelCollecting" : book.paused ? "DTO_NovelPaused" : book.regeneration != null ? "DTO_NovelRegenerating" : component.IsWorking ? component.Phase : "DTO_NovelWaiting";
            Widgets.Label(new Rect(0, 36, rect.width - 180, 26), status.Translate() + " · " + "DTO_NovelCounts".Translate(book.chapters.Count(c => c.complete), book.records.Count));
            if (book.paused && enabled && !component.IsWorking && Widgets.ButtonText(new Rect(rect.width - 160, 34, 150, 28), "DTO_NovelResume".Translate())) component.Resume();
            var menu = new Rect(0, 72, 205, rect.height - 150);
            var menuView = new Rect(0, 0, menu.width - 20, Math.Max(menu.height, book.chapters.Count * 44f));
            Widgets.BeginScrollView(menu, ref menuScroll, menuView);
            for (int i = 0; i < book.chapters.Count; i++)
            {
                var c = book.chapters[i];
                string label = c.number == 0 ? "DTO_NovelPreface".Translate().ToString() : "DTO_NovelChapter".Translate(c.number).ToString();
                if (!c.complete) label += " …";
                if (book.regeneration != null && book.regeneration.number == c.number) label += " ↻";
                if (Widgets.ButtonText(new Rect(0, i * 44, menuView.width, 40), label, selected == i)) { selected = i; textScroll = Vector2.zero; }
            }
            Widgets.EndScrollView();
            var chapter = book.chapters.Count == 0 ? null : book.chapters[Math.Min(selected, book.chapters.Count - 1)];
            string prose = chapter == null ? "DTO_NovelEmpty".Translate().ToString() : chapter.complete
                ? chapter.title + "\n" + chapter.author + "\n" + "DTO_NovelRange".Translate(chapter.startTick / 60000f, chapter.endTick / 60000f).ToString() + "\n\n" + chapter.text
                : "DTO_NovelPending".Translate().ToString();
            var content = new Rect(220, 72, rect.width - 220, rect.height - 150);
            if (cachedText != prose || cachedWidth != content.width)
            { cachedText = prose; cachedWidth = content.width; cachedHeight = Text.CalcHeight(prose, content.width - 24) + 16; }
            var view = new Rect(0, 0, content.width - 20, Math.Max(content.height, cachedHeight));
            Widgets.BeginScrollView(content, ref textScroll, view); Widgets.Label(view, prose); Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(0, rect.height - 64, 120, 28), "DTO_NovelExportTxt".Translate())) Export(false);
            if (Widgets.ButtonText(new Rect(130, rect.height - 64, 160, 28), "DTO_NovelExportMd".Translate())) Export(true);
            int diagnostics = book.sourceErrors.Count + (book.paused ? 1 : 0);
            if (Widgets.ButtonText(new Rect(310, rect.height - 64, 190, 28), "DTO_NovelDiagnostics".Translate(diagnostics)))
                Find.WindowStack.Add(new NovelDiagnosticsWindow(book));
            var regenerateRect = new Rect(520, rect.height - 64, 210, 28);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && enabled && component.CanRegenerateLatest;
            bool regenerateClicked = Widgets.ButtonText(regenerateRect, "DTO_NovelRegenerate".Translate());
            GUI.enabled = oldEnabled;
            var latest = book.LatestCompleted;
            TooltipHandler.TipRegion(regenerateRect, latest == null ? "DTO_NovelRegenerateUnavailable".Translate().ToString()
                : "DTO_NovelRegenerateHelp".Translate(latest.title).ToString());
            if (regenerateClicked && latest != null)
                exportMessage = component.RegenerateLatest(latest.number) ? "DTO_NovelRegenerateStarted".Translate().ToString() : "DTO_NovelRegenerateUnavailable".Translate().ToString();
            Widgets.Label(new Rect(0, rect.height - 32, rect.width, 30), exportMessage);
            TooltipHandler.TipRegion(new Rect(0, rect.height - 32, rect.width, 30), exportMessage);
        }
        private void Export(bool markdown)
        {
            try { exportMessage = NovelExport.Save(component.Book, markdown); }
            catch (Exception ex) { exportMessage = ex.Message; }
        }
    }

    public sealed class NovelDiagnosticsWindow : Window
    {
        private readonly NovelBook book;
        private Vector2 scroll;
        public NovelDiagnosticsWindow(NovelBook book)
        { this.book = book; doCloseX = true; draggable = true; resizeable = true; absorbInputAroundWindow = true; }
        public override Vector2 InitialSize { get { return new Vector2(820, 600); } }
        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, rect.width - 40, 32), "DTO_NovelDiagnosticsTitle".Translate());
            Text.Font = GameFont.Small;
            string text = (book.paused ? "DTO_NovelError".Translate().ToString() + "\n" + book.error + "\n\n" : "")
                + (book.sourceErrors.Count > 0 ? "DTO_NovelSourceWarnings".Translate().ToString() + "\n"
                    + string.Join("\n\n", book.sourceErrors.Select(p => p.Key + ":\n" + p.Value).ToArray()) : "DTO_NovelNoDiagnostics".Translate().ToString());
            var content = new Rect(0, 44, rect.width, rect.height - 44);
            var view = new Rect(0, 0, content.width - 24, Math.Max(content.height, Text.CalcHeight(text, content.width - 24) + 16));
            Widgets.BeginScrollView(content, ref scroll, view); Widgets.Label(view, text); Widgets.EndScrollView();
        }
    }
}
