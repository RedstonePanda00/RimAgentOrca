using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class NovelSettings : IExposable
    {
        public float periodDays = 3f;
        public int targetLength = 2000;
        public int logInterval = 250;
        public int stateInterval = 2500;
        public int PeriodTicks { get { return (int)Math.Min(int.MaxValue / 2, Math.Max(1d, periodDays * 60000d)); } }
        public void ExposeData()
        {
            Scribe_Values.Look(ref periodDays, "periodDays", 3f);
            Scribe_Values.Look(ref targetLength, "targetLength", 2000);
            Scribe_Values.Look(ref logInterval, "logInterval", 250);
            Scribe_Values.Look(ref stateInterval, "stateInterval", 2500);
            if (float.IsNaN(periodDays) || float.IsInfinity(periodDays) || periodDays <= 0) periodDays = 3;
            targetLength = Math.Max(1, targetLength);
            logInterval = Math.Max(1, logInterval);
            stateInterval = Math.Max(1, stateInterval);
        }
    }

    public sealed class NovelRecord : IExposable
    {
        public string id = "", key = "", source = "", category = "", subject = "", location = "", summary = "", detail = "";
        public int tick, observedTick;
        public int rangeStartTick = -1, rangeEndTick = -1;
        public string stateBefore = "", stateAfter = "";
        public bool historical;
        // Transient commit metadata. A source can prepare/discard a record without changing
        // the stored baseline; only NovelBook.Add commits the observation and its baseline.
        internal string pendingBaselineKey, pendingStateLabel;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", ""); Scribe_Values.Look(ref key, "key", "");
            Scribe_Values.Look(ref source, "source", ""); Scribe_Values.Look(ref category, "category", "");
            Scribe_Values.Look(ref subject, "subject", ""); Scribe_Values.Look(ref location, "location", "");
            Scribe_Values.Look(ref summary, "summary", ""); Scribe_Values.Look(ref detail, "detail", "");
            Scribe_Values.Look(ref tick, "tick"); Scribe_Values.Look(ref observedTick, "observedTick");
            Scribe_Values.Look(ref historical, "historical");
            Scribe_Values.Look(ref rangeStartTick, "rangeStartTick", -1); Scribe_Values.Look(ref rangeEndTick, "rangeEndTick", -1);
            Scribe_Values.Look(ref stateBefore, "stateBefore", ""); Scribe_Values.Look(ref stateAfter, "stateAfter", "");
        }
    }

    public sealed class NovelChapter : IExposable
    {
        public int number, startTick, endTick, targetLength;
        public string title = "", text = "", continuity = "", outline = "", author = "", persona = "", language = "", modelReference = "";
        public string modelId = "", baseUrl = "", providerId = "", organization = "", project = "";
        public bool frozen, started, complete, outputTruncated;
        public bool writingSkillCaptured;
        public string writingSkillId = "", writingSkillText = "";
        // Null means a legacy single snapshot; empty means this stage was explicitly disabled.
        public string writingSkillOrganizeText, writingSkillDraftText;
        public List<string> recordIds = new List<string>(), selectedIds = new List<string>(), personIds = new List<string>();
        public string lastResponse = "";
        public void ExposeData()
        {
            Scribe_Values.Look(ref number, "number"); Scribe_Values.Look(ref startTick, "startTick"); Scribe_Values.Look(ref endTick, "endTick");
            Scribe_Values.Look(ref targetLength, "targetLength"); Scribe_Values.Look(ref title, "title", ""); Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref continuity, "continuity", ""); Scribe_Values.Look(ref outline, "outline", ""); Scribe_Values.Look(ref author, "author", "");
            Scribe_Values.Look(ref persona, "persona", ""); Scribe_Values.Look(ref language, "language", ""); Scribe_Values.Look(ref modelReference, "modelReference", "");
            Scribe_Values.Look(ref modelId, "modelId", ""); Scribe_Values.Look(ref baseUrl, "baseUrl", ""); Scribe_Values.Look(ref providerId, "providerId", "");
            Scribe_Values.Look(ref organization, "organization", ""); Scribe_Values.Look(ref project, "project", "");
            Scribe_Values.Look(ref frozen, "frozen"); Scribe_Values.Look(ref started, "started"); Scribe_Values.Look(ref complete, "complete");
            Scribe_Values.Look(ref lastResponse, "lastResponse", ""); Scribe_Values.Look(ref outputTruncated, "outputTruncated");
            Scribe_Values.Look(ref writingSkillCaptured, "writingSkillCaptured");
            Scribe_Values.Look(ref writingSkillId, "writingSkillId", ""); Scribe_Values.Look(ref writingSkillText, "writingSkillText", "");
            Scribe_Values.Look(ref writingSkillOrganizeText, "writingSkillOrganizeText", null);
            Scribe_Values.Look(ref writingSkillDraftText, "writingSkillDraftText", null);
            Scribe_Collections.Look(ref recordIds, "recordIds", LookMode.Value);
            Scribe_Collections.Look(ref selectedIds, "selectedIds", LookMode.Value);
            Scribe_Collections.Look(ref personIds, "personIds", LookMode.Value);
            recordIds = recordIds ?? new List<string>(); selectedIds = selectedIds ?? new List<string>(); personIds = personIds ?? new List<string>();
        }
    }

    public sealed class NovelBook : IExposable
    {
        public string id = Guid.NewGuid().ToString("N"), error = "";
        public bool initialized, seedComplete, wasEnabled, paused;
        public int startTick, nextBoundary, lastObservedTick, nextLogScan, nextStateScan, nextId = 1;
        public List<NovelRecord> records = new List<NovelRecord>();
        public List<NovelChapter> chapters = new List<NovelChapter>();
        public NovelChapter regeneration;
        public NovelChapter LatestCompleted { get { return chapters.Where(c => c.complete).OrderBy(c => c.number).LastOrDefault(); } }
        public NovelChapter NextWork { get { return regeneration ?? chapters.FirstOrDefault(c => !c.complete); } }
        public Dictionary<string, string> baselines = new Dictionary<string, string>();
        public Dictionary<string, string> sourceErrors = new Dictionary<string, string>();
        private HashSet<string> seen;
        private Dictionary<string, NovelRecord> byId;
        public void Reindex() { sourceSubjects = null; seen = new HashSet<string>(records.Select(r => r.key)); byId = records.ToDictionary(r => r.id); }
        public bool Seen(string key) { if (seen == null) Reindex(); return seen.Contains(key); }
        public NovelRecord Find(string key) { if (byId == null) Reindex(); NovelRecord r; return byId.TryGetValue(key, out r) ? r : null; }
        private HashSet<string> sourceSubjects;
        public bool HasSourceRecord(string source, string category, string subject)
        {
            if (sourceSubjects == null) sourceSubjects = new HashSet<string>(records.Select(r => SubjectKey(r.source, r.category, r.subject)));
            return sourceSubjects.Contains(SubjectKey(source, category, subject));
        }
        private static string SubjectKey(string source, string category, string subject)
        { return source + "\n" + category + "\n" + subject; }
        public bool Add(NovelRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.key)) return false;
            bool keySeen = Seen(record.key);
            string baselineKey = record.pendingBaselineKey;
            if (baselineKey == null && keySeen) return false;
            if (baselineKey != null)
            {
                string previous;
                baselines.TryGetValue(baselineKey, out previous);
                if (previous == record.stateAfter) return false;
                if ((previous ?? "") != record.stateBefore)
                {
                    // A source may prepare multiple observations before yielding them.
                    // Preserve each different observation, rebasing its comparison on the
                    // last committed state instead of overwriting that evidence.
                    record.stateBefore = previous ?? "";
                    record.summary = record.pendingStateLabel + ": observed state changed from "
                        + (previous ?? "unknown") + " to " + record.stateAfter;
                }
                // Distinct changes in one tick need distinct keys; the book sequence also
                // survives save/load and avoids retaining another per-object counter.
                if (keySeen) record.key += ":version:" + nextId;
            }
            record.id = "r" + nextId++;
            records.Add(record); seen.Add(record.key); byId.Add(record.id, record);
            if (sourceSubjects != null) sourceSubjects.Add(SubjectKey(record.source, record.category, record.subject));
            if (baselineKey != null)
            {
                baselines[baselineKey] = record.stateAfter;
                record.pendingBaselineKey = null;
                record.pendingStateLabel = null;
            }
            return true;
        }
        public void Start(int now, int period)
        {
            initialized = true; startTick = now; lastObservedTick = now; nextBoundary = now + period;
            chapters.Add(new NovelChapter { number = 0, startTick = 0, endTick = now });
        }
        public void Advance(int now, int period)
        {
            // At most one interval per frame; normal operation reaches each boundary exactly.
            if (now < nextBoundary) return;
            int previous = chapters.Count <= 1 ? startTick : chapters[chapters.Count - 1].endTick;
            chapters.Add(new NovelChapter { number = chapters.Count, startTick = previous, endTick = nextBoundary });
            nextBoundary = (int)Math.Min(int.MaxValue, (long)nextBoundary + period);
        }
        public IEnumerable<NovelRecord> ForChapter(NovelChapter chapter)
        {
            return records.Where(r => chapter.number == 0 ? r.historical : !r.historical &&
                ((r.tick >= chapter.startTick && r.tick < chapter.endTick) ||
                 (r.category == "gap" && r.rangeStartTick < chapter.endTick && r.rangeEndTick > chapter.startTick)));
        }
        public bool BeginRegeneration(int expectedNumber)
        {
            var latest = LatestCompleted;
            if (regeneration != null || latest == null || latest.number != expectedNumber) return false;
            // The completed version stays readable/exportable until the new draft succeeds.
            regeneration = new NovelChapter { number = latest.number, startTick = latest.startTick, endTick = latest.endTick };
            paused = false; error = "";
            return true;
        }
        public void CommitRegeneration()
        {
            if (regeneration == null || !regeneration.complete) return;
            var latest = LatestCompleted;
            if (latest == null || latest.number != regeneration.number)
                throw new InvalidOperationException("The latest completed section changed while regeneration was pending.");
            int index = chapters.IndexOf(latest);
            chapters[index] = regeneration;
            // Later queued outlines depend on the old continuity. Preserve their periods,
            // but organize them again against the replacement with fresh chapter settings.
            for (int i = index + 1; i < chapters.Count; i++)
            {
                var old = chapters[i];
                if (!old.complete) chapters[i] = new NovelChapter { number = old.number, startTick = old.startTick, endTick = old.endTick };
            }
            regeneration = null;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", ""); Scribe_Values.Look(ref error, "error", "");
            Scribe_Values.Look(ref initialized, "initialized"); Scribe_Values.Look(ref seedComplete, "seedComplete");
            Scribe_Values.Look(ref wasEnabled, "wasEnabled"); Scribe_Values.Look(ref paused, "paused");
            Scribe_Values.Look(ref startTick, "startTick"); Scribe_Values.Look(ref nextBoundary, "nextBoundary");
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick"); Scribe_Values.Look(ref nextLogScan, "nextLogScan");
            Scribe_Values.Look(ref nextStateScan, "nextStateScan"); Scribe_Values.Look(ref nextId, "nextId", 1);
            Scribe_Collections.Look(ref records, "records", LookMode.Deep); Scribe_Collections.Look(ref chapters, "chapters", LookMode.Deep);
            Scribe_Deep.Look(ref regeneration, "regeneration");
            Scribe_Collections.Look(ref baselines, "baselines", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref sourceErrors, "sourceErrors", LookMode.Value, LookMode.Value);
            records = records ?? new List<NovelRecord>(); chapters = chapters ?? new List<NovelChapter>();
            baselines = baselines ?? new Dictionary<string, string>(); sourceErrors = sourceErrors ?? new Dictionary<string, string>();
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Reindex();
        }
    }
}
