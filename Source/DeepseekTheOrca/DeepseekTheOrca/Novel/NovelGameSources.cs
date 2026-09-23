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
                if (c.Book.Seen(Id + ":" + key)) { yield return null; continue; }
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
        public string Id { get { return "logs"; } }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if (stateScan && !initial) yield break;
            var logs = new List<LogEntry>();
            if (Find.PlayLog != null) logs.AddRange(Find.PlayLog.AllEntries);
            if (Find.BattleLog != null)
                foreach (var battle in Find.BattleLog.Battles.ToList()) logs.AddRange(battle.Entries);
            foreach (var log in logs.Distinct())
            {
                string key = log.GetUniqueLoadID();
                if (c.Book.Seen(Id + ":" + key)) { yield return null; continue; }
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
                if (tale == null || tale.hidden || c.Book.Seen(Id + ":" + tale.GetUniqueLoadID())) { yield return null; continue; }
                // ShortSummary describes the recorded tale; do not call art text generation.
                string summary = tale.ShortSummary;
                yield return c.Record(tale.GetUniqueLoadID(), "tale", tale.DominantPawn == null ? "" : tale.DominantPawn.GetUniqueLoadID(),
                    "", summary, summary + "\nTaleDef: " + (tale.def == null ? "" : tale.def.defName), GenDate.TickAbsToGame(tale.date));
            }
        }
    }

    public sealed class NovelPawnSource : INovelSource
    {
        public string Id { get { return "pawns"; } }
        public IEnumerable<NovelRecord> Scan(NovelCaptureContext c, bool initial, bool stateScan)
        {
            if (!initial && !stateScan) yield break;
            var pawns = ObservablePawns(c.Book);
            var known = new HashSet<string>(c.Book.baselines.Keys.Where(k => k.StartsWith("pawns:profile:")).Select(k => k.Substring("pawns:profile:".Length)));
            var found = new HashSet<string>();
            foreach (var pawn in pawns)
            {
                string id = pawn.GetUniqueLoadID(); found.Add(id);
                foreach (var record in Isolate(c.Book, "pawns:entity:" + id, CapturePawn(c, pawn))) yield return record;
            }
            foreach (string absent in known.Where(id => !found.Contains(id)))
                yield return c.State("state:" + absent, "pawn_state", absent, "unknown", absent,
                    new Dictionary<string, object> { {"location", "not found in observable maps/caravans/world pawns; fate unknown"} });
        }

        public static List<Pawn> ObservablePawns(NovelBook book)
        {
            var pawns = new List<Pawn>();
            foreach (var map in Find.Maps.Where(m => m != null).ToList())
            {
                pawns.AddRange(map.mapPawns.AllPawns.Where(p => p != null && p.RaceProps != null && (p.RaceProps.Humanlike || p.Faction == Faction.OfPlayer)));
                pawns.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().Select(x => x.InnerPawn));
            }
            if (Find.WorldObjects != null)
                foreach (var caravan in Find.WorldObjects.Caravans.ToList())
                    if (caravan != null && caravan.Faction == Faction.OfPlayer) pawns.AddRange(caravan.PawnsListForReading);
            var known = new HashSet<string>(book.baselines.Keys.Where(k => k.StartsWith("pawns:profile:")).Select(k => k.Substring("pawns:profile:".Length)));
            if (Find.WorldPawns != null)
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                    if (pawn != null && known.Contains(pawn.GetUniqueLoadID())) pawns.Add(pawn);
            return pawns.Where(p => p != null).Distinct().ToList();
        }

        // Isolate a broken pawn as well as individual fields: it must never hide later pawns.
        public static IEnumerable<NovelRecord> Isolate(NovelBook book, string key, IEnumerable<NovelRecord> records)
        {
            using (var iterator = records.GetEnumerator())
            {
                while (true)
                {
                    bool moved = false, failed = false;
                    try { moved = iterator.MoveNext(); }
                    catch (Exception ex) { book.sourceErrors[key] = ex.ToString(); failed = true; }
                    if (failed) yield break;
                    if (!moved) { book.sourceErrors.Remove(key); yield break; }
                    yield return iterator.Current;
                }
            }
        }

        private IEnumerable<NovelRecord> CapturePawn(NovelCaptureContext c, Pawn pawn)
        {
            string id = pawn.GetUniqueLoadID();
            var profile = new NovelPawnFields(c.Book, id, "profile");
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
            var state = new NovelPawnFields(c.Book, id, "state");
            state.Read("name", () => name);
            state.Read("location", () => pawn.Map == null ? (pawn.GetCaravan() == null ? "off-map; location unknown" : pawn.GetCaravan().GetUniqueLoadID()) : pawn.Map.GetUniqueLoadID());
            string location = state.Values.ContainsKey("location") ? Convert.ToString(state.Values["location"]) : "unknown";
            yield return c.State("profile:" + id, "profile", id, location, name, profile.Values);
            state.Read("dead", () => pawn.Dead);
            state.Read("downed", () => pawn.Downed);
            state.Read("mentalState", () => pawn.MentalStateDef == null ? "" : pawn.MentalStateDef.label);
            state.Read("jobAtObservation", () => pawn.CurJobDef == null ? "" : pawn.CurJobDef.defName);
            state.Read("needs", () => PawnDetailsFormatter.NeedsSummary(pawn));
            state.Read("health", () => pawn.health == null || pawn.health.hediffSet == null ? "" : string.Join("; ", pawn.health.hediffSet.hediffs
                .Where(h => h != null && h.def != null).Select(h => h.Label + " [" + h.def.defName + "] " + (h.Part == null ? "" : h.Part.Label) + " severity=" + h.Severity.ToString("0.##")).ToArray()));
            state.Read("moodReasons", () => MoodReasons(pawn));
            yield return c.State("state:" + id, "pawn_state", id, location, name, state.Values);
            if (pawn.records != null)
            {
                var values = new NovelPawnFields(c.Book, id, "records");
                foreach (var def in DefDatabase<RecordDef>.AllDefsListForReading)
                {
                    if (def != null) values.Read(def.defName + " (" + def.label + ")", () => Math.Round(pawn.records.GetValue(def), 2));
                    yield return null;
                }
                yield return c.State("records:" + id, "daily_records", id, location, name + " cumulative records", values.Values);
            }
        }
        private static string MoodReasons(Pawn pawn)
        {
            if (pawn.Dead || pawn.needs == null || pawn.needs.mood == null || pawn.needs.mood.thoughts == null) return "";
            var thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            return string.Join("; ", thoughts.Where(t => t != null && t.def != null).Select(t => t.LabelCap.ToString()).ToArray());
        }
    }

    public sealed class NovelPawnFields
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        private readonly NovelBook book;
        private readonly string prefix;
        public NovelPawnFields(NovelBook book, string pawnId, string section)
        { this.book = book; prefix = "pawns:field:" + pawnId + ":" + section + ":"; }
        public void Read(string name, Func<object> read)
        {
            try { Values[name] = read(); book.sourceErrors.Remove(prefix + name); }
            catch (Exception ex)
            {
                // Diagnostics are never source text. Null means unavailable, not an empty/zero fact.
                Values[name] = null;
                book.sourceErrors[prefix + name] = ex.ToString();
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
                var s = ColonyDeepSnapshot.Capture(map);
                yield return c.State(id, "colony_state", id, id, map.Parent.LabelCap.ToString(), new Dictionary<string, object>
                {
                    {"tile", map.Tile.ToString()}, {"biome", map.Biome.label}, {"weather", map.weatherManager.curWeather.label},
                    {"temperature", Math.Round(map.mapTemperature.OutdoorTemp, 1)},
                    {"conditions", string.Join("; ", map.gameConditionManager.ActiveConditions.Select(x => x.Label).ToArray())},
                    {"colonists", s.colonists}, {"downed", s.downedColonists}, {"injured", s.injuredColonists},
                    {"foodNutrition", Math.Round(s.humanEdibleNutrition, 1)}, {"medicine", s.medicineCount}, {"wealth", Math.Round(s.playerWealth)},
                    {"mood", Math.Round(s.averageMood, 2)}
                });
                var buildings = map.listerBuildings.allBuildingsColonist.GroupBy(b => b.def).ToDictionary(g => g.Key.defName + " (" + g.Key.label + ")", g => (object)g.Count());
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
            List<RimtalkHistorySnapshot> rows; string error;
            if (!RimtalkIntegration.TryGetRecentHistorySnapshots(int.MaxValue, int.MaxValue, out rows, out error)) throw new InvalidOperationException(error);
            foreach (var row in rows)
            {
                if (!IsSpoken(row)) { yield return null; continue; }
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
