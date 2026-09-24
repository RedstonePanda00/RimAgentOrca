using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DeepseekTheOrca
{
    // Main-thread cooperative scanning. Source-specific readiness is implemented by sources.
    internal sealed class NovelCollectionScheduler
    {
        private readonly Func<NovelBook> getBook;
        private readonly Action<string> fail;
        private NovelBook Book { get { return getBook(); } }
        private List<Func<INovelSource>> factories = new List<Func<INovelSource>>();
        private readonly List<INovelSource> sources = new List<INovelSource>();
        private readonly Queue<ScanJob> jobs = new Queue<ScanJob>();
        private bool seeding, ready, readinessFailed;
        private readonly Func<int> frameClock;
        private int budgetFrame = int.MinValue, frameSteps;
        private double frameMilliseconds;
        public bool Preparing { get; private set; }
        public bool HasJobs { get { return jobs.Count > 0; } }
        public double LastScanMilliseconds { get; private set; }
        public NovelCollectionScheduler(Func<NovelBook> getBook, Action<string> fail)
            : this(getBook, fail, () => UnityEngine.Time.frameCount) { }
        internal NovelCollectionScheduler(Func<NovelBook> getBook, Action<string> fail, Func<int> frameClock)
        { this.getBook = getBook; this.fail = fail; this.frameClock = frameClock; }
        public void InvalidateReadiness() { ready = false; readinessFailed = false; }
        public void EnsureInitial(int now)
        { if (!Book.seedComplete && !seeding) { seeding = true; BeginScan(now, true, true); } }
        public void Schedule(int now, NovelSettings settings)
        {
            EnsureInitial(now);
            if (HasJobs || !Book.seedComplete) return;
            if (now >= Book.nextLogScan) { BeginScan(now, false, false); Book.nextLogScan = now + settings.logInterval; }
            if (now >= Book.nextStateScan) { BeginScan(now, false, true); Book.nextStateScan = now + settings.stateInterval; }
        }
        private void RefreshSources()
        {
            var next = OrcaExtensionManager.NovelSourceFactories();
            if (next.SequenceEqual(factories)) return;
            factories = next; sources.Clear(); InvalidateReadiness();
            Book.sourceErrors.Remove("source_registration");
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
                catch (Exception ex) { Error("source_registration", ex); }
            }
        }
        private void Enqueue(string id, Func<IEnumerable<NovelRecord>> scan)
        {
            try { jobs.Enqueue(new ScanJob { id = id, iterator = scan().GetEnumerator() }); }
            catch (Exception ex) { Error(id, ex); }
        }
        private void BeginScan(int now, bool initial, bool state)
        {
            RefreshSources();
            foreach (var source in sources)
            {
                var context = new NovelCaptureContext(Book, now, initial, source.Id);
                Enqueue(source.Id + (initial ? ":initial" : state ? ":state" : ":logs"), () => source.Scan(context, initial, state));
            }
        }
        public bool EnsureReady(int now)
        {
            RefreshSources();
            if (ready) return true;
            if (Preparing || readinessFailed || HasJobs) return false;
            Preparing = true;
            bool opening = Book.chapters.Any(c => c.number == 0 && !c.complete);
            foreach (var source in sources)
            {
                var preparation = source as INovelSourceReadiness;
                if (preparation == null) continue;
                var context = new NovelCaptureContext(Book, now, opening, source.Id);
                Enqueue(source.Id + ":prepare", () => preparation.PrepareForWriting(context));
            }
            FinishBatch();
            return ready;
        }
        public void Pump()
        {
            int frame = frameClock();
            if (frame != budgetFrame) { budgetFrame = frame; frameSteps = 0; frameMilliseconds = 0; }
            var timer = Stopwatch.StartNew();
            while (HasJobs && frameSteps < 32 && frameMilliseconds + timer.Elapsed.TotalMilliseconds < 2)
            {
                frameSteps++;
                var job = jobs.Peek();
                bool finished = false;
                try
                {
                    if (job.iterator.MoveNext()) Book.Add(job.iterator.Current);
                    else { finished = true; Book.sourceErrors.Remove(job.id); }
                }
                catch (Exception ex) { Error(job.id, ex); finished = true; }
                if (finished) { jobs.Dequeue(); Dispose(job); }
            }
            LastScanMilliseconds = timer.Elapsed.TotalMilliseconds;
            frameMilliseconds += LastScanMilliseconds;
            FinishBatch();
        }
        private void FinishBatch()
        {
            if (HasJobs) return;
            if (seeding) { Book.seedComplete = true; seeding = false; }
            if (Preparing) { Preparing = false; ready = !readinessFailed; }
        }
        private void Error(string id, Exception ex)
        {
            string detail = OrcaRuntimeDiagnostics.ExceptionText(ex), previous;
            if (!Book.sourceErrors.TryGetValue(id, out previous) || previous != detail)
                OrcaRuntimeDiagnostics.Record("Novel scanner " + id, detail);
            Book.sourceErrors[id] = detail;
            if (Preparing) { readinessFailed = true; fail(ex.Message); }
        }
        private void Dispose(ScanJob job)
        { try { job.iterator.Dispose(); } catch (Exception ex) { Error(job.id, ex); } }
        public void Stop()
        {
            while (HasJobs) Dispose(jobs.Dequeue());
            seeding = false; Preparing = false; InvalidateReadiness();
        }
        private sealed class ScanJob { public string id; public IEnumerator<NovelRecord> iterator; }
    }
}
