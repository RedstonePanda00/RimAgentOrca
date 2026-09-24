using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    // Fact sources run on the main thread. Yield regularly; never perform network work here.
    // A null yield is a cooperative checkpoint without a new observation.
    public interface INovelSource
    {
        string Id { get; }
        IEnumerable<NovelRecord> Scan(NovelCaptureContext context, bool initial, bool stateScan);
    }

    public interface INovelSourceReadiness
    {
        IEnumerable<NovelRecord> PrepareForWriting(NovelCaptureContext context);
    }

    public sealed class NovelCaptureContext
    {
        private readonly NovelBook book;
        public readonly int Tick;
        public readonly bool Initial;
        public readonly string Source;
        public NovelCaptureContext(NovelBook book, int tick, bool initial, string source)
        { this.book = book; Tick = tick; Initial = initial; Source = source; }
        public bool Seen(string localKey) { return book.Seen(Source + ":" + localKey); }
        public string Baseline(string key) { string value; return book.baselines.TryGetValue(Source + ":" + key, out value) ? value : null; }
        public void RemoveBaseline(string key) { book.baselines.Remove(Source + ":" + key); }
        public IEnumerable<string> BaselineKeys(string prefix)
        { string full = Source + ":" + prefix; return book.baselines.Keys.Where(k => k.StartsWith(full, StringComparison.Ordinal)).Select(k => k.Substring(full.Length)).ToArray(); }
        public bool HasRecord(string category, string subject)
        { return book.HasSourceRecord(Source, category, subject); }
        public void Diagnostic(string key, Exception error)
        {
            string fullKey = Source + ":" + key;
            string detail = OrcaRuntimeDiagnostics.ExceptionText(error), previous;
            if (!book.sourceErrors.TryGetValue(fullKey, out previous) || previous != detail)
                OrcaRuntimeDiagnostics.Record("Novel source " + fullKey, "tick=" + Tick + "\n" + detail);
            book.sourceErrors[fullKey] = detail;
        }
        public void ClearDiagnostic(string key) { book.sourceErrors.Remove(Source + ":" + key); }
        public NovelRecord Record(string key, string category, string subject, string location, string summary, string detail, int? tick = null)
        {
            int when = tick ?? Tick;
            bool historical = Initial && (!tick.HasValue || when < book.startTick);
            // A late observation belongs to the period in which it was collected, with its
            // original event time retained in details; do not silently lose it into a frozen chapter.
            if (!historical && (when < book.startTick || book.chapters.Any(c => c.frozen && c.number > 0 && when < c.endTick)))
            { detail = "Original event tick: " + when + "\n" + detail; when = Tick; }
            return new NovelRecord { key = Source + ":" + key, source = Source, category = category,
                subject = subject ?? "", location = location ?? "", summary = summary ?? "", detail = detail ?? "",
                tick = when, observedTick = Tick, historical = historical };
        }
        public NovelRecord State(string key, string category, string subject, string location, string label, Dictionary<string, object> fields)
        { return State(key, category, subject, location, label, fields, null); }

        public NovelRecord State(string key, string category, string subject, string location, string label,
            Dictionary<string, object> fields, ICollection<string> detailOnlyFields)
        {
            string baselineKey = Source + ":" + key;
            string json = MiniJson.Serialize(fields), previous;
            if (book.baselines.TryGetValue(baselineKey, out previous) && previous == json) return null;
            Dictionary<string, object> old = string.IsNullOrEmpty(previous) ? null : MiniJson.Deserialize(previous) as Dictionary<string, object>;
            List<string> changes = new List<string>();
            if (old != null)
                foreach (var name in fields.Keys.Union(old.Keys))
                {
                    object prior, value; old.TryGetValue(name, out prior); fields.TryGetValue(name, out value);
                    if (MiniJson.Serialize(prior) != MiniJson.Serialize(value))
                        changes.Add(name + ": " + MiniJson.Serialize(prior) + " -> " + MiniJson.Serialize(value));
                }
            // Each source decides which fields need full-detail expansion; storage is domain-neutral.
            var indexFields = fields.Where(p => detailOnlyFields == null || !detailOnlyFields.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            string summary = label + (old == null ? " (first observation): " + MiniJson.Serialize(indexFields) : ": " + string.Join("; ", changes.ToArray()));
            if (old != null && changes.Count == 0) return null;
            var record = Record(key + ":" + Tick, category, subject, location, summary,
                "Observed state; changes do not prove causation.\n" + json);
            record.stateBefore = previous ?? ""; record.stateAfter = json;
            record.pendingBaselineKey = baselineKey;
            record.pendingStateLabel = label;
            return record;
        }
    }

    public sealed class NovelExtensionWorker : OrcaExtensionWorker
    {
        public const string DefName = "DTO_Extension_Novel";
        public override void Register(OrcaExtensionRegistry registry)
        {
            registry.AddNovelSource(() => new NovelLetterSource());
            registry.AddNovelSource(() => new NovelLogSource());
            registry.AddNovelSource(() => new NovelPawnSource());
            registry.AddNovelSource(() => new NovelWorldSource());
            registry.AddNovelSource(() => new NovelRimtalkSource());
            registry.AddNovelSource(() => new NovelIncidentSource());
            registry.AddDisabled(() => { var c = NovelGameComponent.Current; if (c != null) c.SetEnabled(false); });
        }
    }
}
