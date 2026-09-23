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

    public sealed class NovelCaptureContext
    {
        public readonly NovelBook Book;
        public readonly int Tick;
        public readonly bool Initial;
        public readonly string Source;
        public NovelCaptureContext(NovelBook book, int tick, bool initial, string source)
        { Book = book; Tick = tick; Initial = initial; Source = source; }
        public NovelRecord Record(string key, string category, string subject, string location, string summary, string detail, int? tick = null)
        {
            int when = tick ?? Tick;
            bool historical = Initial && (!tick.HasValue || when < Book.startTick);
            // A late observation belongs to the period in which it was collected, with its
            // original event time retained in details; do not silently lose it into a frozen chapter.
            if (!historical && (when < Book.startTick || Book.chapters.Any(c => c.frozen && c.number > 0 && when < c.endTick)))
            { detail = "Original event tick: " + when + "\n" + detail; when = Tick; }
            return new NovelRecord { key = Source + ":" + key, source = Source, category = category,
                subject = subject ?? "", location = location ?? "", summary = summary ?? "", detail = detail ?? "",
                tick = when, observedTick = Tick, historical = historical };
        }
        public NovelRecord State(string key, string category, string subject, string location, string label, Dictionary<string, object> fields)
        {
            string baselineKey = Source + ":" + key;
            string json = MiniJson.Serialize(fields), previous;
            if (Book.baselines.TryGetValue(baselineKey, out previous) && previous == json) return null;
            Book.baselines[baselineKey] = json;
            Dictionary<string, object> old = string.IsNullOrEmpty(previous) ? null : MiniJson.Deserialize(previous) as Dictionary<string, object>;
            List<string> changes = new List<string>();
            if (old != null)
                foreach (var name in fields.Keys.Union(old.Keys))
                {
                    object prior, value; old.TryGetValue(name, out prior); fields.TryGetValue(name, out value);
                    if (MiniJson.Serialize(prior) != MiniJson.Serialize(value))
                        changes.Add(name + ": " + MiniJson.Serialize(prior) + " -> " + MiniJson.Serialize(value));
                }
            // Opening dossiers remain discoverable without sending full biographies twice.
            var indexFields = fields.Where(p => p.Key != "childhood" && p.Key != "adulthood" && p.Key != "skills"
                && p.Key != "rimtalkPersona" && p.Key != "equipment" && p.Key != "apparel").ToDictionary(p => p.Key, p => p.Value);
            string summary = label + (old == null ? " (first observation): " + MiniJson.Serialize(indexFields) : ": " + string.Join("; ", changes.ToArray()));
            if (old != null && changes.Count == 0) return null;
            var record = Record(key + ":" + Tick, category, subject, location, summary,
                "Observed state; changes do not prove causation.\n" + json);
            record.stateBefore = previous ?? ""; record.stateAfter = json;
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
