using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using DeepseekTheOrca.Rimtalk;

namespace DeepseekTheOrca
{
    public sealed class NovelLetterSource : INovelSource
    {
        public string Id { get { return "letters"; } }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if (stateScan && !initial) yield break;
            var letters = new List<Letter>();
            if (Find.Archive != null) letters.AddRange(Find.Archive.ArchivablesListForReading.OfType<Letter>());
            if (Find.LetterStack != null) letters.AddRange(Find.LetterStack.LettersListForReading);
            foreach (var letter in letters.Distinct())
            {
                string key = letter.GetUniqueLoadID();
                if (c.Seen(key)) { yield return null; continue; }
                var choice = letter as ChoiceLetter;
                var targets = letter.lookTargets == null ? new List<GlobalTargetInfo>() : letter.lookTargets.targets.ToList();
                string subject = string.Join(",", targets.Where(t => t.Thing != null).Select(t => t.Thing.GetUniqueLoadID()).ToArray());
                string location = string.Join(",", targets.Where(t => t.Map != null).Select(t => t.Map.GetUniqueLoadID()).Distinct().ToArray());
                string detail = "LetterDef: " + (letter.def == null ? "" : letter.def.defName) + "\n" + letter.Label.Resolve()
                    + "\n" + (choice == null ? "" : choice.Text.Resolve())
                    + "\nFaction: " + (letter.relatedFaction == null ? "" : letter.relatedFaction.Name)
                    + "\nQuest: " + (choice == null || choice.quest == null ? "" : choice.quest.name)
                    + "\nThis notification alone does not establish the eventual outcome.";
                yield return c.Record(key, "letter", subject, location, letter.Label.Resolve(), detail, letter.arrivalTick);
            }
        }
    }

    public sealed class NovelLogSource : INovelSource
    {
        private static IEnumerable<LogEntry> Entries()
        {
            if (Find.PlayLog != null)
                foreach (var log in Find.PlayLog.AllEntries.ToArray()) yield return log;
            if (Find.BattleLog != null)
                foreach (var battle in Find.BattleLog.Battles.ToArray())
                    foreach (var log in battle.Entries.ToArray()) yield return log;
        }
        public string Id { get { return "logs"; } }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if (stateScan && !initial) yield break;
            foreach (var log in Entries())
            {
                string key = log.GetUniqueLoadID();
                if (c.Seen(key)) { yield return null; continue; }
                var concerns = log.GetConcerns().Where(p => p != null).ToList();
                string text;
                try { text = log.ToGameStringFromPOV(concerns.FirstOrDefault()); }
                catch { text = log.def == null ? log.GetType().Name : log.def.LabelCap.ToString(); }
                yield return c.Record(key, "log", string.Join(",", concerns.Select(p => p.GetUniqueLoadID()).ToArray()),
                    string.Join(",", concerns.Where(p => p.Map != null).Select(p => p.Map.GetUniqueLoadID()).Distinct().ToArray()),
                    text, text, GenDate.TickAbsToGame(log.Tick));
            }
            if (Find.TaleManager == null) yield break;
            foreach (var tale in Find.TaleManager.AllTalesListForReading.ToList())
            {
                if (tale == null || tale.hidden || c.Seen(tale.GetUniqueLoadID())) { yield return null; continue; }
                // ShortSummary describes the recorded tale; do not call art text generation.
                string summary = tale.ShortSummary;
                yield return c.Record(tale.GetUniqueLoadID(), "tale", tale.DominantPawn == null ? "" : tale.DominantPawn.GetUniqueLoadID(),
                    "", summary, summary + "\nTaleDef: " + (tale.def == null ? "" : tale.def.defName), GenDate.TickAbsToGame(tale.date));
            }
        }
    }

    public sealed class NovelPawnSource : INovelSource, INovelSourceReadiness
    {
        public string Id { get { return "pawns"; } }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if (!initial && !stateScan) yield break;
            var pawns = ObservablePawnCandidates(c);
            var known = new HashSet<string>(c.BaselineKeys("profile:"));
            var found = new HashSet<string>();
            foreach (var pawn in pawns)
            {
                if (pawn == null) { yield return null; continue; }
                string id = pawn.GetUniqueLoadID();
                if (!found.Add(id)) { yield return null; continue; }
                foreach (var record in Isolate(c, "entity:" + id, CapturePawn(c, pawn))) yield return record;
            }
            foreach (string absent in known.Where(id => !found.Contains(id)))
                yield return c.State("state:" + absent, "pawn_state", absent, "unknown", absent,
                    new Dictionary<string, object> { {"location", "not found in observable maps/caravans/world pawns; fate unknown"} });
        }

        // Snapshot engine-owned collections before yielding, then inspect one entity per step.
        private static IEnumerable<Pawn> ObservablePawnCandidates(NovelCaptureContext c)
        {
            foreach (var map in Find.Maps.ToArray())
            {
                if (map == null) continue;
                foreach (var pawn in map.mapPawns.AllPawns.ToArray())
                    yield return pawn != null && pawn.RaceProps != null && (pawn.RaceProps.Humanlike || pawn.Faction == Faction.OfPlayer) ? pawn : null;
                foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToArray())
                    yield return (thing as Corpse)?.InnerPawn;
            }
            if (Find.WorldObjects != null)
                foreach (var caravan in Find.WorldObjects.Caravans.ToArray())
                    if (caravan != null && caravan.Faction == Faction.OfPlayer)
                        foreach (var pawn in caravan.PawnsListForReading.ToArray()) yield return pawn;
            var known = new HashSet<string>(c.BaselineKeys("profile:"));
            if (Find.WorldPawns != null)
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead.ToArray())
                    yield return pawn != null && known.Contains(pawn.GetUniqueLoadID()) ? pawn : null;
        }

        public IEnumerable<NovelRecord> PrepareForWriting(NovelCaptureContext c)
        {
            var checkedIds = new HashSet<string>();
            foreach (var pawn in ObservablePawnCandidates(c))
            {
                if (pawn == null || !checkedIds.Add(pawn.GetUniqueLoadID())) { yield return null; continue; }
                string id = pawn.GetUniqueLoadID();
                bool profile = c.HasRecord("profile", id);
                if (profile && c.Baseline("profile:" + id) != null && c.Baseline("state:" + id) != null)
                { yield return null; continue; }
                if (!profile) c.RemoveBaseline("profile:" + id);
                foreach (var record in Isolate(c, "entity:" + id, CapturePawn(c, pawn))) yield return record;
                if (!c.HasRecord("profile", id) || c.Baseline("profile:" + id) == null || c.Baseline("state:" + id) == null)
                    throw new InvalidOperationException("DTO_NovelPawnCacheMissing".Translate());
            }
            c.ClearDiagnostic("cache"); c.ClearDiagnostic("initial"); c.ClearDiagnostic("state");
        }

        // Isolate a broken pawn as well as individual fields: it must never hide later pawns.
        public static IEnumerable<NovelRecord> Isolate(NovelCaptureContext context, string key, IEnumerable<NovelRecord> records)
        {
            using (var iterator = records.GetEnumerator())
            {
                while (true)
                {
                    bool moved = false, failed = false;
                    try { moved = iterator.MoveNext(); }
                    catch (Exception ex) { context.Diagnostic(key, ex); failed = true; }
                    if (failed) yield break;
                    if (!moved) { context.ClearDiagnostic(key); yield break; }
                    yield return iterator.Current;
                }
            }
        }

        private IEnumerable<NovelRecord> CapturePawn(NovelCaptureContext c, Pawn pawn)
        {
            string id = pawn.GetUniqueLoadID();
            var profile = new NovelPawnFields(c, id, "profile");
            profile.Read("name", () => pawn.LabelShort);
            profile.Read("kind", () => pawn.KindLabel);
            profile.Read("race", () => pawn.def.label);
            profile.Read("gender", () => pawn.gender.ToString());
            profile.Read("age", () => pawn.ageTracker == null ? (object)null : pawn.ageTracker.AgeBiologicalYears);
            profile.Read("faction", () => pawn.Faction == null ? "" : pawn.Faction.Name);
            profile.Read("childhood", () => pawn.story == null || pawn.story.Childhood == null ? "" : pawn.story.Childhood.baseDesc);
            profile.Read("adulthood", () => pawn.story == null || pawn.story.Adulthood == null ? "" : pawn.story.Adulthood.baseDesc);
            profile.Read("traits", () => PawnDetailsFormatter.TraitsSummary(pawn));
            profile.Read("skills", () => PawnDetailsFormatter.SkillsSummary(pawn));
            profile.Read("relations", () => pawn.relations == null ? "" : string.Join("; ", pawn.relations.DirectRelations
                .Where(r => r != null && r.def != null && r.otherPawn != null)
                .Select(r => r.def.label + ": " + r.otherPawn.LabelShort + " [" + r.otherPawn.GetUniqueLoadID() + "]").ToArray()));
            profile.Read("equipment", () => pawn.equipment == null ? "" : string.Join("; ", pawn.equipment.AllEquipmentListForReading.Where(t => t != null).Select(t => t.LabelCap.ToString()).ToArray()));
            profile.Read("apparel", () => pawn.apparel == null ? "" : string.Join("; ", pawn.apparel.WornApparel.Where(t => t != null).Select(t => t.LabelCap.ToString()).ToArray()));
            profile.Read("rimtalkPersona", () => RimtalkIntegration.PawnPersonaSummary(pawn));
            string name = profile.Values.ContainsKey("name") ? Convert.ToString(profile.Values["name"]) : "Unnamed person";
            var state = new NovelPawnFields(c, id, "state");
            state.Read("name", () => name);
            state.Read("location", () => pawn.Map == null ? (pawn.GetCaravan() == null ? "off-map; location unknown" : pawn.GetCaravan().GetUniqueLoadID()) : pawn.Map.GetUniqueLoadID());
            string location = state.Values.ContainsKey("location") ? Convert.ToString(state.Values["location"]) : "unknown";
            yield return c.State("profile:" + id, "profile", id, location, name, profile.Values,
                new[] { "childhood", "adulthood", "skills", "rimtalkPersona", "equipment", "apparel" });
            state.Read("dead", () => pawn.Dead);
            state.Read("downed", () => pawn.Downed);
            state.Read("mentalState", () => pawn.MentalStateDef == null ? "" : pawn.MentalStateDef.label);
            state.Read("jobAtObservation", () => pawn.CurJobDef == null ? "" : pawn.CurJobDef.defName);
            state.Read("needs", () => PawnDetailsFormatter.NeedsSummary(pawn));
            state.Read("health", () => pawn.health == null || pawn.health.hediffSet == null ? "" : string.Join("; ", pawn.health.hediffSet.hediffs
                .Where(h => h != null && h.def != null).Select(h => h.Label + " [" + h.def.defName + "] " + (h.Part == null ? "" : h.Part.Label) + " severity=" + h.Severity.ToString("0.##")).ToArray()));
            state.Values["moodReasons"] = MoodReasons(c, pawn);
            yield return c.State("state:" + id, "pawn_state", id, location, name, state.Values);
            if (pawn.records != null)
            {
                var values = new NovelPawnFields(c, id, "records");
                foreach (var def in DefDatabase<RecordDef>.AllDefsListForReading)
                {
                    if (def != null) values.Read(def.defName + " (" + def.label + ")", () => Math.Round(pawn.records.GetValue(def), 2));
                    yield return null;
                }
                yield return c.State("records:" + id, "daily_records", id, location, name + " cumulative records", values.Values);
            }
        }
        private static object MoodReasons(NovelCaptureContext context, Pawn pawn)
        {
            string key = "field:" + pawn.GetUniqueLoadID() + ":state:moodReasons";
            if (pawn.Dead || pawn.needs == null || pawn.needs.mood == null || pawn.needs.mood.thoughts == null)
            { context.ClearDiagnostic(key); return null; }
            var errors = new List<Exception>();
            var thoughts = new List<Thought>();
            var labels = new List<string>();
            var handler = pawn.needs.mood.thoughts;
            string pawnInfo = "pawn=" + pawn.GetUniqueLoadID() + "; spawned=" + pawn.Spawned + "; dead=" + pawn.Dead;
            // Vanilla's combined method aborts on the first bad memory. Isolate each
            // entry and the situational source so healthy observations remain usable.
            try
            {
                if (handler.memories == null) throw new InvalidOperationException("Memory thought handler unavailable");
                foreach (var thought in handler.memories.Memories)
                {
                    try { ValidateThought(thought); if (thought.MoodOffset() != 0f) thoughts.Add(thought); }
                    catch (Exception ex) { errors.Add(new InvalidOperationException(pawnInfo + "; phase=memory_mood; " + ThoughtIdentity(thought), ex)); }
                }
            }
            catch (Exception ex) { errors.Add(new InvalidOperationException(pawnInfo + "; phase=collect_memories", ex)); }
            try
            {
                if (handler.situational == null) throw new InvalidOperationException("Situational thought handler unavailable");
                handler.situational.AppendMoodThoughts(thoughts);
            }
            catch (Exception ex) { errors.Add(new InvalidOperationException(pawnInfo + "; phase=collect_situational", ex)); }
            foreach (var thought in thoughts)
            {
                try { ValidateThought(thought); labels.Add(thought.LabelCap); }
                catch (Exception ex) { errors.Add(new InvalidOperationException(pawnInfo + "; phase=label; " + ThoughtIdentity(thought), ex)); }
            }
            if (errors.Count > 0) context.Diagnostic(key, new AggregateException("Mood collection partially unavailable", errors));
            else context.ClearDiagnostic(key);
            return new Dictionary<string, object> { { "observed", labels }, { "complete", errors.Count == 0 } };
        }

        private static string ThoughtIdentity(Thought thought)
        {
            return thought == null ? "thought=null" : "thought=" + (thought.def == null ? "null_def" : thought.def.defName)
                + "; type=" + thought.GetType().FullName + "; worker=" + (thought.def == null || thought.def.workerClass == null ? "none" : thought.def.workerClass.FullName)
                + "; pawnBound=" + (thought.pawn != null);
        }

        private static void ValidateThought(Thought thought)
        {
            if (thought == null || thought.def == null || thought.pawn == null)
                throw new InvalidOperationException("Thought, definition or bound pawn is null");
            int stage = thought.CurStageIndex;
            if (thought.def.stages == null || stage < 0 || stage >= thought.def.stages.Count || thought.def.stages[stage] == null)
                throw new InvalidOperationException("Invalid thought stage " + stage);
        }
    }

    public sealed class NovelPawnFields
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        private readonly NovelCaptureContext context;
        private readonly string prefix;
        public NovelPawnFields(NovelCaptureContext context, string pawnId, string section)
        { this.context = context; prefix = "field:" + pawnId + ":" + section + ":"; }
        public void Read(string name, Func<object> read)
        {
            try { Values[name] = read(); context.ClearDiagnostic(prefix + name); }
            catch (Exception ex)
            {
                // Diagnostics are never source text. Null means unavailable, not an empty/zero fact.
                Values[name] = null;
                context.Diagnostic(prefix + name, ex);
            }
        }
    }

    public sealed class NovelWorldSource : INovelSource
    {
        public string Id { get { return "world"; } }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if (!initial && !stateScan) yield break;
            foreach (var map in Find.Maps.ToList())
            {
                string id = map.GetUniqueLoadID();
                int colonists = 0, downed = 0, injured = 0, moodCount = 0;
                double moodSum = 0;
                foreach (var pawn in map.PlayerPawnsForStoryteller.ToArray())
                {
                    if (pawn != null && !pawn.Dead)
                    {
                        colonists++; if (pawn.Downed) downed++;
                        var health = pawn.health == null ? null : pawn.health.hediffSet;
                        if (health != null && (health.PainTotal > 0.08f || health.BleedRateTotal > 0.01f || health.hediffs.Any(h => h != null && h.TendableNow()))) injured++;
                        if (pawn.needs != null && pawn.needs.mood != null) { moodSum += pawn.needs.mood.CurLevel; moodCount++; }
                    }
                    yield return null;
                }
                int medicine = 0;
                foreach (string name in new[] { "MedicineHerbal", "MedicineIndustrial", "MedicineUltratech" })
                {
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (def != null && map.resourceCounter != null) medicine += map.resourceCounter.GetCount(def);
                    yield return null;
                }
                yield return c.State(id, "colony_state", id, id, map.Parent.LabelCap.ToString(), new Dictionary<string, object>
                {
                    {"tile", map.Tile.ToString()}, {"biome", map.Biome.label}, {"weather", map.weatherManager.curWeather.label},
                    {"temperature", Math.Round(map.mapTemperature.OutdoorTemp, 1)},
                    {"conditions", string.Join("; ", map.gameConditionManager.ActiveConditions.Select(x => x.Label).ToArray())},
                    {"colonists", colonists}, {"downed", downed}, {"injured", injured},
                    {"foodNutrition", map.resourceCounter == null ? -1d : Math.Round(map.resourceCounter.TotalHumanEdibleNutrition, 1)}, {"medicine", medicine}, {"wealth", Math.Round(map.PlayerWealthForStoryteller)},
                    {"mood", moodCount == 0 ? 0.5d : Math.Round(moodSum / moodCount, 2)}
                });
                var buildings = new Dictionary<string, object>();
                foreach (var building in map.listerBuildings.allBuildingsColonist.ToArray())
                {
                    string key = building.def.defName + " (" + building.def.label + ")";
                    object count; buildings.TryGetValue(key, out count);
                    buildings[key] = (count == null ? 0 : (int)count) + 1;
                    yield return null;
                }
                yield return c.State("buildings:" + id, "construction", id, id, "Observed buildings; changes do not identify builders", buildings);
            }
            foreach (var caravan in Find.WorldObjects.Caravans.ToList())
            {
                if (caravan.Faction != Faction.OfPlayer) continue;
                yield return c.State(caravan.GetUniqueLoadID(), "caravan", caravan.GetUniqueLoadID(), caravan.GetUniqueLoadID(), caravan.LabelCap.ToString(),
                    new Dictionary<string, object> { {"tile", caravan.Tile.ToString()}, {"moving", caravan.pather.Moving},
                        {"members", string.Join("; ", caravan.PawnsListForReading.Select(p => p.LabelShort + " [" + p.GetUniqueLoadID() + "]").ToArray())} });
            }
            var research = new Dictionary<string, object>();
            foreach (var def in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                float progress = Find.ResearchManager.GetProgress(def);
                if (progress > 0 || def.IsFinished) research[def.defName + " (" + def.label + ")"] = def.IsFinished ? "finished" : Math.Round(progress).ToString();
                yield return null;
            }
            yield return c.State("research", "research", "world", "", "Research progress", research);
            foreach (var quest in Find.QuestManager.QuestsListForReading.ToList())
                yield return c.State("quest:" + quest.id, "quest", "quest:" + quest.id, "", quest.name,
                    new Dictionary<string, object> { {"name", quest.name}, {"description", quest.description}, {"state", quest.State.ToString()} });
        }
    }

    public sealed class NovelRimtalkSource : INovelSource
    {
        public string Id { get { return "rimtalk"; } }
        public static bool IsSpoken(RimtalkHistorySnapshot r)
        { return r != null && r.entryKind == "ai_response" && !r.isError && r.state == "Spoken" && !string.IsNullOrWhiteSpace(r.response); }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if ((!initial && stateScan) || !RimtalkIntegration.IsAvailable) yield break;
            foreach (var row in RimtalkIntegration.EnumerateHistorySnapshots())
            {
                if (!IsSpoken(row) || c.Seen(row.identityKey)) { yield return null; continue; }
                yield return c.Record(row.identityKey, "dialogue", (row.pawnId ?? row.pawn) + "," + (row.recipientId ?? row.recipient), "", row.pawn + " -> " + row.recipient + ": " + row.interactionType,
                    row.pawn + ": " + row.response + "\nRecipient: " + row.recipient + "\nConversation: " + row.conversationId, row.spokenTick >= 0 ? row.spokenTick : row.finishedTick);
            }
        }
    }

    public sealed class NovelIncidentSource : INovelSource
    {
        public string Id { get { return "rimagent_incidents"; } }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if (stateScan && !initial) yield break;
            foreach (var r in OrcaNarrativeHistoryMemory.RecordsForNovel())
            {
                string map = r.targetMap == null ? "unknown" : r.targetMap.GetUniqueLoadID();
                string key = map + ":" + r.startTick + ":" + r.incidentDef;
                yield return c.Record(key + ":start", "incident", map, map, r.incidentDef + " occurred", "Executed incident: " + r.incidentDef, r.startTick);
                if (r.captured18000)
                    yield return c.Record(key + ":outcome", "incident_outcome", map, map, r.incidentDef + " observed aftermath: " + r.outcomeLabel,
                        "Observed after incident; not exclusive causation. Deaths among previously tracked colonists=" + r.deathDelta + "; downed change=" + r.downedDelta
                        + "; food change=" + r.foodDelta + "; medicine change=" + r.medicineDelta + "; mood change=" + r.moodDelta, r.startTick + 18000);
            }
        }
    }
}
