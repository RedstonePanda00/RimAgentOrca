using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimAgent
{
    public sealed class NovelSettings : IExposable
    {
        public float periodDays = 3f;
        public int targetLength = 2000;
        public int logInterval = 250;
        public int stateInterval = 2500;
        public int requestTimeoutSeconds = 180;
        public int PeriodTicks { get { return (int)Math.Min(int.MaxValue / 2, Math.Max(1d, periodDays * 60000d)); } }
        public void ExposeData()
        {
            Scribe_Values.Look(ref periodDays, "periodDays", 3f);
            Scribe_Values.Look(ref targetLength, "targetLength", 2000);
            Scribe_Values.Look(ref logInterval, "logInterval", 250);
            Scribe_Values.Look(ref stateInterval, "stateInterval", 2500);
            Scribe_Values.Look(ref requestTimeoutSeconds, "requestTimeoutSeconds", 180);
            if (float.IsNaN(periodDays) || float.IsInfinity(periodDays) || periodDays <= 0) periodDays = 3;
            targetLength = Math.Max(1, targetLength);
            logInterval = Math.Max(1, logInterval);
            stateInterval = Math.Max(1, stateInterval);
            requestTimeoutSeconds = Math.Max(5, Math.Min(3600, requestTimeoutSeconds));
        }
    }

    public sealed class NovelRecord : IExposable
    {
        public string id = "", key = "", source = "", category = "", subject = "", location = "", summary = "", detail = "";
        public int tick, observedTick;
        public int rangeStartTick = -1, rangeEndTick = -1;
        public string stateBefore = "", stateAfter = "";
        public bool historical;
        public int playerKnowledgeVersion;
        // Optional typed participant IDs for third-party sources and mixed-object records.
        // A reference is evidence of participation, not a substitute for a pawn dossier.
        public List<string> personIds = new List<string>();
        public IEnumerable<string> PersonReferences
        {
            get
            {
                var explicitIds = personIds ?? new List<string>();
                // These established categories store only Pawn identities in subject.
                // Other categories can contain maps, quests or things and must opt in.
                bool pawnSubjects = category == "profile" || category == "pawn_state" || category == "daily_records"
                    || category == "log" || category == "tale";
                return explicitIds.Concat(pawnSubjects ? (subject ?? "").Split(',') : new string[0])
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct();
            }
        }
        // Transient commit metadata. A source can prepare/discard a record without changing
        // the stored baseline; only NovelBook.Add commits the observation and its baseline.
        internal string pendingBaselineKey, pendingStateLabel;
        internal NovelRecord Snapshot()
        {
            var copy = (NovelRecord)MemberwiseClone();
            copy.personIds = personIds == null ? null : new List<string>(personIds);
            return copy;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", ""); Scribe_Values.Look(ref key, "key", "");
            Scribe_Values.Look(ref source, "source", ""); Scribe_Values.Look(ref category, "category", "");
            Scribe_Values.Look(ref subject, "subject", ""); Scribe_Values.Look(ref location, "location", "");
            Scribe_Values.Look(ref summary, "summary", ""); Scribe_Values.Look(ref detail, "detail", "");
            Scribe_Values.Look(ref tick, "tick"); Scribe_Values.Look(ref observedTick, "observedTick");
            Scribe_Values.Look(ref historical, "historical");
            Scribe_Values.Look(ref playerKnowledgeVersion, "playerKnowledgeVersion");
            Scribe_Collections.Look(ref personIds, "personIds", LookMode.Value);
            personIds = personIds ?? new List<string>();
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
        public bool writeRequested;
        public bool paused;
        public string error = "";
        public string draftingContinuity = "";
        public bool draftingContinuityCaptured;
        public bool Organized { get { return !string.IsNullOrEmpty(outline); } }
        public bool ReadyToGenerate { get { return Organized && !complete && !writeRequested; } }
        public bool writingSkillCaptured;
        public string writingSkillId = "", writingSkillText = "";
        // Null means a legacy single snapshot; empty means this stage was explicitly disabled.
        public string writingSkillOrganizeText, writingSkillDraftText;
        public List<string> recordIds = new List<string>(), selectedIds = new List<string>(), personIds = new List<string>();
        public string lastResponse = "";
        public string draftTitle = "", draftText = "", draftContinuity = "", reviewFeedback = "", reviewResponse = "";
        public bool reviewPending, factsReviewed;
        public int playerKnowledgeVersion;
        public NovelCompressionState organizeCompression, writeCompression, reviewCompression;
        public NovelRecoveryState recovery = new NovelRecoveryState();
        public void ExposeData()
        {
            Scribe_Values.Look(ref number, "number"); Scribe_Values.Look(ref startTick, "startTick"); Scribe_Values.Look(ref endTick, "endTick");
            Scribe_Values.Look(ref targetLength, "targetLength"); Scribe_Values.Look(ref title, "title", ""); Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref continuity, "continuity", ""); Scribe_Values.Look(ref outline, "outline", ""); Scribe_Values.Look(ref author, "author", "");
            Scribe_Values.Look(ref persona, "persona", ""); Scribe_Values.Look(ref language, "language", ""); Scribe_Values.Look(ref modelReference, "modelReference", "");
            Scribe_Values.Look(ref modelId, "modelId", ""); Scribe_Values.Look(ref baseUrl, "baseUrl", ""); Scribe_Values.Look(ref providerId, "providerId", "");
            Scribe_Values.Look(ref organization, "organization", ""); Scribe_Values.Look(ref project, "project", "");
            Scribe_Values.Look(ref frozen, "frozen"); Scribe_Values.Look(ref started, "started"); Scribe_Values.Look(ref complete, "complete");
            Scribe_Values.Look(ref writeRequested, "writeRequested");
            Scribe_Values.Look(ref paused, "paused"); Scribe_Values.Look(ref error, "error", "");
            NovelSavedText.Look(ref draftingContinuity, "draftingContinuity");
            Scribe_Values.Look(ref draftingContinuityCaptured, "draftingContinuityCaptured");
            Scribe_Values.Look(ref lastResponse, "lastResponse", ""); Scribe_Values.Look(ref outputTruncated, "outputTruncated");
            Scribe_Deep.Look(ref organizeCompression, "organizeCompression");
            Scribe_Deep.Look(ref writeCompression, "writeCompression");
            Scribe_Deep.Look(ref reviewCompression, "reviewCompression");
            NovelSavedText.Look(ref draftTitle, "draftTitle"); NovelSavedText.Look(ref draftText, "draftText");
            NovelSavedText.Look(ref draftContinuity, "draftContinuity"); NovelSavedText.Look(ref reviewFeedback, "reviewFeedback");
            NovelSavedText.Look(ref reviewResponse, "reviewResponse");
            Scribe_Values.Look(ref reviewPending, "reviewPending"); Scribe_Values.Look(ref factsReviewed, "factsReviewed");
            Scribe_Values.Look(ref playerKnowledgeVersion, "playerKnowledgeVersion");
            Scribe_Deep.Look(ref recovery, "recovery");
            recovery = recovery ?? new NovelRecoveryState();
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
        public int workflowFailedSection = -1;
        public int startTick, nextBoundary, lastObservedTick, nextLogScan, nextStateScan, nextId = 1;
        public List<NovelRecord> records = new List<NovelRecord>();
        private NovelRecordArchive evidenceArchive = new NovelRecordArchive();
        public bool EvidenceAvailable { get { return evidenceArchive == null || string.IsNullOrEmpty(evidenceArchive.Error); } }
        public List<NovelChapter> chapters = new List<NovelChapter>();
        public NovelChapter regeneration;
        public int PausedWorkCount => chapters.Count(c => c.paused) + (regeneration?.paused == true ? 1 : 0);
        public NovelChapter LatestCompleted { get { return chapters.Where(c => c.complete).OrderBy(c => c.number).LastOrDefault(); } }
        public NovelChapter NextWork
        {
            get
            {
                // A prepared section can wait indefinitely for the player without blocking
                // collection or the automatic preparation of later periods.
                return (regeneration != null && !regeneration.paused ? regeneration : null)
                    ?? chapters.FirstOrDefault(c => !c.complete && !c.paused && c.writeRequested)
                    ?? chapters.FirstOrDefault(c => !c.complete && !c.paused && !c.Organized);
            }
        }
        public NovelChapter FailedWork
        {
            get
            {
                var stopped = paused ? WorkForSection(workflowFailedSection) : null;
                return stopped != null && stopped.paused ? stopped
                    : regeneration != null && regeneration.paused ? regeneration : chapters.FirstOrDefault(c => c.paused);
            }
        }
        public NovelChapter Chapter(int number) { return chapters.FirstOrDefault(c => c.number == number); }
        public NovelChapter WorkForSection(int number)
        { return regeneration != null && regeneration.number == number ? regeneration : Chapter(number); }
        public bool CanRequestGeneration(int number)
        { var chapter = Chapter(number); return EvidenceAvailable && regeneration == null && chapter != null && chapter.ReadyToGenerate && !chapter.paused; }
        public bool RequestGeneration(int number, NovelChapter configuration)
        {
            if (!CanRequestGeneration(number)) return false;
            var chapter = Chapter(number);
            NovelTaskConfiguration.Copy(configuration, chapter);
            chapter.writeCompression = null; chapter.lastResponse = ""; chapter.outputTruncated = false;
            chapter.draftingContinuity = ContinuityBefore(chapter); chapter.draftingContinuityCaptured = true;
            chapter.writeRequested = true;
            return true;
        }
        public string ContinuityBefore(NovelChapter chapter)
        {
            var previous = chapters.Where(c => c.complete && c.playerKnowledgeVersion == NovelPlayerKnowledge.Version && c.number < chapter.number).OrderBy(c => c.number).LastOrDefault();
            var skipped = chapters.Where(c => !c.complete && c.number < chapter.number).Select(c => c.number.ToString()).ToArray();
            return (previous == null ? "No earlier prose has been generated." : "Latest earlier generated section: " + previous.number + "\n" + previous.continuity)
                + (skipped.Length == 0 ? "" : "\nSections without generated prose: " + string.Join(", ", skipped)
                    + ". Do not invent their contents or imply that the reader has read them. Use recorded facts to orient the reader if needed.");
        }
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
            if (!EvidenceAvailable) return false;
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
            if (!EvidenceAvailable || regeneration != null || latest == null || latest.number != expectedNumber) return false;
            // The completed version stays readable/exportable until the new draft succeeds.
            regeneration = new NovelChapter { number = latest.number, startTick = latest.startTick, endTick = latest.endTick,
                writeRequested = true, draftingContinuity = ContinuityBefore(latest), draftingContinuityCaptured = true };
            paused = false; error = ""; workflowFailedSection = -1;
            return true;
        }
        public void CommitRegeneration()
        {
            if (regeneration == null || !regeneration.complete) return;
            // Another explicitly requested section may finish while this replacement is
            // paused. The authorization belongs to its original section, not whichever
            // section happens to be the latest when the replacement finally completes.
            var original = Chapter(regeneration.number);
            if (original == null || !original.complete)
                throw new InvalidOperationException("The original section is missing while regeneration is pending.");
            int index = chapters.IndexOf(original);
            chapters[index] = regeneration;
            // Automatic outlines describe their own period. Drafting captures continuity on
            // the player's click; regenerating earlier prose must not erase paid preparation.
            regeneration = null;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", ""); Scribe_Values.Look(ref error, "workflowError", "");
            Scribe_Values.Look(ref initialized, "initialized"); Scribe_Values.Look(ref seedComplete, "seedComplete");
            Scribe_Values.Look(ref wasEnabled, "wasEnabled"); Scribe_Values.Look(ref paused, "workflowPaused");
            Scribe_Values.Look(ref workflowFailedSection, "workflowFailedSection", -1);
            Scribe_Values.Look(ref startTick, "startTick"); Scribe_Values.Look(ref nextBoundary, "nextBoundary");
            Scribe_Values.Look(ref lastObservedTick, "lastObservedTick"); Scribe_Values.Look(ref nextLogScan, "nextLogScan");
            Scribe_Values.Look(ref nextStateScan, "nextStateScan"); Scribe_Values.Look(ref nextId, "nextId", 1);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (evidenceArchive == null) evidenceArchive = new NovelRecordArchive();
                evidenceArchive.Pack(records);
            }
            Scribe_Deep.Look(ref evidenceArchive, "evidenceArchive");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (evidenceArchive == null) Scribe_Collections.Look(ref records, "records", LookMode.Deep);
                else records = evidenceArchive.Unpack();
            }
            Scribe_Collections.Look(ref chapters, "chapters", LookMode.Deep);
            Scribe_Deep.Look(ref regeneration, "regeneration");
            Scribe_Collections.Look(ref baselines, "baselines", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref sourceErrors, "sourceErrors", LookMode.Value, LookMode.Value);
            records = records ?? new List<NovelRecord>(); chapters = chapters ?? new List<NovelChapter>();
            baselines = baselines ?? new Dictionary<string, string>(); sourceErrors = sourceErrors ?? new Dictionary<string, string>();
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            if (!EvidenceAvailable)
            {
                paused = true; error = "Novel evidence archive could not be read completely. Original blocks are retained; collection and requests are stopped.\n" + evidenceArchive.Error;
                sourceErrors["evidence_archive"] = error;
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Reindex();
        }
    }
}
