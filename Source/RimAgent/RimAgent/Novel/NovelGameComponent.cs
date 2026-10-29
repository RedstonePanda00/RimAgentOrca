using System;
using Verse;

namespace RimAgent
{
    public sealed class NovelGameComponent : GameComponent
    {
        private static WeakReference lastInstance;
        public NovelBook Book = new NovelBook();
        private readonly Game owner;
        private readonly NovelCollectionScheduler collection;
        private readonly NovelGenerationRunner generation;
        private bool requestWasSaved, checkReadinessAfterLoad = true;
        private int savedRequestChapter = -1;
        private bool savedRequestRegeneration;
        private bool gameStarted;
        // GameComponentUpdate can run on the new-game setup screens: a Game and
        // TickManager already exist there, but its World has not been created yet.
        private bool CanAccessWorld
        {
            get { return gameStarted && owner != null && ReferenceEquals(owner, Verse.Current.Game)
                && owner.World != null && owner.World.worldObjects != null && owner.World.worldPawns != null
                && owner.tickManager != null; }
        }
        public double LastScanMilliseconds { get { return collection.LastScanMilliseconds; } }
        public bool IsWorking { get { return generation.IsWorking; } }
        public NovelChapter ActiveChapter { get { return generation.ActiveChapter; } }
        public bool CanRegenerateLatest { get { return Book.EvidenceAvailable && !IsWorking && Book.regeneration == null && Book.LatestCompleted != null; } }
        public bool IsCollecting { get { return !Book.seedComplete || collection.Preparing; } }
        public string Phase { get { return generation.Phase; } }
        public static NovelGameComponent Current { get { return Verse.Current.Game == null ? null : Verse.Current.Game.GetComponent<NovelGameComponent>(); } }
        private NovelSettings Settings { get { return OrcaModuleStorage.Settings<NovelSettings>("RimAgent.Novel"); } }

        public NovelGameComponent(Game game)
        {
            var previous = lastInstance == null ? null : lastInstance.Target as NovelGameComponent;
            if (previous != null) previous.CancelQueued();
            owner = game;
            generation = new NovelGenerationRunner(() => Book, Fail);
            collection = new NovelCollectionScheduler(() => Book, Fail);
            lastInstance = new WeakReference(this);
        }
        internal static void CheckLifetime()
        {
            var instance = lastInstance == null ? null : lastInstance.Target as NovelGameComponent;
            if (instance != null && instance.owner != Verse.Current.Game) instance.CancelQueued();
        }
        public override void StartedNewGame() { gameStarted = true; }
        public override void LoadedGame() { gameStarted = true; checkReadinessAfterLoad = true; }
        public override void GameComponentTick()
        {
            if (!CanAccessWorld || RimAgentMod.Settings == null || !Book.EvidenceAvailable) return;
            bool enabled = OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName);
            SetEnabled(enabled);
            if (!enabled) return;
            int now = Find.TickManager.TicksGame;
            Initialize(now);
            collection.Schedule(now, Settings);
            collection.Pump();
            Book.Advance(now, Settings.PeriodTicks);
            Book.lastObservedTick = now;
        }
        public override void GameComponentUpdate()
        {
            if (owner != Verse.Current.Game) { CancelQueued(); return; }
            if (!CanAccessWorld || !Book.EvidenceAvailable) return;
            generation.PollRequest();
            if (RimAgentMod.Settings == null) return;
            bool enabled = OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName);
            SetEnabled(enabled);
            if (!enabled || Find.TickManager == null) return;
            int now = Find.TickManager.TicksGame;
            Initialize(now);
            collection.EnsureInitial(now);
            collection.Pump();
            if (!Book.seedComplete || collection.HasJobs) return;
            if (checkReadinessAfterLoad)
            {
                if (!collection.EnsureReady(now)) return;
                checkReadinessAfterLoad = false;
            }
            if (!Book.paused && !IsWorking && Book.NextWork != null && collection.EnsureReady(now))
            {
                generation.StartNextStep();
                collection.InvalidateReadiness();
            }
        }
        private void Initialize(int now)
        { if (!Book.initialized) Book.Start(now, Settings.PeriodTicks); collection.EnsureInitial(now); }
        public void SetEnabled(bool enabled)
        {
            if (Book.wasEnabled == enabled) return;
            if (!enabled)
            {
                CancelQueued(); collection.Stop(); checkReadinessAfterLoad = true;
            }
            else if (Book.initialized)
            {
                int now = Find.TickManager == null ? Book.lastObservedTick : Find.TickManager.TicksGame;
                var context = new NovelCaptureContext(Book, now, false, "collection");
                var gap = context.Record("gap:" + Book.lastObservedTick + ":" + now, "gap", "", "", "Collection was disabled",
                    "No observation was made between ticks " + Book.lastObservedTick + " and " + now + ". Do not invent the missing history.");
                gap.rangeStartTick = Book.lastObservedTick; gap.rangeEndTick = now;
                Book.Add(gap); Book.nextLogScan = now; Book.nextStateScan = now;
            }
            Book.wasEnabled = enabled;
        }
        public void Resume(int? number = null)
        {
            if (IsWorking || !Book.EvidenceAvailable) return;
            // A global transport/interruption pause belongs to the request that stopped
            // it, even if the reader currently has a different failed section selected.
            var chapter = Book.paused && Book.workflowFailedSection >= 0 ? Book.FailedWork
                : number.HasValue ? Book.WorkForSection(number.Value) : null;
            if (!generation.Resume(chapter)) return;
            collection.InvalidateReadiness(); checkReadinessAfterLoad = true;
            Book.paused = false; Book.error = ""; Book.workflowFailedSection = -1;
        }
        public bool CanSplitPending { get { return generation.CanSplitPending; } }
        public bool CanSplitSection(int number)
        {
            return Book.EvidenceAvailable && (!Book.paused || Book.workflowFailedSection < 0 || Book.workflowFailedSection == number)
                && generation.CanSplitChapter(Book.WorkForSection(number));
        }
        public void SplitPending(int? number = null)
        {
            if (!Book.EvidenceAvailable || (number.HasValue && !CanSplitSection(number.Value))) return;
            if (!generation.SplitPending(number.HasValue ? Book.WorkForSection(number.Value) : null)) return;
            collection.InvalidateReadiness(); checkReadinessAfterLoad = true;
            Book.paused = false; Book.error = ""; Book.workflowFailedSection = -1;
        }
        public bool RegenerateLatest(int expectedNumber)
        {
            if (!CanRegenerateLatest || owner != Verse.Current.Game || !OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName)) return false;
            var captured = new NovelChapter();
            try { NovelTaskConfiguration.Capture(captured, Settings); }
            catch (Exception ex) { Fail(ex.Message); return false; }
            if (!Book.BeginRegeneration(expectedNumber)) return false;
            NovelTaskConfiguration.Copy(captured, Book.regeneration);
            generation.ResetForRegeneration(); collection.InvalidateReadiness(); checkReadinessAfterLoad = true;
            return true;
        }
        public bool RequestGeneration(int number, out string message)
        {
            message = "";
            if (owner != Verse.Current.Game || !OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName)
                || !Book.CanRequestGeneration(number)) return false;
            var captured = new NovelChapter();
            try { NovelTaskConfiguration.Capture(captured, Settings); }
            catch (Exception ex) { message = ex.Message; return false; }
            if (!Book.RequestGeneration(number, captured)) return false;
            collection.InvalidateReadiness();
            message = "DTO_NovelGenerationQueued".Translate(number);
            return true;
        }
        private void Fail(string error) { Book.paused = true; Book.error = error ?? "Unknown failure"; }
        private void CancelQueued() { generation.CancelQueued(); }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                requestWasSaved = IsWorking;
                savedRequestChapter = ActiveChapter == null ? -1 : ActiveChapter.number;
                savedRequestRegeneration = ActiveChapter != null && ReferenceEquals(ActiveChapter, Book.regeneration);
            }
            Scribe_Deep.Look(ref Book, "rimAgentNovelBook");
            Scribe_Values.Look(ref requestWasSaved, "novelRequestInFlight");
            Scribe_Values.Look(ref savedRequestChapter, "novelRequestChapter", -1);
            Scribe_Values.Look(ref savedRequestRegeneration, "novelRequestRegeneration");
            if (Book == null) Book = new NovelBook();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                gameStarted = false;
                checkReadinessAfterLoad = true; collection.InvalidateReadiness();
                if (requestWasSaved)
                {
                    var interrupted = savedRequestRegeneration ? Book.regeneration : Book.Chapter(savedRequestChapter);
                    string message = "DTO_NovelInterrupted".Translate();
                    if (interrupted != null) { interrupted.paused = true; interrupted.error = message; }
                    Book.workflowFailedSection = interrupted == null ? -1 : interrupted.number;
                    Fail(message); requestWasSaved = false;
                }
            }
        }
    }

}
