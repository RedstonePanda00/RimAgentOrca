using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class NovelGameComponent : GameComponent
    {
        private static WeakReference lastInstance;
        public NovelBook Book = new NovelBook();
        private readonly Game owner;
        private List<Func<INovelSource>> factories = new List<Func<INovelSource>>();
        private readonly List<INovelSource> sources = new List<INovelSource>();
        private readonly Queue<ScanJob> scans = new Queue<ScanJob>();
        private Task<LlmChatResponse> request;
        private CancellationTokenSource cancellation;
        private NovelChapter pendingChapter;
        private bool drafting, seeding, requestWasSaved;
        private bool checkPawnCacheAfterLoad = true, refreshingPawnCache, pawnRefreshAttempted;
        private List<string> requiredPawnIds = new List<string>();
        private OrcaLlmRequestConfig config;
        public double LastScanMilliseconds { get; private set; }
        public bool IsWorking { get { return request != null; } }
        public bool CanRegenerateLatest { get { return !IsWorking && Book.regeneration == null && Book.LatestCompleted != null; } }
        public bool IsCollecting { get { return !Book.seedComplete || refreshingPawnCache; } }
        public string Phase { get { return request == null ? "" : drafting ? "DTO_NovelWriting" : "DTO_NovelOrganizing"; } }
        public static NovelGameComponent Current { get { return Verse.Current.Game == null ? null : Verse.Current.Game.GetComponent<NovelGameComponent>(); } }
        private NovelSettings Settings { get { return DeepseekTheOrcaMod.Settings.novelSettings; } }

        public NovelGameComponent(Game game)
        {
            var previous = lastInstance == null ? null : lastInstance.Target as NovelGameComponent;
            if (previous != null) previous.CancelQueued();
            owner = game; lastInstance = new WeakReference(this);
        }

        internal static void CheckLifetime()
        {
            var instance = lastInstance == null ? null : lastInstance.Target as NovelGameComponent;
            if (instance != null && instance.owner != Verse.Current.Game) instance.CancelQueued();
        }

        public override void GameComponentTick()
        {
            if (DeepseekTheOrcaMod.Settings == null || owner != Verse.Current.Game) return;
            bool enabled = OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName);
            SetEnabled(enabled);
            if (!enabled) return;
            int now = Find.TickManager.TicksGame;
            if (!Book.initialized) Initialize(now);
            if (!Book.seedComplete && !seeding) BeginScan(now, true, true);
            if (scans.Count == 0 && Book.seedComplete)
            {
                if (now >= Book.nextLogScan) { BeginScan(now, false, false); Book.nextLogScan = now + Settings.logInterval; }
                if (now >= Book.nextStateScan) { BeginScan(now, false, true); Book.nextStateScan = now + Settings.stateInterval; }
            }
            PumpScans();
            Book.Advance(now, Settings.PeriodTicks);
            Book.lastObservedTick = now;
        }

        public override void GameComponentUpdate()
        {
            if (owner != Verse.Current.Game) { CancelQueued(); return; }
            // Process completions while paused and while the plugin is off. No network thread
            // accesses the book, pawns, maps, translations or save machinery.
            PollRequest();
            if (DeepseekTheOrcaMod.Settings == null) return;
            bool enabled = OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName);
            SetEnabled(enabled);
            if (!enabled || Find.TickManager == null) return;
            int now = Find.TickManager.TicksGame;
            if (!Book.initialized) Initialize(now);
            if (!Book.seedComplete && !seeding) BeginScan(now, true, true);
            // Cache repair must progress even when the game is paused immediately after load.
            if (scans.Count > 0) PumpScans();
            if (Book.seedComplete && scans.Count == 0 && checkPawnCacheAfterLoad)
            {
                checkPawnCacheAfterLoad = false;
                if (!EnsurePawnCache(now)) return;
            }
            if (Book.seedComplete && !Book.paused && request == null && scans.Count == 0)
                StartNextStep();
        }

        private void Initialize(int now) { Book.Start(now, Settings.PeriodTicks); BeginScan(now, true, true); }

        public void SetEnabled(bool enabled)
        {
            if (Book.wasEnabled == enabled) return;
            if (!enabled)
            {
                CancelQueued();
                while (scans.Count > 0) scans.Dequeue().iterator.Dispose();
                seeding = false;
                refreshingPawnCache = false; pawnRefreshAttempted = false; checkPawnCacheAfterLoad = true;
            }
            else if (Book.initialized)
            {
                int now = Find.TickManager == null ? Book.lastObservedTick : Find.TickManager.TicksGame;
                var c = new NovelCaptureContext(Book, now, false, "collection");
                var gap = c.Record("gap:" + Book.lastObservedTick + ":" + now, "gap", "", "", "Collection was disabled",
                    "No observation was made between ticks " + Book.lastObservedTick + " and " + now + ". Do not invent the missing history.");
                gap.rangeStartTick = Book.lastObservedTick; gap.rangeEndTick = now;
                Book.Add(gap);
                Book.nextLogScan = now; Book.nextStateScan = now;
            }
            Book.wasEnabled = enabled;
        }

        private void RefreshSources()
        {
            var next = OrcaExtensionManager.NovelSourceFactories();
            if (next.SequenceEqual(factories)) return;
            factories = next; sources.Clear();
            var ids = new HashSet<string>();
            foreach (var factory in factories)
            {
                try
                {
                    var source = factory();
                    if (source == null || string.IsNullOrWhiteSpace(source.Id) || !ids.Add(source.Id))
                        throw new InvalidOperationException("Null source or duplicate source ID");
                    sources.Add(source);
                }
                catch (Exception ex) { Book.sourceErrors["source_registration"] = ex.Message; }
            }
        }
        private void BeginScan(int now, bool initial, bool state)
        {
            RefreshSources();
            if (initial) seeding = true;
            foreach (var source in sources)
            {
                try
                {
                    var context = new NovelCaptureContext(Book, now, initial, source.Id);
                    scans.Enqueue(new ScanJob { id = source.Id + (initial ? ":initial" : state ? ":state" : ":logs"), iterator = source.Scan(context, initial, state).GetEnumerator() });
                }
                catch (Exception ex) { Book.sourceErrors[source.Id] = ex.Message; }
            }
        }
        private void PumpScans()
        {
            var timer = Stopwatch.StartNew();
            int steps = 0;
            while (scans.Count > 0 && steps++ < 32 && timer.ElapsedMilliseconds < 2)
            {
                var scan = scans.Peek();
                try
                {
                    if (scan.iterator.MoveNext()) Book.Add(scan.iterator.Current);
                    else
                    {
                        scan.iterator.Dispose(); scans.Dequeue(); Book.sourceErrors.Remove(scan.id);
                        if (scan.id == "pawns:initial" || scan.id == "pawns:state" || scan.id == "pawns:cache")
                        { Book.sourceErrors.Remove("pawns:initial"); Book.sourceErrors.Remove("pawns:state"); }
                    }
                }
                catch (Exception ex)
                {
                    Book.sourceErrors[scan.id] = ex.ToString();
                    try { scan.iterator.Dispose(); } catch { }
                    scans.Dequeue();
                }
            }
            LastScanMilliseconds = timer.Elapsed.TotalMilliseconds;
            if (seeding && scans.Count == 0) { Book.seedComplete = true; seeding = false; }
            if (refreshingPawnCache && scans.Count == 0)
            {
                refreshingPawnCache = false;
                if (Book.MissingPawnCache(requiredPawnIds).Count > 0) Fail("DTO_NovelPawnCacheMissing".Translate());
                else pawnRefreshAttempted = false;
            }
        }

        private bool EnsurePawnCache(int now)
        {
            if (refreshingPawnCache) return false;
            try
            {
                return PreparePawnCache(now, NovelPawnSource.ObservablePawns(Book).Select(p => p.GetUniqueLoadID()).ToList());
            }
            catch (Exception ex)
            {
                Book.sourceErrors["pawns:cache"] = ex.ToString();
                refreshingPawnCache = false; Fail("DTO_NovelPawnCacheMissing".Translate()); return false;
            }
        }

        private bool PreparePawnCache(int now, List<string> observableIds)
        {
            requiredPawnIds = observableIds;
            var missing = Book.MissingPawnCache(requiredPawnIds);
            if (missing.Count == 0) { Book.sourceErrors.Remove("pawns:cache"); return true; }
            if (pawnRefreshAttempted) { Fail("DTO_NovelPawnCacheMissing".Translate()); return false; }
            pawnRefreshAttempted = true; refreshingPawnCache = true;
            // Rebuild orphaned baselines as actual source records, not just in-memory keys.
            var profiles = new HashSet<string>(Book.records.Where(r => r.category == "profile").Select(r => r.subject));
            foreach (var id in missing)
                if (!profiles.Contains(id)) Book.baselines.Remove("pawns:profile:" + id);
            bool opening = Book.chapters.Any(c => c.number == 0 && !c.complete);
            var context = new NovelCaptureContext(Book, now, opening, "pawns");
            scans.Enqueue(new ScanJob { id = "pawns:cache", iterator = new NovelPawnSource().Scan(context, opening, true).GetEnumerator() });
            return false; // No request until the repair has completed on the main thread.
        }

        public void Resume()
        {
            if (request != null) return;
            config = null; // Re-read credentials after the player fixes a connection; retain the saved model/endpoint.
            pawnRefreshAttempted = false; checkPawnCacheAfterLoad = true;
            Book.paused = false; Book.error = "";
        }
        public bool RegenerateLatest(int expectedNumber)
        {
            if (!CanRegenerateLatest || owner != Verse.Current.Game || !OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName)) return false;
            if (!Book.BeginRegeneration(expectedNumber)) return false;
            config = null; pendingChapter = null;
            pawnRefreshAttempted = false; checkPawnCacheAfterLoad = true;
            return true;
        }
        private void Fail(string error) { Book.paused = true; Book.error = error ?? "Unknown failure"; }
        private void CancelQueued() { if (cancellation != null) cancellation.Cancel(); }

        private void StartNextStep()
        {
            var chapter = Book.NextWork;
            if (chapter == null) return;
            try
            {
                if (!EnsurePawnCache(Find.TickManager.TicksGame)) return;
                // A failed organizing attempt has no successful result to preserve. Include
                // repaired opening dossiers when the player resumes that step.
                if (!chapter.frozen || string.IsNullOrEmpty(chapter.outline)) NovelWriting.Freeze(Book, chapter);
                if (!chapter.started)
                {
                    var settings = DeepseekTheOrcaMod.Settings;
                    var resolved = settings.RequestConfigForRole(OrcaLlmModelRole.Dialogue);
                    if (resolved == null) throw new InvalidOperationException("DTO_NovelNoModel".Translate());
                    var persona = OrcaChatPersonaManager.Get(settings.chatPersonaDefName);
                    chapter.author = persona.label; chapter.persona = persona.prompt;
                    chapter.language = OrcaLanguageUtility.CurrentGameLanguage(); chapter.targetLength = Settings.targetLength;
                    var connection = settings.llmConnections.FirstOrDefault(x => x != null && x.enabled && x.ActiveBaseUrl == resolved.baseUrl && x.apiKey == resolved.apiKey);
                    if (connection == null) throw new InvalidOperationException("DTO_NovelNoModel".Translate());
                    chapter.modelReference = OrcaLlmConnectionResolver.MakeModelReference(connection.id, resolved.model);
                    chapter.modelId = resolved.model; chapter.baseUrl = resolved.baseUrl; chapter.providerId = resolved.providerId;
                    chapter.organization = resolved.openAiOrganization; chapter.project = resolved.openAiProject;
                    config = resolved; chapter.started = true;
                }
                else if (config == null || pendingChapter != chapter)
                {
                    string connectionId, modelId;
                    OrcaLlmConnectionResolver.TryParseModelReference(chapter.modelReference, out connectionId, out modelId);
                    var credentials = DeepseekTheOrcaMod.Settings.llmConnections.FirstOrDefault(x => x != null && x.enabled && x.id == connectionId);
                    // Never use the resolver's fallback here: a different connection's key must
                    // not be sent to the chapter's previously captured endpoint.
                    if (credentials == null || string.IsNullOrEmpty(credentials.apiKey) || credentials.ActiveBaseUrl != chapter.baseUrl)
                        throw new InvalidOperationException("DTO_NovelConnectionChanged".Translate());
                    config = new OrcaLlmRequestConfig { apiKey = credentials.apiKey, proxyUrl = credentials.proxyUrl,
                        model = chapter.modelId, baseUrl = chapter.baseUrl, providerId = chapter.providerId,
                        openAiOrganization = chapter.organization, openAiProject = chapter.project };
                }
                if (!chapter.writingSkillCaptured)
                    NovelWriting.CaptureWritingSkill(chapter, OrcaSkillManager.NovelWritingSkill());
                drafting = !string.IsNullOrEmpty(chapter.outline);
                var messages = NovelWriting.BuildMessages(Book, chapter, drafting);
                // Output allowance scales with requested prose/selection size; never truncate input.
                int outputTokens = (int)Math.Min(int.MaxValue, drafting ? Math.Max(4096L, (long)chapter.targetLength * 3 + 2048) : Math.Max(4096L, (long)chapter.recordIds.Count * 24 + 2048));
                cancellation = new CancellationTokenSource();
                pendingChapter = chapter;
                var capturedConfig = config;
                var capturedCancellation = cancellation.Token;
                request = Task.Run(() => new LlmApiClient().SendNovelCompletionAsync(capturedConfig, messages, outputTokens, capturedCancellation));
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void PollRequest()
        {
            if (request == null || !request.IsCompleted) return;
            try
            {
                if (!request.IsCanceled)
                {
                    if (request.IsFaulted) throw request.Exception.GetBaseException();
                    NovelWriting.Accept(Book, pendingChapter, request.Result, drafting);
                    if (ReferenceEquals(pendingChapter, Book.regeneration) && pendingChapter.complete) Book.CommitRegeneration();
                }
            }
            catch (Exception ex) { Fail(ex.Message); }
            finally
            {
                request = null; requestWasSaved = false;
                if (cancellation != null) cancellation.Dispose(); cancellation = null;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) requestWasSaved = request != null;
            Scribe_Deep.Look(ref Book, "rimAgentNovelBook");
            Scribe_Values.Look(ref requestWasSaved, "novelRequestInFlight");
            if (Book == null) Book = new NovelBook();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                checkPawnCacheAfterLoad = true; pawnRefreshAttempted = false;
                if (requestWasSaved) { Fail("DTO_NovelInterrupted".Translate()); requestWasSaved = false; }
            }
        }
        private sealed class ScanJob { public string id; public IEnumerator<NovelRecord> iterator; }
    }

    [StaticConstructorOnStartup]
    internal static class NovelLifetime
    {
        static NovelLifetime()
        {
            var watcher = new UnityEngine.GameObject("RimAgent.NovelLifetime");
            watcher.AddComponent<NovelLifetimeMonitor>();
            UnityEngine.Object.DontDestroyOnLoad(watcher);
        }
    }
    // GameComponent.Update stops when returning to the main menu. This tiny main-thread
    // watcher revokes queued requests there too; it does not scan or access book data.
    public sealed class NovelLifetimeMonitor : UnityEngine.MonoBehaviour
    {
        private void Update() { NovelGameComponent.CheckLifetime(); }
    }
}
