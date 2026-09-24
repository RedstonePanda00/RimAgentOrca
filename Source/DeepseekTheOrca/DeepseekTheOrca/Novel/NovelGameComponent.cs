using System;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class NovelGameComponent : GameComponent
    {
        private static WeakReference lastInstance;
        public NovelBook Book = new NovelBook();
        private readonly Game owner;
        private readonly NovelCollectionScheduler collection;
        private readonly NovelGenerationRunner generation;
        private bool requestWasSaved, checkReadinessAfterLoad = true;
        public double LastScanMilliseconds { get { return collection.LastScanMilliseconds; } }
        public bool IsWorking { get { return generation.IsWorking; } }
        public bool CanRegenerateLatest { get { return !IsWorking && Book.regeneration == null && Book.LatestCompleted != null; } }
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
        public override void GameComponentTick()
        {
            if (DeepseekTheOrcaMod.Settings == null || owner != Verse.Current.Game) return;
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
            generation.PollRequest();
            if (DeepseekTheOrcaMod.Settings == null) return;
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
        public void Resume()
        {
            if (IsWorking) return;
            generation.Resume(); collection.InvalidateReadiness(); checkReadinessAfterLoad = true;
            Book.paused = false; Book.error = "";
        }
        public bool RegenerateLatest(int expectedNumber)
        {
            if (!CanRegenerateLatest || owner != Verse.Current.Game || !OrcaExtensionManager.ExtensionEnabled(NovelExtensionWorker.DefName)) return false;
            if (!Book.BeginRegeneration(expectedNumber)) return false;
            generation.ResetForRegeneration(); collection.InvalidateReadiness(); checkReadinessAfterLoad = true;
            return true;
        }
        private void Fail(string error) { Book.paused = true; Book.error = error ?? "Unknown failure"; }
        private void CancelQueued() { generation.CancelQueued(); }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) requestWasSaved = IsWorking;
            Scribe_Deep.Look(ref Book, "rimAgentNovelBook");
            Scribe_Values.Look(ref requestWasSaved, "novelRequestInFlight");
            if (Book == null) Book = new NovelBook();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                checkReadinessAfterLoad = true; collection.InvalidateReadiness();
                if (requestWasSaved) { Fail("DTO_NovelInterrupted".Translate()); requestWasSaved = false; }
            }
        }
    }

}
